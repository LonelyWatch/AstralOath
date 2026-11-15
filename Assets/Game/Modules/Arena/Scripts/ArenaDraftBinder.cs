using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDraftBinder
    {
        private const string RecentKey = "arena.draft.recent";
        private static readonly string[] Empty = Array.Empty<string>();
        private readonly ArenaDraft view;
        private readonly ArenaApi api;
        private readonly ILocaleService locale;
        private readonly IAssetService assets;
        private readonly ISceneAssetService scenes;
        private readonly Func<Task> returnToRoom;
        private readonly ArenaDraftRosterFilter rosterFilter;
        private static readonly Vector2 RosterCardSize = new Vector2(361f, 112f);
        private Choice[] visibleRoster = Array.Empty<Choice>();
        private float lastRosterOffset = float.NaN, lastRosterWidth, lastRosterHeight, lastRosterStep;
        private int? localLatency;
        private int lastRosterFocus = -1;
        private ArenaDraftRosterGesture rosterGesture;
        private float observedRosterOffset, rosterMovedAt, snapFrom, snapTime;
        private int snapIndex = -1, previewSequence;
        private bool rosterMoving;
        private readonly Dictionary<string, ArenaDraftArt.Hero> art;
        private readonly Sprite[] scoreDigits;
        private readonly ArenaDraftArt artwork;
        private readonly RectTransform gridViewport, recentGrid;
        private readonly GridLayoutGroup prebanLayout, recentLayout;
        private readonly GameText timerText, preTimerText;
        private readonly Image[] digits, preDigits;
        private int lastSecond = int.MinValue;
        private int lastPickCount = -1, pickWindowSeconds = 30;
        private string lastPickSide;
        private readonly List<Choice> roster = new List<Choice>(), grid = new List<Choice>(),
            selected = new List<Choice>(), revealedOwn = new List<Choice>(), revealedEnemy = new List<Choice>(), recent = new List<Choice>();
        private readonly List<Choice> revealCards = new List<Choice>();
        private float revealUntil;
        private readonly List<string> prebans = new List<string>();
        private readonly Choice[] left = new Choice[5], right = new Choice[5], positions = new Choice[4];
        private readonly ArenaDraftPortrait[] modelSurfaces = new ArenaDraftPortrait[4];
        private readonly ArenaDraftPortrait feature, banFeedback;
        private readonly ArenaDraftPortrait[] blueFeedback = new ArenaDraftPortrait[5], redFeedback = new ArenaDraftPortrait[5];
        private readonly Dictionary<string, Texture2D> particleTextures;
        private static readonly int[] NativeSlotWidths = { 291, 226, 196, 196, 226 };
        private float lastPickFeedbackUntil;
        private bool banIntroPlaying, revealBanCards;
        private readonly string[] shownFormation = new string[4];

        private BindingScope scope;
        private BattleOnlineLaunch.Preparation battlePreload;
        private ArenaDraftState draft;
        private string candidate, featured, featuredBan, loadedBattle;
        private string[] formation = Empty;
        private int swapIndex;
        private bool sending, launching;
        private DateTimeOffset deadline;

        public ArenaDraftBinder(ArenaDraft view, ArenaApi api, ILocaleService locale, ArenaDraftArt artwork,
            WindowCoordinator windows, IAssetService assets, ISceneAssetService scenes, Func<Task> returnToRoom)
        {
            this.view = view;
            this.api = api;
            this.locale = locale;
            this.assets = assets;
            this.scenes = scenes;
            this.returnToRoom = returnToRoom;
            rosterFilter = new ArenaDraftRosterFilter(view, locale, windows, ApplySearch);
            this.artwork = artwork;
            art = artwork.Heroes.ToDictionary(hero => hero.Id, StringComparer.Ordinal);
            scoreDigits = artwork.ScoreDigits;
            gridViewport = view.Scroll("grid").viewport;
            prebanLayout = view.Scroll("grid").content.GetComponent<GridLayoutGroup>();
            recentGrid = (RectTransform)view.Node("recent").transform;
            recentLayout = recentGrid.GetComponent<GridLayoutGroup>();
            timerText = view.Label("timer");
            preTimerText = view.Label("preTimer");
            digits = Clock("timer");
            preDigits = Clock("preTimer");

            Image[] Clock(string key) => new[] { view.Picture(key + "Tens"), view.Picture(key + "Units"), view.Picture(key + "Single") };
            feature = new ArenaDraftPortrait(view.Surface("featured"));
            var banSurface = view.Surface("banFeedback");
            banSurface.color = Color.white;
            banFeedback = new ArenaDraftPortrait(banSurface,
                nativeEffectSize: new Vector2(1040, 620) * 2.60f, centralBanLight: true);
            particleTextures = artwork.ParticleTextures.ToDictionary(texture => texture.name, StringComparer.Ordinal);
            for (int index = 0; index < 5; index++)
            {
                var blue = view.Card("blue", index);
                var red = view.Card("red", index);
                left[index] = new Choice(blue, artwork.LevelDigits, teamSlot: true, protectedSlot: index == 2);
                right[index] = new Choice(red, artwork.LevelDigits, teamSlot: true, protectedSlot: index == 2);
                // The rendered gutter lets the original additive glows and sparks fade beyond the card.
                float width = NativeSlotWidths[index] + 208;
                var effectSize = new Vector2(width, 293) * 2.24f;
                blueFeedback[index] = new ArenaDraftPortrait(blue.Require<RawImage>("confirm"),
                    nativeEffectSize: effectSize, blendCardLight: true);
                // The source outline extends past the card. Fade the wide glow into the
                // roster rather than cutting off the outline at the edge of the card.
                var rightFade = new Vector2((width - 91) / width, (width - 43) / width);
                redFeedback[index] = new ArenaDraftPortrait(red.Require<RawImage>("confirm"),
                    nativeEffectSize: effectSize, effectRightFade: rightFade, blendCardLight: true);
            }
            for (int index = 0; index < 4; index++)
            {
                var card = view.Card("formation", index);
                positions[index] = new Choice(card, miniature: true);
                positions[index].Model = card.Require<RawImage>("model");
                modelSurfaces[index] = new ArenaDraftPortrait(positions[index].Model, miniature: true);
            }
        }

        public Task OpenAsync(ArenaDraftState initial, BindingScope owner)
        {
            scope = owner;
            draft = null;
            candidate = featured = featuredBan = loadedBattle = null;
            formation = Empty;
            swapIndex = -1;
            revealUntil = lastPickFeedbackUntil = 0f;
            sending = launching = banIntroPlaying = revealBanCards = false;
            localLatency = null;
            prebans.Clear();
            lastSecond = int.MinValue;
            lastPickCount = -1;
            lastPickSide = null;
            view.Show();
            view.FitLayout();
            owner.Add(Close);
            view.Label("status").text = "";
            view.Search("search").SetTextWithoutNotify("");
            view.Search("preSearch").SetTextWithoutNotify("");
            var rosterScroll = view.Scroll("roster");
            rosterScroll.StopMovement();
            rosterScroll.content.anchoredPosition = Vector2.zero;
            if (!rosterScroll.TryGetComponent(out rosterGesture))
                rosterGesture = rosterScroll.gameObject.AddComponent<ArenaDraftRosterGesture>();
            rosterGesture.ResetDrag();
            previewSequence = 0;
            ResetRosterMotion(rosterScroll);
            WatchSearch("search");
            WatchSearch("preSearch");
            Listen(view.Button("action"), SubmitAsync);
            Listen(view.Button("preAction"), SubmitAsync);
            rosterFilter.Bind(owner);
            for (int index = 0; index < 5; index++)
            {
                int slot = index;
                Listen(left[index].Button, () => { SelectTeamHero(left[slot].Id, false); return Task.CompletedTask; });
                Listen(right[index].Button, () => { SelectTeamHero(right[slot].Id, true); return Task.CompletedTask; });
            }
            for (int index = 0; index < positions.Length; index++)
            {
                int slot = index;
                Listen(positions[index].Button, () => { Swap(slot); return Task.CompletedTask; });
            }
            foreach (string id in initial.eligibleHeroIds ?? Empty)
            {
                if (!art.ContainsKey(id)) continue;
                AddHero(roster, "row", view.Scroll("roster").content, id);
                AddHero(grid, "gridCell", view.Scroll("grid").content, id);
            }
            foreach (string id in PlayerPrefs.GetString(RecentKey, "").Split(',').Where(art.ContainsKey).Take(8))
                AddHero(recent, "gridCell", view.Node("recent").transform, id);

            Adopt(initial);
            _ = WatchAsync(initial.roomId, owner.Token);
            _ = AnimateAsync(owner.Token);
            return Task.CompletedTask;
        }

        private void Tick(float seconds)
        {
            if (scope == null || launching) return;
            if (revealUntil > 0f && Time.unscaledTime >= revealUntil)
            {
                revealUntil = 0f;
                Release(revealCards);
                Render();
            }
            view.FitLayout();
            UpdateTimer();
            LayoutRoster();
            UpdateRosterSnap(seconds);
            FitGrid(prebanLayout, gridViewport.rect.width);
            FitGrid(recentLayout, recentGrid.rect.width);
            feature.Tick(seconds);
            foreach (var model in modelSurfaces) model.Tick(seconds);
            foreach (var effect in blueFeedback) effect.Tick(seconds);
            foreach (var effect in redFeedback) effect.Tick(seconds);
            banFeedback.Tick(seconds);
            if (lastPickFeedbackUntil > 0f && Time.unscaledTime >= lastPickFeedbackUntil)
            {
                lastPickFeedbackUntil = 0f;
                if (draft.phase == "ban" && candidate == null) Render();
            }
            if (banIntroPlaying && !banFeedback.IsVisible)
            {
                banIntroPlaying = false;
                if (draft.phase == "ban" && draft.Own.submitted)
                    banFeedback.Show(artwork.FinalBan.MakeSource(), animation: "loop");
                Render();
            }
        }

        private async Task AnimateAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                Tick(Time.unscaledDeltaTime);
                await Task.Yield();
            }
        }

        private async Task WatchAsync(string roomId, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (draft.phase == "cancelled") { await returnToRoom(); return; }
                    if (draft.phase == "battle" && loadedBattle != draft.battleId)
                    {
                        await LaunchAsync(token);
                        if (loadedBattle == draft.battleId) return;
                    }
                    await Task.Delay(650, token);
                    try
                    {
                        double started = Time.realtimeSinceStartupAsDouble;
                        var next = await api.DraftAsync(roomId, token);
                        localLatency = Mathf.RoundToInt((float)(Time.realtimeSinceStartupAsDouble - started) * 1000);
                        if (next == null) { await returnToRoom(); return; }
                        Adopt(next);
                    }
                    catch (ArenaRequestException error) when (!token.IsCancellationRequested)
                    {
                        if (error.Code is "not_in_room" or "room_not_found" or "draft_not_seated")
                        { await returnToRoom(); return; }
                        localLatency = null;
                        ShowConnection();
                        view.Label("status").text = ArenaError.Text(locale, error);
                        await Task.Delay(900, token);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (!token.IsCancellationRequested) view.Label("status").text = L("arena.error.request_failed");
            }
        }

        private async Task LaunchAsync(CancellationToken token)
        {
            if (launching || string.IsNullOrEmpty(draft.battleId)) return;
            launching = true;
            Render();
            var match = draft;
            var prepared = battlePreload;
            battlePreload = null;
            try
            {
                var own = match.Own.player;
                var opponent = match.Opponent.player;
                var identities = new BattleOnlinePlayers(own.displayName, opponent.displayName, own.avatarId, opponent.avatarId,
                    GetArt(own.avatarId)?.Face, GetArt(opponent.avatarId)?.Face);
                await BattleOnlineLaunch.LoadAsync(assets, scenes, api.Endpoint, match.roomId, match.battleId, match.contentHash,
                    api.Bearer, match.IsHost, token, identities, prepared);
                loadedBattle = match.battleId;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { prepared?.Dispose(); }
            catch (Exception error)
            {
                prepared?.Dispose();
                Debug.LogException(error);
                if (!token.IsCancellationRequested) view.Label("status").text = L("arena.draft.launch_failed");
                await Task.Delay(2500, token);
            }
            finally { launching = false; }
        }

        private void Adopt(ArenaDraftState next)
        {
            if (draft != null && (next.id != draft.id || next.version < draft.version)) return;
            if (draft != null && next.version == draft.version)
            {
                if (next.previewSequence <= draft.previewSequence) return;
                draft.previewSequence = next.previewSequence;
                draft.previewHeroId = next.previewHeroId;
                if (!draft.OwnTurn && draft.phase == "pick") Render();
                return;
            }
            previewSequence = next.previewSequence;
            string previousPhase = draft?.phase;
            string previousBan = draft?.Own.ban;
            bool changedPhase = previousPhase != next.phase;
            bool revealPreban = previousPhase == "preban" && next.phase == "pick";
            int priorHostPicks = Count(draft?.host?.picks);
            int priorGuestPicks = Count(draft?.guest?.picks);
            draft = next;
            deadline = DateTimeOffset.TryParse(draft.deadlineAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var end) ? end : default;
            if (changedPhase)
            {
                candidate = null;
                swapIndex = -1;
                if (scope != null && battlePreload == null && draft.phase is "pick" or "ban" or "formation")
                    battlePreload = BattleOnlineLaunch.BeginPreload(assets, scenes, scope.Token);
                if (draft.phase == "formation") formation = DraftFormation();
            }
            bool hostPicked = previousPhase == "pick" && Count(draft.host.picks) > priorHostPicks;
            bool guestPicked = previousPhase == "pick" && Count(draft.guest.picks) > priorGuestPicks;
            if (battlePreload != null && (changedPhase || hostPicked || guestPicked) &&
                draft.phase is "pick" or "ban" or "formation")
            {
                // Only the public, locked selections supply model IDs. A highlighted candidate
                // or an unacknowledged click must never affect the warmed roster.
                string ownBan = draft.phase == "formation" ? draft.Opponent.ban : null;
                string enemyBan = draft.phase == "formation" ? draft.Own.ban : null;
                battlePreload.WarmRoster((draft.Own.picks ?? Empty).Where(id => !string.IsNullOrEmpty(id) && id != ownBan)
                    .Concat((draft.Opponent.picks ?? Empty).Where(id => !string.IsNullOrEmpty(id) && id != enemyBan)));
            }
            if (hostPicked || guestPicked)
            {
                candidate = null;
                bool showHost = hostPicked && (!guestPicked || draft.activeSide != "host");
                featured = (showHost ? draft.host.picks : draft.guest.picks).Last();
                if (draft.phase == "ban") lastPickFeedbackUntil = Time.unscaledTime + .30f;
            }
            if (previousPhase == "ban" && draft.Own.ban != null && previousBan != draft.Own.ban)
                banIntroPlaying = PlayEffect(banFeedback, artwork.FinalBan, "intro", .10f, 7f);
            else if (previousPhase == null && draft.phase == "ban" && draft.Own.submitted)
                banFeedback.Show(artwork.FinalBan.MakeSource(), animation: "loop");
            if (previousPhase == "ban" && draft.phase == "formation") revealBanCards = true;
            if (changedPhase && draft.phase != "ban" && !banIntroPlaying)
                banFeedback.Clear();
            if (draft.phase == "preban" && draft.Own.submitted)
            {
                prebans.Clear();
                prebans.AddRange(draft.Own.prebans ?? Empty);
            }
            if (draft.phase == "formation" && draft.Own.submitted && Count(draft.Own.formation) == 4)
                formation = draft.Own.formation.ToArray();

            SyncChips(selected, "preSelected", prebans);
            string[] banIds = (draft.Own.prebans ?? Empty).Concat(draft.Opponent.prebans ?? Empty).Distinct().ToArray();
            SyncChips(revealedOwn, "revealedOwn", draft.Own.prebans ?? Empty);
            SyncChips(revealedEnemy, "revealedEnemy", draft.Opponent.prebans ?? Empty);
            if (revealPreban && banIds.Length > 0)
            {
                revealUntil = Time.unscaledTime + .83f;
                SyncChips(revealCards, "revealHeroes", banIds);
                foreach (var card in revealCards)
                {
                    card.Button.interactable = false;
                    card.Mark.enabled = true;
                }
            }
            view.Label("status").text = "";
            Render();
            if (hostPicked || guestPicked) feature.PlaySelectionFeedback();
            if (hostPicked) PlayPicks(draft.IsHost, priorHostPicks, draft.host.picks.Length);
            if (guestPicked) PlayPicks(!draft.IsHost, priorGuestPicks, draft.guest.picks.Length);
        }

        private void PlayPicks(bool own, int first, int count)
        {
            var targets = own ? blueFeedback : redFeedback;
            var effects = own ? artwork.BluePick : artwork.RedPick;
            string animation = own ? "blue_set" : "red_set";
            for (int index = first; index < Mathf.Min(count, targets.Length); index++)
                PlayEffect(targets[index], effects[index], animation);
        }

        private bool PlayEffect(ArenaDraftPortrait target, ArenaDraftArt.NativeEffect effect, string animation,
            float particleDelay = 0f, float particleRotation = 0f, RectTransform placement = null, float scale = 1f)
        {
            if (!target.PlayOneShot(effect.MakeSource(), animation, placement, scale)) return false;
            target.AddParticles(effect.Particles, particleTextures, particleDelay, particleRotation);
            return true;
        }

        private void PlayBanCard(Choice[] team, ArenaDraftPortrait[] effects, string bannedHero)
        {
            int index = Array.FindIndex(team, card => card.Id == bannedHero && card.Mark.enabled);
            if (index >= 0)
                PlayEffect(effects[index], artwork.FinalBan, "intro", .10f, 7f, team[index].Mark.rectTransform, .30f);
        }

        private string[] DraftFormation() => Count(draft.Own.formation) == 4
            ? draft.Own.formation.ToArray()
            : (draft.Own.picks ?? Empty).Where(id => id != draft.Opponent.ban).ToArray();

        private void Render()
        {
            bool pre = draft.phase == "preban";
            bool finishingBan = draft.phase == "formation" && banIntroPlaying;
            bool arrange = !finishingBan && (draft.phase == "formation" || draft.phase == "battle");
            bool showingReveal = revealUntil > 0f;
            bool canAct = !sending && !launching && !showingReveal && !finishingBan && !draft.Own.submitted;
            view.Node("reveal").SetActive(showingReveal);
            bool pick = draft.phase == "pick";
            bool ban = draft.phase == "ban" || finishingBan;
            if (pick) canAct &= draft.OwnTurn;
            view.Node("preban").SetActive(pre);
            view.Node("formation").SetActive(arrange);
            view.Node("preHeader").SetActive(!pre && (revealedOwn.Count + revealedEnemy.Count) > 0);
            bool filtersAllowed = !pre && !showingReveal && draft.phase != "battle";
            view.Button("openRosterFilters").interactable = filtersAllowed;
            if (!filtersAllowed) rosterFilter.Close();
            var own = draft.Own.player;
            var opponent = draft.Opponent.player;
            ArenaArt.SetText(view.Label("host"), own.displayName);
            ArenaArt.SetText(view.Label("guest"), opponent.displayName);
            SetAvatar(view.Avatar("host"), own);
            SetAvatar(view.Avatar("guest"), opponent);
            ShowConnection();
            view.Label("title").text = L(pick ? draft.OwnTurn ? "arena.draft.own_turn" : "arena.draft.other_turn"
                : ban ? "arena.draft.ban" : "arena.draft." + draft.phase);
            view.Label("title").color = pick && !draft.OwnTurn ? new Color32(254, 30, 30, 255) : new Color32(68, 148, 255, 255);
            view.Label("hint").text = L(pick ? "arena.draft.pick_hint" : ban ? "arena.draft.ban_hint"
                : arrange ? "arena.draft.formation_hint" : "arena.draft.preban_hint");
            view.Label("preCount").text = GameText.Format(locale, "arena.draft.preban_count", prebans.Count, draft.prebanCount);
            RenderActions(canAct, pick, ban, arrange, finishingBan);
            view.Picture("blue").enabled = !pick || draft.OwnTurn;
            view.Picture("red").enabled = !pick || !draft.OwnTurn;
            view.Picture("banMark").enabled = ban && !banFeedback.IsVisible;
            view.Picture("banGlow").enabled = ban;
            // Keep the final-ban instruction below the stamp and its brightest sparks.
            var hintRect = view.Label("hint").rectTransform;
            hintRect.anchorMin = new Vector2(.285f, ban ? .139f : arrange ? .161f : .244f);
            hintRect.anchorMax = new Vector2(.713f, ban ? .206f : arrange ? .234f : .317f);
            hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;

            RenderTeam(left, draft.Own, draft.Opponent, pick && draft.OwnTurn, false, ban);
            RenderTeam(right, draft.Opponent, draft.Own, pick && !draft.OwnTurn, true, ban);
            foreach (var choice in roster)
            {
                bool valid = CanSelect(choice.Id);
                choice.Button.interactable = pick && valid && canAct && draft.OwnTurn;
                choice.Selection.enabled = choice.Id == candidate && pick;
                bool taken = Used(choice.Id);
                bool banned = IsPrebanned(choice.Id);
                choice.SetState(taken ? L("arena.draft.picked") :
                    banned ? L("arena.draft.banned") : null, banned);
            }
            bool canEditPrebans = pre && !draft.Own.submitted && !sending;
            foreach (var choice in grid.Concat(recent))
            {
                choice.SetPrebanMark(pre && prebans.Contains(choice.Id));
                choice.Button.interactable = canEditPrebans;
            }
            foreach (var choice in selected) choice.Button.interactable = canEditPrebans;
            foreach (var choice in revealedOwn.Concat(revealedEnemy)) choice.Button.interactable = false;

            if (arrange) RenderFormation(canAct);
            if (arrange && draft.phase == "formation" && revealBanCards)
            {
                revealBanCards = false;
                PlayBanCard(left, blueFeedback, draft.Opponent.ban);
                PlayBanCard(right, redFeedback, draft.Own.ban);
            }
            string activeCandidate = draft.OwnTurn ? candidate : draft.previewHeroId;
            string portraitId = pre || arrange ? null : ban
                ? candidate ?? draft.Own.ban ?? (lastPickFeedbackUntil > 0f ? featured : null)
                : !string.IsNullOrEmpty(activeCandidate) ? activeCandidate
                : featured ?? draft.Own.picks?.LastOrDefault() ?? draft.Opponent.picks?.LastOrDefault();
            SetFeatured(portraitId, ban && (candidate != null || draft.Own.ban != null));
            UpdateTimer();
            ApplySearch();
        }

        private void RenderActions(bool canAct, bool pick, bool ban, bool arrange, bool finishingBan)
        {
            var action = view.Button("action");
            var label = view.Label("actionLabel");
            bool waiting = draft.Own.submitted || finishingBan || (pick && !draft.OwnTurn);
            bool enabled = canAct && (pick || ban ? CanSelect(candidate) : draft.phase == "formation" && formation.Length == 4);
            label.text = L(launching ? "arena.draft.entering" : waiting ? "arena.draft.waiting"
                : arrange ? "arena.start" : ban ? "arena.draft.complete" : "arena.draft.choose");
            var position = label.rectTransform.anchoredPosition;
            position.x = pick ? -22f : 0f;
            label.rectTransform.anchoredPosition = position;
            var arrow = view.Picture("actionArrow");
            arrow.enabled = pick;
            arrow.color = new Color(.42f, .76f, .106f, enabled ? 1f : .28f);
            ((Image)action.targetGraphic).sprite = ban ? artwork.ActionRed : artwork.ActionGreen;
            var states = action.spriteState;
            states.pressedSprite = ban ? artwork.ActionRedPressed : artwork.ActionGreenPressed;
            action.spriteState = states;
            SetActionState(action, label, enabled, pick && waiting
                ? new Color(.62f, .62f, .62f, .29f) : new Color(.48f, .48f, .48f, .55f));

            var preAction = view.Button("preAction");
            var preLabel = view.Label("preActionLabel");
            preLabel.text = L("arena.draft.choose");
            SetActionState(preAction, preLabel, canAct && prebans.Count == draft.prebanCount,
                new Color(.34f, .34f, .34f, .96f));
        }

        private static void SetActionState(GameButton button, GameText label, bool enabled, Color disabled)
        {
            button.interactable = enabled;
            button.targetGraphic.color = enabled ? Color.white : disabled;
            label.color = enabled ? Color.white : new Color32(132, 126, 130, 255);
        }

        private void ShowConnection()
        {
            view.Node("hostPing").SetActive(localLatency.HasValue);
            int level = localLatency < 120 ? 3 : localLatency < 250 ? 2 : 1;
            for (int i = 1; i <= 3; i++) view.Picture("hostPing" + i).enabled = i == level;
        }

        private void SetAvatar(BindingMap avatar, ArenaPlayer player)
        {
            var portrait = avatar.Require<Image>("portrait");
            var hero = GetArt(player.avatarId);
            if (hero != null) portrait.sprite = hero.Face;
            ArenaArt.SetAvatar(avatar, player, false);
        }

        private void RenderTeam(Choice[] team, ArenaDraftSeat seat, ArenaDraftSeat opponent, bool active, bool enemy, bool ban)
        {
            int taken = Count(seat.picks);
            for (int index = 0; index < team.Length; index++)
            {
                string id = index < taken ? seat.picks[index] : null;
                var item = team[index];
                string preview = active && index == taken && draft.picksRemainingInWindow > 0
                    ? enemy ? draft.previewHeroId : candidate
                    : null;
                var hero = GetArt(id ?? preview);
                bool provisional = id == null && hero != null;
                item.Set(id, hero, asCard: true, tags: true, levelLabel: true);
                item.SetProvisional(provisional);
                bool protect = id != null && id == seat.protectedHeroId;
                bool isBanned = id != null && !ban && (draft.phase is "formation" or "battle") && id == opponent.ban;
                bool highlightedBan = ban && enemy && id != null && id == (candidate ?? draft.Own.ban);
                item.Button.interactable = id != null && (!ban || (enemy && CanSelect(id) && !sending));
                item.Pending.enabled = active && index >= taken && index < taken + draft.picksRemainingInWindow;
                item.Selection.enabled = false;
                bool showSlotProtection = draft.banProtection && index == 2 && hero == null;
                item.Guard.enabled = showSlotProtection;
                if (item.GuardPlate)
                {
                    item.FilledGuard.enabled = protect;
                    item.GuardPlate.enabled = item.GuardCaption.enabled = showSlotProtection;
                    item.GuardCaption.text = L("arena.draft.safeguard");
                }
                item.Mark.enabled = isBanned;
                item.SetState(null, isBanned || highlightedBan);
                item.SetBanTint(isBanned || highlightedBan);
                ((Image)item.Button.targetGraphic).overrideSprite = id == null ? null : item.Filled.sprite;
            }
        }

        private void RenderFormation(bool canAct)
        {
            for (int index = 0; index < positions.Length; index++)
            {
                var item = positions[index];
                string id = index < formation.Length ? formation[index] : null;
                var hero = GetArt(id);
                item.Set(id, hero, asCard: false, tags: true);
                item.SetFormationLevel(hero);
                item.Button.interactable = draft.phase == "formation" && canAct && hero != null;
                item.Selection.enabled = index == swapIndex;
                item.Change.enabled = canAct && swapIndex >= 0 && swapIndex != index;
                if (shownFormation[index] == id) { if (hero != null) item.Portrait.enabled = !item.Model.enabled; continue; }
                shownFormation[index] = id;
                if (hero == null) { modelSurfaces[index].Clear(); continue; }
                item.Portrait.enabled = !modelSurfaces[index].Show(hero.MakeModel());
            }
        }

        private void SetFeatured(string id, bool ban)
        {
            string filter = ban ? "ban" : "";
            if (string.IsNullOrEmpty(id)) id = "";
            if (featuredBan == id + filter) return;
            featuredBan = id + filter;
            var hero = GetArt(id);
            var fallback = view.Picture("featuredFallback");
            var surface = view.Surface("featured");
            Color tone = ban ? new Color(.43f, .43f, .43f, 1) : Color.white;
            surface.color = fallback.color = tone;
            if (hero == null)
            {
                feature.Clear();
                fallback.enabled = false;
                return;
            }
            fallback.sprite = hero.Illustration ? hero.Illustration : hero.Card;
            fallback.enabled = !feature.Show(hero.MakePortrait());
            feature.SetDesaturation(ban ? .72f : 0f);
        }

        private void Select(string id)
        {
            if (!CanSelect(id) || sending || (draft.phase == "pick" && !draft.OwnTurn)) return;
            if (draft.phase == "preban")
            {
                if (!prebans.Remove(id))
                {
                    if (prebans.Count == draft.prebanCount) { GameUiFeedback.Play(view.Button("preAction"), GameUiFeedback.Sound.Blocked); return; }
                    prebans.Add(id);
                }
                SyncChips(selected, "preSelected", prebans);
            }
            else
            {
                candidate = id; featured = id;
                int selectedIndex = Array.FindIndex(visibleRoster, row => row.Id == id);
                if (draft.phase == "pick" && selectedIndex >= 0)
                {
                    var scroll = view.Scroll("roster");
                    scroll.StopMovement();
                    scroll.content.anchoredPosition = new Vector2(0, selectedIndex * RosterStep);
                    ResetRosterMotion(scroll);
                    PublishPreview(id);
                }
            }
            Render();
        }

        private void SelectTeamHero(string id, bool enemy)
        {
            if (id == null) return;
            if (enemy && draft.phase == "ban") { Select(id); return; }
            if (draft.phase == "pick" && candidate == null) { featured = id; Render(); }
        }

        private bool CanSelect(string id)
        {
            if (string.IsNullOrEmpty(id) || draft == null || draft.Own.submitted) return false;
            return draft.phase switch
            {
                "preban" => true,
                "pick" => !Used(id) && !IsPrebanned(id),
                "ban" => (draft.Opponent.picks ?? Empty).Contains(id) && draft.Opponent.protectedHeroId != id,
                _ => false
            };
        }

        private bool IsPrebanned(string id) => (draft.Own.prebans ?? Empty).Contains(id) ||
            (draft.Opponent.prebans ?? Empty).Contains(id);

        private bool Used(string id) => (draft.Own.picks ?? Empty).Contains(id) || (draft.Opponent.picks ?? Empty).Contains(id);

        private void Swap(int index)
        {
            if (sending || draft.Own.submitted || index >= formation.Length) return;
            if (swapIndex < 0) swapIndex = index;
            else
            {
                (formation[swapIndex], formation[index]) = (formation[index], formation[swapIndex]);
                swapIndex = -1;
            }
            Render();
        }

        private async Task SubmitAsync()
        {
            if (sending || launching || draft.Own.submitted || (draft.phase == "formation" && banIntroPlaying)) return;
            string phase = draft.phase;
            var ids = phase switch
            {
                "preban" when prebans.Count == draft.prebanCount => prebans.ToArray(),
                "pick" when draft.OwnTurn && CanSelect(candidate) => new[] { candidate },
                "ban" when CanSelect(candidate) => new[] { candidate },
                "formation" when formation.Length == 4 => formation.ToArray(),
                _ => null
            };
            if (ids == null) return;
            var owner = scope;
            var action = new ArenaDraftAction { requestId = Guid.NewGuid().ToString("N"), draftId = draft.id,
                kind = phase, heroIds = ids, version = draft.version };
            sending = true;
            Render();
            try
            {
                var next = await api.SubmitDraftAsync(draft.roomId, action, owner.Token);
                owner.Token.ThrowIfCancellationRequested();
                if (phase == "preban" && next != null)
                {
                    var history = ids.Concat(PlayerPrefs.GetString(RecentKey, "").Split(','))
                        .Where(art.ContainsKey).Distinct().Take(8);
                    PlayerPrefs.SetString(RecentKey, string.Join(",", history));
                }
                if (phase == "pick") candidate = null;
                Adopt(next ?? throw new ArenaRequestException("invalid_response"));
            }
            catch (OperationCanceledException) when (owner.Token.IsCancellationRequested) { }
            catch (ArenaRequestException error)
            {
                if (!owner.Token.IsCancellationRequested) view.Label("status").text = ArenaError.Text(locale, error);
            }
            finally
            {
                if (scope == owner) { sending = false; Render(); }
            }
        }

        private void UpdateTimer()
        {
            float now = Time.unscaledTime;
            float pulse = Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.Repeat(now, 1f) * 2f - 1f));
            foreach (var card in left) card.AnimatePick(pulse);
            foreach (var card in right) card.AnimatePick(pulse);
            if (draft?.phase == "preban")
            {
                foreach (var card in grid) card.AnimatePreban(now);
                foreach (var card in recent) card.AnimatePreban(now);
            }

            double remaining = deadline == default ? -1 : (deadline - DateTimeOffset.UtcNow).TotalSeconds;
            int picksTaken = Count(draft?.Own.picks) + Count(draft?.Opponent.picks);
            // Opening and closing single picks use 30 seconds; the shared two-pick window is 60.
            if (draft?.phase == "pick" && picksTaken != lastPickCount)
            {
                if (draft.activeSide != lastPickSide || lastPickCount < 0)
                    pickWindowSeconds = picksTaken == 0 || picksTaken == 9 ? 30 : 60;
                lastPickCount = picksTaken;
                lastPickSide = draft.activeSide;
            }
            float fraction = remaining < 0 ? 0 : Mathf.Clamp01((float)(remaining / (draft?.phase == "pick" ? pickWindowSeconds : 30)));
            view.Picture("blue").fillAmount = view.Picture("red").fillAmount = fraction;
            int seconds = deadline == default ? -1 : Math.Max(0, (int)Math.Ceiling(remaining));
            if (seconds == lastSecond) return;
            lastSecond = seconds;
            timerText.text = preTimerText.text = seconds < 0 ? "" : seconds.ToString(CultureInfo.InvariantCulture);
            SetClock(digits, seconds);
            SetClock(preDigits, seconds);
        }

        private void SetClock(Image[] clock, int seconds)
        {
            bool doubleDigit = seconds >= 10;
            SetDigit(clock[0], doubleDigit ? Math.Min(9, seconds / 10) : -1);
            SetDigit(clock[1], doubleDigit ? seconds % 10 : -1);
            SetDigit(clock[2], doubleDigit ? -1 : seconds);
        }

        private void SetDigit(Image image, int digit)
        {
            image.enabled = digit >= 0 && digit < scoreDigits.Length;
            if (image.enabled) image.sprite = scoreDigits[digit];
        }

        private static void FitGrid(GridLayoutGroup layout, float width)
        {
            if (width <= 0f || !layout.gameObject.activeInHierarchy) return;
            var columns = layout.constraintCount;
            float cell = (width - (columns - 1) * layout.spacing.x) / columns;
            if (Mathf.Abs(layout.cellSize.x - cell) > .5f)
                layout.cellSize = new Vector2(cell, cell * 1.035f);
        }

        private void ApplySearch()
        {
            Filter(roster, view.Search("search").text);
            var filtered = rosterFilter.Order(roster.Where(row => row.Button.gameObject.activeSelf), row => art[row.Id]).ToArray();
            bool changed = !visibleRoster.SequenceEqual(filtered);
            if (changed)
            {
                visibleRoster = filtered;
                lastRosterOffset = float.NaN;
                lastRosterFocus = -1;
            }
            view.Node("rosterEmpty").SetActive(visibleRoster.Length == 0);
            LayoutRoster();
            if (changed) ResetRosterMotion(view.Scroll("roster"));
            Filter(grid, view.Search("preSearch").text);
        }

        private void Filter(List<Choice> list, string query)
        {
            string term = query?.Trim() ?? "";
            foreach (var choice in list)
            {
                var hero = art[choice.Id];
                bool matches = term.Length == 0 || hero.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    hero.Id.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
                if (ReferenceEquals(list, roster)) matches &= rosterFilter.Matches(hero);
                if (choice.Button.gameObject.activeSelf != matches) choice.Button.gameObject.SetActive(matches);
            }
        }

        private float RosterStep => rosterFilter.Compact ? 62f : 82f;

        private void LayoutRoster()
        {
            var scroll = view.Scroll("roster");
            float width = scroll.viewport.rect.width, height = scroll.viewport.rect.height;
            if (width <= 8f || height <= 0f) return;
            float step = RosterStep;
            // The first hero starts on the third position. Dragging down to later heroes
            // reveals the two prior cards above the focus line.
            float focusLine = Mathf.Min(height * .23f, 2.5f * step);
            float maxOffset = Mathf.Max(0, visibleRoster.Length - 1) * step;
            float extent = height + maxOffset;
            if (!Mathf.Approximately(scroll.content.rect.height, extent))
                scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, extent);
            float offset = Mathf.Clamp(scroll.content.anchoredPosition.y, 0, maxOffset);
            if (!Mathf.Approximately(scroll.content.anchoredPosition.y, offset))
                scroll.content.anchoredPosition = new Vector2(0, offset);
            if (Mathf.Abs(lastRosterOffset - offset) < .02f && lastRosterWidth == width && lastRosterHeight == height &&
                lastRosterStep == step) return;
            lastRosterOffset = offset;
            lastRosterWidth = width;
            lastRosterHeight = height;
            lastRosterStep = step;
            float focus = offset / step;
            int nearest = Mathf.Clamp(Mathf.RoundToInt(focus), 0, Mathf.Max(0, visibleRoster.Length - 1));
            float fullScale = (width - 10f) / RosterCardSize.x * (rosterFilter.Compact ? .86f : 1f);
            for (int i = 0; i < visibleRoster.Length; i++)
            {
                var row = (RectTransform)visibleRoster[i].Button.transform;
                float distance = Mathf.Abs(i - focus);
                float scale = fullScale * Mathf.Lerp(.70f, 1f, Mathf.Exp(-distance * .58f));
                row.anchorMin = row.anchorMax = new Vector2(1, 1);
                row.pivot = new Vector2(1, .5f);
                // The native plate has transparent pixels along its right edge.
                row.anchoredPosition = new Vector2(10f, -(focusLine + i * step));
                row.sizeDelta = RosterCardSize;
                row.localScale = new Vector3(scale, scale, 1f);
            }
            // Far rows are raised first. The focused card draws on top of both neighbors.
            if (visibleRoster.Length == 0 || nearest == lastRosterFocus) return;
            lastRosterFocus = nearest;
            int maxDistance = Mathf.Max(nearest, visibleRoster.Length - 1 - nearest);
            for (int distance = maxDistance; distance > 0; distance--)
            {
                if (nearest - distance >= 0) visibleRoster[nearest - distance].Button.transform.SetAsLastSibling();
                if (nearest + distance < visibleRoster.Length) visibleRoster[nearest + distance].Button.transform.SetAsLastSibling();
            }
            visibleRoster[nearest].Button.transform.SetAsLastSibling();
        }

        private void ResetRosterMotion(ScrollRect scroll)
        {
            observedRosterOffset = scroll.content.anchoredPosition.y;
            rosterMoving = false;
            snapIndex = -1;
        }

        private void UpdateRosterSnap(float seconds)
        {
            var scroll = view.Scroll("roster");
            float offset = scroll.content.anchoredPosition.y;
            bool moved = Mathf.Abs(offset - observedRosterOffset) > .1f;
            observedRosterOffset = offset;
            if (moved || rosterGesture.Dragging)
            {
                if (moved)
                {
                    rosterMoving = true;
                    snapIndex = -1;
                }
                rosterMovedAt = Time.unscaledTime;
                return;
            }

            if (snapIndex >= 0)
            {
                snapTime += seconds;
                float progress = Mathf.Clamp01(snapTime / .12f);
                scroll.content.anchoredPosition = new Vector2(0, Mathf.SmoothStep(snapFrom, snapIndex * RosterStep, progress));
                observedRosterOffset = scroll.content.anchoredPosition.y;
                if (progress < 1f) return;

                string id = visibleRoster[snapIndex].Id;
                ResetRosterMotion(scroll);
                if (CanPreviewRoster && CanSelect(id)) Select(id);
                return;
            }

            if (!rosterMoving || visibleRoster.Length == 0 || Time.unscaledTime - rosterMovedAt < .12f) return;
            scroll.StopMovement();
            snapIndex = Mathf.Clamp(Mathf.RoundToInt(offset / RosterStep), 0, visibleRoster.Length - 1);
            if (CanPreviewRoster && !CanSelect(visibleRoster[snapIndex].Id))
            {
                // Keep the focus on a legal candidate after passing a picked or banned card.
                int nearest = snapIndex;
                for (int distance = 1; distance < visibleRoster.Length; distance++)
                {
                    int after = nearest + distance;
                    int before = nearest - distance;
                    if (after < visibleRoster.Length && CanSelect(visibleRoster[after].Id)) { snapIndex = after; break; }
                    if (before >= 0 && CanSelect(visibleRoster[before].Id)) { snapIndex = before; break; }
                }
            }
            snapFrom = offset;
            snapTime = 0f;
            rosterMoving = false;
        }

        private bool CanPreviewRoster => draft?.phase == "pick" && draft.OwnTurn && !sending && revealUntil == 0f;

        private void PublishPreview(string heroId)
        {
            if (!CanPreviewRoster || scope.Token.IsCancellationRequested) return;
            previewSequence = Math.Max(previewSequence, draft.previewSequence) + 1;
            var request = new ArenaDraftPreview
            {
                draftId = draft.id, version = draft.version, sequence = previewSequence, heroId = heroId
            };
            _ = PublishPreviewAsync(draft.roomId, request, scope.Token);
        }

        private async Task PublishPreviewAsync(string roomId, ArenaDraftPreview request, CancellationToken token)
        {
            try
            {
                var next = await api.PreviewDraftAsync(roomId, request, token);
                if (!token.IsCancellationRequested && next != null) Adopt(next);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ArenaRequestException) { } // A preview may race a turn change; the authoritative poll still runs.
            catch (Exception error) { Debug.LogException(error); }
        }

        private void WatchSearch(string key)
        {
            var input = view.Search(key);
            UnityAction<string> changed = _ => ApplySearch();
            input.onValueChanged.AddListener(changed);
            scope.Add(() => { if (input) input.onValueChanged.RemoveListener(changed); });
        }

        private void AddHero(List<Choice> list, string template, Transform parent, string id)
        {
            var row = UnityEngine.Object.Instantiate(view.Node(template), parent, false);
            row.name = id;
            bool rosterCard = template == "row";
            var card = new Choice(row.GetComponent<BindingMap>(), artwork.LevelDigits, rosterCard: rosterCard,
                prebanCard: template == "gridCell");
            card.Set(id, GetArt(id), asCard: rosterCard, tags: template != "chip");
            UnityAction select = () => Select(card.Id);
            card.Button.onClick.AddListener(select);
            list.Add(card);
            row.SetActive(true);
        }

        private void SyncChips(List<Choice> chips, string parent, IReadOnlyList<string> heroes)
        {
            if (chips.Select(item => item.Id).SequenceEqual(heroes)) return;
            Release(chips);
            string template = parent == "preSelected" ? "gridCell" : "chip";
            foreach (string id in heroes) if (art.ContainsKey(id)) AddHero(chips, template, view.Node(parent).transform, id);
        }

        private void Listen(GameButton button, Func<Task> handler)
        {
            UnityAction click = async () =>
            {
                try { await handler(); }
                catch (Exception error) { Debug.LogException(error); }
            };
            button.onClick.AddListener(click);
            scope.Add(() => { if (button) button.onClick.RemoveListener(click); });
        }

        public void Dispose()
        {
            feature.Dispose();
            banFeedback.Dispose();
            foreach (var effect in blueFeedback) effect.Dispose();
            foreach (var effect in redFeedback) effect.Dispose();
            foreach (var model in modelSurfaces) model.Dispose();
        }

        private void Close()
        {
            rosterFilter.Close();
            battlePreload?.Dispose();
            battlePreload = null;
            scope = null;
            visibleRoster = Array.Empty<Choice>();
            feature.Clear();
            banFeedback.Clear();
            foreach (var effect in blueFeedback) effect.Clear();
            foreach (var effect in redFeedback) effect.Clear();
            foreach (var model in modelSurfaces) model.Clear();
            Array.Clear(shownFormation, 0, shownFormation.Length);
            Release(roster); Release(grid); Release(selected); Release(revealedOwn); Release(revealedEnemy); Release(recent); Release(revealCards);
            if (view.Root) view.Hide();
        }

        private static void Release(List<Choice> list)
        {
            foreach (var item in list)
            {
                if (!item.Button) continue;
                item.Button.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(item.Button.gameObject);
            }
            list.Clear();
        }

        private ArenaDraftArt.Hero GetArt(string id) => id != null && art.TryGetValue(id, out var hero) ? hero : null;
        private string L(string key) => locale.Resolve(key);
        private static int Count(string[] items) => items?.Length ?? 0;

        private sealed class Choice
        {
            internal string Id { get; private set; }
            internal readonly GameButton Button;
            internal readonly Image Portrait, Selection, Guard, Mark, Filled;
            internal RawImage Model;
            private readonly Image element, role, dim;
            internal readonly Image GuardPlate, FilledGuard, Change, Pending;
            internal readonly GameText GuardCaption;
            private readonly CanvasGroup levelVisibility, starVisibility;
            private readonly Image tens, units;
            private readonly Sprite[] levelDigits;
            private readonly Image[] grade, speed;
            private readonly Image pickedIcon, bannedIcon;
            private readonly GameText title, stars, state;
            private readonly Image prebanFlash, prebanGlow;
            private ArenaDraftArt.Hero displayedHero;
            private bool provisional;
            private float prebanStarted;
            // Scale keys from preben_effect_spine/intro. The mark holds its settled pose during the loop.
            private static readonly AnimationCurve PrebanStrikeScale = new AnimationCurve(
                new Keyframe(0f, 5f), new Keyframe(.1f, 1f), new Keyframe(.1333f, 3f),
                new Keyframe(.2f, 1f), new Keyframe(.2333f, 1.05f), new Keyframe(.3333f, 1f));

            internal Choice(BindingMap map, Sprite[] digits = null, bool teamSlot = false, bool protectedSlot = false,
                bool miniature = false, bool rosterCard = false, bool prebanCard = false)
            {
                Button = map.Require<GameButton>("button");
                Portrait = map.Require<Image>("portrait");
                Selection = map.Require<Image>("selection");
                Guard = map.Require<Image>("guard");
                Mark = map.Require<Image>("mark");
                Filled = map.Require<Image>("filled");
                element = map.Require<Image>("element");
                role = map.Require<Image>("role");
                title = map.Require<GameText>("name");
                stars = map.Require<GameText>("stars");
                state = map.Require<GameText>("state");
                dim = map.Require<Image>("dim");
                if (teamSlot)
                {
                    levelDigits = digits;
                    levelVisibility = map.Require<GameObject>("levelRoot").GetComponent<CanvasGroup>();
                    starVisibility = map.Require<GameObject>("starRoot").GetComponent<CanvasGroup>();
                    tens = map.Require<Image>("levelTens");
                    units = map.Require<Image>("levelUnits");
                    grade = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                    Pending = map.Require<Image>("pending");
                    if (protectedSlot)
                    {
                        GuardPlate = map.Require<Image>("guardPlate");
                        FilledGuard = map.Require<Image>("filledGuard");
                        GuardCaption = map.Require<GameText>("guardCaption");
                    }
                }
                if (miniature) Change = map.Require<Image>("change");
                if (rosterCard)
                {
                    levelDigits = digits;
                    speed = Enumerable.Range(0, 3).Select(index => map.Require<Image>("speed" + index)).ToArray();
                    grade = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                    pickedIcon = map.Require<Image>("pickedIcon");
                    bannedIcon = map.Require<Image>("bannedIcon");
                }
                if (prebanCard)
                {
                    prebanFlash = map.Require<Image>("prebanFlash");
                    prebanGlow = map.Require<Image>("prebanGlow");
                    grade = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                }
            }

            internal void Set(string id, ArenaDraftArt.Hero hero, bool asCard, bool tags, bool levelLabel = false)
            {
                if (Id == id && displayedHero == hero) return;
                Id = id;
                displayedHero = hero;
                if (levelVisibility)
                {
                    levelVisibility.gameObject.SetActive(hero != null);
                    starVisibility.gameObject.SetActive(hero != null);
                    if (hero != null)
                    {
                        tens.sprite = levelDigits[Mathf.Clamp(hero.Level / 10, 0, 9)];
                        units.sprite = levelDigits[Mathf.Clamp(hero.Level % 10, 0, 9)];
                        for (int index = 0; index < grade.Length; index++) grade[index].enabled = index < hero.Stars;
                    }
                }
                Portrait.sprite = asCard ? hero?.Card : hero?.Face;
                Portrait.enabled = Portrait.sprite;
                title.text = levelLabel && hero != null ? "Lv." + hero.Level : asCard ? hero?.Name ?? "" : "";
                element.sprite = hero?.ElementIcon;
                role.sprite = hero?.ClassIcon;
                element.enabled = tags && element.sprite;
                role.enabled = tags && role.sprite;
                stars.text = grade == null && tags && hero != null ? new string('★', hero.Stars) : "";
                if (speed != null && hero != null) SetRosterDetails(hero);
                if (prebanFlash && hero != null) SetPrebanGrade(hero);
            }

            private void SetPrebanGrade(ArenaDraftArt.Hero hero)
            {
                int count = Mathf.Clamp(hero.BaseStars > 0 ? hero.BaseStars : hero.Stars, 1, grade.Length);
                const float width = .177f;
                const float stride = width * 20f / 30f;
                float first = .5f - (width + stride * (count - 1)) / 2f;
                for (int index = 0; index < grade.Length; index++)
                {
                    var star = grade[index];
                    star.enabled = index < count;
                    var rect = star.rectTransform;
                    float x = first + index * stride;
                    rect.anchorMin = new Vector2(x, rect.anchorMin.y);
                    rect.anchorMax = new Vector2(x + width, rect.anchorMax.y);
                }
            }

            internal void SetPrebanMark(bool visible)
            {
                if (Mark.enabled == visible) return;
                Mark.enabled = visible;
                if (visible)
                {
                    prebanStarted = Time.unscaledTime;
                    AnimatePreban(prebanStarted);
                }
                else
                {
                    prebanFlash.enabled = prebanGlow.enabled = false;
                }
            }

            internal void AnimatePreban(float now)
            {
                if (!Mark.enabled || !Mark.gameObject.activeInHierarchy) return;
                float elapsed = now - prebanStarted;
                float size = PrebanStrikeScale.Evaluate(elapsed);
                Mark.rectTransform.localScale = new Vector3(size, size, 1f);
                SetOpacity(Mark, Mathf.Clamp01(elapsed / .1f));
                prebanFlash.enabled = true;
                float pulse = .5f + .5f * Mathf.Cos(Mathf.Max(0f, elapsed - .5f) * Mathf.PI * 2f);
                float flare = 1f - Mathf.Clamp01(elapsed / .2666f);
                prebanFlash.color = new Color(1f, .114f, 0f, Mathf.Lerp(.27f, .47f, pulse) + flare * .43f);
                prebanGlow.enabled = elapsed < .2333f;
                if (prebanGlow.enabled) SetOpacity(prebanGlow, 1f - elapsed / .2333f);
            }

            internal void SetProvisional(bool value)
            {
                if (provisional == value) return;
                provisional = value;
                SetPickCardOpacity(value ? .18f : 1f);
            }

            internal void AnimatePick(float pulse)
            {
                // Native blue_idle/red_idle fade from 255 to about 53 and back in one second.
                if (Pending.enabled) SetOpacity(Pending, Mathf.Lerp(.207f, 1f, pulse));
                if (provisional) SetPickCardOpacity(Mathf.Lerp(.17f, .48f, pulse));
            }

            internal void SetBanTint(bool banned)
            {
                Button.targetGraphic.color = banned ? new Color(.37f, .37f, .37f) : Color.white;
                if (provisional) return;
                Portrait.color = role.color = element.color = banned ? new Color(.34f, .34f, .34f) : Color.white;
                levelVisibility.alpha = starVisibility.alpha = banned ? .46f : 1f;
            }

            private void SetPickCardOpacity(float opacity)
            {
                SetOpacity(Portrait, opacity);
                SetOpacity(role, opacity);
                SetOpacity(element, opacity);
                levelVisibility.alpha = starVisibility.alpha = opacity;
            }

            private static void SetOpacity(Graphic graphic, float opacity)
            {
                var color = graphic.color;
                color.a = opacity;
                graphic.color = color;
            }

            private void SetRosterDetails(ArenaDraftArt.Hero hero)
            {
                if (Portrait.sprite)
                {
                    var rect = Portrait.rectTransform;
                    rect.anchorMin = new Vector2(rect.anchorMax.x - Portrait.sprite.rect.width / RosterCardSize.x, rect.anchorMin.y);
                }
                int value = Mathf.Clamp(hero.Speed, 0, 999);
                speed[0].sprite = levelDigits[value / 100];
                speed[1].sprite = levelDigits[value / 10 % 10];
                speed[2].sprite = levelDigits[value % 10];
                speed[0].enabled = value >= 100;
                speed[1].enabled = value >= 10;
                speed[2].enabled = true;
                for (int index = 0; index < grade.Length; index++) grade[index].enabled = index < hero.Stars;
            }

            internal void SetFormationLevel(ArenaDraftArt.Hero hero)
            {
                stars.text = "";
                title.text = hero == null ? "" : hero.Level.ToString(CultureInfo.InvariantCulture);
            }

            internal void SetState(string value, bool banned = false)
            {
                bool caption = !string.IsNullOrEmpty(value);
                state.text = value ?? "";
                state.color = banned ? new Color32(255, 145, 134, 255) : new Color32(140, 205, 94, 255);
                state.gameObject.SetActive(caption);
                dim.gameObject.SetActive(caption || banned);
                dim.color = banned ? new Color(.22f, .040f, .041f, .72f) : new Color(.012f, .022f, .055f, .62f);
                title.enabled = !caption;
                role.enabled = role.sprite && !caption;
                if (pickedIcon) pickedIcon.gameObject.SetActive(caption && !banned);
                if (bannedIcon) bannedIcon.gameObject.SetActive(banned);
            }
        }
    }
}
