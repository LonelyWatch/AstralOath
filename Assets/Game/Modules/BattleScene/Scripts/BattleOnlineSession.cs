using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Shared.Battle;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

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
            internal bool PresentationPending;
            internal BattleTurnClockView Clock;
            internal double ClockSampleTime;
            internal int LatencyMilliseconds;
            internal bool ClockUnavailable;
            internal ulong? ReadySequence, ReadyEventSequence;
        }

        private const float PollInterval = .30f;
        private readonly Uri url;
        private readonly HttpClient http;
        private readonly string battleId, contentHash, battleContentHash, bearer;
        private readonly bool localIsHost;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Dictionary<string, BattleCatalogData.HeroRow> heroes;
        private Task<Update> request;
        private float nextPoll;
        private bool disposed;
        private ulong? readySequence, readyEventSequence;
        private double clockSampleTime;
        internal BattleSnapshot Snapshot { get; private set; }
        internal IReadOnlyList<BattleFlowEvent> OpeningArtifactEvents { get; private set; } = Array.Empty<BattleFlowEvent>();
        internal BattleOnlinePlayers Players { get; set; }
        internal bool Submitting { get; private set; }
        internal int? LatencyMilliseconds { get; private set; }
        internal bool Healthy { get; private set; } = true;
        internal bool Terminated { get; private set; }
        internal bool ClockUnavailable { get; private set; }
        internal BattleTurnClockView Clock { get; private set; }
        internal double ClockElapsed => Math.Max(0d, Time.realtimeSinceStartupAsDouble - clockSampleTime);
        internal bool IsOwnedTrial { get; }
        internal bool CanSubmit => Healthy && !Terminated && !Submitting && Snapshot?.Outcome == BattleOutcome.InProgress &&
            Snapshot.GetUnit(Snapshot.CurrentActorId)?.Slot.Side == BattleSide.Ally;

        internal BattleOnlineSession(Uri endpoint, string roomId, string battleId, string contentHash,
            string bearer, bool localIsHost, string trialBattleContentHash = null)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri) throw new ArgumentException("Invalid battle endpoint.");
            if (string.IsNullOrWhiteSpace(roomId) || string.IsNullOrWhiteSpace(battleId) ||
                string.IsNullOrWhiteSpace(contentHash) || string.IsNullOrWhiteSpace(bearer))
                throw new ArgumentException("Battle invitation is incomplete.");
            IsOwnedTrial = trialBattleContentHash != null;
            battleContentHash = trialBattleContentHash ?? contentHash;
            string room = Uri.EscapeDataString(roomId);
            string route = IsOwnedTrial ? "/api/battle-preview/rooms/" + room : "/api/arena/rooms/" + room + "/battle";
            if (IsOwnedTrial && roomId.StartsWith("tower-", StringComparison.Ordinal))
                route = "/api/arena/tower/battles/" + room;
            url = new Uri(endpoint, route);
            this.battleId = battleId;
            this.contentHash = contentHash;
            this.bearer = bearer;
            this.localIsHost = localIsHost;
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = !endpoint.IsLoopback });
        }

        internal async Task<(JObject Payload, double SentAt, double ReceivedAt)> FetchOpeningAsync(CancellationToken token)
        {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            return await FetchResponseAsync(null, 0, cancellation.Token);
        }

        internal void Open(TextAsset asset, BattleCatalogData catalog, (JObject Payload, double SentAt, double ReceivedAt) response)
        {
            using var hash = SHA256.Create();
            string canonical = asset.text.TrimStart('\ufeff').Replace("\r\n", "\n");
            string localHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "");
            if (!string.Equals(localHash, battleContentHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Client and server battle data differ.");
            heroes = catalog.heroes.ToDictionary(row => row.id, StringComparer.Ordinal);
            var update = Decode(response, null, 0, false);
            OpeningArtifactEvents = update.Events.Where(item => item.Kind == BattleFlowEventKind.ArtifactTriggered).ToArray();
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
                bool needsReady = !IsOwnedTrial && canReady && (readySequence != Snapshot.LastCommandSequence || readyEventSequence != Snapshot.LastEventSequence);
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
            LatencyMilliseconds = update.LatencyMilliseconds;
            ClockUnavailable = update.ClockUnavailable;
            if (ClockUnavailable) { Healthy = false; Terminated = true; }
            Clock = update.Clock != null && update.Clock.CommandSequence == Snapshot.LastCommandSequence &&
                update.Clock.EventSequence == Snapshot.LastEventSequence && update.Clock.ActorId == Snapshot.CurrentActorId
                ? update.Clock : null;
            clockSampleTime = update.ClockSampleTime;
        }

        private sealed class PresentationPendingException : Exception { }

        private async Task<Update> SendAsync(JObject command, ulong cursor, CancellationToken token, bool ready = false)
        {
            try { return Decode(await FetchResponseAsync(command, cursor, token, ready), command, cursor, ready); }
            catch (PresentationPendingException)
            {
                var latest = Decode(await FetchResponseAsync(null, cursor, token), null, cursor, false);
                latest.Status = BattleCommandStatus.InvalidAction;
                latest.PresentationPending = true;
                return latest;
            }
        }

        private async Task<(JObject Payload, double SentAt, double ReceivedAt)> FetchResponseAsync(
            JObject command, ulong cursor, CancellationToken token, bool ready = false)
        {
            token.ThrowIfCancellationRequested();
            string after = "?after=" + cursor.ToString(CultureInfo.InvariantCulture);
            string suffix = ready ? "/ready" + after : command == null ? after : "/commands";
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            using var request = new HttpRequestMessage(command == null ? HttpMethod.Get : HttpMethod.Post,
                new Uri(url.AbsoluteUri + suffix));
            if (command != null)
                request.Content = new StringContent(command.ToString(Formatting.None), Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

            double sentAt = Time.realtimeSinceStartupAsDouble;
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, deadline.Token);
                deadline.Token.ThrowIfCancellationRequested();
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
                    throw new InvalidDataException("Battle room access has ended.");

                string json = await response.Content.ReadAsStringAsync();
                deadline.Token.ThrowIfCancellationRequested();
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    JObject conflict = null;
                    try { conflict = JObject.Parse(json); }
                    catch (JsonException) { }
                    if (!ready && command != null && (string)conflict?["code"] == "battle_presentation_pending")
                        throw new PresentationPendingException();
                    if ((string)conflict?["code"] is "battle_content_mismatch" or "battle_changed")
                        throw new InvalidDataException("The battle version changed.");
                    if ((string)conflict?["code"] == "battle_not_started")
                        throw new InvalidDataException("The battle was cancelled or the seated player's lease expired.");
                }
                if (!response.IsSuccessStatusCode)
                    throw new IOException("Battle service did not respond.");
                double receivedAt = Time.realtimeSinceStartupAsDouble;
                try { return (JObject.Parse(json), sentAt, receivedAt); }
                catch (JsonException error) { throw new InvalidDataException("Battle service returned invalid data.", error); }
            }
            catch (OperationCanceledException error) when (!token.IsCancellationRequested)
            {
                throw new IOException("Battle service did not respond.", error);
            }
            catch (HttpRequestException error)
            {
                token.ThrowIfCancellationRequested();
                throw new IOException("Battle service did not respond.", error);
            }
        }

        private Update Decode((JObject Payload, double SentAt, double ReceivedAt) response, JObject command, ulong cursor, bool ready)
        {
            try
            {
                var update = Read(response.Payload, command == null || ready, cursor);
                update.ClockSampleTime = (response.SentAt + response.ReceivedAt) * .5d;
                update.LatencyMilliseconds = (int)Math.Round((response.ReceivedAt - response.SentAt) * 1000d);
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
            if (state["units"] is not JArray rows || (IsOwnedTrial ? rows.Count < 2 || rows.Count > 8 : rows.Count != 8))
                throw new InvalidDataException("Invalid battle formation size.");
            var units = rows.OfType<JObject>().Select(ReadUnit).ToArray();
            if (units.Length != rows.Count || units.Select(unit => unit.InstanceId).Distinct(StringComparer.Ordinal).Count() != units.Length ||
                !ValidTeam(BattleSide.Ally) || !ValidTeam(BattleSide.Enemy))
                throw new InvalidDataException("Battle formation contains duplicate or missing positions.");

            bool ValidTeam(BattleSide side)
            {
                var positions = units.Where(unit => unit.Slot.Side == side).Select(unit => (int)unit.Slot.Position)
                    .OrderBy(position => position).ToArray();
                return positions.Length >= 1 && positions.Length <= 4 &&
                    positions.SequenceEqual(Enumerable.Range(1, positions.Length));
            }
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
            else update.ClockUnavailable = !IsOwnedTrial && outcome == BattleOutcome.InProgress;
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
                (int)row["injuryAmount"], (int)row["openingAttackBonus"], (bool?)row["isExtinct"] ?? false,
                (bool?)row["lethalProtectionConsumed"] ?? false);
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
                (bool)row["useSoulBurn"], (int?)row["remainingHealth"], (int?)row["maximumHealth"],
                (bool?)row["immortalityPreventedLethalDamage"] ?? false, (int?)row["cooldownReduction"] ?? 0);
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
            http.Dispose();
            lifetime.Dispose();
            Observe(request);
            request = null;
        }
    }
}
