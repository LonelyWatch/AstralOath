using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed class ArtifactInventoryBinder
    {
        private const string Prefix = "home.artifacts.";
        private const int Columns = 6;
        private const float RowHeight = 145f;
        private static readonly string[] FilterClasses =
            { null, "", "warrior", "knight", "thief", "ranger", "mage", "soul_weaver" };
        private static readonly string[] MaterialKeys = { "lesserCharm", "greaterCharm", "epicCharm", "bottle" };

        private readonly ArtifactInventoryBindings ui;
        private readonly ArtifactInventoryApi api;
        private readonly ArtifactArtCatalog art;
        private readonly ILocaleService locale;
        private readonly Func<HeroInventoryItem> currentHero;
        private readonly Func<string, string> heroClass;
        private readonly Func<string, string> heroName;
        private readonly Action onChanged;
        private readonly ScrollRect scroll;
        private readonly List<Card> cards = new List<Card>();

        private ArtifactInventorySnapshot snapshot;
        private ArtifactInventoryItem[] visible = Array.Empty<ArtifactInventoryItem>();
        private ArtifactInventoryItem selected;
        private HeroInventoryItem pickerHero;
        private Pending pending;
        private bool bag, active, loading, failed, busy, sortByRarity, pickingMaterial;
        private int filter;
        private string failure;

        internal ArtifactInventoryBinder(ArtifactInventoryBindings ui, ArtifactInventoryApi api, ArtifactArtCatalog art,
            ILocaleService locale, Func<HeroInventoryItem> currentHero, Func<string, string> heroClass,
            Func<string, string> heroName, Action onChanged)
        {
            this.ui = ui;
            this.api = api;
            this.art = art;
            this.locale = locale;
            this.currentHero = currentHero;
            this.heroClass = heroClass;
            this.heroName = heroName;
            this.onChanged = onChanged;
            scroll = ui.Get<ScrollRect>("Items");
        }

        internal void Bind(BindingScope scope)
        {
            foreach (string action in ArtifactInventoryBindings.Actions)
                ui.ActionLabel(action).Bind(locale, scope, Prefix + action);
            ui.Get<GameText>("PickerTitle").Bind(locale, scope, Prefix + "picker_title");
            ui.Get<GameText>("RetryLabel").Bind(locale, scope, "home.hero_inventory.retry");
            ui.Get<GameButton>("Slot").Bind(scope, OpenPicker);
            ui.Get<GameButton>("PickerClose").Bind(scope, _ => ClosePickerAsync());
            ui.Get<GameButton>("PickerShade").Bind(scope, _ => ClosePickerAsync());
            ui.Get<GameButton>("Retry").Bind(scope, LoadAsync);
            ui.Get<GameButton>("ConfirmShade").Bind(scope, CancelAsync);
            ui.Action("cancel").Bind(scope, CancelAsync);
            ui.Action("confirm").Bind(scope, ConfirmAsync);
            foreach (string action in new[] { "equip", "unequip", "lock", "enhance", "sell" })
                ui.Action(action).Bind(scope, _ => PreviewAction(action));
            foreach (string key in MaterialKeys)
                ui.Action(key).Bind(scope, _ => SelectMaterial(key));
            ui.Action("chooseCopy").Bind(scope, _ =>
            {
                if (pending?.Action == "enhance" && !busy && !pending.Submitted)
                {
                    pickingMaterial = true;
                    Refresh(reset: true);
                }
                return Task.CompletedTask;
            });

            ui.Get<GameButton>("Sort").Bind(scope, _ =>
            {
                sortByRarity = !sortByRarity;
                Refresh(reset: true);
                return Task.CompletedTask;
            });
            for (int i = 0; i < FilterClasses.Length; i++)
            {
                int index = i;
                ui.Get<GameButton>("Filter" + index).Bind(scope, _ =>
                {
                    filter = index;
                    Refresh(reset: true);
                    return Task.CompletedTask;
                });
            }
            scroll.onValueChanged.AddListener(OnScroll);
            scope.Add(() =>
            {
                if (scroll) scroll.onValueChanged.RemoveListener(OnScroll);
                foreach (var card in cards)
                    if (card.Root) UnityEngine.Object.Destroy(card.Root.gameObject);
                cards.Clear();
            });
        }

        internal async Task ShowTabAsync(CancellationToken token)
        {
            bag = active = true;
            filter = 0;
            selected = null;
            await LoadAsync(token);
        }

        internal void HideTab()
        {
            if (!bag) return;
            bag = active = false;
            pending = null;
            pickingMaterial = false;
            Refresh(reset: false);
        }

        internal Task PreloadAsync(CancellationToken token) => LoadAsync(token);

        internal (Sprite Art, int Level) EquippedCard(string hero)
        {
            var match = snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == hero);
            return match == null ? (null, 0) : (art.Find(match.templateId)?.portrait, match.level);
        }

        internal (int Attack, int Health) EquippedStats(string hero)
        {
            var match = snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == hero);
            var template = match == null ? null : art.Find(match.templateId)?.template;
            return template == null ? (0, 0) : (template.AttackAt(match.level), template.HealthAt(match.level));
        }

        internal void PaintSlot(HeroInventoryItem hero)
        {
            ui.Get<GameButton>("Slot").interactable = hero != null;
            var (image, level) = hero == null ? (null, 0) : EquippedCard(hero.instanceId);
            SetImage(ui.Get<Image>("SlotImage"), image);
            ui.Get<GameText>("SlotLevel").text = image ? Plus(level) : "";
        }

        internal bool Back()
        {
            if (pickingMaterial)
            {
                pickingMaterial = false;
                Refresh(reset: true);
                return true;
            }
            if (pending != null)
            {
                if (!busy)
                {
                    pending = null;
                    Refresh(reset: false);
                }
                return true;
            }
            return ClosePicker();
        }

        private async Task OpenPicker(CancellationToken token)
        {
            var hero = currentHero();
            if (hero == null) return;
            pickerHero = hero;
            bag = false;
            active = true;
            selected = snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == hero.instanceId);
            await LoadAsync(token);
        }

        private Task ClosePickerAsync()
        {
            Back();
            return Task.CompletedTask;
        }

        private bool ClosePicker()
        {
            if (pickerHero == null) return false;
            if (busy) return true;
            pickerHero = null;
            pending = null;
            active = false;
            Refresh(reset: false);
            return true;
        }

        private async Task LoadAsync(CancellationToken token)
        {
            if (loading) return;
            loading = true;
            failed = false;
            failure = null;
            Refresh(reset: false);
            try { snapshot = await api.FetchAsync(token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                Debug.LogWarning("Unable to load artifact inventory: " + error.GetType().Name);
                failed = true;
            }
            finally { loading = false; }
            if (token.IsCancellationRequested) return;
            Refresh(reset: true);
            onChanged();
        }

        private void Refresh(bool reset)
        {
            bool picker = pickerHero != null || pickingMaterial;
            var frame = ui.Get<RectTransform>("Browser");
            frame.gameObject.SetActive(active);
            ui.Get<GameObject>("Confirmation").SetActive(active && pending != null && !pickingMaterial);
            ui.Get<GameButton>("PickerShade").gameObject.SetActive(active && picker);
            if (!active) return;

            frame.anchorMin = picker ? new Vector2(.10f, .099f) : new Vector2(.102f, .013f);
            frame.anchorMax = picker ? new Vector2(.90f, .91f) : new Vector2(.980f, .803f);
            scroll.GetComponent<RectTransform>().anchorMax = new Vector2(.664f, picker ? .875f : .995f);
            ui.Get<Image>("BrowserPlate").enabled = picker;
            ui.Get<GameButton>("PickerClose").gameObject.SetActive(picker);
            var heading = ui.Get<GameText>("PickerTitle");
            heading.gameObject.SetActive(picker);
            heading.text = locale.Resolve(Prefix + (pickingMaterial ? "materials_title" : "picker_title"));
            ui.Get<GameText>("Capacity").text = snapshot == null ? "" : Format("capacity", snapshot.used, snapshot.capacity);
            ui.Get<GameText>("SortLabel").text = locale.Resolve(Prefix + (sortByRarity ? "sort_rarity" : "sort_enhancement"));
            for (int index = 0; index < FilterClasses.Length; index++)
                ui.Get<Image>("FilterActive" + index).gameObject.SetActive(index == filter);

            var candidates = snapshot?.artifacts?.Where(CanDisplay) ?? Enumerable.Empty<ArtifactInventoryItem>();
            visible = (sortByRarity
                ? candidates.OrderByDescending(item => art.Find(item.templateId)?.template.Stars ?? 0).ThenByDescending(item => item.level)
                : candidates.OrderByDescending(item => item.level).ThenByDescending(item => art.Find(item.templateId)?.template.Stars ?? 0))
                .ToArray();
            selected = pickingMaterial ? pending.Item :
                visible.FirstOrDefault(item => item.instanceId == selected?.instanceId) ?? visible.FirstOrDefault();
            scroll.content.sizeDelta = new Vector2(0, Mathf.CeilToInt((float)visible.Length / Columns) * RowHeight);
            if (reset)
            {
                scroll.StopMovement();
                scroll.content.anchoredPosition = Vector2.zero;
            }
            var status = ui.Get<GameText>("Status");
            status.gameObject.SetActive(loading || failed || visible.Length == 0 && (picker || (snapshot?.used ?? 0) != 0));
            status.text = locale.Resolve(Prefix + (loading ? "loading" : failed ? "load_failed" : "no_matches"));
            ui.Get<GameButton>("Retry").gameObject.SetActive(failed);
            UpdateDetail();
            DrawCards();
            if (pending != null && !pickingMaterial) DrawConfirmation();
        }

        private bool CanDisplay(ArtifactInventoryItem item)
        {
            var template = art.Find(item.templateId)?.template;
            if (template == null) return false;
            if (pickingMaterial) return IsMaterial(item);
            if (pickerHero != null)
                return template.HeroClass.Length == 0 || template.HeroClass == heroClass(pickerHero.templateId);
            return filter == 0 || template.HeroClass == FilterClasses[filter];
        }

        private void OnScroll(Vector2 _) => DrawCards();

        private void DrawCards()
        {
            if (!active) return;
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.content.anchoredPosition.y / RowHeight) - 1) * Columns;
            int displayed = Mathf.Clamp(visible.Length - first, 0,
                (Mathf.CeilToInt(Mathf.Max(scroll.viewport.rect.height, 700f) / RowHeight) + 3) * Columns);
            while (cards.Count < displayed)
            {
                var card = new Card(UnityEngine.Object.Instantiate(ui.Get<GameObject>("Card"), scroll.content, false));
                card.Button.onClick.AddListener(() =>
                {
                    if (busy) return;
                    if (pickingMaterial)
                    {
                        pending.Material = new ArtifactInventoryMaterial { kind = "artifact", instanceId = card.Item.instanceId };
                        pickingMaterial = false;
                        selected = pending.Item;
                        Refresh(reset: true);
                        return;
                    }
                    if (pending != null) return;
                    selected = card.Item;
                    UpdateDetail();
                    DrawCards();
                });
                cards.Add(card);
            }
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (i >= displayed)
                {
                    card.Root.gameObject.SetActive(false);
                    continue;
                }
                int index = first + i;
                int col = index % Columns;
                var item = visible[index];
                var entry = art.Find(item.templateId);
                card.Root.anchorMin = new Vector2((float)col / Columns, 1);
                card.Root.anchorMax = new Vector2((float)(col + 1) / Columns, 1);
                card.Root.pivot = new Vector2(.5f, 1);
                card.Root.sizeDelta = new Vector2(-7, RowHeight - 10);
                card.Root.anchoredPosition = new Vector2(0, -(index / Columns) * RowHeight);
                card.Display(item, entry.portrait, entry.template.Stars, item == selected);
            }
        }

        private void UpdateDetail()
        {
            var entry = selected == null || pickingMaterial ? null : art.Find(selected.templateId);
            ui.Get<GameObject>("Detail").SetActive(entry != null);
            ui.Get<GameObject>("EmptyIcon").SetActive(entry == null && !failed && !loading);
            ui.Get<GameObject>("EmptyMessage").SetActive(entry == null && !failed && !loading);
            if (entry == null) return;
            var template = entry.template;
            SetImage(ui.Get<Image>("Portrait"), entry.portrait);
            ui.Get<GameText>("Title").text = template.Name;
            ui.Get<GameText>("Level").text = Format("level", selected.level, 15 + selected.limitBreaks * 3);
            ui.Get<GameText>("Stats").text = Format("stats", template.AttackAt(selected.level), template.HealthAt(selected.level));
            ui.Get<GameText>("Skill").text = template.DescriptionAt(selected.level);
            ui.Get<GameText>("Wearer").text = HasWearer(selected)
                ? Format("wearing", heroName(selected.heroInstanceId)) : locale.Resolve(Prefix + "unassigned");
            ui.Action("equip").gameObject.SetActive(pickerHero != null);
            ui.Action("equip").interactable = pickerHero != null && selected.heroInstanceId != pickerHero.instanceId;
            ui.Action("unequip").gameObject.SetActive(HasWearer(selected));
            ui.Action("unequip").interactable = HasWearer(selected);
            ui.Action("lock").interactable = true;
            ui.ActionLabel("lock").text = locale.Resolve(Prefix + (selected.locked ? "unlock" : "lock"));
            ui.Action("sell").interactable = !selected.locked && !HasWearer(selected);
            ui.Action("enhance").interactable = selected.level < 30;
        }

        private Task PreviewAction(string action)
        {
            if (busy || pending != null || selected == null) return Task.CompletedTask;
            if (action == "equip" && pickerHero == null || action == "sell" && (selected.locked || HasWearer(selected)))
                return Task.CompletedTask;
            pending = new Pending(action, selected, pickerHero?.instanceId);
            failure = null;
            Refresh(reset: false);
            return Task.CompletedTask;
        }

        private bool IsMaterial(ArtifactInventoryItem item) =>
            pending != null && item.instanceId != pending.Item.instanceId && !item.locked && !HasWearer(item)
            && (item.templateId != pending.Item.templateId || pending.Item.limitBreaks < 5);

        private Task SelectMaterial(string kind)
        {
            if (busy || pending?.Action != "enhance" || pending.Submitted) return Task.CompletedTask;
            if (Amount(kind) > 0)
                pending.Material = new ArtifactInventoryMaterial { kind = kind };
            failure = null;
            DrawConfirmation();
            return Task.CompletedTask;
        }

        private void DrawConfirmation()
        {
            var item = pending.Item;
            var template = art.Find(item.templateId).template;
            string title = pending.Action == "lock" && item.locked ? "unlock" : pending.Action;
            ui.Get<GameText>("ConfirmTitle").text = locale.Resolve(Prefix + title);
            ui.Get<GameObject>("Materials").SetActive(pending.Action == "enhance");
            if (pending.Action == "enhance")
            {
                foreach (string material in MaterialKeys)
                {
                    ui.ActionLabel(material).text = Format("material_count", locale.Resolve(Prefix + material), Amount(material));
                    ui.Action(material).interactable = !busy && !pending.Submitted && Amount(material) > 0 &&
                        (material != "bottle" || template.CanUseBottle && item.limitBreaks < 5);
                }
                ui.Action("chooseCopy").interactable = !busy && !pending.Submitted && snapshot.artifacts.Any(IsMaterial);
            }
            string summary = pending.Action switch
            {
                "sell" => Format("sell_quote", template.Name, template.SellGold, template.SellPowder),
                "equip" when HasWearer(item) => Format("transfer_quote", template.Name,
                    heroName(item.heroInstanceId), heroName(pending.Hero)),
                "equip" => Format("equip_quote", template.Name, heroName(pending.Hero)),
                "enhance" when pending.Material != null => EnhancementQuote(template.Name),
                "enhance" => Format("choose_material", template.Name),
                _ => template.Name
            };
            if (!string.IsNullOrEmpty(failure)) summary += "\n" + locale.Resolve(Prefix + failure);
            ui.Get<GameText>("ConfirmText").text = summary;
            ui.Action("confirm").interactable = !busy && (pending.Action != "enhance" || pending.Material != null);
        }

        private string EnhancementQuote(string target)
        {
            var material = pending.Material;
            if (material.kind == "bottle")
                return Format("break_quote", target, locale.Resolve(Prefix + "bottle"), 0);
            if (material.kind == "artifact")
            {
                var source = snapshot.artifacts.First(item => item.instanceId == material.instanceId);
                var template = art.Find(source.templateId).template;
                string itemName = template.Name + " " + Plus(source.level);
                int gained = art.Catalog.MaterialExperience(template.Stars, source.experience);
                int cost = art.Catalog.EnhancementCost(gained);
                return source.templateId == pending.Item.templateId ? Format("break_quote", target, itemName, cost)
                    : Format("enhance_quote", target, itemName, gained, cost);
            }
            int experience = art.Catalog.CharmExperience(material.kind);
            return Format("enhance_quote", target, locale.Resolve(Prefix + material.kind), experience,
                art.Catalog.EnhancementCost(experience));
        }

        private async Task CancelAsync(CancellationToken token)
        {
            if (busy) return;
            bool committed = pending?.Submitted == true;
            pending = null;
            if (committed) await LoadAsync(token);
            else Refresh(reset: false);
        }

        private async Task ConfirmAsync(CancellationToken token)
        {
            if (busy || pending == null || pending.Action == "enhance" && pending.Material == null) return;
            var request = pending;
            var id = request.Item.instanceId;
            request.Submitted = true;
            busy = true;
            failure = null;
            DrawConfirmation();
            try
            {
                snapshot = await (request.Action switch
                {
                    "equip" => api.EquipAsync(id, request.Id, request.Hero, token),
                    "unequip" => api.UnequipAsync(id, request.Id, token),
                    "lock" => api.LockAsync(id, request.Id, !request.Item.locked, token),
                    "sell" => api.SellAsync(id, request.Id, token),
                    _ => api.EnhanceAsync(id, request.Id, new[] { request.Material }, token)
                });
                pending = null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (ArtifactRequestException error)
            {
                if ((int)error.Status < 500) request.Submitted = false;
                failure = error.Code == "artifact_material_insufficient" ? "insufficient" : "action_failed";
            }
            catch (Exception error)
            {
                Debug.LogWarning("Unable to update artifact inventory: " + error.GetType().Name);
                failure = "action_failed";
            }
            finally { busy = false; }
            if (token.IsCancellationRequested) return;
            Refresh(reset: false);
            onChanged();
        }

        private int Amount(string key) => key switch
        {
            "lesserCharm" => snapshot?.materials?.lesserCharm ?? 0,
            "greaterCharm" => snapshot?.materials?.greaterCharm ?? 0,
            "epicCharm" => snapshot?.materials?.epicCharm ?? 0,
            "bottle" => snapshot?.materials?.bottle ?? 0,
            _ => 0
        };

        private string Format(string key, params object[] args) => GameText.Format(locale, Prefix + key, args);
        private static bool HasWearer(ArtifactInventoryItem item) => !string.IsNullOrEmpty(item.heroInstanceId);
        private static string Plus(int level) => "+" + level.ToString(CultureInfo.InvariantCulture);

        private static void SetImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
        }

        private sealed class Pending
        {
            internal readonly string Action, Id, Hero;
            internal readonly ArtifactInventoryItem Item;
            internal ArtifactInventoryMaterial Material;
            internal bool Submitted;

            internal Pending(string action, ArtifactInventoryItem item, string hero)
            {
                Action = action;
                Item = item;
                Hero = hero;
                Id = Guid.NewGuid().ToString("N");
            }
        }

        private sealed class Card
        {
            internal readonly RectTransform Root;
            internal readonly GameButton Button;
            internal ArtifactInventoryItem Item { get; private set; }
            private readonly Image portrait, locked, selection;
            private readonly GameText level, stars;

            internal Card(GameObject instance)
            {
                Root = (RectTransform)instance.transform;
                var map = instance.GetComponent<BindingMap>();
                Button = map.Require<GameButton>("artifactCardButton");
                portrait = map.Require<Image>("artifactCardPortrait");
                locked = map.Require<Image>("artifactCardLock");
                selection = map.Require<Image>("artifactCardSelection");
                level = map.Require<GameText>("artifactCardLevel");
                stars = map.Require<GameText>("artifactCardStars");
            }

            internal void Display(ArtifactInventoryItem item, Sprite sprite, int rarity, bool selected)
            {
                Item = item;
                Root.name = item.instanceId;
                SetImage(portrait, sprite);
                level.text = Plus(item.level);
                stars.text = new string('★', rarity);
                locked.enabled = item.locked && locked.sprite;
                selection.enabled = selected && selection.sprite;
                Root.gameObject.SetActive(true);
            }
        }
    }
}
