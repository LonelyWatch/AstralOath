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
        private const int EnhancementSlots = 20;
        private readonly List<ArtifactInventoryMaterial> ingredients = new List<ArtifactInventoryMaterial>();
        private readonly List<ArtifactInventoryMaterial> availableMaterials = new List<ArtifactInventoryMaterial>();
        private readonly List<ArtifactInventoryCardBinder> materialRows = new List<ArtifactInventoryCardBinder>();
        private readonly Dictionary<string, (string id, ArtifactInventoryMaterial[] inputs)> unresolvedEnhancements =
            new Dictionary<string, (string, ArtifactInventoryMaterial[])>();
        private BindingMap enhancementPage;
        private ScrollRect materialList, ingredientTray;
        private readonly RectTransform[] ingredientTiles = new RectTransform[EnhancementSlots];
        private float trayHeight;
        private BindingScope enhancementOwner;
        private ArtifactInventoryItem enhancementTarget;
        private bool sortMaterialsByLevel;
        private string enhancementMessage;
        private float enhancementStart, successTime = float.NegativeInfinity, materialWidth;
        private RectTransform[] rotatingRunes;
        private Image runeGlow, successFlash;
        private bool EnhancementOpen => enhancementTarget != null;
        private T EnhancementControl<T>(string key) where T : UnityEngine.Object => enhancementPage.Require<T>(key);

        private void BindEnhancement(BindingScope scope)
        {
            enhancementOwner = scope;
            enhancementPage = ui.Get<BindingMap>("Enhancement");
            materialList = EnhancementControl<ScrollRect>("candidates");
            ingredientTray = EnhancementControl<ScrollRect>("tray");
            rotatingRunes = new[] { "star_01", "star_02", "star_03", "panta_in", "stone", "stone_glow" }
                .Select(EnhancementControl<RectTransform>).ToArray();
            runeGlow = EnhancementControl<Image>("glow");
            successFlash = EnhancementControl<Image>("success");
            EnhancementControl<GameButton>("back").FeedbackSound = GameUiFeedback.Sound.PageBack;
            BindAction(scope, EnhancementControl<GameButton>("back"), _ => { BackEnhancement(); return Task.CompletedTask; });
            BindAction(scope, EnhancementControl<GameButton>("submit"), SubmitEnhancementAsync);
            BindAction(scope, EnhancementControl<GameButton>("sort"), _ =>
            {
                sortMaterialsByLevel = !sortMaterialsByLevel;
                materialList.content.anchoredPosition = Vector2.zero;
                RefreshEnhancement();
                return Task.CompletedTask;
            });
            for (int i = 0; i < EnhancementSlots; i++)
            {
                int index = i;
                var remove = EnhancementControl<GameButton>("remove" + i);
                ingredientTiles[i] = (RectTransform)remove.transform;
                BindAction(scope, remove, _ =>
                {
                    if (CanEditEnhancement() && index < ingredients.Count)
                    {
                        ingredients.RemoveAt(index);
                        enhancementMessage = null;
                        RefreshEnhancement();
                    }
                    return Task.CompletedTask;
                });
            }
            materialList.onValueChanged.AddListener(ScrollMaterials);
            Canvas.willRenderCanvases += AnimateEnhancement;
            scope.Add(() =>
            {
                Canvas.willRenderCanvases -= AnimateEnhancement;
                if (materialList) materialList.onValueChanged.RemoveListener(ScrollMaterials);
                foreach (var row in materialRows)
                    if (row.Root) UnityEngine.Object.Destroy(row.Root.gameObject);
                materialRows.Clear();
                enhancementTarget = null;
                unresolvedEnhancements.Clear();
            });
        }

        private Task OpenEnhancementAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (busy || selected == null) return Task.CompletedTask;
            enhancementTarget = selected;
            ingredients.Clear();
            bool retry = unresolvedEnhancements.TryGetValue(selected.instanceId, out var prior);
            if (retry) ingredients.AddRange(prior.inputs);
            enhancementMessage = retry ? "action_failed" : null;
            enhancementStart = Time.unscaledTime;
            successTime = float.NegativeInfinity;
            materialList.StopMovement();
            materialList.content.anchoredPosition = Vector2.zero;
            ingredientTray.StopMovement();
            ingredientTray.content.anchoredPosition = Vector2.zero;
            Refresh(false);
            return Task.CompletedTask;
        }

        private bool BackEnhancement()
        {
            if (!EnhancementOpen) return false;
            if (busy) return true;
            selected = enhancementTarget;
            enhancementTarget = null;
            ingredients.Clear();
            Refresh(false);
            return true;
        }

        private bool CanEditEnhancement() => EnhancementOpen && !busy && !unresolvedEnhancements.ContainsKey(enhancementTarget.instanceId);

        private void RefreshEnhancement()
        {
            if (!enhancementPage) return;
            enhancementPage.gameObject.SetActive(active && EnhancementOpen);
            if (!active || !EnhancementOpen) return;
            var target = enhancementTarget;
            var entry = art.Find(target.templateId);
            var template = entry.template;
            var catalog = art.Catalog;
            int experience = 0, breaks = target.limitBreaks;
            foreach (var material in ingredients)
            {
                if (material.kind == "bottle") ++breaks;
                else if (material.kind == "artifact")
                {
                    var item = FindIngredient(material.instanceId);
                    if (item == null) continue;
                    experience += catalog.MaterialExperience(art.Find(item.templateId).template.Stars, item.experience);
                    if (item.templateId == target.templateId) ++breaks;
                }
                else experience += catalog.CharmExperience(material.kind);
            }
            int cap = 15 + 3 * Mathf.Min(5, breaks);
            int nextExp = Math.Min(catalog.ExperienceForLevel(cap), target.experience + experience);
            int nextLevel = catalog.LevelForExperience(nextExp, Math.Min(5, breaks));
            int currentLevelStart = catalog.ExperienceForLevel(target.level);
            int required = catalog.ExperienceForLevel(Math.Min(cap, target.level + 1)) - currentLevelStart;
            void Value(string key, string value) => EnhancementControl<GameText>(key).text = value;
            void Change(string key, int current, int next)
            {
                Value(key, current.ToString(CultureInfo.InvariantCulture));
                Value(key + "Gain", next > current ? Format("gain", next - current) : "");
            }
            Value("grade", Local("materialRarity" + template.Stars));
            EnhancementControl<GameText>("grade").color = template.Stars switch
            {
                5 => new Color32(255, 92, 47, 255), 4 => new Color32(225, 105, 230, 255), _ => new Color32(77, 179, 235, 255)
            };
            Value("name", template.Name);
            SetImage(EnhancementControl<Image>("portrait"), entry.combatIcon);
            Value("level", target.level > 0 ? Plus(target.level) : "");
            Change("attack", template.AttackAt(target.level), template.AttackAt(nextLevel));
            Change("health", template.HealthAt(target.level), template.HealthAt(nextLevel));
            Change("cap", 15 + 3 * target.limitBreaks, cap);
            Value("rank", Format("skillRank", 1 + target.level / 3));
            Value("skill", template.DescriptionAt(target.level));
            bool skillChanges = nextLevel / 3 > target.level / 3;
            Value("nextRank", skillChanges ? Format("skillRank", 1 + nextLevel / 3) : "");
            Value("nextSkill", skillChanges ? template.DescriptionAt(nextLevel) : "");
            Value("exp", Format("expProgress", (nextExp - currentLevelStart).ToString("N0"), required.ToString("N0")));
            Value("gain", nextLevel > target.level ? Plus(nextLevel - target.level) : "");
            EnhancementControl<RectTransform>("fill").localScale = new Vector3(required == 0 ? 1f :
                Mathf.Clamp01((float)(nextExp - currentLevelStart) / required), 1, 1);
            Value("balance", (snapshot.materials?.gold ?? 0).ToString("N0"));
            Value("cost", catalog.EnhancementCost(experience).ToString("N0"));
            Value("status", enhancementMessage == null ? "" : Local(enhancementMessage));
            Value("sortLabel", Local(sortMaterialsByLevel ? "sort_enhancement" : "sort_rarity"));
            int consumedInstances = ingredients.Count(input => input.kind == "artifact");
            Value("capacity", Format("capacity", snapshot.used - consumedInstances, snapshot.capacity));
            for (int i = 0; i < 5; i++) EnhancementControl<Image>("break" + i).enabled = i < breaks;
            for (int i = 0; i < EnhancementSlots; i++)
                SetImage(EnhancementControl<Image>("material" + i), i < ingredients.Count ? IngredientIcon(ingredients[i]) : null);
            ArrangeIngredientTray();
            availableMaterials.Clear();
            foreach (string kind in new[] { "epicCharm", "greaterCharm", "lesserCharm", "bottle" })
                if (Amount(kind) > ingredients.Count(input => input.kind == kind) && (kind != "bottle" || template.CanUseBottle && breaks < 5))
                    availableMaterials.Add(new ArtifactInventoryMaterial { kind = kind });
            var eligible = snapshot.artifacts.Where(item => item.instanceId != target.instanceId && !HasWearer(item) && !item.locked &&
                art.Find(item.templateId) != null &&
                (item.templateId != target.templateId || breaks < 5) && !ingredients.Any(input => input.instanceId == item.instanceId));
            var ordered = sortMaterialsByLevel ? eligible.OrderByDescending(item => item.level) :
                eligible.OrderByDescending(item => art.Find(item.templateId).template.Stars).ThenByDescending(item => item.level);
            availableMaterials.AddRange(ordered.Select(item => new ArtifactInventoryMaterial { kind = "artifact", instanceId = item.instanceId }));
            ArrangeMaterialList();
        }

        private ArtifactInventoryItem FindIngredient(string id) => snapshot.artifacts.FirstOrDefault(item => item.instanceId == id);

        private void ArrangeIngredientTray()
        {
            trayHeight = ingredientTray.viewport.rect.height;
            int rows = Mathf.Max(2, (ingredients.Count + 4) / 5);
            float rowHeight = trayHeight / 2f;
            ingredientTray.content.sizeDelta = new Vector2(0, rows * rowHeight);
            for (int i = 0; i < ingredientTiles.Length; i++)
            {
                ingredientTiles[i].gameObject.SetActive(i < rows * 5);
                ingredientTiles[i].SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, i / 5 * rowHeight, rowHeight * .972f);
            }
            ingredientTray.vertical = rows > 2;
            ingredientTray.content.anchoredPosition = new Vector2(0,
                Mathf.Clamp(ingredientTray.content.anchoredPosition.y, 0, (rows - 2) * rowHeight));
        }

        private Sprite IngredientIcon(ArtifactInventoryMaterial material)
        {
            if (material.kind != "artifact") return EnhancementControl<Image>(material.kind).sprite;
            var item = FindIngredient(material.instanceId);
            return item == null ? null : art.Find(item.templateId)?.combatIcon;
        }

        private void ArrangeMaterialList()
        {
            materialWidth = Mathf.Max(1f, materialList.viewport.rect.width);
            float height = materialWidth * 145f / 358f;
            materialList.content.sizeDelta = new Vector2(0, availableMaterials.Count * height);
            var offset = materialList.content.anchoredPosition;
            offset.y = Mathf.Clamp(offset.y, 0, Mathf.Max(0, materialList.content.sizeDelta.y - materialList.viewport.rect.height));
            materialList.content.anchoredPosition = offset;
            DrawMaterialRows();
        }

        private void ScrollMaterials(Vector2 _) { if (EnhancementOpen && materialWidth > 1) DrawMaterialRows(); }

        private void DrawMaterialRows()
        {
            if (materialWidth <= 1) return;
            float stride = materialWidth * 145f / 358f;
            int first = Mathf.Max(0, Mathf.FloorToInt(materialList.content.anchoredPosition.y / stride) - 1);
            int count = Mathf.Clamp(availableMaterials.Count - first, 0, Mathf.CeilToInt(materialList.viewport.rect.height / stride) + 3);
            for (int i = 0; i < count; i++)
            {
                if (i == materialRows.Count)
                {
                    var row = new ArtifactInventoryCardBinder(UnityEngine.Object.Instantiate(ui.Get<GameObject>("Card"), materialList.content, false));
                    materialRows.Add(row);
                    BindAction(enhancementOwner, row.Button, _ =>
                    {
                        if (!CanEditEnhancement()) return Task.CompletedTask;
                        var material = availableMaterials.FirstOrDefault(choice => (choice.instanceId ?? choice.kind) == row.Root.name);
                        if (material == null) return Task.CompletedTask;
                        if (ingredients.Count == EnhancementSlots) enhancementMessage = "maxMaterials";
                        else { ingredients.Add(material); enhancementMessage = null; }
                        RefreshEnhancement();
                        ingredientTray.verticalNormalizedPosition = 0;
                        return Task.CompletedTask;
                    });
                }
                var card = materialRows[i];
                var material = availableMaterials[first + i];
                card.Root.anchorMin = card.Root.anchorMax = card.Root.pivot = new Vector2(0, 1);
                card.Root.sizeDelta = new Vector2(materialWidth, materialWidth * (135f / 358f));
                card.Root.anchoredPosition = new Vector2(0, -(first + i) * stride);
                if (material.kind == "artifact")
                {
                    var item = FindIngredient(material.instanceId);
                    var entry = art.Find(item.templateId);
                    int role = Array.IndexOf(FilterClasses, entry.template.HeroClass);
                    card.Display(item, entry, role > 0 ? classIcons[role] : null, ArtifactSkillLevel(item), false);
                }
                else
                {
                    int remaining = Amount(material.kind) - ingredients.Count(input => input.kind == material.kind);
                    card.DisplayMaterial(material.kind, IngredientIcon(material),
                        material.kind == "bottle" ? null : EnhancementControl<Image>(material.kind + "Banner").sprite,
                        Local(material.kind), remaining.ToString(CultureInfo.InvariantCulture));
                }
            }
            for (int i = count; i < materialRows.Count; i++) materialRows[i].Root.gameObject.SetActive(false);
        }

        private async Task SubmitEnhancementAsync(CancellationToken token)
        {
            if (busy || !EnhancementOpen) return;
            if (ingredients.Count == 0)
            {
                enhancementMessage = "materialNeeded";
                RefreshEnhancement();
                return;
            }
            string target = enhancementTarget.instanceId;
            if (!unresolvedEnhancements.TryGetValue(target, out var request))
                unresolvedEnhancements[target] = request = (Guid.NewGuid().ToString("N"), ingredients.ToArray());
            busy = true;
            try
            {
                snapshot = await api.EnhanceAsync(target, request.id, request.inputs, token);
                unresolvedEnhancements.Remove(target);
                enhancementTarget = selected = FindIngredient(target);
                ingredients.Clear();
                enhancementMessage = null;
                successTime = Time.unscaledTime;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (ArtifactRequestException error)
            {
                if ((int)error.Status < 500) unresolvedEnhancements.Remove(target);
                enhancementMessage = error.Code == "artifact_material_insufficient" ? "insufficient" : "action_failed";
            }
            catch (System.IO.IOException) { enhancementMessage = "action_failed"; }
            catch (System.Net.Http.HttpRequestException) { enhancementMessage = "action_failed"; }
            finally { busy = false; }
            if (token.IsCancellationRequested) return;
            Refresh(false);
            onChanged();
        }

        private void AnimateEnhancement()
        {
            if (!EnhancementOpen || !enhancementPage.isActiveAndEnabled) return;
            if (!Mathf.Approximately(materialWidth, materialList.viewport.rect.width)) ArrangeMaterialList();
            if (!Mathf.Approximately(trayHeight, ingredientTray.viewport.rect.height)) ArrangeIngredientTray();
            float seconds = Time.unscaledTime - enhancementStart;
            for (int i = 0; i < rotatingRunes.Length; i++)
                rotatingRunes[i].localRotation = Quaternion.Euler(0, 0, seconds * RuneDegreesPerSecond[i]);
            var glow = new Color32(138, 198, 255, (byte)(255 * Mathf.PingPong(seconds / 4f, 1f)));
            runeGlow.color = glow;
            float age = Time.unscaledTime - successTime;
            successFlash.color = new Color(.65f, .64f, 1f, Mathf.Clamp01(1f - age / .6f));
        }

        private static readonly float[] RuneDegreesPerSecond = { -11.25f, -22.5f, -33.75f, 11.25f, -22.5f, -22.5f };
    }
}
