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

namespace AstralOath.Client.Home
{
    // Owns the ordinary equipment page only. Material use, rolls and rewards remain server-authoritative.
    internal sealed class EquipmentEnhancementBinder
    {
        private const string Prefix = "home.gear.enhance.";
        private readonly EquipmentEnhancement view;
        private readonly BindingScope scope;
        private readonly ILocaleService locale;
        private readonly EquipmentInventoryApi api;
        private readonly Action back;
        private readonly Action<EquipmentInventorySnapshot> changed;
        private readonly Action<EquipmentItemCardBinder, EquipmentInventoryItem> bindCard;
        private readonly Func<EquipmentInventoryItem, string> itemName, gradeName, setLabel;
        private readonly Func<EquipmentStatValue, Sprite> statArt;
        private readonly Func<EquipmentStatValue, string> statValue;
        private readonly Dictionary<string, int> selected = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly UiAudioPlayback.OneShots sounds = new UiAudioPlayback.OneShots();
        private readonly EquipmentItemCardBinder card, thumbnail;
        private EquipmentInventorySnapshot inventory;
        private EquipmentInventorySnapshot presentedReceipt;
        private EquipmentInventoryItem target;
        private EquipmentEnhancementRules rules;
        private EquipmentEnhancementProfile profile;
        private EquipmentEnhancementMaterialRule[] materials = Array.Empty<EquipmentEnhancementMaterialRule>();
        private EquipmentEnhancementResult lastResult;
        private EquipmentEnhancementMaterial[] retryMaterials;
        private string requestId, quantityKind;
        private long requestVersion;
        private int quantityLimit, autoLevel = 3;
        private bool powderOnly, submitting, animating, startPlayed, finishPlayed;
        private float animationStart, idleStart;
        private float resultSweepStart = -1;
        private readonly bool[] resultSweepRows = new bool[4];
        private bool resultSweepScore;
        private EquipmentEnhancementResultDisplay resultDisplay;
        private string message;
        private T Get<T>(string key) where T : UnityEngine.Object => view.Map.Require<T>(key);
        private string L(string key) => locale.Resolve(Prefix + key);
        private string F(string key, params object[] values) => GameText.Format(locale, Prefix + key, values);
        private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        private bool Closed => scope.Token.IsCancellationRequested;
        private bool Editable => !Closed && !submitting && !animating && retryMaterials == null && Supported && target.enhanceLevel < 15;
        private bool Supported => target != null && target.enhancementSupported && profile != null && profile.supported;

        internal EquipmentEnhancementBinder(EquipmentEnhancement view, BindingScope scope, ILocaleService locale,
            EquipmentInventoryApi api, Action back, Action<EquipmentInventorySnapshot> changed,
            Action<EquipmentItemCardBinder, EquipmentInventoryItem> bindCard, Func<EquipmentInventoryItem, string> itemName,
            Func<EquipmentStatValue, Sprite> statArt, Func<EquipmentStatValue, string> statValue,
            Func<EquipmentInventoryItem, string> gradeName, Func<EquipmentInventoryItem, string> setLabel)
        {
            this.view = view; this.scope = scope; this.locale = locale; this.api = api;
            this.back = back; this.changed = changed; this.bindCard = bindCard; this.itemName = itemName;
            this.statArt = statArt; this.statValue = statValue; this.gradeName = gradeName; this.setLabel = setLabel;
            card = new EquipmentItemCardBinder(Get<BindingMap>("card"));
            thumbnail = new EquipmentItemCardBinder(Get<BindingMap>("thumbnail"));
            void Hook(string key, UnityAction action)
            {
                var control = Get<GameButton>(key);
                control.onClick.AddListener(action);
                scope.Add(() => { if (control) control.onClick.RemoveListener(action); });
            }
            Get<GameButton>("back").FeedbackSound = GameUiFeedback.Sound.PageBack;
            Hook("back", () =>
            {
                if (Get<GameObject>("quantityPopup").activeSelf) Get<GameObject>("quantityPopup").SetActive(false);
                else if (Get<GameObject>("autoPopup").activeSelf) Get<GameObject>("autoPopup").SetActive(false);
                else back();
            });
            Hook("submit", () => { if (!submitting && !animating) _ = SubmitAsync(); });
            Hook("clear", () => { if (Editable) { selected.Clear(); lastResult = null; Play("slotOutSound"); Refresh(); } });
            Hook("auto", () => { if (Editable) { RefreshAutoLabels(); Get<GameObject>("autoPopup").SetActive(!Get<GameObject>("autoPopup").activeSelf); } });
            Hook("powderOnly", () => { powderOnly = !powderOnly; RefreshAutoLabels(); });
            for (int i = 0; i < 5; i++)
            {
                int stage = 3 + i * 3;
                Hook("auto" + stage, () => AutoRegister(stage));
                int index = i;
                Hook("remove" + i, () =>
                {
                    if (!Editable) return;
                    var sorted = SelectedRules();
                    if (index >= sorted.Length) return;
                    selected.Remove(sorted[index].id); lastResult = null; Play("slotOutSound"); Refresh();
                });
            }
            for (int i = 0; i < 4; i++)
            {
                int row = i;
                var button = Get<BindingMap>("row" + i).Require<GameButton>("select");
                UnityAction handler = () => { if (Editable && row < materials.Length) OpenQuantity(materials[row]); };
                button.onClick.AddListener(handler); scope.Add(() => { if (button) button.onClick.RemoveListener(handler); });
            }
            Hook("quantityCancel", () => Get<GameObject>("quantityPopup").SetActive(false));
            Hook("quantityMin", () => Get<Slider>("quantitySlider").value = 1);
            Hook("quantityMax", () => Get<Slider>("quantitySlider").value = quantityLimit);
            Hook("quantityMinus", () => Get<Slider>("quantitySlider").value--);
            Hook("quantityPlus", () => Get<Slider>("quantitySlider").value++);
            Hook("quantityConfirm", () =>
            {
                if (!Editable || quantityKind == null) return;
                selected[quantityKind] = (int)Get<Slider>("quantitySlider").value;
                lastResult = null; Get<GameObject>("quantityPopup").SetActive(false); Play("slotInSound"); Refresh();
            });
            UnityAction<float> slide = _ => Get<GameText>("quantityCount").text = F("quantityCount", (int)Get<Slider>("quantitySlider").value, Number(quantityLimit));
            Get<Slider>("quantitySlider").onValueChanged.AddListener(slide);
            scope.Add(() => Get<Slider>("quantitySlider").onValueChanged.RemoveListener(slide));
            Canvas.willRenderCanvases += Animate;
            scope.Add(() =>
            {
                Canvas.willRenderCanvases -= Animate; sounds.FadeOut();
                Get<GameObject>("quantityPopup").SetActive(false); Get<GameObject>("autoPopup").SetActive(false);
                resultSweepStart = -1; UpdateResultSweeps();
                selected.Clear(); target = null; inventory = null;
            });
            BindLabels();
        }

        internal async Task ShowAsync(EquipmentInventorySnapshot inventory, EquipmentInventoryItem target, CancellationToken token)
        {
            this.inventory = inventory; this.target = target; idleStart = Time.unscaledTime;
            resultDisplay = new EquipmentEnhancementResultDisplay(target);
            resultSweepStart = -1;
            UpdateResultSweeps();
            message = L("loading"); Refresh();
            rules = await api.FetchEnhancementRulesAsync(token);
            token.ThrowIfCancellationRequested();
            profile = rules.profiles.FirstOrDefault(row => row.itemLevel == target.itemLevel && row.grade == target.grade &&
                row.slot == target.slot && row.mainKind == target.mainStat.kind);
            string group = target.slot == "ring" || target.slot == "necklace" ? "j" : "e";
            var standard = new[] { "ma_est1" + group, "ma_est2" + group, "ma_est4" + group, "ma_est6" + group };
            materials = standard.Select(id => rules.materials.FirstOrDefault(row => row.id == id && row.slots.Contains(target.slot)))
                .Where(row => row != null).ToArray();
            message = !Supported ? L("unsupported") : target.enhanceLevel == 15 ? L("maximum") : null;
            var pending = api.GetPendingEnhancement(target.instanceId);
            if (pending != null)
            {
                retryMaterials = pending.materials; requestId = pending.requestId; requestVersion = pending.expectedVersion;
                foreach (var material in retryMaterials) selected[material.kind] = material.quantity;
                message = L("retry");
            }
            Refresh();
        }

        private void BindLabels()
        {
            foreach (var key in new[] { "title", "materialsTitle", "scoreLabel", "overflow", "arcaneLabel", "quantityTitle", "quantityInstruction" })
                Get<GameText>(key).text = L(key);
            foreach (var key in new[] { "submit", "clear", "auto", "quantityConfirm", "quantityMin", "quantityMax", "quantityMinus", "quantityPlus" })
                Get<GameText>(key + "Label").text = L(key);
            RefreshAutoLabels();
        }

        private void RefreshAutoLabels()
        {
            // The source uses separate checkbox images; do not render the old text-square prefix.
            Get<GameText>("powderOnlyLabel").text = L("powderOnly").TrimStart('□', '☑', ' ');
            Get<Image>("powderOnlyCheck").enabled = powderOnly;
            for (int stage = 3; stage <= 15; stage += 3)
            {
                Get<GameText>("auto" + stage + "Label").text = F("stage", stage);
                Get<GameText>("auto" + stage + "Label").fontStyle = stage == autoLevel ? TMPro.FontStyles.Bold : TMPro.FontStyles.Normal;
                Get<GameText>("auto" + stage + "Label").color = stage == autoLevel ? Color.white : new Color32(191, 191, 191, 255);
                Get<Image>("auto" + stage + "Selected").enabled = stage == autoLevel;
            }
        }

        private void OpenQuantity(EquipmentEnhancementMaterialRule material)
        {
            long otherXp = SelectionExperience() - (long)material.experience * Quantity(material.id);
            long remaining = Math.Max(0, profile.xpThresholds[15] - target.xp - otherXp);
            quantityLimit = (int)Math.Min(int.MaxValue, Math.Min(Balance(material.id), (remaining + material.experience - 1) / material.experience));
            if (quantityLimit < 1) { message = L("noMaterials"); Refresh(); return; }
            quantityKind = material.id;
            var slider = Get<Slider>("quantitySlider"); slider.minValue = 1; slider.maxValue = quantityLimit;
            slider.SetValueWithoutNotify(Mathf.Clamp(Quantity(material.id), 1, quantityLimit));
            Get<GameText>("quantityCount").text = F("quantityCount", (int)slider.value, Number(quantityLimit));
            Get<GameObject>("autoPopup").SetActive(false); Get<GameObject>("quantityPopup").SetActive(true);
        }

        private void AutoRegister(int stage)
        {
            if (!Editable) return;
            stage = Math.Max(stage, Math.Min(15, (target.enhanceLevel / 3 + 1) * 3)); autoLevel = stage;
            long needed = Math.Max(0, profile.xpThresholds[stage] - target.xp);
            selected.Clear();
            var ordered = materials.Where(row => !powderOnly || row.experience == materials.Min(m => m.experience))
                .OrderByDescending(row => row.experience).ToArray();
            foreach (var material in ordered)
            {
                if (needed <= 0) break;
                long available = Balance(material.id);
                long count = Math.Min(available, needed / material.experience);
                if (count > 0) { selected[material.id] = (int)Math.Min(count, int.MaxValue); needed -= count * material.experience; }
            }
            if (needed > 0)
            {
                var extra = ordered.OrderBy(row => row.experience).FirstOrDefault(row => Balance(row.id) > Quantity(row.id));
                if (extra != null) { selected[extra.id] = Quantity(extra.id) + 1; needed = 0; }
            }
            lastResult = null; message = needed > 0 ? L("noMaterials") : null;
            Get<GameObject>("autoPopup").SetActive(false); Play("slotInSound"); Refresh();
        }

        private int Quantity(string kind) => selected.TryGetValue(kind, out var count) ? count : 0;
        private long Balance(string kind) => inventory?.materials?.items?.FirstOrDefault(row => row.kind == kind)?.quantity ?? 0;
        private long SelectionExperience() => materials.Sum(row => (long)Quantity(row.id) * row.experience);
        private long Cost() => materials.Sum(row => (long)Quantity(row.id) * row.goldPerUnit);
        private EquipmentEnhancementMaterialRule[] SelectedRules() => materials.Where(row => Quantity(row.id) > 0).OrderByDescending(row => row.experience).ToArray();
        private int PreviewLevel(long xp)
        {
            if (profile == null) return target?.enhanceLevel ?? 0;
            int level = target.enhanceLevel;
            while (level < 15 && xp >= profile.xpThresholds[level + 1]) level++;
            return level;
        }

        private void Refresh()
        {
            if (Closed || target == null) return;
            bindCard(card, target); bindCard(thumbnail, target);
            Get<GameText>("grade").text = gradeName(target); Get<GameText>("name").text = itemName(target);
            Get<GameText>("grade").color = target.grade switch
            {
                "epic" => new Color32(232, 66, 37, 255), "heroic" => new Color32(187, 91, 205, 255),
                "rare" => new Color32(91, 163, 221, 255), "good" => new Color32(126, 176, 64, 255),
                _ => new Color32(181, 181, 181, 255)
            };
            Get<GameText>("set").text = setLabel(target);
            var icon = Get<Image>("mainIcon"); icon.sprite = statArt(target.mainStat); icon.enabled = icon.sprite;
            Get<GameText>("mainName").text = StatName(target.mainStat.kind);
            Get<GameText>("mainValue").text = statValue(target.mainStat);
            long exp = SelectionExperience(); int next = PreviewLevel(target.xp + exp);
            Get<GameText>("mainGain").text = Supported && next > target.enhanceLevel
                ? F("increase", statValue(new EquipmentStatValue { kind = target.mainStat.kind, value = profile.mainValues[next] - target.mainStat.value })) : "";
            for (int i = 0; i < 4; i++)
            {
                var stat = i < target.substats.Length ? target.substats[i] : null;
                int delta = stat == null ? 0 : resultDisplay?.SubstatDelta(stat) ?? 0;
                Get<GameText>("subName" + i).text = stat == null ? "" : StatName(stat.kind) + EquipmentEnhancementResultDisplay.CountSuffix(stat);
                Get<GameText>("subValue" + i).text = stat == null ? "" : statValue(stat) + (delta <= 0 ? "" : " <color=#FF7E00>" +
                    F("delta", statValue(new EquipmentStatValue { kind = stat.kind, value = delta })) + "</color>");
                Color color = delta <= 0 ? (Color)new Color32(171, 179, 191, 255) : Color.white;
                Get<GameText>("subName" + i).color = color;
                Get<GameText>("subValue" + i).color = color;
            }
            int scoreDelta = resultDisplay?.ScoreDelta(target) ?? 0;
            Get<GameText>("score").text = Number(EquipmentScoreCalculator.Calculate(target.ToPiece())) +
                (scoreDelta <= 0 ? "" : "<color=#FF7E00>" + F("delta", Number(scoreDelta)) + "</color>");
            Get<GameText>("balance").text = Number(inventory.materials?.gold ?? 0);
            Get<GameText>("cost").text = Number(Cost());
            Get<GameText>("gain").text = exp == 0 ? "" : F("experienceGain", Number(exp));
            Get<GameText>("nextLevel").text = next > target.enhanceLevel ? "+" + next : "";
            long start = Supported ? profile.xpThresholds[target.enhanceLevel] : 0;
            long required = Supported && target.enhanceLevel < 15 ? profile.xpThresholds[target.enhanceLevel + 1] - start : 0;
            Get<GameText>("experience").text = target.enhanceLevel == 15 ? L("maxExperience") : F("experience", Number(target.xp + exp - start), Number(required));
            Get<RectTransform>("fill").localScale = new Vector3(required == 0 ? 1 : Mathf.Clamp01((float)(target.xp + exp - start) / required), 1, 1);
            Get<GameText>("status").text = message ?? "";
            var selectedRules = SelectedRules();
            for (int i = 0; i < 5; i++)
            {
                var material = i < selectedRules.Length ? selectedRules[i] : null;
                var image = Get<Image>("ingredient" + i); image.sprite = material == null ? null : MaterialIcon(material); image.enabled = image.sprite;
                var quality = Get<Image>("ingredientQuality" + i);
                quality.sprite = material == null ? null : Get<Image>("materialQuality" + Array.FindIndex(materials,row=>row.id==material.id)).sprite;
                quality.enabled = quality.sprite;
                Get<GameText>("ingredientCount" + i).text = material == null ? "" : Number(Quantity(material.id));
                Get<GameButton>("remove" + i).interactable = Editable && material != null;
            }
            for (int i = 0; i < 4; i++)
            {
                var map = Get<BindingMap>("row" + i); var row = i < materials.Length ? materials[i] : null;
                map.gameObject.SetActive(row != null); if (row == null) continue;
                map.Require<Image>("icon").sprite = MaterialIcon(row);
                map.Require<GameText>("name").text = L((target.slot == "ring" || target.slot == "necklace" ? "accessory" : "equipment") + "Material" + i);
                map.Require<GameText>("experience").text = F("materialExperience", Number(row.experience));
                map.Require<GameText>("quantityLabel").text = L("quantityLabel");
                map.Require<GameText>("quantity").text = Quantity(row.id) == 0 ? Number(Balance(row.id)) : F("selectedQuantity", Number(Balance(row.id) - Quantity(row.id)), Quantity(row.id));
                map.Require<GameText>("quantity").color = Quantity(row.id) > 0 ? new Color32(112, 172, 42, 255) : Color.white;
                map.Require<GameButton>("select").interactable = Editable && Balance(row.id) > 0;
            }
            Get<GameButton>("submit").interactable = !submitting && !animating && (retryMaterials != null || (Editable && exp > 0 && Cost() <= (inventory.materials?.gold ?? 0)));
            Get<GameButton>("clear").interactable = Editable && selected.Count > 0;
            Get<GameButton>("auto").interactable = Editable;
        }

        private Sprite MaterialIcon(EquipmentEnhancementMaterialRule material)
        {
            int index = Array.FindIndex(materials, row => row.id == material.id);
            string group = target.slot == "ring" || target.slot == "necklace" ? "acce" : "equip";
            return Get<Image>("materialArt_" + group + "_" + Mathf.Max(0, index)).sprite;
        }

        private string StatName(string kind)
        {
            return locale.Resolve("home.gear." + kind);
        }

        private async Task SubmitAsync()
        {
            if ((!Editable && retryMaterials == null) || submitting || animating || target == null) return;
            var inputs = retryMaterials ?? SelectedRules().Select(row => new EquipmentEnhancementMaterial { kind = row.id, quantity = Quantity(row.id) }).ToArray();
            if (inputs.Length == 0) return;
            if (requestId == null) { requestId = Guid.NewGuid().ToString("N"); requestVersion = inventory.version; }
            retryMaterials = inputs; submitting = true; message = L("submitting"); Refresh();
            try
            {
                var receipt = await api.EnhanceAsync(target.instanceId, requestId, inputs, requestVersion, scope.Token);
                if (Closed) return;
                // The receipt is already authoritative. The visual clock only delays the presentation of its snapshot.
                presentedReceipt = receipt.inventory;
                lastResult = receipt.result;
                changed(receipt.inventory);
                retryMaterials = null; requestId = null; message = null;
                animating = true; animationStart = Time.unscaledTime; startPlayed = finishPlayed = false;
                Get<GameObject>("autoPopup").SetActive(false); Get<GameObject>("quantityPopup").SetActive(false);
                Refresh();
            }
            catch (OperationCanceledException) when (Closed) { }
            catch (EquipmentRequestException error)
            {
                if (Closed) return;
                if (error.Status == HttpStatusCode.Conflict)
                {
                    try
                    {
                        inventory = await api.FetchAsync(scope.Token);
                        if (Closed) return;
                        target = inventory.items.FirstOrDefault(row => row.instanceId == target.instanceId);
                        changed(inventory); selected.Clear(); retryMaterials = null; requestId = null;
                        message = L("conflict");
                    }
                    catch (OperationCanceledException) when (Closed) { }
                    catch (Exception) { if (!Closed) message = L("retry"); }
                }
                else if ((int)error.Status < 500 && error.Status != HttpStatusCode.RequestTimeout)
                { retryMaterials = null; requestId = null; message = L("rejected"); }
                else message = L("retry");
            }
            catch (Exception error)
            {
                if (!Closed) { message = L("retry"); Debug.LogWarning("Equipment enhancement awaiting receipt: " + error.GetType().Name); }
            }
            finally { submitting = false; if (!Closed) Refresh(); }
        }

        private void Play(string key) => sounds.Play(Get<AudioSource>("audio"), Get<AudioSource>(key).clip);
        private void Animate()
        {
            if (Closed || !view.Root.activeInHierarchy) return;
            float idle = Time.unscaledTime - idleStart;
            var speeds = new[] { ("star_01", 8f), ("star_02", -7f), ("star_03", 10f), ("panta_in", -9f), ("stone", 23f) };
            foreach (var (key, speed) in speeds) Get<RectTransform>(key).localEulerAngles = new Vector3(0, 0, idle * speed);
            float elapsed = animating ? Time.unscaledTime - animationStart : -1;
            if (animating && elapsed >= .48f && !startPlayed) { startPlayed = true; Play("startSound"); }
            if (animating && elapsed >= 2.21f && !finishPlayed)
            {
                finishPlayed = true; Play("finishSound");
                if (lastResult?.successTier == "good") Play("goodSound");
                else if (lastResult?.successTier == "great") Play("greatSound");
            }
            float glow = .28f + Mathf.Sin(idle * 6) * .05f + (elapsed < 0 ? 0 : Mathf.Exp(-Mathf.Pow((elapsed - .2f) / .18f, 2)) * .65f);
            float flare = elapsed < 0 ? 0 : Mathf.Exp(-Mathf.Pow((elapsed - 1.7f) / .23f, 2));
            SetAlpha(Get<Image>("glow"), glow); SetAlpha(Get<Image>("success"), flare);
            for (int i = 0; i < 5; i++) SetAlpha(Get<Image>("ingredientFlash" + i), i < selected.Count && elapsed >= 0 ?
                Mathf.Exp(-Mathf.Pow((elapsed - .2f - .1f * i) / .20f, 2)) : 0);
            if (animating && elapsed >= 2.25f)
            {
                inventory = presentedReceipt;
                int previousScore = EquipmentScoreCalculator.Calculate(target.ToPiece());
                target = inventory.items.First(row => row.instanceId == target.instanceId);
                for (int i = 0; i < resultSweepRows.Length; i++)
                    resultSweepRows[i] = i < target.substats.Length && lastResult.changes != null &&
                        lastResult.changes.Any(row => row.kind == target.substats[i].kind && row.after != row.before);
                resultSweepScore = previousScore != EquipmentScoreCalculator.Calculate(target.ToPiece());
                resultSweepStart = Time.unscaledTime;
                presentedReceipt = null;
                animating = false; selected.Clear(); message = lastResult.returnedPowderQuantity > 0
                    ? F("successWithRefund", Number(lastResult.returnedPowderQuantity)) : target.enhanceLevel == 15 ? L("maximum") : L("success"); Refresh();
            }
            UpdateResultSweeps();
        }

        private void UpdateResultSweeps()
        {
            // Source animation:32 samples at30fps, 1.0333s. It is a visual tail only.
            float elapsed = resultSweepStart < 0 ? -1 : Time.unscaledTime - resultSweepStart;
            bool active = elapsed >= 0 && elapsed < 1.0333f;
            int frame = active ? Mathf.Min(31, Mathf.FloorToInt(elapsed * 30)) : 0;
            var uv = new Rect(frame % 8 / 8f, 1 - (frame / 8 + 1) / 4f, 1f / 8, 1f / 4);
            for (int i = 0; i < 5; i++)
            {
                var image = Get<RawImage>(i < 4 ? "subSweep" + i : "scoreSweep");
                image.enabled = active && (i < 4 ? resultSweepRows[i] : resultSweepScore);
                if (image.enabled) image.uvRect = uv;
            }
        }
        private static void SetAlpha(Image image, float alpha) { var color = image.color; color.a = alpha; image.color = color; }
    }

    // Each newly opened page owns this baseline; consecutive authoritative replies do not replace it.
    internal sealed class EquipmentEnhancementResultDisplay
    {
        private readonly Dictionary<string, int> openingSubstats = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly int openingScore;

        internal EquipmentEnhancementResultDisplay(EquipmentInventoryItem opening)
        {
            openingScore = EquipmentScoreCalculator.Calculate(opening.ToPiece());
            foreach (var stat in opening.substats) openingSubstats[stat.kind] = stat.value;
        }

        public int SubstatDelta(EquipmentStatValue stat) => stat.value -
            (openingSubstats.TryGetValue(stat.kind, out var initial) ? initial : 0);
        public int ScoreDelta(EquipmentInventoryItem item) => EquipmentScoreCalculator.Calculate(item.ToPiece()) - openingScore;
        public static string CountSuffix(EquipmentStatValue stat) => stat.enhancementCountKnown && stat.enhancementCount > 0
            ? " <color=#FF7E00>(" + stat.enhancementCount.ToString(CultureInfo.InvariantCulture) + ")</color>" : "";
    }
}
