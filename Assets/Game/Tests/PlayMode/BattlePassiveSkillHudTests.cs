#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed class BattlePassiveSkillHudTests
    {
        private Scene scene;

        [UnityTest]
        public IEnumerator PassiveSkillsUseNativeFrameAndOnlyDarkenWhenCoolingDown()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene("c1004");
            yield return null;
            yield return null;
            var roots = scene.GetRootGameObjects();
            var host = roots.Select(root => root.GetComponent<BattleHudPreview>()).Single(component => component);
            var map = roots.SelectMany(root => root.GetComponentsInChildren<BindingMap>()).Single();
            var skill = map.Require<GameButton>("skill2");
            var activeFrame = map.transform.Find("Skills/Skill 1 Frame").GetComponent<Image>();
            var frame = map.transform.Find("Skills/Skill 2 Frame").GetComponent<Image>();
            var shade = skill.transform.Find("Cooldown Overlay").GetComponent<Image>();
            var icon = (Image)skill.targetGraphic;

            Assert.That(host.SimulationSnapshot.GetUnit(host.SimulationSnapshot.CurrentActorId).DefinitionId,
                Is.EqualTo("c1004"));
            Assert.That(icon.sprite.name, Is.EqualTo("pa_c1004_2"));
            Assert.That(frame.sprite.name, Is.EqualTo("_frame_passive"));
            Assert.That(activeFrame.sprite.name, Is.EqualTo("_cm_skill_frm"));
            Assert.That(icon.color, Is.EqualTo(Color.white));
            Assert.That(shade.gameObject.activeSelf, Is.False);
            Assert.That(skill.interactable, Is.False);
            skill.onClick.Invoke();
            Assert.That(map.Require<Image>("skillSelected2").gameObject.activeSelf, Is.False);
            Assert.That(host.SimulationSnapshot.LastCommandSequence, Is.Zero);

            var simulation = typeof(BattleHudPreview).GetField("simulation",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(host);
            var turnHud = simulation.GetType().GetField("turnHud", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(simulation);
            var setCooldown = turnHud.GetType().GetMethod("SetCooldown", BindingFlags.Instance | BindingFlags.NonPublic);
            setCooldown.Invoke(turnHud, new object[] { 1, 2 });
            Assert.That(shade.gameObject.activeSelf, Is.True);
            Assert.That(shade.sprite.name, Is.EqualTo("cm_cool_skill_passive"));
            setCooldown.Invoke(turnHud, new object[] { 1, 0 });
            Assert.That(shade.gameObject.activeSelf, Is.False);
            Assert.That(icon.color, Is.EqualTo(Color.white));

            map.transform.Find("Combat Readiness/Live Readiness/Readiness Details Button")
                .GetComponent<GameButton>().onClick.Invoke();
            var row = map.transform.Find("Combat Readiness/Live Readiness/Readiness Details/Rows/" +
                host.SimulationSnapshot.CurrentActorId);
            var detail = row.Find("Skill 2").GetComponent<Image>();
            Assert.That(detail.sprite.name, Is.EqualTo("pa_c1004_2"));
            Assert.That(detail.transform.Find("Passive Frame").GetComponent<Image>().sprite.name,
                Is.EqualTo("_frame_passive"));
            Assert.That(detail.transform.Find("Cooldown").gameObject.activeSelf, Is.False);
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene);
        }
    }
}
#endif
