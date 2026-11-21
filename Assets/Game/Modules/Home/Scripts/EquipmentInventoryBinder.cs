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
    internal sealed class EquipmentInventoryBinder
    {
        internal static readonly string[] Slots = { "weapon", "helmet", "armor", "necklace", "ring", "boots" };
        private static readonly string[] AuthoredSlots = { "weapon", "helm", "armor", "neck", "ring", "boots" };
        private const string Prefix = "home.gear.";
        private readonly BindingMap page, ui;
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
        private EquipmentInventoryItem[] Items => snapshot?.items ?? Array.Empty<EquipmentInventoryItem>();
        private string L(string key) => locale.Resolve(Prefix + key);
        private string F(string key, params object[] args) => GameText.Format(locale, Prefix + key, args);
        private T Get<T>(string key) where T : Object => ui.Require<T>(key);

        internal EquipmentInventoryBinder(BindingMap page, ILocaleService locale, EquipmentInventoryApi api,
            Func<CancellationToken,Task<EquipmentInventorySnapshot>> fetch, Func<HeroInventoryItem[]> heroes,
            Func<HeroInventoryItem> currentHero, Func<string,string> heroName, Func<HeroInventoryItem,BattleStats> baseStats,
            Func<string,(int attack,int health)> artifactStats, Action changed)
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
            this.changed = changed;
            ui = page.Require<BindingMap>("gearBrowser");
            slots = AuthoredSlots.Select(key => new EquipmentSlotBinder(page.Require<BindingMap>("equipmentSlot_" + key))).ToArray();
            art = Slots.ToDictionary(key => key,key => Get<Image>("icon_" + key).sprite);
        }

        internal void Bind(BindingScope scope)
        {
            void Listen(GameButton button, UnityAction handler)
            {
                button.onClick.AddListener(handler);
                scope.Add(() => { if (button) button.onClick.RemoveListener(handler); });
            }
            foreach (string slot in Slots)
            {
                Listen(page.Require<GameButton>("gearOpen_" + slot), () => Show(true,slot));
                Listen(Get<GameButton>("filter_" + slot), () => Filter(slot));
            }
            Listen(page.Require<GameButton>("gearManage"), () => Show(true,null));
            Listen(Get<GameButton>("filter_all"), () => Filter(null));
            Listen(Get<GameButton>("close"), () => Back());
            Listen(Get<GameButton>("shade"), () => Back());
            Listen(Get<GameButton>("cancel"), CloseDialog);
            Listen(Get<GameButton>("apply"), () => Prepare("equip"));
            Listen(Get<GameButton>("remove"), () => Prepare("unequip"));
            Listen(Get<GameButton>("retry"), () => { if (!busy) _ = RetryAsync(scope.Token); });
            Listen(Get<GameButton>("confirm"), () => { if (!busy && targetId != null) _ = SubmitAsync(scope.Token); });
            api.RemoteInventoryChanged += OnRemoteInventory;
            scope.Add(() =>
            {
                api.RemoteInventoryChanged -= OnRemoteInventory;
                Clear(cards);
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
            Refresh();
            if (staleQuote) Get<GameText>("status").text = L("conflict");
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
            Get<GameButton>("retry").interactable = false;
            await LoadAsync(null,token);
            if (token.IsCancellationRequested) return;
            busy = false;
            Get<GameButton>("retry").interactable = true;
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
            picker = fromHero;
            filter = slot;
            selected = fromHero ? Items.FirstOrDefault(row => row.equippedHeroId == currentHero()?.instanceId && row.slot == slot) : null;
            ui.gameObject.SetActive(true);
            Get<GameButton>("shade").gameObject.SetActive(picker);
            Get<GameButton>("close").gameObject.SetActive(picker);
            CloseDialog();
            Refresh();
        }

        internal void Hide() { ui.gameObject.SetActive(false); }
        internal bool Back()
        {
            if (!ui.gameObject.activeInHierarchy) return false;
            if (Get<GameObject>("dialog").activeSelf) { if (!busy) CloseDialog(); return true; }
            if (!picker) return false;
            Hide();
            return true;
        }

        private void Filter(string key) { filter = key; selected = null; Refresh(); }
        private Sprite Icon(EquipmentInventoryItem item) => art.TryGetValue(item.slot, out var sprite) ? sprite : null;
        private string ItemName(EquipmentInventoryItem item) => F("title", L(item.slot));
        private string SetName(string key) => key == "speed" ? L("speedSet") : key == "critical" ? L("criticalSet") : key;
        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Percent(int bp) => (bp / 100m).ToString("0.##",CultureInfo.InvariantCulture) + "%";
        private string Stat(EquipmentStatValue s) => L(s.kind) + "  +" +
            (s.kind.EndsWith("Pct",StringComparison.Ordinal) || s.kind.EndsWith("Bp",StringComparison.Ordinal) ? Percent(s.value) : Num(s.value));

        private static void Clear(List<GameObject> rows)
        {
            foreach (var row in rows) if (row) { row.SetActive(false); Object.Destroy(row); }
            rows.Clear();
        }

        private void Refresh()
        {
            Get<GameText>("capacity").text = snapshot == null ? "" : snapshot.used + " / " + snapshot.capacity;
            Get<Image>("active_all").enabled = filter == null;
            foreach (string key in Slots) Get<Image>("active_" + key).enabled = filter == key;
            var visible = Items.Where(row => filter == null || row.slot == filter).ToArray();
            selected = visible.FirstOrDefault(row => row.instanceId == selected?.instanceId) ?? visible.FirstOrDefault();
            Clear(cards);
            var content = Get<ScrollRect>("items").content;
            foreach (var item in visible)
            {
                var row = Object.Instantiate(Get<GameObject>("template"),content,false);
                row.name = item.instanceId;
                var map = row.GetComponent<BindingMap>();
                SetItemArt(map.Require<Image>("icon"),item);
                map.Require<GameText>("enhancement").text = "+" + Num(item.enhanceLevel);
                map.Require<GameText>("level").text = "Lv. " + Num(item.itemLevel);
                map.Require<GameButton>("select").onClick.AddListener(() => { selected = item; PaintDetail(); });
                cards.Add(row);
                row.SetActive(true);
            }
            Get<ScrollRect>("items").verticalNormalizedPosition = 1f;
            PaintDetail();
        }

        private void PaintDetail()
        {
            foreach (var card in cards) card.GetComponent<BindingMap>().Require<Image>("highlight").enabled = card.name == selected?.instanceId;
            Get<GameObject>("detail").SetActive(selected != null);
            Get<GameText>("status").text = loadFailed ? L("loadFailed") : selected == null ? L("empty") : "";
            Get<GameButton>("retry").gameObject.SetActive(loadFailed);
            if (selected == null) return;
            SetItemArt(Get<Image>("portrait"),selected);
            Get<GameText>("name").text = ItemName(selected);
            Get<GameText>("quality").text = F("grade",selected.itemLevel,selected.enhanceLevel,L(selected.grade));
            Get<GameText>("attributes").text = SetName(selected.setId) + "\n" + F("stats",Stat(selected.mainStat)) +
                string.Concat(selected.substats.Select(s => "\n" + Stat(s)));
            Get<GameText>("wearer").text = string.IsNullOrEmpty(selected.equippedHeroId) ? L("unworn") : F("owner",heroName(selected.equippedHeroId));
            Get<GameText>("applyLabel").text = L(!picker && !string.IsNullOrEmpty(selected.equippedHeroId) ? "changeTarget" : "equip");
            Get<GameButton>("apply").gameObject.SetActive(!picker || currentHero()?.instanceId != selected.equippedHeroId);
            Get<GameButton>("remove").gameObject.SetActive(!string.IsNullOrEmpty(selected.equippedHeroId) && (!picker || currentHero()?.instanceId == selected.equippedHeroId));
        }

        private void SetItemArt(Image image, EquipmentInventoryItem item)
        {
            image.sprite = Icon(item);
            image.color = Color.white;
            image.enabled = image.sprite;
        }

        private void Prepare(string operation)
        {
            if (selected == null || busy) return;
            action = operation;
            requestId = Guid.NewGuid().ToString("N");
            quotedVersion = snapshot.version;
            targetId = operation == "unequip" ? selected.equippedHeroId : picker ? currentHero()?.instanceId : null;
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
                var first = EquipmentStatCalculator.Calculate(plain,beforePieces);
                var last = EquipmentStatCalculator.Calculate(plain,afterPieces);
                var artifact = artifactStats(targetId);
                void Add(string key, int a, int b, bool percent = false)
                {
                    if (a != b) lines.Add(L(key) + "　" + (percent ? Percent(a) : Num(a)) + " → " + (percent ? Percent(b) : Num(b)));
                }
                Add("attack",first.Attack + artifact.attack,last.Attack + artifact.attack);
                Add("health",first.MaxHealth + artifact.health,last.MaxHealth + artifact.health);
                Add("defense",first.Defense,last.Defense);
                Add("speed",first.Speed,last.Speed);
                Add("critChanceBp",first.CritChanceBp,last.CritChanceBp,true);
                Add("critDamageBp",first.CritDamageBp,last.CritDamageBp,true);
                Add("effectivenessBp",first.EffectivenessBp,last.EffectivenessBp,true);
                Add("resistanceBp",first.EffectResistanceBp,last.EffectResistanceBp,true);
            }
            Get<GameText>("change").text = lines.Count == 0 ? L("noStatChange") : string.Join("\n",lines);
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
            try
            {
                var response = await api.ChangeAsync(action,requestId,targetId,selected.instanceId,selected.slot,quotedVersion,token);
                var fresh = cachedRead == null ? response : await cachedRead(token);
                token.ThrowIfCancellationRequested();
                AcceptSnapshot(fresh);
                busy = false;
                CloseDialog();
                Refresh();
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
                    await LoadAsync(null,token);
                    if (token.IsCancellationRequested) return;
                    Refresh();
                    if (!loadFailed) Get<GameText>("status").text = L("conflict");
                }
                else
                {
                    Get<GameText>("change").text = L("failed");
                    Get<GameButton>("confirm").interactable = true;
                }
            }
            finally { if (!token.IsCancellationRequested) busy = false; }
        }
    }
}
