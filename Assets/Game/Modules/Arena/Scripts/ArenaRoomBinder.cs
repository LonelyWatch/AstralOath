using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
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
        private string memberSignature, roomError, chatError, actionError;
        private bool leaving, sending, starting, transitioning;

        public ArenaRoomBinder(ArenaRoom view, ILocaleService locale, ArenaApi api, WindowCoordinator windows, Func<Task> goBack, Func<ArenaDraftState, Task> openDraft = null)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.goBack = goBack;
            this.openDraft = openDraft;
            this.windows = windows;
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
            view.Send.interactable = initial.room.allowChat;
            view.Emoji.interactable = initial.room.allowChat;
            if (initial.room.allowChat)
            {
                view.Send.Bind(screen, SendAsync);
                view.Emoji.Bind(screen, ignored => { ToggleStickerPicker(); return Task.CompletedTask; });
            }
            _ = WatchRoomAsync(initial.room.id, screen.Token);
            if (openDraft != null && initial.room.rule == "basic") _ = WatchDraftAsync(initial.room.id, screen.Token);
            if (initial.room.allowChat)
                _ = WatchChatAsync(initial.room.id, screen.Token);
            return Task.CompletedTask;
        }

        private async Task WatchRoomAsync(string id, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    try
                    {
                        Render(await api.GetAsync(id, token));
                        RefreshMessageAges();
                        roomError = null;
                        ShowStatus();
                    }
                    catch (ArenaRequestException error)
                    {
                        if (token.IsCancellationRequested) return;
                        if (IsRoomClosed(error) && !leaving) { await ReturnToDirectoryAsync(); return; }
                        roomError = ArenaError.Text(locale, error);
                        ShowStatus();
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        private async Task WatchChatAsync(string id, CancellationToken token)
        {
            var minimumIdleInterval = TimeSpan.FromSeconds(2);
            try
            {
                while (true)
                {
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var incoming = await api.ReadMessagesAsync(id, cursor, token, waitForNewMessages: true);
                        if (incoming.Length > 0)
                        {
                            AppendMessages(incoming);
                            cursor = incoming[incoming.Length - 1].id;
                        }
                        chatError = null;
                        ShowStatus();
                        if (incoming.Length > 0) continue;
                    }
                    catch (ArenaRequestException error)
                    {
                        if (token.IsCancellationRequested) return;
                        if (IsRoomClosed(error) && !leaving) { await ReturnToDirectoryAsync(); return; }
                        chatError = ArenaError.Text(locale, error);
                        ShowStatus();
                    }
                    // Older servers return empty reads immediately.
                    var remaining = minimumIdleInterval - elapsed.Elapsed;
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); }
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
            view.Start.GetComponent<CanvasGroup>().alpha =
                next.room.rule == "basic" && next.room.host?.id == api.Player?.id && members.Length >= 2 ? 1f : .55f;
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
            if (owner == null || starting || leaving) return;
            string denial = state.room.rule != "basic" || openDraft == null ? "arena.error.draft_rule_unsupported"
                : state.room.host?.id != api.Player?.id ? "arena.error.draft_host_only"
                : (state.members?.Length ?? 0) < 2 ? "arena.no_player" : null;
            if (denial != null)
            {
                GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Blocked);
                toast.Show(locale.Resolve(denial), owner);
                return;
            }
            starting = true;
            GameUiFeedback.Play(view.Start, GameUiFeedback.Sound.Confirm);
            try
            {
                var draft = await api.StartDraftAsync(state.room.id, owner.Token);
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
            finally { starting = false; }

            bool scopeStillOpen() => screen == owner && !owner.Token.IsCancellationRequested;
        }

        private async Task WatchDraftAsync(string id, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    await Task.Delay(750, token);
                    if (transitioning || starting || leaving) continue;
                    try { await EnterDraftAsync(await api.DraftAsync(id, token)); }
                    catch (ArenaRequestException error) when (error.Code is "not_in_room" or "room_not_found")
                    { return; }
                    catch (ArenaRequestException) { }
                    if (token.IsCancellationRequested) return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        private async Task EnterDraftAsync(ArenaDraftState draft)
        {
            if (draft == null || draft.phase == "cancelled" || transitioning || screen == null || screen.Token.IsCancellationRequested) return;
            transitioning = true;
            try { await openDraft(draft); }
            finally { transitioning = false; }
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
                var row = UnityEngine.Object.Instantiate(view.ChatRow, history.content, false);
                var map = row.GetComponent<BindingMap>();
                ArenaArt.SetAvatar(map.Require<BindingMap>("avatar"), message.player, false);
                var sender = map.Require<GameText>("sender");
                SetPlayerName(sender, message.player);

                var body = map.Require<GameText>("body");
                var sticker = map.Require<Image>("sticker");
                bool isSticker = message.kind == "sticker";
                if (isSticker && message.stickerId != null && stickers.TryGetValue(message.stickerId, out var sprite))
                    sticker.sprite = sprite;
                sticker.enabled = sticker.sprite;
                ArenaArt.SetText(body, isSticker ? (sticker.enabled ? "" : locale.Resolve("arena.chat.sticker_missing")) : message.text);
                DateTimeOffset? createdAt = DateTimeOffset.TryParse(message.createdAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var time) ? time : (DateTimeOffset?)null;

                row.SetActive(true);
                var size = row.GetComponent<LayoutElement>();
                float rowWidth = history.viewport.rect.width - layout.padding.horizontal;
                float textFraction = body.rectTransform.anchorMax.x - body.rectTransform.anchorMin.x;
                float bodyWidth = Mathf.Max(1, rowWidth * textFraction - body.margin.x - body.margin.z);
                size.preferredHeight = sticker.enabled ? 176 : Mathf.Max(size.minHeight, 58 + body.GetPreferredValues(body.text, bodyWidth, 0).y);
                var rowRect = (RectTransform)row.transform;
                var entry = new MessageRow(rowRect, map.Require<GameText>("age"), createdAt);
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
            string text = actionError ?? chatError ?? roomError ?? string.Empty;
            view.Status.text = text;
            view.StatusBoard.SetActive(text.Length != 0);
        }

        private void Close()
        {
            toast.Hide();
            screen = null;
            sending = false;
            roster?.Dispose();
            roster = null;
            foreach (var row in messages.Values)
                if (row.Root) { row.Root.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.Root.gameObject); }
            messages.Clear();
            cursor = 0;
            memberSignature = roomError = chatError = actionError = null;
            state = null;
            if (view.Root) view.Hide();
        }
    }
}
