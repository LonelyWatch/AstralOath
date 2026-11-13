using System.Collections.Generic;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleTurnHud
    {
        private DetailRow CreateDetailRow(BattleUnitSnapshot unit)
        {
            // Coordinates are from inbattle_detail.csb, relative to n_face at the row origin.
            var root = Rect(detailRows, unit.InstanceId, Vector2.zero, Vector2.zero);
            var rowSurface = Rect(root, "Row Surface", new Vector2(0f, 1.1749f) * SourceScale,
                new Vector2(440f, 60f) * SourceScale).gameObject.AddComponent<Image>();
            rowSurface.rectTransform.pivot = new Vector2(0f, 0.5f);
            rowSurface.color = Color.clear;
            rowSurface.raycastTarget = true;
            rowSurface.canvasRenderer.cullTransparentMesh = false;
            var face = Rect(root, "Portrait", Vector2.zero, Vector2.zero);
            var highlight = CreateFace(face, unit);
            DetailIcon(root, "Hourglass", "cm_icon_etctime2", new Vector2(60f, 0f), new Vector2(16f, 22f));
            var readiness = Text(root, "Readiness", "", new Vector2(70f, 0f) * SourceScale,
                new Vector2(82f, 28f) * SourceScale, 24f * 0.79f * SourceScale, textSource, TextAlignmentOptions.Left);
            readiness.rectTransform.pivot = new Vector2(0f, 0.5f);
            var hp = Rect(root, "Health Background", new Vector2(195f, -8f) * SourceScale,
                new Vector2(100f, 6f) * SourceScale).gameObject.AddComponent<Image>();
            hp.color = new Color(0f, 0f, 0f, 0.65f);
            hp.raycastTarget = false;
            var healthFill = Layer(hp.transform, "Health Fill").gameObject.AddComponent<Image>();
            healthFill.color = unit.Slot.Side == BattleSide.Ally ? new Color32(153, 195, 42, 255) : new Color32(220, 57, 50, 255);
            healthFill.raycastTarget = false;
            var health = Text(root, "Health", "", new Vector2(195f, 10f) * SourceScale,
                new Vector2(112f, 24f) * SourceScale, 16f * SourceScale, numberSource);
            foreach (float x in new[] { 261f, 446f })
            {
                var divider = Rect(root, "Divider", new Vector2(x, 0f) * SourceScale,
                    new Vector2(1f, 28f) * SourceScale).gameObject.AddComponent<Image>();
                divider.color = new Color(1f, 1f, 1f, 0.28f);
                divider.raycastTarget = false;
            }
            var skillIcons = new Image[3];
            var shades = new Image[3];
            var cooldownNumbers = new GameText[3];
            for (int slot = 0; slot < 3; slot++)
            {
                sprites.TryGetValue("sk_" + unit.DefinitionId + "_" + (slot + 1), out var sprite);
                var skill = Rect(root, "Skill " + (slot + 1), new Vector2(302f + slot * 51f, -2f) * SourceScale,
                    new Vector2(sprite ? sprite.rect.width : 81f, sprite ? sprite.rect.height : 84f) * (0.65f * SourceScale))
                    .gameObject.AddComponent<Image>();
                skill.sprite = sprite;
                skill.raycastTarget = inspectSkill != null;
                skillIcons[slot] = skill;
                skill.gameObject.SetActive(sprite);
                shades[slot] = Layer(skill.transform, "Cooldown").gameObject.AddComponent<Image>();
                shades[slot].sprite = sprite;
                shades[slot].color = new Color(0f, 0f, 0f, 0.65f);
                shades[slot].raycastTarget = false;
                cooldownNumbers[slot] = Text(shades[slot].transform, "Turns", "", Vector2.zero,
                    skill.rectTransform.sizeDelta, 25f, textSource);
                cooldownNumbers[slot].rectTransform.anchorMin = cooldownNumbers[slot].rectTransform.anchorMax = Vector2.one * 0.5f;
                cooldownNumbers[slot].rectTransform.anchoredPosition = new Vector2(-9f, 0f);
                var hourglass = DetailIcon(shades[slot].transform, "Cooldown Hourglass", "cm_icon_etctime_b2",
                    Vector2.zero, new Vector2(18f, 20f));
                hourglass.rectTransform.anchorMin = hourglass.rectTransform.anchorMax = Vector2.one * 0.5f;
                hourglass.rectTransform.anchoredPosition = new Vector2(15f, 0f);
            }
            var states = Rect(root, "States", new Vector2(486f, 0f) * SourceScale, Vector2.zero);
            return new DetailRow(this, root, highlight, readiness, health, healthFill, skillIcons, shades, cooldownNumbers, states);
        }

        private Image DetailIcon(Transform parent, string name, string spriteName, Vector2 position, Vector2 size)
        {
            var image = Rect(parent, name, position * SourceScale, size * SourceScale).gameObject.AddComponent<Image>();
            image.sprite = spriteName == "cm_icon_etctime2" ? hourglassSprite : sprites.GetValueOrDefault(spriteName);
            image.enabled = image.sprite;
            image.raycastTarget = false;
            return image;
        }

        private static string StatusSprite(BattleStatusKind status) => status switch
        {
            BattleStatusKind.AttackUp => "stic_att_up",
            BattleStatusKind.DefenseUp => "stic_def_up",
            BattleStatusKind.Immunity => "stic_debuf_impossible",
            BattleStatusKind.Barrier => "stic_protect",
            BattleStatusKind.Stun => "stic_stun",
            _ => null
        };

        private sealed class DetailRow
        {
            private readonly BattleTurnHud owner;
            private readonly Image highlight;
            private readonly GameText readiness;
            private readonly GameText health;
            private readonly Image healthFill;
            private readonly Image[] cooldowns;
            private readonly GameText[] turns;
            private readonly RectTransform states;
            private readonly List<StatusSlot> statusSlots = new List<StatusSlot>();
            private BattleUnitSnapshot unit;

            internal DetailRow(BattleTurnHud owner, RectTransform root, Image highlight, GameText readiness,
                GameText health, Image healthFill, Image[] skillIcons, Image[] cooldowns, GameText[] turns, RectTransform states)
            {
                this.owner = owner;
                Root = root;
                this.highlight = highlight;
                this.readiness = readiness;
                this.health = health;
                this.healthFill = healthFill;
                this.cooldowns = cooldowns;
                this.turns = turns;
                this.states = states;
                if (owner.inspectSkill == null) return;
                for (int slot = 0; slot < skillIcons.Length; slot++)
                {
                    int index = slot;
                    Image icon = skillIcons[index];
                    icon.gameObject.AddComponent<GameLongPress>().Held = () =>
                    {
                        if (unit != null && index < unit.Form.SkillIds.Count)
                            owner.inspectSkill(unit, index, icon);
                    };
                }
            }

            internal RectTransform Root { get; }

            internal void Update(BattleUnitSnapshot unit, int readinessBp, bool acting)
            {
                this.unit = unit;
                highlight.enabled = acting && highlight.sprite;
                readiness.text = (readinessBp / 100f).ToString("0.#") + "%";
                health.text = unit.CurrentHealth + "/" + unit.MaximumHealth;
                healthFill.rectTransform.anchorMax = new Vector2(unit.CurrentHealth / (float)unit.MaximumHealth, 1f);
                for (int slot = 0; slot < cooldowns.Length; slot++)
                {
                    int remaining = slot < unit.Form.SkillIds.Count ? unit.GetCooldown(unit.Form.SkillIds[slot]) : 0;
                    cooldowns[slot].gameObject.SetActive(remaining > 0);
                    turns[slot].text = remaining.ToString();
                }
                for (int index = 0; index < unit.Statuses.Count; index++)
                {
                    if (index == statusSlots.Count) statusSlots.Add(CreateStatusSlot(index));
                    var status = unit.Statuses[index];
                    var slot = statusSlots[index];
                    if (slot.Status?.Kind != status.Kind) slot.Hold?.Cancel();
                    slot.Status = status;
                    string name = StatusSprite(status.Kind);
                    slot.Icon.sprite = owner.statusArt != null && owner.statusArt.TryGet(status.Kind, out var art)
                        ? art.Icon : name == null ? null : owner.sprites.GetValueOrDefault(name);
                    slot.Icon.enabled = slot.Icon.sprite;
                    slot.Icon.gameObject.SetActive(slot.Icon.sprite);
                    slot.Turns.text = status.RemainingTurns.ToString();
                }
                for (int index = unit.Statuses.Count; index < statusSlots.Count; index++)
                {
                    var slot = statusSlots[index];
                    slot.Status = null;
                    slot.Icon.gameObject.SetActive(false);
                }
            }

            private StatusSlot CreateStatusSlot(int index)
            {
                var icon = owner.DetailIcon(states, "Status " + (index + 1), "", new Vector2(index * 31f, 0f), new Vector2(30f, 30f));
                var count = Text(icon.transform, "Turns", "", new Vector2(14f, -13f), new Vector2(20f, 20f), 17f, owner.textSource);
                count.rectTransform.anchorMin = count.rectTransform.anchorMax = Vector2.one * 0.5f;
                var slot = new StatusSlot { Icon = icon, Turns = count };
                if (owner.inspectStatus != null)
                {
                    icon.raycastTarget = true;
                    slot.Hold = icon.gameObject.AddComponent<GameLongPress>();
                    slot.Hold.Held = () =>
                    {
                        if (slot.Status is BattleStatusSnapshot status)
                            owner.inspectStatus(status, icon);
                    };
                }
                return slot;
            }

            private sealed class StatusSlot
            {
                internal Image Icon;
                internal GameText Turns;
                internal GameLongPress Hold;
                internal BattleStatusSnapshot Status;
            }
        }
    }
}
