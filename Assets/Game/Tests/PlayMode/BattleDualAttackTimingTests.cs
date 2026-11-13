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

        internal static BattleFlowEvent Event(BattleFlowEventKind kind, string source, string skill, string presentation, BattleActionOrigin origin)
        {
            var ctor = typeof(BattleFlowEvent).GetConstructors(Instance).Single();
            var values = ctor.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : p.ParameterType == typeof(ulong) ? (object)1UL : p.ParameterType == typeof(int) ? 1 : (object)kind).ToArray();
            var named = new Dictionary<string, object> { ["kind"] = kind, ["sourceId"] = source, ["targetId"] = "enemy-1", ["skillId"] = skill, ["presentationId"] = presentation, ["origin"] = origin, ["amount"] = 30, ["hitKind"] = BattleHitKind.Normal, ["remainingHealth"] = 3000 };
            foreach (var p in ctor.GetParameters()) if (named.TryGetValue(p.Name, out var value)) values[p.Position] = value;
            return (BattleFlowEvent)ctor.Invoke(values);
        }
        private static object Field(object value, string field) => value.GetType().GetField(field, Instance).GetValue(value);
        private static object Call(object value, string method, params object[] args) => value.GetType().GetMethod(method, Instance).Invoke(value, args);
        [UnityTearDown] public IEnumerator Cleanup() { if (scene.IsValid() && scene.isLoaded) yield return SceneManager.UnloadSceneAsync(scene); }
    }
}
#endif
