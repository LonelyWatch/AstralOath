using System;
using System.Collections.Generic;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleStatusArt
    {
        [Serializable] private sealed class Manifest { public Mapping[] statuses; }
        [Serializable] private sealed class Mapping { public string kind, group, asset; }

        internal readonly struct Entry
        {
            public readonly Sprite Icon;
            public readonly bool IsBuff;

            public Entry(Sprite icon, bool isBuff) { Icon = icon; IsBuff = isBuff; }
        }

        private readonly Dictionary<BattleStatusKind, Entry> entries = new Dictionary<BattleStatusKind, Entry>();
        public Sprite[] Digits { get; }

        public BattleStatusArt(TextAsset manifest, Sprite[] glyphs, Sprite[] digits)
        {
            Digits = digits ?? Array.Empty<Sprite>();
            if (!manifest) return;
            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var glyph in glyphs ?? Array.Empty<Sprite>())
                if (glyph) sprites.Add(glyph.name + ".png", glyph);
            var mappings = JsonUtility.FromJson<Manifest>(manifest.text)?.statuses;
            if (mappings == null) throw new InvalidOperationException("Missing battle status icon mappings.");
            foreach (var mapping in mappings)
            {
                if (!Enum.TryParse(mapping.kind, out BattleStatusKind kind) || !sprites.TryGetValue(mapping.asset, out var icon) ||
                    (mapping.group != "Buff" && mapping.group != "Debuff" && mapping.group != "Unique"))
                    throw new InvalidOperationException("Invalid battle status icon mapping: " + mapping.kind);
                entries.Add(kind, new Entry(icon, mapping.group == "Buff"));
            }
        }

        public bool TryGet(BattleStatusKind kind, out Entry entry) => entries.TryGetValue(kind, out entry);

        public void Validate()
        {
            foreach (BattleStatusKind kind in Enum.GetValues(typeof(BattleStatusKind)))
                if (!entries.ContainsKey(kind))
                    throw new InvalidOperationException("Missing battle status icon: " + kind);
            if (Digits.Length != 10) throw new InvalidOperationException("Battle status digits require ten sprites.");
            for (int digit = 0; digit < Digits.Length; digit++)
                if (!Digits[digit]) throw new InvalidOperationException("Missing battle status digit: " + digit);
        }
    }

    internal sealed class BattleStatusIcons : IDisposable
    {
        private const float SourceFrameWidth = 86f;
        private const float SourceIconSize = 28f;
        private const float SourceIconScale = .9f;
        private static readonly Color BuffDigit = new Color32(95, 208, 255, 255);
        private static readonly Color OtherDigit = new Color32(255, 133, 113, 255);
        private readonly BattleStatusArt art;
        private readonly int columns;
        private readonly float glyphScale;
        private readonly RectTransform root;
        private readonly List<(BattleStatusKind Kind, int Turns, BattleStatusArt.Entry Art)> statuses =
            new List<(BattleStatusKind, int, BattleStatusArt.Entry)>();
        private readonly List<Slot> slots = new List<Slot>();

        public BattleStatusIcons(RectTransform frame, BattleStatusArt art, int columns)
        {
            this.art = art;
            this.columns = Mathf.Max(1, columns);
            float unit = frame.rect.width / SourceFrameWidth;
            glyphScale = unit * SourceIconScale;
            root = CreateRect("Status Icons", frame, new Vector2(1f, 15f) * unit, Vector2.zero);
            root.anchorMin = root.anchorMax = Vector2.up * .5f;
        }

        public void Apply(BattleStatusSnapshot status)
        {
            if (!art.TryGet(status.Kind, out var entry)) return;
            int current = IndexOf(status.Kind);
            var next = (status.Kind, status.RemainingTurns, entry);
            if (current >= 0) statuses[current] = next;
            else
            {
                int index = entry.IsBuff
                    ? statuses.FindIndex(item => !item.Art.IsBuff || item.Turns < status.RemainingTurns)
                    : -1;
                if (index < 0) statuses.Add(next);
                else statuses.Insert(index, next);
            }
            Draw();
        }

        public void Remove(BattleStatusKind kind)
        {
            int index = IndexOf(kind);
            if (index < 0) return;
            statuses.RemoveAt(index);
            Draw();
        }

        public void Reconcile(IReadOnlyList<BattleStatusSnapshot> snapshot)
        {
            bool changed = false;
            for (int index = statuses.Count - 1; index >= 0; index--)
            {
                var displayed = statuses[index];
                BattleStatusSnapshot current = null;
                foreach (var candidate in snapshot)
                    if (candidate.Kind == displayed.Kind) { current = candidate; break; }
                if (current == null || (current.Kind == BattleStatusKind.Barrier && current.Strength <= 0))
                {
                    statuses.RemoveAt(index);
                    changed = true;
                }
                else if (current.RemainingTurns != displayed.Turns)
                {
                    statuses[index] = (displayed.Kind, current.RemainingTurns, displayed.Art);
                    changed = true;
                }
            }
            foreach (var current in snapshot)
                if (IndexOf(current.Kind) < 0 && (current.Kind != BattleStatusKind.Barrier || current.Strength > 0))
                    Apply(current);
            if (changed) Draw();
        }

        private int IndexOf(BattleStatusKind kind) => statuses.FindIndex(item => item.Kind == kind);

        private void Draw()
        {
            for (int index = 0; index < statuses.Count; index++)
            {
                if (index == slots.Count)
                    slots.Add(new Slot(root, new Vector2(index % columns, index / columns) * (SourceIconSize * glyphScale), glyphScale));
                var item = statuses[index];
                slots[index].Show(item.Art.Icon, item.Art.IsBuff ? BuffDigit : OtherDigit, item.Turns, art.Digits);
            }
            for (int index = statuses.Count; index < slots.Count; index++) slots[index].Icon.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (!root) return;
            root.gameObject.SetActive(false);
            root.SetParent(null, false);
            UnityEngine.Object.Destroy(root.gameObject);
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var image = CreateRect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private sealed class Slot
        {
            public readonly Image Icon;
            private readonly Image firstDigit, secondDigit;

            public Slot(Transform parent, Vector2 position, float scale)
            {
                Icon = CreateImage("Status", parent, position, Vector2.one * (SourceIconSize * scale));
                Vector2 digitSize = new Vector2(12f, 16f) * scale;
                firstDigit = CreateImage("Remaining Turns", Icon.transform, new Vector2(-4f, 16f) * scale, digitSize);
                secondDigit = CreateImage("Remaining Turns (ones)", Icon.transform, new Vector2(4f, 16f) * scale, digitSize);
            }

            public void Show(Sprite icon, Color digitColor, int turns, Sprite[] digits)
            {
                Icon.sprite = icon;
                Icon.gameObject.SetActive(true);
                bool twoDigits = turns >= 10;
                SetDigit(firstDigit, digits, twoDigits ? turns / 10 : turns, digitColor);
                SetDigit(secondDigit, digits, twoDigits ? turns % 10 : -1, digitColor);
            }

            private static void SetDigit(Image image, Sprite[] digits, int value, Color color)
            {
                image.sprite = value >= 0 && value < digits.Length ? digits[value] : null;
                image.color = color;
                image.enabled = image.sprite;
            }
        }
    }
}
