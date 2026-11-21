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
        private readonly BattleNativeFeedbackEffect.Library feedbackLibrary;
        private const float SoulBurnIntroPixelScale = .69f;
        private const float SoulBurnLoopPixelScale = .50f;
        private readonly BattleNativeFeedbackEffect[] soulBurnEffects = new BattleNativeFeedbackEffect[2];
        private readonly AudioSource soulBurnAudio;
        private readonly RectTransform[] soulBurnAnchors = new RectTransform[2];
        private readonly TeamBar allyHealth, enemyHealth;
        private readonly GameText turn;
        private readonly RectTransform clockRoot;
        private readonly Image clockProgress;
        private const float ScoreScale = .55f * 1.5f;
        private const float ScoreWidthLimit = 103f;
        private readonly Image[] digits = new Image[3];
        private readonly Sprite[] scoreDigits;
        private readonly ScoreGlyph[] scoreGlyphs;
        private readonly Image[] localPing;
        private readonly GameObject localPingRoot;
        private readonly Material playerNameShadow;
        private int shownSeconds = -1;
        private readonly Sprite clockBlue, clockRed;
        private readonly BattleOnlineTransitionAssets.Copy copy;
        private readonly List<(GameObject Item, bool Active)> hidden = new List<(GameObject, bool)>();
        private readonly RectTransform speed;
        private readonly Vector2 speedMin, speedMax, speedPosition;

        [Serializable]
        private sealed class ScoreLayout
        {
            public ScoreGlyph[] scoreGlyphs;
        }

        [Serializable]
        private struct ScoreGlyph
        {
            public int xOffset, yOffset, advance;
            public int left, top, right, bottom;
        }

        internal BattleOnlineHud(BindingMap map, BattleOnlinePlayers players, BattleOnlineTransitionAssets.Copy copy,
            IReadOnlyDictionary<string, Sprite> roster, BattleOnlineTransitionAssets assets, BattleSkillPresentationAsset[] feedbackAssets = null)
        {
            this.copy = copy ?? throw new ArgumentNullException(nameof(copy));
            feedbackLibrary = new BattleNativeFeedbackEffect.Library(feedbackAssets);
            var art = assets.hudSprites.ToDictionary(item => item.name, StringComparer.Ordinal);
            foreach (string name in new[] { "_battle_pvp_icon_vs", "game_hud_bar_pvplive", "game_hud_bar_hp",
                "game_hud_bar_hp_red", "game_hud_bar_split", "_hero_s_frame_enemy", "_hero_s_bg_ally",
                "time_progress_bg", "time_progress_bar_blue", "time_progress_bar_red" })
                if (!art.ContainsKey(name)) throw new InvalidOperationException("Online battle HUD art is missing: " + name);
            scoreDigits = Enumerable.Range(0, 10).Select(i => art["score_" + i]).ToArray();
            var source = assets.hudScoreLayout;
            scoreGlyphs = source ? JsonUtility.FromJson<ScoreLayout>(source.text)?.scoreGlyphs : null;
            if (scoreGlyphs?.Length != scoreDigits.Length || scoreGlyphs.Any(glyph =>
                glyph.advance <= 0 || glyph.right <= glyph.left || glyph.bottom <= glyph.top))
                throw new InvalidOperationException("Online HUD score glyph metrics are missing or invalid.");
            var font = map.Require<GameText>("wave");
            // Sibling of the ordinary HUD: team identities remain visible during the entry HUD fade.
            root = Rect(map.transform.parent, "Online PvP HUD", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            root.SetSiblingIndex(map.transform.GetSiblingIndex() + 1);
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            if (assets.soulBurnRelease)
            {
                soulBurnAudio = root.gameObject.AddComponent<AudioSource>();
                soulBurnAudio.playOnAwake = false;
                soulBurnAudio.spatialBlend = 0f;
                soulBurnAudio.clip = assets.soulBurnRelease;
                soulBurnAudio.clip.LoadAudioData();
            }
            Hide(map.Require<GameText>("wave").gameObject);
            Hide(map.Require<GameText>("soulCount").gameObject);
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
            for (int i = 0; i < digits.Length; i++)
                digits[i] = Picture(clockRoot, "Countdown digit " + (i + 1), scoreDigits[0], Vector2.one * .5f,
                    Vector2.one * .5f, Vector2.zero, Vector2.zero);
            var pingAnchor = new Vector2(.5f, 1f);
            var ping = Rect(root, "Local Connection Quality", pingAnchor, pingAnchor, new Vector2(-507f, -27f), Vector2.zero);
            localPingRoot = ping.gameObject;
            Picture(ping, "Connection background", art["cm_ping_bg"], Vector2.one * .5f, Vector2.one * .5f,
                Vector2.zero, new Vector2(51, 45));
            localPing = Enumerable.Range(1, 3).Select(i => Picture(ping, "Quality " + i, art["cm_ping_" + i],
                Vector2.one * .5f, Vector2.one * .5f, Vector2.zero, new Vector2(43.5f, 39))).ToArray();
            localPingRoot.SetActive(false);
            clockRoot.gameObject.SetActive(false);

            TeamBar Team(bool ally, string name, string avatarId, Sprite avatar)
            {
                string side = ally ? "Local" : "Opponent";
                var start = new Vector2(ally ? .215f : .545f, 1f);
                var end = new Vector2(ally ? .455f : .785f, 1f);
                var background = Picture(root, side + " Health Background", art["game_hud_bar_hp_red"], start, end,
                    new Vector2(0, -73.5f), new Vector2(0, 31.5f));
                background.color = new Color(.15f, .09f, .06f, .9f);
                Image Fill(string label, Sprite sprite)
                {
                    var image = Picture(root, side + label, sprite, start, end,
                        new Vector2(0, -73.5f), new Vector2(0, 31.5f));
                    image.type = Image.Type.Filled;
                    image.fillMethod = Image.FillMethod.Horizontal;
                    image.fillOrigin = ally ? 1 : 0;
                    return image;
                }
                var damage = Fill(" Team Damage", art["game_hud_bar_hp_red"]);
                var health = Fill(" Team Health", art["game_hud_bar_hp"]);
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
                soulBurnAnchors[ally ? 0 : 1] = Rect(root, side + " Soul Burn Anchor", faceAnchor, faceAnchor,
                    new Vector2(0, -72f), Vector2.zero);
                return new TeamBar(health, damage);
            }
        }

        // Called only by a first-consumed authoritative primary Action.UseSoulBurn event.
        internal void PlaySoulBurn(BattleSide side)
        {
            const string source = "ui_livepvp_soulburn_eff";
            if (!root) return;
            if (soulBurnAudio) soulBurnAudio.PlayOneShot(soulBurnAudio.clip);
            if (!feedbackLibrary.Contains(source)) return;
            int index = side == BattleSide.Ally ? 0 : 1;
            soulBurnAnchors[index].localScale = Vector3.one;
            if (soulBurnEffects[index] == null)
            {
                var canvas = root.GetComponentInParent<Canvas>();
                soulBurnEffects[index] = new BattleNativeFeedbackEffect(feedbackLibrary, source,
                    soulBurnAnchors[index], SoulBurnIntroPixelScale, canvas.sortingLayerID, canvas.sortingOrder + 2);
            }
            soulBurnEffects[index].PlayUntilFinished();
        }

        internal void Tick(float delta)
        {
            allyHealth.Tick(delta);
            enemyHealth.Tick(delta);
            for (int index = 0; index < soulBurnEffects.Length; index++)
            {
                var effect = soulBurnEffects[index];
                if (effect?.Active != true) continue;
                effect.Tick(delta);
                // P2 13:41 shows the opening rays crossing the field. The sustained fire
                // at 13:44.5 spans about 260px around the 130px portrait.
                float progress = Mathf.InverseLerp(.65f, 1f, effect.Elapsed);
                float scale = Mathf.SmoothStep(1f, SoulBurnLoopPixelScale / SoulBurnIntroPixelScale, progress);
                soulBurnAnchors[index].localScale = Vector3.one * scale;
            }
        }

        internal void FinishSoulBurn()
        {
            foreach (var effect in soulBurnEffects) effect?.Finish();
        }

        internal void ClearFeedback()
        {
            foreach (var effect in soulBurnEffects) effect?.Stop();
            if (soulBurnAudio) soulBurnAudio.Stop();
        }

        internal void SetHealth(long allyCurrent, long allyMaximum, long enemyCurrent, long enemyMaximum)
        {
            allyHealth.Set(Ratio(allyCurrent, allyMaximum), allyMaximum);
            enemyHealth.Set(Ratio(enemyCurrent, enemyMaximum), enemyMaximum);
        }

        private sealed class TeamBar
        {
            private readonly Image health, damage;
            private float before, time;
            private long maximum = -1;

            internal TeamBar(Image health, Image damage)
            {
                this.health = health;
                this.damage = damage;
            }

            internal void Set(float value, long capacity)
            {
                if (capacity != maximum || value > health.fillAmount)
                    damage.fillAmount = value;
                else if (value < health.fillAmount)
                {
                    before = damage.fillAmount;
                    time = 0f;
                }
                maximum = capacity;
                health.fillAmount = value;
            }

            internal void Tick(float delta)
            {
                if (damage.fillAmount <= health.fillAmount || delta <= 0f) return;
                time += delta;
                // P2 08:10.83–08:12.13. The red end initially holds, then accelerates.
                float progress = Mathf.Clamp01((time - .24f) / 1.06f);
                damage.fillAmount = Mathf.Lerp(before, health.fillAmount, progress * progress);
            }
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
            int divisor = seconds >= 100 ? 100 : seconds >= 10 ? 10 : 1;
            int pen = 0;
            int left = int.MaxValue, right = int.MinValue;
            int top = int.MaxValue, bottom = int.MinValue;
            foreach (var image in digits)
            {
                image.enabled = divisor > 0;
                if (!image.enabled) continue;

                int digit = seconds / divisor % 10;
                var metric = scoreGlyphs[digit];
                var sprite = scoreDigits[digit];
                image.sprite = sprite;
                int x = pen + metric.xOffset;
                int y = metric.yOffset;
                var rect = image.rectTransform;
                rect.sizeDelta = sprite.rect.size;
                rect.anchoredPosition = new Vector2(x, -y) + Vector2.Scale(rect.sizeDelta, new Vector2(.5f, -.5f));
                left = Math.Min(left, x + metric.left);
                right = Math.Max(right, x + metric.right);
                top = Math.Min(top, y + metric.top);
                bottom = Math.Max(bottom, y + metric.bottom);
                pen += metric.advance;
                divisor /= 10;
            }

            // Center the visible pixel union. The crop rectangles include transparent space,
            // and the source advance sets spacing rather than the glyph's visible center.
            var center = new Vector2((left + right) * .5f, -(top + bottom) * .5f);
            float scale = Mathf.Min(ScoreScale, ScoreWidthLimit / (right - left));
            foreach (var image in digits)
            {
                if (!image.enabled) continue;
                var rect = image.rectTransform;
                rect.anchoredPosition = (rect.anchoredPosition - center) * scale;
                rect.sizeDelta *= scale;
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
            foreach (var effect in soulBurnEffects) effect?.Dispose();
            foreach (var (item, active) in hidden) if (item) item.SetActive(active);
            hidden.Clear();
            if (speed) { speed.anchorMin = speedMin; speed.anchorMax = speedMax; speed.anchoredPosition = speedPosition; }
            if (root) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
            if (playerNameShadow) UnityEngine.Object.Destroy(playerNameShadow);
        }
    }
}
