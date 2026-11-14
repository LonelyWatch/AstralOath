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
        private const string StickerWindow = "arena/stickers";
        private readonly Dictionary<string, Sprite> stickers = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly List<GameObject> messages = new List<GameObject>();
        private BindingScope screen, roster;
        private ArenaRoomState state;
        private long cursor;
        private string memberSignature;
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
            _ = WatchAsync(screen.Token);
            return Task.CompletedTask;
        }

        private async Task WatchAsync(CancellationToken token)
        {
            try
            {
                while (true)
                {
                    string id = state.room.id;
                    try
                    {
                        var next = await api.GetAsync(id, token);
                        Render(next);
                        if (next.room.allowChat)
                            AppendMessages(await api.ReadMessagesAsync(id, cursor, token));
                        view.Status.text = "";
                    }
                    catch (ArenaRequestException error)
                    {
                        if (leaving || token.IsCancellationRequested) return;
                        view.Status.text = ArenaError.Text(locale, error);
                        if (error.Code == "not_in_room" || error.Code == "room_not_found" || error.Code == "unauthorized") return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2), token);
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

        private async Task SendStickerAsync(string id)
        {
            if (sending) return;
            sending = true;
            var owner = screen;
            windows.CloseOverlay(StickerWindow);
            try { await api.SendStickerAsync(state.room.id, id, owner.Token); }
            catch (OperationCanceledException) when (owner.Token.IsCancellationRequested) { }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally { if (screen == owner) sending = false; }
        }

        private async Task SendAsync(CancellationToken token)
        {
            string message = view.Message.text.Trim();
            if (message.Length == 0 || sending) return;
            sending = true;
            var owner = screen;
            try
            {
                await api.SendMessageAsync(state.room.id, message, token);
                view.Message.SetTextWithoutNotify("");
            }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally { if (screen == owner) sending = false; }
        }

        private void AppendMessages(ArenaMessage[] incoming)
        {
            if (incoming.Length == 0) return;
            var history = view.Messages;
            bool follow = messages.Count == 0 || history.content.rect.height <= history.viewport.rect.height ||
                history.verticalNormalizedPosition <= .04f;
            float previousY = history.content.anchoredPosition.y;
            float removedHeight = 0;
            var layout = history.content.GetComponent<VerticalLayoutGroup>();
            foreach (var message in incoming)
            {
                if (message.id <= cursor) continue;
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
                messages.Add(row);
                if (messages.Count > 100)
                {
                    removedHeight += ((RectTransform)messages[0].transform).rect.height +
                        layout.spacing;
                    messages[0].SetActive(false);
                    UnityEngine.Object.Destroy(messages[0]);
                    messages.RemoveAt(0);
                }
                cursor = message.id;
            }
            Canvas.ForceUpdateCanvases();
            if (follow) history.verticalNormalizedPosition = 0;
            else
            {
                var position = history.content.anchoredPosition;
                position.y = Mathf.Max(0, previousY - removedHeight);
                history.content.anchoredPosition = position;
            }
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
            catch (ArenaRequestException error) when (error.Code == "not_in_room" || error.Code == "room_not_found" || error.Code == "unauthorized")
            {
                await goBack();
            }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally { leaving = false; }
        }

        private void Close()
        {
            screen = null;
            sending = false;
            roster?.Dispose();
            roster = null;
            foreach (var row in messages) if (row) { row.SetActive(false); UnityEngine.Object.Destroy(row); }
            messages.Clear();
            cursor = 0;
            memberSignature = null;
            state = null;
            if (view.Root) view.Hide();
        }
    }
}
