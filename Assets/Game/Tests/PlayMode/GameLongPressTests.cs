using System.Collections;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class GameLongPressTests
    {
#if UNITY_EDITOR
        [UnityTest]
        public IEnumerator ConfirmPrefabPlaysOnCanvasWhenButtonCloses()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Common/Res/Prefab/ConfirmButton.prefab");
            Assert.That(prefab, Is.Not.Null);
            var canvas = new GameObject("Confirmation owner", typeof(Canvas));
            var system = new GameObject("Confirmation events", typeof(EventSystem), typeof(AudioListener));
            try
            {
                var item = Object.Instantiate(prefab, canvas.transform);
                var map = item.GetComponent<BindingMap>();
                var button = map.Require<GameButton>("button");
                Assert.That(button.GetComponent<AudioSource>().clip, Is.Not.Null);
                button.onClick.AddListener(() => item.SetActive(false));
                button.OnSubmit(new BaseEventData(system.GetComponent<EventSystem>()));
                Assert.That(item.activeSelf, Is.False);
                Assert.That(canvas.GetComponent<AudioSource>(), Is.Not.Null);
                yield return null;
                Assert.That(canvas.GetComponent<AudioSource>().isPlaying, Is.True,
                    "A confirmation that closes its own button still plays its click.");
            }
            finally
            {
                Object.Destroy(canvas);
                Object.Destroy(system);
            }
        }
#endif

        [UnityTest]
        public IEnumerator MouseAndTouchHoldsDoNotDispatchClicks()
        {
            var pressType = System.Type.GetType("AstralOath.Client.Common.GameLongPress, AstralOath.Client.Common");
            Assert.That(pressType, Is.Not.Null, "A shared mouse/touch hold recognizer must be installed.");
            var input = new GameObject("Hold event system", typeof(EventSystem));
            var obj = new GameObject("Skill", typeof(RectTransform), typeof(Image), typeof(GameButton), pressType);
            try
            {
                var system = input.GetComponent<EventSystem>();
                var hold = obj.GetComponent(pressType);
                int held = 0, clicks = 0;
                pressType.GetProperty("Held").SetValue(hold, (System.Action)(() => held++));
                obj.GetComponent<GameButton>().onClick.AddListener(() => clicks++);
                foreach (int id in new[] { -1, 7 })
                {
                    var pointer = new PointerEventData(system) { pointerId = id, button = PointerEventData.InputButton.Left };
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerDownHandler);
                    yield return new WaitForSecondsRealtime(.55f);
                    Assert.That(held, Is.EqualTo(id == -1 ? 1 : 2));
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerClickHandler);
                    Assert.That(clicks, Is.Zero);
                }

                var mouse = new PointerEventData(system) { pointerId = -1, button = PointerEventData.InputButton.Left };
                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerClickHandler);
                Assert.That(clicks, Is.EqualTo(1), "Ordinary clicks remain available.");

                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerDownHandler);
                mouse.position += Vector2.one * 100f;
                yield return new WaitForSecondsRealtime(.55f);
                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(obj, mouse, ExecuteEvents.pointerClickHandler);
                Assert.That(held, Is.EqualTo(2));
                Assert.That(clicks, Is.EqualTo(1), "A canceled hold must not become a cast.");
            }
            finally
            {
                Object.Destroy(obj);
                Object.Destroy(input);
            }
        }
    }
}
