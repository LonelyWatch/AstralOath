using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleOnlineSession : IDisposable
    {
        internal sealed class Update
        {
            internal BattleSnapshot Snapshot;
            internal BattleFlowEvent[] Events;
            internal bool Reset;
            internal BattleCommandStatus? Status;
            internal BattleTurnClockView Clock;
            internal double ClockSampleTime;
            internal bool ClockUnavailable;
            internal ulong? ReadySequence, ReadyEventSequence;
        }

        private const float PollInterval = .30f;
        private readonly Uri url;
        private readonly string battleId, contentHash, bearer;
        private readonly bool localIsHost;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Dictionary<string, BattleCatalogData.HeroRow> heroes;
        private Task<Update> request;
        private float nextPoll;
        private bool disposed;
        private ulong? readySequence, readyEventSequence;
        private double clockSampleTime;
        internal BattleSnapshot Snapshot { get; private set; }
        internal BattleOnlinePlayers Players { get; set; }
        internal bool Submitting { get; private set; }
        internal bool Healthy { get; private set; } = true;
        internal bool Terminated { get; private set; }
        internal bool ClockUnavailable { get; private set; }
        internal BattleTurnClockView Clock { get; private set; }
        internal double ClockElapsed => Math.Max(0d, Time.realtimeSinceStartupAsDouble - clockSampleTime);
        internal bool CanSubmit => Healthy && !Terminated && !Submitting && Clock != null &&
            Clock.CommandSequence == Snapshot.LastCommandSequence && Clock.EventSequence == Snapshot.LastEventSequence &&
            Clock.ActorId == Snapshot.CurrentActorId && Clock.DeadlineUnixMilliseconds.HasValue && Clock.RemainingSeconds(ClockElapsed) > 0;

        internal BattleOnlineSession(Uri endpoint, string roomId, string battleId, string contentHash,
            string bearer, bool localIsHost)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri) throw new ArgumentException("Invalid battle endpoint.");
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(battleId) ||
                string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(bearer))
                throw new ArgumentException("Battle invitation is incomplete.");
            url = new Uri(endpoint, "/api/arena/rooms/" + Uri.EscapeDataString(roomId) + "/battle");
            this.battleId = battleId;
            this.contentHash = contentHash;
            this.bearer = bearer;
            this.localIsHost = localIsHost;
        }

        internal async Task OpenAsync(TextAsset asset, BattleCatalogData catalog, CancellationToken token)
        {
            using var hash = SHA256.Create();
            string canonical = asset.text.TrimStart('\ufeff').Replace("\r\n", "\n");
            string localHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
            if (!string.Equals(localHash, contentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Client and server battle data differ.");
            heroes = catalog.heroes.ToDictionary(row => row.id, StringComparer.Ordinal);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var update = await SendAsync(null, 0, cancellation.Token);
            Snapshot = update.Snapshot;
            ApplyClock(update);
        }

        internal bool Submit(string skillId, string targetId, bool useSoulBurn)
        {
            if (disposed || !CanSubmit) return false;
            // If a read is already in flight, ignore its stale response. The new command response
            // or the subsequent cursor read supplies the complete authoritative result.
            Observe(request);
            var command = new JObject
            {
                ["battleId"] = battleId,
                ["rulesVersion"] = Snapshot.RulesVersion,
                ["sequence"] = Snapshot.LastCommandSequence + 1,
                ["commandId"] = Guid.NewGuid().ToString("N"),
                ["actorId"] = Snapshot.CurrentActorId, ["skillId"] = skillId, ["targetId"] = targetId,
                ["useSoulBurn"] = useSoulBurn, ["contentHash"] = contentHash
            };
            Submitting = true;
            request = SendAsync(command, Snapshot.LastEventSequence, lifetime.Token);
            return true;
        }

        internal Update Tick(bool canPoll, bool canReady = true)
        {
            if (disposed || Terminated) return null;
            if (request != null)
            {
                if (!request.IsCompleted) return null;
                var finished = request;
                request = null;
                Submitting = false;
                nextPoll = Time.unscaledTime + PollInterval;
                try
                {
                    var update = finished.GetAwaiter().GetResult();
                    Healthy = true;
                    if (update.Snapshot.LastEventSequence < Snapshot.LastEventSequence) return null;
                    Snapshot = update.Snapshot;
                    ApplyClock(update);
                    if (update.ReadySequence.HasValue)
                    {
                        readySequence = update.ReadySequence;
                        readyEventSequence = update.ReadyEventSequence;
                    }
                    return update;
                }
                catch (InvalidDataException)
                {
                    Healthy = false;
                    Terminated = true;
                    return null;
                }
                catch (IOException)
                {
                    Healthy = false;
                    nextPoll = Time.unscaledTime + 1f;
                }
            }
            if (request == null && canPoll && Time.unscaledTime >= nextPoll &&
                Snapshot.Outcome == BattleOutcome.InProgress)
            {
                bool needsReady = canReady && (readySequence != Snapshot.LastCommandSequence || readyEventSequence != Snapshot.LastEventSequence);
                var ready = needsReady ? new JObject
                {
                    ["battleId"] = battleId, ["commandSequence"] = Snapshot.LastCommandSequence,
                    ["eventSequence"] = Snapshot.LastEventSequence, ["actorId"] = Snapshot.CurrentActorId
                } : null;
                request = SendAsync(ready, Snapshot.LastEventSequence, lifetime.Token, needsReady);
            }
            return null;
        }

        private void ApplyClock(Update update)
        {
            ClockUnavailable = update.ClockUnavailable;
            if (ClockUnavailable) { Healthy = false; Terminated = true; }
            Clock = update.Clock != null && update.Clock.CommandSequence == Snapshot.LastCommandSequence &&
                update.Clock.EventSequence == Snapshot.LastEventSequence && update.Clock.ActorId == Snapshot.CurrentActorId
                ? update.Clock : null;
            clockSampleTime = update.ClockSampleTime;
        }

        private async Task<Update> SendAsync(JObject command, ulong cursor, CancellationToken token, bool ready = false)
        {
            token.ThrowIfCancellationRequested();
            string after = "?after=" + cursor.ToString(CultureInfo.InvariantCulture);
            string suffix = ready ? "/ready" + after : command == null ? after : "/commands";
            using var web = new UnityWebRequest(url.AbsoluteUri + suffix,
                command == null ? UnityWebRequest.kHttpVerbGET : UnityWebRequest.kHttpVerbPOST)
            {
                downloadHandler = new DownloadHandlerBuffer(), timeout = 8, redirectLimit = 0
            };
            if (command != null)
            {
                web.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(command.ToString(Formatting.None)));
                web.SetRequestHeader("Content-Type", "application/json");
            }
            web.SetRequestHeader("Accept", "application/json");
            web.SetRequestHeader("Authorization", "Bearer " + bearer);
            double sentAt = Time.realtimeSinceStartupAsDouble;
            var operation = web.SendWebRequest();
            if (!operation.isDone)
            {
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Complete(AsyncOperation _) => completion.TrySetResult(true);
                operation.completed += Complete;
                try
                {
                    using var cancellation = token.Register(() => completion.TrySetCanceled(token));
                    await completion.Task;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { web.Abort(); throw; }
                finally { operation.completed -= Complete; }
            }
            token.ThrowIfCancellationRequested();
            if (web.responseCode is 401 or 403 or 404 or 410)
                throw new InvalidDataException("Battle room access has ended.");
            if (web.responseCode == 409)
            {
                JObject conflict = null;
                try { conflict = JObject.Parse(web.downloadHandler.text); }
                catch (JsonException) { }
                if ((string)conflict?["code"] is "battle_content_mismatch" or "battle_changed")
                    throw new InvalidDataException("The battle version changed.");
                if ((string)conflict?["code"] == "battle_not_started")
                    throw new InvalidDataException("The battle was cancelled or the seated player's lease expired.");
            }
            if (web.result != UnityWebRequest.Result.Success)
                throw new IOException("Battle service did not respond.");
            try
            {
                var update = Read(JObject.Parse(web.downloadHandler.text), command == null || ready, cursor);
                update.ClockSampleTime = (sentAt + Time.realtimeSinceStartupAsDouble) * .5d;
                if (ready)
                {
                    update.ReadySequence = (ulong)command["commandSequence"];
                    update.ReadyEventSequence = (ulong)command["eventSequence"];
                }
                return update;
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
            { throw new InvalidDataException("Battle service returned invalid data.", error); }
        }

        private Update Read(JObject root, bool hasContentHash, ulong cursor)
        {
            if (hasContentHash && !string.Equals((string)root["contentHash"], contentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Battle catalog changed while the match was active.");
            if (root["snapshot"] is not JObject state || (string)state["battleId"] != battleId ||
                (uint?)state["rulesVersion"] != BattlePreviewFactory.RulesVersion)
                throw new InvalidDataException("Battle identity or rules version changed.");
            if (state["units"] is not JArray rows || rows.Count != 8)
                throw new InvalidDataException("A draft battle must contain exactly eight combatants.");
            var units = rows.OfType<JObject>().Select(ReadUnit).ToArray();
            if (units.Length != 8 || units.Select(unit => unit.Slot).Distinct().Count() != 8 ||
                units.Select(unit => unit.InstanceId).Distinct(StringComparer.Ordinal).Count() != 8)
                throw new InvalidDataException("Battle formation contains duplicate or missing positions.");
            ulong last = (ulong)state["lastEventSequence"];
            var events = ((JArray)root["events"]).OfType<JObject>().Select(ReadEvent)
                .Where(item => item.Sequence > cursor).ToArray();
            bool gap = false;
            ulong next = cursor;
            foreach (var item in events)
            {
                if (item.Sequence != ++next) { gap = true; break; }
            }
            var outcome = Outcome(ReadEnum<BattleOutcome>(state["outcome"]));
            var actor = (string)state["currentActorId"];
            if (outcome == BattleOutcome.InProgress && !units.Any(unit => unit.InstanceId == actor && unit.IsAlive))
                throw new InvalidDataException("The current battle actor is unavailable.");
            var update = new Update
            {
                Snapshot = BattlePresentationWire.Snapshot(battleId, BattlePreviewFactory.RulesVersion,
                    (ulong)state["lastCommandSequence"], last, (int)state["turnNumber"], actor, outcome,
                    (int)state[localIsHost ? "allySoul" : "enemySoul"], (int)state[localIsHost ? "enemySoul" : "allySoul"], units),
                Reset = (bool?)root["snapshotReset"] == true || gap || next != last,
                Status = ReadOptional<BattleCommandStatus>(root["status"])
            };
            update.Events = update.Reset ? Array.Empty<BattleFlowEvent>() : events;
            if (root["clock"] is JObject clock)
            {
                update.Clock = clock.ToObject<BattleTurnClockView>();
                update.ClockUnavailable = update.Clock == null || (int?)clock["protocolVersion"] != BattleTurnClockView.CurrentProtocolVersion ||
                    clock["deadlineUnixMilliseconds"] == null || clock["commandSequence"] == null || clock["eventSequence"] == null ||
                    update.Clock.DecisionSeconds < 5 || update.Clock.DecisionSeconds > 120 ||
                    update.Clock.ServerNowUnixMilliseconds <= 0 || string.IsNullOrWhiteSpace(update.Clock.ActorId);
            }
            else update.ClockUnavailable = outcome == BattleOutcome.InProgress;
            return update;
        }

        private BattleUnitSnapshot ReadUnit(JObject row)
        {
            if (!heroes.TryGetValue((string)row["definitionId"], out var hero))
                throw new InvalidDataException("Battle service selected a hero absent from the client catalog.");
            var slot = row["slot"];
            var form = row["form"];
            return BattlePresentationWire.Unit((string)row["instanceId"], hero, (int)row["level"],
                ReadEnum<BattleElement>(row["element"]),
                new BattleSlot(Side(ReadEnum<BattleSide>(slot["side"])), (int)slot["position"]),
                new BattleUnitForm((string)form["id"], (string)form["presentationId"], form["skillIds"].Values<string>()),
                new BattleStats((int)row["stats"]["attack"], (int)row["stats"]["maxHealth"],
                    (int)row["stats"]["defense"], (int)row["stats"]["speed"], (int)row["stats"]["critChanceBp"],
                    (int)row["stats"]["critDamageBp"], (int)row["stats"]["effectivenessBp"], (int)row["stats"]["effectResistanceBp"]),
                (int)row["currentHealth"], (int)row["readinessBp"],
                ((JObject)row["cooldowns"]).Properties().ToDictionary(item => item.Name, item => (int)item.Value, StringComparer.Ordinal),
                ((JArray)row["statuses"]).OfType<JObject>().Select(ReadStatus).ToArray(), (int?)(row["resource"] as JObject)?["amount"] ?? 0,
                (int)row["injuryAmount"], (int)row["openingAttackBonus"]);
        }

        private static BattleStatusSnapshot ReadStatus(JObject row) => row == null ? null : BattlePresentationWire.Status(
            ReadEnum<BattleStatusKind>(row["kind"]), (int)row["remainingTurns"], (int)row["strength"],
            (string)row["sourceId"], (ulong)row["instanceId"]);

        private BattleFlowEvent ReadEvent(JObject row)
        {
            var side = ReadOptional<BattleSide>(row["side"]);
            var outcome = ReadOptional<BattleOutcome>(row["outcome"]);
            return BattlePresentationWire.Event((ulong)row["sequence"], (int)row["turnNumber"],
                ReadEnum<BattleFlowEventKind>(row["kind"]), (string)row["sourceId"], (string)row["targetId"], (int)row["amount"],
                (string)row["skillId"], (string)row["presentationId"], ReadEnum<BattleActionOrigin>(row["origin"]),
                ReadOptional<BattleStatusKind>(row["statusKind"]), ReadStatus(row["status"] as JObject),
                side.HasValue ? Side(side.Value) : null, outcome.HasValue ? Outcome(outcome.Value) : null,
                ReadEnum<BattleHitKind>(row["hitKind"]), ReadOptional<BattleEffectRejection>(row["effectRejection"]),
                (bool)row["useSoulBurn"], (int?)row["remainingHealth"], (int?)row["maximumHealth"]);
        }

        private BattleSide Side(BattleSide side) => localIsHost ? side :
            side == BattleSide.Ally ? BattleSide.Enemy : BattleSide.Ally;

        private BattleOutcome Outcome(BattleOutcome outcome) => localIsHost ? outcome : outcome switch
        {
            BattleOutcome.AllyVictory => BattleOutcome.EnemyVictory,
            BattleOutcome.EnemyVictory => BattleOutcome.AllyVictory,
            _ => outcome
        };

        private static T? ReadOptional<T>(JToken token) where T : struct, Enum =>
            token == null || token.Type == JTokenType.Null ? null : ReadEnum<T>(token);

        private static T ReadEnum<T>(JToken token) where T : struct, Enum =>
            token != null && Enum.TryParse<T>(token.ToString(), true, out var value) && Enum.IsDefined(typeof(T), value)
                ? value : throw new InvalidDataException("Unknown battle enum value.");

        private static void Observe(Task task)
        {
            if (task != null)
                _ = task.ContinueWith(failed => { _ = failed.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
            Observe(request);
            request = null;
        }
    }
}
