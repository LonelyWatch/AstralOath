using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        internal static readonly string[] Slots = { "weapon", "helmet", "armor", "necklace", "ring", "boots" };
        private static readonly string[] AuthoredSlots = { "weapon", "helm", "armor", "neck", "ring", "boots" };
        private const string Prefix = "home.gear.";
        private readonly BindingMap page, ui, bagPane;
        private readonly Func<string,Sprite> heroPortrait;
        private readonly Dictionary<string,EquipmentItemCardBinder> itemCards = new Dictionary<string,EquipmentItemCardBinder>();
        private readonly EquipmentStatRowBinder bagMain;
        private readonly EquipmentItemCardBinder bagThumbnail;
        private readonly EquipmentStatRowBinder[] bagSubs;
        private readonly ILocaleService locale;
        private readonly EquipmentInventoryApi api;
        private readonly Func<CancellationToken, Task<EquipmentInventorySnapshot>> fetch;
        private readonly Func<CancellationToken, Task<EquipmentInventorySnapshot>> cachedRead;
        private readonly Func<HeroInventoryItem[]> heroes;
        private readonly Func<HeroInventoryItem> currentHero;
        private readonly Func<string,string> heroName;
        private readonly Func<HeroInventoryItem,BattleStats> baseStats;
        private readonly Func<string,(int attack,int health)> artifactStats;
        private readonly Action changed;
        private readonly EquipmentSlotBinder[] slots;
        private readonly Dictionary<string,Sprite> art;
        private readonly Dictionary<string,EquipmentInventoryItem[]> loadouts = new Dictionary<string,EquipmentInventoryItem[]>();
        private readonly List<GameObject> cards = new List<GameObject>(), targets = new List<GameObject>();
        private EquipmentInventorySnapshot snapshot;
        private EquipmentInventoryItem selected;
        private string filter, targetId, action, requestId;
        private long quotedVersion;
        private bool picker, busy, loadFailed;
        private bool orderByLevelAscending;
        private EquipmentInventoryItem[] Items => snapshot?.items ?? Array.Empty<EquipmentInventoryItem>();
        private string L(string key) => locale.Resolve(Prefix + key);
        private string F(string key, params object[] args) => GameText.Format(locale, Prefix + key, args);
        private T Get<T>(string key) where T : Object => ui.Require<T>(key);
        private BindingMap Pane => picker ? ui : bagPane;

        internal EquipmentInventoryBinder(BindingMap page, ILocaleService locale, EquipmentInventoryApi api,
            Func<CancellationToken,Task<EquipmentInventorySnapshot>> fetch, Func<HeroInventoryItem[]> heroes,
            Func<HeroInventoryItem> currentHero, Func<string,string> heroName, Func<HeroInventoryItem,BattleStats> baseStats,
            Func<string,(int attack,int health)> artifactStats, Func<string,Sprite> heroPortrait, Action changed)
        {
            this.page = page;
            this.locale = locale;
            this.api = api;
            cachedRead = fetch;
            this.fetch = fetch ?? api.FetchAsync;
            this.heroes = heroes;
            this.currentHero = currentHero;
            this.heroName = heroName;
            this.baseStats = baseStats;
            this.artifactStats = artifactStats;
            this.heroPortrait = heroPortrait;
            this.changed = changed;
            ui = page.Require<BindingMap>("gearBrowser");
            bagPane = Get<BindingMap>("bagPane");
            bagThumbnail = new EquipmentItemCardBinder(bagPane.Require<BindingMap>("thumbnail"));
            bagMain = new EquipmentStatRowBinder(Get<BindingMap>("bagMain"));
            bagSubs = Enumerable.Range(0,4).Select(i => new EquipmentStatRowBinder(Get<BindingMap>("bagSub" + i))).ToArray();
            slots = AuthoredSlots.Select(key => new EquipmentSlotBinder(page.Require<BindingMap>("equipmentSlot_" + key))).ToArray();
            art = Slots.ToDictionary(key => key,key => Get<Image>("icon_" + key).sprite);
            art.Add("training_speed_weapon", Get<Image>("icon_training_speed_weapon").sprite);
            art.Add("training_critical_weapon", Get<Image>("icon_training_critical_weapon").sprite);
        }

        internal void Bind(BindingScope scope)
        {
            void Listen(GameButton button, UnityAction handler)
            {
                button.onClick.AddListener(handler);
                scope.Add(() => { if (button) button.onClick.RemoveListener(handler); });
            }
            foreach (string slot in Slots)
                Listen(page.Require<GameButton>("gearOpen_" + slot), () => Show(true,slot));
            foreach (var pane in new[] { ui, bagPane })
            {
                foreach (string slot in Slots) Listen(pane.Require<GameButton>("filter_" + slot), () => Filter(slot));
                Listen(pane.Require<GameButton>("apply"), () => Prepare("equip"));
                Listen(pane.Require<GameButton>("retry"), () => { if (!busy) _ = RetryAsync(scope.Token); });
            }
            Listen(bagPane.Require<GameButton>("filter_all"), () => Filter(null));
            Listen(bagPane.Require<GameButton>("remove"), () => Prepare("unequip"));
            Listen(Get<GameButton>("manageReset"), () => { if (!busy) { selected = null; PaintDetail(); } });
            Listen(Get<GameButton>("manageCancel"), CloseDialog);
            Listen(Get<GameButton>("manageConfirm"), () => { if (!busy && targetId != null) _ = SubmitAsync(scope.Token); });
            Listen(page.Require<GameButton>("gearManage"), () => Show(true,null));
            Listen(bagPane.Require<GameButton>("sort"), () => { orderByLevelAscending = !orderByLevelAscending; Refresh(); });
            Listen(Get<GameButton>("close"), () => Back());
            Listen(Get<GameButton>("shade"), () => Back());
            Listen(Get<GameButton>("cancel"), CloseDialog);
            Listen(Get<GameButton>("confirm"), () => { if (!busy && targetId != null) _ = SubmitAsync(scope.Token); });
            BindSingleSlot(scope);
            api.RemoteInventoryChanged += OnRemoteInventory;
            scope.Add(() =>
            {
                api.RemoteInventoryChanged -= OnRemoteInventory;
                Clear(cards);
                itemCards.Clear();
                Clear(targets);
                if (ui) ui.gameObject.SetActive(false);
                busy = false;
            });
        }

        private void OnRemoteInventory(EquipmentInventorySnapshot incoming)
        {
            if (snapshot != null && incoming.version <= snapshot.version) return;
            AcceptSnapshot(incoming);
            loadFailed = false;
            changed();
            if (busy || !ui.gameObject.activeInHierarchy) return;
            bool staleQuote = Get<GameObject>("dialog").activeSelf;
            if (staleQuote) CloseDialog();
            if (Managing) selected = null;
            Refresh();
            if (staleQuote) SetEquipmentStatus(L("conflict"));
        }

        internal async Task LoadAsync(EquipmentInventorySnapshot initial, CancellationToken token)
        {
            try
            {
                var fresh = initial ?? await fetch(token);
                token.ThrowIfCancellationRequested();
                AcceptSnapshot(fresh);
                loadFailed = false;
                changed();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                loadFailed = true;
                Debug.LogWarning("Unable to load equipment inventory: " + error.GetType().Name);
            }
        }

        private async Task RetryAsync(CancellationToken token)
        {
            busy = true;
            Pane.Require<GameButton>("retry").interactable = false;
            await LoadAsync(null,token);
            if (token.IsCancellationRequested) return;
            busy = false;
            Pane.Require<GameButton>("retry").interactable = true;
            Refresh();
        }

        internal (Sprite portrait, int level) Equipped(string hero, string slot)
        {
            if (string.IsNullOrEmpty(hero) || !loadouts.TryGetValue(hero,out var loadout)) return (null,0);
            int index = Array.IndexOf(Slots,slot);
            var item = index < 0 ? null : loadout[index];
            return item == null ? (null, 0) : (Icon(item), item.enhanceLevel);
        }

        private void AcceptSnapshot(EquipmentInventorySnapshot fresh)
        {
            if (snapshot != null && snapshot.version > fresh.version) return;
            snapshot = fresh;
            loadouts.Clear();
            foreach (var item in fresh.items)
            {
                if (string.IsNullOrEmpty(item.equippedHeroId)) continue;
                int index = Array.IndexOf(Slots,item.slot);
                if (index < 0) continue;
                if (!loadouts.TryGetValue(item.equippedHeroId,out var slotsForHero))
                    loadouts.Add(item.equippedHeroId,slotsForHero = new EquipmentInventoryItem[Slots.Length]);
                slotsForHero[index] = item;
            }
        }

        internal EquipmentPiece[] Pieces(string hero) =>
            !string.IsNullOrEmpty(hero) && loadouts.TryGetValue(hero,out var loadout)
                ? loadout.Where(item => item != null).Select(item => item.ToPiece()).ToArray()
                : Array.Empty<EquipmentPiece>();

        internal void PaintSlots(string hero)
        {
            for (int i = 0; i < Slots.Length; ++i)
            {
                var (portrait, level) = Equipped(hero, Slots[i]);
                slots[i].Set(portrait,level);
            }
            var sets = EquipmentStatCalculator.ActiveSets(Pieces(hero));
            page.Require<GameText>("gearSets").text = sets.Count == 0 ? locale.Resolve("home.hero_inventory.roster_no_set") :
                string.Join("  ", sets.Select(pair => F("setCount",SetName(pair.Key),pair.Value)));
        }

        internal void Show(bool fromHero, string slot)
        {
            if (TryShowSingleSlot(fromHero,slot)) return;
            picker = fromHero;
            filter = fromHero ? "weapon" : slot;
            selected = null;
            ui.gameObject.SetActive(true);
            Get<GameObject>("pickerSurface").SetActive(picker);
            bagPane.gameObject.SetActive(!picker);
            Get<GameButton>("shade").gameObject.SetActive(picker);
            Get<GameButton>("close").gameObject.SetActive(picker);
            CloseDialog();
            Refresh();
        }

        internal void Hide() { ResetSingleSlot(); ui.gameObject.SetActive(false); }
        internal bool Back()
        {
            if (!ui.gameObject.activeInHierarchy) return false;
            if (Get<GameObject>("dialog").activeSelf) { if (!busy) CloseDialog(); return true; }
            if (BackSingleSlot()) return true;
            if (!picker) return false;
            Hide();
            return true;
        }

        private void Filter(string key) { if (!busy) { filter = key; selected = null; Refresh(); } }
        private Sprite Icon(EquipmentInventoryItem item)
        {
            if (!string.IsNullOrEmpty(item.templateId) && art.TryGetValue(item.templateId, out var icon)) return icon;
            return art.TryGetValue(item.slot, out var fallback) ? fallback : null;
        }
        private string ItemName(EquipmentInventoryItem item) => F("title", L(item.slot));
        private string ItemDescription(EquipmentInventoryItem item)
        {
            try { return L("description." + item.templateId); }
            catch (KeyNotFoundException) { return F("description.unknown", SetName(item.setId), L(item.slot)); }
        }

        private static int ItemScore(EquipmentInventoryItem item) => EquipmentScoreCalculator.Calculate(item.ToPiece());

        private void PaintEquipmentScore(string key, EquipmentInventoryItem item)
        {
            new EquipmentStatRowBinder(Get<BindingMap>(key)).Set(locale.Resolve("home.hero_inventory.sort_equipment_score"),Num(ItemScore(item)));
        }
        private string SetName(string key) => key == "speed" ? L("speedSet") : key == "critical" ? L("criticalSet") : key;
        private string SetProgress(EquipmentInventoryItem item)
        {
            int count = string.IsNullOrEmpty(item.equippedHeroId) ? 0 :
                Items.Count(other => other.equippedHeroId == item.equippedHeroId && other.setId == item.setId);
            return SetName(item.setId) + " (" + count + "/" + (item.setId == "speed" ? 4 : 2) + ")";
        }
        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Percent(int bp) => (bp / 100m).ToString("0.##",CultureInfo.InvariantCulture) + "%";
        private static string StatValue(EquipmentStatValue s) =>
            s.kind.EndsWith("Pct",StringComparison.Ordinal) || s.kind.EndsWith("Bp",StringComparison.Ordinal) ? Percent(s.value) : Num(s.value);
        private Sprite GradeArt(EquipmentInventoryItem item) => Get<Image>("grade_" + item.grade).sprite;
        private Sprite SetArt(EquipmentInventoryItem item) => Get<Image>("set_" + item.setId).sprite;
        private Sprite StatArt(EquipmentStatValue stat) => Get<Image>("stat_" + stat.kind.Replace("Pct",string.Empty)).sprite;
        private Sprite WearerArt(EquipmentInventoryItem item) => string.IsNullOrEmpty(item.equippedHeroId) ? null : heroPortrait(item.equippedHeroId);

        private EquipmentItemCardBinder CreateCard(EquipmentInventoryItem item, GameObject source, RectTransform parent, Action click)
        {
            var row = Object.Instantiate(source,parent,false);
            row.name = item.instanceId;
            var card = new EquipmentItemCardBinder(row.GetComponent<BindingMap>());
            card.Set(Icon(item),GradeArt(item),SetArt(item),StatArt(item.mainStat),WearerArt(item),
                item.itemLevel,item.enhanceLevel,StatValue(item.mainStat));
            card.Select.onClick.AddListener(() => click());
            row.SetActive(true);
            return card;
        }

        private static void Clear(List<GameObject> rows)
        {
            foreach (var row in rows) if (row) { row.SetActive(false); Object.Destroy(row); }
            rows.Clear();
        }

        private void Refresh()
        {
            if (RefreshSingleSlot()) return;
            if (!picker)
            {
                bagPane.Require<GameText>("capacity").text = snapshot == null ? "" : snapshot.used + " / " + snapshot.capacity;
                SetFilterActive("all",filter == null);
            }
            foreach (string key in Slots) SetFilterActive(key,filter == key);
            var filtered = Items.Where(row => filter == null || row.slot == filter);
            if (!picker)
            {
                var sorted = orderByLevelAscending ? filtered.OrderBy(i => i.itemLevel).ThenBy(i => i.enhanceLevel) :
                    filtered.OrderByDescending(i => i.itemLevel).ThenByDescending(i => i.enhanceLevel);
                filtered = sorted.ThenBy(i => i.instanceId,StringComparer.Ordinal);
                bagPane.Require<GameText>("sortLabel").text = L("sortItemLevel") + (orderByLevelAscending ? " ↑" : " ↓");
            }
            var visible = filtered.ToArray();
            selected = visible.FirstOrDefault(row => row.instanceId == selected?.instanceId) ?? (picker ? null : visible.FirstOrDefault());
            Clear(cards);
            itemCards.Clear();
            var content = Pane.Require<ScrollRect>("items").content;
            foreach (var item in visible)
            {
                var card = CreateCard(item,Get<GameObject>("template"),content,() => { selected = item; PaintDetail(); });
                itemCards.Add(item.instanceId,card);
                cards.Add(card.Select.gameObject);
            }
            Pane.Require<ScrollRect>("items").verticalNormalizedPosition = 1f;
            PaintDetail();
        }

        private void SetFilterActive(string key,bool value)
        {
            var active = Pane.Require<Image>("active_" + key);
            if (picker) active.enabled = value;
            else active.gameObject.SetActive(value);
        }

        private void PaintDetail()
        {
            foreach (var pair in itemCards) pair.Value.SetSelected(pair.Key == selected?.instanceId);
            Pane.Require<GameObject>("detail").SetActive(selected != null);
            Pane.Require<GameText>("status").text = loadFailed ? L("loadFailed") : itemCards.Count == 0 ? L("empty") : "";
            Pane.Require<GameButton>("retry").gameObject.SetActive(loadFailed);
            if (picker) { PaintManager(); return; }
            if (selected == null) return;
            Pane.Require<GameText>("name").text = ItemName(selected);
            PaintBagStats();
            Pane.Require<GameText>("wearer").text = string.IsNullOrEmpty(selected.equippedHeroId) ? L("unworn") : F("owner",heroName(selected.equippedHeroId));
            Pane.Require<GameText>("applyLabel").text = L(!string.IsNullOrEmpty(selected.equippedHeroId) ? "changeTarget" : "equip");
            Pane.Require<GameButton>("remove").gameObject.SetActive(!string.IsNullOrEmpty(selected.equippedHeroId));
        }

        private void PaintBagStats()
        {
            BindSlotArt(bagThumbnail,selected);
            bagPane.Require<Image>("plate").sprite = Get<Image>("detail_" + selected.grade).sprite;
            bagPane.Require<GameText>("quality").text = L(selected.grade) + L(selected.slot);
            bagPane.Require<GameText>("description").text = ItemDescription(selected);
            PaintEquipmentScore("bagScore",selected);
            var set = bagPane.Require<Image>("setIcon");
            set.sprite = SetArt(selected);
            set.enabled = set.sprite;
            bagPane.Require<GameText>("set").text = SetProgress(selected);
            bagMain.Set(L(selected.mainStat.kind),StatValue(selected.mainStat));
            for (int i = 0; i < bagSubs.Length; ++i)
            {
                var value = i < selected.substats.Length ? selected.substats[i] : null;
                Get<BindingMap>("bagSub" + i).gameObject.SetActive(value != null);
                if (value != null) bagSubs[i].Set(L(value.kind),StatValue(value));
            }
        }

        private void SetEquipmentStatus(string message)
        {
            Pane.Require<GameText>("status").text = message;
            SetSingleSlotFeedback(message);
        }

        private void Prepare(string operation)
        {
            if (selected == null || busy) return;
            action = operation;
            requestId = Guid.NewGuid().ToString("N");
            quotedVersion = snapshot.version;
            targetId = operation == "unequip" ? selected.equippedHeroId : picker ? currentHero()?.instanceId : null;
            Get<GameObject>("managementQuote").SetActive(Managing);
            Get<GameObject>("ordinaryQuote").SetActive(!Managing);
            Get<GameObject>("dialog").SetActive(true);
            Clear(targets);
            if (targetId != null) { Quote(); return; }
            Get<GameText>("summary").text = L("selectHero");
            Get<GameText>("change").text = "";
            Get<GameButton>("confirm").interactable = false;
            var choices = Get<ScrollRect>("targets");
            choices.gameObject.SetActive(true);
            foreach (var hero in heroes().Where(h => h.instanceId != selected.equippedHeroId))
            {
                var row = Object.Instantiate(Get<GameObject>("targetTemplate"),choices.content,false);
                row.name = hero.instanceId;
                var map = row.GetComponent<BindingMap>();
                map.Require<GameText>("title").text = heroName(hero.instanceId) + "　Lv. " + hero.level;
                map.Require<GameButton>("select").onClick.AddListener(() => { targetId = hero.instanceId; Quote(); });
                targets.Add(row);
                row.SetActive(true);
            }
        }

        private void Quote()
        {
            Get<ScrollRect>("targets").gameObject.SetActive(false);
            Get<GameButton>("confirm").interactable = true;
            Get<GameButton>("manageConfirm").interactable = true;
            bool equip = action == "equip";
            Get<GameText>("summary").text = F(equip ? "equipSummary" : "removeSummary",heroName(targetId),ItemName(selected));
            var old = Items.Where(row => row.equippedHeroId == targetId).ToArray();
            var after = old.Where(row => row.slot != selected.slot).ToList();
            if (equip) after.Add(selected);
            var lines = new List<string>();
            var prior = old.FirstOrDefault(row => row.slot == selected.slot);
            if (equip && prior != null) lines.Add(F("replace",ItemName(prior)));
            if (equip && !string.IsNullOrEmpty(selected.equippedHeroId)) lines.Add(F("transfer",heroName(selected.equippedHeroId)));
            var plain = baseStats(heroes().FirstOrDefault(h => h.instanceId == targetId));
            if (plain != null)
            {
                var beforePieces = old.Select(i => i.ToPiece()).ToArray();
                var afterPieces = after.Select(i => i.ToPiece()).ToArray();
                var oldSets = EquipmentStatCalculator.ActiveSets(beforePieces);
                var newSets = EquipmentStatCalculator.ActiveSets(afterPieces);
                foreach (var key in oldSets.Keys.Union(newSets.Keys))
                {
                    oldSets.TryGetValue(key,out int before);
                    newSets.TryGetValue(key,out int next);
                    if (before != next) lines.Add(SetName(key) + "　×" + before + " → ×" + next);
                }
                var first = PanelValues(targetId,plain,beforePieces);
                var last = PanelValues(targetId,plain,afterPieces);
                for (int i = 0; i < first.Length; ++i)
                {
                    if (first[i] != last[i]) lines.Add(L(PanelKeys[i]) + "　" + PanelValue(i,first[i]) + " → " + PanelValue(i,last[i]));
                }
            }
            Get<GameText>("change").text = lines.Count == 0 ? L("noStatChange") : string.Join("\n",lines);
            if (Managing)
            {
                Get<GameText>("manageSummary").text = Get<GameText>("summary").text;
                Get<GameText>("manageChange").text = Get<GameText>("change").text;
            }
        }

        private void CloseDialog()
        {
            if (busy) return;
            Get<GameObject>("dialog").SetActive(false);
            Clear(targets);
            targetId = action = requestId = null;
        }

        private async Task SubmitAsync(CancellationToken token)
        {
            busy = true;
            Get<GameButton>("confirm").interactable = false;
            Get<GameButton>("manageConfirm").interactable = false;
            try
            {
                var response = await api.ChangeAsync(action,requestId,targetId,selected.instanceId,selected.slot,quotedVersion,token);
                var fresh = cachedRead == null ? response : await cachedRead(token);
                token.ThrowIfCancellationRequested();
                AcceptSnapshot(fresh);
                busy = false;
                CloseDialog();
                if (Managing) selected = null;
                OnSingleSlotApplied();
                if (ui.gameObject.activeSelf) Refresh();
                changed();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                if (token.IsCancellationRequested) return;
                if (error is EquipmentRequestException rejected && rejected.Status == HttpStatusCode.Conflict)
                {
                    busy = false;
                    CloseDialog();
                    if (Managing) selected = null;
                    await LoadAsync(null,token);
                    if (token.IsCancellationRequested) return;
                    Refresh();
                    if (!loadFailed) SetEquipmentStatus(L("conflict"));
                }
                else
                {
                    Get<GameText>("change").text = L("failed");
                    Get<GameText>("manageChange").text = L("failed");
                    SetSingleSlotFeedback(L("failed"));
                    Get<GameButton>("confirm").interactable = true;
                    Get<GameButton>("manageConfirm").interactable = true;
                }
            }
            finally { if (!token.IsCancellationRequested) busy = false; }
        }
    }
}
