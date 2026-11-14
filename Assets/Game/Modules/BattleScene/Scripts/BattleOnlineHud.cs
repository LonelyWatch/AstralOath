using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    // Native pvplive_global_hp.csb artwork, in the shared safe-area coordinate space.
    // No clock is fabricated: the live API currently has no authoritative turn deadline.
    internal sealed class BattleOnlineHud : IDisposable
    {
        private readonly RectTransform root;
        private readonly Image allyHealth, enemyHealth;
        private readonly GameText turn;
        private readonly BattleOnlineTransitionAssets.Copy copy;
        private readonly List<(GameObject Item, bool Active)> hidden = new List<(GameObject, bool)>();
        private readonly RectTransform speed;
        private readonly Vector2 speedMin, speedMax, speedPosition;

        internal BattleOnlineHud(BindingMap map, BattleOnlinePlayers players, BattleOnlineTransitionAssets.Copy copy,
            IReadOnlyDictionary<string, Sprite> roster)
        {
            this.copy = copy ?? throw new ArgumentNullException(nameof(copy));
            var art = Resources.LoadAll<Sprite>("AstralOathOnlineHud").ToDictionary(item => item.name, StringComparer.Ordinal);
            foreach (string name in new[] { "_battle_pvp_icon_vs", "game_hud_bar_pvplive", "game_hud_bar_hp",
                "game_hud_bar_hp_red", "game_hud_bar_split", "_hero_s_frame_enemy", "_hero_s_bg_ally" })
                if (!art.ContainsKey(name)) throw new InvalidOperationException("Online battle HUD art is missing: " + name);
            var font = map.Require<GameText>("activeName");
            // Sibling of the ordinary HUD: team identities remain visible during the entry HUD fade.
            root = Rect(map.transform.parent, "Online PvP HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.SetSiblingIndex(map.transform.GetSiblingIndex() + 1);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Hide(map.Require<GameText>("wave").gameObject);
            Hide(map.transform.Find("Top Bar/Wave Plate")?.gameObject);
            Hide(map.transform.Find("Top Bar/Help")?.gameObject);
            speed = map.Require<GameButton>("speed").GetComponent<RectTransform>();
            speedMin = speed.anchorMin; speedMax = speed.anchorMax; speedPosition = speed.anchoredPosition;
            speed.anchorMin = speed.anchorMax = Vector2.one;
            speed.anchoredPosition = new Vector2(-210f, -49.5f);

            allyHealth = Team(true, players?.LocalName, players?.LocalAvatarId, players?.LocalAvatar);
            enemyHealth = Team(false, players?.OpponentName, players?.OpponentAvatarId, players?.OpponentAvatar);
            var vs = Picture(root, "Versus", art["_battle_pvp_icon_vs"], new Vector2(.5f, 1f),
                new Vector2(.5f, 1f), new Vector2(0, -69f), new Vector2(171f, 171f));
            vs.preserveAspect = true;
            turn = Text(root, "Online Turn", font, new Vector2(.5f, 1f), new Vector2(0, -232.5f),
                new Vector2(420f, 54f), 31.2f, TextAlignmentOptions.Center);
            turn.fontStyle = FontStyles.Bold;
            turn.gameObject.SetActive(false);

            Image Team(bool ally, string name, string avatarId, Sprite avatar)
            {
                string side = ally ? "Local" : "Opponent";
                var start = new Vector2(ally ? .215f : .545f, 1f);
                var end = new Vector2(ally ? .455f : .785f, 1f);
                var background = Picture(root, side + " Health Background", art["game_hud_bar_hp_red"], start, end,
                    new Vector2(0, -73.5f), new Vector2(0, 31.5f));
                background.color = new Color(.15f, .09f, .06f, .9f);
                var health = Picture(root, side + " Team Health", art["game_hud_bar_hp"], start, end,
                    new Vector2(0, -73.5f), new Vector2(0, 31.5f));
                health.type = Image.Type.Filled;
                health.fillMethod = Image.FillMethod.Horizontal;
                health.fillOrigin = ally ? 0 : 1;
                var marks = Rect(root, side + " Health Dividers", start, end, new Vector2(0, -73.5f), new Vector2(0, 31.5f))
                    .gameObject.AddComponent<BattleHealthSegments>();
                marks.SourceSprite = art["game_hud_bar_split"];
                marks.UseFixedSections(20);
                marks.raycastTarget = false;
                Picture(root, side + " Team Frame", art["game_hud_bar_pvplive"], start, end,
                    new Vector2(0, -73.5f), new Vector2(30f, 52.5f));
                var label = Text(root, side + " Player Name", font, new Vector2(ally ? .455f : .545f, 1f),
                    new Vector2(0, -27f), new Vector2(430f, 78f), 30f,
                    ally ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                label.rectTransform.pivot = new Vector2(ally ? 1f : 0f, .5f);
                label.richText = false;
                // The CJK font's line metrics exceed the visible glyph height. A short
                // box makes TMP's vertical ellipsis discard even a one-line player name.
                label.margin = Vector4.zero;
                label.text = string.IsNullOrWhiteSpace(name) ? (ally ? copy.localPlayer : copy.opponentPlayer) : name;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.fontStyle = FontStyles.Bold;
                var faceAnchor = new Vector2(ally ? .18f : .82f, 1f);
                Picture(root, side + " Avatar Background", art["_hero_s_bg_ally"], faceAnchor, faceAnchor,
                    new Vector2(0, -72f), Vector2.one * 116f);
                if (!avatar && !string.IsNullOrWhiteSpace(avatarId))
                    roster.TryGetValue(avatarId.EndsWith("_s", StringComparison.Ordinal) ? avatarId : avatarId + "_s", out avatar);
                if (avatar)
                    Picture(root, side + " Player Avatar", avatar, faceAnchor, faceAnchor, new Vector2(0, -72f), Vector2.one * 116f);
                Picture(root, side + " Avatar Frame", art["_hero_s_frame_enemy"], faceAnchor, faceAnchor,
                    new Vector2(1.5f, -73.5f), Vector2.one * 132.3f);
                return health;
            }
        }

        internal void SetHealth(long allyCurrent, long allyMaximum, long enemyCurrent, long enemyMaximum)
        {
            allyHealth.fillAmount = Ratio(allyCurrent, allyMaximum);
            enemyHealth.fillAmount = Ratio(enemyCurrent, enemyMaximum);
        }

        internal void ShowTurn(BattleSide? side, bool visible)
        {
            turn.gameObject.SetActive(visible && side.HasValue);
            if (!side.HasValue) return;
            bool mine = side.Value == BattleSide.Ally;
            turn.text = mine ? copy.yourTurn : copy.opponentTurn;
            turn.color = mine ? new Color32(68, 148, 255, 255) : new Color32(254, 30, 30, 255);
        }

        private static float Ratio(long current, long maximum) => maximum > 0 ? Mathf.Clamp01((float)(current / (double)maximum)) : 0f;
        private void Hide(GameObject item)
        {
            if (!item) return;
            hidden.Add((item, item.activeSelf));
            item.SetActive(false);
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        private static Image Picture(Transform parent, string name, Sprite sprite, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, name, min, max, position, size).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        private static GameText Text(Transform parent, string name, GameText template, Vector2 anchor, Vector2 position,
            Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            var text = Rect(parent, name, anchor, anchor, position, size).gameObject.AddComponent<GameText>();
            text.font = template.font;
            text.fontSharedMaterial = template.fontSharedMaterial;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            return text;
        }

        public void Dispose()
        {
            foreach (var (item, active) in hidden) if (item) item.SetActive(active);
            hidden.Clear();
            if (speed) { speed.anchorMin = speedMin; speed.anchorMax = speedMax; speed.anchoredPosition = speedPosition; }
            if (root) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
        }
    }
}
