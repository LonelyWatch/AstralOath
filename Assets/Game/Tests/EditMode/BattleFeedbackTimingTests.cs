using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleFeedbackTimingTests
    {
        private const string BattleAssembly = "AstralOath.Client.BattleScene";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private GameObject root;
        private object hud;

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/Modules/BattleScene/Res/Prefab/BattleHud.prefab");
            root = Object.Instantiate(prefab);
            hud = Activator.CreateInstance(BattleType("BattleTurnHud"), Instance, null,
                new object[] { root.GetComponent<BindingMap>(), new Func<BattleUnitSnapshot, string>(unit => unit.NameKey),
                    new Dictionary<string, Sprite>(), null }, null);
        }

        [TearDown]
        public void Cleanup()
        {
            if (root) Object.DestroyImmediate(root);
        }

        [TestCase(BattleHitKind.Miss)]
        [TestCase(BattleHitKind.Normal)]
        public void ZeroDamageCreatesNoDamageNumberPopup(BattleHitKind kind)
        {
            Call(hud, "ShowDamageHit", "target", 0, kind,
                (RectTransform)root.transform, Vector3.zero, null);

            Assert.That(root.transform.Find("Battle Feedback/target"), Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void PooledDigitsReapplyTheHealingAndOrdinaryDamageGradients(bool nativeFont)
        {
            if (nativeFont)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                    "Assets/Game/Modules/BattleScene/Res/Fonts/BattleDamageDigits.asset");
                Object.DestroyImmediate(root);
                root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Game/Modules/BattleScene/Res/Prefab/BattleHud.prefab"));
                hud = Activator.CreateInstance(BattleType("BattleTurnHud"), Instance, null,
                    new object[] { root.GetComponent<BindingMap>(), new Func<BattleUnitSnapshot, string>(unit => unit.NameKey),
                        new Dictionary<string, Sprite>(), font }, null);
            }
            var anchor = (RectTransform)root.transform;
            var healing = BattlePresentationWire.Event(1, 1, BattleFlowEventKind.Healing, "caster", "target", 3402,
                null, null, BattleActionOrigin.Primary, null, null, null, null, BattleHitKind.None, null, false, null, null);
            Call(hud, "ShowImpact", healing, anchor, Vector3.zero, null);
            var label = root.transform.Find("Battle Feedback/target");
            var digits = label.GetComponentsInChildren<RawImage>();
            if (nativeFont)
            {
                Assert.That(digits.Length, Is.EqualTo(5));
                Call(hud, "TickFeedback", .045f);
                var heights = digits.Select(glyph => glyph.rectTransform.anchoredPosition.y).ToArray();
                Assert.That(heights.Max() - heights.Min(), Is.GreaterThan(20f), "Recovery digits bounce independently.");
            }
            void CheckGradient(bool green)
            {
                Color32 top, bottom;
                if (nativeFont)
                {
                    var glyph = label.GetComponentsInChildren<RawImage>().First();
                    using var mesh = new VertexHelper();
                    typeof(Graphic).GetMethod("OnPopulateMesh", Instance, null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(glyph, new object[] { mesh });
                    var vertex = default(UIVertex);
                    mesh.PopulateUIVertex(ref vertex, 0);
                    bottom = vertex.color;
                    mesh.PopulateUIVertex(ref vertex, 1);
                    top = vertex.color;
                }
                else
                {
                    var text = label.GetComponentInChildren<GameText>();
                    Assert.That(text.enableVertexGradient, Is.True);
                    top = text.colorGradient.topLeft;
                    bottom = text.colorGradient.bottomLeft;
                }
                Assert.That(top.r, Is.GreaterThan(215));
                Assert.That(bottom.r, Is.LessThan(green ? 82 : 165));
                if (green) Assert.That(bottom.g, Is.GreaterThan(220));
                else Assert.That(bottom.g, Is.EqualTo(bottom.r));
            }
            CheckGradient(true);
            Call(hud, "TickFeedback", 2f);
            Call(hud, "ShowDamageHit", "target", 8640, BattleHitKind.Normal, anchor, Vector3.zero, null);
            Assert.That(root.transform.Find("Battle Feedback/target"), Is.SameAs(label));
            CheckGradient(false);
        }

        [Test]
        public void LargeFrameShowsEachFeedbackAtItsOwnWorldPose()
        {
            var world = new GameObject("Battle World").transform;
            world.SetParent(root.transform, false);
            using var stage = (IDisposable)Activator.CreateInstance(BattleType("BattleSkillPresentation"),
                new object[] { world, Array.Empty<TextAsset>(), Array.CreateInstance(BattleType("BattleSkillPresentationAsset"), 0),
                    null, null, null });

            Call(stage, "Schedule", .5f, (Action)(() => world.localPosition = Vector3.right * 3f));
            Call(stage, "Schedule", 1f, (Action)(() => world.localPosition = Vector3.right * 7f));
            Call(stage, "Schedule", 1.25f, (Action)(() => world.localPosition = Vector3.zero));

            var simulationType = BattleType("BattleSimulationPreview");
            var simulation = FormatterServices.GetUninitializedObject(simulationType);
            Set("skillPresentation", stage);
            Set("turnHud", hud);
            var hudMap = root.GetComponent<BindingMap>();
            var skillBanner = Activator.CreateInstance(BattleType("BattleSkillBanner"), Instance, null,
                new object[] { (RectTransform)root.transform.Find("Top Bar"), hudMap.Require<GameText>("activeName"),
                    new Dictionary<string, Sprite>() }, null);
            Set("skillBanner", skillBanner);
            Set("voice", FormatterServices.GetUninitializedObject(BattleType("BattleVoice")));
            Set("views", Activator.CreateInstance(simulationType.GetField("views", Instance).FieldType));
            Set("playback", Activator.CreateInstance(simulationType.GetField("playback", Instance).FieldType));
            Set("pendingFeedback", Activator.CreateInstance(simulationType.GetField("pendingFeedback", Instance).FieldType));
            Set("actionDuration", 2f);

            var positions = new List<float>();
            Call(simulation, "ScheduleFeedback", .5f, "target", (Action)(() => positions.Add(world.localPosition.x)));
            Call(simulation, "ScheduleFeedback", 1f, "target", (Action)(() => positions.Add(world.localPosition.x)));
            Call(simulation, "AdvancePresentation", 1.5f);

            Assert.That(positions, Is.EqualTo(new[] { 3f, 7f }));
            Assert.That(world.localPosition, Is.EqualTo(Vector3.zero));

            void Set(string name, object value) => simulationType.GetField(name, Instance).SetValue(simulation, value);
        }

        private static Type BattleType(string name) => Type.GetType(BattleAssembly + "." + name + ", " + BattleAssembly, true);

        private static object Call(object instance, string method, params object[] args) =>
            instance.GetType().GetMethod(method, Instance).Invoke(instance, args);
    }
}
