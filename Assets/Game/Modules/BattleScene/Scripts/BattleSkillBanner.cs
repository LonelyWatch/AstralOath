using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleSkillBanner
    {
        private const float EntranceSeconds = .13f;
        private const float VisibleSeconds = 1.8f;
        private const float ExitSeconds = .18f;
        private static readonly Vector2 Home = new Vector2(12f, -51f);
        private readonly RectTransform root;
        private readonly CanvasGroup visibility;
        private readonly Image icon;
        private readonly GameText casterName;
        private readonly GameText skillName;
        private readonly GameObject waveLabel, wavePlate;
        private bool restoreWaveLabel, restoreWavePlate;
        private float elapsed;

        internal BattleSkillBanner(RectTransform topBar, GameText style, IReadOnlyDictionary<string, Sprite> sprites)
        {
            waveLabel = topBar.Find("Wave")?.gameObject;
            wavePlate = topBar.Find("Wave Plate")?.gameObject;
            root = Rect(topBar, "Skill Use Banner", Home, new Vector2(360f, 80f));
            visibility = root.gameObject.AddComponent<CanvasGroup>();
            visibility.blocksRaycasts = false;
            visibility.interactable = false;

            var plate = Art(root, "Black Gold Plate", sprites.GetValueOrDefault("_box_black_gold"),
                new Vector2(-65f, 35f), new Vector2(432f, 150f));
            if (plate.sprite && plate.sprite.border.sqrMagnitude > 0f) plate.type = Image.Type.Sliced;
            var line = sprites.GetValueOrDefault("_bar_1");
            var warm = new Color32(203, 168, 106, 160);
            Art(root, "Top Rule", line, new Vector2(74f, 4f), new Vector2(222f, 15f)).color = warm;
            Art(root, "Bottom Rule", line, new Vector2(74f, -74f), new Vector2(222f, 15f)).color = warm;

            icon = Art(root, "Skill Icon", null, new Vector2(0f, -1f), new Vector2(79f, 79f));
            icon.preserveAspect = true;
            casterName = Label(root, style, "Caster Name", new Vector2(96f, -9f), 22f, new Color32(215, 183, 124, 255));
            skillName = Label(root, style, "Skill Name", new Vector2(96f, -40f), 25f, Color.white);
            Hide();
        }

        internal void Show(string caster, string skill, Sprite image)
        {
            if (!root.gameObject.activeSelf)
            {
                restoreWaveLabel = HideIfVisible(waveLabel);
                restoreWavePlate = HideIfVisible(wavePlate);
            }
            casterName.text = caster;
            skillName.text = skill;
            icon.sprite = image;
            icon.enabled = image;
            elapsed = 0f;
            visibility.alpha = 1f;
            root.anchoredPosition = Home + Vector2.left * 22f;
            root.gameObject.SetActive(true);
        }

        internal void Tick(float delta, bool cutin)
        {
            if (!root.gameObject.activeSelf) return;
            elapsed += delta;
            float exit = Mathf.Clamp01((elapsed - VisibleSeconds) / ExitSeconds);
            if (exit >= 1f)
            {
                Hide();
                return;
            }
            float entry = Mathf.Clamp01(elapsed / EntranceSeconds);
            root.anchoredPosition = Home + Vector2.left * (22f * (1f - entry) + 32f * exit);
            visibility.alpha = cutin ? 0f : 1f - exit;
        }

        internal void Hide()
        {
            root.gameObject.SetActive(false);
            if (restoreWaveLabel && waveLabel) waveLabel.SetActive(true);
            if (restoreWavePlate && wavePlate) wavePlate.SetActive(true);
            restoreWaveLabel = restoreWavePlate = false;
        }

        private static bool HideIfVisible(GameObject item)
        {
            if (!item || !item.activeSelf) return false;
            item.SetActive(false);
            return true;
        }

        internal void Dispose()
        {
            if (!root) return;
            Hide();
            root.SetParent(null, false);
            Object.Destroy(root.gameObject);
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image Art(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, name, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.enabled = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static GameText Label(Transform parent, GameText style, string name, Vector2 position, float size, Color color)
        {
            var text = Rect(parent, name, position, new Vector2(246f, 32f)).gameObject.AddComponent<GameText>();
            text.font = style.font;
            text.fontSharedMaterial = style.fontSharedMaterial;
            text.fontSize = size;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }
    }
}
