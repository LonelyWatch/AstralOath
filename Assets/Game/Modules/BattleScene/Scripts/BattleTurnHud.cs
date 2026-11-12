using System;
using System.Collections.Generic;
using System.Linq;
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
        private const float PortraitGap = 29f;
        private readonly RectTransform liveRail;
        private readonly RectTransform portraitLayer;
        private readonly RectTransform details;
        private readonly RectTransform feedbackLayer;
        private readonly RectTransform track;
        private readonly GameText textSource;
        private readonly GameText numberSource;
        private readonly TMP_FontAsset damageFont;
        private readonly GameButton detailsButton;
        private readonly UnityAction toggleDetails;
        private readonly Func<BattleUnitSnapshot, string> label;
        private readonly IReadOnlyDictionary<string, Sprite> sprites;
        private readonly Dictionary<string, Portrait> portraits = new Dictionary<string, Portrait>(StringComparer.Ordinal);
        private readonly List<Image> staticPortraits = new List<Image>();
        private readonly List<Cooldown> cooldowns = new List<Cooldown>(3);
        private string activeId;

        internal BattleTurnHud(BindingMap map, Func<BattleUnitSnapshot, string> label,
            IReadOnlyDictionary<string, Sprite> sprites, TMP_FontAsset damageFont = null)
        {
            this.label = label;
            this.sprites = sprites;
            this.damageFont = damageFont;
            textSource = map.Require<GameText>("activeName");
            numberSource = map.Require<GameText>("activeHp");
            var hourglass = map.Require<Image>("orderHourglass");
            var rail = (RectTransform)hourglass.transform.parent;
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

            var headerRect = detailsArtwork.rectTransform;
            details = Rect(liveRail, "Readiness Details",
                headerRect.anchoredPosition + new Vector2(headerRect.rect.width / 2f + 12f, headerRect.rect.height / 2f),
                new Vector2(540f, 402f));
            details.pivot = new Vector2(0f, 1f);
            var background = details.gameObject.AddComponent<Image>();
            background.color = new Color(0.025f, 0.04f, 0.065f, 0.96f);
            background.raycastTarget = true;
            Text(details, "Header", "行动条", new Vector2(20f, 368f), new Vector2(490f, 28f),
                25f, textSource, TextAlignmentOptions.Left).rectTransform.pivot = new Vector2(0f, 0.5f);
            details.gameObject.SetActive(false);
            toggleDetails = () => details.gameObject.SetActive(!details.gameObject.activeSelf);
            detailsButton.onClick.AddListener(toggleDetails);
            for (int index = 1; index <= 3; index++)
                cooldowns.Add(new Cooldown(map.Require<GameButton>("skill" + index), textSource));
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

        internal void RemoveUnit(string id)
        {
            if (!portraits.TryGetValue(id, out var portrait))
                return;
            portrait.Visible = false;
            ArrangePortraits();
        }

        internal void SetCooldown(int index, int turns) => cooldowns[index].Show(turns);

        internal void Tick(float delta)
        {
            float ease = 1f - Mathf.Exp(-delta * 11f);
            foreach (var portrait in portraits.Values)
            {
                if (portrait.Visible)
                    portrait.Root.anchoredPosition = Vector2.Lerp(portrait.Root.anchoredPosition, portrait.Position, ease);
            }
            TickFeedback(delta);
        }

        private Portrait CreatePortrait(BattleUnitSnapshot unit)
        {
            var root = Rect(portraitLayer, unit.InstanceId, new Vector2(track.anchoredPosition.x, TrackBottom),
                new Vector2(70f, 70f));
            string side = unit.Slot.Side == BattleSide.Ally ? "ally" : "enemy";
            Icon(root, "Base", sprites.GetValueOrDefault("_hero_s_bg_" + side), 62f, Color.white);
            if (sprites.TryGetValue(unit.DefinitionId + "_s", out var face) ||
                unit.DefinitionId == "preview.slime" && sprites.TryGetValue("pet_slime1_n_grade1_1_s", out face))
                Icon(root, "Face", face, 62f, Color.white);
            else
                Text(root, "Slot", unit.Slot.Position.ToString(), new Vector2(35f, 35f), new Vector2(38f, 35f),
                    26f, numberSource).color = ElementColor(unit.Element);
            Icon(root, "Frame", sprites.GetValueOrDefault("_hero_s_frame_" + side), 67f, Color.white);
            var highlight = Icon(root, "Current Turn", sprites.GetValueOrDefault("cm_hero_circool1"), 70f,
                new Color(1f, 0.82f, 0.40f));
            highlight.enabled = false;

            var row = Text(details, unit.InstanceId, "", Vector2.zero, new Vector2(500f, 36f),
                19f, textSource, TextAlignmentOptions.Left);
            row.rectTransform.pivot = new Vector2(0f, 0.5f);
            return new Portrait(root, highlight, row);
        }

        private float TrackBottom => track.anchoredPosition.y - track.rect.height / 2f + 36f;
        private float TrackTop => track.anchoredPosition.y + track.rect.height / 2f - 35f;

        private void ArrangePortraits()
        {
            foreach (var portrait in portraits.Values)
            {
                portrait.Root.gameObject.SetActive(portrait.Visible);
                portrait.Detail.gameObject.SetActive(portrait.Visible);
            }

            var ordered = portraits.Values.Where(portrait => portrait.Visible)
                .OrderByDescending(portrait => portrait.Unit.InstanceId == activeId)
                .ThenByDescending(portrait => portrait.ReadinessBp)
                .ThenBy(portrait => portrait.Unit.Slot.Side)
                .ThenBy(portrait => portrait.Unit.Slot.Position)
                .ToArray();
            float limit = TrackTop;
            for (int index = 0; index < ordered.Length; index++)
            {
                var portrait = ordered[index];
                bool acting = portrait.Unit.InstanceId == activeId;
                float readiness = Mathf.Clamp01(portrait.ReadinessBp / (float)FullReadiness);
                float y = Mathf.Min(limit, Mathf.Lerp(TrackBottom + (ordered.Length - index - 1) * PortraitGap, TrackTop,
                    acting ? 1f : readiness));
                limit = y - PortraitGap;
                portrait.Position = new Vector2(track.anchoredPosition.x, y);
                portrait.Highlight.enabled = acting && portrait.Highlight.sprite;
                portrait.Root.SetAsFirstSibling();

                string cool = string.Join("  ", portrait.Unit.Form.SkillIds.Select((id, skill) =>
                    $"S{skill + 1} {portrait.Unit.GetCooldown(id)}"));
                string statuses = string.Join(" ", portrait.Unit.Statuses.Select(status =>
                    $"{StatusLabel(status.Kind)}{status.RemainingTurns}"));
                string percent = (portrait.ReadinessBp / 100f).ToString("0.#");
                string unitName = label(portrait.Unit);
                if (!sprites.ContainsKey(portrait.Unit.DefinitionId + "_s"))
                    unitName += portrait.Unit.Slot.Position.ToString();
                portrait.Detail.text = $"{unitName}    {percent}%    {cool}    {statuses}";
                portrait.Detail.color = acting ? new Color(1f, 0.82f, 0.48f) : Color.white;
                portrait.Detail.rectTransform.anchoredPosition = new Vector2(20f, 328f - index * 39f);
            }
        }

        private static string StatusLabel(BattleStatusKind status) => status switch
        {
            BattleStatusKind.AttackUp => "攻↑",
            BattleStatusKind.DefenseUp => "防↑",
            BattleStatusKind.Immunity => "免疫",
            BattleStatusKind.Barrier => "护盾",
            BattleStatusKind.Stun => "眩晕",
            _ => status.ToString()
        };

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
            foreach (var image in staticPortraits)
                if (image) image.enabled = true;
            foreach (var cooldown in cooldowns)
                Remove(cooldown.Shade);
            Remove(liveRail);
            Remove(feedbackLayer);
        }

        private static void Remove(Component component)
        {
            if (!component) return;
            component.gameObject.SetActive(false);
            Object.Destroy(component.gameObject);
        }

        private sealed class Portrait
        {
            internal Portrait(RectTransform root, Image highlight, GameText detail)
            {
                Root = root;
                Highlight = highlight;
                Detail = detail;
            }

            internal RectTransform Root { get; }
            internal Image Highlight { get; }
            internal GameText Detail { get; }
            internal BattleUnitSnapshot Unit { get; set; }
            internal Vector2 Position { get; set; }
            internal int ReadinessBp { get; set; }
            internal bool Visible { get; set; }
        }

        private sealed class Cooldown
        {
            private readonly Image icon;
            private readonly GameText number;
            private int previousTurns;

            internal Cooldown(GameButton button, GameText source)
            {
                icon = (Image)button.targetGraphic;
                Shade = Layer(button.transform, "Cooldown Overlay").gameObject.AddComponent<Image>();
                Shade.color = new Color(0f, 0f, 0f, 0.56f);
                Shade.raycastTarget = false;
                number = Text(Shade.transform, "Cooldown Number", "", Vector2.zero, icon.rectTransform.sizeDelta,
                    24f, source);
                number.rectTransform.anchorMin = number.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                number.rectTransform.anchoredPosition = Vector2.zero;
                Shade.gameObject.SetActive(false);
            }

            internal Image Shade { get; }

            internal void Show(int turns)
            {
                if (turns > 0)
                {
                    Shade.sprite = icon.overrideSprite;
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
