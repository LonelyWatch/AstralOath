using System;
using System.Collections.Generic;
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

        [Test]
        public void ZeroDamageMissShowsOnlyItsLocalizedCaption()
        {
            Call(hud, "SetMissFeedbackLabel", "未命中");
            Call(hud, "ShowDamageHit", "target", 0, BattleHitKind.Miss,
                (RectTransform)root.transform, Vector3.zero, null);

            var popup = root.transform.Find("Battle Feedback/target");
            Assert.That(popup, Is.Not.Null, "An absorbed miss still needs its localized caption.");
            Assert.That(popup.Find("Impact Caption").GetComponent<GameText>().text, Is.EqualTo("未命中"));
            Assert.That(popup.Find("Impact").GetComponent<GameText>().text, Is.Empty);
            Assert.That(popup.GetComponentsInChildren<RawImage>(), Is.Empty);
        }

        [Test]
        public void ZeroDamageNormalHitShowsNoPopup()
        {
            Call(hud, "ShowDamageHit", "target", 0, BattleHitKind.Normal,
                (RectTransform)root.transform, Vector3.zero, null);

            Assert.That(root.transform.Find("Battle Feedback/target"), Is.Null);
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
            Set("voice", FormatterServices.GetUninitializedObject(BattleType("BattleVoice")));
            Set("playback", Activator.CreateInstance(simulationType.GetField("playback", Instance).FieldType));
            Set("pendingFeedback", Activator.CreateInstance(simulationType.GetField("pendingFeedback", Instance).FieldType));
            Set("actionDuration", 2f);

            var positions = new List<float>();
            Call(simulation, "ScheduleFeedback", .5f, "target", (Action)(() => positions.Add(world.localPosition.x)));
            Call(simulation, "ScheduleFeedback", 1f, "target", (Action)(() => positions.Add(world.localPosition.x)));
            Call(simulation, "Tick", 1.5f);

            Assert.That(positions, Is.EqualTo(new[] { 3f, 7f }));
            Assert.That(world.localPosition, Is.EqualTo(Vector3.zero));

            void Set(string name, object value) => simulationType.GetField(name, Instance).SetValue(simulation, value);
        }

        private static Type BattleType(string name) => Type.GetType(BattleAssembly + "." + name + ", " + BattleAssembly, true);

        private static object Call(object instance, string method, params object[] args) =>
            instance.GetType().GetMethod(method, Instance).Invoke(instance, args);
    }
}
