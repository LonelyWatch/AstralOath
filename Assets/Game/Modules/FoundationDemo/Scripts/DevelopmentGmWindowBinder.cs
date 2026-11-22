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
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed class DevelopmentGmWindowBinder
    {
        private const string Overlay = "development-gm";
        private readonly DevelopmentGmWindow view;
        private readonly WindowCoordinator windows;
        private readonly DevelopmentGmApi api;
        private readonly ILocaleService locale;
        private readonly Func<string, string, string> itemName;
        private readonly Action reconcile;
        private readonly Func<Task> refreshPage;
        private readonly List<(GameButton button, string key)> rows = new List<(GameButton, string)>();
        private BindingScope opening;
        private GmSnapshot snapshot;
        // Login-owned intent survives closing and reopening the panel after an ambiguous POST.
        private GmAction pending;
        private string kind = "set", selected;
        private BindingScope reading;
        private Task write;
        private bool pageChanged;
        private int fps;
        private bool inBattle;

        internal DevelopmentGmWindowBinder(DevelopmentGmWindow view, WindowCoordinator windows, DevelopmentGmApi api,
            ILocaleService locale, Func<string, string, string> itemName, Action reconcile, Func<Task> refreshPage)
        {
            this.view = view;
            this.windows = windows;
            this.api = api;
            this.locale = locale;
            this.itemName = itemName;
            this.reconcile = reconcile;
            this.refreshPage = refreshPage;
        }

        internal void Install(BindingScope owner)
        {
            view.Show();
            var canvas = view.Root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 150;
            view.Root.AddComponent<GraphicRaycaster>();
            var entry = view.Button("entry");
            var entryDrag = new DevelopmentGmEntryDrag(entry, owner);
            var home = view.Root.transform.parent;
            GameObject battleArea = null;
            void AttachToScene(Scene previous, Scene next)
            {
                entryDrag.Cancel();
                windows.CloseOverlay(Overlay);
                if (next.path == BattleOnlineLaunch.ScenePath)
                {
                    var hud = next.GetRootGameObjects().Select(root => root.GetComponentInChildren<BattleHudPreview>(true)).FirstOrDefault(item => item);
                    if (!hud) return;
                    battleArea = new GameObject("GM battle safe area", typeof(RectTransform), typeof(SafeAreaLayout));
                    battleArea.transform.SetParent(hud.transform, false);
                    view.Root.transform.SetParent(battleArea.transform, false);
                    // The transition has just suspended all home canvases, including this nested canvas.
                    canvas.enabled = true;
                    canvas.worldCamera = hud.GetComponent<Canvas>().worldCamera;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = 32000; // Below the online entry gate, above battle HUD and result.
                    inBattle = true;
                }
                else if (inBattle && home)
                {
                    view.Root.transform.SetParent(home, false);
                    canvas.worldCamera = home.GetComponentInParent<Canvas>().worldCamera;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = 150;
                    if (battleArea) Object.Destroy(battleArea);
                    battleArea = null;
                    inBattle = false;
                }
            }
            SceneManager.activeSceneChanged += AttachToScene;
            owner.Add(() =>
            {
                SceneManager.activeSceneChanged -= AttachToScene;
                if (battleArea) Object.Destroy(battleArea);
            });
            Listen(entry, owner, () => windows.ShowOverlay(Overlay, Open));
            owner.Add(view.Hide);
        }

        private void Open(BindingScope scope)
        {
            opening = scope;
            fps = 0;
            view.Panel.SetActive(true);
            var events = EventSystem.current;
            var previousSelection = events ? events.currentSelectedGameObject : null;
            if (events) events.SetSelectedGameObject(view.Button("close").gameObject);
            foreach (var control in view.Panel.GetComponentsInChildren<Selectable>(true))
                control.navigation = new Navigation { mode = Navigation.Mode.None };
            view.Filter.SetTextWithoutNotify("");
            scope.Add(() =>
            {
                opening = null;
                ClearRows();
                view.Panel.SetActive(false);
                if (events && previousSelection && previousSelection.activeInHierarchy)
                    events.SetSelectedGameObject(previousSelection);
            });
            void Tab(string button, string next) => Listen(view.Button(button), scope, () =>
            {
                kind = next;
                view.Filter.SetTextWithoutNotify("");
                Select(null);
                Draw();
            });
            Tab("resources", "set");
            Tab("inventory", "hero");
            Tab("diagnostics", "diagnostics");
            foreach (var category in new[] { "hero", "artifact", "equipment" }) Tab(category, category);
            view.Filter.onValueChanged.AddListener(Search);
            scope.Add(() => view.Filter.onValueChanged.RemoveListener(Search));
            void Search(string _) { Select(null); DrawRows(); }

            view.Button("close").Bind(scope, _ => CloseAsync());
            view.Button("refresh").Bind(scope, _ => FetchAsync(scope, true));
            Listen(view.Button("submit"), scope, () =>
            {
                if (reading != scope && (write == null || write.IsCompleted)) write = SubmitAsync(scope);
            });
            Listen(view.Button("copy"), scope, () =>
            {
                GUIUtility.systemCopyBuffer = Report();
                Status("copied");
            });

            Select(null);
            Draw();
            _ = MeasureFramesAsync(scope.Token);
            _ = FetchAsync(scope, false);
        }

        private static void Listen(GameButton button, BindingScope scope, UnityAction action)
        {
            button.onClick.AddListener(action);
            scope.Add(() => button.onClick.RemoveListener(action));
        }

        private async Task CloseAsync()
        {
            windows.CloseOverlay(Overlay);
            if (write != null) await write;
            if (opening == null && pageChanged)
            {
                pageChanged = false;
                await refreshPage();
            }
        }

        private async Task FetchAsync(BindingScope scope, bool refreshData)
        {
            if (reading == scope) return;
            reading = scope;
            if (refreshData) Reconcile();
            Status("loading");
            try
            {
                if (write != null) await write;
                scope.Token.ThrowIfCancellationRequested();
                snapshot = await api.SendAsync(null, scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                Draw();
                Status(pending == null ? "refreshed" : "uncertain", pending?.key, pending?.value);
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (GmRejectedException error) { if (opening == scope) ShowRejection(error); }
            catch (Exception) { if (opening == scope) Status("offline"); }
            finally { if (reading == scope) reading = null; }
        }

        private void Reconcile()
        {
            pageChanged = true;
            reconcile();
        }

        private async Task SubmitAsync(BindingScope scope)
        {
            if (pending == null)
            {
                if (selected == null) { Status("select"); return; }
                if (!int.TryParse(view.Amount.text, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
                    kind == "set" && amount > 1_000_000_000 ||
                    kind != "set" && (amount < 1 || amount > 10))
                {
                    Status(kind == "set" ? "setAmount" : "grantAmount");
                    return;
                }
                pending = new GmAction { requestId = Guid.NewGuid().ToString("N"), action = kind, key = selected, value = amount };
            }

            Status("sending");
            try
            {
                snapshot = await api.SendAsync(pending, scope.Token);
                pending = null;
                Reconcile();
                if (opening == scope) { Draw(); Status("success"); }
            }
            catch (GmRejectedException error)
            {
                pending = null;
                if (opening == scope) { Draw(); ShowRejection(error); }
            }
            catch (Exception)
            {
                Reconcile();
                if (opening == scope)
                {
                    try { snapshot = await api.SendAsync(null, scope.Token); }
                    catch (Exception) { /* The original POST still needs an idempotent retry. */ }
                    if (opening == scope) { Draw(); Status("uncertain", pending.key, pending.value); }
                }
            }
        }

        private string T(string key, params object[] args) => GameText.Format(locale, "gm." + key, args);
        private void Status(string key, params object[] args)
        {
            string status = T(key, args);
            view.Label("status").text = inBattle ? T("battleStatus", status) : status;
        }

        private void ShowRejection(GmRejectedException error)
        {
            if (error.Code == "gm_inventory_full" || error.Code == "equipment_inventory_full") Status("full");
            else if (error.Status == 404) Status("disabled");
            else if (error.Status == 401) Status("expired");
            else Status("rejected", error.Status);
        }

        private void Draw()
        {
            bool diag = kind == "diagnostics";
            bool inventory = !diag && kind != "set";
            view.Editor.SetActive(!diag);
            view.InventoryKinds.SetActive(inventory);
            view.Label("report").gameObject.SetActive(diag);
            view.Button("copy").gameObject.SetActive(diag);
            foreach (var tab in new[] { "resources", "inventory", "diagnostics", "hero", "artifact", "equipment" })
            {
                bool active = tab == kind || tab == "resources" && kind == "set" || tab == "inventory" && inventory;
                var button = view.Button(tab);
                button.interactable = !active;
                button.GetComponentInChildren<GameText>(true).color = active ? new Color32(251, 209, 128, 255) : Color.white;
            }
            view.Label("report").text = diag ? Report() : "";
            view.Label("submitLabel").text = T(pending != null ? "retry" : kind == "set" ? "submit" : "grant");
            view.Label("amountLabel").text = T(kind == "set" ? "targetBalance" : "grantCount");
            DrawRows();
        }

        private void DrawRows()
        {
            ClearRows();
            if (snapshot == null || kind == "diagnostics") return;
            IEnumerable<string> keys = kind switch
            {
                "set" => snapshot.resources.Select(row => row.key),
                "hero" => snapshot.heroes,
                "artifact" => snapshot.artifacts,
                _ => snapshot.equipment
            };
            string filter = view.Filter.text.Trim();
            foreach (string key in keys)
            {
                string name = Name(key);
                string label = kind == "set" ? name + "    " + Balance(key).ToString("N0") : name == key ? key : name + "   " + key;
                if (key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                var row = Object.Instantiate(view.Row, view.Items.content, false);
                rows.Add((row, key));
                row.GetComponentInChildren<GameText>(true).text = label;
                row.onClick.AddListener(() => Select(key));
                row.gameObject.SetActive(true);
            }
            view.Items.verticalNormalizedPosition = 1;
            Select(rows.Any(row => row.key == selected) ? selected : rows.FirstOrDefault().key);
        }

        private void ClearRows()
        {
            foreach (var (row, _) in rows)
            {
                row.gameObject.SetActive(false);
                Object.Destroy(row.gameObject);
            }
            rows.Clear();
        }

        private int Balance(string key) => snapshot.resources.First(row => row.key == key).value;
        private string Name(string key)
        {
            if (kind != "set") return itemName(kind, key) ?? key;
            try { return T("res." + key); }
            catch (KeyNotFoundException) { return key; }
        }

        private void Select(string key)
        {
            selected = key;
            foreach (var row in rows) row.button.interactable = row.key != key;
            view.Label("selection").text = key == null ? T("select") :
                kind == "set" ? T("balance", Name(key), Balance(key)) : T("template", Name(key), key);
            view.Amount.SetTextWithoutNotify(key == null ? "" : kind == "set" ? Balance(key).ToString(CultureInfo.InvariantCulture) : "1");
        }

        private string Report() => T("report", Application.version, Application.unityVersion, Application.platform,
            Screen.width, Screen.height, Screen.fullScreenMode, fps > 0 ? fps.ToString() : T("unavailable"), SystemInfo.graphicsDeviceType,
            snapshot?.playerId ?? T("unavailable"), snapshot?.realmId ?? T("unavailable"));

        private async Task MeasureFramesAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    int frame = Time.frameCount;
                    double start = Time.realtimeSinceStartupAsDouble;
                    await Task.Delay(750, token);
                    token.ThrowIfCancellationRequested();
                    fps = Mathf.RoundToInt((float)((Time.frameCount - frame) / Math.Max(.001, Time.realtimeSinceStartupAsDouble - start)));
                    if (kind == "diagnostics") view.Label("report").text = Report();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }
}
