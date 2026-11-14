using System;
using System.Linq;
using System.Reflection;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class BattleTurnClockProjectionTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void CountdownProjectsTheServerSampleWithElapsedMonotonicTimeAndNeverGoesNegative()
        {
            var clock = new BattleTurnClockView { DecisionSeconds = 15, ServerNowUnixMilliseconds = 100000,
                DeadlineUnixMilliseconds = 115000 };
            Assert.That(clock.RemainingSeconds(1.25), Is.EqualTo(13.75));
            Assert.That(clock.RemainingSeconds(16), Is.Zero);
            Assert.That(clock.RemainingSeconds(-10), Is.EqualTo(15));
            clock.DeadlineUnixMilliseconds = null;
            Assert.That(clock.RemainingSeconds(0), Is.Zero, "Presentation waiting is not a fabricated countdown.");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BothSeatsKeepTheSameDeadlineAndServerActorIdentity(bool host)
        {
            WithSession(host, (type, session, wire) =>
            {
                var update = Read(type, session, wire);
                var clock = (BattleTurnClockView)update.GetType().GetField("Clock", Members).GetValue(update);
                Assert.That(clock.DeadlineUnixMilliseconds, Is.EqualTo(115000));
                Assert.That(clock.ActorId, Is.EqualTo((string)wire["snapshot"]["currentActorId"]));
                Assert.That(update.GetType().GetField("ClockUnavailable", Members).GetValue(update), Is.False);
            });
        }

        [TestCase("missing")]
        [TestCase("version")]
        [TestCase("deadline")]
        public void MissingOrIncompatibleClockReportsServiceReadinessInsteadOfInventingTime(string fault)
        {
            WithSession(true, (type, session, wire) =>
            {
                if (fault == "missing") wire.Remove("clock");
                else if (fault == "version") wire["clock"]["protocolVersion"] = 99;
                else ((JObject)wire["clock"]).Remove("deadlineUnixMilliseconds");
                var update = Read(type, session, wire);
                Assert.That(update.GetType().GetField("ClockUnavailable", Members).GetValue(update), Is.True);
                type.GetProperty("Snapshot", Members).SetValue(session,
                    update.GetType().GetField("Snapshot", Members).GetValue(update));
                type.GetMethod("ApplyClock", Members).Invoke(session, new[] { update });
                Assert.That(type.GetProperty("ClockUnavailable", Members).GetValue(session), Is.True);
                Assert.That(type.GetProperty("Terminated", Members).GetValue(session), Is.True);
                Assert.That(type.GetProperty("CanSubmit", Members).GetValue(session), Is.False);
            });
        }

        private static object Read(Type type, object session, JObject wire) =>
            type.GetMethod("Read", Members).Invoke(session, new object[] { wire, true, 0UL });

        private static void WithSession(bool host, Action<Type, object, JObject> check)
        {
            var catalog = JsonConvert.DeserializeObject<BattleCatalogData>(AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Game/Modules/BattleScene/Configs/battle-catalog.json").text);
            var match = BattlePreviewFactory.Create("clock-match", 7723, catalog,
                new[] { "c1001", "c1002", "c1008", "c1005" }, new[] { "c1003", "c1004", "c1062", "c1007" }, duel: true);
            var type = Type.GetType("AstralOath.Client.BattleScene.BattleOnlineSession, AstralOath.Client.BattleScene", true);
            using var session = (IDisposable)Activator.CreateInstance(type, Members, null,
                new object[] { new Uri("http://localhost"), "room", "clock-match", "test-hash", "token", host }, null);
            type.GetField("heroes", Members).SetValue(session, catalog.heroes.ToDictionary(row => row.id, StringComparer.Ordinal));
            var clock = new BattleTurnClockView { CommandSequence = match.Snapshot.LastCommandSequence,
                EventSequence = match.Snapshot.LastEventSequence, ActorId = match.Snapshot.CurrentActorId,
                DecisionSeconds = 15, ServerNowUnixMilliseconds = 100000, DeadlineUnixMilliseconds = 115000 };
            var wire = JObject.Parse(JsonConvert.SerializeObject(new { ContentHash = "test-hash", match.Snapshot,
                SnapshotReset = false, Events = match.OpeningEvents, Clock = clock },
                new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() }));
            check(type, session, wire);
        }
    }
}
