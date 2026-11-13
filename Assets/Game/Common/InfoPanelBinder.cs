using System;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Common
{
    // A scene or screen owns one binder and reuses both cards.
    public sealed class InfoPanelBinder : IDisposable
    {
        private readonly RectTransform container;
        private readonly Canvas canvas;
        private readonly InfoPanel main, auxiliary;
        private readonly GameButton close;
        private readonly Vector3[] corners = new Vector3[4];
        private RectTransform anchor, layoutAnchor;
        private Card currentCard;
        private Card? currentSecondary;
        private Rect lastLimits;
        private bool opensRight;
        public bool IsOpen => container && container.gameObject.activeSelf;

        public readonly struct Card
        {
            public Card(Sprite icon, string title, string description, Color? effectTint = null, Color? effectText = null,
                string soul = null, string cooldown = null, string burnTitle = null, string burnCost = null,
                string burnDescription = null, string upgrades = null)
            {
                Icon = icon;
                Title = title;
                Description = description;
                EffectTint = effectTint;
                EffectText = effectText;
                Soul = soul;
                Cooldown = cooldown;
                BurnTitle = burnTitle;
                BurnCost = burnCost;
                BurnDescription = burnDescription;
                Upgrades = upgrades;
            }

            public Sprite Icon { get; }
            public string Title { get; }
            public string Description { get; }
            public Color? EffectTint { get; }
            public Color? EffectText { get; }
            public string Soul { get; }
            public string Cooldown { get; }
            public string BurnTitle { get; }
            public string BurnCost { get; }
            public string BurnDescription { get; }
            public string Upgrades { get; }
        }

        public InfoPanelBinder(GameObject prefab, RectTransform parent)
        {
            UIViewRegistry.ValidatePrefab<InfoPanel>(prefab);
            canvas = parent.GetComponentInParent<Canvas>();
            container = (RectTransform)new GameObject("Battle Inspection", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster)).transform;
            container.SetParent(parent, false);
            Stretch(container);
            var overlay = container.GetComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingLayerID = canvas.sortingLayerID;
            overlay.sortingOrder = canvas.sortingOrder + 20;
            container.gameObject.SetActive(false);
            var surface = new GameObject("Dismiss Inspection", typeof(RectTransform), typeof(Image), typeof(GameButton));
            var surfaceRect = (RectTransform)surface.transform;
            surfaceRect.SetParent(container, false);
            Stretch(surfaceRect);
            var image = surface.GetComponent<Image>();
            image.color = Color.clear;
            image.canvasRenderer.cullTransparentMesh = false;
            close = surface.GetComponent<GameButton>();
            close.targetGraphic = image;
            close.transition = Selectable.Transition.None;
            close.onClick.AddListener(Hide);
            main = Create(prefab);
            auxiliary = Create(prefab);
        }

        private InfoPanel Create(GameObject prefab)
        {
            var view = new InfoPanel();
            view.Attach(Object.Instantiate(prefab, container, false));
            view.Hide();
            return view;
        }

        public void Show(RectTransform source, Card content, Card? secondary = null, bool preferRight = false,
            RectTransform positionSource = null)
        {
            if (!source) return;
            anchor = source;
            layoutAnchor = positionSource ? positionSource : source;
            currentCard = content;
            currentSecondary = secondary;
            opensRight = preferRight;
            container.SetAsLastSibling();
            container.gameObject.SetActive(true);
            Layout(SafeRect());
        }

        private void Layout(Rect limits)
        {
            lastLimits = limits;
            Card content = currentCard;
            Card? secondary = currentSecondary;
            float width = content.EffectTint.HasValue ? 385f : 560f;
            SizeCard(main, content, Mathf.Min(width, limits.width), limits.height * .76f);
            bool paired = secondary.HasValue;
            if (paired) SizeCard(auxiliary, secondary.Value, Mathf.Min(385f, limits.width), limits.height * .38f);
            else auxiliary.Hide();

            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            layoutAnchor.GetWorldCorners(corners);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(container,
                RectTransformUtility.WorldToScreenPoint(camera, corners[1]), camera, out var upperLeft);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(container,
                RectTransformUtility.WorldToScreenPoint(camera, corners[2]), camera, out var upperRight);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(container,
                RectTransformUtility.WorldToScreenPoint(camera, corners[0]), camera, out var lowerLeft);

            const float gap = 6f;
            Vector2 mainSize = main.Frame.sizeDelta;
            Vector2 extraSize = paired ? auxiliary.Frame.sizeDelta : Vector2.zero;
            bool sideBySide = paired && mainSize.x + gap + extraSize.x <= limits.width;
            bool relatedOnLeft = !opensRight && sideBySide;
            float totalWidth = sideBySide ? mainSize.x + gap + extraSize.x : mainSize.x;
            float x = opensRight ? upperRight.x + gap : upperLeft.x - mainSize.x - gap;
            if (relatedOnLeft) x -= extraSize.x + gap;
            if (opensRight && x + totalWidth > limits.xMax) x = upperLeft.x - gap - totalWidth;
            x = Mathf.Clamp(x, limits.xMin, limits.xMax - totalWidth);
            float totalHeight = paired && !sideBySide ? mainSize.y + gap + extraSize.y : Mathf.Max(mainSize.y, extraSize.y);
            if (paired && !sideBySide && totalHeight > limits.height)
            {
                SizeCard(main, content, mainSize.x, Mathf.Max(110f, limits.height - extraSize.y - gap));
                mainSize = main.Frame.sizeDelta;
                totalHeight = mainSize.y + extraSize.y + gap;
            }
            float y = content.EffectTint.HasValue ? upperRight.y - 34f : lowerLeft.y;
            y = Mathf.Clamp(y, limits.yMin, Mathf.Max(limits.yMin, limits.yMax - totalHeight));
            main.Frame.anchoredPosition = new Vector2(relatedOnLeft ? x + extraSize.x + gap : x, y);
            if (paired)
                auxiliary.Frame.anchoredPosition = sideBySide
                    ? new Vector2(relatedOnLeft ? x : x + mainSize.x + gap, y + Mathf.Max(0f, mainSize.y - extraSize.y))
                    : new Vector2(Mathf.Clamp(x, limits.xMin, limits.xMax - extraSize.x), y + mainSize.y + gap);
        }

        private static void SizeCard(InfoPanel view, Card card, float width, float maximumHeight)
        {
            view.Show();
            bool effect = card.EffectTint.HasValue;
            view.Background.enabled = !effect;
            view.EffectBackground.enabled = effect;
            view.EffectBackground.color = card.EffectTint.GetValueOrDefault(Color.white);
            view.Icon.sprite = card.Icon;
            view.Icon.enabled = card.Icon;

            float inset = effect ? 22f : 42f;
            float iconSize = effect ? 42f : 104f;
            float titleLeft = card.Icon ? inset + iconSize + 12f : inset;
            Top(view.Icon.rectTransform, inset, effect ? 15f : 26f, iconSize, iconSize);
            view.Title.text = card.Title;
            view.Title.fontSize = effect ? 24f : 27.5f;
            Top(view.Title.rectTransform, titleLeft, effect ? 20f : 29f, width - titleLeft - inset, 39f);

            bool hasSoul = !string.IsNullOrEmpty(card.Soul);
            bool hasCooldown = !string.IsNullOrEmpty(card.Cooldown);
            view.SoulIcon.enabled = hasSoul;
            view.CooldownIcon.enabled = hasCooldown;
            view.Metadata.text = card.Soul;
            view.Cooldown.text = card.Cooldown;
            view.Metadata.gameObject.SetActive(hasSoul);
            view.Cooldown.gameObject.SetActive(hasCooldown);
            float bodyWidth = Mathf.Max(1f, width - inset * 2f);
            float metaAvailable = Mathf.Max(1f, width - titleLeft - inset);
            float soulWidth = hasSoul ? Mathf.Min(view.Metadata.GetPreferredValues().x + 2f, Mathf.Max(1f, metaAvailable * .53f - 18f)) : 0f;
            Top(view.SoulIcon.rectTransform, titleLeft, 84f, 19f, 27f);
            Top(view.Metadata.rectTransform, titleLeft + 23f, 84f, soulWidth, 35f);
            float cooldownLeft = titleLeft + (hasSoul ? 34f + soulWidth : 0f);
            float cooldownWidth = hasCooldown
                ? Mathf.Clamp(view.Cooldown.GetPreferredValues().x + 3f, 1f, Mathf.Max(1f, width - inset - cooldownLeft - 29f))
                : 0f;
            Top(view.Cooldown.rectTransform, cooldownLeft, 84f, cooldownWidth, 35f);
            Top(view.CooldownIcon.rectTransform, cooldownLeft + cooldownWidth + 3f, 83f, 26f, 28f);

            float heading = effect ? (card.Icon ? 64f : 60f) : 144f;
            view.Description.color = card.EffectText ?? new Color32(136, 136, 136, 255);
            float bottom = TextSection(view.Description, card.Description, 0f, bodyWidth);
            bool hasBurn = !string.IsNullOrEmpty(card.BurnDescription);
            view.BurnDivider.enabled = hasBurn;
            view.BurnIcon.enabled = hasBurn;
            view.BurnTitle.gameObject.SetActive(hasBurn);
            view.BurnCost.gameObject.SetActive(hasBurn);
            view.BurnDescription.gameObject.SetActive(hasBurn);
            if (hasBurn)
            {
                bottom += 18f;
                Top(view.BurnDivider.rectTransform, 0f, bottom, bodyWidth, 10f);
                bottom += 20f;
                view.BurnTitle.text = card.BurnTitle;
                view.BurnCost.text = card.BurnCost;
                Top(view.BurnTitle.rectTransform, 0f, bottom, bodyWidth * .5f, 32f);
                Top(view.BurnCost.rectTransform, bodyWidth * .5f, bottom, bodyWidth * .5f - 26f, 32f);
                Top(view.BurnIcon.rectTransform, bodyWidth - 21f, bottom - 2f, 21f, 29f);
                bottom += 36f;
                bottom += TextSection(view.BurnDescription, card.BurnDescription, bottom, bodyWidth);
            }
            bool hasUpgrades = !string.IsNullOrEmpty(card.Upgrades);
            view.UpgradeDivider.enabled = hasUpgrades;
            view.Upgrades.gameObject.SetActive(hasUpgrades);
            if (hasUpgrades)
            {
                bottom += 13f;
                Top(view.UpgradeDivider.rectTransform, 0f, bottom, bodyWidth, 10f);
                bottom += 18f;
                bottom += TextSection(view.Upgrades, card.Upgrades, bottom, bodyWidth);
            }

            float padding = effect ? 18f : 23f;
            float height = Mathf.Min(maximumHeight, heading + bottom + padding);
            view.Frame.sizeDelta = new Vector2(width, height);
            var scroll = (RectTransform)view.Scroll.transform;
            scroll.offsetMin = new Vector2(inset, padding);
            scroll.offsetMax = new Vector2(-inset, -heading);
            view.Scroll.content.sizeDelta = new Vector2(0f, Mathf.Max(bottom, height - heading - padding));
            view.Scroll.content.anchoredPosition = Vector2.zero;
            view.Scroll.StopMovement();
        }

        private static float TextSection(GameText label, string text, float top, float width)
        {
            label.text = text;
            float height = Mathf.Max(label.fontSize, label.GetPreferredValues(text, width, 0f).y + 4f);
            Top(label.rectTransform, 0f, top, width, height);
            return height;
        }

        private static void Top(RectTransform rect, float x, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = new Vector2(x, -top);
            rect.sizeDelta = new Vector2(Mathf.Max(1f, width), height);
        }

        private Rect SafeRect()
        {
            Rect available = container.rect;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Rect safe = camera && camera.targetTexture ? camera.pixelRect : Screen.safeArea;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(container, safe.min, camera, out var lo) &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(container, safe.max, camera, out var hi))
            {
                available = Rect.MinMaxRect(Mathf.Max(available.xMin, lo.x), Mathf.Max(available.yMin, lo.y),
                    Mathf.Min(available.xMax, hi.x), Mathf.Min(available.yMax, hi.y));
            }
            return Rect.MinMaxRect(available.xMin + 16f, available.yMin + 12f, available.xMax - 16f, available.yMax - 12f);
        }

        public void Tick()
        {
            if (!IsOpen) return;
            if (!anchor || !anchor.gameObject.activeInHierarchy || !IsAnchorVisible())
            {
                Hide();
                return;
            }
            Rect limits = SafeRect();
            if (lastLimits != limits) Layout(limits);
        }

        private bool IsAnchorVisible() => !anchor.TryGetComponent<Graphic>(out var graphic) || graphic.isActiveAndEnabled;

        public void Hide()
        {
            anchor = layoutAnchor = null;
            currentCard = default;
            currentSecondary = null;
            if (container) container.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            Hide();
            if (!container) return;
            close.onClick.RemoveListener(Hide);
            container.SetParent(null, false);
            if (Application.isPlaying) Object.Destroy(container.gameObject);
            else Object.DestroyImmediate(container.gameObject);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
