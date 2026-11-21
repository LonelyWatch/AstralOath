using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleTurnHud : IDisposable
    {
        private const int FullReadiness = 10000;
        private const float SourceScale = 1.5f;
        private readonly RectTransform liveRail;
        private readonly int originalRailSibling;
        private readonly Sprite hourglassSprite;
        private readonly RectTransform portraitLayer;
        private readonly RectTransform details;
        private readonly RectTransform detailRows;
        private readonly Image detailsBackdrop;
        private readonly CanvasGroup expandedRows;
        private readonly Image detailsTrack;
        private const float SpreadSeconds = .30f, BackdropSeconds = .24f;
        private float detailsElapsed;
        private readonly GameButton closeDetailsButton;
        private readonly UnityAction closeDetails;
        private readonly RectTransform feedbackLayer;
        private readonly RectTransform track;
        private readonly GameText textSource;
        private readonly GameText numberSource;
        private readonly TMP_FontAsset damageFont;
        private readonly GameButton detailsButton;
        private readonly UnityAction toggleDetails;
        private readonly IReadOnlyDictionary<string, Sprite> sprites;
        private readonly IReadOnlyDictionary<string, BattleCatalogData.SkillRow> skillDefinitions;
        private Sprite PassiveFrame => sprites.GetValueOrDefault("_frame_passive");
        private Sprite PassiveCooldown => sprites.GetValueOrDefault("cm_cool_skill_passive");
        private Sprite ActiveCooldown => sprites.GetValueOrDefault("cm_cool_skill");
        private readonly Action<BattleUnitSnapshot, int, Image> inspectSkill;
        private readonly Action<BattleStatusSnapshot, Image> inspectStatus;
        private readonly Action closeInspection;
        private readonly BattleStatusArt statusArt;
        private readonly Dictionary<string, Portrait> portraits = new Dictionary<string, Portrait>(StringComparer.Ordinal);
        private readonly List<Portrait> ordered = new List<Portrait>(8);
        private readonly Comparison<Portrait> comparePortraits;
        private readonly List<Image> staticPortraits = new List<Image>();
        private readonly List<Cooldown> cooldowns = new List<Cooldown>(3);
        private string activeId;

        internal BattleTurnHud(BindingMap map, Func<BattleUnitSnapshot, string> label,
            IReadOnlyDictionary<string, Sprite> sprites, TMP_FontAsset damageFont = null)
            : this(map, label, sprites, damageFont, null, null, null, null)
        {
        }

        internal BattleTurnHud(BindingMap map, Func<BattleUnitSnapshot, string> label,
            IReadOnlyDictionary<string, Sprite> sprites, TMP_FontAsset damageFont,
            Action<BattleUnitSnapshot, int, Image> inspectSkill,
            Action<BattleStatusSnapshot, Image> inspectStatus, Action closeInspection,
            BattleStatusArt statusArt)
            : this(map, label, sprites, damageFont, inspectSkill, inspectStatus, closeInspection, statusArt, null)
        {
        }

        internal BattleTurnHud(BindingMap map, Func<BattleUnitSnapshot, string> label,
            IReadOnlyDictionary<string, Sprite> sprites, TMP_FontAsset damageFont,
            Action<BattleUnitSnapshot, int, Image> inspectSkill,
            Action<BattleStatusSnapshot, Image> inspectStatus, Action closeInspection,
            BattleStatusArt statusArt, IReadOnlyDictionary<string, BattleCatalogData.SkillRow> skillDefinitions,
            BattleUnitPresentationProfile presentation = null, string cooldownFormat = "{0}")
        {
            this.sprites = sprites;
            this.skillDefinitions = skillDefinitions;
            comparePortraits = ComparePortraits;
            this.inspectSkill = inspectSkill;
            this.inspectStatus = inspectStatus;
            this.closeInspection = closeInspection;
            this.statusArt = statusArt;
            this.damageFont = damageFont;
            textSource = map.Require<GameText>("activeName");
            numberSource = map.Require<GameText>("activeHp");
            var hourglass = map.Require<Image>("orderHourglass");
            hourglassSprite = hourglass.sprite;
            var rail = (RectTransform)hourglass.transform.parent;
            originalRailSibling = rail.GetSiblingIndex();
            var detailsArtwork = rail.Find("Details Background").GetComponent<Image>();
            track = (RectTransform)rail.Find("Track");
            foreach (Transform child in rail)
            {
                if (!child.TryGetComponent<Image>(out var image) || !image.enabled ||
                    child == track || image == hourglass || image == detailsArtwork)
                    continue;
                staticPortraits.Add(image);
                image.enabled = false;
            }

            liveRail = Layer(rail, "Live Readiness");
            portraitLayer = Layer(liveRail, "Readiness Portraits");
            feedbackLayer = Layer(map.transform, "Battle Feedback");
            var buttonRect = Rect(liveRail, "Readiness Details Button",
                detailsArtwork.rectTransform.anchoredPosition, detailsArtwork.rectTransform.rect.size);
            var clickSurface = buttonRect.gameObject.AddComponent<Image>();
            clickSurface.color = Color.clear;
            clickSurface.raycastTarget = true;
            clickSurface.canvasRenderer.cullTransparentMesh = false;
            detailsButton = buttonRect.gameObject.AddComponent<GameButton>();
            detailsButton.targetGraphic = clickSurface;
            detailsButton.transition = Selectable.Transition.None;

            // inbattle_order_detail.csb: full-screen close surface and a left-side expanded queue.
            details = Layer(liveRail, "Readiness Details");
            var closeSurface = Layer(details, "Close Surface").gameObject.AddComponent<Image>();
            closeSurface.color = Color.clear;
            closeSurface.raycastTarget = true;
            closeSurface.canvasRenderer.cullTransparentMesh = false;
            closeDetailsButton = closeSurface.gameObject.AddComponent<GameButton>();
            closeDetailsButton.targetGraphic = closeSurface;
            closeDetailsButton.transition = Selectable.Transition.None;
            // bg_glow is a vertical source gradient rotated across the screen, with a stretched opaque cap.
            const float glowWidth = 209f * 5.5f * SourceScale;
            detailsBackdrop = Rect(details, "Details Background", new Vector2(glowWidth * .5f, 540f),
                new Vector2(800f * SourceScale, glowWidth)).gameObject.AddComponent<Image>();
            detailsBackdrop.rectTransform.localEulerAngles = new Vector3(0f, 0f, 90f);
            detailsBackdrop.sprite = sprites.GetValueOrDefault("_grow_s_white");
            detailsBackdrop.type = Image.Type.Sliced;
            detailsBackdrop.pixelsPerUnitMultiplier = 1f / (5.5f * SourceScale);
            detailsBackdrop.color = new Color32(23, 29, 35, 255);
            detailsBackdrop.raycastTarget = false;
            detailsTrack = Rect(details, "Details Track", new Vector2(32f * SourceScale, 355f * SourceScale),
                new Vector2(5.6f * SourceScale, 648f * SourceScale)).gameObject.AddComponent<Image>();
            detailsTrack.sprite = sprites.GetValueOrDefault("_btn_small_white");
            detailsTrack.color = new Color(1f, 1f, 1f, 76f / 255f);
            detailsTrack.raycastTarget = false;
            detailRows = Layer(details, "Rows");
            expandedRows = detailRows.gameObject.AddComponent<CanvasGroup>();
            details.gameObject.SetActive(false);
            toggleDetails = () => SetDetailsVisible(!details.gameObject.activeSelf);
            closeDetails = () => SetDetailsVisible(false);
            detailsButton.onClick.AddListener(toggleDetails);
            closeDetailsButton.onClick.AddListener(closeDetails);
            buttonRect.SetAsLastSibling();
            for (int index = 1; index <= 3; index++)
            {
                var button = map.Require<GameButton>("skill" + index);
                var frame = button.transform.parent.Find($"Skill {index} Frame")?.GetComponent<Image>();
                cooldowns.Add(new Cooldown(button, frame, textSource, presentation, cooldownFormat));
            }
        }

        internal void Refresh(BattleSnapshot snapshot)
        {
            activeId = snapshot.CurrentActorId;
            foreach (var unit in snapshot.Units)
            {
                if (!portraits.TryGetValue(unit.InstanceId, out var portrait))
                {
                    portrait = CreatePortrait(unit);
                    portraits.Add(unit.InstanceId, portrait);
                }
                portrait.Unit = unit;
                portrait.ReadinessBp = unit.ReadinessBp;
                if (!unit.IsAlive && portrait.Visible) portrait.ResetFeedback();
                portrait.Visible = unit.IsAlive;
            }
            ArrangePortraits();
        }

        internal int GetTargetRank(string id)
        {
            if (!portraits.TryGetValue(id, out var portrait)) return 0;
            if (portrait.Visible) return portrait.TargetRank;

            // Legal revival targets follow the living rail in their side's formation order.
            var slot = portrait.Unit.Slot;
            int rank = 1;
            foreach (var other in portraits.Values)
                if (other.Unit.Slot.Side == slot.Side && (other.Visible || other.Unit.Slot.Position < slot.Position))
                    ++rank;
            return rank;
        }

        internal void ShowTargetNumber(string id, Sprite art, Sprite digit)
        {
            if (!portraits.TryGetValue(id, out var portrait)) return;
            portrait.TargetNumber.Show(art, digit);
            portrait.DetailTargetNumber.Show(art, digit);
        }

        internal int SetReadiness(string id, int readinessBp)
        {
            if (!portraits.TryGetValue(id, out var portrait) || !portrait.Visible)
                return 0;
            int change = readinessBp - portrait.ReadinessBp;
            if (change == 0) return 0;
            portrait.ReadinessBp = readinessBp;
            ArrangePortraits();
            return change;
        }

        internal int ShowReadinessChange(string id, int readinessBp)
        {
            int change = SetReadiness(id, readinessBp);
            if (change > 0 && !details.gameObject.activeSelf)
                portraits[id].StartAdvance();
            return change;
        }

        internal void RestoreUnit(string id)
        {
            if (!portraits.TryGetValue(id, out var portrait)) return;
            portrait.ReadinessBp = 0;
            portrait.Visible = true;
            ArrangePortraits();
        }

        internal void RemoveUnit(string id)
        {
            if (!portraits.TryGetValue(id, out var portrait))
                return;
            portrait.Visible = false;
            portrait.ResetFeedback();
            ArrangePortraits();
        }

        internal void SetSkillStyle(int index, bool passive, int maximumTurns) =>
            cooldowns[index].SetStyle(passive ? PassiveFrame : null, passive ? PassiveCooldown : ActiveCooldown, maximumTurns);

        internal void SetCooldown(int index, int turns) => cooldowns[index].Show(turns);

        internal void PlayReadinessHit(string id)
        {
            if (portraits.TryGetValue(id, out var portrait) && portrait.Visible)
                portrait.PlayHit();
        }

        internal void Tick(float delta)
        {
            if (delta <= 0f) return;
            bool open = details.gameObject.activeSelf;
            if (open && detailsElapsed < SpreadSeconds + BackdropSeconds)
                SetDetailsProgress(detailsElapsed + delta);
            float ease = 1f - Mathf.Exp(-delta * 11f);
            bool restoreOrder = false;
            foreach (var portrait in portraits.Values)
            {
                if (!portrait.Visible) continue;
                restoreOrder |= portrait.Tick(delta, ease, open, Mathf.Clamp01(detailsElapsed / SpreadSeconds));
            }
            if (restoreOrder) ArrangePortraits();
            TickFeedback(delta);
        }

        private Portrait CreatePortrait(BattleUnitSnapshot unit)
        {
            var root = Rect(portraitLayer, unit.InstanceId, new Vector2(track.anchoredPosition.x, TrackTop),
                Vector2.zero);
            var highlight = CreateFace(root, unit);
            return new Portrait(root, highlight, CreateDetailRow(unit));
        }

        private Image CreateFace(RectTransform root, BattleUnitSnapshot unit)
        {
            // hud_face.csb: parent scale .7, source icon scale .69, base/frame scale .66.
            bool ally = unit.Slot.Side == BattleSide.Ally;
            string side = ally ? "ally" : "enemy";
            float baseSize = 112f * 0.66f * 0.7f * SourceScale;
            Icon(root, "Base", sprites.GetValueOrDefault("_hero_s_bg_" + side), baseSize, Color.white);
            string faceName = unit.DefinitionId == "preview.slime" ? PreviewSlimeFace(unit.Element) : unit.DefinitionId + "_s";
            if (faceName != null && sprites.TryGetValue(faceName, out var face))
                Icon(root, "Face", face, 112f * 0.69f * 0.7f * SourceScale, Color.white);
            else
                Text(root, "Slot", unit.Slot.Position.ToString(), Vector2.zero, new Vector2(38f, 35f),
                    26f, numberSource, material: numberSource.font.material).color = ElementColor(unit.Element);
            var flash = Icon(root, "Hit Flash", sprites.GetValueOrDefault("mob_icon3"), baseSize * .88f, Color.red);
            flash.enabled = false;
            var ring = sprites.GetValueOrDefault("cm_hero_circool1");
            Color sideColor = ally ? Color.white : new Color32(241, 42, 48, 255);
            float ringSize = 82f * 0.7f * SourceScale;
            var highlightColor = sideColor;
            highlightColor.a = .48f;
            var highlight = Icon(root, "Current Turn", ring, ringSize + 5f, highlightColor);
            Icon(root, "Frame", ring, ringSize, sideColor);
            highlight.enabled = false;
            return highlight;
        }

        // Project preview art mapping, verified against the original slime skin attachments.
        // The encrypted monster database has not been decoded; preview.slime remains its logical ID.
        private static string PreviewSlimeFace(BattleElement element) => element switch
        {
            BattleElement.Fire => "m0001_s",
            BattleElement.Ice => "m0002_s",
            BattleElement.Wind => "m0003_s",
            BattleElement.Dark => "m0005_s",
            _ => null
        };

        private void SetDetailsVisible(bool visible)
        {
            if (!visible && details.gameObject.activeSelf) closeInspection?.Invoke();
            details.gameObject.SetActive(visible);
            if (visible)
            {
                liveRail.parent.SetAsLastSibling();
                ArrangePortraits();
                foreach (var portrait in portraits.Values)
                    portrait.StartSpread();
                SetDetailsProgress(0f);
            }
            else
            {
                liveRail.parent.SetSiblingIndex(originalRailSibling);
                portraitLayer.gameObject.SetActive(true);
                track.gameObject.SetActive(true);
            }
        }

        private void SetDetailsProgress(float elapsed)
        {
            detailsElapsed = elapsed;
            bool spreadComplete = elapsed >= SpreadSeconds;
            expandedRows.alpha = spreadComplete ? 1f : 0f;
            expandedRows.blocksRaycasts = spreadComplete;
            detailsTrack.enabled = spreadComplete;
            portraitLayer.gameObject.SetActive(!spreadComplete);
            track.gameObject.SetActive(!spreadComplete);
            var color = detailsBackdrop.color;
            color.a = Mathf.Clamp01((elapsed - SpreadSeconds) / BackdropSeconds);
            detailsBackdrop.color = color;
        }

        private float TrackBottom => track.anchoredPosition.y - track.rect.height / 2f + 36f;
        private float TrackTop => track.anchoredPosition.y + track.rect.height / 2f - 35f;

        private void ArrangePortraits()
        {
            bool showDetails = details.gameObject.activeSelf;
            ordered.Clear();
            foreach (var portrait in portraits.Values)
            {
                portrait.Root.gameObject.SetActive(portrait.Visible);
                if (showDetails) portrait.Detail.Root.gameObject.SetActive(portrait.Visible);
                if (portrait.Visible) ordered.Add(portrait);
            }
            ordered.Sort(comparePortraits);
            int allyRank = 0, enemyRank = 0;
            for (int index = 0; index < ordered.Count; index++)
            {
                var portrait = ordered[index];
                portrait.TargetRank = portrait.Unit.Slot.Side == BattleSide.Ally ? ++allyRank : ++enemyRank;
                bool acting = portrait.Unit.InstanceId == activeId;
                float readiness = Mathf.Clamp01(portrait.ReadinessBp / (float)FullReadiness);
                float y = Mathf.Lerp(TrackTop, TrackBottom, acting ? 1f : readiness);
                portrait.Position = new Vector2(track.anchoredPosition.x, y);
                portrait.Highlight.enabled = acting && portrait.Highlight.sprite;
                portrait.Root.SetAsFirstSibling();
                if (!showDetails) continue;
                // inbattle_order_detail.csb n_pos1..n_pos10: first row y48, then y128 + n*64.
                float detailY = index == 0 ? 48f : 128f + (index - 1) * 64f;
                portrait.Detail.Root.anchoredPosition = new Vector2(33f, detailY) * SourceScale;
                portrait.Detail.Update(portrait.Unit, portrait.ReadinessBp, acting);
            }
            foreach (var portrait in ordered)
                if (portrait.Advancing) portrait.Root.SetAsLastSibling();
        }

        private int ComparePortraits(Portrait left, Portrait right)
        {
            int result = (right.Unit.InstanceId == activeId).CompareTo(left.Unit.InstanceId == activeId);
            if (result != 0) return result;
            result = right.ReadinessBp.CompareTo(left.ReadinessBp);
            if (result != 0) return result;
            result = ((int)left.Unit.Slot.Side).CompareTo((int)right.Unit.Slot.Side);
            return result != 0 ? result : left.Unit.Slot.Position.CompareTo(right.Unit.Slot.Position);
        }

        private static Color ElementColor(BattleElement element) => element switch
        {
            BattleElement.Fire => new Color(1f, 0.38f, 0.30f),
            BattleElement.Ice => new Color(0.43f, 0.85f, 1f),
            BattleElement.Wind => new Color(0.62f, 0.92f, 0.42f),
            BattleElement.Light => new Color(1f, 0.9f, 0.44f),
            BattleElement.Dark => new Color(0.89f, 0.56f, 1f),
            _ => Color.white
        };

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Layer(Transform parent, string name)
        {
            var layer = Rect(parent, name, Vector2.zero, Vector2.zero);
            layer.anchorMax = Vector2.one;
            layer.offsetMin = layer.offsetMax = Vector2.zero;
            return layer;
        }

        private static Image Icon(Transform parent, string name, Sprite sprite, float size, Color color)
        {
            var rect = Rect(parent, name, Vector2.zero, new Vector2(size, size));
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.sprite = sprite;
            image.color = color;
            image.enabled = sprite;
            return image;
        }

        private static GameText Text(Transform parent, string name, string content, Vector2 position, Vector2 size,
            float fontSize, GameText source, TextAlignmentOptions alignment = TextAlignmentOptions.Center, Material material = null)
        {
            var text = Rect(parent, name, position, size).gameObject.AddComponent<GameText>();
            text.text = content;
            text.font = source.font;
            text.fontSharedMaterial = material ? material : source.fontSharedMaterial;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        public void Dispose()
        {
            if (detailsButton)
                detailsButton.onClick.RemoveListener(toggleDetails);
            if (closeDetailsButton)
                closeDetailsButton.onClick.RemoveListener(closeDetails);
            if (track) track.gameObject.SetActive(true);
            if (liveRail) liveRail.parent.SetSiblingIndex(originalRailSibling);
            foreach (var image in staticPortraits)
                if (image) image.enabled = true;
            foreach (var cooldown in cooldowns)
                cooldown.Dispose();
            Remove(liveRail);
            Remove(feedbackLayer);
        }

        private static void Remove(Component component)
        {
            if (!component) return;
            component.gameObject.SetActive(false);
            component.transform.SetParent(null, false);
            Object.Destroy(component.gameObject);
        }

        private sealed class Portrait
        {
            private const float HitSeconds = 4f / 30f, AdvanceSeconds = 13f / 30f;
            private readonly Image flash, detailFlash;
            private readonly RectTransform detailPortrait;
            private Vector2 railPosition, startPosition, shake;
            private float hitRemaining, advanceRemaining;

            internal Portrait(RectTransform root, Image highlight, DetailRow detail)
            {
                Root = root;
                Highlight = highlight;
                var badgeOffset = new Vector2(45f, 0f);
                var badgeSize = new Vector2(42f, 48f);
                TargetNumber = new BattleTargetBadge(root, badgeOffset, badgeSize);
                detailPortrait = (RectTransform)detail.Root.Find("Portrait");
                DetailTargetNumber = new BattleTargetBadge(detailPortrait, badgeOffset, badgeSize);
                Detail = detail;
                railPosition = root.anchoredPosition;
                flash = root.Find("Hit Flash").GetComponent<Image>();
                detailFlash = detailPortrait.Find("Hit Flash").GetComponent<Image>();
            }

            internal void PlayHit()
            {
                hitRemaining = HitSeconds;
                DrawHit();
            }

            internal bool Advancing => advanceRemaining > 0f;

            internal void StartAdvance()
            {
                startPosition = railPosition;
                advanceRemaining = AdvanceSeconds;
                Root.localScale = Vector3.one * 2.9f;
                Root.SetAsLastSibling();
            }

            internal void StartSpread()
            {
                startPosition = railPosition;
                advanceRemaining = 0f;
                Root.localScale = Vector3.one;
            }

            internal void ResetFeedback()
            {
                hitRemaining = advanceRemaining = 0f;
                Root.localScale = Vector3.one;
                DrawHit();
            }

            internal bool Tick(float delta, float ease, bool spread, float spreadProgress)
            {
                bool advancing = Advancing;
                if (spread)
                    railPosition = Vector2.Lerp(startPosition, Detail.Root.anchoredPosition,
                        Mathf.SmoothStep(0f, 1f, spreadProgress));
                else if (advancing)
                {
                    advanceRemaining = Mathf.Max(0f, advanceRemaining - delta);
                    float remaining = advanceRemaining / AdvanceSeconds;
                    railPosition = Vector2.Lerp(startPosition, Position, 1f - Mathf.Pow(remaining, 1.5f));
                    Root.localScale = Vector3.one * (1f + 1.9f * Mathf.Sqrt(remaining));
                }
                else
                    railPosition = Vector2.Lerp(railPosition, Position, ease);

                if (hitRemaining > 0f)
                {
                    hitRemaining = Mathf.Max(0f, hitRemaining - delta);
                    DrawHit();
                }
                Root.anchoredPosition = railPosition + shake;
                return advancing && !Advancing;
            }

            private void DrawHit()
            {
                float strength = hitRemaining / HitSeconds;
                float phase = (1f - strength) * Mathf.PI * 3f;
                shake = new Vector2(Mathf.Cos(phase) * 4f, Mathf.Sin(phase) * 3f) * strength;
                flash.enabled = detailFlash.enabled = hitRemaining > 0f;
                flash.color = detailFlash.color = strength > .5f ? Color.red : Color.white;
                Root.anchoredPosition = railPosition + shake;
                detailPortrait.anchoredPosition = shake;
            }

            internal RectTransform Root { get; }
            internal Image Highlight { get; }
            internal BattleTargetBadge TargetNumber { get; }
            internal BattleTargetBadge DetailTargetNumber { get; }
            internal int TargetRank { get; set; }
            internal DetailRow Detail { get; }
            internal BattleUnitSnapshot Unit { get; set; }
            internal Vector2 Position { get; set; }
            internal int ReadinessBp { get; set; }
            internal bool Visible { get; set; }
        }

        private sealed class Cooldown
        {
            private readonly Image icon;
            private readonly Image frame;
            private readonly Sprite originalFrame;
            private readonly Vector2 originalPosition, originalSize;
            private readonly GameText number;
            private readonly string format;
            private int maximumTurns;
            private int previousTurns;

            internal Cooldown(GameButton button, Image frame, GameText source,
                BattleUnitPresentationProfile presentation, string format)
            {
                this.format = format ?? "{0}";
                icon = (Image)button.targetGraphic;
                this.frame = frame;
                if (frame)
                {
                    originalFrame = frame.sprite;
                    originalPosition = frame.rectTransform.anchoredPosition;
                    originalSize = frame.rectTransform.sizeDelta;
                }
                Shade = Layer(button.transform, "Cooldown Overlay").gameObject.AddComponent<Image>();
                // P2 08:29.500 retains about 40% of the uncovered icon's sRGB brightness.
                float retained = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.GammaToLinearSpace(.4f) : .4f;
                Shade.color = new Color(0f, 0f, 0f, 1f - retained);
                Shade.type = Image.Type.Filled;
                Shade.fillMethod = Image.FillMethod.Radial360;
                Shade.fillOrigin = (int)Image.Origin360.Top;
                Shade.fillClockwise = false;
                Shade.raycastTarget = false;
                number = Text(Shade.transform, "Cooldown Number", "", Vector2.zero, icon.rectTransform.sizeDelta,
                    28.5f, source, TextAlignmentOptions.MidlineGeoAligned);
                if (presentation && presentation.EffectWordFont && presentation.EffectWordOutline)
                {
                    number.font = presentation.EffectWordFont;
                    number.fontSharedMaterial = presentation.EffectWordOutline;
                    number.UpdateMeshPadding();
                }
                number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                number.rectTransform.anchoredPosition = Vector2.zero;
                Shade.gameObject.SetActive(false);
            }

            private Image Shade { get; }

            internal void SetStyle(Sprite passiveFrame, Sprite cooldownMask, int turnsWhenUsed)
            {
                maximumTurns = turnsWhenUsed;
                Shade.sprite = cooldownMask;
                Vector2 inset = passiveFrame ? icon.rectTransform.sizeDelta * .075f : Vector2.zero;
                Vector2 center = Vector2.zero;
                if (frame)
                {
                    frame.sprite = passiveFrame ? passiveFrame : originalFrame;
                    frame.rectTransform.anchoredPosition = passiveFrame
                        ? icon.rectTransform.anchoredPosition : originalPosition;
                    frame.rectTransform.sizeDelta = passiveFrame ? icon.rectTransform.sizeDelta : originalSize;
                    center = frame.rectTransform.anchoredPosition - icon.rectTransform.anchoredPosition;
                }
                Shade.rectTransform.offsetMin = center + inset;
                Shade.rectTransform.offsetMax = center - inset;
                if (previousTurns > 0) Show(previousTurns);
            }

            internal void Dispose()
            {
                SetStyle(null, null, 0);
                Remove(Shade);
            }

            internal void Show(int turns)
            {
                if (turns > 0)
                {
                    Shade.fillAmount = maximumTurns > 0 ? Mathf.Clamp01((float)turns / maximumTurns) : 1f;
                    if (previousTurns != turns)
                        number.text = string.Format(format, turns);
                }
                if (previousTurns != turns)
                    Shade.gameObject.SetActive(turns > 0);
                previousTurns = turns;
            }
        }
    }
}
