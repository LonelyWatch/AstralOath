using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleSimulationPreview : IDisposable
    {
        private readonly BattleMatch match;
        private readonly BattleHudBinder binder;
        private readonly BindingMap hud;
        private readonly Camera camera;
        private readonly Dictionary<string, BattleCatalogData.SkillRow> skills;
        private readonly Dictionary<string, string> messages;
        private readonly Dictionary<string, string> classes;
        private readonly Dictionary<string, Sprite> sprites;
        private readonly BattleSkillPresentation skillPresentation;
        private readonly BattleVoice voice;
        private readonly BattleTurnHud turnHud;
        private readonly Dictionary<string, View> views = new Dictionary<string, View>(StringComparer.Ordinal);
        private readonly Queue<BattleFlowEvent> playback = new Queue<BattleFlowEvent>();
        private readonly GameText banner;
        private BattleUnitSnapshot displayedActor;
        private readonly Action restore;
        private float pause;
        private float recovery;
        private int selected;
        private int previousSelection;
        private bool useSoulBurn;
        private string presentedSkillId;
        private bool cinematicVisible;
        private readonly Dictionary<GameObject, bool> cinematicHudStates = new Dictionary<GameObject, bool>();

        public BattleSimulationPreview(Scene scene, BindingMap hud, BattleHudBinder binder, BattleCatalogData catalog,
            Sprite[] icons, TextAsset[] graphs, BattleSkillPresentationAsset[] skillAssets, TextAsset audioCatalog,
            TextAsset[] audioBanks, TMP_FontAsset damageFont, TextAsset impactBank, GameObject stageBackground)
        {
            this.hud = hud;
            this.binder = binder;
            camera = hud.GetComponentInParent<Canvas>().worldCamera;
            skills = catalog.skills.ToDictionary(skill => skill.id, StringComparer.Ordinal);
            classes = catalog.heroes.ToDictionary(hero => hero.id, hero => hero.heroClass, StringComparer.Ordinal);
            messages = catalog.messages.ToDictionary(item => item.key, item => item.zhCn, StringComparer.Ordinal);
            sprites = icons.ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
            match = BattlePreviewFactory.Create("local", 271828UL, catalog);
            var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            foreach (var state in match.Snapshot.Units)
            {
                string team = state.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies";
                var slot = units.Find(team).GetChild(state.Slot.Position - 1);
                var marker = hud.transform.Find($"Unit Status/{team}/{slot.name}").gameObject;
                views.Add(state.InstanceId, new View(slot, marker));
            }

            var text = hud.Require<GameText>("activeName");
            var hp = hud.Require<GameText>("activeHp");
            var soul = hud.Require<GameText>("soulCount");
            var wave = hud.Require<GameText>("wave");
            string oldWave = wave.text;
            wave.text = "1 / 1";
            var portrait = hud.Require<Image>("activePortrait");
            var hpFill = hud.Require<Image>("activeHpFill");
            var soulFill = hud.Require<Image>("soulFill");
            string oldName = text.text, oldHp = hp.text, oldSoul = soul.text;
            var oldPortrait = portrait.sprite;
            float oldHpFill = hpFill.fillAmount, oldSoulFill = soulFill.fillAmount;
            var skillGroup = hud.transform.Find("Skills").gameObject;
            var burnButton = hud.Require<GameButton>("soulBurn").gameObject;
            bool oldSkillVisibility = skillGroup.activeSelf, oldBurnVisibility = burnButton.activeSelf;
            var buttons = SkillButtons().Select(button => (button, image: (Image)button.targetGraphic)).ToArray();
            var oldIcons = buttons.Select(item => item.image.sprite).ToArray();
            restore = () =>
            {
                if (!hud) return;
                skillGroup.SetActive(oldSkillVisibility);
                burnButton.SetActive(oldBurnVisibility);
                text.text = oldName;
                hp.text = oldHp;
                soul.text = oldSoul;
                wave.text = oldWave;
                SetActorArt("Element", "cm_icon_promfire");
                SetActorArt("Class", "cm_icon_role_knight");
                portrait.enabled = true;
                portrait.sprite = oldPortrait;
                hpFill.fillAmount = oldHpFill;
                soulFill.fillAmount = oldSoulFill;
                for (int i = 0; i < buttons.Length; i++)
                {
                    buttons[i].button.interactable = true;
                    buttons[i].image.sprite = oldIcons[i];
                    buttons[i].image.color = Color.white;
                }
                hud.Require<GameButton>("soulBurn").interactable = true;
            };

            banner = CreateText(hud.transform, text, "Battle State", new Vector2(0.5f, 1),
                new Vector2(0, -46), new Vector2(720, 40), 24);
            skillPresentation = new BattleSkillPresentation(units, graphs, skillAssets, camera, SetCinematicVisible, stageBackground);
            voice = new BattleVoice(audioCatalog, audioBanks, impactBank);
            turnHud = new BattleTurnHud(hud, unit => Label(unit.NameKey), sprites, damageFont);
            foreach (var view in views.Values) view.AddLabels(text, hp);
            Refresh();
        }

        public BattleSnapshot Snapshot => match.Snapshot;
        public int StartedSkillVoices => voice.StartedVoiceCount;
        public int StartedSkillSounds => voice.StartedEffectCount;
        public int StartedHitSounds => voice.StartedHitCount;
        public bool IsCinematicVisible => cinematicVisible;

        private BattleUnitSnapshot Actor => Snapshot.GetUnit(Snapshot.CurrentActorId);
        private bool Busy => playback.Count != 0 || pause > 0f || recovery > 0f;
        private bool CanAct => !Busy && Actor?.Slot.Side == BattleSide.Ally;

        public void Tick(float delta)
        {
            skillPresentation.Tick(delta);
            voice.Tick();
            turnHud.Tick(delta);
            if (pause > 0f)
            {
                pause = Mathf.Max(0f, pause - delta);
                if (pause > 0f) return;
            }
            while (playback.Count > 0)
            {
                var item = playback.Peek();
                if (recovery > 0f && item.Kind == BattleFlowEventKind.Action)
                    break;
                playback.Dequeue();
                Present(item);
                if (pause > 0f) return;
            }
            if (recovery > 0f)
            {
                recovery = Mathf.Max(0f, recovery - delta);
                if (recovery > 0f) return;
                if (playback.Count == 0) Refresh();
                return;
            }
            if (Snapshot.Outcome != BattleOutcome.InProgress) return;
            if (Actor.Slot.Side == BattleSide.Enemy)
            {
                var targets = Snapshot.Units.Where(unit => unit.IsAlive && unit.Slot.Side == BattleSide.Ally);
                var target = targets.OrderBy(unit => (decimal)unit.CurrentHealth / unit.Stats.MaxHealth).First();
                Submit(Actor.Form.SkillIds[0], target.InstanceId, false);
                return;
            }
            if (Input.GetMouseButtonDown(0) && (EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject()))
            {
                Ray ray = camera.ScreenPointToRay(Input.mousePosition);
                var target = views.Where(pair => pair.Value.Contains(ray))
                    .OrderBy(pair => (camera.WorldToScreenPoint(pair.Value.Center) - Input.mousePosition).sqrMagnitude)
                    .Select(pair => pair.Key).FirstOrDefault();
                if (target != null) ChooseTarget(target);
            }
        }

        public void ChooseSkill(int index)
        {
            if (!CanAct || !TrySkill(index, out var skill) || Actor.GetCooldown(skill.id) > 0) return;
            selected = index;
            useSoulBurn = false;
            binder.SetSimulationSelection(index);
            ShowPrompt();
        }

        public void ToggleBurn()
        {
            if (!CanAct) return;
            if (useSoulBurn)
            {
                useSoulBurn = false;
                selected = previousSelection;
                binder.CancelSimulationBurn();
            }
            else
            {
                int index = BurnSlot();
                if (index < 0) return;
                previousSelection = selected;
                selected = index;
                useSoulBurn = true;
                binder.ArmSimulationBurn(index);
            }
            ShowPrompt();
        }

        public bool ChooseTarget(string id)
        {
            if (!CanAct || !TrySkill(selected, out var skill)) return false;
            var target = Snapshot.GetUnit(id);
            bool allies = skill.target == "one_ally" || skill.target == "all_allies";
            if (target?.IsAlive != true || (target.Slot.Side == Actor.Slot.Side) != allies)
            {
                banner.text = allies ? "请选择存活的友军" : "请选择存活的敌人";
                return false;
            }
            return Submit(skill.id, target.InstanceId, useSoulBurn);
        }

        private bool Submit(string skill, string target, bool burn)
        {
            var snapshot = Snapshot;
            ulong sequence = snapshot.LastCommandSequence + 1;
            var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence,
                $"local-{sequence}", snapshot.CurrentActorId, skill, target, burn));
            if (result.Status != BattleCommandStatus.Accepted)
            {
                banner.text = "当前无法使用该技能";
                return false;
            }
            binder.SetSimulationSelection(-1);
            foreach (var button in SkillButtons()) button.interactable = false;
            hud.Require<GameButton>("soulBurn").interactable = false;
            foreach (var item in result.Events) playback.Enqueue(item);
            return true;
        }

        private void Present(BattleFlowEvent item)
        {
            switch (item.Kind)
            {
                case BattleFlowEventKind.Action:
                    SetPlayerControlsVisible(Snapshot.GetUnit(item.SourceId).Slot.Side == BattleSide.Ally);
                    var primary = item.TargetId != null && views.TryGetValue(item.TargetId, out var target)
                        ? target.Actor : null;
                    var targets = playback.TakeWhile(next => next.Kind != BattleFlowEventKind.Action)
                        .Where(next => next.Kind == BattleFlowEventKind.Damage || next.Kind == BattleFlowEventKind.Healing)
                        .Select(next => next.TargetId).Distinct(StringComparer.Ordinal)
                        .Select(id => views[id].Actor).ToArray();
                    var timing = skillPresentation.Begin(item.PresentationId, views[item.SourceId].Actor, primary, targets);
                    presentedSkillId = item.SkillId;
                    voice.Play(string.IsNullOrEmpty(item.PresentationId) ? item.SkillId : item.PresentationId);
                    pause = timing.ImpactSeconds;
                    recovery = Math.Max(0.15f, timing.Duration - pause);
                    string name = skills.TryGetValue(item.SkillId, out var skill) ? Label(skill.nameKey) : "史莱姆攻击";
                    banner.text = name;
                    break;
                case BattleFlowEventKind.Damage:
                case BattleFlowEventKind.Healing:
                    var view = views[item.TargetId];
                    view.Health.fillAmount = item.RemainingHealth.GetValueOrDefault() /
                        (float)Snapshot.GetUnit(item.TargetId).Stats.MaxHealth;
                    if (item.Amount > 0)
                        turnHud.ShowImpact(item, view.FeedbackAnchor, view.Actor.Bounds.center, camera);
                    // Damage is emitted after barrier absorption; zero health loss still represents a hit.
                    if (item.Kind == BattleFlowEventKind.Damage)
                    {
                        voice.PlayHit(Snapshot.GetUnit(item.TargetId).DefinitionId, presentedSkillId);
                        if (item.RemainingHealth > 0) view.Actor.PlayBattleAnimation("attacked");
                    }
                    break;
                case BattleFlowEventKind.CooldownChanged:
                    if (displayedActor?.InstanceId == item.TargetId)
                    {
                        int index = Array.IndexOf(displayedActor.Form.SkillIds.ToArray(), item.SkillId);
                        if (index >= 0) turnHud.SetCooldown(index, item.Amount);
                    }
                    break;
                case BattleFlowEventKind.ReadinessChanged:
                    turnHud.SetReadiness(item.TargetId, item.Amount);
                    break;
                case BattleFlowEventKind.TurnEnded:
                    turnHud.SetReadiness(item.SourceId, 0);
                    break;
                case BattleFlowEventKind.UnitDefeated:
                    turnHud.RemoveUnit(item.TargetId);
                    views[item.TargetId].Show(false);
                    break;
                case BattleFlowEventKind.TurnSkipped:
                    banner.text = $"{Label(Snapshot.GetUnit(item.SourceId).NameKey)} · 眩晕";
                    break;
            }
        }

        private void Refresh()
        {
            foreach (var state in Snapshot.Units)
            {
                var view = views[state.InstanceId];
                view.Show(state.IsAlive);
                view.Health.fillAmount = state.CurrentHealth / (float)state.Stats.MaxHealth;
                view.Readiness.fillAmount = Mathf.Clamp01(state.ReadinessBp / 10000f);
                view.UpdateLabels(state);
            }
            int soul = Snapshot.GetSoul(BattleSide.Ally);
            hud.Require<GameText>("soulCount").text = $"{soul} / 20";
            hud.Require<Image>("soulFill").fillAmount = soul / 20f;
            turnHud.Refresh(Snapshot);
            var actor = Actor;
            displayedActor = actor;
            SetPlayerControlsVisible(actor?.Slot.Side == BattleSide.Ally && Snapshot.Outcome == BattleOutcome.InProgress);
            if (actor != null)
            {
                hud.Require<GameText>("activeName").text = Label(actor.NameKey);
                hud.Require<GameText>("activeHp").text = $"{actor.CurrentHealth}/{actor.Stats.MaxHealth}";
                hud.Require<Image>("activeHpFill").fillAmount = actor.CurrentHealth / (float)actor.Stats.MaxHealth;
                var portrait = hud.Require<Image>("activePortrait");
                portrait.enabled = sprites.TryGetValue(actor.DefinitionId + "_su", out var actorPortrait);
                if (portrait.enabled) portrait.sprite = actorPortrait;
                SetActorArt("Element", "cm_icon_prom" + actor.Element.ToString().ToLowerInvariant());
                string role = classes.TryGetValue(actor.DefinitionId, out var heroClass) ? heroClass : "";
                SetActorArt("Class", "cm_icon_role_" + (role == "soul_weaver" ? "manauser" : role));
            }
            selected = 0;
            useSoulBurn = false;
            binder.SetSimulationSelection(CanAct ? selected : -1);
            int index = 0;
            foreach (var button in SkillButtons())
            {
                var image = (Image)button.targetGraphic;
                if (TrySkill(index, out var skill) && sprites.TryGetValue(Path.GetFileNameWithoutExtension(skill.iconAsset), out var icon))
                    image.sprite = icon;
                int turns = TrySkill(index, out skill) ? actor.GetCooldown(skill.id) : 0;
                button.interactable = CanAct && skill != null && turns == 0;
                image.color = button.interactable || turns > 0 ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
                turnHud.SetCooldown(index++, turns);
            }
            hud.Require<GameButton>("soulBurn").interactable = CanAct && BurnSlot() >= 0;
            ShowPrompt();
        }

        private void SetPlayerControlsVisible(bool visible)
        {
            SetHudObjectVisible(hud.transform.Find("Skills").gameObject, visible);
            SetHudObjectVisible(hud.Require<GameButton>("soulBurn").gameObject, visible);
        }

        private void SetHudObjectVisible(GameObject item, bool visible)
        {
            if (cinematicVisible && cinematicHudStates.ContainsKey(item))
                cinematicHudStates[item] = visible;
            else
                item.SetActive(visible);
        }

        private void SetCinematicVisible(bool value)
        {
            if (cinematicVisible == value) return;
            cinematicVisible = value;
            if (value)
            {
                foreach (var item in new[] { hud.transform.Find("Skills").gameObject,
                    hud.Require<GameButton>("soulBurn").gameObject, hud.transform.Find("Unit Status").gameObject })
                {
                    cinematicHudStates.Add(item, item.activeSelf);
                    item.SetActive(false);
                }
            }
            else
            {
                foreach (var item in cinematicHudStates)
                    if (item.Key) item.Key.SetActive(item.Value);
                cinematicHudStates.Clear();
            }
        }

        private void SetActorArt(string name, string spriteName)
        {
            var image = hud.transform.Find("Active Unit/" + name).GetComponent<Image>();
            image.enabled = sprites.TryGetValue(spriteName, out var sprite);
            if (image.enabled) image.sprite = sprite;
        }

        private void ShowPrompt()
        {
            banner.text = Snapshot.Outcome switch
            {
                BattleOutcome.AllyVictory => "战斗胜利",
                BattleOutcome.EnemyVictory => "战斗失败",
                BattleOutcome.Draw => "战斗平局",
                _ when !CanAct => "敌方行动",
                _ when TrySkill(selected, out var skill) => $"{Label(skill.nameKey)} · {(useSoulBurn ? "燃魂 · " : "")}点击目标",
                _ => "选择技能"
            };
        }

        private bool TrySkill(int index, out BattleCatalogData.SkillRow skill)
        {
            skill = null;
            return Actor != null && index >= 0 && index < Actor.Form.SkillIds.Count &&
                skills.TryGetValue(Actor.Form.SkillIds[index], out skill);
        }

        private int BurnSlot()
        {
            for (int i = 0; i < 3; i++)
                if (TrySkill(i, out var skill) && skill.soulBurnCost > 0 &&
                    Actor.GetCooldown(skill.id) == 0 && Snapshot.GetSoul(Actor.Slot.Side) >= skill.soulBurnCost)
                    return i;
            return -1;
        }

        private IEnumerable<GameButton> SkillButtons() =>
            Enumerable.Range(1, 3).Select(index => hud.Require<GameButton>("skill" + index));

        private string Label(string key) => messages.TryGetValue(key, out var value) ? value :
            key == "battle.preview.slime" ? "史莱姆" : key;

        private static GameText CreateText(Transform parent, GameText source, string name, Vector2 anchor,
            Vector2 position, Vector2 size, float point)
        {
            var label = new GameObject(name, typeof(RectTransform)).AddComponent<GameText>();
            label.transform.SetParent(parent, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = anchor;
            label.rectTransform.anchoredPosition = position;
            label.rectTransform.sizeDelta = size;
            label.font = source.font;
            label.fontSharedMaterial = source.fontSharedMaterial;
            label.fontSize = point;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            return label;
        }

        public void Dispose()
        {
            skillPresentation.Dispose();
            voice.Dispose();
            turnHud.Dispose();
            foreach (var view in views.Values) view.Restore();
            if (banner) UnityEngine.Object.Destroy(banner.gameObject);
            restore();
        }

        private sealed class View
        {
            private readonly Transform slot;
            private readonly GameObject marker;
            private readonly Image readiness;
            private readonly float originalHealth;
            private readonly float originalReadiness;
            private GameText statusLabel;
            private GameText levelLabel;
            private Image levelBadge;

            public View(Transform slot, GameObject marker)
            {
                this.slot = slot;
                this.marker = marker;
                Actor = slot.GetComponentInChildren<BattlePreviewActor>();
                Health = marker.transform.Find("HP").GetComponent<Image>();
                readiness = marker.transform.Find("Combat Readiness").GetComponent<Image>();
                FeedbackAnchor = (RectTransform)marker.transform.Find("Frame");
                originalHealth = Health.fillAmount;
                originalReadiness = readiness.fillAmount;
            }
            public BattlePreviewActor Actor { get; }
            public Image Health { get; }
            public Image Readiness => readiness;
            public RectTransform FeedbackAnchor { get; }
            public Vector3 Center => Actor.Bounds.center;
            public bool Contains(Ray ray) => slot.gameObject.activeInHierarchy && Actor.Bounds.IntersectRay(ray);

            public void Show(bool value)
            {
                slot.gameObject.SetActive(value);
                marker.SetActive(value);
            }

            public void AddLabels(GameText font, GameText digits)
            {
                statusLabel = CreateText(marker.transform, font, "Statuses", Vector2.zero,
                    FeedbackAnchor.anchoredPosition + Vector2.up * 24f, new Vector2(180, 24), 16);
                levelBadge = marker.transform.Find("Level").GetComponent<Image>();
                levelBadge.enabled = false;
                levelLabel = CreateText(marker.transform, digits, "Level Value", Vector2.zero,
                    levelBadge.rectTransform.anchoredPosition, new Vector2(34, 29), 19);
            }

            public void UpdateLabels(BattleUnitSnapshot state)
            {
                levelLabel.text = state.Level.ToString();
                statusLabel.text = string.Join(" ", state.Statuses.Select(status => $"{StatusName(status.Kind)}{status.RemainingTurns}"));
            }

            private static string StatusName(BattleStatusKind kind) => kind switch
            {
                BattleStatusKind.AttackUp => "攻↑",
                BattleStatusKind.DefenseUp => "防↑",
                BattleStatusKind.Immunity => "免疫",
                BattleStatusKind.Barrier => "盾",
                BattleStatusKind.Stun => "晕",
                _ => kind.ToString()
            };

            public void Restore()
            {
                if (statusLabel) UnityEngine.Object.Destroy(statusLabel.gameObject);
                if (levelLabel) UnityEngine.Object.Destroy(levelLabel.gameObject);
                if (levelBadge) levelBadge.enabled = true;
                if (slot) slot.gameObject.SetActive(true);
                if (!marker) return;
                marker.SetActive(true);
                Health.fillAmount = originalHealth;
                readiness.fillAmount = originalReadiness;
            }
        }

    }
}
