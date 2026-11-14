using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaRoomBinder : IDisposable
    {
        private readonly ArenaRoom view;
        private readonly ILocaleService locale;
        private readonly ArenaApi api;
        private readonly Func<Task> goBack;
        private readonly List<GameText> messages = new List<GameText>();
        private BindingScope screen, roster;
        private ArenaRoomState state;
        private long cursor;
        private string memberSignature;
        private bool leaving;

        public ArenaRoomBinder(ArenaRoom view, ILocaleService locale, ArenaApi api, Func<Task> goBack)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.goBack = goBack;
        }

        public void Show(ArenaRoomState initial, CancellationToken token)
        {
            Dispose();
            screen = new BindingScope(token);
            view.Show();
            view.Status.text = "";
            view.Message.SetTextWithoutNotify("");
            Render(initial);
            view.Leave.Bind(screen, LeaveAsync);
            view.Send.interactable = initial.room.allowChat;
            if (initial.room.allowChat) view.Send.Bind(screen, SendAsync);
            _ = WatchAsync(screen.Token);
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
                            foreach (var message in await api.ReadMessagesAsync(id, cursor, token)) AddMessage(message);
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
            ArenaArt.SetText(view.GuestName, guest?.displayName ?? locale.Resolve("arena.no_player"));
            ArenaArt.SetAvatar(view.Host, host, true);
            view.Vacant.enabled = guest == null;
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

        private async Task SendAsync(CancellationToken token)
        {
            string message = view.Message.text.Trim();
            if (message.Length == 0) return;
            try
            {
                await api.SendMessageAsync(state.room.id, message, token);
                view.Message.SetTextWithoutNotify("");
            }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
        }

        private void AddMessage(ArenaMessage message)
        {
            if (message.id <= cursor) return;
            var row = UnityEngine.Object.Instantiate(view.ChatRow, view.Messages.content, false);
            ArenaArt.SetText(row, $"{message.player?.displayName}  {message.text}");
            row.gameObject.SetActive(true);
            messages.Add(row);
            if (messages.Count > 100)
            {
                messages[0].gameObject.SetActive(false);
                UnityEngine.Object.Destroy(messages[0].gameObject);
                messages.RemoveAt(0);
            }
            cursor = message.id;
            Canvas.ForceUpdateCanvases();
            view.Messages.verticalNormalizedPosition = 0;
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

        public void Dispose()
        {
            screen?.Dispose();
            screen = null;
            roster?.Dispose();
            roster = null;
            foreach (var row in messages) if (row) UnityEngine.Object.Destroy(row.gameObject);
            messages.Clear();
            cursor = 0;
            memberSignature = null;
            state = null;
            if (view.Root) view.Hide();
        }
    }
}
