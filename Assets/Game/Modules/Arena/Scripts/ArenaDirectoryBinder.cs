using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaDirectoryBinder : IDisposable
    {
        private readonly ArenaDirectory view;
        private readonly ILocaleService locale;
        private readonly ArenaApi api;
        private readonly ArenaCreateRoomBinder create;
        private readonly Func<ArenaRoomState, CancellationToken, Task> enter;
        private readonly Action goHome;
        private readonly string displayName;
        private BindingScope screen, rows, passwordScope;
        private string visibility = "all", sort = "recent", query = "";
        private bool busy;

        public ArenaDirectoryBinder(ArenaDirectory view, ArenaCreateRoom createView, ILocaleService locale, ArenaApi api,
            Func<ArenaRoomState, CancellationToken, Task> enter, Action goHome, string displayName)
        {
            this.view = view;
            this.locale = locale;
            this.api = api;
            this.enter = enter;
            this.goHome = goHome;
            this.displayName = displayName;
            create = new ArenaCreateRoomBinder(createView, locale, CreateAsync);
        }

        public async Task ShowAsync(CancellationToken token)
        {
            if (screen != null) return;
            screen = new BindingScope(token);
            view.Show();
            view.SearchPanel.SetActive(false);
            view.Choice.SetActive(false);
            view.Password.SetActive(false);
            view.Back.Bind(screen, _ => { goHome(); return Task.CompletedTask; });
            view.Create.Bind(screen, _ => { if (!busy) create.Open(screen.Token); return Task.CompletedTask; });
            view.Refresh.Bind(screen, RefreshAsync);
            view.Filter.Bind(screen, _ => { OpenFilter(); return Task.CompletedTask; });
            view.Search.Bind(screen, ToggleSearch);
            var choice = view.Choice.GetComponent<BindingMap>();
            var body = choice.Require<RectTransform>("body").GetComponent<BindingMap>();
            var dismiss = choice.Require<GameButton>("dismiss");
            dismiss.onClick.AddListener(CloseFilter);
            screen.Add(() => { if (dismiss) dismiss.onClick.RemoveListener(CloseFilter); });
            foreach (string value in new[] { "all", "public", "private", "players", "recent" })
            {
                var button = body.Require<GameButton>(value);
                UnityEngine.Events.UnityAction action = () => _ = ChooseFilter(value);
                button.onClick.AddListener(action);
                screen.Add(() => { if (button) button.onClick.RemoveListener(action); });
            }
            UpdateFilterLabel();
            view.SearchLabel.text = locale.Resolve(query.Length == 0 ? "arena.search" : "arena.cancel_search");
            await RefreshAsync(screen.Token, recoverRoom: true);
        }

        private void CloseFilter() => view.Choice.SetActive(false);

        private void OpenFilter()
        {
            view.SearchPanel.SetActive(false);
            view.SearchLabel.text = locale.Resolve(query.Length == 0 ? "arena.search" : "arena.cancel_search");
            var body = view.Choice.GetComponent<BindingMap>().Require<RectTransform>("body").GetComponent<BindingMap>();
            foreach (string key in new[] { "all", "public", "private" })
            {
                body.Require<GameButton>(key).targetGraphic.color = key == visibility
                    ? new Color32(91, 187, 55, 230) : Color.clear;
                body.Require<Image>(key + "_mark").enabled = key == visibility;
            }
            foreach (string key in new[] { "players", "recent" })
            {
                body.Require<GameButton>(key).targetGraphic.color = key == sort ? Color.white : Color.clear;
                body.Require<GameText>(key + "_label").color = key == sort ? new Color32(24, 27, 29, 255) : Color.white;
            }
            view.Choice.SetActive(!view.Choice.activeSelf);
        }

        private async Task ChooseFilter(string value)
        {
            if (screen == null || busy) return;
            if (value == "players" || value == "recent") sort = value;
            else visibility = value;
            CloseFilter();
            UpdateFilterLabel();
            var token = screen.Token;
            try { await RefreshAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); }
        }

        private void UpdateFilterLabel() => view.FilterLabel.text = locale.Resolve("arena.filter." + visibility);

        private Task ToggleSearch(CancellationToken token)
        {
            CloseFilter();
            bool opening = !view.SearchPanel.activeSelf && query.Length == 0;
            view.SearchPanel.SetActive(opening);
            view.SearchLabel.text = locale.Resolve(opening ? "arena.cancel_search" : "arena.search");
            if (opening)
            {
                view.SubmitSearch.Bind(screen, SearchAsync);
                view.Query.SetTextWithoutNotify(query);
                view.Query.Select();
                view.Query.ActivateInputField();
                return Task.CompletedTask;
            }
            if (query.Length == 0) return Task.CompletedTask;
            query = "";
            return RefreshAsync(token);
        }

        private async Task SearchAsync(CancellationToken token)
        {
            if (busy) return;
            query = view.Query.text.Trim();
            view.SearchLabel.text = locale.Resolve(query.Length == 0 ? "arena.search" : "arena.cancel_search");
            await RefreshAsync(token);
            view.SearchPanel.SetActive(false);
        }

        private Task RefreshAsync(CancellationToken token) => RefreshAsync(token, recoverRoom: false);

        private async Task RefreshAsync(CancellationToken token, bool recoverRoom)
        {
            if (busy) return;
            busy = true;
            view.Create.interactable = false;
            view.Status.text = locale.Resolve("arena.loading");
            try
            {
                await ConnectAsync(token);
                if (recoverRoom)
                {
                    var current = await api.CurrentAsync(token);
                    if (current != null) { await enter(current, token); return; }
                }
                var result = await api.ListAsync(visibility, sort, query, token);
                Render(result);
                view.Status.text = result.Length == 0 ? locale.Resolve("arena.empty") : "";
            }
            catch (ArenaRequestException error) { view.Status.text = ArenaError.Text(locale, error); }
            finally { busy = false; if (view.Root) view.Create.interactable = true; }
        }

        private Task ConnectAsync(CancellationToken token) => api.ConnectAsync(displayName, "c1001", token);

        private async Task CreateAsync(ArenaCreateRequest request, CancellationToken token)
        {
            if (busy) return;
            busy = true;
            try
            {
                await ConnectAsync(token);
                var result = await api.CreateAsync(request, token);
                await enter(result, token);
            }
            finally { busy = false; }
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
            view.List.verticalNormalizedPosition = 1;
        }

        private Task AskPassword(ArenaRoomInfo room)
        {
            ClosePassword();
            var map = view.Password.GetComponent<BindingMap>();
            var input = map.Require<TMPro.TMP_InputField>("input");
            var errorLabel = map.Require<GameText>("status");
            passwordScope = new BindingScope(screen.Token);
            input.SetTextWithoutNotify("");
            errorLabel.text = "";
            view.Password.SetActive(true);
            map.Require<GameButton>("cancel").Bind(passwordScope, _ => { ClosePassword(); return Task.CompletedTask; });
            map.Require<GameButton>("confirm").Bind(passwordScope, async token =>
            {
                if (!ArenaCreateRoomBinder.ValidPassword(input.text))
                    errorLabel.text = locale.Resolve("arena.error.invalid_password");
                else await JoinAsync(room.id, input.text, token, errorLabel);
            });
            return Task.CompletedTask;
        }

        private async Task JoinAsync(string id, string password, CancellationToken token, GameText status = null)
        {
            if (busy) return;
            busy = true;
            try
            {
                await ConnectAsync(token);
                var result = await api.JoinAsync(id, password, token);
                await enter(result, token);
            }
            catch (ArenaRequestException error) { (status ? status : view.Status).text = ArenaError.Text(locale, error); }
            finally { busy = false; }
        }

        private void ClosePassword()
        {
            passwordScope?.Dispose();
            passwordScope = null;
            if (view.Root) view.Password.SetActive(false);
        }

        public void Dispose()
        {
            create.Dispose();
            rows?.Dispose();
            rows = null;
            ClosePassword();
            screen?.Dispose();
            screen = null;
            if (view.Root) view.Hide();
        }
    }
}
