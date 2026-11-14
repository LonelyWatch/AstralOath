using System;
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
        private readonly Func<ArenaRoomState, CancellationToken, Task> enter;
        private readonly Func<Task> goHome;
        private readonly string displayName;
        private BindingScope screen, rows;
        private string visibility = "all", sort = "players", query = "";
        private bool ascending, busy;

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
            create = new ArenaCreateRoomBinder(createView, locale, CreateAsync, () => windows.CloseOverlay(CreateWindow));
        }

        public async Task OpenAsync(BindingScope owner)
        {
            screen = owner;
            view.Show();
            owner.Add(Close);
            view.SearchPanel.SetActive(false);
            view.Choice.SetActive(false);
            view.Password.SetActive(false);
            view.Back.Bind(owner, _ => goHome());
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
            await RefreshAsync(owner.Token, recoverRoom: true);
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
            return RefreshAsync(screen.Token, operation: "directory_filter");
        }

        private void UpdateFilterLabel() => view.FilterLabel.text = locale.Resolve("arena.filter." + visibility);

        private void UpdateSearchLabel()
        {
            bool selected = windows.IsOverlayOpen(SearchWindow) || query.Length != 0;
            view.SearchSelected.enabled = selected;
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
                return hasFilter ? RefreshAsync(token) : Task.CompletedTask;
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
            return RefreshAsync(token, operation: "directory_search");
        }

        private async Task RefreshAsync(CancellationToken token, bool recoverRoom = false, string operation = null)
        {
            if (busy) return;
            busy = true;
            var owner = screen;
            view.Create.interactable = false;
            view.Status.text = locale.Resolve("arena.loading");
            try
            {
                await ConnectAsync(token);
                if (operation != null) _ = api.RecordOperationAsync(operation);
                if (recoverRoom)
                {
                    var current = await api.CurrentAsync(token);
                    if (current != null) { await enter(current, token); return; }
                }
                var result = await api.ListAsync(visibility, sort, ascending, query, token);
                Render(result);
                view.Status.text = result.Length == 0 ? locale.Resolve("arena.empty") : "";
            }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally
            {
                if (screen == owner)
                {
                    busy = false;
                    if (view.Root) view.Create.interactable = true;
                }
            }
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
                await enter(result, token);
            }
            finally { if (screen == owner) busy = false; }
        }

        private void Render(ArenaRoomInfo[] entries)
        {
            rows?.Dispose();
            rows = new BindingScope(screen.Token);
            foreach (var room in entries)
            {
                var clone = UnityEngine.Object.Instantiate(view.Row, view.List.content, false);
                rows.Add(() => { if (clone) { clone.SetActive(false); UnityEngine.Object.Destroy(clone); } });
                var row = clone.GetComponent<BindingMap>();
                ArenaArt.SetText(row.Require<GameText>("name"), room.name);
                ArenaArt.SetText(row.Require<GameText>("hostName"), room.host?.displayName);
                row.Require<GameText>("rule").text = locale.Resolve("arena.rule." + room.rule);
                row.Require<GameText>("count").text = $"{room.memberCount}/{room.capacity}";
                row.Require<Image>("lock").enabled = room.requiresPassword;
                ArenaArt.SetAvatar(row.Require<BindingMap>("avatar"), room.host, true);
                clone.SetActive(true);
                row.Require<GameButton>("join").Bind(rows, token => room.requiresPassword
                    ? AskPassword(room) : JoinAsync(room.id, "", token));
            }
            Canvas.ForceUpdateCanvases();
            view.List.StopMovement();
            view.List.verticalNormalizedPosition = 1;
        }

        private Task AskPassword(ArenaRoomInfo room)
        {
            windows.ShowOverlay(PasswordWindow, scope =>
            {
                var map = view.Password.GetComponent<BindingMap>();
                var input = map.Require<TMPro.TMP_InputField>("input");
                var errorLabel = map.Require<GameText>("status");
                input.SetTextWithoutNotify("");
                errorLabel.text = "";
                view.Password.SetActive(true);
                scope.Add(() => { if (view.Root) view.Password.SetActive(false); });
                var cancel = map.Require<GameButton>("cancel");
                cancel.Bind(scope, _ =>
                {
                    windows.CloseOverlay(PasswordWindow);
                    return Task.CompletedTask;
                });
                map.Require<GameButton>("confirm").Bind(scope, async token =>
                {
                    if (!ArenaCreateRoomBinder.ValidPassword(input.text))
                    {
                        errorLabel.text = locale.Resolve("arena.error.invalid_password");
                        return;
                    }
                    cancel.interactable = input.interactable = false;
                    try { await JoinAsync(room.id, input.text, token, errorLabel); }
                    finally
                    {
                        if (cancel) cancel.interactable = true;
                        if (input) input.interactable = true;
                    }
                });
            });
            return Task.CompletedTask;
        }

        private async Task JoinAsync(string id, string password, CancellationToken token, GameText status = null)
        {
            if (busy) return;
            busy = true;
            var owner = screen;
            try
            {
                await ConnectAsync(token);
                var result = await api.JoinAsync(id, password, token);
                await enter(result, token);
            }
            catch (ArenaRequestException error) { (status ? status : view.Status).text = ArenaError.Text(locale, error); }
            finally { if (screen == owner) busy = false; }
        }

        private void Close()
        {
            screen = null;
            busy = false;
            rows?.Dispose();
            rows = null;
            if (view.Root)
            {
                view.Create.interactable = true;
                view.Hide();
            }
        }
    }
}
