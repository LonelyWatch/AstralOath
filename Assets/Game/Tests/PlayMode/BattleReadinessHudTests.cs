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
        public void HiddenDetailsWaitUntilOpenedBeforeRebuildingRows()
        {
            string id = snapshot.Units.First(unit => unit.InstanceId != snapshot.CurrentActorId).InstanceId;
            var toggle = root.transform.Find("Combat Readiness/Live Readiness/Readiness Details Button").GetComponent<GameButton>();
            toggle.onClick.Invoke();
            var label = details.Find("Rows/" + id + "/Readiness").GetComponent<GameText>();
            string before = label.text;
            details.Find("Close Surface").GetComponent<GameButton>().onClick.Invoke();
            Invoke("SetReadiness", id, before.Contains("53") ? 6100 : 5300);
            Assert.That(label.text, Is.EqualTo(before), "Closed details should not rebuild text or icons during turn handoffs.");
            toggle.onClick.Invoke();
            Assert.That(label.text, Is.Not.EqualTo(before), "Opening details must immediately show the latest readiness.");
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
        public void DetailRowsInspectCurrentSkillAndStatusAndDismissWhenClosed()
        {
            (turnHud as IDisposable)?.Dispose();
            var sprites = AssetDatabase.FindAssets("t:Sprite", new[] { Module + "Res/Textures/Hud" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(sprite => sprite).GroupBy(sprite => sprite.name).ToDictionary(group => group.Key, group => group.First());
            var profile = AssetDatabase.LoadAssetAtPath<BattleUnitPresentationProfile>(Module + "Res/Configs/UnitPresentation.asset");
            object art = typeof(BattleUnitPresentationProfile).GetMethod("CreateStatusArt", PrivateInstance).Invoke(profile, null);
            BattleUnitSnapshot inspectedUnit = null;
            Image inspectedIcon = null;
            int inspectedIndex = -1, closed = 0;
            BattleStatusSnapshot inspectedStatus = null;
            Action<BattleUnitSnapshot, int, Image> skill = (unit, index, icon) =>
            {
                inspectedUnit = unit;
                inspectedIndex = index;
                inspectedIcon = icon;
            };
            Action<BattleStatusSnapshot, Image> status = (snapshotStatus, icon) =>
            {
                inspectedStatus = snapshotStatus;
                inspectedIcon = icon;
            };
            turnHud = Activator.CreateInstance(hudType, PrivateInstance, null, new object[]
            {
                root.GetComponent<BindingMap>(), (Func<BattleUnitSnapshot, string>)(unit => unit.NameKey), sprites, null,
                skill, status, (Action)(() => closed++), art
            }, null);
            Invoke("Refresh", snapshot);

            var current = snapshot.GetUnit(snapshot.CurrentActorId);
            details = root.transform.Find("Combat Readiness/Live Readiness/Readiness Details");
            root.transform.Find("Combat Readiness/Live Readiness/Readiness Details Button").GetComponent<GameButton>().onClick.Invoke();
            var row = details.Find("Rows/" + current.InstanceId);
            var skillIcon = row.Find("Skill 2").GetComponent<Image>();
            Assert.That(skillIcon.raycastTarget, Is.True);
            skillIcon.GetComponent<GameLongPress>().Held.Invoke();
            Assert.That(inspectedUnit, Is.SameAs(current));
            Assert.That(inspectedIndex, Is.EqualTo(1));
            Assert.That(inspectedIcon, Is.SameAs(skillIcon));

            // Publish a new snapshot value through the detail row, as Refresh does on a new turn.
            var detail = turnHud.GetType().GetField("portraits", PrivateInstance).GetValue(turnHud) as System.Collections.IDictionary;
            var portrait = detail[current.InstanceId];
            var rowView = portrait.GetType().GetProperty("Detail", PrivateInstance).GetValue(portrait);
            var unit = BattleUnit.CreateMonster(current.InstanceId,
                new MonsterDefinition(current.DefinitionId, current.NameKey, current.Element, current.Form),
                current.Slot, current.Level, MonsterRank.Normal, current.Stats);
            BattleUnitSnapshot WithStatus(BattleStatusKind kind, int turns, int strength = 0)
            {
                var effect = (BattleStatusSnapshot)Activator.CreateInstance(typeof(BattleStatusSnapshot), PrivateInstance, null,
                    new object[] { kind, turns, strength }, null);
                return (BattleUnitSnapshot)Activator.CreateInstance(typeof(BattleUnitSnapshot), PrivateInstance, null,
                    new object[] { unit, current.CurrentHealth, current.ReadinessBp, current.Cooldowns, new[] { effect }, 0 }, null);
            }
            var updated = WithStatus(BattleStatusKind.DefenseDown, 2, 7000);
            var update = rowView.GetType().GetMethod("Update", PrivateInstance);
            update.Invoke(rowView, new object[] { updated, current.ReadinessBp, true });
            var statusIcon = row.Find("States/Status 1").GetComponent<Image>();
            Assert.That(statusIcon.sprite.name, Is.EqualTo("stic_def_dn"));
            statusIcon.GetComponent<GameLongPress>().Held.Invoke();
            Assert.That(inspectedStatus, Is.SameAs(updated.GetStatus(BattleStatusKind.DefenseDown)));
            Assert.That(inspectedStatus.Strength, Is.EqualTo(7000));
            Assert.That(inspectedIcon, Is.SameAs(statusIcon));

            var rebound = WithStatus(BattleStatusKind.Immunity, 1);
            update.Invoke(rowView, new object[] { rebound, current.ReadinessBp, true });
            statusIcon.GetComponent<GameLongPress>().Held.Invoke();
            Assert.That(inspectedStatus, Is.SameAs(rebound.GetStatus(BattleStatusKind.Immunity)));
            Assert.That(inspectedStatus.RemainingTurns, Is.EqualTo(1));
            skillIcon.GetComponent<GameLongPress>().Held.Invoke();
            Assert.That(inspectedUnit, Is.SameAs(rebound));

            details.Find("Close Surface").GetComponent<GameButton>().onClick.Invoke();
            Assert.That(closed, Is.EqualTo(1));
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
