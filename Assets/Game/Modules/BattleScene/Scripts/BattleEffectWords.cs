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
        private const int Capacity = 8;
        private static readonly Vector2 MarkerOffset = new Vector2(12f, -63f);
        private static readonly Color Gold = new Color32(232, 202, 121, 255);
        private static readonly Color Violet = new Color32(229, 168, 225, 255);
        private readonly RectTransform layer;
        private readonly TMP_FontAsset font;
        private readonly bool ownsLayer;
        private readonly List<Word> active = new List<Word>(Capacity);
        private readonly Stack<Word> available = new Stack<Word>(Capacity);
        private Material material;
        private float hiddenSeconds;

        public BattleEffectWords(RectTransform marker, TMP_FontAsset preferredFont)
        {
            font = preferredFont ? preferredFont : TMP_Settings.defaultFontAsset;
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
            word.Position = 0f;
            word.Time = 0f;
            word.Text.text = text;
            word.Text.color = passive ? Violet : Gold;
            word.Icon.sprite = icon;
            word.Icon.enabled = icon;
            float width = word.Text.GetPreferredValues(text).x;
            float iconWidth = icon ? 43f : 0f;
            word.Text.rectTransform.anchoredPosition = new Vector2(iconWidth * .5f, 0f);
            word.Text.rectTransform.sizeDelta = new Vector2(width + 2f, Spacing);
            word.Icon.rectTransform.anchoredPosition = new Vector2(-width * .5f - 4f, 0f);
            word.Root.SetAsLastSibling();
            word.Root.gameObject.SetActive(true);
            word.Root.anchoredPosition = Vector2.zero;
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

                float row = (active.Count - 1 - index) * Spacing;
                word.Position = Mathf.Lerp(word.Position, row, 1f - Mathf.Exp(-18f * step));
                word.Root.anchoredPosition = Vector2.up * (word.Position + 34f * (word.Time / Duration));
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
            if (material) Destroy(material);
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
            icon.rectTransform.sizeDelta = Vector2.one * 36f;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var text = MakeRect("Caption", root).gameObject.AddComponent<GameText>();
            if (font)
            {
                text.font = font;
                if (!material)
                {
                    material = new Material(font.material) { name = "Battle Effect Word Outline" };
                    material.SetColor(ShaderUtilities.ID_OutlineColor, new Color32(39, 28, 18, 255));
                    material.SetFloat(ShaderUtilities.ID_OutlineWidth, .24f);
                    material.SetFloat(ShaderUtilities.ID_OutlineSoftness, .04f);
                    material.SetFloat(ShaderUtilities.ID_FaceDilate, .06f);
                    material.EnableKeyword(ShaderUtilities.Keyword_Outline);
                }
                text.fontSharedMaterial = material;
                text.UpdateMeshPadding();
            }
            text.fontSize = 38f;
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
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
            internal float Position;
            internal float Time;
        }
    }
}
