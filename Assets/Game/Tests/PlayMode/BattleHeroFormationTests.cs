#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattleHeroFormationTests
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
        private Scene scene;

        [UnityTest]
        public IEnumerator DefaultBattlePlaysEightDistinctHeroesAndAnEnemyHeroActsAutomatically()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity",
                new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.SelectMany(root => root.GetComponentsInChildren<BattleHudPreview>()).Single();
            var hud = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var snapshot = host.SimulationSnapshot;
            Assert.That(snapshot, Is.Not.Null);

            var allies = new[] { "c1001", "c1062", "c1005", "c1002" };
            var enemies = new[] { "c1003", "c1004", "c1006", "c1007" };
            var units = roots.Single(root => root.name == "Units").transform;
            CheckTeam(BattleSide.Ally, allies, "Allies");
            CheckTeam(BattleSide.Enemy, enemies, "Enemies");
            Assert.That(snapshot.Units.Select(unit => unit.DefinitionId).Distinct().Count(), Is.EqualTo(8));

            var simulation = typeof(BattleHudPreview).GetField("simulation", InstancePrivate).GetValue(host);
            var skillField = simulation.GetType().GetField("presentedSkillId", InstancePrivate);
            string skill = null;
            float deadline = Time.realtimeSinceStartup + 10f;
            while (Time.realtimeSinceStartup < deadline)
            {
                skill = (string)skillField.GetValue(simulation);
                if (skill != null && enemies.Any(id => skill == id + ".s1") &&
                    hud.Require<Image>("activePortrait").sprite.name == skill.Substring(0, 5) + "_su") break;
                yield return null;
            }
            Assert.That(skill, Is.Not.Null, "An enemy must submit an actual hero skill without player input.");
            Assert.That(enemies.Select(id => id + ".s1"), Does.Contain(skill));
            Assert.That(hud.transform.Find("Skills").gameObject.activeSelf, Is.False);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.GreaterThan(0));

            if (skill == "c1004.s1")
            {
                var portrait = hud.Require<Image>("activePortrait");
                Assert.That(portrait.sprite.name, Is.EqualTo("c1004_su"));
                Assert.That(portrait.rectTransform.rect.size, Is.EqualTo(new Vector2(363, 309)));
                var silkSlot = units.Find("Enemies").GetChild(1);
                var silkResource = hud.transform.Find("Unit Status/Enemies/" + silkSlot.name + "/HP/Personal Resource")
                    .GetComponent<Image>();
                Assert.That(silkResource.fillAmount, Is.EqualTo(.2f).Within(.001f));
            }

            var swap = units.Find("Allies").GetChild(3).GetComponentInChildren<BattlePreviewActor>(true);
            typeof(BattlePreviewActor).GetField("heroId", InstancePrivate).SetValue(swap, "c1008");
            host.RestartBattle();
            Assert.That(host.SimulationSnapshot.GetUnit("ally-4").DefinitionId, Is.EqualTo("c1008"));
            Assert.That(swap.name, Does.EndWith("[c1008]"));
            Assert.That(swap.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));

            typeof(BattlePreviewActor).GetField("heroId", InstancePrivate).SetValue(swap, "c1009");
            host.RestartBattle();
            var swappedMarker = hud.transform.Find("Unit Status/Allies/" + swap.transform.parent.name);
            var spirit = swappedMarker.Find("HP/Personal Resource").GetComponent<Image>();
            Assert.That(spirit.sprite.name, Is.EqualTo("game_hud_bar_bp"));
            Assert.That(spirit.fillAmount, Is.EqualTo(0f));
            Assert.That(spirit.transform.Find("Resource Dividers").GetComponent<BattleHealthSegments>().DividerCount, Is.EqualTo(4));

            typeof(BattlePreviewActor).GetField("heroId", InstancePrivate).SetValue(swap, "c1008");
            host.RestartBattle();
            Assert.That(swappedMarker.Find("HP/Personal Resource"), Is.Null);
            Assert.That(swappedMarker.Find("Frame").GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_bar_bg_n"));

            void CheckTeam(BattleSide side, string[] ids, string group)
            {
                var ordered = snapshot.Units.Where(unit => unit.Slot.Side == side).OrderBy(unit => unit.Slot.Position).ToArray();
                CollectionAssert.AreEqual(ids, ordered.Select(unit => unit.DefinitionId));
                for (int slot = 0; slot < ids.Length; slot++)
                {
                    var slotObject = units.Find(group).GetChild(slot);
                    var actor = slotObject.GetComponentInChildren<BattlePreviewActor>(true);
                    Assert.That(actor.name, Does.EndWith("[" + ids[slot] + "]"));
                    Assert.That(actor.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
                    Assert.That(typeof(BattlePreviewActor).GetField("heroId", InstancePrivate).GetValue(actor), Is.EqualTo(ids[slot]));
                    Assert.That((float)typeof(BattlePreviewActor).GetProperty("FacingDirection", InstancePrivate).GetValue(actor),
                        Is.EqualTo(side == BattleSide.Enemy ? -1f : 1f));
                    var marker = hud.transform.Find("Unit Status/" + group + "/" + slotObject.name);
                    Assert.That(marker.Find("Element").GetComponent<Image>().sprite.name,
                        Is.EqualTo("game_hud_bar_" + ordered[slot].Element.ToString().ToLowerInvariant()));
                    var resource = marker.Find("HP/Personal Resource")?.GetComponent<Image>();
                    if (ids[slot] == "c1004")
                    {
                        Assert.That(resource, Is.Not.Null, "Silk uses the source Focus strip.");
                        Assert.That(resource.sprite.name, Is.EqualTo("game_hud_bar_cp"));
                        Assert.That(marker.Find("Frame").GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_bar_bg"));
                    }
                    else
                    {
                        Assert.That(resource, Is.Null, ids[slot]);
                        Assert.That(marker.Find("Frame").GetComponent<Image>().sprite.name, Is.EqualTo("game_hud_bar_bg_n"));
                    }
                }
            }
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
