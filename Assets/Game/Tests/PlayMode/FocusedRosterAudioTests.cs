#if UNITY_EDITOR
using System.Reflection;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class FocusedRosterAudioTests
    {
        private const string ClipPath = "Assets/Game/Common/Res/Sounds/UiRosterScroll.wav";

        [Test]
        public void HomeCanvasIncludesOriginalRosterTick() =>
            Assert.That(AssetDatabase.GetDependencies("Assets/Game/Modules/Home/Scenes/GameHome.unity"), Does.Contain(ClipPath));

        [Test]
        public void OnlyPointerDrivenRowCrossingsPlayIncludingCoasting()
        {
            using var scope = new BindingScope(default);
            var root = new GameObject("Audio test", typeof(RectTransform), typeof(Canvas), typeof(GameUiFeedback));
            var events = new GameObject("Roster events", typeof(EventSystem));
            var sample = AudioClip.Create("Roster test sound", 4800, 1, 48000, false);
            try
            {
                var feedback = root.GetComponent<GameUiFeedback>();
                typeof(GameUiFeedback).GetField("rosterScroll", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(feedback, sample);
                var panel = new GameObject("Cards", typeof(RectTransform), typeof(ScrollRect));
                var scroll = panel.GetComponent<ScrollRect>();
                panel.transform.SetParent(root.transform, false);
                scroll.viewport = (RectTransform)panel.transform;
                scroll.content = (RectTransform)new GameObject("Content", typeof(RectTransform)).transform;
                scroll.content.SetParent(scroll.viewport, false);
                scroll.content.anchorMin = scroll.content.anchorMax = scroll.content.pivot = Vector2.up;
                scroll.horizontal = false;
                scroll.scrollSensitivity = 100f;
                scroll.movementType = ScrollRect.MovementType.Clamped;
                var list = FocusedRosterScroll.Bind(scroll, scope, null, _ => { });
                var tick = typeof(FocusedRosterScroll).GetMethod("LateUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
                var pointer = new PointerEventData(events.GetComponent<EventSystem>()) { scrollDelta = Vector2.down };
                void Move(float y)
                {
                    scroll.content.anchoredPosition = new Vector2(0f, y);
                    tick.Invoke(list, null);
                }
                Component AudioOutput() => root.GetComponent("GameUiAudio");
                void ExpectSound()
                {
                    Assert.That(AudioOutput(), Is.Not.Null);
                    Object.DestroyImmediate(AudioOutput());
                }
                list.Layout(12, 100f);
                list.Focus(3);
                Move(300);
                Assert.That(AudioOutput(), Is.Null, "Binding or restoring the initial focus played a tick.");
                list.OnBeginDrag(pointer);
                Move(330);
                Assert.That(AudioOutput(), Is.Null, "Movement inside the current card played a tick.");
                Move(365);
                ExpectSound();
                list.OnEndDrag(pointer);
                Move(480);
                ExpectSound();
                list.Focus(1);
                Move(100);
                Assert.That(AudioOutput(), Is.Null, "Jumping focus should be silent.");
                ExecuteEvents.Execute<IScrollHandler>(panel, pointer, ExecuteEvents.scrollHandler);
                Move(210);
                ExpectSound();
                list.Layout(3, 80f);
                list.Focus(0);
                Move(0);
                Assert.That(AudioOutput(), Is.Null, "Resizing or filtering replayed a tick.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(events);
                Object.DestroyImmediate(sample);
            }
        }
    }
}
#endif
