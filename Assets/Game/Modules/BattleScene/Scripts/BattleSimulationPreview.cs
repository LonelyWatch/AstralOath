using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using TMPro;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleSimulationPreview : IDisposable
    {
        private static readonly ProfilerMarker SubmitMarker = new ProfilerMarker("Astral.Battle.Submit");
        private readonly BattleMatch match;
        private readonly BattleHudBinder binder;
        private readonly BindingMap hud;
        private readonly Camera camera;
        private readonly Dictionary<string, BattleCatalogData.SkillRow> skills;
        private readonly Dictionary<string, string> messages;
        private readonly Dictionary<string, string> classes;
        private readonly Dictionary<string, Sprite> sprites;
        private BattleSkillPresentation skillPresentation;
        private readonly List<(BattleSkillPresentation Stage, float Duration)> recoveryStages = new List<(BattleSkillPresentation, float)>();
        private readonly BattleVoice voice;
        private readonly BattleTurnHud turnHud;
        private readonly Dictionary<string, View> views = new Dictionary<string, View>(StringComparer.Ordinal);
        private readonly Queue<BattleFlowEvent> playback = new Queue<BattleFlowEvent>();
        private readonly List<(float At, string TargetId, Action Show)> pendingFeedback =
            new List<(float, string, Action)>();
        private readonly GameText banner;
        private BattleUnitSnapshot displayedActor;
        private readonly Action restore;
        private BattleFlowEvent pendingAbsorption;
        private float actionElapsed;
        private float impactSeconds;
        private float actionDuration;
        private float lastDamageImpact;
        private float[] hitTimes = Array.Empty<float>();
        private int selected;
        private int previousSelection;
        private bool useSoulBurn;
        private string presentedSkillId;
        private bool cinematicVisible;
        private readonly Dictionary<GameObject, bool> cinematicHudStates = new Dictionary<GameObject, bool>();

        public BattleSimulationPreview(Scene scene, BindingMap hud, BattleHudBinder binder, BattleCatalogData catalog,
            Sprite[] icons, TextAsset[] graphs, BattleSkillPresentationAsset[] skillAssets, TextAsset audioCatalog,
            TextAsset[] audioBanks, TMP_FontAsset damageFont, TextAsset impactBank, GameObject stageBackground,
            BattleUnitPresentationProfile unitPresentation, BattleMatch match)
        {
            this.hud = hud;
            this.binder = binder;
            camera = hud.GetComponentInParent<Canvas>().worldCamera;
            skills = catalog.skills.ToDictionary(skill => skill.id, StringComparer.Ordinal);
            classes = catalog.heroes.ToDictionary(hero => hero.id, hero => hero.heroClass, StringComparer.Ordinal);
            messages = catalog.messages.ToDictionary(item => item.key, item => item.zhCn, StringComparer.Ordinal);
            sprites = icons.ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var deathTextures = unitPresentation ? unitPresentation.CreateDeathTextures() : new Dictionary<string, Texture2D>();
            var statusArt = unitPresentation
                ? unitPresentation.CreateStatusArt()
                : new BattleStatusArt(null, null, null);
            var shieldAura = unitPresentation
                ? BattleShieldAura.Prepare(unitPresentation.ShieldAura, unitPresentation.ShieldAuraEffect)
                : null;
            try
            {
                foreach (var state in match.Snapshot.Units)
                {
                    string team = state.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies";
                    var slot = units.Find(team).GetChild(state.Slot.Position - 1);
                    var marker = hud.transform.Find($"Unit Status/{team}/{slot.name}").gameObject;
                    sprites.TryGetValue("game_hud_bar_" + state.Element.ToString().ToLowerInvariant(), out var element);
                    views.Add(state.InstanceId, new View(slot, marker, camera, unitPresentation, deathTextures, statusArt, shieldAura, element));
                }
            }
            catch
            {
                foreach (var view in views.Values) view.Restore();
                views.Clear();
                throw;
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
            // Resolve the active roster before exposing inputs: no source DOM/Spine parse on an attack click.
            foreach (var state in Snapshot.Units)
                foreach (string skillId in state.Form.SkillIds)
                    skillPresentation.Prepare(views[state.InstanceId].Actor,
                        skills.TryGetValue(skillId, out var row) ? row.presentationId : skillId);
            voice = new BattleVoice(audioCatalog, audioBanks, impactBank);
            turnHud = new BattleTurnHud(hud, unit => Label(unit.NameKey), sprites, damageFont);
            turnHud.SetMissFeedbackLabel(Label("battle.feedback.miss"));
            foreach (var view in views.Values) view.AddLevel(hp);
            Refresh();
        }

        public BattleSnapshot Snapshot => match.Snapshot;
        public int StartedSkillVoices => voice.StartedVoiceCount;
        public int StartedSkillSounds => voice.StartedEffectCount;
        public int StartedHitSounds => voice.StartedHitCount;
        public bool IsCinematicVisible => cinematicVisible;
        public bool IsPaused { get; private set; }

        public void SetPaused(bool value)
        {
            if (IsPaused == value) return;
            IsPaused = value;
            foreach (var view in views.Values) view.Actor.SetPaused(value || view.DeathStarted);
            voice.SetPaused(value);
            UpdateSkillAvailability();
            hud.Require<GameButton>("soulBurn").interactable = CanAct && BurnSlot() >= 0;
        }

        private BattleUnitSnapshot Actor => Snapshot.GetUnit(Snapshot.CurrentActorId);
        private bool HasActionPresentation => actionElapsed < actionDuration || pendingFeedback.Count != 0 ||
            (recoveryStages != null && recoveryStages.Count != 0);
        private bool Busy
        {
            get
            {
                if (playback.Count != 0 || HasActionPresentation) return true;
                foreach (var view in views.Values) if (view.BlocksNextAction) return true;
                return false;
            }
        }
        private bool CanAct => !IsPaused && !Busy && Actor?.Slot.Side == BattleSide.Ally;

        public void Tick(float delta)
        {
            try { TickBattle(delta); }
            finally { foreach (var view in views.Values) view.LateTick(camera); }
        }

        private void TickBattle(float delta)
        {
            if (IsPaused) return;
            bool wasBusy = Busy;
            voice.Tick();
            AdvancePresentation(Mathf.Max(0f, delta));
            if (Busy) return;
            if (wasBusy)
            {
                Refresh();
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
                var target = views.Where(pair => Snapshot.GetUnit(pair.Key).IsAlive && pair.Value.Contains(ray))
                    .OrderBy(pair => (camera.WorldToScreenPoint(pair.Value.Center) - Input.mousePosition).sqrMagnitude)
                    .Select(pair => pair.Key).FirstOrDefault();
                if (target != null) ChooseTarget(target);
            }
        }

        private void AdvancePresentation(float remaining)
        {
            while (true)
            {
                ShowDueFeedback();
                PresentReadyEvents();
                ShowDueFeedback();
                if (remaining <= 0f) return;

                // Advance the STG pose to each cue before sampling that cue's world anchor.
                float nextTime = actionElapsed < actionDuration ? actionDuration : float.PositiveInfinity;
                if (playback.Count != 0 && actionElapsed < impactSeconds)
                    nextTime = Mathf.Min(nextTime, impactSeconds);
                if (pendingFeedback.Count != 0)
                    nextTime = Mathf.Min(nextTime, pendingFeedback[0].At);
                float untilNext = nextTime - actionElapsed;
                // A source-time boundary cannot subdivide wall time while Cut-in freezes the source clock.
                float cutinRemaining = skillPresentation.CutinRemainingSeconds;
                float step = Mathf.Min(remaining, cutinRemaining > 0f ? cutinRemaining : untilNext);

                float before = skillPresentation.StageTime;
                skillPresentation.Tick(step);
                float advanced = Mathf.Max(0f, skillPresentation.StageTime - before);
                TickRecoveries(advanced);
                // Source time owns feedback during a skill; idle/death tails still need a clock
                // when no finite action or cue is left. Cut-in freezes the source clock.
                float visualDelta = float.IsPositiveInfinity(nextTime) ? step : advanced;
                foreach (var view in views.Values) view.TickVisual(visualDelta);
                turnHud.Tick(advanced);
                if (float.IsPositiveInfinity(nextTime)) return;
                actionElapsed = advanced >= untilNext ? nextTime : actionElapsed + advanced;
                remaining -= step;
            }
        }

        private void TickRecoveries(float delta)
        {
            if (recoveryStages == null) return;
            for (int index = recoveryStages.Count - 1; index >= 0; index--)
            {
                var tail = recoveryStages[index];
                tail.Stage.Tick(delta);
                if (tail.Stage.StageTime < tail.Duration) continue;
                tail.Stage.Dispose();
                recoveryStages.RemoveAt(index);
            }
        }

        private void PresentReadyEvents()
        {
            while (playback.Count != 0)
            {
                var item = playback.Peek();
                if (item.Kind == BattleFlowEventKind.Action)
                {
                    bool handoff = item.Origin == BattleActionOrigin.DualAttack &&
                        actionElapsed >= (hitTimes.Length == 0 ? impactSeconds : hitTimes[hitTimes.Length - 1]) &&
                        pendingFeedback.Count == 0;
                    if (HasActionPresentation && !handoff) return;
                    if (handoff && actionElapsed < actionDuration)
                    {
                        var previous = skillPresentation;
                        skillPresentation = previous.CreateSuccessor();
                        recoveryStages.Add((previous, actionDuration));
                    }
                }
                else if (actionElapsed < impactSeconds) return;
                playback.Dequeue();
                Present(item);
            }
        }

        public void ChooseSkill(int index)
        {
            if (!CanAct || !TrySkill(index, out var skill) || skill.target == "passive" || Actor.GetCooldown(skill.id) > 0) return;
            // Once armed, only the dedicated Cancel control may clear Soul Burn.
            if (useSoulBurn) return;
            selected = index;
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
            UpdateSkillAvailability();
            ShowPrompt();
        }

        private void UpdateSkillAvailability()
        {
            int index = 0;
            foreach (var button in SkillButtons())
            {
                bool active = TrySkill(index, out var skill) && skill.target != "passive";
                bool available = CanAct && active && Actor.GetCooldown(skill.id) == 0 &&
                    (!useSoulBurn || index == selected);
                button.interactable = available;
                ((Image)button.targetGraphic).color = !active || (useSoulBurn && index != selected)
                    ? new Color(.35f, .35f, .35f, 1f) : Color.white;
                index++;
            }
        }

        public bool ChooseTarget(string id)
        {
            if (!CanAct || !TrySkill(selected, out var skill) || skill.target == "passive") return false;
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
            using var sample = SubmitMarker.Auto();
            var snapshot = Snapshot;
            ulong sequence = snapshot.LastCommandSequence + 1;
            var result = match.Submit(new BattleSkillCommand(snapshot.BattleId, snapshot.RulesVersion, sequence,
                $"local-{sequence}", snapshot.CurrentActorId, skill, target, burn));
            if (result.Status != BattleCommandStatus.Accepted)
            {
                banner.text = "当前无法使用该技能";
                return false;
            }
            turnHud.ResetFeedbackCombo();
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
                    if (item.Origin != BattleActionOrigin.DualAttack && item.Origin != BattleActionOrigin.ExtraAttack)
                        turnHud.BeginFeedbackAction();
                    if (item.Origin == BattleActionOrigin.Counter)
                        turnHud.ResetFeedbackCombo();
                    SetPlayerControlsVisible(Snapshot.GetUnit(item.SourceId).Slot.Side == BattleSide.Ally);
                    var primary = item.TargetId != null && views.TryGetValue(item.TargetId, out var target)
                        ? target.Actor : null;
                    var actionEvents = playback.TakeWhile(next => next.Kind != BattleFlowEventKind.Action).ToArray();
                    var targetEvents = actionEvents.Where(next => next.Kind == BattleFlowEventKind.Damage || next.Kind == BattleFlowEventKind.Healing).ToArray();
                    if (targetEvents.Length == 0 && skills.TryGetValue(item.SkillId, out var support) &&
                        (support.target == "all_allies" || support.target == "all_enemies"))
                    {
                        var sourceSide = Snapshot.GetUnit(item.SourceId).Slot.Side;
                        bool allies = support.target == "all_allies";
                        targetEvents = actionEvents.Where(next => next.SourceId == item.SourceId && next.TargetId != null &&
                            (next.Kind == BattleFlowEventKind.StatusApplied || next.Kind == BattleFlowEventKind.ReadinessChanged ||
                                (next.Kind == BattleFlowEventKind.StatusRemoved && next.TargetId != item.SourceId)) &&
                            (Snapshot.GetUnit(next.TargetId).Slot.Side == sourceSide) == allies).ToArray();
                    }
                    var targets = targetEvents.Select(next => next.TargetId).Where(id => id != null)
                        .Distinct(StringComparer.Ordinal).Select(id => views[id].Actor).ToArray();
                    var timing = skillPresentation.Begin(item.PresentationId, views[item.SourceId].Actor, primary, targets);
                    presentedSkillId = item.SkillId;
                    voice.Play(string.IsNullOrEmpty(item.PresentationId) ? item.SkillId : item.PresentationId);
                    actionElapsed = 0f;
                    impactSeconds = timing.ImpactSeconds;
                    lastDamageImpact = impactSeconds;
                    hitTimes = skillPresentation.HitTimes.ToArray();
                    float lastHit = hitTimes.Length == 0 ? impactSeconds : hitTimes[hitTimes.Length - 1];
                    actionDuration = Mathf.Max(timing.Duration, lastHit + 0.15f);
                    string name = skills.TryGetValue(item.SkillId, out var skill) ? Label(skill.nameKey) :
                        item.SkillId == BattlePreviewFactory.SlimeSkillId ? "史莱姆攻击" : item.SkillId;
                    banner.text = name;
                    break;
                case BattleFlowEventKind.BarrierAbsorbed:
                    pendingAbsorption = item;
                    break;
                case BattleFlowEventKind.StatusApplied:
                    ScheduleFeedback(Mathf.Max(lastDamageImpact, LastFeedbackAt(item.TargetId)),
                        item.TargetId, () => views[item.TargetId].ApplyStatus(item.Status));
                    break;
                case BattleFlowEventKind.StatusRemoved:
                    // Absorb emits this just before Damage when the last shield point is used.
                    // Damage will remove the icon and aura at the hit that consumes it.
                    if (item.StatusKind.HasValue &&
                        (item.StatusKind != BattleStatusKind.Barrier || !MatchesAbsorption(item)))
                    {
                        float at = LastFeedbackAt(item.TargetId);
                        // Turn-end expiry is emitted with the unit as both source and target.
                        // Keep its buff/ward through the attacking unit's remaining hit cues.
                        if (item.SourceId == item.TargetId && hitTimes.Length != 0)
                            at = Mathf.Max(at, hitTimes[hitTimes.Length - 1]);
                        ScheduleFeedback(at, item.TargetId, () => views[item.TargetId].RemoveStatus(item.StatusKind.Value));
                    }
                    break;
                case BattleFlowEventKind.Damage:
                    int absorbed = MatchesAbsorption(item) ? pendingAbsorption.Amount : 0;
                    pendingAbsorption = null;
                    ScheduleDamage(item, absorbed);
                    break;
                case BattleFlowEventKind.Healing:
                    ScheduleFeedback(LastFeedbackAt(item.TargetId), item.TargetId, () => ShowHealing(item));
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
                    ScheduleFeedback(LastFeedbackAt(item.TargetId), item.TargetId, () =>
                    {
                        turnHud.RemoveUnit(item.TargetId);
                        views[item.TargetId].BeginDeath();
                    });
                    break;
                case BattleFlowEventKind.TurnSkipped:
                    banner.text = $"{Label(Snapshot.GetUnit(item.SourceId).NameKey)} · 眩晕";
                    break;
            }
        }

        private bool MatchesAbsorption(BattleFlowEvent item) =>
            pendingAbsorption != null && pendingAbsorption.SourceId == item.SourceId &&
            pendingAbsorption.TargetId == item.TargetId;

        private void ScheduleDamage(BattleFlowEvent item, int absorbed)
        {
            var view = views[item.TargetId];
            var unit = Snapshot.GetUnit(item.TargetId);
            // The authoritative damage event reports HP loss separately. Use the same source
            // hit clock for both parts and spend the shield before subtracting HP.
            var hits = BattleDamageSequence.Build(item.Amount + absorbed, hitTimes, impactSeconds);
            for (int index = 0; index < hits.Length; index++)
            {
                var hit = hits[index];
                lastDamageImpact = hit.At;
                bool first = index == 0;
                int shieldLoss = Math.Min(absorbed, hit.Amount);
                int healthLoss = hit.Amount - shieldLoss;
                absorbed -= shieldLoss;
                ScheduleFeedback(hit.At, item.TargetId, () =>
                {
                    // A hit absorbed by a barrier still plays its first-impact reaction.
                    if (first)
                    {
                        voice.PlayHit(unit.DefinitionId, presentedSkillId);
                        if ((item.RemainingHealth > 0 || hits.Length > 1) && !skillPresentation.OwnsNodeAnimationPose(view.Actor))
                            view.Actor.PlayBattleAnimation("attacked");
                    }
                    if (shieldLoss > 0) view.AbsorbShield(shieldLoss);
                    view.AdjustHealth(-healthLoss, unit.Stats.MaxHealth);
                    var bounds = view.Actor.Bounds;
                    var world = bounds.center;
                    world.y = bounds.min.y + 0.27f * bounds.size.y;
                    turnHud.ShowDamageHit(item.TargetId, healthLoss, item.HitKind, view.FeedbackAnchor, world, camera);
                });
            }
        }

        private void ShowHealing(BattleFlowEvent item)
        {
            var view = views[item.TargetId];
            view.AdjustHealth(item.Amount, Snapshot.GetUnit(item.TargetId).Stats.MaxHealth);
            if (item.Amount > 0)
                turnHud.ShowImpact(item, view.FeedbackAnchor, view.Actor.Bounds.center, camera);
        }

        private float LastFeedbackAt(string targetId)
        {
            int index = pendingFeedback.FindLastIndex(cue => cue.TargetId == targetId);
            return index < 0 ? impactSeconds : pendingFeedback[index].At;
        }

        private void ScheduleFeedback(float at, string targetId, Action show)
        {
            // Preserve authoritative event order for cues that share a time.
            int index = pendingFeedback.FindIndex(cue => cue.At > at);
            if (index < 0) pendingFeedback.Add((at, targetId, show));
            else pendingFeedback.Insert(index, (at, targetId, show));
        }

        private void ShowDueFeedback()
        {
            while (pendingFeedback.Count != 0 && pendingFeedback[0].At <= actionElapsed)
            {
                var cue = pendingFeedback[0];
                pendingFeedback.RemoveAt(0);
                cue.Show();
            }
        }

        private void Refresh()
        {
            foreach (var state in Snapshot.Units)
            {
                var view = views[state.InstanceId];
                view.Show(state.IsAlive);
                view.SetHealth(state.CurrentHealth, state.Stats.MaxHealth);
                view.Readiness.fillAmount = Mathf.Clamp01(state.ReadinessBp / 10000f);
                view.RefreshStatuses(state);
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
                SetActorArt("Class", "cm_icon_role_" + (role switch
                {
                    "soul_weaver" => "manauser",
                    "thief" => "assassin",
                    _ => role
                }));
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
                button.interactable = CanAct && skill != null && skill.target != "passive" && turns == 0;
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
                // Each status follows its body mask; Cut-in covers the remaining status through renderer ordering.
                foreach (var item in new[] { hud.transform.Find("Skills").gameObject,
                    hud.Require<GameButton>("soulBurn").gameObject })
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
                if (TrySkill(i, out var skill) && skill.target != "passive" && skill.soulBurnCost > 0 &&
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
            playback.Clear();
            pendingFeedback.Clear();
            pendingAbsorption = null;
            skillPresentation.Dispose();
            foreach (var tail in recoveryStages) tail.Stage.Dispose();
            recoveryStages.Clear();
            foreach (var view in views.Values) view.Actor.SetPaused(false);
            voice.Dispose();
            turnHud.Dispose();
            foreach (var view in views.Values) view.Restore();
            RemoveOwnedObject(banner ? banner.gameObject : null);
            restore();
        }

        private static void RemoveOwnedObject(GameObject item)
        {
            if (!item) return;
            item.SetActive(false);
            item.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(item);
        }

        private sealed class View
        {
            private readonly Transform slot;
            private readonly GameObject marker;
            private readonly Image readiness, elementBadge;
            private readonly Sprite originalElement;
            private readonly float originalHealth;
            private readonly float originalReadiness;
            private readonly BattleUnitStatusPresentation status;
            private readonly BattleUnitPresentationProfile profile;
            private readonly IReadOnlyDictionary<string, Texture2D> deathTextures;
            private BattleDeathPresentation death;
            private bool warnedAnchor, deathStarted;
            private bool shown = true;
            private long presentedHealth;
            private long presentedShield;
            private int maximumHealth;
            private readonly BattleStatusIcons statusIcons;
            private readonly BattleShieldAura shieldAura;
            private GameText levelLabel;
            private Image levelBadge;

            public View(Transform slot, GameObject marker, Camera camera, BattleUnitPresentationProfile profile,
                IReadOnlyDictionary<string, Texture2D> deathTextures, BattleStatusArt statusArt,
                BattleShieldAura.Definition auraDefinition, Sprite element)
            {
                this.slot = slot;
                this.marker = marker;
                Actor = slot.GetComponentInChildren<BattlePreviewActor>();
                Health = marker.transform.Find("HP").GetComponent<Image>();
                readiness = marker.transform.Find("Combat Readiness").GetComponent<Image>();
                elementBadge = marker.transform.Find("Element").GetComponent<Image>();
                originalElement = elementBadge.sprite;
                FeedbackAnchor = (RectTransform)marker.transform.Find("Frame");
                originalHealth = Health.fillAmount;
                originalReadiness = readiness.fillAmount;
                this.profile = profile; this.deathTextures = deathTextures;
                status = new BattleUnitStatusPresentation((RectTransform)marker.transform, Health, Actor.transform,
                    StatusAnchor, camera, profile ? profile.DamageTrail : null, profile ? profile.HealthDivider : null,
                    profile ? profile.HealthSections : 10);
                statusIcons = new BattleStatusIcons(FeedbackAnchor, statusArt, profile ? profile.StatusIconsPerRow : 5);
                shieldAura = new BattleShieldAura(auraDefinition, Actor.PresentationShadow, Actor.PresentationRenderer);
                if (element) elementBadge.sprite = element;
                if (profile)
                {
                    status.SetShieldSprite(profile.Shield);
                    status.AnchorOffset = profile.StatusOffset;
                    status.DamageSeconds = profile.DamageSeconds; status.HealSeconds = profile.HealSeconds;
                    status.TrailDelaySeconds = profile.TrailDelaySeconds; status.TrailSeconds = profile.TrailSeconds;
                }
                try { PrepareDeath(); }
                catch { shieldAura.Dispose(); statusIcons.Dispose(); status.Dispose(); elementBadge.sprite = originalElement; throw; }
            }
            public BattlePreviewActor Actor { get; }
            public Image Health { get; }
            public Image Readiness => readiness;
            public RectTransform FeedbackAnchor { get; }
            public Vector3 Center => Actor.Bounds.center;
            public bool Contains(Ray ray) => slot.gameObject.activeInHierarchy && Actor.Bounds.IntersectRay(ray);

            public bool IsDying => death?.IsRunning == true;
            public bool DeathStarted => deathStarted;
            public bool BlocksNextAction => deathStarted && !death.BodyComplete;

            private Vector3 StatusAnchor()
            {
                string anchor = profile && !string.IsNullOrWhiteSpace(profile.StatusAnchor) ? profile.StatusAnchor : "top";
                if (Actor.TryGetPresentationAnchor(anchor, out var world)) return world;
                if (!warnedAnchor) { warnedAnchor = true; Debug.LogWarning("Missing status anchor '" + anchor + "' on " + Actor.name + ". Configure the presentation profile; using actor origin."); }
                return Actor.transform.position;
            }
            public void LateTick(Camera camera)
            {
                UpdateStatusVisibility();
                if (marker.activeInHierarchy) status.LateTick(camera);
                SyncAura(0f);
            }
            private void UpdateStatusVisibility()
            {
                // Source layouts own body masking. Read the current skeleton, never last-frame
                // Renderer.isVisible or the presence of auxiliary particles/shadows.
                var body = Actor.PresentationRenderer;
                bool visible = shown && !deathStarted && body && body.enabled && !body.forceRenderingOff && body.gameObject.activeInHierarchy;
                if (marker.activeSelf != visible) marker.SetActive(visible);
            }
            public void TickVisual(float delta)
            {
                status.Tick(delta);
                SyncAura(delta);
                if (death?.IsRunning != true) return;
                death.Tick(delta);
                if (death.IsComplete) slot.gameObject.SetActive(false);
            }
            private void PrepareDeath()
            {
                death = new BattleDeathPresentation(Actor.transform, profile ? profile.DeathParticles : null,
                    deathTextures, Actor.PresentationShadow ? Actor.PresentationShadow.GetComponent<Renderer>() : null, Actor.PresentationRenderer);
                try { if (profile) death.ConfigureTimings(profile.DeathDarkenSeconds, profile.DeathFadeSeconds); }
                catch { death.Dispose(); death = null; throw; }
            }
            public void BeginDeath()
            {
                if (deathStarted) return;
                deathStarted = true;
                SetShield(0);
                Actor.SetPaused(true); marker.SetActive(false); death.Begin();
            }
            public void Show(bool value)
            {
                shown = value;
                bool resurrecting = value && deathStarted;
                if (resurrecting)
                {
                    // A disabled slot has destroyed its skeleton renderers. Rebind only after
                    // activation, during revival preparation rather than the next fatal hit.
                    death.Dispose(); death = null; deathStarted = false;
                }
                if (value || !IsDying) slot.gameObject.SetActive(value);
                if (resurrecting) { Actor.SetPaused(false); PrepareDeath(); }
                UpdateStatusVisibility();
            }

            public void AdjustHealth(int delta, int maximum) => SetHealth(presentedHealth + delta, maximum);

            public void SetHealth(long health, int maximum)
            {
                presentedHealth = health;
                maximumHealth = maximum;
                status.UpdateHealth(health, maximum, false);
            }

            public void AddLevel(GameText digits)
            {
                levelBadge = marker.transform.Find("Level").GetComponent<Image>();
                levelBadge.enabled = false;
                levelLabel = CreateText(marker.transform, digits, "Level Value", Vector2.zero,
                    levelBadge.rectTransform.anchoredPosition, new Vector2(34, 29), 19);
            }

            public void RefreshStatuses(BattleUnitSnapshot state)
            {
                levelLabel.text = state.Level.ToString();
                statusIcons.Reconcile(state.IsAlive ? state.Statuses : Array.Empty<BattleStatusSnapshot>());
                SetShield(state.IsAlive ? state.Barrier : 0);
            }

            public void ApplyStatus(BattleStatusSnapshot effect)
            {
                if (effect.Kind == BattleStatusKind.Barrier)
                {
                    SetShield(effect.Strength);
                    if (presentedShield == 0)
                    {
                        statusIcons.Remove(effect.Kind);
                        return;
                    }
                }
                statusIcons.Apply(effect);
            }

            public void RemoveStatus(BattleStatusKind kind)
            {
                if (kind == BattleStatusKind.Barrier) SetShield(0);
                statusIcons.Remove(kind);
            }

            public void AbsorbShield(int amount)
            {
                SetShield(Math.Max(0L, presentedShield - amount));
                if (presentedShield == 0) statusIcons.Remove(BattleStatusKind.Barrier);
            }

            private void SetShield(long amount)
            {
                presentedShield = amount;
                status.UpdateShield(amount, maximumHealth, false);
                SyncAura(0f);
            }

            private void SyncAura(float delta) =>
                shieldAura.Sync(Actor.PresentationRenderer, shown && !deathStarted && presentedShield > 0, delta);

            public void Restore()
            {
                death?.Dispose(); death = null; deathStarted = false; status.Dispose();
                shieldAura.Dispose();
                statusIcons.Dispose();
                RemoveOwnedObject(levelLabel ? levelLabel.gameObject : null);
                if (levelBadge) levelBadge.enabled = true;
                if (slot) slot.gameObject.SetActive(true);
                if (!marker) return;
                marker.SetActive(true);
                Health.fillAmount = originalHealth;
                elementBadge.sprite = originalElement;
                readiness.fillAmount = originalReadiness;
            }
        }

    }
}
