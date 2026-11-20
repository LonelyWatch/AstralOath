using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDirectoryBinder
    {
        private const string CreateWindow = "arena/create";
        private const string FilterWindow = "arena/filter";
        private const string SearchWindow = "arena/search";
        private const string PasswordWindow = "arena/password";
        private static readonly string[] VisibilityChoices = { "all", "public", "private" };
        private static readonly string[] SortChoices = { "players", "recent" };

        private readonly ArenaDirectory view;
        private readonly ILocaleService locale;
        private readonly ArenaApi api;
        private readonly WindowCoordinator windows;
        private readonly ArenaCreateRoomBinder create;
        private readonly ArenaPasswordEntryBinder passwordEntry;
        private readonly GameButton passwordConfirm;
        private readonly GameToastBinder toast;
        private readonly Func<ArenaRoomState, CancellationToken, Task> enter;
        private readonly Func<Task> goHome;
        private readonly string displayName;
        private sealed class RoomRow
        {
            internal BindingMap View;
            internal ArenaRoomInfo Room;
        }

        private readonly List<RoomRow> rows = new List<RoomRow>();
        private BindingScope screen;
        private readonly Sprite searchIdle, searchIdlePressed;
        private string visibility = "all", sort = "players", query = "";
        private bool ascending, busy, hasList;
        private string renderedLocale;

        public ArenaDirectoryBinder(ArenaDirectory view, ArenaCreateRoom createView, ILocaleService locale, ArenaApi api,
            WindowCoordinator windows, Func<ArenaRoomState, CancellationToken, Task> enter, Func<Task> goHome, string displayName)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.windows = windows;
            this.enter = enter;
            this.goHome = goHome;
            this.displayName = displayName;
            searchIdle = ((Image)view.Search.targetGraphic).sprite;
            searchIdlePressed = view.Search.spriteState.pressedSprite;
            toast = new GameToastBinder(GameToast.ForInstance(view.Toast));
            var passwordView = ArenaPasswordEntry.FromInstance(view.Password);
            passwordConfirm = passwordView.Confirm;
            passwordEntry = new ArenaPasswordEntryBinder(passwordView, locale, toast);
            create = new ArenaCreateRoomBinder(createView, locale, CreateAsync, () => windows.CloseOverlay(CreateWindow), toast);
        }

        public Task OpenAsync(BindingScope owner) => OpenAsync(owner, null);

        public async Task OpenAfterBattleAsync(BindingScope owner)
        {
            var directoryConfirmed = new TaskCompletionSource<bool>();
            var opening = OpenAsync(owner, directoryConfirmed);
            if (await Task.WhenAny(opening, directoryConfirmed.Task) == opening) await opening;
            else _ = FinishListingAsync(opening);
        }

        private static async Task FinishListingAsync(Task opening)
        {
            try { await opening; }
            catch (Exception error) { Debug.LogException(error); }
        }

        private async Task OpenAsync(BindingScope owner, TaskCompletionSource<bool> directoryConfirmed)
        {
            screen = owner;
            view.Show();
            owner.Add(Close);
            view.SearchPanel.SetActive(false);
            view.Choice.SetActive(false);
            view.Password.SetActive(false);
            view.Back.BindBack(owner, () => windows.TryCloseOverlay() ? Task.CompletedTask : goHome());
            view.Create.Bind(owner, ignored =>
            {
                if (!busy)
                {
                    windows.ShowOverlay(CreateWindow, create.Open);
                    _ = api.RecordOperationAsync("create_dialog_open");
                }
                return Task.CompletedTask;
            });
            view.Refresh.Bind(owner, token => RefreshAsync(token, recoverRoom: true, operation: "directory_refresh"));
            view.Filter.Bind(owner, _ => { ToggleFilter(); return Task.CompletedTask; });
            view.Search.Bind(owner, ToggleSearch);
            UpdateFilterLabel();
            UpdateSearchLabel();
            await RefreshAsync(owner.Token, recoverRoom: true, directoryConfirmed: directoryConfirmed);
        }

        private void ToggleFilter()
        {
            if (windows.IsOverlayOpen(FilterWindow)) { windows.CloseOverlay(FilterWindow); return; }
            windows.ShowOverlay(FilterWindow, scope =>
            {
                var choice = view.Choice.GetComponent<BindingMap>();
                var body = choice.Require<RectTransform>("body").GetComponent<BindingMap>();
                view.Choice.SetActive(true);
                scope.Add(() => { if (view.Root) view.Choice.SetActive(false); });
                choice.Require<GameButton>("dismiss").Bind(scope, _ =>
                {
                    windows.CloseOverlay(FilterWindow);
                    return Task.CompletedTask;
                });
                foreach (string key in VisibilityChoices)
                    body.Require<GameButton>(key).Bind(scope, _ => ChooseFilterAsync(body, key));
                foreach (string key in SortChoices)
                    body.Require<GameButton>(key).Bind(scope, _ => ChooseFilterAsync(body, key));
                RenderFilter(body);
            });
        }

        private void RenderFilter(BindingMap body)
        {
            foreach (string key in VisibilityChoices)
            {
                body.Require<GameButton>(key).targetGraphic.color = key == visibility
                    ? new Color32(38, 79, 27, 255) : Color.clear;
                body.Require<Image>(key + "_mark").enabled = key == visibility;
            }
            foreach (string key in SortChoices)
            {
                body.Require<GameButton>(key).targetGraphic.color = key == sort ? Color.white : Color.clear;
                body.Require<GameText>(key + "_label").color = key == sort ? new Color32(24, 27, 29, 255) : Color.white;
            }
            foreach (string key in SortChoices)
            {
                body.Require<Image>(key + "_up").enabled = key == sort && ascending;
                body.Require<Image>(key + "_down").enabled = key == sort && !ascending;
            }
        }

        private Task ChooseFilterAsync(BindingMap body, string value)
        {
            if (busy) return Task.CompletedTask;
            if (value == "players" || value == "recent")
            {
                ascending = value == sort && !ascending;
                sort = value;
            }
            else visibility = value;
            UpdateFilterLabel();
            RenderFilter(body);
            return RefreshAsync(screen.Token, operation: "directory_filter", resetPosition: true);
        }

        private void UpdateFilterLabel() => view.FilterLabel.text = locale.Resolve("arena.filter." + visibility);

        private void UpdateSearchLabel()
        {
            bool selected = windows.IsOverlayOpen(SearchWindow) || query.Length != 0;
            var graphic = (Image)view.Search.targetGraphic;
            graphic.sprite = selected ? view.SearchSelected.sprite : searchIdle;
            graphic.overrideSprite = null;
            var states = view.Search.spriteState;
            states.pressedSprite = selected ? view.SearchPressed.sprite : searchIdlePressed;
            view.Search.spriteState = states;
            view.SearchIcon.enabled = !selected;
            view.SearchLabel.color = selected ? new Color32(27, 31, 37, 255) : Color.white;
            view.SearchLabel.text = locale.Resolve(selected ? "arena.cancel_search" : "arena.search");
        }

        private Task ToggleSearch(CancellationToken token)
        {
            if (busy) return Task.CompletedTask;
            if (windows.IsOverlayOpen(SearchWindow) || query.Length != 0)
            {
                windows.CloseOverlay(SearchWindow);
                bool hasFilter = query.Length != 0;
                query = "";
                UpdateSearchLabel();
                return hasFilter ? RefreshAsync(token, resetPosition: true) : Task.CompletedTask;
            }
            windows.ShowOverlay(SearchWindow, scope =>
            {
                view.SearchPanel.SetActive(true);
                scope.Add(() =>
                {
                    if (!view.Root) return;
                    view.SearchPanel.SetActive(false);
                    UpdateSearchLabel();
                });
                view.Query.SetTextWithoutNotify(query);
                view.SubmitSearch.Bind(scope, SearchAsync);
            });
            UpdateSearchLabel();
            return Task.CompletedTask;
        }

        private Task SearchAsync(CancellationToken token)
        {
            if (busy) return Task.CompletedTask;
            query = view.Query.text.Trim();
            view.Query.DeactivateInputField();
            return RefreshAsync(token, operation: "directory_search", resetPosition: true);
        }

        private async Task RefreshAsync(CancellationToken token, bool recoverRoom = false, string operation = null,
            TaskCompletionSource<bool> directoryConfirmed = null, bool resetPosition = false)
        {
            if (busy) return;
            busy = true;
            var owner = screen;
            view.Create.interactable = false;
            if (!hasList) view.Status.text = locale.Resolve("arena.loading");
            using var listLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            Task<ArenaRoomInfo[]> pendingList = null;
            try
            {
                await ConnectAsync(token);
                var membership = recoverRoom ? api.CurrentAsync(token) : null;
                // Initial/recovery navigation checks membership first. A manual refresh of an
                // established directory can read both, but only membership decides navigation.
                if (membership == null || hasList)
                    pendingList = api.ListAsync(visibility, sort, ascending, query, listLifetime.Token);
                if (operation != null) _ = api.RecordOperationAsync(operation);
                if (membership != null)
                {
                    var current = await membership;
                    token.ThrowIfCancellationRequested();
                    if (current != null) { await enter(current, token); return; }
                    directoryConfirmed?.TrySetResult(true);
                }
                var listing = pendingList ?? api.ListAsync(visibility, sort, ascending, query, token);
                pendingList = null;
                var result = await listing;
                token.ThrowIfCancellationRequested();
                Render(result, !hasList || resetPosition);
                hasList = true;
                view.Status.text = result.Length == 0 ? locale.Resolve("arena.empty") : "";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (ArenaRequestException error) { if (screen == owner) view.Status.text = ArenaError.Text(locale, error); }
            finally
            {
                if (pendingList != null)
                {
                    listLifetime.Cancel();
                    _ = DiscardListAsync(pendingList);
                }
                if (screen == owner)
                {
                    busy = false;
                    if (view.Root) view.Create.interactable = true;
                }
            }
        }

        private static async Task DiscardListAsync(Task<ArenaRoomInfo[]> listing)
        {
            try { await listing; }
            catch (OperationCanceledException) { }
            catch (ArenaRequestException) { }
        }

        private Task ConnectAsync(CancellationToken token) => api.ConnectAsync(displayName, "c1001", token);

        private async Task CreateAsync(ArenaCreateRequest request, CancellationToken token)
        {
            if (busy) return;
            busy = true;
            var owner = screen;
            try
            {
                await ConnectAsync(token);
                var result = await api.CreateAsync(request, token);
                GameUiFeedback.Play(view.Create, GameUiFeedback.Sound.Confirm);
                await enter(result, token);
            }
            finally { if (screen == owner) busy = false; }
        }

        private void Render(ArenaRoomInfo[] entries, bool resetPosition)
        {
            while (rows.Count > entries.Length) RemoveLastRow();
            for (int i = 0; i < entries.Length; ++i)
            {
                var room = entries[i];
                if (i == rows.Count)
                {
                    var clone = UnityEngine.Object.Instantiate(view.Row, view.List.content, false);
                    var added = new RoomRow { View = clone.GetComponent<BindingMap>() };
                    rows.Add(added);
                    clone.SetActive(true);
                    added.View.Require<GameButton>("join").Bind(screen, token => added.Room.requiresPassword
                        ? AskPassword(added.Room) : JoinAsync(added.Room.id, "", token));
                }
                var entry = rows[i];
                var previous = entry.Room;
                entry.Room = room;
                if (renderedLocale == locale.TextLocale && SameRow(previous, room)) continue;
                var row = entry.View;
                ArenaArt.SetText(row.Require<GameText>("name"), room.name);
                ArenaArt.SetText(row.Require<GameText>("hostName"), room.host?.displayName);
                row.Require<GameText>("rule").text = locale.Resolve("arena.rule." + room.rule);
                row.Require<GameText>("count").text = $"{room.memberCount}/{room.capacity}";
                row.Require<Image>("lock").enabled = room.requiresPassword;
                ArenaArt.SetAvatar(row.Require<BindingMap>("avatar"), room.host, true);
            }
            renderedLocale = locale.TextLocale;
            if (resetPosition)
            {
                view.List.StopMovement();
                var content = view.List.content;
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            }
        }

        private static bool SameRow(ArenaRoomInfo previous, ArenaRoomInfo next) =>
            previous != null && previous.id == next.id && previous.name == next.name && previous.rule == next.rule &&
            previous.requiresPassword == next.requiresPassword && previous.memberCount == next.memberCount &&
            previous.capacity == next.capacity && previous.host?.id == next.host?.id &&
            previous.host?.displayName == next.host?.displayName && previous.host?.avatarId == next.host?.avatarId;

        private void RemoveLastRow()
        {
            int index = rows.Count - 1;
            var row = rows[index].View;
            rows.RemoveAt(index);
            if (!row) return;
            row.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(row.gameObject);
        }

        private Task AskPassword(ArenaRoomInfo room)
        {
            windows.ShowOverlay(PasswordWindow, scope => passwordEntry.Open(scope,
                (pin, token) => JoinAsync(room.id, pin, token, passwordConfirm),
                () => windows.CloseOverlay(PasswordWindow)));
            return Task.CompletedTask;
        }

        private async Task JoinAsync(string id, string password, CancellationToken token, GameButton confirmation = null)
        {
            if (busy) return;
            busy = true;
            var owner = screen;
            try
            {
                await ConnectAsync(token);
                var result = await api.JoinAsync(id, password, token);
                if (confirmation) GameUiFeedback.Play(confirmation, GameUiFeedback.Sound.Confirm);
                await enter(result, token);
            }
            catch (ArenaRequestException error) when (!confirmation)
            {
                if (!token.IsCancellationRequested)
                {
                    GameUiFeedback.Play(view.List, GameUiFeedback.Sound.Blocked);
                    toast.Show(ArenaError.Text(locale, error), owner);
                }
            }
            finally { if (screen == owner) busy = false; }
        }

        private void Close()
        {
            screen = null;
            busy = false;
            hasList = false;
            while (rows.Count > 0) RemoveLastRow();
            if (view.Root)
            {
                view.Create.interactable = true;
                view.Hide();
            }
        }
    }
}
