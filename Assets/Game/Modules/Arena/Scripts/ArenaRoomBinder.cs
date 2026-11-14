using System;
using System.Collections.Generic;
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
        private readonly WindowCoordinator windows;
        private const int ChatHistoryLimit = 100;
        private const string StickerWindow = "arena/stickers";
        private readonly Dictionary<string, Sprite> stickers = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly SortedList<long, RectTransform> messages = new SortedList<long, RectTransform>();
        private BindingScope screen, roster;
        private ArenaRoomState state;
        private long cursor;
        private string memberSignature, roomError, chatError, actionError;
        private bool leaving, sending;

        public ArenaRoomBinder(ArenaRoom view, ILocaleService locale, ArenaApi api, WindowCoordinator windows, Func<Task> goBack)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.goBack = goBack;
            this.windows = windows;
            var choices = view.EmojiPicker.GetComponent<BindingMap>().Require<ScrollRect>("choices");
            foreach (var choice in choices.content.GetComponentsInChildren<GameButton>(true))
                stickers.Add(choice.name, choice.GetComponent<BindingMap>().Require<Image>("art").sprite);
        }

        public Task OpenAsync(ArenaRoomState initial, BindingScope owner)
        {
            screen = owner;
            view.Show();
            owner.Add(Close);
            view.Status.text = "";
            view.Message.SetTextWithoutNotify("");
            Render(initial);
            view.Leave.Bind(screen, LeaveAsync);
            view.Send.interactable = initial.room.allowChat;
            view.Emoji.interactable = initial.room.allowChat;
            if (initial.room.allowChat)
            {
                view.Send.Bind(screen, SendAsync);
                view.Emoji.Bind(screen, ignored => { ToggleStickerPicker(); return Task.CompletedTask; });
            }
            _ = WatchRoomAsync(initial.room.id, screen.Token);
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
            view.Rule.text = locale.Resolve("arena.rule." + next.room.rule);
            view.Count.text = $"{next.room.memberCount}/{next.room.capacity}";
            view.Message.interactable = next.room.allowChat;
            if (memberSignature == signature) return;
            memberSignature = signature;
            var host = next.room.host;
            var guest = members.FirstOrDefault(member => member.id != host?.id);
            ArenaArt.SetText(view.HostName, host?.displayName);
            ArenaArt.SetText(view.GuestName, guest?.displayName);
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
                ArenaArt.SetText(map.Require<GameText>("name"), member.displayName);
                row.SetActive(true);
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
            var anchor = follow ? null : messages.Values.FirstOrDefault(row =>
            {
                var bottom = viewport.InverseTransformPoint(row.TransformPoint(0, row.rect.yMin, 0)).y;
                var top = viewport.InverseTransformPoint(row.TransformPoint(0, row.rect.yMax, 0)).y;
                return top >= viewport.rect.yMin && bottom <= viewport.rect.yMax;
            });
            var contentParent = history.content.parent;
            float anchorY = anchor ? contentParent.InverseTransformPoint(anchor.position).y : 0;
            var layout = history.content.GetComponent<VerticalLayoutGroup>();
            bool changed = false;
            foreach (var message in incoming)
            {
                if (messages.ContainsKey(message.id) || messages.Count >= ChatHistoryLimit && message.id < messages.Keys[0])
                    continue;
                var row = UnityEngine.Object.Instantiate(view.ChatRow, history.content, false);
                var map = row.GetComponent<BindingMap>();
                ArenaArt.SetAvatar(map.Require<BindingMap>("avatar"), message.player, false);
                var sender = map.Require<GameText>("sender");
                ArenaArt.SetText(sender, message.player?.displayName);
                sender.color = message.player?.id == api.Player?.id ? new Color32(137, 201, 62, 255) : Color.white;
                var body = map.Require<GameText>("body");
                var sticker = map.Require<Image>("sticker");
                bool isSticker = message.kind == "sticker";
                if (isSticker && message.stickerId != null && stickers.TryGetValue(message.stickerId, out var sprite))
                    sticker.sprite = sprite;
                sticker.enabled = sticker.sprite;
                ArenaArt.SetText(body, isSticker ? (sticker.enabled ? "" : locale.Resolve("arena.chat.sticker_missing")) : message.text);
                string age = DateTimeOffset.TryParse(message.createdAt, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var time) ? time.ToLocalTime().ToString("HH:mm") : "";
                ArenaArt.SetText(map.Require<GameText>("age"), age);
                row.SetActive(true);
                var size = row.GetComponent<LayoutElement>();
                float rowWidth = history.viewport.rect.width - layout.padding.horizontal;
                float textFraction = body.rectTransform.anchorMax.x - body.rectTransform.anchorMin.x;
                float bodyWidth = Mathf.Max(1, rowWidth * textFraction - body.margin.x - body.margin.z);
                size.preferredHeight = sticker.enabled ? 164 : Mathf.Max(size.minHeight, 49 + body.GetPreferredValues(body.text, bodyWidth, 0).y);
                var rowRect = (RectTransform)row.transform;
                messages.Add(message.id, rowRect);
                rowRect.SetSiblingIndex(messages.IndexOfKey(message.id));
                if (messages.Count > ChatHistoryLimit)
                {
                    var oldest = messages.Values[0].gameObject;
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

        private void ShowStatus() => view.Status.text = actionError ?? chatError ?? roomError ?? "";

        private void Close()
        {
            screen = null;
            sending = false;
            roster?.Dispose();
            roster = null;
            foreach (var row in messages.Values)
                if (row) { row.gameObject.SetActive(false); UnityEngine.Object.Destroy(row.gameObject); }
            messages.Clear();
            cursor = 0;
            memberSignature = roomError = chatError = actionError = null;
            state = null;
            if (view.Root) view.Hide();
        }
    }
}
