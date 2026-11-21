#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaCommandPlaybackTests
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [UnityTest]
        public IEnumerator LocalTurnSubmitsWithoutDeadlineAndOnlyAcceptedEventsStartTheSkill()
        {
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json").text);
            var match = BattlePreviewFactory.Create("command-fixture", 7723, catalog,
                Enumerable.Repeat("c1001", 4).ToArray(), BattleScenePreviewTests.SlowEnemyIds(), duel: true);
            var opening = match.Snapshot;
            var actor = opening.GetUnit(opening.CurrentActorId);
            var ai = new BattleAiPolicy(catalog);
            bool hostSeat = actor.Slot.Side == BattleSide.Ally;
            var skill = actor.Form.SkillIds[0];
            var target = ai.ChooseTarget(opening, skill).TargetId;
            var transport = new Replies();
            var sessionType = typeof(BattleHudPreview).Assembly.GetType("AstralOath.Client.BattleScene.BattleOnlineSession", true);
            using var session = (IDisposable)Activator.CreateInstance(sessionType, Members, null,
                new object[] { new Uri("http://localhost"), "fixture-room", opening.BattleId, "fixture-hash", "fixture", hostSeat, null }, null);
            var http = sessionType.GetField("http", Members);
            ((IDisposable)http.GetValue(session)).Dispose();
            http.SetValue(session, new HttpClient(transport));
            sessionType.GetField("heroes", Members).SetValue(session, catalog.heroes.ToDictionary(row => row.id));
            var update = Call(session, "Read", Wire(opening, match.OpeningEvents, null), true, 0UL);
            sessionType.GetProperty("Snapshot", Members).SetValue(session, Field(update, "Snapshot"));
            Call(session, "ApplyClock", update);
            // This test drives the command response precisely. The independent time-based
            // ready/poll loop is covered by the clock and server integration fixtures.
            sessionType.GetField("nextPoll", Members).SetValue(session, float.MaxValue);
            Assert.That(((BattleTurnClockView)Property(session, "Clock")).DeadlineUnixMilliseconds, Is.Null);
            var scene = BattleScenePreviewTests.LoadPlayerOpeningScene("c1001");
            try
            {
                yield return null;
                var host = scene.GetRootGameObjects().Select(root => root.GetComponent<BattleHudPreview>()).Single(item => item);
                var assets = AssetDatabase.LoadAssetAtPath<BattleOnlineTransitionAssets>("Assets/Game/Modules/BattleScene/Configs/AstralOathOnlineTransition.asset");
                Call(host, "PrepareOnline", session, assets, catalog);
                Call(host, "FinishOnlineEntry");
                var simulation = Field(host, "simulation");
                var hud = (BindingMap)Field(host, "hud");
                var skills = hud.transform.Find("Skills").gameObject;
                var soul = hud.Require<GameButton>("soulBurn");
                var first = hud.Require<GameButton>("skill1");
                Assert.That(first.interactable && skills.activeInHierarchy && soul.gameObject.activeInHierarchy);
                first.onClick.Invoke();
                Assert.That(Call(simulation, "ChooseTarget", target), Is.EqualTo(true));
                Assert.That(transport.Command["skillId"].Value<string>(), Is.EqualTo(skill));
                Assert.That(transport.Command["targetId"].Value<string>(), Is.EqualTo(target));
                Assert.That(skills.activeSelf || soul.gameObject.activeSelf, Is.False, "Both input groups must hide synchronously.");
                Assert.That(Field(simulation, "presentedSkillId"), Is.Null);
                Assert.That(Call(simulation, "ChooseTarget", target), Is.EqualTo(false));
                transport.Respond(HttpStatusCode.OK, Wire(opening, Array.Empty<BattleFlowEvent>(), BattleCommandStatus.SkillOnCooldown));
                yield return WaitUntil(() => first.interactable && skills.activeInHierarchy);
                Assert.That(soul.gameObject.activeInHierarchy);
                Assert.That(Field(simulation, "presentedSkillId"), Is.Null, "A rejected command must not start a presentation.");
                Assert.That(((GameText)Field(simulation, "banner")).text, Does.Contain("冷却"));
                first.onClick.Invoke();
                Assert.That(Call(simulation, "ChooseTarget", target), Is.EqualTo(true));
                var command = transport.Command.ToObject<BattleSkillCommand>();
                var confirmed = match.Submit(command);
                Assert.That(confirmed.Status, Is.EqualTo(BattleCommandStatus.Accepted));
                transport.Respond(HttpStatusCode.OK, Wire(confirmed.Snapshot, confirmed.Events, confirmed.Status));
                yield return WaitUntil(() => (string)Field(simulation, "presentedSkillId") == skill);
                Assert.That(skills.activeSelf || soul.gameObject.activeSelf, Is.False);
                // A new 401 on a read is a terminal session, unlike an authoritative game-rule rejection.
                sessionType.GetField("nextPoll", Members).SetValue(session, 0f);
                Call(session, "Tick", true, false);
                transport.Respond(HttpStatusCode.Unauthorized, null);
                yield return WaitUntil(() => { Call(session, "Tick", false, false); return (bool)Property(session, "Terminated"); });
                Assert.That(Property(session, "CanSubmit"), Is.EqualTo(false));
            }
            finally { SceneManager.UnloadSceneAsync(scene); }
        }

        private static JObject Wire(BattleSnapshot state, System.Collections.Generic.IEnumerable<BattleFlowEvent> events, BattleCommandStatus? status)
        {
            var clock = new BattleTurnClockView { ActorId = state.CurrentActorId, CommandSequence = state.LastCommandSequence,
                EventSequence = state.LastEventSequence, DecisionSeconds = 15, ServerNowUnixMilliseconds = 100000,
                DeadlineUnixMilliseconds = null };
            return JObject.FromObject(new { ContentHash = "fixture-hash", Snapshot = state, Events = events, Status = status, Clock = clock },
                JsonSerializer.Create(new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
        }

        private sealed class Replies : HttpMessageHandler
        {
            private TaskCompletionSource<HttpResponseMessage> pending;
            internal JObject Command { get; private set; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.That(pending, Is.Null, "The same turn cannot issue overlapping fixture commands.");
                if (request.RequestUri.AbsolutePath.EndsWith("/commands", StringComparison.Ordinal))
                    Command = JObject.Parse(request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                pending = new TaskCompletionSource<HttpResponseMessage>();
                return pending.Task;
            }
            internal void Respond(HttpStatusCode status, JObject payload)
            {
                var next = pending;
                pending = null;
                Assert.That(next, Is.Not.Null);
                next.SetResult(new HttpResponseMessage(status) { Content = new StringContent(payload?.ToString() ?? string.Empty) });
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 10;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, "The expected session/HUD transition did not complete.");
        }
        private static object Field(object owner, string name) => owner.GetType().GetField(name, Members).GetValue(owner);
        private static object Property(object owner, string name) => owner.GetType().GetProperty(name, Members).GetValue(owner);
        private static object Call(object owner, string method, params object[] args) =>
            owner.GetType().GetMethod(method, Members).Invoke(owner, args);
    }
}
#endif
