using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaRoomBinder
    {
        private readonly ArenaRoom view;
        private readonly ILocaleService locale;
        private readonly ArenaApi api;
        private readonly Func<Task> goBack;
        private readonly Func<ArenaDraftState, Task> openDraft;
        private readonly WindowCoordinator windows;
        private readonly GameToastBinder toast;
        private const int ChatHistoryLimit = 100;
        private const string StickerWindow = "arena/stickers";
        private const string PrebanWindow = "arena/room-preban";
        private const string ProtectionWindow = "arena/room-protection";
        private static readonly Color InactivePickColor = new Color32(150, 153, 157, 255);
        private static readonly Color SelectedChoiceColor = new Color32(20, 21, 23, 255);
        private static readonly Color UnselectedChoiceColor = new Color32(149, 149, 149, 255);
        private readonly Dictionary<string, Sprite> stickers = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Color SelfNameColor = new Color32(137, 201, 62, 255);
        private readonly SortedList<long, MessageRow> messages = new SortedList<long, MessageRow>();

        private sealed class MessageRow
        {
            internal RectTransform Root { get; }
            internal GameText Age { get; }
            internal DateTimeOffset? CreatedAt { get; }

            internal MessageRow(RectTransform root, GameText age, DateTimeOffset? createdAt)
            {
                Root = root;
                Age = age;
                CreatedAt = createdAt;
            }
        }
        private BindingScope screen, roster;
        private ArenaRoomState state;
        private long cursor;
        private string memberSignature, roomError, actionError;
        private bool leaving, sending, starting, transitioning, savingSettings;
        private int settingsRevision;
        private Func<BindingScope, RectTransform, Task> openPublicChannel;
        private Action<BindingMap, BindingScope> bindUnread;
        private WindowCoordinator publicChannelWindows;

        public void ConfigurePublicChannel(Func<BindingScope, RectTransform, Task> open, Action<BindingMap, BindingScope> unread)
        { openPublicChannel = open; bindUnread = unread; }

        public ArenaRoomBinder(ArenaRoom view, ILocaleService locale, ArenaApi api, WindowCoordinator windows, Func<Task> goBack, Func<ArenaDraftState, Task> openDraft = null)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.goBack = goBack;
            this.openDraft = openDraft;
            this.windows = windows;
            foreach (var picker in new[] { view.EmojiPicker, view.PrebanPicker, view.ProtectionPicker })
                picker.GetComponent<BindingMap>().Require<GameButton>("dismiss").KeepBrightnessOnPress = true;
            toast = new GameToastBinder(GameToast.ForInstance(view.Toast));
            var choices = view.EmojiPicker.GetComponent<BindingMap>().Require<ScrollRect>("choices");
            foreach (var choice in choices.content.GetComponentsInChildren<GameButton>(true))
                stickers.Add(choice.name, choice.GetComponent<BindingMap>().Require<Image>("art").sprite);
        }

        public Task OpenAsync(ArenaRoomState initial, BindingScope owner)
        {
            screen = owner;
            view.Show();
            owner.Add(Close);
            ShowStatus();
            view.Message.SetTextWithoutNotify("");
            Render(initial);
            view.Leave.Bind(screen, LeaveAsync);
            var start = view.Start;
            start.onClick.AddListener(StartClick);
            screen.Add(() => { if (start) start.onClick.RemoveListener(StartClick); });
            Listen(view.PrebanCount, OpenPrebanPicker);
            Listen(view.BanProtection, OpenProtectionPicker);
            Listen(view.FirstPick, () => ChangeFirstPick(view.FirstPick, "host"));
            Listen(view.GuestFirstPick, () => ChangeFirstPick(view.GuestFirstPick, "guest"));
            ShowArenaChat();
            Listen(view.ChannelTab, () => _ = ShowPublicChannelAsync());
            Listen(view.ArenaTab, ShowArenaChat);
            bindUnread?.Invoke(view.ChannelUnreadBadge, owner);
            view.Send.interactable = initial.room.allowChat;
            view.Emoji.interactable = initial.room.allowChat;
            if (initial.room.allowChat)
            {
                view.Send.Bind(screen, SendAsync);
                view.Emoji.Bind(screen, ignored => { ToggleStickerPicker(); return Task.CompletedTask; });
            }
            _ = WatchRoomAsync(initial.room.id, screen.Token);
            return Task.CompletedTask;
        }

        private async Task ShowPublicChannelAsync()
        {
            if (screen == null || screen.Token.IsCancellationRequested || publicChannelWindows != null || openPublicChannel == null) return;
            windows.TryCloseOverlay();
            var opening = new WindowCoordinator(screen.Token); publicChannelWindows = opening;
            view.RoomChatContents.SetActive(false); view.PublicChannelHost.gameObject.SetActive(true);
            view.ChannelSelected.enabled = true; view.ArenaSelected.enabled = false;
            try { await opening.ShowPageAsync(scope => openPublicChannel(scope, view.PublicChannelHost)); }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (ReferenceEquals(publicChannelWindows, opening)) ShowArenaChat();
                Debug.LogWarning("Public channel window: " + error.GetType().Name);
            }
        }

        private void ShowArenaChat()
        {
            var previous = publicChannelWindows; publicChannelWindows = null; previous?.Dispose();
            view.PublicChannelHost.gameObject.SetActive(false); view.RoomChatContents.SetActive(true);
            view.ChannelSelected.enabled = false; view.ArenaSelected.enabled = true;
        }

        private async Task WatchRoomAsync(string id, CancellationToken token)
        {
            var sinceSync = new System.Diagnostics.Stopwatch();
            int retrySeconds = 1;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var connected = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        await api.WatchRoomEventsAsync(id, Receive, token);
                    }
                    catch (ArenaRequestException error)
                    {
                        token.ThrowIfCancellationRequested();
                        if (IsRoomClosed(error)) { await ReturnToDirectoryAsync(); return; }
                        // A subscription can fail while ordinary reads still work (including older servers).
                        if (!sinceSync.IsRunning || sinceSync.Elapsed.TotalSeconds >= 30)
                        {
                            sinceSync.Restart();
                            try { await Receive("sync"); }
                            catch (ArenaRequestException snapshotError)
                            {
                                token.ThrowIfCancellationRequested();
                                if (IsRoomClosed(snapshotError)) { await ReturnToDirectoryAsync(); return; }
                                roomError = ArenaError.Text(locale, snapshotError);
                                ShowStatus();
                            }
                        }
                    }
                    if (connected.Elapsed.TotalSeconds >= 30) retrySeconds = 1;
                    await Task.Delay(TimeSpan.FromSeconds(retrySeconds), token);
                    retrySeconds = Math.Min(15, retrySeconds * 2);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); }

            async Task Receive(string kind)
            {
                token.ThrowIfCancellationRequested();
                bool sync = kind == "sync" || sinceSync.Elapsed.TotalMinutes >= 2;
                if (sync || kind == "room")
                {
                    int revision = settingsRevision;
                    var current = await api.GetAsync(id, token);
                    token.ThrowIfCancellationRequested();
                    if (revision == settingsRevision && !savingSettings) Render(current);
                }
                if (sync || kind == "heartbeat") RefreshMessageAges();
                if (sync || kind == "chat")
                {
                    ArenaMessage[] incoming;
                    do
                    {
                        incoming = await api.ReadMessagesAsync(id, cursor, token);
                        token.ThrowIfCancellationRequested();
                        if (incoming.Length == 0) break;
                        AppendMessages(incoming);
                        cursor = incoming[incoming.Length - 1].id;
                    } while (incoming.Length == ChatHistoryLimit);
                }
                if ((sync || kind == "room") && openDraft != null && state.room.rule == "basic" &&
                    !transitioning && !starting && !leaving)
                {
                    ArenaDraftState draft = null;
                    try { draft = await api.DraftAsync(id, token); }
                    catch (ArenaRequestException error) when (error.Code == "draft_not_seated") { }
                    token.ThrowIfCancellationRequested();
                    if (draft != null) await EnterDraftAsync(draft);
                }
                token.ThrowIfCancellationRequested();
                if (sync) sinceSync.Restart();
                roomError = null;
                ShowStatus();
            }
        }

        private void Render(ArenaRoomState next)
        {
            state = next;
            var members = next.members ?? Array.Empty<ArenaPlayer>();
            string signature = next.room.host?.id + ":" + string.Join("|", members.Select(member => member.id));
            ArenaArt.SetText(view.RoomName, next.room.name);
            view.RoomLock.gameObject.SetActive(next.room.requiresPassword);
            view.Rule.text = locale.Resolve("arena.rule." + next.room.rule);
            view.Count.text = $"{next.room.memberCount}/{next.room.capacity}";
            view.Message.interactable = next.room.allowChat;
            RenderDraftSettings();
            if (memberSignature == signature) return;
            memberSignature = signature;
            var host = next.room.host;
            var guest = members.FirstOrDefault(member => member.id != host?.id);
            SetPlayerName(view.HostName, host);
            SetPlayerName(view.GuestName, guest);
            ArenaArt.SetAvatar(view.Host, host, true);
            view.Vacant.enabled = guest == null;
            view.EmptyInfo.SetActive(guest == null);
            view.Guest.gameObject.SetActive(guest != null);
            if (guest != null) ArenaArt.SetAvatar(view.Guest, guest, false);
            roster?.Dispose();
            roster = new BindingScope(screen.Token);
            foreach (var member in members)
            {
                var row = UnityEngine.Object.Instantiate(view.MemberRow, view.Members.content, false);
                roster.Add(() => { if (row) { row.SetActive(false); UnityEngine.Object.Destroy(row); } });
                var map = row.GetComponent<BindingMap>();
                ArenaArt.SetAvatar(map.Require<BindingMap>("avatar"), member, member.id == host?.id);
                SetPlayerName(map.Require<GameText>("name"), member);
                row.SetActive(true);
            }
        }

        private bool IsDraftHost => state?.room?.host?.id != null && state.room.host.id == api.Player?.id;

        private ArenaPlayer GuestSeat => state?.members?.FirstOrDefault(member => member.id != state.room.host?.id);
        private bool SupportsReady => state?.supportsDraftReady == true;
        private bool IsGuestSeat => GuestSeat?.id is string guestId && guestId == api.Player?.id;
        private bool GuestIsReady => SupportsReady && state.readyPlayerIds != null &&
            Array.IndexOf(state.readyPlayerIds, GuestSeat?.id) >= 0;
        private bool CanUseDraftSettings => state?.room.rule == "basic" && screen != null &&
            !screen.Token.IsCancellationRequested && !savingSettings && !starting && !transitioning && !leaving;
        private bool CanEditDraftSettings => CanUseDraftSettings && IsDraftHost;

        private void RenderDraftSettings()
        {
            var room = state.room;
            bool basic = room.rule == "basic";
            bool guestPresent = GuestSeat != null;
            view.DraftSettings.SetActive(basic);
            view.GuestFirstPick.gameObject.SetActive(basic && guestPresent);
            view.GuestReady.gameObject.SetActive(basic && GuestIsReady);
            view.StartLabel.text = locale.Resolve(basic && SupportsReady && IsGuestSeat
                ? GuestIsReady ? "arena.draft.room_cancel_ready" : "arena.draft.room_ready"
                : "arena.start");
            bool canStart = IsDraftHost && guestPresent && (!SupportsReady || GuestIsReady);
            view.Start.GetComponent<CanvasGroup>().alpha =
                CanUseDraftSettings && (canStart || SupportsReady && IsGuestSeat) ? 1f : .55f;
            if (!basic) return;

            view.PrebanCount.interactable = view.BanProtection.interactable = CanUseDraftSettings;
            view.FirstPick.interactable = CanEditDraftSettings;
            view.GuestFirstPick.interactable = CanEditDraftSettings && guestPresent;
            view.PrebanLabel.text = room.prebanCount.ToString(CultureInfo.InvariantCulture);
            view.ProtectionLabel.text = locale.Resolve(room.banProtection ? "arena.draft.room_on" : "arena.draft.room_off");

            bool hostFirst = room.firstPick == "host";
            bool guestFirst = room.firstPick == "guest";
            view.FirstPickCheck.enabled = hostFirst;
            view.GuestFirstPickCheck.enabled = guestFirst;
            view.FirstPickLabel.color = hostFirst ? SelfNameColor : InactivePickColor;
            view.GuestFirstPickLabel.color = guestFirst ? SelfNameColor : InactivePickColor;
        }

        private void Listen(GameButton button, UnityAction action)
        {
            button.onClick.AddListener(action);
            screen.Add(() => { if (button) button.onClick.RemoveListener(action); });
        }

        private void OpenPrebanPicker()
        {
            if (!CanUseDraftSettings) return;
            ShowDraftPicker(PrebanWindow, view.PrebanPicker, view.PrebanCount, state.room.prebanCount, 4,
                api.SetPrebanCountAsync);
        }

        private void OpenProtectionPicker()
        {
            if (!CanUseDraftSettings) return;
            ShowDraftPicker(ProtectionWindow, view.ProtectionPicker, view.BanProtection,
                state.room.banProtection ? 1 : 0, 2, (id, choice, token) => api.SetBanProtectionAsync(id, choice == 1, token));
        }

        private void ShowDraftPicker(string window, GameObject picker, GameButton source, int selected,
            int choices, Func<string, int, CancellationToken, Task<ArenaRoomState>> save)
        {
            windows.ShowOverlay(window, overlay =>
            {
                picker.SetActive(true);
                overlay.Add(() => { if (picker) picker.SetActive(false); });
                var bindings = picker.GetComponent<BindingMap>();
                bindings.Require<GameButton>("dismiss").Bind(overlay, _ =>
                {
                    windows.CloseOverlay(window);
                    return Task.CompletedTask;
                });
                for (int i = 0; i < choices; i++)
                {
                    int choice = i;
                    string suffix = i.ToString(CultureInfo.InvariantCulture);
                    var button = bindings.Require<GameButton>("choice" + suffix);
                    button.interactable = CanEditDraftSettings;
                    bindings.Require<Image>("selected" + suffix).enabled = choice == selected;
                    bindings.Require<GameText>("choiceLabel" + suffix).color =
                        choice == selected ? SelectedChoiceColor : UnselectedChoiceColor;
                    button.Bind(overlay, _ =>
                    {
                        // Saving belongs to the room, so closing the popup does not cancel it.
                        windows.CloseOverlay(window);
                        return choice == selected ? Task.CompletedTask :
                            SaveDraftSettingAsync(source, (id, token) => save(id, choice, token));
                    });
                }
            });
        }

        private async void ChangeFirstPick(GameButton source, string seat)
        {
            if (!CanEditDraftSettings || state.room.firstPick == seat || (seat == "guest" && GuestSeat == null)) return;
            await SaveDraftSettingAsync(source, (id, token) => api.SetFirstPickAsync(id, seat, token));
        }

        private async Task SaveDraftSettingAsync(GameButton source,
            Func<string, CancellationToken, Task<ArenaRoomState>> save, bool guestReadiness = false)
        {
            if (!CanUseDraftSettings || (guestReadiness ? !IsGuestSeat : !IsDraftHost)) return;
            var owner = screen;
            savingSettings = true;
            ++settingsRevision;
            RenderDraftSettings();
            try
            {
                var next = await save(state.room.id, owner.Token);
                if (StillOpen())
                {
                    Render(next);
                    actionError = null;
                    ShowStatus();
                }
            }
            catch (OperationCanceledException) when (owner.Token.IsCancellationRequested) { }
            catch (ArenaRequestException error) when (StillOpen())
            {
                GameUiFeedback.Play(source, GameUiFeedback.Sound.Blocked);
                toast.Show(error.Code == "draft_host_only" ? locale.Resolve("arena.error.room_settings_host_only") :
                    ArenaError.Text(locale, error), owner);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (StillOpen()) toast.Show(locale.Resolve("arena.error.request_failed"), owner);
            }
            finally
            {
                if (StillOpen())
                {
                    // A GET started while saving can still carry the pre-save settings.
                    ++settingsRevision;
                    savingSettings = false;
                    RenderDraftSettings();
                }
            }

            bool StillOpen() => screen == owner && !owner.Token.IsCancellationRequested;
        }

        private void SetPlayerName(GameText label, ArenaPlayer player)
        {
            bool isSelf = player != null && player.id == api.Player?.id;
            string name = isSelf ? string.Format(locale.Resolve("arena.player_self"), player.displayName) : player?.displayName;
            ArenaArt.SetText(label, name);
            label.color = isSelf ? SelfNameColor : Color.white;
        }

        private async void StartClick()
        {
            var owner = screen;
            if (owner == null || starting || leaving || savingSettings || transitioning) return;
            if (state.room.rule == "basic" && SupportsReady && IsGuestSeat)
            {
                bool ready = !GuestIsReady;
                GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Confirm);
                await SaveDraftSettingAsync(view.Start, (id, token) => api.SetReadyAsync(id, ready, token), guestReadiness: true);
                return;
            }
            string denial = state.room.rule != "basic" || openDraft == null ? "arena.error.draft_rule_unsupported"
                : state.room.host?.id != api.Player?.id ? "arena.error.draft_host_only"
                : GuestSeat == null ? "arena.no_player"
                : SupportsReady && !GuestIsReady ? "arena.error.draft_guest_not_ready" : null;
            if (denial != null)
            {
                GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Blocked);
                toast.Show(locale.Resolve(denial), owner);
                return;
            }
            starting = true;
            RenderDraftSettings();
            GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Confirm);
            try
            {
                var draft = await api.StartDraftAsync(state.room.id, owner.Token, requireReady: SupportsReady);
                if (scopeStillOpen()) await EnterDraftAsync(draft);
            }
            catch (OperationCanceledException) when (owner.Token.IsCancellationRequested) { }
            catch (ArenaRequestException error) when (!owner.Token.IsCancellationRequested)
            {
                GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Blocked);
                toast.Show(ArenaError.Text(locale, error), owner);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (scopeStillOpen()) toast.Show(locale.Resolve("arena.error.request_failed"), owner);
            }
            finally
            {
                starting = false;
                if (scopeStillOpen()) RenderDraftSettings();
            }

            bool scopeStillOpen() => screen == owner && !owner.Token.IsCancellationRequested;
        }

        private async Task EnterDraftAsync(ArenaDraftState draft)
        {
            if (draft == null || draft.phase == "cancelled" || transitioning || screen == null || screen.Token.IsCancellationRequested) return;
            var owner = screen;
            transitioning = true;
            RenderDraftSettings();
            windows.CloseOverlay(PrebanWindow);
            windows.CloseOverlay(ProtectionWindow);
            try { await openDraft(draft); }
            finally
            {
                transitioning = false;
                if (screen == owner && !owner.Token.IsCancellationRequested) RenderDraftSettings();
            }
        }

        private void ToggleStickerPicker()
        {
            if (windows.IsOverlayOpen(StickerWindow)) { windows.CloseOverlay(StickerWindow); return; }
            windows.ShowOverlay(StickerWindow, scope =>
            {
                var picker = view.EmojiPicker;
                picker.SetActive(true);
                scope.Add(() => { if (picker) picker.SetActive(false); });
                var map = picker.GetComponent<BindingMap>();
                map.Require<GameButton>("dismiss").Bind(scope, ignored =>
                {
                    windows.CloseOverlay(StickerWindow);
                    return Task.CompletedTask;
                });
                var scroller = map.Require<ScrollRect>("choices");
                foreach (var choice in scroller.content.GetComponentsInChildren<GameButton>())
                    choice.Bind(scope, ignored => SendStickerAsync(choice.name));
            });
        }

        private Task SendStickerAsync(string id) => SendChatAsync(id, isSticker: true, screen.Token);

        private Task SendAsync(CancellationToken token) => SendChatAsync(view.Message.text, isSticker: false, token);

        private async Task SendChatAsync(string value, bool isSticker, CancellationToken token)
        {
            if (leaving || sending || string.IsNullOrWhiteSpace(value)) return;
            sending = true;
            var owner = screen;
            try
            {
                var message = isSticker
                    ? await api.SendStickerAsync(state.room.id, value, token)
                    : await api.SendMessageAsync(state.room.id, value.Trim(), token);
                if (message == null || message.id <= 0) throw new ArenaRequestException("invalid_response");
                // The send response can arrive ahead of unseen messages from other players.
                // Only ascending history reads advance the cursor.
                AppendMessages(new[] { message });
                if (isSticker) windows.CloseOverlay(StickerWindow);
                else if (view.Message.text == value)
                    view.Message.SetTextWithoutNotify("");
                actionError = null;
                ShowStatus();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ArenaRequestException error)
            {
                if (leaving || token.IsCancellationRequested) return;
                if (IsRoomClosed(error)) { await ReturnToDirectoryAsync(); return; }
                actionError = ArenaError.Text(locale, error);
                ShowStatus();
            }
            finally { if (screen == owner) sending = false; }
        }

        private void AppendMessages(ArenaMessage[] incoming)
        {
            if (incoming.Length == 0) return;
            var history = view.Messages;
            bool follow = messages.Count == 0 || history.content.rect.height <= history.viewport.rect.height ||
                history.verticalNormalizedPosition <= .04f;
            var viewport = history.viewport;
            var anchor = follow ? null : messages.Values.Select(row => row.Root).FirstOrDefault(row =>
            {
                var bottom = viewport.InverseTransformPoint(row.TransformPoint(0, row.rect.yMin, 0)).y;
                var top = viewport.InverseTransformPoint(row.TransformPoint(0, row.rect.yMax, 0)).y;
                return top >= viewport.rect.yMin && bottom <= viewport.rect.yMax;
            });
            var contentParent = history.content.parent;
            float anchorY = anchor ? contentParent.InverseTransformPoint(anchor.position).y : 0;
            var layout = history.content.GetComponent<VerticalLayoutGroup>();
            bool changed = false;
            var now = DateTimeOffset.UtcNow;
            foreach (var message in incoming)
            {
                if (messages.ContainsKey(message.id) || messages.Count >= ChatHistoryLimit && message.id < messages.Keys[0])
                    continue;
                bool isSystem = message.kind == "system";
                var row = UnityEngine.Object.Instantiate(isSystem ? view.SystemRow : view.ChatRow, history.content, false);
                var map = row.GetComponent<BindingMap>();
                var body = map.Require<GameText>("body");
                Image sticker = null;
                if (isSystem)
                {
                    var text = message.systemCode switch
                    {
                        "first_pick_changed" => GameText.Format(locale, "arena.chat.first_pick_changed", message.text),
                        "preban_count_changed" => GameText.Format(locale, "arena.chat.preban_count_changed", message.text),
                        "ban_protection_enabled" => locale.Resolve("arena.chat.ban_protection_enabled"),
                        "ban_protection_disabled" => locale.Resolve("arena.chat.ban_protection_disabled"),
                        _ => message.text
                    };
                    ArenaArt.SetText(body, text);
                }
                else
                {
                    ArenaArt.SetAvatar(map.Require<BindingMap>("avatar"), message.player, false);
                    SetPlayerName(map.Require<GameText>("sender"), message.player);
                    sticker = map.Require<Image>("sticker");
                    bool isSticker = message.kind == "sticker";
                    if (isSticker && message.stickerId != null && stickers.TryGetValue(message.stickerId, out var sprite))
                        sticker.sprite = sprite;
                    sticker.enabled = sticker.sprite;
                    ArenaArt.SetText(body, isSticker ? (sticker.enabled ? "" : locale.Resolve("arena.chat.sticker_missing")) : message.text);
                }
                DateTimeOffset? createdAt = DateTimeOffset.TryParse(message.createdAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time) ? time : (DateTimeOffset?)null;

                row.SetActive(true);
                var size = row.GetComponent<LayoutElement>();
                float rowWidth = history.viewport.rect.width - layout.padding.horizontal;
                float textFraction = body.rectTransform.anchorMax.x - body.rectTransform.anchorMin.x;
                float bodyWidth = Mathf.Max(1, rowWidth * textFraction - body.margin.x - body.margin.z);
                float padding = isSystem ? 12f : 58f;
                size.preferredHeight = sticker && sticker.enabled ? 176 :
                    Mathf.Max(size.minHeight, padding + body.GetPreferredValues(body.text, bodyWidth, 0).y);
                var rowRect = (RectTransform)row.transform;
                var entry = new MessageRow(rowRect, isSystem ? null : map.Require<GameText>("age"), createdAt);
                SetMessageAge(entry, now);
                messages.Add(message.id, entry);
                rowRect.SetSiblingIndex(messages.IndexOfKey(message.id));
                if (messages.Count > ChatHistoryLimit)
                {
                    var oldest = messages.Values[0].Root.gameObject;
                    oldest.SetActive(false);
                    oldest.transform.SetAsLastSibling();
                    UnityEngine.Object.Destroy(oldest);
                    messages.RemoveAt(0);
                }
                changed = true;
            }
            if (!changed) return;
            LayoutRebuilder.ForceRebuildLayoutImmediate(history.content);
            if (follow) history.verticalNormalizedPosition = 0;
            else if (anchor && anchor.gameObject.activeSelf)
            {
                var position = history.content.anchoredPosition;
                position.y += anchorY - contentParent.InverseTransformPoint(anchor.position).y;
                history.content.anchoredPosition = position;
            }
            else history.verticalNormalizedPosition = 1;
        }

        private void RefreshMessageAges()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var row in messages.Values)
                SetMessageAge(row, now);
        }

        private void SetMessageAge(MessageRow row, DateTimeOffset now)
        {
            if (!row.Age) return;
            string text = row.CreatedAt.HasValue ? FormatMessageAge(now - row.CreatedAt.Value) : string.Empty;
            if (row.Age.text == text) return;
            ArenaArt.SetText(row.Age, text);
            if (row.Age.TryGetComponent<LayoutElement>(out var size))
                size.minWidth = row.Age.preferredWidth;
        }

        private string FormatMessageAge(TimeSpan elapsed)
        {
            if (elapsed.TotalMinutes < 1) return locale.Resolve("arena.chat.age_now");
            if (elapsed.TotalHours < 1)
                return string.Format(locale.Resolve("arena.chat.age_minutes"), (int)elapsed.TotalMinutes);
            if (elapsed.TotalDays < 1)
                return string.Format(locale.Resolve("arena.chat.age_hours"), (int)elapsed.TotalHours);
            return string.Format(locale.Resolve("arena.chat.age_days"), (int)elapsed.TotalDays);
        }

        private async Task LeaveAsync(CancellationToken token)
        {
            if (leaving) return;
            leaving = true;
            try
            {
                await api.LeaveAsync(state.room.id, token);
                await goBack();
            }
            catch (ArenaRequestException error) when (IsRoomClosed(error))
            {
                await goBack();
            }
            catch (ArenaRequestException error)
            {
                actionError = ArenaError.Text(locale, error);
                ShowStatus();
            }
            finally { leaving = false; }
        }

        private async Task ReturnToDirectoryAsync()
        {
            if (leaving) return;
            leaving = true;
            try { await goBack(); }
            finally { leaving = false; }
        }

        private static bool IsRoomClosed(ArenaRequestException error) =>
            error.Code == "not_in_room" || error.Code == "room_not_found" || error.Code == "unauthorized";

        private void ShowStatus()
        {
            string text = actionError ?? roomError ?? string.Empty;
            view.Status.text = text;
            view.StatusBoard.SetActive(text.Length != 0);
        }

        private void Close()
        {
            ShowArenaChat();
            toast.Hide();
            screen = null;
            sending = savingSettings = false;
            roster?.Dispose();
            roster = null;
            foreach (var row in messages.Values)
                if (row.Root) { row.Root.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Root.gameObject); }
            messages.Clear();
            cursor = 0;
            memberSignature = roomError = actionError = null;
            state = null;
            if (view.Root) view.Hide();
        }
    }
}
