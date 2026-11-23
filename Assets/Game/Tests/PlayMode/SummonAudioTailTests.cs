#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Summon;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class SummonAudioTailTests
    {
        [UnityTest]
        public IEnumerator ClosingThePageFadesThePlayingSourceWithoutChangingTheReopenedTheme()
        {
            var page = new GameObject("Audio test summon page", typeof(RectTransform), typeof(RawImage));
            var effects = page.AddComponent<AudioSource>();
            var music = page.AddComponent<AudioSource>();
            music.volume = .64f;
            var clip = AudioClip.Create("summon tail test tone", 96000, 1, 48000, false);
            var tone = new float[clip.samples];
            for (int i = 0; i < tone.Length; i++) tone[i] = .04f * Mathf.Sin(2f * Mathf.PI * 440f * i / clip.frequency);
            clip.SetData(tone, 0);
            var assets = ScriptableObject.CreateInstance<SummonPresentation>();
            assets.Configure(Array.Empty<SummonPresentation.Phase>(),
                new[] { new SummonPresentation.Cue { id = "Theme", clip = clip } }, Shader.Find("UI/Default"));
            var old = assets.CreatePlayer(page.GetComponent<RawImage>(), effects);
            float originalScale = Time.timeScale;
            var nextPage = new GameObject("Reopened summon page", typeof(RectTransform), typeof(RawImage));
            var next = assets.CreatePlayer(nextPage.GetComponent<RawImage>(), nextPage.AddComponent<AudioSource>());
            try
            {
                old.PlayTheme(music, false);
                var first = Resources.FindObjectsOfTypeAll<AudioSource>().Single(source => source.clip == clip && source.isPlaying);
                Assert.That(first.volume, Is.EqualTo(.64f).Within(.001f));
                Time.timeScale = 0f;
                old.Dispose();
                Object.DestroyImmediate(page);
                Assert.That(first && first.isPlaying, Is.True, "The same source must outlive the closed page.");
                var nextMusic = nextPage.AddComponent<AudioSource>();
                nextMusic.volume = .38f;
                next.PlayTheme(nextMusic, false);
                var second = Resources.FindObjectsOfTypeAll<AudioSource>().Single(source => source != first && source.clip == clip && source.isPlaying);
                double start = Time.realtimeSinceStartupAsDouble;
                yield return new WaitForSecondsRealtime(.065f);
                Assert.That(first && first.isPlaying, Is.True);
                Assert.That(first.volume, Is.InRange(.015f, .60f));
                Assert.That(second.volume, Is.EqualTo(.38f).Within(.001f));
                TestContext.WriteLine($"At {(Time.realtimeSinceStartupAsDouble - start) * 1000:F0} ms: outgoing gain {first.volume:F3}, incoming gain {second.volume:F3}");
                while (first && Time.realtimeSinceStartupAsDouble - start < .55d) yield return null;
                Assert.That(!first, Is.True, "The tail must release its source at the end of the fade.");
                TestContext.WriteLine($"Released after {(Time.realtimeSinceStartupAsDouble - start) * 1000:F0} ms.");
                Assert.That(second && second.isPlaying, Is.True);
                Assert.That(second.volume, Is.EqualTo(.38f).Within(.001f));
            }
            finally
            {
                Time.timeScale = originalScale;
                next.Dispose();
                old.Dispose();
                if (page) Object.DestroyImmediate(page);
                Object.DestroyImmediate(nextPage);
                Object.DestroyImmediate(assets);
                Object.DestroyImmediate(clip);
            }
        }

        [UnityTest]
        public IEnumerator AFeaturedPreviewResumesAtTheSampleWhereThePageWasHidden()
        {
            var asset = AssetDatabase.LoadAssetAtPath<SummonSkillLoop>(
                "Assets/Game/Modules/Summon/Res/Presentation/IseriaSkillLoop/IseriaSkillLoop.asset");
            var clip = (AudioClip)new SerializedObject(asset).FindProperty("originalAudio").objectReferenceValue;
            var page = new GameObject("Featured preview", typeof(RectTransform), typeof(RawImage), typeof(CanvasGroup), typeof(AudioSource));
            var settings = page.GetComponent<AudioSource>();
            var group = page.GetComponent<CanvasGroup>();
            var scope = new BindingScope(CancellationToken.None);
            var player = asset.CreatePlayer(page.GetComponent<RawImage>(), settings, scope, group, group, group);
            try
            {
                player.SetState(true, false);
                const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(SummonSkillLoop.Player).GetField("elapsed", instance).SetValue(player, 3.7d);
                typeof(SummonSkillLoop.Player).GetMethod("Tick", instance).Invoke(player, null);
                AudioSource[] Playing() => Resources.FindObjectsOfTypeAll<AudioSource>().Where(s => s.isPlaying && s.clip == clip).ToArray();
                var original = Playing().Single();
                original.timeSamples = clip.frequency;
                player.SetState(true, true);
                Assert.That(original.isPlaying, Is.True);
                player.SetState(true, false);
                var resumed = Playing().Single(source => source != original);
                Assert.That(resumed.timeSamples, Is.InRange(clip.frequency - 1024, clip.frequency + 4096));
                scope.Dispose();
                Object.DestroyImmediate(page);
                Assert.That(resumed && resumed.isPlaying, Is.True);
                yield return new WaitForSecondsRealtime(.28f);
                yield return null;
                Assert.That(!resumed && !original, Is.True);
            }
            finally
            {
                scope.Dispose();
                if (page) Object.DestroyImmediate(page);
            }
        }

        [UnityTest]
        public IEnumerator SkippingTheDialogueFadesOnlyItsVoiceAndClosingFadesBothCardCues()
        {
            var assets = AssetDatabase.LoadAssetAtPath<SummonPresentation>(
                "Assets/Game/Modules/Summon/Res/Presentation/SummonPresentation.asset");
            var page = new GameObject("Summon card audio", typeof(RectTransform), typeof(RawImage), typeof(AudioSource));
            using var player = assets.CreatePlayer(page.GetComponent<RawImage>(), page.GetComponent<AudioSource>());
            try
            {
                player.PlayCardFlip(false, true);
                player.PlayHeroVoice("c1004");
                AudioSource Playing(string name) => Resources.FindObjectsOfTypeAll<UiAudioPlayback>()
                    .Select(item => item.GetComponent<AudioSource>()).Single(item => item.isPlaying && item.clip.name == name);
                var flip = Playing("FlipCommon");
                var badge = Playing("New");
                var voice = Playing("Voice_c1004");
                player.StopHeroVoice();
                yield return new WaitForSecondsRealtime(.07f);
                Assert.That(voice.volume, Is.LessThan(.95f));
                Assert.That(flip.volume, Is.EqualTo(1f));
                Assert.That(badge.volume, Is.EqualTo(1f));
                player.Stop();
                Object.DestroyImmediate(page);
                Assert.That(flip.isPlaying && badge.isPlaying, Is.True);
                yield return new WaitForSecondsRealtime(.28f);
                yield return null;
                Assert.That(!flip && !badge && !voice, Is.True);
            }
            finally { if (page) Object.DestroyImmediate(page); }
        }

        [UnityTest]
        public IEnumerator ACompletedOrMissingClipDoesNotWaitForAFade()
        {
            var page = new GameObject("Short UI cue", typeof(AudioSource));
            var clip = AudioClip.Create("Short natural UI cue", 4800, 1, 48000, false);
            var source = page.GetComponent<AudioSource>();
            try
            {
                Assert.That(UiAudioPlayback.Play(source, null), Is.Null);
                var playback = UiAudioPlayback.Play(source, clip);
                yield return new WaitForSecondsRealtime(.16f);
                yield return null;
                Assert.That(!playback, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(page);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
#endif
