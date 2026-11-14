#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class BattleDualAttackTimingTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private Scene scene;

        [UnityTest]
        public IEnumerator DualAttackBeginsAtLastHitWhileFirstActorRetainsRecovery()
        {
            scene = EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/BattleScene/Scenes/BattleScene.unity", new LoadSceneParameters(LoadSceneMode.Additive));
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(x => x.GetComponent<BattleHudPreview>()).Single(x => x);
            host.StartBattle();
            var simulation = Field(host, "simulation");
            var queue = (Queue<BattleFlowEvent>)Field(simulation, "playback");
            queue.Enqueue(Event(BattleFlowEventKind.Damage, "ally-1", "c1001.s2", null, BattleActionOrigin.Primary));
            queue.Enqueue(Event(BattleFlowEventKind.Action, "ally-3", "c1005.s1", "c_mercedes_skill_01", BattleActionOrigin.DualAttack));
            queue.Enqueue(Event(BattleFlowEventKind.Damage, "ally-3", "c1005.s1", null, BattleActionOrigin.DualAttack));
            Call(simulation, "Present", Event(BattleFlowEventKind.Action, "ally-1", "c1001.s2", "c_ras_skill_02", BattleActionOrigin.Primary));
            var skillBanner = host.GetComponentInChildren<BindingMap>().transform.Find("Top Bar/Skill Use Banner");
            Assert.That(skillBanner.gameObject.activeInHierarchy, Is.True);
            Assert.That(skillBanner.Find("Skill Icon").GetComponent<Image>().sprite.name, Is.EqualTo("sk_c1001_2"));
            Assert.That(skillBanner.Find("Caster Name").GetComponent<GameText>().text, Is.Not.Empty);
            Assert.That(skillBanner.Find("Skill Name").GetComponent<GameText>().text, Is.Not.Empty);
            Assert.That(((GameText)Field(simulation, "banner")).text, Is.Empty);
            var first = Field(simulation, "skillPresentation");
            var hits = (float[])Field(simulation, "hitTimes");
            float lastHit = hits.Last();
            float duration = (float)Field(simulation, "actionDuration");
            Assert.That(duration, Is.GreaterThan(lastHit + .05f), "Fixture has a recovery segment after its final HIT.");
            Call(simulation, "Tick", lastHit - .001f);
            Assert.That(Field(simulation, "presentedSkillId"), Is.EqualTo("c1001.s2"));
            Call(simulation, "Tick", .002f);
            Assert.That(Field(simulation, "presentedSkillId"), Is.EqualTo("c1005.s1"), "Dual attacker starts at the last source HIT, without waiting for return and CFX expiry.");
            Assert.That(Field(first, "caster"), Is.Not.Null, "The first actor keeps its recovery instead of teleporting home.");
            Assert.That(Field(simulation, "skillPresentation"), Is.Not.SameAs(first));
            Assert.That(skillBanner.gameObject.activeInHierarchy, Is.True);
            Assert.That(skillBanner.Find("Skill Icon").GetComponent<Image>().sprite.name, Is.EqualTo("sk_c1005_1"));
            Call(simulation, "ShowPrompt");
            Assert.That(skillBanner.gameObject.activeSelf, Is.False, "Returning to the command prompt dismisses the skill banner.");
        }

        [UnityTest]
        public IEnumerator OffensivePassiveWaitsForPrimaryFeedbackAndKeepsControlsHidden()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene("c1007");
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var simulation = Field(host, "simulation");
            var queue = (Queue<BattleFlowEvent>)Field(simulation, "playback");
            var map = host.GetComponentInChildren<BindingMap>();
            var feedback = Field(simulation, "turnHud");
            var caster = host.SimulationSnapshot.CurrentActorId;
            queue.Enqueue(Event(BattleFlowEventKind.Damage, caster, "c1007.s1", null, BattleActionOrigin.Primary));
            queue.Enqueue(Event(BattleFlowEventKind.StatusApplied, caster, "c1007.s1", null, BattleActionOrigin.Primary,
                statusKind: BattleStatusKind.AttackUp));
            queue.Enqueue(Event(BattleFlowEventKind.CooldownChanged, caster, "c1007.s2", null, BattleActionOrigin.Primary, amount: 2, target: caster));
            queue.Enqueue(Event(BattleFlowEventKind.Action, caster, "c1007.s2", "c_wilderd_skill_02", BattleActionOrigin.Passive));
            Call(simulation, "Present", Event(BattleFlowEventKind.Action, caster, "c1007.s1", "c_wilderd_skill_01", BattleActionOrigin.Primary));

            float duration = (float)Field(simulation, "actionDuration");
            Call(simulation, "Tick", duration - .001f);
            Assert.That(Field(simulation, "presentedSkillId"), Is.EqualTo("c1007.s1"));
            Assert.That(map.transform.Find("Unit Status/Enemies/01 Front/Frame/Status Icons").GetChild(0)
                .GetComponent<Image>().sprite.name, Is.EqualTo("stic_att_up"));
            Assert.That(map.Require<GameButton>("skill2").transform.Find("Cooldown Overlay").gameObject.activeSelf, Is.True);
            Assert.That(map.Require<GameButton>("skill2").interactable, Is.False);
            int hits = (int)Field(feedback, "hitCount");
            Call(simulation, "Tick", .002f);
            Assert.That(Field(simulation, "presentedSkillId"), Is.EqualTo("c1007.s2"));
            Assert.That((int)Field(feedback, "hitCount"), Is.EqualTo(hits), "Passive joins the existing combo.");
            Assert.That(map.transform.Find("Top Bar/Skill Use Banner/Skill Icon").GetComponent<Image>().sprite.name,
                Is.EqualTo("pa_c1007_2"));
            Assert.That(map.transform.Find("Skills").gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator DamageTrailWaitsForTheLastImpactAcrossAnExtraAttack() =>
            AssertDamageTrailHolds(BattleActionOrigin.ExtraAttack, "ally-1", "c1001.s2", "c_ras_skill_02");

        [UnityTest]
        public IEnumerator DamageTrailWaitsForTheLastImpactAcrossADualAttack() =>
            AssertDamageTrailHolds(BattleActionOrigin.DualAttack, "ally-3", "c1005.s1", "c_mercedes_skill_01");

        private IEnumerator AssertDamageTrailHolds(BattleActionOrigin followUp, string source, string skill, string presentation)
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene();
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var simulation = Field(host, "simulation");
            var queue = (Queue<BattleFlowEvent>)Field(simulation, "playback");
            var marker = host.GetComponentInChildren<BindingMap>().transform.Find("Unit Status/Enemies/01 Front");
            var health = marker.Find("HP").GetComponent<Image>();
            var trail = marker.GetComponentsInChildren<Image>(true).Single(image => image.name == "Damage Trail");
            float RedEnd() => trail.rectTransform.anchorMax.x;

            queue.Enqueue(Event(BattleFlowEventKind.Damage, "ally-1", "c1001.s2", null, BattleActionOrigin.Primary, amount: 900));
            queue.Enqueue(Event(BattleFlowEventKind.Action, source, skill, presentation, followUp));
            queue.Enqueue(Event(BattleFlowEventKind.Damage, source, skill, null, followUp, amount: 1100));
            Call(simulation, "Present", Event(BattleFlowEventKind.Action, "ally-1", "c1001.s2", "c_ras_skill_02", BattleActionOrigin.Primary));

            float healthBefore = health.fillAmount;
            float firstDuration = (float)Field(simulation, "actionDuration");
            float firstLastImpact = ((float[])Field(simulation, "hitTimes")).Last();
            float transition = followUp == BattleActionOrigin.DualAttack ? firstLastImpact : firstDuration;
            Call(simulation, "Tick", transition + .01f);
            Assert.That(health.fillAmount, Is.LessThan(healthBefore), "The first action already dealt damage.");
            Assert.That(trail.enabled, Is.True);
            Assert.That(RedEnd(), Is.EqualTo(healthBefore).Within(.001f),
                "The red edge stays at the starting HP through recovery and the next attack's windup.");

            float healthAfterFirst = health.fillAmount;
            float lastImpact = ((float[])Field(simulation, "hitTimes")).DefaultIfEmpty((float)Field(simulation, "impactSeconds")).Last();
            Call(simulation, "Tick", lastImpact - (float)Field(simulation, "actionElapsed"));
            Assert.That(health.fillAmount, Is.LessThan(healthAfterFirst));
            Assert.That(RedEnd(), Is.EqualTo(healthBefore).Within(.001f));
            Call(simulation, "Tick", .4f);
            Assert.That(RedEnd(), Is.InRange(health.fillAmount + .001f, healthBefore - .001f),
                "Only the final impact starts the red trail's retraction.");
        }

        [UnityTest]
        public IEnumerator EachVisualHitFlashesOnlyTheTargetsReadinessPortrait()
        {
            scene = BattleScenePreviewTests.LoadPlayerOpeningScene("c1001");
            yield return null;
            yield return null;
            var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
            var map = host.GetComponentInChildren<BindingMap>();
            var simulation = Field(host, "simulation");
            var queue = (Queue<BattleFlowEvent>)Field(simulation, "playback");
            var rail = map.transform.Find("Combat Readiness/Live Readiness/Readiness Portraits");
            var face = rail.Find("enemy-1/Face").GetComponent<Image>();
            var other = rail.Find("enemy-2/Face").GetComponent<Image>();
            var normal = face.color;
            var otherNormal = other.color;
            queue.Enqueue(Event(BattleFlowEventKind.Damage, "ally-1", "c1001.s2", null, BattleActionOrigin.Primary, amount: 900));
            Call(simulation, "Present", Event(BattleFlowEventKind.Action, "ally-1", "c1001.s2", "c_ras_skill_02", BattleActionOrigin.Primary));
            var hits = (float[])Field(simulation, "hitTimes");
            Assert.That(hits.Length, Is.GreaterThan(1));
            foreach (float hit in hits)
            {
                Call(simulation, "Tick", hit - (float)Field(simulation, "actionElapsed") + .001f);
                Assert.That(face.color, Is.Not.EqualTo(normal), "Every authored hit flashes the damaged unit.");
                Assert.That(other.color, Is.EqualTo(otherNormal));
                Call(simulation, "Tick", .2f);
                Assert.That(face.color, Is.EqualTo(normal));
                Assert.That(face.rectTransform.anchoredPosition, Is.EqualTo(Vector2.zero));
            }
        }

        internal static BattleFlowEvent Event(BattleFlowEventKind kind, string source, string skill, string presentation, BattleActionOrigin origin,
            BattleStatusKind? statusKind = null, int amount = 30, string target = "enemy-1")
        {
            var ctor = typeof(BattleFlowEvent).GetConstructors(Instance).Single();
            var values = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(ulong) ? (object)1UL : p.ParameterType == typeof(int) ? 1 : (object)kind).ToArray();
            var status = statusKind.HasValue ? typeof(BattleStatusSnapshot).GetConstructor(Instance, null,
                new[] { typeof(BattleStatusKind), typeof(int), typeof(int) }, null)
                .Invoke(new object[] { statusKind.Value, 2, 5000 }) : null;
            var named = new Dictionary<string, object> { ["kind"] = kind, ["sourceId"] = source, ["targetId"] = target, ["skillId"] = skill, ["presentationId"] = presentation, ["origin"] = origin, ["amount"] = amount, ["statusKind"] = statusKind, ["status"] = status, ["hitKind"] = BattleHitKind.Normal, ["remainingHealth"] = 3000 };
            foreach (var p in ctor.GetParameters()) if (named.TryGetValue(p.Name, out var value)) values[p.Position] = value;
            return (BattleFlowEvent)ctor.Invoke(values);
        }
        private static object Field(object value, string field) => value.GetType().GetField(field, Instance).GetValue(value);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Instance).Invoke(value, args);
        [UnityTearDown] public IEnumerator Cleanup() { if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene); }
    }
}
#endif
