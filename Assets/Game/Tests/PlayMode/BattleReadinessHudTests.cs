#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleReadinessHudTests
    {
        private const string Module = "Assets/Game/Modules/BattleScene/";
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject root;
        private object turnHud;
        private Type hudType;
        private BattleSnapshot snapshot;
        private Transform portraits;
        private Transform details;

        [SetUp]
        public void SetUp()
        {
            root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Module + "Res/Prefab/BattleHud.prefab"));
            var catalog = JsonUtility.FromJson<BattleCatalogData>(File.ReadAllText(Module + "Configs/battle-catalog.json"));
            snapshot = BattlePreviewFactory.Create("readiness-test", 271828UL, catalog).Snapshot;
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { Module + "Res/Textures/Hud" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(sprite => sprite).GroupBy(sprite => sprite.name).ToDictionary(group => group.Key, group => group.First());
            hudType = typeof(BattleHud).Assembly.GetType("AstralOath.Client.BattleScene.BattleTurnHud", true);
            turnHud = Activator.CreateInstance(hudType, PrivateInstance, null,
                new object[] { root.GetComponent<BindingMap>(), (Func<BattleUnitSnapshot, string>)(unit => unit.NameKey), sprites, null }, null);
            Invoke("Refresh", snapshot);
            Invoke("Tick", 2f);
            portraits = root.transform.Find("Combat Readiness/Live Readiness/Readiness Portraits");
            details = root.transform.Find("Combat Readiness/Live Readiness/Readiness Details");
        }

        [Test]
        public void CurrentActorIsAtTheBottomOfTheReadinessRail()
        {
            float actorY = Position(snapshot.CurrentActorId);
            foreach (var unit in snapshot.Units.Where(unit => unit.InstanceId != snapshot.CurrentActorId))
                Assert.That(actorY, Is.LessThanOrEqualTo(Position(unit.InstanceId)), "Readiness increases downwards toward the active portrait.");
        }

        [Test]
        public void ReadinessPositionIsLinearAndIndependentOfSurvivorCount()
        {
            string id = snapshot.Units.First(unit => unit.InstanceId != snapshot.CurrentActorId).InstanceId;
            Invoke("SetReadiness", id, 0); Invoke("Tick", 2f); float zero = Position(id);
            Invoke("SetReadiness", id, 10000); Invoke("Tick", 2f); float full = Position(id);
            Invoke("SetReadiness", id, 5000); Invoke("Tick", 2f); float half = Position(id);
            Assert.That(full, Is.LessThan(zero));
            Assert.That(half, Is.EqualTo((zero + full) / 2f).Within(0.01f));
            foreach (var unit in snapshot.Units.Where(unit => unit.InstanceId != id)) Invoke("RemoveUnit", unit.InstanceId);
            Invoke("Tick", 2f);
            Assert.That(Position(id), Is.EqualTo(half).Within(0.01f), "Removing unrelated units must not falsify this unit's readiness.");
        }

        [Test]
        public void DetailsUsesOriginalRowStructureAndOutsideClickClosesIt()
        {
            var button = root.transform.Find("Combat Readiness/Live Readiness/Readiness Details Button").GetComponent<GameButton>();
            button.onClick.Invoke();
            Assert.That(details.gameObject.activeSelf, Is.True);
            var row = details.Find("Rows/" + snapshot.CurrentActorId);
            Assert.That(row, Is.Not.Null, "Original details expand the left portrait queue into rows.");
            Assert.That(row.Find("Readiness").GetComponent<GameText>().text, Does.Contain("100"));
            Assert.That(row.Find("Skill 1").GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(row.Find("Skill 2").GetComponent<Image>().sprite, Is.Not.Null);
            Assert.That(row.Find("Skill 3").GetComponent<Image>().sprite, Is.Not.Null);
            details.Find("Close Surface").GetComponent<GameButton>().onClick.Invoke();
            Assert.That(details.gameObject.activeSelf, Is.False);
        }

        [Test]
        public void PreviewSlimePortraitsMatchTheFourAuthoredModelSkins()
        {
            var names = new Dictionary<BattleElement, string>
            {
                { BattleElement.Fire, "m0001_s" }, { BattleElement.Ice, "m0002_s" },
                { BattleElement.Wind, "m0003_s" }, { BattleElement.Dark, "m0005_s" }
            };
            foreach (var unit in snapshot.Units.Where(unit => unit.DefinitionId == "preview.slime"))
            {
                var face = portraits.Find(unit.InstanceId + "/Face").GetComponent<Image>();
                Assert.That(face.sprite.name, Is.EqualTo(names[unit.Element]),
                    "The preview monster uses the original face matching its model skin, not a pet portrait.");
            }
        }

        private float Position(string id) => ((RectTransform)portraits.Find(id)).anchoredPosition.y;
        private void Invoke(string method, params object[] args) => hudType.GetMethod(method, PrivateInstance).Invoke(turnHud, args);

        [TearDown]
        public void TearDown()
        {
            (turnHud as IDisposable)?.Dispose();
            if (root) UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
#endif
