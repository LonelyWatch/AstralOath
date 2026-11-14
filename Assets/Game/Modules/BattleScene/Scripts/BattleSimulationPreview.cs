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
        private static readonly ProfilerMarker RefreshMarker = new ProfilerMarker("Astral.Battle.Refresh");
        private const double SkillQuickCastSeconds = .5;
        private readonly BattleMatch match;
        private readonly BattleAiPolicy ai;
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
        private readonly BattleStatusArt statusArt;
        private readonly BattleSoulGauge soulGauge;
        private readonly BattleActivePortrait activePortrait;
        private readonly BattleTurnEntryEffects turnEntryEffects;
        private readonly Dictionary<string, View> views = new Dictionary<string, View>(StringComparer.Ordinal);
        private readonly Queue<BattleFlowEvent> playback = new Queue<BattleFlowEvent>();
        private readonly List<(float At, string TargetId, Action Show)> pendingFeedback =
            new List<(float, string, Action)>();
        private readonly GameText banner;
        private readonly BattleSkillBanner skillBanner;
        private readonly BattleInspectionContent inspectionContent;
        private readonly InfoPanelBinder inspection;
        private readonly GameLongPress[] skillHolds = new GameLongPress[3];
        private BattleUnitSnapshot displayedActor;
        private (int Turn, string ActorId) lastReadyVoice;
        private readonly Action restore;
        private BattleFlowEvent pendingAbsorption;
        private float actionElapsed;
        private float impactSeconds;
        private float actionDuration;
        private float lastDamageImpact;
        private float[] hitTimes = Array.Empty<float>();
        private int selected;
        private bool useSoulBurn;
        private bool autoBattle;
        private int lastClickedSkill = -1;
        private double lastSkillClickTime;
        private string presentedSkillId;
        private string forcedActorId;
        private bool cinematicVisible;
        private bool presentingNextTurn;
        private int damageTurn = -1;
        private readonly Dictionary<GameObject, bool> cinematicHudStates = new Dictionary<GameObject, bool>();

        public BattleSimulationPreview(Scene scene, BindingMap hud, BattleHudBinder binder, BattleCatalogData catalog,
            Sprite[] icons, TextAsset[] graphs, BattleSkillPresentationAsset[] skillAssets, TextAsset audioCatalog,
            TextAsset[] audioBanks, TMP_FontAsset damageFont, TextAsset impactBank, GameObject stageBackground,
            BattleHudPreview.SpineEffect soulDisplay, BattleHudPreview.SpineEffect soulConsume,
            TextAsset[] particles, Texture2D[] particleTextures, BattleMatch match, GameObject inspectionPrefab = null,
            BattleHudPreview.SpineEffect readyDust = null, TextAsset skillTimelineOverrides = null)
        {
            this.hud = hud;
            this.binder = binder;
            inspectionContent = new BattleInspectionContent(catalog);
            if (inspectionPrefab) inspection = new InfoPanelBinder(inspectionPrefab, (RectTransform)hud.transform);
            camera = hud.GetComponentInParent<Canvas>().worldCamera;
            skills = catalog.skills.ToDictionary(skill => skill.id, StringComparer.Ordinal);
            classes = catalog.heroes.ToDictionary(hero => hero.id, hero => hero.heroClass, StringComparer.Ordinal);
            messages = catalog.messages.ToDictionary(item => item.key, item => item.zhCn, StringComparer.Ordinal);
            sprites = icons.ToDictionary(sprite => sprite.name, StringComparer.Ordinal);
            this.match = match ?? throw new ArgumentNullException(nameof(match));
            ai = new BattleAiPolicy(catalog);
            var units = scene.GetRootGameObjects().Single(root => root.name == "Units").transform;
            var statusRoot = hud.transform.Find("Unit Status");
            var unitPresentation = units.GetComponentInChildren<BattleActorStatusTemplate>(true)?.Profile;
            if (!statusRoot || !unitPresentation)
                throw new InvalidOperationException("BattleActor requires a unit status template and shared presentation profile.");
            var deathTextures = unitPresentation ? unitPresentation.CreateDeathTextures() : new Dictionary<string, Texture2D>();
            statusArt = unitPresentation
                ? unitPresentation.CreateStatusArt()
                : new BattleStatusArt(null, null, null);
            var shieldAura = unitPresentation
                ? BattleShieldAura.Prepare(unitPresentation.ShieldAura, unitPresentation.ShieldAuraEffect)
                : null;
            var invincibleAura = unitPresentation
                ? BattleShieldAura.Prepare(unitPresentation.InvincibleAura, unitPresentation.InvincibleAuraEffect)
                : null;
            try
            {
                foreach (var state in match.Snapshot.Units)
                {
                    string team = state.Slot.Side == BattleSide.Ally ? "Allies" : "Enemies";
                    var slot = units.Find(team).GetChild(state.Slot.Position - 1);
                    var actor = slot.GetComponentInChildren<BattlePreviewActor>(true);
                    var template = actor.GetComponent<BattleActorStatusTemplate>();
                    var marker = template.CreateMarker(statusRoot.Find(team), slot.name);
                    try
                    {
                        sprites.TryGetValue("game_hud_bar_" + state.Element.ToString().ToLowerInvariant(), out var element);
                        views.Add(state.InstanceId, new View(slot, actor, marker, camera, unitPresentation, deathTextures,
                            statusArt, shieldAura, invincibleAura, element, state.Resource, inspection == null ? null : InspectStatus));
                    }
                    catch
                    {
                        RemoveOwnedObject(marker);
                        throw;
                    }
                }
            }
            catch
            {
                foreach (var view in views.Values) view.Restore();
                views.Clear();
                inspection?.Dispose();
                throw;
            }

            var text = hud.Require<GameText>("activeName");
            var hp = hud.Require<GameText>("activeHp");
            var soul = hud.Require<GameText>("soulCount");
            var wave = hud.Require<GameText>("wave");
            string oldWave = wave.text;
            wave.text = "1 / 1";
            activePortrait = new BattleActivePortrait(hud.Require<Image>("activePortrait"));
            var hpFill = hud.Require<Image>("activeHpFill");
            var soulFill = hud.Require<Image>("soulFill");
            string oldName = text.text, oldHp = hp.text, oldSoul = soul.text;
            float oldHpFill = hpFill.fillAmount, oldSoulFill = soulFill.fillAmount;
            var skillGroup = hud.transform.Find("Skills").gameObject;
            var burnButton = hud.Require<GameButton>("soulBurn").gameObject;
            bool oldSkillVisibility = skillGroup.activeSelf, oldBurnVisibility = burnButton.activeSelf;
            var buttons = SkillButtons().Select(button => (button, image: (Image)button.targetGraphic)).ToArray();
            var oldIcons = buttons.Select(item => item.image.sprite).ToArray();
            if (inspection != null)
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    int slot = i;
                    var button = buttons[i].button;
                    var hold = button.GetComponent<GameLongPress>() ?? button.gameObject.AddComponent<GameLongPress>();
                    hold.Held = () => InspectSkill(displayedActor, slot, buttons[slot].image, false);
                    skillHolds[i] = hold;
                }
            }
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
                activePortrait.Restore();
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
            skillBanner = new BattleSkillBanner((RectTransform)hud.transform.Find("Top Bar"), text, sprites);
            skillPresentation = new BattleSkillPresentation(units, graphs, skillAssets, camera, SetCinematicVisible, stageBackground,
                overrides: skillTimelineOverrides);
            // Resolve the active roster before exposing inputs: no source DOM/Spine parse on an attack click.
            foreach (var state in Snapshot.Units)
            {
                var actorView = views[state.InstanceId].Actor;
                foreach (string skillId in state.Form.SkillIds)
                {
                    skills.TryGetValue(skillId, out var row);
                    skillPresentation.Prepare(actorView, row?.presentationId ?? skillId);
                    if (!string.IsNullOrEmpty(row?.speedUpPresentationId))
                        skillPresentation.Prepare(actorView, row.speedUpPresentationId);
                    if (!string.IsNullOrEmpty(row?.burnPresentationId))
                        skillPresentation.Prepare(actorView, row.burnPresentationId);
                }
            }
            voice = new BattleVoice(audioCatalog, audioBanks, impactBank, Snapshot.Units.Select(unit => unit.DefinitionId));
            turnHud = new BattleTurnHud(hud, unit => Label(unit.NameKey), sprites, damageFont,
                (unit, slot, image) => InspectSkill(unit, slot, image, true),
                InspectStatus, CloseInspection, statusArt, skills);
            turnHud.SetMissFeedbackLabel(Label("battle.feedback.miss"));
            soulGauge = new BattleSoulGauge(soulFill, soul, match.MaximumSoul, soulDisplay, soulConsume,
                particles.Single(item => item.name == "soul_pati_01.particle"),
                particles.Single(item => item.name == "soul_consume_pati.particle"),
                particleTextures.ToDictionary(texture => texture.name + ".sct", StringComparer.Ordinal));
            turnEntryEffects = new BattleTurnEntryEffects(units, views.Values.Select(view => view.Actor), readyDust);
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
            if (value) CloseInspection();
            lastClickedSkill = -1;
            foreach (var view in views.Values) view.Actor.SetPaused(value || view.DeathStarted);
            voice.SetPaused(value);
            UpdateSkillAvailability();
            hud.Require<GameButton>("soulBurn").interactable = CanAct && BurnSlot() >= 0;
            binder.SetAutoInteractable(!value && !useSoulBurn && Snapshot.Outcome == BattleOutcome.InProgress);
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
        private bool CanAct => !autoBattle && !IsPaused && !Busy &&
            Actor?.Slot.Side == BattleSide.Ally && !ai.IsForcedAction(Snapshot);

        public void Tick(float delta)
        {
            try { TickBattle(delta); }
            finally
            {
                foreach (var view in views.Values) view.LateTick(camera);
                inspection?.Tick();
            }
        }

        private void TickBattle(float delta)
        {
            if (IsPaused) return;
            bool wasBusy = Busy;
            turnEntryEffects.Tick(Mathf.Max(0f, delta));
            voice.Tick();
            soulGauge.Tick(delta);
            AdvancePresentation(Mathf.Max(0f, delta));
            if (Busy) return;
            if (wasBusy)
            {
                Refresh();
                return;
            }
            if (Snapshot.Outcome != BattleOutcome.InProgress) return;
            if (autoBattle || Actor.Slot.Side == BattleSide.Enemy || ai.IsForcedAction(Snapshot))
            {
                var choice = ai.ChooseAction(Snapshot);
                if (choice != null) Submit(choice.SkillId, choice.TargetId, choice.UseSoulBurn);
                return;
            }
            if (TrySkill(selected, out var skill) && TryGetWorldPress(out var press))
            {
                Ray ray = camera.ScreenPointToRay(press);
                var target = views.Where(pair => ai.CanSelectTarget(Snapshot, skill.id, pair.Key, useSoulBurn) &&
                    pair.Value.Contains(ray, allowInactive: skill.target == "one_ally_including_dead"))
                    .OrderBy(pair => ((Vector2)camera.WorldToScreenPoint(pair.Value.Center) - press).sqrMagnitude)
                    .Select(pair => pair.Key).FirstOrDefault();
                if (target != null) ChooseTarget(target);
            }
        }

        private bool TryGetWorldPress(out Vector2 position)
        {
            position = default;
            if (inspection?.IsOpen == true) return false;
            var input = EventSystem.current;
            if (Input.touchCount > 0)
            {
                if (Input.touchCount != 1) return false;
                Touch touch = Input.GetTouch(0);
                position = touch.position;
                return touch.phase == TouchPhase.Began && (input == null || !input.IsPointerOverGameObject(touch.fingerId));
            }

            position = Input.mousePosition;
            return Input.GetMouseButtonDown(0) && (input == null || !input.IsPointerOverGameObject());
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
                skillBanner.Tick(advanced, skillPresentation.CutinRemainingSeconds > 0f);
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
                else if (IsPeriodicDamage(item) ? HasActionPresentation : actionElapsed < impactSeconds) return;
                playback.Dequeue();
                Present(item);
            }
        }

        public void ChooseSkill(int index)
        {
            if (!CanAct || !TrySkill(index, out var skill) || (useSoulBurn && index != selected) ||
                !IsSkillAvailable(skill, useSoulBurn)) return;
            double now = Time.unscaledTimeAsDouble;
            bool quickCast = lastClickedSkill == index && now - lastSkillClickTime <= SkillQuickCastSeconds;
            lastClickedSkill = index;
            lastSkillClickTime = now;
            if (quickCast)
            {
                var choice = ai.ChooseTarget(Snapshot, skill.id, useSoulBurn);
                if (choice != null && Submit(choice.SkillId, choice.TargetId, choice.UseSoulBurn)) return;
            }
            if (!useSoulBurn)
            {
                selected = index;
                binder.SetSimulationSelection(index);
                ShowPrompt();
            }
        }

        public void ToggleAuto()
        {
            if (IsPaused || useSoulBurn || Snapshot.Outcome != BattleOutcome.InProgress) return;
            autoBattle = !autoBattle;
            lastClickedSkill = -1;
            binder.SetAutoEnabled(autoBattle);
            if (Busy) return;
            binder.SetSimulationSelection(CanAct ? selected : -1);
            UpdateSkillAvailability();
            hud.Require<GameButton>("soulBurn").interactable = CanAct && BurnSlot() >= 0;
            ShowPrompt();
        }

        public void ToggleBurn()
        {
            if (!CanAct) return;
            lastClickedSkill = -1;
            if (useSoulBurn)
            {
                useSoulBurn = false;
                binder.CancelSimulationBurn();
            }
            else
            {
                int index = BurnSlot();
                if (index < 0) return;
                selected = index;
                useSoulBurn = true;
                binder.ArmSimulationBurn(index);
                voice.PlayBurnReady();
            }
            binder.SetAutoInteractable(!useSoulBurn);
            UpdateSkillAvailability();
            ShowPrompt();
        }

        private void UpdateSkillAvailability()
        {
            int index = 0;
            foreach (var button in SkillButtons())
            {
                bool hasSkill = TrySkill(index, out var skill);
                bool passive = hasSkill && skill.target == "passive";
                bool available = hasSkill && !passive && (!useSoulBurn || index == selected) && IsSkillAvailable(skill, useSoulBurn);
                button.interactable = CanAct && available;
                ((Image)button.targetGraphic).color = !(available || passive)
                    ? new Color(.35f, .35f, .35f, 1f) : Color.white;
                index++;
            }
        }

        public bool ChooseTarget(string id)
        {
            if (!CanAct || !TrySkill(selected, out var skill) || !IsSkillAvailable(skill, useSoulBurn)) return false;
            if (!ai.CanSelectTarget(Snapshot, skill.id, id, useSoulBurn)) return false;
            return Submit(skill.id, id, useSoulBurn);
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
            if (burn && snapshot.GetUnit(snapshot.CurrentActorId).Slot.Side == BattleSide.Ally)
            {
                // The resolver emits the confirmed cost before the ordinary soul gain.
                var cost = result.Events.FirstOrDefault(item => item.Kind == BattleFlowEventKind.SoulChanged &&
                    item.Side == BattleSide.Ally && item.SourceId == snapshot.CurrentActorId);
                if (cost != null && cost.Amount < snapshot.AllySoul)
                    soulGauge.Show(cost.Amount, animateConsumption: true);
            }
            lastClickedSkill = -1;
            useSoulBurn = false;
            forcedActorId = ai.IsForcedAction(snapshot) ? snapshot.CurrentActorId : null;
            binder.SetAutoInteractable(result.Snapshot.Outcome == BattleOutcome.InProgress);
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
                    CloseInspection();
                    presentingNextTurn = false;
                    if (item.Origin == BattleActionOrigin.Primary)
                        turnHud.SetReadiness(item.SourceId, 0);
                    if (item.Origin == BattleActionOrigin.Primary || item.Origin == BattleActionOrigin.Counter)
                        turnHud.BeginFeedbackAction();
                    if (item.Origin == BattleActionOrigin.Counter)
                        turnHud.ResetFeedbackCombo();
                    bool allyPrimary = item.Origin == BattleActionOrigin.Primary &&
                        Snapshot.GetUnit(item.SourceId).Slot.Side == BattleSide.Ally && item.SourceId != forcedActorId;
                    SetPlayerControlsVisible(allyPrimary);
                    var primary = item.TargetId != null && views.TryGetValue(item.TargetId, out var target)
                        ? target.Actor : null;
                    var actionEvents = playback.TakeWhile(next => next.Kind != BattleFlowEventKind.Action).ToArray();
                    var sourceSide = Snapshot.GetUnit(item.SourceId).Slot.Side;
                    bool offensive = skills.TryGetValue(item.SkillId, out var actionSkill)
                        ? actionSkill.target is "one_enemy" or "two_enemies" or "all_enemies" or "passive"
                        : item.SkillId == BattlePreviewFactory.SlimeSkillId;
                    var targetEvents = actionEvents.Where(next =>
                        (offensive ? next.Kind == BattleFlowEventKind.Damage && !IsPeriodicDamage(next)
                            : next.Kind is BattleFlowEventKind.Healing or BattleFlowEventKind.UnitRevived) &&
                        (Snapshot.GetUnit(next.TargetId).Slot.Side != sourceSide) == offensive).ToArray();
                    if (targetEvents.Length == 0 && skills.TryGetValue(item.SkillId, out var support) &&
                        (support.target == "all_allies" || support.target == "all_enemies" || support.target == "self"))
                    {
                        bool allies = support.target == "all_allies" || support.target == "self";
                        if (support.target == "self") primary = views[item.SourceId].Actor;
                        targetEvents = actionEvents.Where(next => next.SourceId == item.SourceId && next.TargetId != null &&
                            (next.Kind == BattleFlowEventKind.StatusApplied || next.Kind == BattleFlowEventKind.ReadinessChanged ||
                                (next.Kind == BattleFlowEventKind.StatusRemoved && next.TargetId != item.SourceId)) &&
                            (Snapshot.GetUnit(next.TargetId).Slot.Side == sourceSide) == allies).ToArray();
                    }
                    var targets = targetEvents.Select(next => next.TargetId).Where(id => id != null)
                        .Distinct(StringComparer.Ordinal).Select(id => views[id].Actor).ToArray();
                    if (item.TargetId != null && views[item.TargetId].DeathStarted &&
                        skills.TryGetValue(item.SkillId, out var resurrection) && resurrection.target == "one_ally_including_dead")
                        views[item.TargetId].PrepareRevivalTarget();
                    var timing = skillPresentation.Begin(item.PresentationId, views[item.SourceId].Actor, primary, targets);
                    presentedSkillId = item.SkillId;
                    bool callsAlly = item.Origin == BattleActionOrigin.Primary &&
                        playback.Skip(actionEvents.Length).FirstOrDefault()?.Origin == BattleActionOrigin.DualAttack;
                    voice.PlaySkill(string.IsNullOrEmpty(item.PresentationId) ? item.SkillId : item.PresentationId,
                        callsAlly, item.Origin == BattleActionOrigin.DualAttack);
                    actionElapsed = 0f;
                    impactSeconds = timing.ImpactSeconds;
                    lastDamageImpact = impactSeconds;
                    hitTimes = skillPresentation.HitTimes.ToArray();
                    float lastHit = hitTimes.Length == 0 ? impactSeconds : hitTimes[hitTimes.Length - 1];
                    actionDuration = Mathf.Max(timing.Duration, lastHit + 0.15f);
                    skills.TryGetValue(item.SkillId, out var skill);
                    bool alternate = skill != null && !string.IsNullOrEmpty(skill.speedUpPresentationId) &&
                        string.Equals(item.PresentationId, skill.speedUpPresentationId, StringComparison.Ordinal);
                    string name = skill != null ? Label(SkillNameKey(skill, alternate)) :
                        item.SkillId == BattlePreviewFactory.SlimeSkillId ? "史莱姆攻击" : item.SkillId;
                    var caster = Snapshot.GetUnit(item.SourceId);
                    var icon = skill == null ? null : SkillIcon(skill, alternate);
                    banner.text = "";
                    skillBanner.Show(Label(caster.NameKey), name, icon);
                    break;
                case BattleFlowEventKind.BarrierAbsorbed:
                    pendingAbsorption = item;
                    break;
                case BattleFlowEventKind.StatusApplied:
                    ScheduleFeedback(Mathf.Max(lastDamageImpact, LastFeedbackAt(item.TargetId)),
                        item.TargetId, () => views[item.TargetId].ApplyStatus(item.Status));
                    break;
                case BattleFlowEventKind.StatusDurationChanged:
                    ScheduleFeedback(AfterLastHitAt(item.TargetId), item.TargetId,
                        () => views[item.TargetId].UpdateStatusDuration(item.Status));
                    break;
                case BattleFlowEventKind.StatusRemoved:
                    // Absorb emits this just before Damage when the last shield point is used.
                    // Damage will remove the icon and aura at the hit that consumes it.
                    if (item.StatusKind.HasValue &&
                        (item.StatusKind != BattleStatusKind.Barrier || !MatchesAbsorption(item)))
                    {
                        // Turn-end expiry keeps the marker visible through the attacking unit's last hit.
                        float at = item.SourceId == item.TargetId ? AfterLastHitAt(item.TargetId) : LastFeedbackAt(item.TargetId);
                        ScheduleFeedback(at, item.TargetId, () =>
                        {
                            var view = views[item.TargetId];
                            if (item.Status != null) view.RemoveStatusInstance(item.Status);
                            else view.RemoveStatus(item.StatusKind.Value);
                        });
                    }
                    break;
                case BattleFlowEventKind.Damage:
                    int absorbed = MatchesAbsorption(item) ? pendingAbsorption.Amount : 0;
                    pendingAbsorption = null;
                    if (IsPeriodicDamage(item)) ShowPeriodicDamage(item, absorbed);
                    else ScheduleDamage(item, absorbed);
                    break;
                case BattleFlowEventKind.InjuryApplied:
                    ScheduleFeedback(AfterLastHitAt(item.TargetId), item.TargetId, () =>
                    {
                        var view = views[item.TargetId];
                        view.SetHealthCapacity(Snapshot.GetUnit(item.TargetId).Stats.MaxHealth, item.MaximumHealth.Value);
                    });
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
                case BattleFlowEventKind.SoulChanged:
                    if (item.Side == BattleSide.Ally) soulGauge.Show(item.Amount);
                    break;
                case BattleFlowEventKind.TurnStarted:
                    presentingNextTurn = true;
                    // The event can be read before the preceding skill's queued visual hits.
                    if (pendingFeedback.Count == 0) BeginDamageTurn(item.TurnNumber);
                    else ScheduleFeedback(pendingFeedback[pendingFeedback.Count - 1].At, null,
                        () => BeginDamageTurn(item.TurnNumber));
                    break;
                case BattleFlowEventKind.ResourceChanged:
                    // The final snapshot applies next-turn gains after the current skill's tail.
                    if (!presentingNextTurn)
                    {
                        var resourceView = views[item.TargetId];
                        float resourceAt = Mathf.Max(lastDamageImpact, LastFeedbackAt(item.TargetId));
                        ScheduleFeedback(resourceAt, item.TargetId, () => resourceView.SetResource(item.Amount));
                    }
                    break;
                case BattleFlowEventKind.ReadinessChanged:
                    ScheduleFeedback(LastFeedbackAt(item.TargetId), item.TargetId,
                        () => turnHud.SetReadiness(item.TargetId, item.Amount));
                    break;
                case BattleFlowEventKind.TurnEnded:
                    views[item.SourceId].Actor.SetTurnReady(false);
                    break;
                case BattleFlowEventKind.UnitRevived:
                    ScheduleFeedback(Mathf.Max(lastDamageImpact, LastFeedbackAt(item.TargetId)), item.TargetId, () =>
                    {
                        var view = views[item.TargetId];
                        view.Show(true);
                        view.SetHealth(item.RemainingHealth.Value, item.MaximumHealth.Value);
                        turnHud.RestoreUnit(item.TargetId);
                    });
                    break;
                case BattleFlowEventKind.UnitDefeated:
                    ScheduleFeedback(LastFeedbackAt(item.TargetId), item.TargetId, () =>
                    {
                        turnHud.RemoveUnit(item.TargetId);
                        views[item.TargetId].BeginDeath();
                    });
                    break;
                case BattleFlowEventKind.EffectRejected:
                    string feedbackKey = item.EffectRejection switch
                    {
                        BattleEffectRejection.Resisted => "battle.feedback.effect_resisted",
                        BattleEffectRejection.Blocked => "battle.feedback.effect_blocked",
                        BattleEffectRejection.HealingPrevented => "battle.feedback.healing_prevented",
                        _ => null
                    };
                    if (feedbackKey != null)
                        ScheduleFeedback(Mathf.Max(lastDamageImpact, LastFeedbackAt(item.TargetId)), item.TargetId, () =>
                        {
                            var view = views[item.TargetId];
                            turnHud.ShowStatusFeedback(Label(feedbackKey), view.FeedbackAnchor);
                        });
                    break;
                case BattleFlowEventKind.TurnSkipped:
                    turnHud.SetReadiness(item.SourceId, 0);
                    banner.text = $"{Label(Snapshot.GetUnit(item.SourceId).NameKey)} · 眩晕";
                    break;
            }
        }

        private bool MatchesAbsorption(BattleFlowEvent item) =>
            pendingAbsorption != null && pendingAbsorption.SourceId == item.SourceId &&
            pendingAbsorption.TargetId == item.TargetId;

        private static bool IsPeriodicDamage(BattleFlowEvent item) =>
            item.Kind == BattleFlowEventKind.Damage && (item.StatusKind == BattleStatusKind.Poison ||
                item.StatusKind == BattleStatusKind.Burn || item.StatusKind == BattleStatusKind.Bleed);

        private void ShowPeriodicDamage(BattleFlowEvent item, int absorbed)
        {
            var view = views[item.TargetId];
            if (absorbed > 0) view.AbsorbShield(absorbed);
            view.ApplyDamageHit(item.Amount, item.MaximumHealth ?? Snapshot.GetUnit(item.TargetId).MaximumHealth, false);
            turnHud.PlayReadinessHit(item.TargetId);
            turnHud.ShowPeriodicDamage(item, view.FeedbackAnchor, view.Actor.Bounds.center, camera);
            if (!HasLaterTurnDamage(item.TurnNumber)) FinishDamageSequence();
        }

        private void ScheduleDamage(BattleFlowEvent item, int absorbed)
        {
            var view = views[item.TargetId];
            var unit = Snapshot.GetUnit(item.TargetId);
            // The hit clock drives individual reactions, shield consumption and damage numbers.
            // Present the accepted HP loss across the same authored hit sequence.
            var hits = BattleDamageSequence.Build(item.Amount + absorbed, hitTimes, impactSeconds);
            bool closesTrail = !HasLaterTurnDamage(item.TurnNumber);
            for (int index = 0; index < hits.Length; index++)
            {
                var hit = hits[index];
                lastDamageImpact = hit.At;
                bool first = index == 0;
                bool releaseTrail = closesTrail && index == hits.Length - 1;
                int shieldLoss = Math.Min(absorbed, hit.Amount);
                int healthLoss = hit.Amount - shieldLoss;
                absorbed -= shieldLoss;
                ScheduleFeedback(hit.At, item.TargetId, () =>
                {
                    if (first) voice.PlayHit(unit.DefinitionId, presentedSkillId);
                    // Shield-only and fatal hits still react. Preserve authored NODE_ANI poses.
                    if (!skillPresentation.OwnsNodeAnimationPose(view.Actor))
                        view.Actor.PlayBattleAnimation("attacked");
                    if (shieldLoss > 0) view.AbsorbShield(shieldLoss);
                    if (item.HitKind != BattleHitKind.Miss || healthLoss > 0 || shieldLoss > 0)
                    {
                        view.ApplyDamageHit(healthLoss, item.MaximumHealth ?? unit.MaximumHealth, false);
                        turnHud.PlayReadinessHit(item.TargetId);
                    }
                    if (releaseTrail) FinishDamageSequence();
                    var bounds = view.Actor.Bounds;
                    var world = bounds.center;
                    world.y = bounds.min.y + 0.27f * bounds.size.y;
                    turnHud.ShowDamageHit(item.TargetId, healthLoss, item.HitKind, view.FeedbackAnchor, world, camera);
                });
            }
        }

        private bool HasLaterTurnDamage(int turn)
        {
            foreach (var next in playback)
            {
                if (next.TurnNumber != turn) break;
                if (next.Kind == BattleFlowEventKind.Damage) return true;
            }
            return false;
        }

        private void BeginDamageTurn(int turn)
        {
            if (turn <= damageTurn) return;
            damageTurn = turn;
            foreach (var view in views.Values) view.BeginDamageTurn();
        }

        private void FinishDamageSequence()
        {
            foreach (var view in views.Values) view.FinishDamageSequence();
        }

        private void ShowHealing(BattleFlowEvent item)
        {
            var view = views[item.TargetId];
            view.AdjustHealth(item.Amount, item.MaximumHealth ?? Snapshot.GetUnit(item.TargetId).MaximumHealth);
            if (item.Amount > 0)
                turnHud.ShowImpact(item, view.FeedbackAnchor, view.Actor.Bounds.center, camera);
        }

        private float LastFeedbackAt(string targetId)
        {
            int index = pendingFeedback.FindLastIndex(cue => cue.TargetId == targetId);
            return index < 0 ? impactSeconds : pendingFeedback[index].At;
        }

        private float AfterLastHitAt(string targetId)
        {
            float last = LastFeedbackAt(targetId);
            return hitTimes.Length == 0 ? last : Mathf.Max(last, hitTimes[hitTimes.Length - 1]);
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
            using var sample = RefreshMarker.Auto();
            foreach (var state in Snapshot.Units)
            {
                var view = views[state.InstanceId];
                view.Show(state.IsAlive);
                view.Actor.SetTurnReady(state.IsAlive && state.InstanceId == Snapshot.CurrentActorId);
                view.SetHealthCapacity(state.Stats.MaxHealth, state.MaximumHealth);
                view.SetHealth(state.CurrentHealth, state.MaximumHealth);
                view.RefreshStatuses(state);
            }
            BeginDamageTurn(Snapshot.TurnNumber);
            soulGauge.Show(Snapshot.GetSoul(BattleSide.Ally));
            turnHud.Refresh(Snapshot);
            var actor = Actor;
            displayedActor = actor;
            if (actor?.IsAlive == true && Snapshot.Outcome == BattleOutcome.InProgress)
            {
                var ready = (Snapshot.TurnNumber, actor.InstanceId);
                if (ready != lastReadyVoice)
                {
                    lastReadyVoice = ready;
                    voice.PlayTurnReady(actor.DefinitionId);
                }
            }
            SetPlayerControlsVisible(actor?.Slot.Side == BattleSide.Ally && Snapshot.Outcome == BattleOutcome.InProgress &&
                !ai.IsForcedAction(Snapshot));
            if (actor != null)
            {
                hud.Require<GameText>("activeName").text = Label(actor.NameKey);
                hud.Require<GameText>("activeHp").text = $"{actor.CurrentHealth}/{actor.MaximumHealth}";
                hud.Require<Image>("activeHpFill").fillAmount = actor.CurrentHealth / (float)actor.MaximumHealth;
                sprites.TryGetValue(actor.DefinitionId + "_su", out var actorPortrait);
                activePortrait.Show(actorPortrait);
                SetActorArt("Element", "cm_icon_prom" + actor.Element.ToString().ToLowerInvariant());
                string role = classes.TryGetValue(actor.DefinitionId, out var heroClass) ? heroClass : "";
                SetActorArt("Class", "cm_icon_role_" + (role switch
                {
                    "soul_weaver" => "manauser",
                    "thief" => "assassin",
                    _ => role
                }));
            }
            if (Snapshot.Outcome != BattleOutcome.InProgress && autoBattle)
            {
                autoBattle = false;
                binder.SetAutoEnabled(false);
            }
            binder.SetAutoInteractable(!IsPaused && Snapshot.Outcome == BattleOutcome.InProgress);
            selected = 0;
            useSoulBurn = false;
            lastClickedSkill = -1;
            binder.SetSimulationSelection(CanAct ? selected : -1);
            int index = 0;
            foreach (var button in SkillButtons())
            {
                var image = (Image)button.targetGraphic;
                if (TrySkill(index, out var skill) && SkillIcon(skill, UsesSpeedVariant(skill, actor)) is Sprite icon)
                    image.sprite = icon;
                int turns = TrySkill(index, out skill) ? actor.GetCooldown(skill.id) : 0;
                button.interactable = CanAct && skill != null && IsSkillAvailable(skill);
                bool passive = skill?.target == "passive";
                turnHud.SetSkillStyle(index, passive);
                image.color = button.interactable || turns > 0 || passive ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
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
                CloseInspection();
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
            skillBanner.Hide();
            banner.text = Snapshot.Outcome switch
            {
                BattleOutcome.AllyVictory => "战斗胜利",
                BattleOutcome.EnemyVictory => "战斗失败",
                BattleOutcome.Draw => "战斗平局",
                _ when Actor?.Slot.Side == BattleSide.Enemy => "敌方行动",
                _ when ai.IsForcedAction(Snapshot) => Label("battle.prompt.forced_action"),
                _ when autoBattle => Label("battle.auto.active"),
                _ when TrySkill(selected, out var skill) => $"{Label(SkillNameKey(skill, UsesSpeedVariant(skill, Actor)))} · {(useSoulBurn ? "燃魂 · " : "")}点击目标",
                _ => "选择技能"
            };
        }

        private bool TrySkill(int index, out BattleCatalogData.SkillRow skill)
        {
            skill = null;
            return Actor != null && index >= 0 && index < Actor.Form.SkillIds.Count &&
                skills.TryGetValue(Actor.Form.SkillIds[index], out skill);
        }

        private static bool UsesSpeedVariant(BattleCatalogData.SkillRow skill, BattleUnitSnapshot unit) =>
            unit?.HasSpeedBuff == true && !string.IsNullOrEmpty(skill.speedUpPresentationId);

        private static string SkillNameKey(BattleCatalogData.SkillRow skill, bool alternate) =>
            alternate && !string.IsNullOrEmpty(skill.speedUpNameKey) ? skill.speedUpNameKey : skill.nameKey;

        private Sprite SkillIcon(BattleCatalogData.SkillRow skill, bool alternate)
        {
            if (skill.target == "passive" && sprites.TryGetValue($"pa_{skill.heroId}_{skill.slot}", out var passive))
                return passive;
            string path = alternate && !string.IsNullOrEmpty(skill.speedUpIconAsset) ? skill.speedUpIconAsset : skill.iconAsset;
            return sprites.GetValueOrDefault(Path.GetFileNameWithoutExtension(path));
        }

        private void InspectSkill(BattleUnitSnapshot unit, int slot, Image image, bool detailRow)
        {
            if (inspection == null || IsPaused || cinematicVisible || unit == null || slot >= unit.Form.SkillIds.Count ||
                !skills.TryGetValue(unit.Form.SkillIds[slot], out var skill)) return;

            // Holding consumes the gesture and also breaks an existing quick-cast pair.
            lastClickedSkill = -1;
            string cooldown = skill.cooldownTurns > 0
                ? string.Format(Label("battle.inspect.cooldown"), skill.cooldownTurns)
                : null;
            string soul = skill.soulGain > 0 ? string.Format(Label("battle.inspect.soul_gain"), skill.soulGain) : null;
            bool burn = skill.soulBurnCost > 0;
            string upgrades = detailRow ? null : inspectionContent.SkillEnhancements(skill);
            bool alternate = UsesSpeedVariant(skill, unit);
            var icon = SkillIcon(skill, alternate);
            var primary = new InfoPanelBinder.Card(icon ? icon : image.sprite, Label(SkillNameKey(skill, alternate)),
                alternate && !string.IsNullOrEmpty(skill.speedUpDescriptionKey) ? Label(skill.speedUpDescriptionKey) :
                    inspectionContent.SkillDescription(skill), soul: soul, cooldown: cooldown,
                burnTitle: burn ? Label("battle.inspect.burn_header") : null,
                burnCost: burn ? string.Format(Label("battle.inspect.soul_burn"), skill.soulBurnCost) : null,
                burnDescription: burn ? inspectionContent.SkillBurnDescription(skill) : null,
                upgrades: string.IsNullOrEmpty(upgrades) ? null : "<color=#ff7800>" +
                    Label("battle.inspect.enhancement_header") + "</color>\n" + upgrades);
            var (title, description, status) = inspectionContent.SkillAuxiliaryEffect(skill);
            InfoPanelBinder.Card? related = null;
            if (!string.IsNullOrEmpty(title))
            {
                if (status is BattleStatusKind kind) related = StatusCard(kind, title, description, null);
                else related = new InfoPanelBinder.Card(null, title, description,
                    new Color32(64, 45, 35, 237), new Color32(198, 156, 108, 255));
            }
            inspection.Show(image.rectTransform, primary, related, detailRow, image.GetComponent<GameLongPress>());
        }

        private void InspectStatus(BattleStatusSnapshot status, Image image)
        {
            if (inspection == null || IsPaused || cinematicVisible) return;
            lastClickedSkill = -1;
            inspection.Show(image.rectTransform, StatusCard(status.Kind, inspectionContent.StatusName(status.Kind),
                inspectionContent.StatusDescription(status.Kind), image.sprite), preferRight: true,
                hold: image.GetComponent<GameLongPress>());
        }

        private InfoPanelBinder.Card StatusCard(BattleStatusKind kind, string title, string description, Sprite icon)
        {
            bool debuff = statusArt.TryGet(kind, out var art) && !art.IsBuff;
            return new InfoPanelBinder.Card(icon ? icon : art.Icon, title, description,
                debuff ? new Color32(92, 41, 39, 242) : new Color32(27, 67, 104, 255),
                debuff ? new Color32(255, 153, 146, 255) : new Color32(101, 190, 255, 255));
        }

        private void CloseInspection()
        {
            inspection?.Hide();
            foreach (var hold in skillHolds)
                if (hold) hold.Cancel();
        }

        private int BurnSlot()
        {
            for (int i = 0; i < 3; i++)
                if (TrySkill(i, out var skill) && IsSkillAvailable(skill, true))
                    return i;
            return -1;
        }

        private bool IsSkillAvailable(BattleCatalogData.SkillRow skill, bool burn = false) =>
            ai.IsSkillAvailable(Snapshot, skill.id, burn);

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
            CloseInspection();
            inspection?.Dispose();
            foreach (var hold in skillHolds)
                if (hold) hold.Held = null;
            playback.Clear();
            pendingFeedback.Clear();
            pendingAbsorption = null;
            skillPresentation.Dispose();
            foreach (var tail in recoveryStages) tail.Stage.Dispose();
            recoveryStages.Clear();
            foreach (var view in views.Values) view.Actor.SetPaused(false);
            voice.Dispose();
            turnHud.Dispose();
            soulGauge.Dispose();
            turnEntryEffects.Dispose();
            foreach (var view in views.Values) view.Restore();
            skillBanner.Dispose();
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
            private readonly CanvasGroup markerGroup;
            private readonly float originalMarkerAlpha;
            private readonly Image elementBadge;
            private readonly BattleUnitStatusPresentation status;
            private readonly int resourceMaximum;
            private readonly BattleUnitPresentationProfile profile;
            private readonly IReadOnlyDictionary<string, Texture2D> deathTextures;
            private BattleDeathPresentation death;
            private bool warnedAnchor, deathStarted;
            private Bounds defeatTargetBounds;
            private bool shown = true;
            private long presentedHealth;
            private long presentedShield;
            private int maximumHealth;
            private readonly BattleStatusIcons statusIcons;
            private readonly BattleShieldAura shieldAura, invincibleAura;
            private bool invincible;
            private readonly BattleUnitLevelBadge levelBadge;
            private static readonly int BodyOpacity = Shader.PropertyToID("_PresentationOpacity");
            private const float StealthOpacity = .55f;
            private MaterialPropertyBlock stealthBlock;
            private Renderer stealthBody;
            private float originalStealthOpacity;

            public View(Transform slot, BattlePreviewActor actor, GameObject marker, Camera camera, BattleUnitPresentationProfile profile,
                IReadOnlyDictionary<string, Texture2D> deathTextures, BattleStatusArt statusArt,
                BattleShieldAura.Definition auraDefinition, BattleShieldAura.Definition invincibleDefinition,
                Sprite element, BattleResourceSnapshot resource,
                Action<BattleStatusSnapshot, Image> inspectStatus)
            {
                this.slot = slot;
                this.marker = marker;
                markerGroup = marker.GetComponent<CanvasGroup>();
                if (!markerGroup) markerGroup = marker.AddComponent<CanvasGroup>();
                originalMarkerAlpha = markerGroup.alpha;
                Actor = actor;
                Health = marker.transform.Find("HP").GetComponent<Image>();
                elementBadge = marker.transform.Find("Element").GetComponent<Image>();
                FeedbackAnchor = (RectTransform)marker.transform.Find("Frame");
                this.profile = profile; this.deathTextures = deathTextures;
                status = new BattleUnitStatusPresentation((RectTransform)marker.transform, Health, Actor.transform,
                    StatusAnchor, camera, profile ? profile.HealthDivider : null,
                    profile ? profile.HealthSections : 10);
                resourceMaximum = resource?.Maximum ?? 0;
                if (profile && resource != null)
                {
                    bool focus = resource.Kind == BattleResourceKind.Focus;
                    status.ConfigureResource(profile.ResourceFrame, focus ? profile.FocusFill : profile.FightingSpiritFill,
                        profile.HealthDivider, focus ? Mathf.Min(resource.Maximum, 10) : 5);
                    SetResource(resource.Amount);
                }
                statusIcons = new BattleStatusIcons(FeedbackAnchor, statusArt,
                    profile ? profile.StatusIconsPerRow : BattleStatusIcons.MaximumColumns, inspectStatus);
                shieldAura = new BattleShieldAura(auraDefinition, Actor.PresentationShadow, Actor.PresentationRenderer);
                invincibleAura = new BattleShieldAura(invincibleDefinition, Actor.PresentationShadow,
                    Actor.PresentationRenderer, "Battle invincibility aura");
                if (element) elementBadge.sprite = element;
                if (profile)
                {
                    status.SetShieldSprite(profile.Shield);
                    status.AnchorOffset = profile.StatusOffset;
                    status.DamageSeconds = profile.DamageSeconds; status.HealSeconds = profile.HealSeconds;
                    status.TrailDelaySeconds = profile.TrailDelaySeconds; status.TrailSeconds = profile.TrailSeconds;
                }
                levelBadge = new BattleUnitLevelBadge(marker.transform.Find("Level").GetComponent<Image>(), profile ? profile.LevelDigits : null);
                try { PrepareDeath(); }
                catch { shieldAura.Dispose(); invincibleAura.Dispose(); statusIcons.Dispose(); status.Dispose(); levelBadge.Dispose(); throw; }
            }
            public BattlePreviewActor Actor { get; }
            public Image Health { get; }
            public RectTransform FeedbackAnchor { get; }
            public Vector3 Center => Actor.Bounds.center;
            public bool Contains(Ray ray, bool allowInactive = false) =>
                slot.gameObject.activeInHierarchy && !deathStarted ? Actor.Bounds.IntersectRay(ray)
                    : allowInactive && deathStarted && defeatTargetBounds.IntersectRay(ray);

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
                var body = Actor.PresentationRenderer;
                UpdateStatusVisibility(body);
                if (marker.activeInHierarchy)
                {
                    status.ScaleAnchor = body.transform;
                    status.LateTick(camera);
                }
                SyncAura(0f);
            }
            private void UpdateStatusVisibility(Renderer body)
            {
                // Source layouts own body masking. Read the current skeleton, never last-frame
                // Renderer.isVisible or the presence of auxiliary particles/shadows.
                bool keepMarker = deathStarted ? !death.BodyComplete : shown;
                bool visible = keepMarker && BattlePreviewActor.IsBodyVisible(body);
                if (marker.activeSelf != visible) marker.SetActive(visible);
            }
            public void TickVisual(float delta)
            {
                status.Tick(delta);
                SyncAura(delta);
                if (death?.IsRunning != true) return;
                Actor.TickDefeatPose(Mathf.Min(delta, death.RemainingBodySeconds));
                death.Tick(delta);
                markerGroup.alpha = originalMarkerAlpha * death.BodyOpacity;
                if (death.BodyComplete && marker.activeSelf) marker.SetActive(false);
                if (death.IsComplete) slot.gameObject.SetActive(false);
            }
            private void PrepareDeath()
            {
                death = new BattleDeathPresentation(Actor.transform, profile ? profile.DeathParticles : null,
                    deathTextures, Actor.PresentationShadow ? Actor.PresentationShadow.GetComponent<Renderer>() : null, Actor.PresentationRenderer);
                try { if (profile) death.ConfigureTimings(profile.DeathDarkenSeconds, profile.DeathHoldSeconds, profile.DeathFadeSeconds); }
                catch { death.Dispose(); death = null; throw; }
            }
            public void BeginDeath()
            {
                if (deathStarted) return;
                defeatTargetBounds = Actor.Bounds;
                defeatTargetBounds.Expand(new Vector3(0.25f, 0.25f, 0.5f));
                deathStarted = true;
                SetStealth(false);
                SetShield(0);
                Actor.BeginDefeatPose();
                Actor.SetPaused(true);
                death.Begin();
            }
            public void PrepareRevivalTarget()
            {
                if (!slot.gameObject.activeSelf) slot.gameObject.SetActive(true);
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
                if (resurrecting)
                {
                    Actor.EndDefeatPose();
                    Actor.SetPaused(false);
                    markerGroup.alpha = originalMarkerAlpha;
                    PrepareDeath();
                }
                UpdateStatusVisibility(Actor.PresentationRenderer);
            }

            public void BeginDamageTurn() => status.BeginDamageTurn(presentedHealth);

            public void FinishDamageSequence() => status.FinishDamageSequence();

            public void ApplyDamageHit(int loss, int maximum, bool lastHit)
            {
                presentedHealth = Math.Max(0L, presentedHealth - loss);
                maximumHealth = maximum;
                // Once HP is exposed, a depleted ward should not cover the new red segment.
                if (loss > 0 && presentedShield == 0) status.UpdateShield(0, maximum, true);
                status.ApplyDamageHit(presentedHealth, maximum, lastHit);
            }

            public void SetHealthCapacity(int original, int effective)
            {
                maximumHealth = effective;
                presentedHealth = Math.Min(presentedHealth, effective);
                status.SetHealthCapacity(original, effective);
            }

            public void AdjustHealth(int delta, int maximum) => SetHealth(presentedHealth + delta, maximum);

            public void SetHealth(long health, int maximum)
            {
                presentedHealth = health;
                maximumHealth = maximum;
                status.UpdateHealth(health, maximum, false);
            }

            public void RefreshStatuses(BattleUnitSnapshot state)
            {
                levelBadge.Show(state.Level);
                statusIcons.Reconcile(state.IsAlive ? state.Statuses : Array.Empty<BattleStatusSnapshot>());
                SetStealth(state.IsAlive && state.GetStatus(BattleStatusKind.Stealth) != null);
                invincible = state.IsAlive && state.GetStatus(BattleStatusKind.Invincible) != null;
                SetShield(state.IsAlive ? state.Barrier : 0);
                if (state.Resource != null) SetResource(state.Resource.Amount);
            }

            public void SetResource(int amount) => status.UpdateResource(amount, resourceMaximum);

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
                if (effect.Kind == BattleStatusKind.Stealth) SetStealth(true);
                if (effect.Kind == BattleStatusKind.Invincible) { invincible = true; SyncAura(0f); }
            }

            public void UpdateStatusDuration(BattleStatusSnapshot effect)
            {
                if (effect != null) statusIcons.UpdateDuration(effect);
            }

            public void RemoveStatus(BattleStatusKind kind)
            {
                if (kind == BattleStatusKind.Barrier) SetShield(0);
                statusIcons.Remove(kind);
                if (kind == BattleStatusKind.Stealth) SetStealth(false);
                if (kind == BattleStatusKind.Invincible) { invincible = false; SyncAura(0f); }
            }

            public void RemoveStatusInstance(BattleStatusSnapshot effect)
            {
                if (effect.InstanceId == 0) RemoveStatus(effect.Kind);
                else statusIcons.Remove(effect.Kind, effect.InstanceId);
            }

            private void SetStealth(bool active)
            {
                var body = active && !deathStarted ? Actor.PresentationRenderer : null;
                if (body == stealthBody) return;
                if (stealthBody) ApplyBodyOpacity(stealthBody, originalStealthOpacity);
                stealthBody = body;
                if (!body) return;
                stealthBlock ??= new MaterialPropertyBlock();
                body.GetPropertyBlock(stealthBlock);
                originalStealthOpacity = stealthBlock.HasFloat(BodyOpacity) ? stealthBlock.GetFloat(BodyOpacity) : 1f;
                ApplyBodyOpacity(body, originalStealthOpacity * StealthOpacity);
            }

            private void ApplyBodyOpacity(Renderer body, float opacity)
            {
                body.GetPropertyBlock(stealthBlock);
                stealthBlock.SetFloat(BodyOpacity, opacity);
                body.SetPropertyBlock(stealthBlock);
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

            private void SyncAura(float delta)
            {
                bool shielded = shown && !deathStarted && presentedShield > 0;
                bool protectedFromDamage = shown && !deathStarted && invincible;
                Vector2? anchor = (shielded || protectedFromDamage) && Actor.TryGetSkeletonAnchor("shadow", out var position)
                    ? position : (Vector2?)null;
                var body = Actor.PresentationRenderer;
                shieldAura.Sync(body, shielded, delta, anchor);
                invincibleAura.Sync(body, protectedFromDamage, delta, anchor);
            }

            public void Restore()
            {
                SetStealth(false);
                death?.Dispose();
                death = null;
                Actor.SetTurnReady(false);
                if (deathStarted)
                {
                    Actor.EndDefeatPose();
                    Actor.SetPaused(false);
                    deathStarted = false;
                }
                status.Dispose();
                shieldAura.Dispose();
                invincibleAura.Dispose();
                statusIcons.Dispose();
                levelBadge.Dispose();
                if (slot) slot.gameObject.SetActive(true);
                RemoveOwnedObject(marker);
            }
        }

    }
}
