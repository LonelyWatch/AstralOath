using System;
using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    // Children of the unit marker share its projected position, scale and visibility.
    internal sealed class BattleEffectWords : IDisposable
    {
        internal const string LayerName = "Effect Words";
        private const float Duration = 1.10f;
        private const float FadeIn = .08f;
        private const float MaximumFirstStep = .10f;
        private const float MaximumHiddenSeconds = 4f;
        private const float FadeOut = .24f;
        private const float Spacing = 42f;
        private const float CaptionSize = 36f;
        private const float IconSize = 35f;
        private const float IconGap = 8f;
        private const float Baseline = -10f;
        private const float Rise = 28f;
        private const int Capacity = 8;
        private static readonly Vector2 MarkerOffset = new Vector2(12f, -63f);
        private static readonly Color Gold = new Color32(214, 191, 123, 255);
        private static readonly Color Violet = new Color32(220, 174, 215, 255);
        private readonly RectTransform layer;
        private readonly TMP_FontAsset font;
        private readonly Material outlinePreset;
        private readonly bool ownsLayer;
        private readonly List<Word> active = new List<Word>(Capacity);
        private readonly Stack<Word> available = new Stack<Word>(Capacity);
        private Material fallbackOutline;
        private float hiddenSeconds;

        public BattleEffectWords(RectTransform marker, TMP_FontAsset preferredFont, Material preferredOutline)
        {
            font = preferredFont ? preferredFont : TMP_Settings.defaultFontAsset;
            outlinePreset = font && preferredOutline && preferredOutline.mainTexture == font.atlasTexture ? preferredOutline : null;
            layer = marker.Find(LayerName) as RectTransform;
            if (!layer)
            {
                // Older authored actors can still use their existing projected marker.
                layer = MakeRect(LayerName, marker);
                layer.anchoredPosition = MarkerOffset;
                ownsLayer = true;
            }
        }

        public void Show(string text, Sprite icon, bool passive)
        {
            if (!layer || string.IsNullOrWhiteSpace(text)) return;
            if (active.Count == Capacity) Retire(0);
            var word = available.Count > 0 ? available.Pop() : CreateWord();
            word.Time = 0f;
            word.Text.text = text;
            word.Text.color = passive ? Violet : Gold;
            word.Icon.sprite = icon;
            word.Icon.enabled = icon;
            float width = word.Text.GetPreferredValues(text).x;
            word.Text.rectTransform.sizeDelta = new Vector2(width + 2f, Spacing);
            // The caption stays centered on the actor whether or not an icon precedes it.
            word.Icon.rectTransform.anchoredPosition = new Vector2(-(width + IconSize) * .5f - IconGap, 0f);
            word.Root.SetAsLastSibling();
            word.Root.gameObject.SetActive(true);
            word.Root.anchoredPosition = Vector2.up * Baseline;
            word.Opacity.alpha = 0f;
            active.Add(word);
        }

        public void Tick(float delta)
        {
            // A skill can mask its unit through the entire impact. Keep the queued words
            // at their current progress until the projected actor marker is visible again.
            if (delta <= 0f || !layer || active.Count == 0) return;
            if (!layer.gameObject.activeInHierarchy)
            {
                hiddenSeconds += delta;
                if (hiddenSeconds >= MaximumHiddenSeconds) Clear();
                return;
            }
            hiddenSeconds = 0f;
            float nextRow = Baseline;
            for (int index = active.Count - 1; index >= 0; index--)
            {
                var word = active[index];
                // A long first frame includes time spent before this event existed.
                float step = word.Time == 0f ? Mathf.Min(delta, MaximumFirstStep) : delta;
                word.Time += step;
                if (word.Time >= Duration)
                {
                    Retire(index);
                    continue;
                }

                float position = Mathf.Max(nextRow, Baseline + Rise * (word.Time / Duration));
                word.Root.anchoredPosition = Vector2.up * position;
                nextRow = position + Spacing;
                word.Opacity.alpha = Mathf.Min(1f, Mathf.Min(word.Time / FadeIn, (Duration - word.Time) / FadeOut));
            }
        }

        public void Clear()
        {
            while (active.Count != 0) Retire(active.Count - 1);
            hiddenSeconds = 0f;
        }

        public void Dispose()
        {
            Clear();
            while (available.Count > 0)
            {
                var root = available.Pop().Root;
                if (root) Destroy(root.gameObject);
            }
            if (fallbackOutline) Destroy(fallbackOutline);
            if (ownsLayer && layer) Destroy(layer.gameObject);
        }

        private void Retire(int index)
        {
            var word = active[index];
            active.RemoveAt(index);
            if (word.Root) word.Root.gameObject.SetActive(false);
            available.Push(word);
        }

        private Word CreateWord()
        {
            var root = MakeRect("Effect Word", layer);
            var opacity = root.gameObject.AddComponent<CanvasGroup>();
            opacity.interactable = opacity.blocksRaycasts = false;
            var icon = MakeRect("Icon", root).gameObject.AddComponent<Image>();
            icon.rectTransform.sizeDelta = Vector2.one * IconSize;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var text = MakeRect("Caption", root).gameObject.AddComponent<GameText>();
            if (font)
            {
                text.font = font;
                if (!outlinePreset && !fallbackOutline)
                {
                    // Keep older character profiles readable before their authored material is updated.
                    fallbackOutline = new Material(font.material) { name = "Battle Effect Word Outline (Fallback)" };
                    // A centered underlay draws the edge behind the face without narrowing thin strokes.
                    fallbackOutline.SetColor(ShaderUtilities.ID_UnderlayColor, new Color32(25, 18, 8, 255));
                    fallbackOutline.SetFloat(ShaderUtilities.ID_UnderlayDilate, .56f);
                    fallbackOutline.SetFloat(ShaderUtilities.ID_UnderlaySoftness, .035f);
                    fallbackOutline.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                }
                text.fontSharedMaterial = outlinePreset ? outlinePreset : fallbackOutline;
                text.UpdateMeshPadding();
            }
            text.fontSize = CaptionSize;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Normal;
            text.enableWordWrapping = false;
            text.richText = false;
            text.raycastTarget = false;
            return new Word { Root = root, Opacity = opacity, Text = text, Icon = icon };
        }

        private static RectTransform MakeRect(string name, Transform parent)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static void Destroy(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
        }

        private sealed class Word
        {
            internal RectTransform Root;
            internal CanvasGroup Opacity;
            internal GameText Text;
            internal Image Icon;
            internal float Time;
        }
    }
}
