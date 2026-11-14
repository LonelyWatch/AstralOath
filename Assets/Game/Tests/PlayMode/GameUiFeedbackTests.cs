using System;
using System.Collections;
using System.Reflection;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class GameUiFeedbackTests
    {
        [UnityTest]
        public IEnumerator ACanvasReusesNonInteractiveClickGlows()
        {
            var target = new GameObject("Feedback test canvas", typeof(Canvas));
            target.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            yield return null;
            Canvas.ForceUpdateCanvases();
            var texture = Texture2D.whiteTexture;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * .5f);
            try
            {
                var type = Type.GetType("AstralOath.Client.Common.GameUiFeedback, AstralOath.Client.Common");
                Assert.That(type, Is.Not.Null, "The root Canvas needs shared click feedback.");
                var feedback = target.AddComponent(type);
                type.GetMethod("Configure").Invoke(feedback, new object[] { sprite, sprite, null, null, null, null, null });
                var track = type.GetMethod("Track", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(track, Is.Not.Null);
                var center = new Vector2(Screen.width * .5f, Screen.height * .5f);
                track.Invoke(feedback, new object[] { 9, center, true, false, false });
                track.Invoke(feedback, new object[] { 9, center + Vector2.right * 100, false, false, false });
                track.Invoke(feedback, new object[] { 9, center, false, true, false });
                Assert.That(target.GetComponentsInChildren<Image>(), Is.Empty, "A drag returning to its starting point cannot play a click pulse.");
                for (int i = 0; i < 20; i++)
                {
                    track.Invoke(feedback, new object[] { i, center, true, false, false });
                    track.Invoke(feedback, new object[] { i, center, false, true, false });
                }
                var images = target.GetComponentsInChildren<Image>();
                Assert.That(images.Length, Is.EqualTo(20), $"Canvas: {target.GetComponent<Canvas>().renderMode}, rect: {((RectTransform)target.transform).rect}, children: {target.transform.childCount}, rendered: {target.GetComponentsInChildren<Image>(true).Length}");
                foreach (var image in images) Assert.That(image.raycastTarget, Is.False);
                yield return new WaitForSecondsRealtime(.65f);
                Assert.That(target.GetComponentsInChildren<Image>(), Is.Empty);
            }
            finally
            {
                Object.Destroy(target);
                Object.Destroy(sprite);
            }
        }
    }
}
