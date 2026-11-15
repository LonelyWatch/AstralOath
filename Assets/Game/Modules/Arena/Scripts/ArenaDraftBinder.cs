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
        private readonly Func<Task> returnToRoom;
        private readonly ArenaDraftRosterFilter rosterFilter;
        private Choice[] visibleRoster = Array.Empty<Choice>();
        private float lastRosterOffset = float.NaN, lastRosterWidth, lastRosterHeight;
        private int? localLatency;
        private int lastRosterFocus = -1;
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
        private readonly ArenaDraftPortrait feature;
        private readonly string[] shownFormation = new string[4];

        private BindingScope scope;
        private ArenaDraftState draft;
        private string candidate, featured, featuredBan, loadedBattle;
        private string[] formation = Empty;
        private int swapIndex;
        private bool sending, launching;
        private DateTimeOffset deadline;

        public ArenaDraftBinder(ArenaDraft view, ArenaApi api, ILocaleService locale, ArenaDraftArt artwork,
            WindowCoordinator windows, Func<Task> returnToRoom)
        {
            this.view = view;
            this.api = api;
            this.locale = locale;
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
            for (int index = 0; index < 5; index++)
            {
                left[index] = new Choice(view.Card("blue", index), artwork.LevelDigits, teamSlot: true, protectedSlot: index == 2);
                right[index] = new Choice(view.Card("red", index), artwork.LevelDigits, teamSlot: true, protectedSlot: index == 2);
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
            revealUntil = 0f;
            sending = launching = false;
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
            FitGrid(prebanLayout, gridViewport.rect.width);
            FitGrid(recentLayout, recentGrid.rect.width);
            feature.Tick(seconds);
            foreach (var model in modelSurfaces) model.Tick(seconds);
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
            try
            {
                var own = match.Own.player;
                var opponent = match.Opponent.player;
                var identities = new BattleOnlinePlayers(own.displayName, opponent.displayName, own.avatarId, opponent.avatarId,
                    GetArt(own.avatarId)?.Face, GetArt(opponent.avatarId)?.Face);
                await BattleOnlineLaunch.LoadAsync(api.Endpoint, match.roomId, match.battleId, match.contentHash,
                    api.Bearer, match.IsHost, token, identities);
                loadedBattle = match.battleId;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (!token.IsCancellationRequested) view.Label("status").text = L("arena.draft.launch_failed");
                await Task.Delay(2500, token);
            }
            finally { launching = false; }
        }

        private void Adopt(ArenaDraftState next)
        {
            if (draft != null && (next.id != draft.id || next.version < draft.version)) return;
            if (draft != null && next.version == draft.version) return;
            bool changedPhase = draft?.phase != next.phase;
            bool revealPreban = draft?.phase == "preban" && next.phase == "pick";
            int priorHostPicks = Count(draft?.host?.picks);
            int priorGuestPicks = Count(draft?.guest?.picks);
            draft = next;
            deadline = DateTimeOffset.TryParse(draft.deadlineAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var end) ? end : default;
            if (changedPhase)
            {
                candidate = null;
                swapIndex = -1;
                if (draft.phase == "formation")
                    formation = DraftFormation();
            }
            bool hostPicked = Count(draft.host.picks) > priorHostPicks;
            bool guestPicked = Count(draft.guest.picks) > priorGuestPicks;
            if (draft.phase == "pick" && (hostPicked || guestPicked))
            {
                if (!CanSelect(candidate)) candidate = null;
                bool showHost = hostPicked && (!guestPicked || draft.activeSide != "host");
                string last = (showHost ? draft.host.picks : draft.guest.picks)?.LastOrDefault();
                featured = candidate ?? last ?? featured;
            }
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
        }

        private string[] DraftFormation() => Count(draft.Own.formation) == 4
            ? draft.Own.formation.ToArray()
            : (draft.Own.picks ?? Empty).Where(id => id != draft.Opponent.ban).ToArray();

        private void Render()
        {
            bool pre = draft.phase == "preban";
            bool arrange = draft.phase == "formation" || draft.phase == "battle";
            bool showingReveal = revealUntil > 0f;
            bool canAct = !sending && !launching && !showingReveal && !draft.Own.submitted;
            view.Node("reveal").SetActive(showingReveal);
            bool pick = draft.phase == "pick";
            bool ban = draft.phase == "ban";
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
                : "arena.draft." + draft.phase);
            view.Label("title").color = pick && !draft.OwnTurn ? new Color32(254, 30, 30, 255) : new Color32(68, 148, 255, 255);
            view.Label("hint").text = L(pick ? "arena.draft.pick_hint" : ban ? "arena.draft.ban_hint"
                : arrange ? "arena.draft.formation_hint" : "arena.draft.preban_hint");
            view.Label("preCount").text = GameText.Format(locale, "arena.draft.preban_count", prebans.Count, draft.prebanCount);
            view.Label("actionLabel").text = L(launching ? "arena.draft.entering" : draft.Own.submitted ||
                pick && !draft.OwnTurn ? "arena.draft.waiting" : arrange ? "arena.start" : ban ? "arena.draft.complete" : "arena.draft.choose");
            view.Label("preActionLabel").text = L(draft.Own.submitted ? "arena.draft.waiting" : "arena.draft.choose");
            view.Button("action").interactable = canAct && (pick || ban ? CanSelect(candidate) : draft.phase == "formation" && formation.Length == 4);
            view.Button("preAction").interactable = canAct && prebans.Count == draft.prebanCount;
            view.Picture("blue").enabled = !pick || draft.OwnTurn;
            view.Picture("red").enabled = !pick || !draft.OwnTurn;
            view.Picture("banMark").enabled = ban;
            view.Picture("banGlow").enabled = ban;
            var action = view.Button("action");
            ((Image)action.targetGraphic).sprite = ban ? artwork.ActionRed : artwork.ActionGreen;
            var states = action.spriteState;
            states.pressedSprite = ban ? artwork.ActionRedPressed : artwork.ActionGreenPressed;
            action.spriteState = states;
            // Source final-ban prompt is above the red ban emblem.
            var hintRect = view.Label("hint").rectTransform;
            hintRect.anchorMin = new Vector2(.285f, ban ? .279f : arrange ? .161f : .244f);
            hintRect.anchorMax = new Vector2(.713f, ban ? .368f : arrange ? .234f : .317f);
            hintRect.offsetMin = hintRect.offsetMax = Vector2.zero;

            RenderTeam(left, draft.Own, draft.Opponent, pick && draft.OwnTurn, false);
            RenderTeam(right, draft.Opponent, draft.Own, pick && !draft.OwnTurn, true);
            foreach (var choice in roster)
            {
                bool valid = CanSelect(choice.Id);
                choice.Button.interactable = pick && valid && canAct && draft.OwnTurn;
                choice.Selection.enabled = choice.Id == candidate && pick;
                bool taken = Used(choice.Id);
                bool banned = IsPrebanned(choice.Id);
                choice.SetState(taken ? "✓ " + L("arena.draft.picked") :
                    banned ? L("arena.draft.banned") : null, banned);
            }
            foreach (var choice in grid.Concat(recent).Concat(selected))
            {
                choice.Selection.enabled = prebans.Contains(choice.Id);
                choice.Button.interactable = pre && !draft.Own.submitted && !sending;
            }
            foreach (var choice in revealedOwn.Concat(revealedEnemy)) choice.Button.interactable = false;

            if (arrange) RenderFormation(canAct);
            SetFeatured(pre || arrange ? null : candidate ?? featured ?? draft.Own.picks?.LastOrDefault() ??
                draft.Opponent.picks?.LastOrDefault(), ban);
            UpdateTimer();
            ApplySearch();
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

        private void RenderTeam(Choice[] team, ArenaDraftSeat seat, ArenaDraftSeat opponent, bool active, bool enemy)
        {
            int taken = Count(seat.picks);
            for (int index = 0; index < team.Length; index++)
            {
                string id = index < taken ? seat.picks[index] : null;
                var item = team[index];
                item.Set(id, GetArt(id), asCard: true, tags: true, levelLabel: true);
                bool protect = id != null && id == seat.protectedHeroId;
                bool isBanned = id != null && (draft.phase is "formation" or "battle") && id == opponent.ban;
                item.Button.interactable = id != null && (!enemy || draft.phase != "ban" || CanSelect(id));
                item.Pending.enabled = active && index >= taken && index < taken + draft.picksRemainingInWindow;
                item.Pending.color = enemy ? new Color32(255, 104, 95, 72) : new Color32(111, 204, 255, 72);
                item.Selection.enabled = enemy && id != null && id == candidate && draft.phase == "ban";
                ((Image)item.Button.targetGraphic).color = Color.white;
                item.Guard.enabled = protect || draft.banProtection && index == 2 && id == null;
                if (item.GuardPlate)
                {
                    bool showSlotProtection = item.Guard.enabled && id == null;
                    item.GuardPlate.enabled = item.GuardCaption.enabled = showSlotProtection;
                    item.GuardCaption.text = L("arena.draft.safeguard");
                }
                item.Mark.enabled = isBanned;
                item.SetState(null, isBanned);
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
            Color tone = ban ? new Color(.6f, .56f, .56f, 1) : Color.white;
            surface.color = fallback.color = tone;
            if (hero == null)
            {
                feature.Clear();
                fallback.enabled = false;
                return;
            }
            fallback.sprite = hero.Illustration ? hero.Illustration : hero.Card;
            fallback.enabled = !feature.Show(hero.MakePortrait());
        }

        private void Select(string id)
        {
            if (!CanSelect(id) || sending) return;
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
                    scroll.content.anchoredPosition = new Vector2(0, selectedIndex * (rosterFilter.Compact ? 57f : 73f));
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
            if (sending || launching || draft.Own.submitted) return;
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
            visibleRoster = rosterFilter.Order(roster.Where(row => row.Button.gameObject.activeSelf), row => art[row.Id]).ToArray();
            view.Node("rosterEmpty").SetActive(visibleRoster.Length == 0);
            lastRosterOffset = float.NaN;
            lastRosterFocus = -1;
            LayoutRoster();
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

        private void LayoutRoster()
        {
            var scroll = view.Scroll("roster");
            float width = scroll.viewport.rect.width, height = scroll.viewport.rect.height;
            if (width <= 0f || height <= 0f) return;
            float step = rosterFilter.Compact ? 57f : 73f;
            float baseHeight = rosterFilter.Compact ? 65f : 84f;
            float focusLine = Mathf.Min(height * .19f, step * 2f);
            float edge = focusLine - step * .5f;
            float extent = Mathf.Max(height, focusLine + (visibleRoster.Length - 1) * step + height - focusLine);
            if (scroll.content.sizeDelta.y != extent) scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, extent);
            float offset = Mathf.Clamp(scroll.content.anchoredPosition.y, 0, extent - height);
            if (!Mathf.Approximately(scroll.content.anchoredPosition.y, offset))
                scroll.content.anchoredPosition = new Vector2(0, offset);
            if (Mathf.Abs(lastRosterOffset - offset) < .02f && lastRosterWidth == width && lastRosterHeight == height) return;
            lastRosterOffset = offset; lastRosterWidth = width; lastRosterHeight = height;
            float focus = offset / step;
            int nearest = Mathf.Clamp(Mathf.RoundToInt(focus), 0, Mathf.Max(0, visibleRoster.Length - 1));
            for (int i = 0; i < visibleRoster.Length; i++)
            {
                var row = (RectTransform)visibleRoster[i].Button.transform;
                float distance = Mathf.Abs(i - focus);
                float prominence = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / 3.8f));
                row.anchorMin = row.anchorMax = new Vector2(1, 1);
                row.pivot = new Vector2(1, .5f);
                row.anchoredPosition = new Vector2(-4f, -(edge + (i + .5f) * step));
                row.sizeDelta = new Vector2((width - 8f) * Mathf.Lerp(.66f, 1f, prominence), baseHeight * Mathf.Lerp(.88f, 1.25f, prominence));

            }
            // Far rows are raised first. The card at the focus line draws on top of both neighbors.
            if (visibleRoster.Length == 0 || nearest == lastRosterFocus) return;
            lastRosterFocus = nearest;
            int maxDistance = Mathf.Max(nearest, visibleRoster.Length - 1 - nearest);
            for (int distance = maxDistance; distance > 0 && visibleRoster.Length > 0; distance--)
            {
                if (nearest - distance >= 0) visibleRoster[nearest - distance].Button.transform.SetAsLastSibling();
                if (nearest + distance < visibleRoster.Length) visibleRoster[nearest + distance].Button.transform.SetAsLastSibling();
            }
            if (visibleRoster.Length > 0) visibleRoster[nearest].Button.transform.SetAsLastSibling();
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
            var card = new Choice(row.GetComponent<BindingMap>());
            card.Set(id, GetArt(id), asCard: template == "row", tags: template != "chip");
            UnityAction select = () => Select(card.Id);
            card.Button.onClick.AddListener(select);
            list.Add(card);
            row.SetActive(true);
        }

        private void SyncChips(List<Choice> chips, string parent, IReadOnlyList<string> heroes)
        {
            if (chips.Select(item => item.Id).SequenceEqual(heroes)) return;
            Release(chips);
            foreach (string id in heroes) if (art.ContainsKey(id)) AddHero(chips, "chip", view.Node(parent).transform, id);
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
            foreach (var model in modelSurfaces) model.Dispose();
        }

        private void Close()
        {
            rosterFilter.Close();
            scope = null;
            visibleRoster = Array.Empty<Choice>();
            feature.Clear();
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
            internal readonly Image GuardPlate, Change, Pending;
            internal readonly GameText GuardCaption;
            private readonly GameObject levelRoot, starRoot;
            private readonly Image tens, units;
            private readonly Sprite[] levelDigits;
            private readonly Image[] grade;
            private readonly GameText title, stars, state;

            internal Choice(BindingMap map, Sprite[] digits = null, bool teamSlot = false, bool protectedSlot = false, bool miniature = false)
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
                    levelRoot = map.Require<GameObject>("levelRoot");
                    starRoot = map.Require<GameObject>("starRoot");
                    tens = map.Require<Image>("levelTens");
                    units = map.Require<Image>("levelUnits");
                    grade = Enumerable.Range(0, 6).Select(index => map.Require<Image>("star" + index)).ToArray();
                    Pending = map.Require<Image>("pending");
                    if (protectedSlot)
                    {
                        GuardPlate = map.Require<Image>("guardPlate");
                        GuardCaption = map.Require<GameText>("guardCaption");
                    }
                }
                if (miniature) Change = map.Require<Image>("change");
            }

            internal void Set(string id, ArenaDraftArt.Hero hero, bool asCard, bool tags, bool levelLabel = false)
            {
                if (levelRoot)
                {
                    levelRoot.SetActive(hero != null);
                    starRoot.SetActive(hero != null);
                    if (hero != null)
                    {
                        tens.sprite = levelDigits[Mathf.Clamp(hero.Level / 10, 0, 9)];
                        units.sprite = levelDigits[Mathf.Clamp(hero.Level % 10, 0, 9)];
                        for (int index = 0; index < grade.Length; index++) grade[index].enabled = index < hero.Stars;
                    }
                }
                if (Id == id) return;
                Id = id;
                Portrait.sprite = asCard ? hero?.Card : hero?.Face;
                Portrait.enabled = Portrait.sprite;
                title.text = levelLabel && hero != null ? "Lv." + hero.Level : asCard ? hero?.Name ?? "" : "";
                element.sprite = hero?.ElementIcon;
                role.sprite = hero?.ClassIcon;
                element.enabled = tags && element.sprite;
                role.enabled = tags && role.sprite;
                stars.text = tags && hero != null ? new string('★', hero.Stars) : "";
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
                dim.color = banned ? new Color(.22f, .040f, .041f, .72f) : new Color(.012f, .022f, .055f, .70f);
                title.enabled = !caption;
                role.enabled = role.sprite && !caption;
                element.enabled = element.sprite && !caption;
            }
        }
    }
}
