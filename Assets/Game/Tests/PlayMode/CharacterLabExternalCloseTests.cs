#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabExternalCloseTests
    {
        private const string ScenePath = "Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity";
        private Scene scene;
        private CharacterLabBootstrap bootstrap;
        private FmodCharacterAudioService audio;
        private static T Field<T>(object owner, string name) => (T)owner.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        private static IEnumerator Until(Func<bool> ready, string message, float seconds = 60)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(ready(), Is.True, message);
        }

        [UnityTest]
        public IEnumerator DestroyingSceneObjectsBeforeBootstrapReleasesOpenWindowAndVoice()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Until(() => bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<CharacterLabBootstrap>(true)).SingleOrDefault(), "Bootstrap was not instantiated.");
            yield return Until(() => bootstrap.IsReady || !string.IsNullOrEmpty(bootstrap.StartupError), "CharacterLab did not finish startup.");
            Assert.That(bootstrap.StartupError, Is.Null.Or.Empty);
            Assert.That(bootstrap.IsReady, Is.True);
            audio = Field<FmodCharacterAudioService>(bootstrap, "audio");
            Assert.That(audio.IsAvailable, Is.True, audio.Diagnostic);
            var core = Field<FMOD.System>(audio, "core");
            Assert.That(core.setOutput(FMOD.OUTPUTTYPE.NOSOUND), Is.EqualTo(FMOD.RESULT.OK));
            var entry = Field<object>(bootstrap, "entry");
            var binder = Field<object>(entry, "binder");
            var map = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<BindingMap>(true)).Single();
            map.Require<RectTransform>("voices").GetComponentsInChildren<Button>(false)[0].onClick.Invoke();
            var sounds = Field<List<ICharacterAudioPlayback>>(binder, "sounds");
            yield return Until(() => sounds.Count == 1, "Voice UI did not create a playback.", 10);
            var voice = sounds[0];

            // Application shutdown does not promise that the Bootstrap outlives its
            // scene renderers, camera or UI. Fix that ordering here without Quit().
            bootstrap.enabled = false;
            UnityEngine.Object.Destroy(Field<Transform>(bootstrap, "worldRoot").gameObject);
            UnityEngine.Object.Destroy(Field<Camera>(bootstrap, "previewCamera").gameObject);
            UnityEngine.Object.Destroy(map.gameObject);
            yield return null;
            Assert.That(!map, Is.True);
            UnityEngine.Object.Destroy(bootstrap.gameObject);
            yield return Until(() => !bootstrap, "Bootstrap was not destroyed.", 10);
            yield return null;
            Assert.That(Field<bool>(binder, "disposed"), Is.True);
            Assert.That(Field<object>(entry, "binder"), Is.Null);
            Assert.That(sounds, Is.Empty);
            Assert.That(Field<bool>(voice, "disposed"), Is.True);
            Assert.That(audio.IsAvailable, Is.False, "External close retained native audio.");
        }

        [UnityTearDown]
        public IEnumerator UnloadOwnedScene()
        {
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
            yield return null;
            scene = default;
            bootstrap = null;
            audio = null;
            Assert.That(!ownedScene.IsValid() || !ownedScene.isLoaded, Is.True, "Owned scene did not unload.");
            Assert.That(!ownedBootstrap, Is.True, "Owned Bootstrap survived scene unload.");
            Assert.That(ownedAudio == null || !ownedAudio.IsAvailable, Is.True, "Owned audio survived scene unload.");
        }
    }
}
#endif
