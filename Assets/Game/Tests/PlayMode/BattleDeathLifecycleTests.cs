#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object=UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class BattleDeathLifecycleTests
    {
        [UnityTest]
        public IEnumerator DisposeDetachesOwnedEffectsBeforeUnityDestroysThemAtFrameEnd()
        {
            var type=Type.GetType("AstralOath.Client.BattleScene.BattleDeathPresentation, AstralOath.Client.BattleScene",true);
            const string folder="Assets/Game/Modules/BattleScene/Res/Effects/Death/Particles/";
            var definition=AssetDatabase.LoadAssetAtPath<TextAsset>(folder+"death_pati_02.particle.json");
            var textures=new Dictionary<string,Texture2D>();
            foreach(string name in new[]{"soft_smoke","ticle_01","tri_pati_01","ticle_02","flash"})
                textures.Add(name,AssetDatabase.LoadAssetAtPath<Texture2D>(folder+name+".png"));
            var root=new GameObject("Death owner lifecycle",typeof(MeshRenderer));
            IDisposable old=null,next=null;
            try
            {
                old=(IDisposable)Activator.CreateInstance(type,root.transform,definition,textures,null,root.GetComponent<MeshRenderer>());
                type.GetMethod("Begin").Invoke(old,null);type.GetMethod("Tick").Invoke(old,new object[]{.3f});
                var effects=root.transform.Find("Battle death particles");Assert.That(effects,Is.Not.Null);
                old.Dispose();old=null;
                Assert.That(effects!=null,Is.True,"Destroy is deferred in a playing frame.");
                Assert.That(effects.parent,Is.Null,"Disposed effects must detach before another owner scans this actor.");
                Assert.That(effects.gameObject.activeSelf,Is.False);
                next=(IDisposable)Activator.CreateInstance(type,root.transform,definition,textures,null,root.GetComponent<MeshRenderer>());
                Assert.That(root.transform.childCount,Is.EqualTo(1),"Same-frame reconstruction must have one owned effect root.");
                yield return null;
                Assert.That(effects==null,Is.True);
            }
            finally {old?.Dispose();next?.Dispose();Object.Destroy(root);}
            yield return null;
        }
    }
}
#endif
