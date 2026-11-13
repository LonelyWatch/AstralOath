#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    // An audio-only baseline: it does not launch the scene, prewarm voices, seek events,
    // choose random variants, or infer an end-to-end rendered battle frame budget.
    public sealed class FmodColdWarmPerformanceTests
    {
        private const string ScenePath = "Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity";
        private FmodCharacterAudioService service;
        private readonly List<ICharacterAudioPlayback> playing = new List<ICharacterAudioPlayback>();

        [UnityTest]
        public IEnumerator NineSkillsPlayOriginalEventsThreeTimesAndReportNativeCosts()
        {
            // Read the authored scene's dependency closure without creating BattleHudPreview.
            // Loading the full scene would already create another FMOD system and warm samples.
            var dependencies = AssetDatabase.GetDependencies(ScenePath, true);
            var catalogPath = dependencies.Single(path => path.EndsWith("/CharacterLab/Configs/catalog.json", StringComparison.Ordinal));
            var catalogAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(catalogPath);
            Assert.That(catalogAsset, Is.Not.Null);
            var catalog = JsonUtility.FromJson<AudioCatalog>(catalogAsset.text);
            Assert.That(catalog?.sharedBanks, Is.Not.Null.And.Not.Empty);
            var banks = new Dictionary<string, TextAsset>(StringComparer.Ordinal);
            foreach (string address in catalog.sharedBanks)
            {
                string filename = Path.GetFileName(address) + ".bytes";
                string path = dependencies.Single(item => Path.GetFileName(item) == filename);
                banks.Add(address, AssetDatabase.LoadAssetAtPath<TextAsset>(path));
                Assert.That(banks[address], Is.Not.Null, address);
            }
            string impactPath = dependencies.SingleOrDefault(item => Path.GetFileName(item) == "gerneal.bank.bytes");
            if (impactPath != null)
                banks.Add("battlescene/source/sound/gerneal.bank", AssetDatabase.LoadAssetAtPath<TextAsset>(impactPath));
            var skills = catalog.characters.Where(character =>
                    character.id == "c1001" || character.id == "c1005" || character.id == "c1062")
                .SelectMany(character => character.skills).ToArray();
            Assert.That(skills.Length, Is.EqualTo(9));
            Assert.That(skills.Select(skill => skill.id).Distinct().Count(), Is.EqualTo(9));
            service = new FmodCharacterAudioService(CancellationToken.None) { CapturePerformanceDiagnostics = true };
            var watch = Stopwatch.StartNew();
            var initialization = service.InitializeAsync(new SceneBanks(banks), banks.Keys);
            double loadDeadline = Time.realtimeSinceStartupAsDouble + 30d;
            while (!initialization.IsCompleted)
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(loadDeadline), "FMOD initialization exceeded 30 seconds.");
                yield return null;
            }
            initialization.GetAwaiter().GetResult();
            watch.Stop();
            Assert.That(service.IsAvailable, Is.True, service.Diagnostic);
            var reports = new List<string> { $"PERF FMOD InitializeMs={watch.Elapsed.TotalMilliseconds:F3} Banks={banks.Count}" };
            int totalCallbacks = 0;
            foreach (var skill in skills)
            for (int pass = 0; pass < 3; pass++)
            {
                yield return null;
                service.ResetPerformanceDiagnostics();
                int collections = GC.CollectionCount(0);
                double framePeak = 0;
                int frames = 0;
                var startTimes = new double[2];
                var paths = new[] { skill.eventPath, skill.voiceEventPath };
                for (int index = 0; index < paths.Length; index++)
                {
                    Assert.That(paths[index], Is.Not.Null.And.Not.Empty, skill.id);
                    watch.Restart();
                    playing.Add(service.PlayEvent(paths[index]));
                    watch.Stop();
                    startTimes[index] = watch.Elapsed.TotalMilliseconds;
                }
                double deadline = Time.realtimeSinceStartupAsDouble + 30d;
                do
                {
                    yield return null;
                    framePeak = Math.Max(framePeak, Time.unscaledDeltaTime * 1000d);
                    frames++;
                    service.Tick();
                    Assert.That(service.IsAvailable, Is.True, service.Diagnostic);
                    Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline), skill.id + " native events did not finish within 30 seconds.");
                } while (service.DiagnosticActivePlaybackCount != 0);

                int callbacks = service.ProgrammerCreateSoundCount;
                totalCallbacks += callbacks;
                Assert.That(callbacks, Is.GreaterThan(0), skill.id + " must execute the source event's actual randomized programmer sound.");
                string selectedKeys = string.Join(",", service.DiagnosticSoundKeys.OrderBy(key => key, StringComparer.Ordinal));
                reports.Add($"PERF FMOD {skill.id} Pass={pass} EffectEventMs={startTimes[0]:F3} VoiceEventMs={startTimes[1]:F3} " +
                    $"PlayEventPeakMs={service.PeakPlayEventMilliseconds:F3} UpdatePeakMs={service.PeakUpdateMilliseconds:F3} " +
                    $"TickPeakMs={service.PeakTickMilliseconds:F3} CreateSoundPeakMs={service.PeakCreateSoundMilliseconds:F3} " +
                    $"CallbackPeakMs={service.PeakProgrammerCallbackMilliseconds:F3} CallbackCount={callbacks} Keys={selectedKeys} " +
                    $"FrameMaxMs={framePeak:F3} Frames={frames} GC0Delta={GC.CollectionCount(0) - collections}");
                StopOwnedPlaybacks();
                service.Tick();
                Assert.That(service.DiagnosticActivePlaybackCount, Is.Zero);
            }
            Assert.That(totalCallbacks, Is.GreaterThanOrEqualTo(27));
            // Keep Console work outside all measurement windows.
            foreach (string report in reports) UnityEngine.Debug.Log(report);
        }

        private void StopOwnedPlaybacks()
        {
            foreach (var playback in playing) { playback.Stop(); playback.Dispose(); }
            playing.Clear();
        }

        [TearDown]
        public void Cleanup()
        {
            StopOwnedPlaybacks();
            service?.Dispose();
            service = null;
        }

        private sealed class SceneBanks : IAssetService
        {
            private readonly IReadOnlyDictionary<string, TextAsset> banks;
            public SceneBanks(IReadOnlyDictionary<string, TextAsset> banks) => this.banks = banks;
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                scope.Token.ThrowIfCancellationRequested();
                if (banks.TryGetValue(address, out var bank) && bank is T asset) return Task.FromResult(asset);
                throw new InvalidDataException("Missing scene audio bank " + address);
            }
        }
        [Serializable] private sealed class AudioCatalog { public AudioCharacter[] characters; public string[] sharedBanks; }
        [Serializable] private sealed class AudioCharacter { public string id; public AudioSkill[] skills; }
        [Serializable] private sealed class AudioSkill { public string id; public string eventPath; public string voiceEventPath; }
    }
}
#endif
