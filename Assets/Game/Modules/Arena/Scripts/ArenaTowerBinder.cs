using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Arena
{
    public sealed class ArenaTowerBinder
    {
        private const string TeamWindow = "arena/tower/team";
        private readonly ArenaTower view;
        private readonly ILocaleService locale;
        private readonly WindowCoordinator windows;
        private readonly Func<CancellationToken, Task<TowerPageDto>> listing;
        private readonly Func<int, CancellationToken, Task<TowerTeamDto>> clearTeam;
        private readonly Func<CancellationToken, Task<TowerHeroDto[]>> inventory;
        private readonly Func<int, string[], CancellationToken, Task> startBattle;
        private readonly Func<string, Sprite> portrait;
        private readonly Func<string, string> heroName;
        private readonly Dictionary<string, ArenaDraftArt.Hero> art;
        private readonly Func<Task> home, simulation, shop;
        private readonly Action<string> notice;
        private readonly List<string> lastSelection = new List<string>();
        private BindingScope page;
        private GameObject highlight;
        private TowerStageDto selected;
        private bool starting;

        public ArenaTowerBinder(ArenaTower view, ILocaleService locale, WindowCoordinator windows,
            Func<CancellationToken, Task<TowerPageDto>> list, Func<int, CancellationToken, Task<TowerTeamDto>> clearTeam,
            Func<CancellationToken, Task<TowerHeroDto[]>> inventory, Func<int, string[], CancellationToken, Task> startBattle,
            Func<string, Sprite> portrait, Func<Task> home, Func<Task> simulation, Func<Task> shop, Action<string> notice,
            Func<string, string> heroName = null, ArenaDraftArt draftArt = null)
        {
            this.view = view; this.locale = locale; this.windows = windows; listing = list;
            this.clearTeam = clearTeam; this.inventory = inventory; this.startBattle = startBattle;
            this.portrait = portrait; this.home = home; this.simulation = simulation; this.shop = shop;
            this.notice = notice; this.heroName = heroName;
            art = draftArt ? draftArt.Heroes.ToDictionary(h => h.Id, StringComparer.Ordinal) : new Dictionary<string, ArenaDraftArt.Hero>();
        }

        public async Task OpenAsync(BindingScope scope)
        {
            page = scope;
            scope.Add(() =>
            {
                selected = null; highlight = null; starting = false; page = null;
                if (view.Root) view.Hide();
            });
            view.Show();
            view.Row.SetActive(false);
            view.Overlay.gameObject.SetActive(false);
            view.Button("back").BindBack(scope, () => windows.TryCloseOverlay() ? Task.CompletedTask : home());
            view.Button("simulation").Bind(scope, _ => simulation());
            view.Button("record").Bind(scope, _ => { notice(locale.Resolve("tower.record")); return Task.CompletedTask; });
            view.Button("shop").Bind(scope, _ => shop());
            var challenge = view.Button("challenge");
            challenge.Bind(scope, _ => { if (selected?.unlocked == true && !starting) OpenTeam(selected, true); return Task.CompletedTask; });
            challenge.interactable = false;
            view.Text("rank").text = view.Text("progress").text = "";
            view.RankIcon.enabled = false;
            view.Text("status").text = locale.Resolve("arena.loading");
            try
            {
                var state = await listing(scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                Render(scope, state);
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception) when (!scope.Token.IsCancellationRequested) { view.Text("status").text = locale.Resolve("arena.error.request_failed"); }
        }

        private void Render(BindingScope scope, TowerPageDto state)
        {
            var list = view.List;
            view.Text("rank").text = locale.Resolve("tower.rank." + state.rankId);
            view.RankIcon.sprite = view.RankArt(state.rankId);
            view.RankIcon.enabled = view.RankIcon.sprite != null;
            view.Text("progress").text = Format("tower.progress", state.highestCleared, state.stages.Length);
            var current = state.stages.First(s => s.stage == state.currentStage);
            view.Text("currentStage").text = Format("tower.target", current.stage);
            view.Text("stageReward").text = current.cleared ? "" : Format("tower.points", current.reward);
            view.Text("status").text = "";
            // A native row contains separate face, frame, class, two digits and up to six stars per hero.
            // Keep only the visible rows and one spare row on each side while retaining the full scroll extent.
            Canvas.ForceUpdateCanvases();
            var layout = list.content.GetComponent<VerticalLayoutGroup>();
            var fit = list.content.GetComponent<ContentSizeFitter>();
            layout.enabled = false;
            fit.enabled = false;
            float height = view.Row.GetComponent<LayoutElement>().preferredHeight;
            float step = height + layout.spacing;
            float fullHeight = state.stages.Length * step - layout.spacing;
            list.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, fullHeight);
            int capacity = Mathf.Min(state.stages.Length, Mathf.CeilToInt(list.viewport.rect.height / step) + 3);
            var rows = new BindingMap[capacity];
            var bound = new TowerStageDto[capacity];
            for (int i = 0; i < capacity; i++)
            {
                int slot = i;
                var row = rows[i] = Clone(view.Row, list.content, scope);
                var rect = (RectTransform)row.transform;
                rect.anchorMin = new Vector2(0,1);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(.5f,1);
                rect.sizeDelta = new Vector2(0,height);
                var choose = row.Require<GameButton>("choose");
                choose.KeepBrightnessOnPress = true;
                choose.Bind(scope, _ =>
                {
                    if (bound[slot]?.unlocked == true) Select(bound[slot],row.Require<GameObject>("selected"));
                    return Task.CompletedTask;
                });
                row.Require<GameButton>("inspect").Bind(scope, _ =>
                {
                    if (!starting && bound[slot]?.cleared == true) OpenTeam(bound[slot],false);
                    return Task.CompletedTask;
                });
            }

            int currentIndex = Array.IndexOf(state.stages,current);
            float overflow = Mathf.Max(0,fullHeight-list.viewport.rect.height);
            float offset = Mathf.Clamp(currentIndex*step + height*.5f - list.viewport.rect.height*.5f,0,overflow);
            list.StopMovement();
            list.content.anchoredPosition = new Vector2(0,offset);
            selected = current;
            RefreshVisible(Vector2.zero);
            view.Button("challenge").interactable = current.unlocked;
            view.Text("status").text = Format("tower.stage_selected",current.stage);
            list.onValueChanged.AddListener(RefreshVisible);
            scope.Add(() => list.onValueChanged.RemoveListener(RefreshVisible));

            void RefreshVisible(Vector2 _)
            {
                int start = Mathf.Clamp(Mathf.FloorToInt(list.content.anchoredPosition.y/step)-1,0,state.stages.Length-capacity);
                for (int index=start;index<start+capacity;index++)
                {
                    int slot = index % capacity;
                    var row = rows[slot];
                    var stage = state.stages[index];
                    if (bound[slot] == stage) continue;
                    bound[slot] = stage;
                    ((RectTransform)row.transform).anchoredPosition = new Vector2(0,-index*step);
                    row.Require<GameText>("label").text = Format("tower.stage", stage.stage);
                    string stateKey = stage.cleared ? "cleared" : stage.unlocked ? "current" : "locked";
                    row.Require<GameText>("state").text = Format("tower.row",stage.level,locale.Resolve("tower."+stateKey));
                    SetBadge(row.Require<Image>("leader"),stage.heroIds.FirstOrDefault(),stage.level);
                    for (int i=0;i<4;i++)
                        SetBadge(row.Require<Image>("portrait"+i),i<stage.heroIds.Length ? stage.heroIds[i] : null,stage.level);
                    var marker = row.Require<GameObject>("selected");
                    marker.SetActive(stage == selected);
                    if (stage == selected) highlight = marker;
                    else if (highlight == marker) highlight = null;
                    row.Require<GameButton>("choose").interactable = stage.unlocked;
                    row.Require<GameButton>("inspect").interactable = stage.cleared;
                    row.Require<Image>("inspectIcon").color = stage.cleared ? new Color(1,1,1,.83f) : new Color(1,1,1,.30f);
                }
            }
        }

        private void Select(TowerStageDto stage, GameObject marker)
        {
            if (highlight) highlight.SetActive(false);
            selected = stage; highlight = marker; highlight.SetActive(true);
            view.Text("status").text = Format("tower.stage_selected", stage.stage);
            view.Button("challenge").interactable = stage.unlocked && !starting;
        }

        private void OpenTeam(TowerStageDto stage, bool editable)
        {
            windows.ShowOverlay(TeamWindow, scope =>
            {
                var overlay = view.Overlay;
                overlay.gameObject.SetActive(true);
                scope.Add(() => { if (overlay) overlay.gameObject.SetActive(false); });
                overlay.Require<GameObject>("scene").SetActive(editable);
                overlay.Require<GameObject>("history").SetActive(!editable);
                var title = overlay.Require<GameText>(editable ? "title" : "historyTitle");
                title.text = locale.Resolve("arena.loading");
                var close = overlay.Require<GameButton>(editable ? "back" : "historyBack");
                close.KeepBrightnessOnPress = !editable;
                close.BindBack(scope, () => { windows.CloseOverlay(TeamWindow); return Task.CompletedTask; });
                var confirm = overlay.Require<GameButton>("confirm");
                confirm.gameObject.SetActive(editable);
                confirm.interactable = false;
                var template = overlay.Require<GameObject>("row");
                template.SetActive(false);
                _ = LoadTeam(scope, overlay, title, stage, editable);
            });
        }

        private async Task LoadTeam(BindingScope scope, BindingMap overlay, GameText title, TowerStageDto stage, bool editable)
        {
            try
            {
                if (!editable)
                {
                    var record = await clearTeam(stage.stage, scope.Token);
                    scope.Token.ThrowIfCancellationRequested();
                    title.text = Format("tower.first_team", stage.stage);
                    for (int i = 0; i < 4; i++)
                    {
                        SetBadge(overlay.Require<Image>("historyAlly" + i), i < record.heroes.Length ? record.heroes[i].heroId : null, 0);
                        SetBadge(overlay.Require<Image>("historyEnemy" + i), i < record.defenderHeroIds.Length ? record.defenderHeroIds[i] : null, 0);
                    }
                    return;
                }
                var heroes = await inventory(scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                title.text = Format("tower.stage", stage.stage);
                overlay.Require<GameText>("enemyInfo").text = Format("tower.level", stage.level);
                overlay.Require<GameText>("reward").text = stage.cleared ? locale.Resolve("tower.cleared") : Format("tower.points",stage.reward);
                var chosen = new List<string>();
                var byId = heroes.ToDictionary(h => h.instanceId);
                chosen.AddRange(lastSelection.Where(byId.ContainsKey).Take(4));
                if (chosen.Count == 0) chosen.AddRange(heroes.Take(4).Select(h => h.instanceId));
                var surfaces = new ArenaDraftPortrait[8];
                scope.Add(() => { foreach (var model in surfaces) model?.Dispose(); });
                for (int i = 0; i < 8; i++)
                    surfaces[i] = new ArenaDraftPortrait(overlay.Require<RawImage>((i < 4 ? "ally" : "enemy") + "Model" + (i % 4)), miniature: true);
                RefreshModels();
                int lastFrame = -1;
                void Tick()
                {
                    if (scope.Token.IsCancellationRequested || Time.frameCount == lastFrame) return;
                    lastFrame = Time.frameCount;
                    foreach (var model in surfaces) model.Tick(Time.unscaledDeltaTime);
                }
                Canvas.willRenderCanvases += Tick;
                scope.Add(() => Canvas.willRenderCanvases -= Tick);
                var confirm = overlay.Require<GameButton>("confirm");
                confirm.interactable = chosen.Count > 0;
                confirm.Bind(scope, _ => Begin(stage.stage, chosen));
                var list = overlay.Require<ScrollRect>("list");
                var filterRows = new List<(GameObject Row, string Name, int Level, Action Rebind)>();
                foreach (var hero in heroes.OrderByDescending(h => h.level))
                {
                    var row = Clone(overlay.Require<GameObject>("row"), list.content, scope);
                    var face = row.Require<Image>("portrait");
                    SetPortrait(face, hero.heroId);
                    art.TryGetValue(hero.heroId, out var definition);
                    if (definition != null && definition.Card) { face.sprite = definition.Card; face.enabled = true; }
                    for (int i=0; i<6; i++) row.Require<Image>("star"+i).enabled = i < hero.stars;
                    string name = hero.displayName ?? heroName?.Invoke(hero.heroId) ?? definition?.Name;
                    row.Require<GameText>("label").text = name;
                    row.Require<GameText>("level").text = Format("tower.level", hero.level);
                    var role = row.Require<Image>("role");
                    role.sprite = definition?.ClassIcon;
                    role.enabled = role.sprite;
                    var mark = row.Require<GameObject>("selected");
                    mark.SetActive(chosen.Contains(hero.instanceId));
                    var choose = row.Require<GameButton>("choose");
                    // GameButton releases its binding on disable. A search may hide and restore a roster row.
                    void BindChoice() => choose.Bind(scope, _ =>
                    {
                        if (!chosen.Remove(hero.instanceId))
                        {
                            if (chosen.Count == 4)
                            {
                                GameUiFeedback.Play(choose, GameUiFeedback.Sound.Blocked);
                                return Task.CompletedTask;
                            }
                            chosen.Add(hero.instanceId);
                        }
                        mark.SetActive(chosen.Contains(hero.instanceId));
                        confirm.interactable = chosen.Count > 0;
                        RefreshModels();
                        return Task.CompletedTask;
                    });
                    BindChoice();
                    filterRows.Add((row.gameObject,name ?? "",hero.level,BindChoice));
                }
                var query = overlay.Require<TMP_InputField>("query");
                query.SetTextWithoutNotify("");
                UnityAction<string> filter = term =>
                {
                    foreach(var item in filterRows)
                    {
                        bool visible = item.Name.IndexOf(term.Trim(),StringComparison.OrdinalIgnoreCase) >= 0;
                        if (item.Row.activeSelf == visible) continue;
                        item.Row.SetActive(visible);
                        if (visible) item.Rebind();
                    }
                    list.StopMovement();
                    list.verticalNormalizedPosition = 1f;
                };
                query.onValueChanged.AddListener(filter);
                scope.Add(() => query.onValueChanged.RemoveListener(filter));
                bool descending=true;
                var order=overlay.Require<GameText>("order");
                order.text=locale.Resolve("tower.level_order");
                overlay.Require<GameButton>("sort").Bind(scope,_ =>
                {
                    descending=!descending;
                    order.text=locale.Resolve(descending ? "tower.level_order" : "tower.level_ascending");
                    var ordered=descending ? filterRows.OrderByDescending(r=>r.Level) : filterRows.OrderBy(r=>r.Level);
                    foreach(var item in ordered) item.Row.transform.SetAsLastSibling();
                    list.StopMovement();
                    list.verticalNormalizedPosition=1f;
                    return Task.CompletedTask;
                });
                list.StopMovement();
                list.verticalNormalizedPosition = 1f;
                void RefreshModels()
                {
                    overlay.Require<GameText>("teamSize").text = Format("tower.members",chosen.Count);
                    for (int i = 0; i < surfaces.Length; i++)
                    {
                        bool ally = i < 4;
                        int at = i % 4;
                        var member = ally && at < chosen.Count ? byId[chosen[at]] : null;
                        string id = ally ? member?.heroId : at < stage.heroIds.Length ? stage.heroIds[at] : null;
                        string side = ally ? "ally" : "enemy";
                        ArenaDraftArt.Hero definition = null;
                        if (id != null) art.TryGetValue(id, out definition);
                        overlay.Require<GameObject>(side + "Tag" + at).SetActive(id != null);
                        var role = overlay.Require<Image>(side + "Class" + at);
                        role.sprite = definition?.ClassIcon;
                        role.enabled = role.sprite;
                        SetElement(overlay.Require<Image>(side + "Tint" + at),definition?.Element);
                        if (ally) overlay.Require<GameText>("slot"+at).text = member?.displayName ?? definition?.Name ?? "";
                        var fallback = overlay.Require<Image>(side + "Face" + at);
                        SetPortrait(fallback, id);
                        if (definition != null && surfaces[i].Show(definition.MakeModel()))
                            fallback.enabled = false;
                        else surfaces[i].Clear();
                        overlay.Require<GameText>(side + "Level" + at).text = id == null ? "" : (member?.level ?? stage.level).ToString("D2");
                    }
                }
            }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception) when (!scope.Token.IsCancellationRequested) { title.text = locale.Resolve("arena.error.request_failed"); }
        }

        private async Task Begin(int stage, List<string> chosen)
        {
            if (starting || chosen.Count < 1 || chosen.Count > 4 || page == null) return;
            var scope = page;
            starting = true;
            var ids = chosen.ToArray();
            lastSelection.Clear(); lastSelection.AddRange(ids);
            windows.CloseOverlay(TeamWindow);
            view.Button("challenge").interactable = false;
            try { await startBattle(stage, ids, scope.Token); }
            catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
            catch (Exception) when (!scope.Token.IsCancellationRequested) { view.Text("status").text = locale.Resolve("arena.error.request_failed"); }
            finally
            {
                if (page == scope)
                {
                    starting = false;
                    view.Button("challenge").interactable = selected?.unlocked == true;
                }
            }
        }

        private static BindingMap Clone(GameObject template, RectTransform parent, BindingScope scope)
        {
            var row = Object.Instantiate(template, parent, false);
            row.SetActive(true);
            scope.Add(() => { if (row) { row.SetActive(false); Object.Destroy(row); } });
            return row.GetComponent<BindingMap>();
        }

        private void SetPortrait(Image image, string hero)
        {
            image.sprite = string.IsNullOrEmpty(hero) ? null : portrait(hero);
            image.enabled = image.sprite;
            image.preserveAspect = true;
        }

        private void SetBadge(Image face, string id, int level)
        {
            SetPortrait(face, id);
            var map = face.transform.parent.GetComponent<BindingMap>();
            map.gameObject.SetActive(id != null);
            if (id == null) return;
            art.TryGetValue(id,out var hero);
            var role = map.Require<Image>("role");
            role.sprite = hero?.ClassIcon;
            role.enabled = role.sprite;
            if (!map.Require<GameObject>("stars").activeSelf) return;
            SetElement(map.Require<Image>("plate"),hero?.Element);
            var tens=map.Require<Image>("tens");
            var ones=map.Require<Image>("ones");
            tens.sprite = view.Digit(level/10%10);
            ones.sprite = view.Digit(level%10);
            tens.enabled = tens.sprite;
            ones.enabled = ones.sprite;
            int stars = Mathf.Clamp(Mathf.Max(hero?.BaseStars ?? 0, (level+9)/10), 1, 6);
            var rail=(RectTransform)map.Require<GameObject>("stars").transform;
            float margin=(1f-(stars-1)*.137f-.205f)*.5f;
            rail.anchorMin=new Vector2(margin,rail.anchorMin.y);
            rail.anchorMax=new Vector2(1f+margin,rail.anchorMax.y);
            for (int i=0;i<6;i++) map.Require<Image>("star"+i).enabled=i<stars;
        }

        private void SetElement(Image plate,string element)
        {
            switch(element)
            {
                case "fire": case "ice": case "wind": case "dark": case "light":
                    plate.sprite=view.Element(element);
                    plate.enabled=plate.sprite;
                    break;
                default:
                    plate.enabled=false;
                    break;
            }
        }

        private string Format(string key, params object[] args) => string.Format(locale.Resolve(key), args);
    }
}
