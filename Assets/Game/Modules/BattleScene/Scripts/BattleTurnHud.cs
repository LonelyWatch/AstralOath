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
            BattleStatusArt statusArt, IReadOnlyDictionary<string, BattleCatalogData.SkillRow> skillDefinitions)
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
            closeSurface.color = new Color(0f, 0f, 0f, 0.20f);
            closeSurface.raycastTarget = true;
            closeDetailsButton = closeSurface.gameObject.AddComponent<GameButton>();
            closeDetailsButton.targetGraphic = closeSurface;
            closeDetailsButton.transition = Selectable.Transition.None;
            var background = Rect(details, "Details Background", new Vector2(600f, 540f),
                new Vector2(1200f, 1080f)).gameObject.AddComponent<Image>();
            background.sprite = sprites.GetValueOrDefault("_grow_s_white");
            background.color = new Color32(23, 29, 35, 255);
            background.raycastTarget = false;
            var detailTrack = Rect(details, "Details Track", new Vector2(32f * SourceScale, 355f * SourceScale),
                new Vector2(5.6f * SourceScale, 648f * SourceScale)).gameObject.AddComponent<Image>();
            detailTrack.sprite = sprites.GetValueOrDefault("_btn_small_white");
            detailTrack.color = new Color(1f, 1f, 1f, 76f / 255f);
            detailTrack.raycastTarget = false;
            detailRows = Layer(details, "Rows");
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
                cooldowns.Add(new Cooldown(button, frame, textSource));
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
                portrait.Visible = unit.IsAlive;
            }
            ArrangePortraits();
        }

        internal void SetReadiness(string id, int readinessBp)
        {
            if (!portraits.TryGetValue(id, out var portrait) || !portrait.Visible)
                return;
            portrait.ReadinessBp = readinessBp;
            ArrangePortraits();
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
            portrait.TickHit(Portrait.HitSeconds);
            ArrangePortraits();
        }

        internal void SetSkillStyle(int index, bool passive) =>
            cooldowns[index].SetStyle(passive ? PassiveFrame : null, passive ? PassiveCooldown : null);

        internal void SetCooldown(int index, int turns) => cooldowns[index].Show(turns);

        internal void PlayReadinessHit(string id)
        {
            if (portraits.TryGetValue(id, out var portrait) && portrait.Visible)
                portrait.PlayHit();
        }

        internal void Tick(float delta)
        {
            float ease = 1f - Mathf.Exp(-delta * 11f);
            foreach (var portrait in portraits.Values)
            {
                if (!portrait.Visible) continue;
                portrait.Root.anchoredPosition = Vector2.Lerp(portrait.Root.anchoredPosition, portrait.Position, ease);
                portrait.TickHit(delta);
            }
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
            string side = unit.Slot.Side == BattleSide.Ally ? "ally" : "enemy";
            float baseSize = 112f * 0.66f * 0.7f * SourceScale;
            Icon(root, "Base", sprites.GetValueOrDefault("_hero_s_bg_" + side), baseSize, Color.white);
            string faceName = unit.DefinitionId == "preview.slime" ? PreviewSlimeFace(unit.Element) : unit.DefinitionId + "_s";
            if (faceName != null && sprites.TryGetValue(faceName, out var face))
                Icon(root, "Face", face, 112f * 0.69f * 0.7f * SourceScale, Color.white);
            else
                Text(root, "Slot", unit.Slot.Position.ToString(), Vector2.zero, new Vector2(38f, 35f),
                    26f, numberSource).color = ElementColor(unit.Element);
            var frame = sprites.GetValueOrDefault("_hero_s_frame_" + side);
            Icon(root, "Frame", frame, frame ? frame.rect.width * 0.66f * 0.7f * SourceScale : baseSize, Color.white);
            var highlight = Icon(root, "Current Turn", sprites.GetValueOrDefault("cm_hero_circool1"), 82f * 0.7f * SourceScale,
                new Color(1f, 0.82f, 0.40f));
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
            }
            else liveRail.parent.SetSiblingIndex(originalRailSibling);
            portraitLayer.gameObject.SetActive(!visible);
            track.gameObject.SetActive(!visible);
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
            for (int index = 0; index < ordered.Count; index++)
            {
                var portrait = ordered[index];
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
            float fontSize, GameText source, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var text = Rect(parent, name, position, size).gameObject.AddComponent<GameText>();
            text.text = content;
            text.font = source.font;
            text.fontSharedMaterial = source.fontSharedMaterial;
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
            internal const float HitSeconds = .17f;
            private static readonly Color HitTint = new Color(1f, .48f, .42f);
            private readonly Image face, detailFace;
            private float hitRemaining;

            internal Portrait(RectTransform root, Image highlight, DetailRow detail)
            {
                Root = root;
                Highlight = highlight;
                Detail = detail;
                face = root.Find("Face")?.GetComponent<Image>();
                detailFace = detail.Root.Find("Portrait/Face")?.GetComponent<Image>();
            }

            internal void PlayHit()
            {
                hitRemaining = HitSeconds;
                DrawHit(1f);
            }

            internal void TickHit(float delta)
            {
                if (hitRemaining <= 0f) return;
                hitRemaining = Mathf.Max(0f, hitRemaining - delta);
                DrawHit(hitRemaining / HitSeconds);
            }

            private void DrawHit(float strength)
            {
                Color tint = Color.Lerp(Color.white, HitTint, strength);
                float x = Mathf.Cos((1f - strength) * 4f * Mathf.PI) * strength * 4f;
                var offset = new Vector2(x, 0f);
                Apply(face);
                Apply(detailFace);

                void Apply(Image image)
                {
                    if (!image) return;
                    image.color = tint;
                    image.rectTransform.anchoredPosition = offset;
                }
            }

            internal RectTransform Root { get; }
            internal Image Highlight { get; }
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
            private Sprite passiveCooldown;
            private int previousTurns;

            internal Cooldown(GameButton button, Image frame, GameText source)
            {
                icon = (Image)button.targetGraphic;
                this.frame = frame;
                if (frame)
                {
                    originalFrame = frame.sprite;
                    originalPosition = frame.rectTransform.anchoredPosition;
                    originalSize = frame.rectTransform.sizeDelta;
                }
                Shade = Layer(button.transform, "Cooldown Overlay").gameObject.AddComponent<Image>();
                Shade.color = new Color(0f, 0f, 0f, 0.56f);
                Shade.raycastTarget = false;
                number = Text(Shade.transform, "Cooldown Number", "", Vector2.zero, icon.rectTransform.sizeDelta,
                    24f, source);
                number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                number.rectTransform.anchoredPosition = Vector2.zero;
                Shade.gameObject.SetActive(false);
            }

            private Image Shade { get; }

            internal void SetStyle(Sprite passiveFrame, Sprite cooldownMask)
            {
                passiveCooldown = cooldownMask;
                if (frame)
                {
                    frame.sprite = passiveFrame ? passiveFrame : originalFrame;
                    frame.rectTransform.anchoredPosition = passiveFrame
                        ? icon.rectTransform.anchoredPosition : originalPosition;
                    frame.rectTransform.sizeDelta = passiveFrame ? icon.rectTransform.sizeDelta : originalSize;
                }
                if (previousTurns > 0) Show(previousTurns);
            }

            internal void Dispose()
            {
                SetStyle(null, null);
                Remove(Shade);
            }

            internal void Show(int turns)
            {
                if (turns > 0)
                {
                    Shade.sprite = passiveCooldown ? passiveCooldown : icon.overrideSprite;
                    if (passiveCooldown)
                    {
                        Shade.rectTransform.offsetMin = icon.rectTransform.sizeDelta * .075f;
                        Shade.rectTransform.offsetMax = -Shade.rectTransform.offsetMin;
                    }
                    else
                    {
                        Shade.rectTransform.offsetMin = Vector2.zero;
                        Shade.rectTransform.offsetMax = Vector2.zero;
                    }
                    if (previousTurns != turns)
                        number.text = $"{turns}回合";
                }
                if (previousTurns != turns)
                    Shade.gameObject.SetActive(turns > 0);
                previousTurns = turns;
            }
        }
    }
}
