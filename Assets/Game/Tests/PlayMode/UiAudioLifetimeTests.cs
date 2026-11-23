using System.Collections;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
#if UNITY_EDITOR
using System;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Core;
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class UiAudioLifetimeTests
    {
        internal static AudioSource[] Playing(AudioClip clip) => Resources.FindObjectsOfTypeAll<UiAudioPlayback>()
            .Select(item => item.GetComponent<AudioSource>()).Where(source => source.isPlaying && source.clip == clip).ToArray();

#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator PackagedClipSurvivesOwnerReleaseUntilItsTailEnds()
        {
            // A prepared Foundation package also exercises native bundle unloading in OfflinePlayMode.
            string offlineRoot = Environment.GetEnvironmentVariable("ASTRAL_UI_AUDIO_OFFLINE_ROOT");
            using var service = new YooAssetService();
            using var assets = new BindingScope(default);
            var initialize = service.InitializeAsync(assets.Token, offlineRoot);
            yield return new WaitUntil(() => initialize.IsCompleted);
            initialize.GetAwaiter().GetResult();
            var load = service.LoadAsync<BattleOnlineTransitionAssets>(BattleOnlineTransitionAssets.Address, assets);
            yield return new WaitUntil(() => load.IsCompleted);
            var transition = load.GetAwaiter().GetResult();
            var clip = (AudioClip)new SerializedObject(transition).FindProperty("resultVictory").objectReferenceValue;

            var page = new GameObject("Results window", typeof(AudioSource));
            try
            {
                var cue = UiAudioPlayback.Play(page.GetComponent<AudioSource>(), clip);
                var output = cue.GetComponent<AudioSource>();
                assets.Dispose();
                // Let the released parent bundle unload while the independent voice is still playing.
                if (offlineRoot != null)
                {
                    double deadline = Time.realtimeSinceStartupAsDouble + 2;
                    while (transition && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                    Assert.That(!transition, Is.True, "The result presentation's native bundle should already be gone.");
                }
                Assert.That(output.isPlaying && output.clip == clip && clip, Is.True);
                cue.FadeOut();
                Object.DestroyImmediate(page);
                yield return new WaitForSecondsRealtime(.065f);
                Assert.That(output && output.isPlaying && clip && output.volume > 0f && output.volume < 1f, Is.True);
                yield return new WaitForSecondsRealtime(.2f);
                yield return null;
                Assert.That(!output, Is.True);
                if (offlineRoot != null)
                {
                    double deadline = Time.realtimeSinceStartupAsDouble + 2;
                    while (clip && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                    Assert.That(!clip, Is.True, "The native AudioClip should unload when its last voice releases it.");
                }
            }
            finally { if (page) Object.DestroyImmediate(page); }
        }

        [UnityTest]
        public IEnumerator DestroyingTheCanvasFadesOverlappingClicksWithoutLoweringTheNextCanvas() =>
            CheckClicks(true);

        [UnityTest]
        public IEnumerator DisablingAStandaloneButtonFadesOnlyItsOwnClicks() => CheckClicks(false);

        private static IEnumerator CheckClicks(bool canvas)
        {
            var clip = AudioClip.Create("Overlapping UI clicks", 96000, 1, 48000, false);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/ConfirmButton.prefab");
            GameObject Owner() => canvas ? new GameObject("Click window", typeof(Canvas)) : new GameObject("No canvas");
            GameButton Button(GameObject owner, float volume)
            {
                var button = Object.Instantiate(prefab, owner.transform).GetComponentInChildren<GameButton>();
                var settings = button.GetComponent<AudioSource>();
                settings.clip = clip;
                settings.volume = volume;
                return button;
            }
            var first = Owner();
            var next = Owner();
            try
            {
                var previousButton = Button(first, .61f);
                previousButton.onClick.Invoke();
                previousButton.onClick.Invoke();
                var previous = Playing(clip);
                Assert.That(previous.Length, Is.EqualTo(2), "Each click can overlap its predecessor.");
                if (canvas) Object.DestroyImmediate(first);
                else previousButton.enabled = false;
                Button(next, .38f).onClick.Invoke();
                var incoming = Playing(clip).Except(previous).Single();
                Assert.That(previous.All(s => s && s.isPlaying), Is.True);
                yield return new WaitForSecondsRealtime(.075f);
                Assert.That(previous.All(s => s && s.isPlaying && s.volume > .01f && s.volume < .60f), Is.True);
                Assert.That(incoming.volume, Is.EqualTo(.38f).Within(.001f));
                yield return new WaitForSecondsRealtime(.19f);
                yield return null;
                Assert.That(previous.All(s => !s), Is.True);
                Assert.That(incoming && incoming.isPlaying, Is.True);
                Assert.That(incoming.volume, Is.EqualTo(.38f).Within(.001f));
            }
            finally
            {
                if (first) Object.DestroyImmediate(first);
                Object.DestroyImmediate(next);
                Object.DestroyImmediate(clip);
            }
        }
#endif
    }
}
