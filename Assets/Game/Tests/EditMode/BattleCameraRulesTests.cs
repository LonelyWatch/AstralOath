using System;
using System.Collections.Generic;
using System.Reflection;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
namespace AstralOath.Tests
{
    public sealed class BattleCameraRulesTests
    {
        private GameObject root;
        private Camera camera;
        private object stage;
        private Sprite sprite;
        private Texture2D texture;
        [TearDown] public void Cleanup()
        {
            (stage as IDisposable)?.Dispose();
            if (root) Object.DestroyImmediate(root);
            if (sprite) Object.DestroyImmediate(sprite);
            if (texture) Object.DestroyImmediate(texture);
        }
        private Transform Create(bool map)
        {
            root = new GameObject("Camera rules");
            camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform); camera.transform.position = new Vector3(0,0,-10);
            camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 16f/9f;
            var target = new GameObject("Target").transform; target.SetParent(root.transform); target.position = new Vector3(4,-3,0);
            GameObject background = null;
            if (map)
            {
                background = new GameObject("Map", typeof(SpriteRenderer)); background.transform.SetParent(root.transform);
                texture = new Texture2D(2,2); texture.SetPixels(new[]{Color.white,Color.white,Color.white,Color.white}); texture.Apply();
                sprite = Sprite.Create(texture, new Rect(0,0,2,2), Vector2.one*.5f, 1f);
                background.GetComponent<SpriteRenderer>().sprite = sprite; background.transform.localScale = new Vector3(15,10,1);
            }
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleCinematicStage, AstralOath.Client.BattleScene", true);
            stage = Activator.CreateInstance(type, camera, (Action<bool>)(value=>{}), background);
            Call("Begin", target, new[]{target}, false); return target;
        }
        [Test] public void FocusingKeepsTheTargetsOriginalGroundFramingWhileZooming()
        {
            var target=Create(false); float y=camera.WorldToViewportPoint(target.position).y;
            Call("Play", JObject.Parse("{'etty':'CAM','location':12,'time':200}"));
            Call("Play", JObject.Parse("{'etty':'ZOOM','scale':1.3,'time':200}")); Call("Tick",.2f);
            Assert.That(camera.WorldToViewportPoint(target.position).y, Is.EqualTo(y).Within(.005f), "A foot anchor must not become the middle of the screen.");
        }
        [Test] public void VisibleMapConstrainsRequestedCameraAndHiddenStageDoesNot()
        {
            Create(true);
            Call("Play",JObject.Parse("{'etty':'CAM','x':10000,'y':10000}")); Call("Tick",0f);
            float halfX=camera.orthographicSize*camera.aspect;
            Assert.That(camera.transform.position.x+halfX, Is.LessThanOrEqualTo(15.001f));
            Assert.That(camera.transform.position.y+camera.orthographicSize, Is.LessThanOrEqualTo(10.001f));
            Call("Play",JObject.Parse("{'etty':'BGSHOW','show':false}"));
            Call("Play",JObject.Parse("{'etty':'CAM','x':10000,'y':10000}")); Call("Tick",0f);
            Assert.That(camera.transform.position.x, Is.GreaterThan(90f));
            Call("Reset"); Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0,0,-10)));
        }
        [Test] public void CameraSpineShakeDoesNotAccumulateAndResetsPrecisely()
        {
            Create(false);
            var method=stage.GetType().GetMethod("PlayShake");
            Assert.That(method, Is.Not.Null, "Original camera skeleton curves must execute, rather than warn and skip.");
            var json="{\"skeleton\":{\"spine\":\"2.1.27\",\"hash\":\"\"},\"bones\":[{\"name\":\"root\"},{\"name\":\"camera\",\"parent\":\"root\"}],\"slots\":[],\"skins\":{\"default\":{}},\"animations\":{\"animation\":{\"bones\":{\"camera\":{\"translate\":[{\"time\":0},{\"time\":0.1,\"x\":20,\"y\":10},{\"time\":0.2}]}}}}}";
            var source=new SkeletonPlayerSource(json,"",new Dictionary<string,Texture2D>());
            method.Invoke(stage,new object[]{JObject.Parse("{'etty':'SHAKE','source':'test','timeScale':1}"),source});
            Call("Tick",.1f); Vector3 shaken=camera.transform.position;
            Assert.That(Vector2.Distance(shaken,Vector2.zero), Is.GreaterThan(.05f));
            Call("Tick",0f); Assert.That(camera.transform.position,Is.EqualTo(shaken));
            Call("Tick",.2f); Assert.That(camera.transform.position,Is.EqualTo(new Vector3(0,0,-10)));
            Call("Reset"); Assert.That(camera.transform.position,Is.EqualTo(new Vector3(0,0,-10)));
        }
        [Test] public void HiddenStageRetainsTheRequestedWideZoomAfterAMapConstraint()
        {
            Create(true);
            Call("Play",JObject.Parse("{'etty':'ZOOM','scale':0.25}"));
            Call("Play",JObject.Parse("{'etty':'BGSHOW','show':false}")); Call("Tick",0f);
            Assert.That(camera.orthographicSize,Is.EqualTo(20f).Within(.001f));
        }
        [Test] public void SourceFocusFollowsTheMovingVisualActor()
        {
            var target=Create(false); float baseline=camera.WorldToViewportPoint(target.position).y;
            Call("Play",JObject.Parse("{'etty':'CAM','location':11}"));
            target.position+=Vector3.up*10f; Call("Tick",.1f);
            Assert.That(camera.WorldToViewportPoint(target.position).y,Is.EqualTo(baseline).Within(.005f));
        }
        private void Call(string name,params object[] args)=>stage.GetType().GetMethod(name).Invoke(stage,args);
    }
}
