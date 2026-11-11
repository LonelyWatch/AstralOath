#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabIntegrationTests
    {
        private const string ScenePath = "Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity";
        private Scene scene;
        private CharacterLabBootstrap bootstrap;
        private FmodCharacterAudioService audio;
        private static T Field<T>(object owner, string name) => (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static void Fmod(FMOD.RESULT result, string operation) => Assert.That(result, Is.EqualTo(FMOD.RESULT.OK), operation);
        private static IEnumerator Until(Func<bool> condition, string message, float seconds = 60)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }
        private static void Click(BindingMap map, string key) => map.Require<GameButton>(key).GetComponent<Button>().onClick.Invoke();
        private static Button[] Rows(BindingMap map, string key) => map.Require<RectTransform>(key).GetComponentsInChildren<Button>(false);
        private static ISkeletonPlayer Player(object binder) => Field<ISkeletonPlayer>(binder, "player");
        private static void AssertRealMesh(ISkeletonPlayer player)
        {
            Assert.That(player, Is.Not.Null);
            Assert.That(player.Root.GetComponentsInChildren<MeshFilter>().Any(filter => filter.sharedMesh && filter.sharedMesh.vertexCount > 0), Is.True,
                "The real imported skeleton must produce mesh vertices.");
        }

        [UnityTest]
        public IEnumerator ActualSceneLoadsRendersPlaysVoiceSwitchesCharactersAndCloses()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Until(() => bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterLabBootstrap>(true)).SingleOrDefault(), "Bootstrap was not instantiated.");
            yield return Until(() => bootstrap.IsReady || !string.IsNullOrEmpty(bootstrap.StartupError), "CharacterLab did not finish startup in 60 seconds.");
            Assert.That(bootstrap.StartupError, Is.Null.Or.Empty);
            Assert.That(bootstrap.IsReady, Is.True);
            var entry = Field<object>(bootstrap, "entry");
            var binder = Field<object>(entry, "binder");
            var map = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>(true)).Single();
            Assert.That(Rows(map, "characters").Length, Is.EqualTo(3));
            Assert.That(Rows(map, "skills").Length, Is.EqualTo(3));
            Assert.That(Rows(map, "voices").Length, Is.GreaterThan(0));
            yield return null;
            AssertRealMesh(Player(binder));

            audio = Field<FmodCharacterAudioService>(bootstrap, "audio");
            Assert.That(audio.IsAvailable, Is.True, audio.Diagnostic);
            var core = Field<FMOD.System>(audio, "core");
            // This integration test verifies real decoded DSP samples without altering
            // system volume or producing loud output. Speaker audition is separate QA.
            Fmod(core.setOutput(FMOD.OUTPUTTYPE.NOSOUND), "Select silent native output");
            Fmod(core.getMasterChannelGroup(out var master), "Master group");
            Fmod(master.getDSP(0, out var meter), "Master DSP");
            Fmod(meter.setMeteringEnabled(false, true), "Enable metering");
            Rows(map, "voices")[0].onClick.Invoke();
            var sounds = Field<List<ICharacterAudioPlayback>>(binder, "sounds");
            yield return Until(() => sounds.Count == 1, "Voice UI did not create an audio playback.", 10);
            var voice = sounds[0];
            float peak = 0, deadline = Time.realtimeSinceStartup + 3;
            while (Time.realtimeSinceStartup < deadline)
            {
                Fmod(meter.getMeteringInfo(IntPtr.Zero, out var output), "Read native DSP samples");
                for (int i = 0; i < output.numchannels; i++) peak = Mathf.Max(peak, output.peaklevel[i]);
                yield return null;
            }
            Assert.That(peak, Is.GreaterThan(.00001f), "Voice UI loaded an event but generated no samples.");
            Click(map, "stop");
            yield return null;
            Assert.That(sounds, Is.Empty);
            Assert.That(Field<bool>(voice, "disposed"), Is.True);

            foreach (int index in new[] { 1, 2, 0, 2, 0 })
            {
                var oldRoot = Player(binder).Root;
                Rows(map, "characters")[index].onClick.Invoke();
                yield return Until(() => Player(binder) != null && Player(binder).Root != oldRoot, "Character selection did not replace the player.", 30);
                yield return null;
                AssertRealMesh(Player(binder));
                Assert.That(!oldRoot, Is.True, "Previous character root survived selection disposal.");
            }
            var battleRoot = Player(binder).Root;
            Click(map, "portrait");
            yield return Until(() => Player(binder) != null && Player(binder).Root != battleRoot && Player(binder).Version.StartsWith("3.8"), "Ras portrait did not activate the 3.8 player.", 30);
            AssertRealMesh(Player(binder));
            Click(map, "close");
            yield return Until(() => !map || !map.gameObject.activeInHierarchy, "Close UI did not release the window.", 10);
            Assert.That(Field<bool>(binder, "disposed"), Is.True);
            Assert.That(Field<object>(entry, "binder"), Is.Null);
            yield return null;
            Assert.That(scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MeshFilter>()).Any(filter => filter.sharedMesh && filter.sharedMesh.vertexCount > 0), Is.False,
                "Closing the lab left a skeleton/effect mesh alive.");
            Assert.That(bootstrap.StartupError, Is.Null.Or.Empty);
        }

        [UnityTearDown]
        public IEnumerator UnloadOwnedScene()
        {
            // Test Runner also invokes this after an assertion or unexpected log aborts
            // the test iterator. A finally block cannot yield until unloading finishes.
            var ownedScene = scene;
            var ownedBootstrap = bootstrap;
            var ownedAudio = audio;
            if (ownedAudio == null && ownedBootstrap)
                ownedAudio = Field<FmodCharacterAudioService>(ownedBootstrap, "audio");
            if (ownedScene.IsValid() && ownedScene.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(ownedScene);
                if (unload != null) yield return unload;
            }
            // Allow deferred destruction and cancellation continuations to finish.
            yield return null;
            scene = default;
            bootstrap = null;
            audio = null;
            Assert.That(!ownedScene.IsValid() || !ownedScene.isLoaded, Is.True, "Scene unload did not complete.");
            Assert.That(!ownedBootstrap, Is.True, "Scene unload retained its Bootstrap owner.");
            Assert.That(ownedAudio == null || !ownedAudio.IsAvailable, Is.True, "Scene unload retained the native audio service.");
        }
    }
}
#endif
