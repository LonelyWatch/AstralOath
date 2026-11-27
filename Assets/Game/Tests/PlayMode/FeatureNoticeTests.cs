#if UNITY_EDITOR
using System.Collections;
using System.Threading;
using AstralOath.Client.Core;
using AstralOath.Client.FoundationDemo;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class FeatureNoticeTests
    {
        [UnityTest]
        public IEnumerator DismissalIsSingleUseAndClosingTheOwnerUnbindsTheReusableBanner()
        {
            var canvas=new GameObject("Feature notice test",typeof(RectTransform),typeof(Canvas));
            canvas.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Common/Res/Prefab/FeatureNotice.prefab");
            Assert.That(prefab,Is.Not.Null);
            var instance=Object.Instantiate(prefab,canvas.transform,false);
            try
            {
                var view=FeatureNotice.ForInstance(instance);var binder=new FeatureNoticeBinder(view);int closed=0;
                using(var scope=new BindingScope(CancellationToken.None))
                {
                    binder.Open(scope,"title","<color=red>plain</color>","close",()=>closed++);
                    Assert.That(view.Root.activeSelf,Is.True);Assert.That(view.Message.richText,Is.False);
                    Assert.That(view.Message.text,Is.EqualTo("<color=red>plain</color>"));
                    view.Dismiss.onClick.Invoke();view.Dismiss.onClick.Invoke();
                    Assert.That(closed,Is.EqualTo(1));
                }
                Assert.That(view.Root.activeSelf,Is.False);
                view.Dismiss.onClick.Invoke();Assert.That(closed,Is.EqualTo(1));
                using(var next=new BindingScope(CancellationToken.None))
                {
                    binder.Open(next,"second","message","close",()=>closed++);
                    Assert.That(view.Root.activeSelf,Is.True);
                    view.Dismiss.onClick.Invoke();Assert.That(closed,Is.EqualTo(2));
                }
                Assert.That(view.Root.activeSelf,Is.False);
                yield return null;
            }
            finally{Object.Destroy(instance);Object.Destroy(canvas);}
        }
    }
}
#endif
