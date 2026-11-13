using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Scripting;

namespace AstralOath.Client.BattleScene
{
    // Audio follows the rendered action. FMOD owns random voice selection and each event's internal timing.
    internal sealed class BattleVoice : IDisposable
    {
        private readonly Dictionary<string, SkillAudio> skills = new Dictionary<string, SkillAudio>(StringComparer.Ordinal);
        private const string SlimeSkillId = "preview.slime.s1";
        private const string BurnReadyEvent = "event:/battle/soulburn/ready";
        private const string ImpactBankAddress = "battlescene/source/sound/gerneal.bank";
        private readonly Dictionary<string, string> hitVoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> failedEvents = new HashSet<string>(StringComparer.Ordinal);
        private FmodCharacterAudioService service;

        public bool IsAvailable => service?.IsAvailable == true;
        public int StartedVoiceCount { get; private set; }
        public int StartedEffectCount { get; private set; }
        public int StartedHitCount { get; private set; }
        public int StartedBurnReadyCount { get; private set; }

        public BattleVoice(TextAsset audioCatalog, TextAsset[] banks, TextAsset impactBank = null,
            IEnumerable<string> activeHeroIds = null)
        {
            try
            {
                if (!audioCatalog)
                    throw new InvalidDataException("Missing character audio catalog.");
                var catalog = JsonUtility.FromJson<AudioCatalog>(audioCatalog.text);
                if (catalog?.characters == null || catalog.sharedBanks == null)
                    throw new InvalidDataException("Invalid character audio catalog.");
                foreach (var character in catalog.characters)
                {
                    foreach (var skill in character.skills ?? Array.Empty<SkillAudio>())
                        skills.Add(skill.id, skill);
                    foreach (var voice in character.voices ?? Array.Empty<CharacterVoice>())
                        if (voice.eventPath?.EndsWith("/ani/attacked", StringComparison.Ordinal) == true)
                            hitVoices[character.id] = voice.eventPath;
                }
                // The source slime STG uses the skill1 model animation, not a skill/m_slime event.
                skills.Add(SlimeSkillId, new SkillAudio
                {
                    id = SlimeSkillId,
                    eventPath = "event:/model/slime/ani/skill1"
                });

                var addresses = new List<string>(catalog.sharedBanks);
                var bankAssets = new List<TextAsset>(banks ?? Array.Empty<TextAsset>());
                const string sharedImpact = "characterlab/source/sound/gerneal.bank";
                // Older authored scenes carry the same bank in the dedicated impact field.
                if (impactBank && addresses.Count == bankAssets.Count + 1 && addresses[addresses.Count - 1] == sharedImpact)
                    bankAssets.Add(impactBank);
                if (addresses.Count != bankAssets.Count)
                    throw new InvalidDataException("Battle scene audio banks do not match the catalog.");
                if (impactBank && !addresses.Contains(sharedImpact))
                {
                    addresses.Add(ImpactBankAddress);
                    bankAssets.Add(impactBank);
                }
                var aliases = catalog.japaneseProgrammerAliases;
                if (activeHeroIds != null)
                {
                    var active = activeHeroIds.ToHashSet(StringComparer.Ordinal);
                    var allVoiceBanks = catalog.characters.Select(row => row.voiceBankAddress)
                        .Where(address => !string.IsNullOrEmpty(address)).ToHashSet(StringComparer.Ordinal);
                    var activeVoiceBanks = catalog.characters.Where(row => active.Contains(row.id))
                        .Select(row => row.voiceBankAddress).ToHashSet(StringComparer.Ordinal);
                    var activeVoiceEvents = catalog.characters.Where(row => active.Contains(row.id))
                        .SelectMany(row => row.skills ?? Array.Empty<SkillAudio>())
                        .SelectMany(skill => new[] { skill.voiceEventPath, skill.dualCallVoiceEventPath, skill.dualAnswerVoiceEventPath })
                        .ToHashSet(StringComparer.Ordinal);
                    aliases = aliases?.Where(alias => activeVoiceEvents.Contains(alias.eventPath)).ToArray();
                    for (int index = addresses.Count - 1; index >= 0; index--)
                    {
                        string address = addresses[index];
                        if (!allVoiceBanks.Contains(address) || activeVoiceBanks.Contains(address)) continue;
                        addresses.RemoveAt(index);
                        bankAssets.RemoveAt(index);
                    }
                }
                var assets = new SceneBanks(addresses.ToArray(), bankAssets.ToArray());
                service = new FmodCharacterAudioService(CancellationToken.None);
                // Scene banks are already loaded TextAssets; the asset adapter always completes inline.
                service.InitializeAsync(assets, addresses, catalog.missingJapaneseProgrammerKeys,
                    aliases).GetAwaiter().GetResult();
            }
            catch (Exception error)
            {
                Disable(error.Message);
            }
        }

        public void Play(string presentationId) => PlaySkill(presentationId, false, false);

        public void PlaySkill(string presentationId, bool callsAlly, bool answersAlly)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(presentationId)) return;
            if (!skills.TryGetValue(presentationId, out var cue))
            {
                WarnOnce(presentationId, "No skill audio mapping for " + presentationId);
                return;
            }

            // Keep the original effect timeline. FMOD chooses a take within one voice event.
            if (Start(cue.eventPath)) StartedEffectCount++;
            string contextualVoice = answersAlly ? cue.dualAnswerVoiceEventPath :
                callsAlly ? cue.dualCallVoiceEventPath : null;
            if (Start(contextualVoice) || Start(cue.voiceEventPath)) StartedVoiceCount++;
        }

        public void PlayBurnReady()
        {
            if (IsAvailable && Start(BurnReadyEvent)) StartedBurnReadyCount++;
        }

        // Called only when presenting accepted damage at its impact time, never by an animation callback.
        public void PlayHit(string targetDefinitionId, string sourceSkillId = null)
        {
            if (!IsAvailable) return;
            // m_slime_skill_01.stg HIT -> hit_new_blunt_normal.cfx -> this original FMOD effect.
            // Hero skill events already own their attack timelines, so do not add a generic impact to them.
            if (sourceSkillId == SlimeSkillId && Start("event:/effect/hit_new_blunt_normal"))
                StartedHitCount++;
            if (targetDefinitionId != null && hitVoices.TryGetValue(targetDefinitionId, out var path) && Start(path))
                StartedHitCount++;
        }

        public void SetPaused(bool value)
        {
            if (!IsAvailable) return;
            try { service.PauseAll(value); }
            catch (Exception error) { Disable(error.Message); }
        }

        public void Tick()
        {
            if (service == null) return;
            try
            {
                service.Tick();
                if (!service.IsAvailable) Disable(service.Diagnostic);
            }
            catch (Exception error)
            {
                Disable(error.Message);
            }
        }

        private bool Start(string eventPath)
        {
            if (string.IsNullOrWhiteSpace(eventPath) || failedEvents.Contains(eventPath)) return false;
            try
            {
                // The audio service keeps the native instance alive until it finishes or is disposed.
                service.PlayEvent(eventPath);
                return true;
            }
            catch (Exception error)
            {
                WarnOnce(eventPath, eventPath + " " + error.Message);
                return false;
            }
        }

        private void WarnOnce(string key, string diagnostic)
        {
            if (failedEvents.Add(key)) Debug.LogWarning("ASTRAL_BATTLE_AUDIO " + diagnostic);
        }

        private void Disable(string diagnostic)
        {
            Debug.LogWarning("ASTRAL_BATTLE_AUDIO_UNAVAILABLE " + diagnostic);
            Dispose();
        }

        public void Dispose()
        {
            var audio = service;
            service = null;
            audio?.Dispose();
        }

        private sealed class SceneBanks : IAssetService
        {
            private readonly Dictionary<string, TextAsset> banks = new Dictionary<string, TextAsset>(StringComparer.Ordinal);

            public SceneBanks(string[] addresses, TextAsset[] assets)
            {
                if (addresses.Length == 0 || assets == null || addresses.Length != assets.Length)
                    throw new InvalidDataException("Battle scene audio banks do not match the catalog.");
                for (int i = 0; i < addresses.Length; i++)
                {
                    var bank = assets[i];
                    string filename = Path.GetFileName(addresses[i]);
                    if (!bank || (bank.name != filename && bank.name != filename + ".bytes"))
                        throw new InvalidDataException("Missing or out-of-order battle scene audio bank " + addresses[i]);
                    banks.Add(addresses[i], bank);
                }
            }

            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            {
                scope.Token.ThrowIfCancellationRequested();
                if (banks.TryGetValue(address, out var asset) && asset is T value)
                    return Task.FromResult(value);
                throw new InvalidDataException("Missing battle scene audio bank " + address);
            }
        }

        [Serializable, Preserve]
        private sealed class AudioCatalog
        {
            public AudioCharacter[] characters;
            public string[] sharedBanks;
            public string[] missingJapaneseProgrammerKeys;
            public FmodCharacterAudioService.JapaneseProgrammerAlias[] japaneseProgrammerAliases;
        }

        [Serializable, Preserve]
        private sealed class AudioCharacter
        {
            public string id;
            public string voiceBankAddress;
            public SkillAudio[] skills;
            public CharacterVoice[] voices;
        }

        [Serializable, Preserve]
        private sealed class CharacterVoice
        {
            public string eventPath;
        }

        [Serializable, Preserve]
        private sealed class SkillAudio
        {
            public string id;
            public string eventPath;
            public string voiceEventPath;
            public string dualCallVoiceEventPath;
            public string dualAnswerVoiceEventPath;
        }
    }
}
