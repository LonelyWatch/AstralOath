#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using TMPro;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterPresentationQuitTests
    {
        private Scene scene;
        private CharacterLabBootstrap bootstrap;
        private FmodCharacterAudioService audio;
        private static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
        private static IEnumerator Until(Func<bool> ready,string message)
        {
            float deadline=Time.realtimeSinceStartup+60;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(ready(),Is.True,message);
        }
        [UnityTest] public IEnumerator ApplicationQuitReleasesOpenUiBeforeTmpParentsAreDestroyed()
        {
            scene=EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity",new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Until(()=>bootstrap=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CharacterLabBootstrap>(true)).SingleOrDefault(),"Bootstrap was not created.");
            yield return Until(()=>bootstrap.IsReady||!string.IsNullOrEmpty(bootstrap.StartupError),"Lab did not finish startup.");
            Assert.That(bootstrap.IsReady,Is.True,bootstrap.StartupError);
            audio=Field<FmodCharacterAudioService>(bootstrap,"audio");
            Assert.That(audio.IsAvailable,Is.True,audio.Diagnostic);
            Assert.That(Field<FMOD.System>(audio,"core").setOutput(FMOD.OUTPUTTYPE.NOSOUND),Is.EqualTo(FMOD.RESULT.OK));
            var owner=Field<BindingScope>(bootstrap,"lifetime");
            var entry=Field<object>(bootstrap,"entry");
            var binder=Field<object>(entry,"binder");
            var map=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<BindingMap>(true)).Single();
            map.Require<RectTransform>("voices").GetComponentsInChildren<Button>(false)[0].onClick.Invoke();
            var sounds=Field<List<ICharacterAudioPlayback>>(binder,"sounds");
            yield return Until(()=>sounds.Count==1,"Voice did not start.");
            var voice=sounds[0];
            var label=map.GetComponentInChildren<TextMeshProUGUI>(false);
            Assert.That(label.fontSharedMaterial.HasProperty(ShaderUtilities.ShaderTag_CullMode),Is.True);
            // This is the real TMP component whose OnDisable consults its parent's font material.
            var subMesh=TMP_SubMeshUI.AddSubTextObject(label,new MaterialReference(1,label.font,null,label.fontSharedMaterial,0));
            Assert.That(subMesh.gameObject.activeInHierarchy,Is.True);
            bootstrap.SendMessage("OnApplicationQuit",SendMessageOptions.DontRequireReceiver);
            Assert.That(bootstrap.IsReady,Is.False,"Application quit must shut down before OnDestroy.");
            Assert.That(owner.Token.IsCancellationRequested,Is.True);
            Assert.That(label,Is.Not.Null,"The parent must still exist while submesh OnDisable runs.");
            Assert.That(map.gameObject.activeInHierarchy,Is.False);
            Assert.That(subMesh.gameObject.activeInHierarchy,Is.False);
            Assert.That(Field<object>(entry,"binder"),Is.Null);
            Assert.That(sounds,Is.Empty);Assert.That(Field<bool>(voice,"disposed"),Is.True);
            Assert.That(audio.IsAvailable,Is.False);
            bootstrap.SendMessage("OnApplicationQuit",SendMessageOptions.DontRequireReceiver);
            UnityEngine.Object.Destroy(bootstrap.gameObject);
            yield return null;
            Assert.That(!map,Is.True);Assert.That(!bootstrap,Is.True);
            // Unity Test Framework still fails this test for any unexpected Error, Exception or Assert.
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
            Assert.That(audio==null||!audio.IsAvailable,Is.True,"Quit retained native audio.");
            scene=default;bootstrap=null;audio=null;
        }
    }
}
#endif
