using System.Collections;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class CommonChoiceFeedbackTests
    {
        // This invokes a real GameButton, including its normal audio lifecycle.
        // It belongs in PlayMode: UiAudioPlayback owns a DontDestroyOnLoad host.
        [UnityTest]
        public IEnumerator ChoiceBinderSelectsOnceAndClearsRowsOnClose()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Common/Res/Prefab/ChoicePopup.prefab");
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            try
            {
                using var binder = new ChoicePopupBinder(ChoicePopup.FromInstance(instance));
                int selected = -1;
                binder.Open(new[] { "全部", "公开" }, 0, value => selected = value);
                var view = instance.GetComponent<BindingMap>();
                var body = view.Require<RectTransform>("body");
                var rows = body.GetComponentsInChildren<GameButton>(false);
                Assert.That(rows, Has.Length.EqualTo(2));
                Assert.That(rows[0].GetComponent<BindingMap>().Require<Image>("check").gameObject.activeSelf, Is.True);
                rows[1].onClick.Invoke();
                Assert.That(selected, Is.EqualTo(1));
                Assert.That(instance.activeSelf, Is.False);
                // PlayMode Destroy removes choice rows at the end of the frame.
                yield return null;
                Assert.That(body.childCount, Is.EqualTo(1));
                yield return new WaitForSecondsRealtime(UiAudioPlayback.FadeSeconds + .05f);
            }
            finally
            {
                Object.Destroy(instance);
            }
            yield return null;
        }
    }
}
