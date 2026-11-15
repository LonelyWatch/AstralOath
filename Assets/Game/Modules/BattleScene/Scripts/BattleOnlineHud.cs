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
    internal sealed class BattleOnlineHud : IDisposable
    {
        private readonly RectTransform root;
        private readonly Image allyHealth, enemyHealth;
        private readonly GameText turn;
        private readonly RectTransform clockRoot;
        private readonly Image clockProgress;
        private static readonly int[] ScoreAdvance = { 46, 32, 48, 46, 46, 46, 45, 40, 46, 44 };
        private readonly Image digitFirst, digitSecond, digitThird;
        private readonly Sprite[] scoreDigits;
        private readonly Image[] localPing;
        private readonly GameObject localPingRoot;
        private readonly Material playerNameShadow;
        private int shownSeconds = -1;
        private readonly Sprite clockBlue, clockRed;
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
                "game_hud_bar_hp_red", "game_hud_bar_split", "_hero_s_frame_enemy", "_hero_s_bg_ally",
                "time_progress_bg", "time_progress_bar_blue", "time_progress_bar_red" })
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

            var shade = Picture(root, "Native Team HUD Shade", art["_grow"], new Vector2(.5f, 1f),
                new Vector2(.5f, 1f), new Vector2(0, -45f), new Vector2(164 * 6.15f * 1.5f, 167 * 2.26f * 1.5f));
            shade.color = new Color(1, 1, 1, .6f);
            var shadowPreset = Resources.Load<Material>("AstralOathPlayerNameShadow");
            if (shadowPreset && font.fontSharedMaterial && font.fontSharedMaterial.HasProperty(ShaderUtilities.ID_UnderlayColor))
            {
                // The arena and battle labels use different font atlases. Apply the common
                // preset's values to the battle font instead of borrowing its texture or UVs.
                playerNameShadow = new Material(font.fontSharedMaterial);
                playerNameShadow.SetColor(ShaderUtilities.ID_UnderlayColor, shadowPreset.GetColor(ShaderUtilities.ID_UnderlayColor));
                foreach (var property in new[] { ShaderUtilities.ID_UnderlayOffsetX, ShaderUtilities.ID_UnderlayOffsetY,
                    ShaderUtilities.ID_UnderlayDilate, ShaderUtilities.ID_UnderlaySoftness })
                    playerNameShadow.SetFloat(property, shadowPreset.GetFloat(property));
                playerNameShadow.EnableKeyword("UNDERLAY_ON");
            }
            allyHealth = Team(true, players?.LocalName, players?.LocalAvatarId, players?.LocalAvatar);
            enemyHealth = Team(false, players?.OpponentName, players?.OpponentAvatarId, players?.OpponentAvatar);
            var vs = Picture(root, "Versus", art["_battle_pvp_icon_vs"], new Vector2(.5f, 1f),
                new Vector2(.5f, 1f), new Vector2(0, -69f), new Vector2(171f, 171f));
            vs.preserveAspect = true;
            turn = Text(root, "Online Turn", font, new Vector2(.5f, 1f), new Vector2(0, -232.5f),
                new Vector2(420f, 54f), 31.2f, TextAlignmentOptions.Center);
            if (playerNameShadow) turn.fontSharedMaterial = playerNameShadow;
            turn.fontStyle = FontStyles.Bold;
            turn.gameObject.SetActive(false);
            clockRoot = Rect(root, "Authoritative Turn Clock", new Vector2(.5f, 1f), new Vector2(.5f, 1f),
                new Vector2(0, -325.5f), Vector2.one * 141f);
            Picture(clockRoot, "Native Clock Background", art["time_progress_bg"], new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.one * 141f);
            clockBlue = art["time_progress_bar_blue"]; clockRed = art["time_progress_bar_red"];
            clockProgress = Picture(clockRoot, "Native Clock Progress", clockBlue, new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.one * 123f);
            clockProgress.type = Image.Type.Filled;
            clockProgress.fillMethod = Image.FillMethod.Radial360;
            clockProgress.fillOrigin = (int)Image.Origin360.Top;
            clockProgress.fillClockwise = false;
            scoreDigits = Enumerable.Range(0, 10).Select(i => art["score_" + i]).ToArray();
            digitFirst = Digit("Countdown tens or single");
            digitSecond = Digit("Countdown units");
            digitThird = Digit("Countdown hundreds overflow");
            Image Digit(string name) => Picture(clockRoot, name, scoreDigits[0], new Vector2(.5f, .5f),
                new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            var pingAnchor = new Vector2(.5f, 1f);
            var ping = Rect(root, "Local Connection Quality", pingAnchor, pingAnchor, new Vector2(-507f, -27f), Vector2.zero);
            localPingRoot = ping.gameObject;
            Picture(ping, "Connection background", art["cm_ping_bg"], Vector2.one * .5f, Vector2.one * .5f,
                Vector2.zero, new Vector2(51, 45));
            localPing = Enumerable.Range(1, 3).Select(i => Picture(ping, "Quality " + i, art["cm_ping_" + i],
                Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(43.5f, 39))).ToArray();
            localPingRoot.SetActive(false);
            clockRoot.gameObject.SetActive(false);

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
                label.fontStyle = FontStyles.Normal;
                if (playerNameShadow) label.fontSharedMaterial = playerNameShadow;
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

        internal void ShowTurn(BattleSide? side, bool visible, BattleTurnClockView clock = null, double elapsed = 0)
        {
            bool timed = visible && side.HasValue && clock?.DeadlineUnixMilliseconds.HasValue == true;
            turn.gameObject.SetActive(timed);
            clockRoot.gameObject.SetActive(timed);
            if (!side.HasValue) return;
            bool mine = side.Value == BattleSide.Ally;
            turn.text = mine ? copy.yourTurn : copy.opponentTurn;
            turn.color = mine ? new Color32(68, 148, 255, 255) : new Color32(254, 30, 30, 255);
            if (!timed) return;
            double remaining = clock.RemainingSeconds(elapsed);
            ShowSeconds((int)Math.Ceiling(remaining));
            clockProgress.sprite = mine ? clockBlue : clockRed;
            clockProgress.fillAmount = (float)(remaining / clock.DecisionSeconds);
        }

        internal void ShowConnection(int? latency)
        {
            localPingRoot.SetActive(latency.HasValue);
            int level = latency < 120 ? 3 : latency < 250 ? 2 : 1;
            for (int i = 0; i < localPing.Length; i++) localPing[i].enabled = i + 1 == level;
        }

        private void ShowSeconds(int seconds)
        {
            if (seconds == shownSeconds) return;
            shownSeconds = seconds;
            // The battle layout scales the same native score atlas to .55 of source size.
            const float scale = .55f * 1.5f;
            int units = seconds % 10;
            int tens = seconds / 10 % 10;
            int hundreds = seconds / 100;
            digitThird.enabled = hundreds > 0;
            digitSecond.enabled = seconds >= 10;
            int first = hundreds > 0 ? hundreds : seconds >= 10 ? tens : units;
            float advance = ScoreAdvance[first] + (seconds >= 10 ? ScoreAdvance[units] : 0) + (hundreds > 0 ? ScoreAdvance[tens] : 0);
            float x = -advance * .5f;
            Place(digitFirst, first);
            if (hundreds > 0) Place(digitThird, tens);
            if (seconds >= 10) Place(digitSecond, units);

            void Place(Image image, int digit)
            {
                image.sprite = scoreDigits[digit];
                var rect = image.rectTransform;
                rect.sizeDelta = image.sprite.rect.size * scale;
                rect.anchoredPosition = new Vector2((x + (ScoreAdvance[digit] - 2) * .5f) * scale, 0);
                x += ScoreAdvance[digit];
            }
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
            if (playerNameShadow) UnityEngine.Object.Destroy(playerNameShadow);
        }
    }
}
