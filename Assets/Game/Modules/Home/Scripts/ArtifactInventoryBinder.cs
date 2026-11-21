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
    internal sealed partial class ArtifactInventoryBinder
    {
        private const string Prefix = "home.artifacts.";
        private const int Columns = 2;
        private float CellWidth => Mathf.Clamp(scroll.viewport.rect.width / Columns, 1, 400f * 1.5f);
        private float CardWidth => Mathf.Min(358f * 1.5f, CellWidth * .96f);
        private float RowHeight => CardWidth * (145f / 358f);
        private Vector2 lastBrowserSize;
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
        private readonly Func<string, Sprite> heroPortrait;
        private readonly Action onChanged;
        private readonly ScrollRect scroll;
        private readonly RectTransform browser;
        private readonly EquipmentSlotBinder equipmentSlot;
        private readonly GameButton slotButton;
        private readonly Sprite[] classIcons = new Sprite[FilterClasses.Length];
        private readonly List<ArtifactInventoryCardBinder> cards = new List<ArtifactInventoryCardBinder>();

        private ArtifactInventorySnapshot snapshot;
        private ArtifactInventoryItem[] visible = Array.Empty<ArtifactInventoryItem>();
        private ArtifactInventoryItem selected;
        private HeroInventoryItem pickerHero;
        private Pending pending;
        private bool bag, active, loading, failed, busy, pickingMaterial;
        private int filter;
        private string failure;

        private readonly Func<CancellationToken, Task<ArtifactInventorySnapshot>> fetch;

        internal ArtifactInventoryBinder(ArtifactInventoryBindings ui, ArtifactInventoryApi api, ArtifactArtCatalog art,
            ILocaleService locale, Func<HeroInventoryItem> currentHero, Func<string, string> heroClass,
            Func<string, string> heroName, Action onChanged, Func<CancellationToken, Task<ArtifactInventorySnapshot>> fetch = null,
            Func<string, Sprite> heroPortrait = null, Func<string, HeroInventoryItem> findWearer = null)
        {
            this.ui = ui;
            equipmentSlot = new EquipmentSlotBinder(ui.Get<BindingMap>("EquipmentSlot"));
            slotButton = ui.Get<GameButton>("Slot");
            this.api = api;
            this.fetch = fetch ?? api.FetchAsync;
            this.art = art;
            this.locale = locale;
            this.currentHero = currentHero;
            this.heroClass = heroClass;
            this.heroName = heroName;
            this.heroPortrait = heroPortrait;
            this.findWearer = findWearer;
            this.onChanged = onChanged;
            scroll = ui.Get<ScrollRect>("Items");
            browser = ui.Get<RectTransform>("Browser");
            for (int i = 1; i < classIcons.Length; ++i)
                classIcons[i] = ui.Get<Image>("ClassIcon" + i).sprite;
        }

        internal void Bind(BindingScope scope)
        {
            BindHeroEquipment(scope);
            BindListOptions(scope);
            BindBagDetail(scope);
            BindEnhancement(scope);
            foreach (string action in ArtifactInventoryBindings.Actions)
                ui.ActionLabel(action).Bind(locale, scope, Prefix + action);
            ui.Get<GameText>("PickerTitle").Bind(locale, scope, Prefix + "picker_title");
            ui.Get<GameText>("RetryLabel").Bind(locale, scope, "home.hero_inventory.retry");
            BindAction(scope, ui.Get<GameButton>("Slot"), OpenPicker);
            ui.Get<GameButton>("PickerClose").FeedbackSound = GameUiFeedback.Sound.PageBack;
            BindAction(scope, ui.Get<GameButton>("PickerClose"), _ => ClosePickerAsync());
            BindAction(scope, ui.Get<GameButton>("PickerShade"), _ => ClosePickerAsync());
            BindAction(scope, ui.Get<GameButton>("Retry"), LoadAsync);
            BindAction(scope, ui.Get<GameButton>("ConfirmShade"), CancelAsync);
            BindAction(scope, ui.Action("cancel"), CancelAsync);
            BindAction(scope, ui.Action("confirm"), ConfirmAsync);
            BindAction(scope, ui.Action("enhance"), OpenEnhancementAsync);
            foreach (string action in new[] { "equip", "unequip", "lock", "sell" })
                BindAction(scope, ui.Action(action), _ => PreviewAction(action));
            foreach (string key in MaterialKeys)
                BindAction(scope, ui.Action(key), _ => SelectMaterial(key));
            BindAction(scope, ui.Action("chooseCopy"), _ =>
            {
                if (pending?.Action == "enhance" && !busy && !pending.Submitted)
                {
                    pickingMaterial = true;
                    Refresh(reset: true);
                }
                return Task.CompletedTask;
            });

            for (int i = 0; i < FilterClasses.Length; i++)
            {
                int index = i;
                BindAction(scope, ui.Get<GameButton>("Filter" + index), _ =>
                {
                    filter = index;
                    Refresh(reset: true);
                    return Task.CompletedTask;
                });
            }
            scroll.onValueChanged.AddListener(OnScroll);
            Canvas.willRenderCanvases += ResizeBrowser;
            scope.Add(() =>
            {
                Canvas.willRenderCanvases -= ResizeBrowser;
                if (scroll) scroll.onValueChanged.RemoveListener(OnScroll);
                foreach (var card in cards)
                    if (card.Root) UnityEngine.Object.Destroy(card.Root.gameObject);
                cards.Clear();
            });
        }

        // The bag, material picker and confirmation panel share a window lifetime and can each reopen.
        private static void BindAction(BindingScope scope, GameButton button, Func<CancellationToken, Task> handler)
        {
            var token = scope.Token;
            bool running = false;
            async void Click()
            {
                if (running || token.IsCancellationRequested || !button.isActiveAndEnabled || !button.IsInteractable()) return;
                running = true;
                try { await handler(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) { Debug.LogException(error, button); }
                finally { running = false; }
            }
            button.onClick.AddListener(Click);
            scope.Add(() => { if (button) button.onClick.RemoveListener(Click); });
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
            CloseListOptions();
            bag = active = false;
            pending = null;
            pickingMaterial = false;
            Refresh(reset: false);
        }

        internal Task PreloadAsync(CancellationToken token, ArtifactInventorySnapshot initial) => LoadAsync(token, initial);

        internal (Sprite Art, int Level, int Stars, bool Locked) EquippedCard(string hero)
        {
            var match = snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == hero);
            if (match == null) return default;
            var entry = art.Find(match.templateId);
            return (entry?.combatIcon, match.level, entry?.template.Stars ?? 0, match.locked);
        }

        internal (int Attack, int Health) EquippedStats(string hero)
        {
            var match = snapshot?.artifacts?.FirstOrDefault(item => item.heroInstanceId == hero);
            var template = match == null ? null : art.Find(match.templateId)?.template;
            return template == null ? (0, 0) : (template.AttackAt(match.level), template.HealthAt(match.level));
        }

        internal void PaintSlot(HeroInventoryItem hero)
        {
            slotButton.interactable = hero != null;
            var worn = hero == null ? default : EquippedCard(hero.instanceId);
            equipmentSlot.Set(worn.Art, worn.Level);
            for (int i = 0; i < 5; ++i) ui.Get<Image>("SlotStar" + i).enabled = i < worn.Stars;
            ui.Get<Image>("SlotLock").enabled = worn.Locked;
        }

        internal bool Back()
        {
            if (CloseListOptions()) return true;
            if (BackEnhancement()) return true;
            if (paintingOpen) { paintingOpen = false; Refresh(false); return true; }
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
            selected = WornArtifact(hero);
            inspectingWorn = selected != null;
            storyTab = false;
            if (snapshot != null) Refresh(true);
            else
            {
                await LoadAsync(token);
                if (!token.IsCancellationRequested && pickerHero == hero && WornArtifact(hero) != null)
                {
                    inspectingWorn = true;
                    Refresh(true);
                }
            }
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
            inspectingWorn = storyTab = paintingOpen = false;
            active = bag;
            if (bag && bagReturnItem != null) selected = bagReturnItem;
            bagReturnItem = null;
            Refresh(reset: false);
            return true;
        }

        private Task LoadAsync(CancellationToken token) => LoadAsync(token, null);

        private async Task LoadAsync(CancellationToken token, ArtifactInventorySnapshot initial)
        {
            if (loading) return;
            loading = true;
            failed = false;
            failure = null;
            try
            {
                var read = initial == null ? fetch(token) : Task.FromResult(initial);
                if (!read.IsCompleted) Refresh(reset: false);
                snapshot = await read;
            }
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
            RefreshEnhancement();
            if (RefreshHeroEquipment(reset)) return;
            bool picker = pickerHero != null || pickingMaterial;
            var frame = browser;
            frame.gameObject.SetActive(active);
            ui.Get<GameObject>("Confirmation").SetActive(active && pending != null && !pickingMaterial);
            ui.Get<GameButton>("PickerShade").gameObject.SetActive(active && picker);
            if (!active) return;

            frame.anchorMin = picker ? new Vector2(.075f, .055f) : Vector2.zero;
            frame.anchorMax = picker ? new Vector2(.925f, .945f) : Vector2.one;
            LayoutBrowser(frame.rect.size, picker);
            ui.Get<Image>("BrowserPlate").enabled = picker;
            ui.Get<GameButton>("PickerClose").gameObject.SetActive(picker);
            var heading = ui.Get<GameText>("PickerTitle");
            heading.gameObject.SetActive(picker);
            heading.text = locale.Resolve(Prefix + (pickingMaterial ? "materials_title" : "picker_title"));
            ui.Get<GameText>("Capacity").text = snapshot == null ? "" : Format("capacity", snapshot.used, snapshot.capacity);
            ui.Get<GameText>("SortLabel").text = locale.Resolve(sortCaptionKeys[bagOptions.order]);
            for (int index = 0; index < FilterClasses.Length; index++)
                ui.Get<Image>("FilterActive" + index).gameObject.SetActive(index == filter);

            var candidates = snapshot?.artifacts?.Where(CanDisplay) ?? Enumerable.Empty<ArtifactInventoryItem>();
            visible = OrderedArtifacts(candidates, bagOptions);
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

        private void ResizeBrowser()
        {
            if (!active || !scroll || !browser.gameObject.activeInHierarchy) return;
            var size = browser.rect.size;
            if (size == lastBrowserSize) return;
            LayoutBrowser(size, pickerHero != null || pickingMaterial);
            scroll.content.sizeDelta = new Vector2(0, Mathf.CeilToInt((float)visible.Length / Columns) * RowHeight);
            DrawCards();
        }

        private void LayoutBrowser(Vector2 size, bool picker)
        {
            lastBrowserSize = size;
            if (!picker)
            {
                // The right-side actions use fixed widths. Keep every class icon and its hit area on their side of the bar.
                var strip = ui.Get<RectTransform>("ClassStrip");
                var sortRect = (RectTransform)ui.Get<GameButton>("Sort").transform;
                float available = sortRect.TransformPoint(new Vector3(sortRect.rect.xMin,0)).x - strip.position.x;
                float inCanvasUnits = available / Mathf.Max(.001f,strip.parent.lossyScale.x);
                strip.localScale = Vector3.one * Mathf.Clamp01((inCanvasUnits-8f)/strip.rect.width);
            }
            // At the 720-line source height the bag detail is 399 wide, 29 from the right and 142 from the top.
            float side = Mathf.Min(399f * 1.5f, size.x * (picker ? .36f : .34f));
            float margin = picker ? 19 : 29 * 1.5f;
            float top = picker ? size.y * .12f : 142f * 1.5f;
            var detail = (RectTransform)ui.Get<GameObject>("Detail").transform;
            detail.anchorMin = new Vector2(1, 0);
            detail.anchorMax = Vector2.one;
            detail.offsetMin = new Vector2(-margin - side, picker ? 15 : 0);
            detail.offsetMax = new Vector2(-margin, -top);
            var list = (RectTransform)scroll.transform;
            list.anchorMin = Vector2.zero;
            list.anchorMax = Vector2.one;
            list.offsetMin = new Vector2(picker ? 21 : Mathf.Min(150f * 1.5f, size.x * .145f), picker ? 15 : 0);
            list.offsetMax = new Vector2(-margin - side - 18, -top - 5);
        }

        private void DrawCards()
        {
            if (!active) return;
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.content.anchoredPosition.y / RowHeight) - 1) * Columns;
            int displayed = Mathf.Clamp(visible.Length - first, 0,
                (Mathf.CeilToInt(Mathf.Max(scroll.viewport.rect.height, 700f) / RowHeight) + 3) * Columns);
            while (cards.Count < displayed)
            {
                var card = new ArtifactInventoryCardBinder(UnityEngine.Object.Instantiate(ui.Get<GameObject>("Card"), scroll.content, false));
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
                card.Root.anchorMin = card.Root.anchorMax = card.Root.pivot = new Vector2(0, 1);
                card.Root.sizeDelta = new Vector2(CardWidth, CardWidth * (135f / 358f));
                card.Root.anchoredPosition = new Vector2(col * CellWidth + 4, -(index / Columns) * RowHeight);
                int role = Array.IndexOf(FilterClasses, entry.template.HeroClass);
                var roleIcon = role > 0 ? classIcons[role] : null;
                card.Display(item, entry, roleIcon, ArtifactSkillLevel(item), item == selected, HasWearer(item) ? heroPortrait?.Invoke(item.heroInstanceId) : null);
            }
        }

        private void UpdateDetail()
        {
            var entry = selected == null || pickingMaterial ? null : art.Find(selected.templateId);
            ui.Get<GameObject>("Detail").SetActive(entry != null);
            ui.Get<Image>("EmptyPlate").enabled = entry == null;
            ui.Get<GameObject>("EmptyIcon").SetActive(entry == null && !failed && !loading);
            ui.Get<GameObject>("EmptyMessage").SetActive(entry == null && !failed && !loading);
            if (entry == null) return;
            var template = entry.template;
            SetImage(ui.Get<Image>("Portrait"), entry.card);
            ui.Get<GameText>("Title").text = template.Name;
            ui.Get<GameText>("Level").text = ArtifactSkillLevel(selected);
            ui.Get<GameText>("Attack").text = template.AttackAt(selected.level).ToString(CultureInfo.InvariantCulture);
            ui.Get<GameText>("Health").text = template.HealthAt(selected.level).ToString(CultureInfo.InvariantCulture);
            PaintBagDetail(entry);
            for (int i = 0; i < 5; ++i)
            {
                ui.Get<Image>("Break" + i).enabled = i < selected.limitBreaks;
                ui.Get<Image>("Rarity" + i).enabled = i < template.Stars;
            }
            ui.Get<GameText>("Wearer").text = HasWearer(selected)
                ? Format("wearing", heroName(selected.heroInstanceId)) : locale.Resolve(Prefix + "unassigned");
            ui.Action("equip").gameObject.SetActive(pickerHero != null);
            ui.Action("equip").interactable = pickerHero != null && selected.heroInstanceId != pickerHero.instanceId;
            ui.Action("unequip").gameObject.SetActive(HasWearer(selected));
            ui.ActionLabel("unequip").text = locale.Resolve("home.gear.singleRemove");
            ui.Action("unequip").interactable = HasWearer(selected);
            ui.Action("lock").interactable = true;
            ui.ActionLabel("lock").text = locale.Resolve(Prefix + (selected.locked ? "unlock" : "lock"));
            ui.Action("sell").interactable = !selected.locked && !HasWearer(selected);
            ui.Action("enhance").interactable = selected.level < 30;
            ui.ActionLabel("enhance").text = locale.Resolve(Prefix +
                (selected.level >= 15 + 3 * selected.limitBreaks && selected.limitBreaks < 5 ? "limit_break" : "enhance"));
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
                token.ThrowIfCancellationRequested();
                GameUiFeedback.Play(ui.Action("confirm"), request.Action switch
                {
                    "equip" => GameUiFeedback.Sound.EquipArtifact,
                    "unequip" => GameUiFeedback.Sound.EquipRemove,
                    "lock" => GameUiFeedback.Sound.ItemLock,
                    _ => GameUiFeedback.Sound.Silent
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
            if (pending == null && pickerHero != null && (request.Action == "equip" || request.Action == "unequip"))
                ClosePicker();
            else
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
        private string ArtifactStory(ArtifactInventoryItem item) => locale.Resolve(Prefix + "story." + item.templateId);
        private string ArtifactSkillLevel(ArtifactInventoryItem item)
        {
            int maximum = 6 + item.limitBreaks;
            int rank = 1 + item.level / 3;
            return rank >= maximum ? Format("skillRankMax", maximum) : Format("skillRankFormat", rank, maximum);
        }
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

    }
}
