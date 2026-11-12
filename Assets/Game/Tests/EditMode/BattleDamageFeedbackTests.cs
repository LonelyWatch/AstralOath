using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleDamageFeedbackTests
    {
        private const string Module = "Assets/Game/Modules/BattleScene";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private GameObject root;
        private RectTransform anchor;
        private Camera camera;
        private object hud;
        private TMP_FontAsset damageFont;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Module + "/Res/Prefab/BattleHud.prefab");
            root = Object.Instantiate(prefab);
            anchor = (RectTransform)root.transform;
            camera = new GameObject("Feedback Test Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(root.transform, false);
            camera.transform.position = Vector3.back * 10f;
            camera.orthographic = true;
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleTurnHud, AstralOath.Client.BattleScene", true);
            var sprites = new Dictionary<string, Sprite>();
            string damagePath = Module + "/Res/Textures/Hud/Damage";
            if (AssetDatabase.IsValidFolder(damagePath))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { damagePath }))
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                    sprites.Add(sprite.name, sprite);
                }
            }
            damageFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Module + "/Res/Fonts/BattleDamageDigits.asset");
            hud = Activator.CreateInstance(type, Instance, null,
                new object[] { root.GetComponent<BindingMap>(), new Func<BattleUnitSnapshot, string>(unit => unit.NameKey),
                    sprites, damageFont }, null);
        }

        [TearDown]
        public void TearDown()
        {
            if (root)
                Object.DestroyImmediate(root);
        }

        [Test]
        public void DamageFromTheSameActionUpdatesOneLabelPerTarget()
        {
            Impact("target-1", 11);
            Impact("target-2", 67);
            Impact("target-1", 23);

            Assert.That(ActiveLabels().Count(label => label.name == "target-1"), Is.EqualTo(1));
            Assert.That(ActiveLabels().Count(label => label.name == "target-2"), Is.EqualTo(1));
            Assert.That(Number(ActiveLabels().Single(label => label.name == "target-1")), Is.EqualTo("34"));
            Assert.That(Number(ActiveLabels().Single(label => label.name == "target-2")), Is.EqualTo("67"));
        }

        [Test]
        public void ANewActionStartsASeparateTotalForTheSameTarget()
        {
            Impact("target-1", 42);
            var begin = hud.GetType().GetMethod("BeginFeedbackAction", Instance);
            Assert.That(begin, Is.Not.Null, "Action boundaries must reset the damage total.");
            begin.Invoke(hud, Array.Empty<object>());
            Impact("target-1", 15);

            Assert.That(ActiveLabels().Select(Number), Does.Contain("15"));
            Assert.That(ActiveLabels().Select(Number), Does.Not.Contain("57"));
        }

        [Test]
        public void HealingDoesNotJoinTheDamageTotal()
        {
            Impact("target-1", 11);
            Impact("target-1", 23, BattleFlowEventKind.Healing, BattleHitKind.None);
            Impact("target-1", 32);

            Assert.That(ActiveLabels().Select(Number), Is.EquivalentTo(new[] { "43", "23" }));
        }

        [Test]
        public void FinishedFeedbackReturnsToPoolAndCanBeReused()
        {
            Impact("target-1", 42);
            var first = ActiveLabels().Single();
            hud.GetType().GetMethod("TickFeedback", Instance).Invoke(hud, new object[] { 5f });
            Assert.That(ActiveLabels(), Is.Empty);
            Impact("target-2", 15);

            Assert.That(ActiveLabels().Single(), Is.SameAs(first));
            Assert.That(Number(first), Is.EqualTo("15"));
        }

        private void Impact(string target, int amount, BattleFlowEventKind kind = BattleFlowEventKind.Damage,
            BattleHitKind hitKind = BattleHitKind.Normal)
        {
            var item = (BattleFlowEvent)Activator.CreateInstance(typeof(BattleFlowEvent), Instance, null,
                new object[] { 1ul, 1, kind, "source", target, amount, null, null, BattleActionOrigin.Primary,
                    null, null, null, null, hitKind, null, false, 1 }, null);
            hud.GetType().GetMethod("ShowImpact", Instance)
                .Invoke(hud, new object[] { item, anchor, Vector3.zero, camera });
        }

        private Transform[] ActiveLabels() => root.transform.Find("Battle Feedback").Cast<Transform>()
            .Where(child => child.gameObject.activeSelf && child.name.StartsWith("target-", StringComparison.Ordinal)).ToArray();

        private string Number(Transform label)
        {
            var glyphs = label.GetComponentsInChildren<RawImage>();
            Assert.That(glyphs, Is.Not.Empty, "A feedback number must use the original bitmap atlas.");

            return string.Concat(glyphs.Select(image =>
            {
                Assert.That(image.texture, Is.SameAs(damageFont.atlasTexture));
                int x = Mathf.RoundToInt(image.uvRect.x * image.texture.width);
                int y = Mathf.RoundToInt(image.uvRect.y * image.texture.height);
                var character = damageFont.characterTable.Single(item => item.glyph.glyphRect.x == x && item.glyph.glyphRect.y == y);
                return (char)character.unicode;
            })).TrimStart('+');
        }
    }
}
