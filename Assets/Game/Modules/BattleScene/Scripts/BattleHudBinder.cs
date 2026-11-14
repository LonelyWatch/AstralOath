using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using SpineEffect = AstralOath.Client.BattleScene.BattleHudPreview.SpineEffect;

namespace AstralOath.Client.BattleScene
{
    internal sealed class BattleHudBinder : IDisposable
    {
        // The authored 1280x720 coordinates map to a 1920x1080 Canvas.
        // The legacy HUD Spine exports include a 2.24x attachment scale.
        private const float EffectScale = 1.5f / 2.24f;
        private const float SkillOutlineScale = EffectScale * 0.94f;
        // Skill icons sit 3.5 authoring pixels right and 2.5 below their frames.
        private static readonly Vector3 SkillFrameOffset = new Vector3(-5.25f, 3.75f, 0f);
        private const float BurnSelectionDelay = 0.231f;
        // The preview's acting Ras can Soul Burn S2. Live combat will supply this state.
        private int burnSkillIndex = 1;
        internal BattleSimulationPreview Simulation { get; private set; }

        private readonly BindingScope scope;
        private readonly GameButton startPreview;
        private readonly GameButton pause;
        private readonly GameButton auto;
        private readonly BattleHud.PauseControls pauseControls;
        private readonly GameButton burn;
        private readonly GameButton[] skills;
        private readonly Image[] selectedArtwork;
        private readonly Image burnImage;
        private readonly Sprite burnNormal;
        private readonly Sprite burnPressed;
        private readonly Sprite cancelNormal;
        private readonly Sprite cancelPressed;
        private readonly ISkeletonPlayer buttonEffect;
        private readonly ISkeletonPlayer selectionEffect;
        private readonly ISkeletonPlayer introEffect;
        private readonly ISkeletonPlayer useEffect;
        private readonly Transform particleAnchor;
        private readonly BattleHudParticles[] normalParticles;
        private readonly BattleHudParticles[] burnParticles;
        private readonly BattleHudParticles useParticles;
        private int selected = -1;
        private bool armed;
        private bool onlineBattle, leaving;
        private float pendingBurnSelection = -1f;

        internal BattleHudBinder(BindingMap map, Sprite burnPressed, Sprite cancelNormal, Sprite cancelPressed,
            SpineEffect burnButton, SpineEffect selection, SpineEffect intro, SpineEffect soulUse,
            TextAsset[] particleDefinitions, Texture2D[] particleTextures, UnityAction startBattle, Func<string, string> label, GameObject confirmPrefab = null)
        {
            startPreview = map.Require<GameButton>("startPreview");
            pause = map.Require<GameButton>("pause");
            auto = map.Require<GameButton>("auto");
            burn = map.Require<GameButton>("soulBurn");
            skills = new[] { map.Require<GameButton>("skill1"), map.Require<GameButton>("skill2"), map.Require<GameButton>("skill3") };
            selectedArtwork = new[] { map.Require<Image>("skillSelected1"), map.Require<Image>("skillSelected2"),
                map.Require<Image>("skillSelected3") };
            burnImage = (Image)burn.targetGraphic;
            burnNormal = burnImage.sprite;
            this.burnPressed = burnPressed;
            this.cancelNormal = cancelNormal;
            this.cancelPressed = cancelPressed;
            scope = new BindingScope(CancellationToken.None);

            try
            {
                pauseControls = BattleHud.CreatePauseControls(map, label, confirmPrefab);
                scope.Add(pauseControls.Dispose);
                pause.interactable = false;
                Bind(pause, PauseBattle);
                var autoGraphic = auto.targetGraphic;
                bool autoRaycast = autoGraphic.raycastTarget;
                bool autoInteractable = auto.interactable;
                Color autoColor = autoGraphic.color;
                autoGraphic.raycastTarget = true;
                auto.interactable = false;
                Bind(auto, () => Simulation?.ToggleAuto());
                scope.Add(() =>
                {
                    if (!auto) return;
                    autoGraphic.raycastTarget = autoRaycast;
                    autoGraphic.color = autoColor;
                    auto.interactable = autoInteractable;
                });
                Bind(pauseControls.Continue, ResumeBattle);
                if (startBattle != null) Bind(pauseControls.Restart, startBattle);
                var burnSkill = skills[burnSkillIndex].transform;
                var canvas = map.GetComponentInParent<Canvas>();
                int aboveHud = canvas.sortingOrder + 1;
                buttonEffect = Create(burnButton, burn.transform, "Burn Button", aboveHud + 1);
                selectionEffect = Create(selection, burnSkill, "Skill Selection", aboveHud + 1, SkillOutlineScale);
                selectionEffect.Root.transform.localPosition = SkillFrameOffset;
                introEffect = Create(intro, burnSkill, "Burn Intro", aboveHud + 3);
                introEffect.Root.transform.localPosition = SkillFrameOffset;
                useEffect = Create(soulUse, burn.transform, "Soul Burn Use", aboveHud + 1);

                var source = particleDefinitions.ToDictionary(item => item.name, StringComparer.Ordinal);
                var textures = particleTextures.ToDictionary(item => item.name + ".sct", StringComparer.Ordinal);
                particleAnchor = new GameObject("Skill Selection Particles").transform;
                particleAnchor.SetParent(burnSkill, false);
                particleAnchor.localPosition = SkillFrameOffset;
                scope.Add(() =>
                {
                    if (particleAnchor)
                        UnityEngine.Object.Destroy(particleAnchor.gameObject);
                });
                normalParticles = new[]
                {
                    Particle("hud_icon_select_pati1", particleAnchor, 110, 2),
                    Particle("hud_icon_select_pati2", particleAnchor, -118, 2)
                };
                burnParticles = new[]
                {
                    Particle("soul_burn_loop_pati", particleAnchor, 0, -1),
                    Particle("hud_icon_select_pati3", particleAnchor, 110, 2),
                    Particle("hud_icon_select_pati4", particleAnchor, -118, 2),
                    Particle("soul_burn_pati_01", particleAnchor, 0, 4)
                };
                useParticles = Particle("ui_soul_burn_use_pati", burn.transform, 0, 2);

                scope.Add(() =>
                {
                    if (burn && burnImage)
                    {
                        SetBurnArtwork(false);
                        burnImage.canvasRenderer.SetAlpha(1f);
                    }
                });
                scope.Add(() =>
                {
                    foreach (var artwork in selectedArtwork)
                        if (artwork)
                            artwork.gameObject.SetActive(false);
                    foreach (var skill in skills)
                        if (skill)
                        {
                            skill.interactable = true;
                            skill.targetGraphic.color = Color.white;
                        }
                });
                SetBurnArtwork(false);
                Play(buttonEffect, "burn_intro", false);
                UpdateBurnButtonVisual();
                if (startBattle != null)
                {
                    Bind(startPreview, startBattle);
                    scope.Add(() =>
                    {
                        if (startPreview)
                            startPreview.gameObject.SetActive(false);
                    });
                    startPreview.gameObject.SetActive(true);
                }
                Bind(burn, () =>
                {
                    if (!burn.interactable) return;
                    if (Simulation == null) ToggleBurn();
                    else Simulation.ToggleBurn();
                });
                for (int i = 0; i < skills.Length; i++)
                {
                    int index = i;
                    Bind(skills[i], () =>
                    {
                        if (!skills[index].interactable) return;
                        if (Simulation == null) SelectSkill(index);
                        else Simulation.ChooseSkill(index);
                    });
                }

                BattleHudParticles Particle(string name, Transform anchor, float y, int layer)
                {
                    // Map CFX z=-1 to just above the HUD, behind the other effect layers.
                    var effect = new BattleHudParticles(source[name + ".particle"], textures, anchor,
                        new Vector2(0, y), EffectScale, aboveHud + Mathf.Max(0, layer));
                    scope.Add(effect.Dispose);
                    return effect;
                }
            }
            catch
            {
                scope.Dispose();
                throw;
            }
        }

        internal void Tick(float delta)
        {
            if (Simulation?.IsPaused == true) return;
            buttonEffect.Tick(delta);
            if (buttonEffect.CurrentAnimation == "burn_intro" && buttonEffect.Time >= buttonEffect.Duration)
                Play(buttonEffect, "burn_loop", true);
            UpdateBurnButtonVisual();

            TickOneShot(introEffect, delta);
            TickOneShot(useEffect, delta);
            if (selectionEffect.Root.activeSelf)
                selectionEffect.Tick(delta);

            if (pendingBurnSelection >= 0f)
            {
                pendingBurnSelection -= delta;
                if (pendingBurnSelection <= 0f)
                {
                    pendingBurnSelection = -1f;
                    ShowSelection(true);
                }
            }

            foreach (var effect in normalParticles)
                effect.Tick(delta);
            foreach (var effect in burnParticles)
                effect.Tick(delta);
            useParticles.Tick(delta);
        }

        internal void AttachSimulation(BattleSimulationPreview simulation, bool online = false)
        {
            Simulation = simulation;
            startPreview.gameObject.SetActive(false);
            onlineBattle = online;
            pause.interactable = !online;
            if (online)
            {
                pause.gameObject.SetActive(false);
                auto.gameObject.SetActive(false);
            }
        }

        internal void ShowOnlineReturn()
        {
            if (!onlineBattle || pause.gameObject.activeSelf) return;
            pause.gameObject.SetActive(true);
            pause.interactable = true;
        }

        internal void SetAutoInteractable(bool interactable) => auto.interactable = interactable;

        internal void SetAutoEnabled(bool enabled) =>
            auto.targetGraphic.color = enabled ? new Color32(255, 211, 112, 255) : Color.white;

        internal void PauseBattle()
        {
            if (onlineBattle)
            {
                if (!leaving) LeaveOnlineBattle();
                return;
            }
            if (Simulation == null || Simulation.IsPaused) return;
            Simulation.SetPaused(true);
            pauseControls.Show(true);
        }

        private async void LeaveOnlineBattle()
        {
            leaving = true;
            try { await BattleOnlineLaunch.ExitAsync(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                leaving = false;
            }
        }

        internal void ResumeBattle()
        {
            if (Simulation == null) return;
            Simulation.SetPaused(false);
            pauseControls.Show(false);
        }

        internal void SetSimulationSelection(int index)
        {
            if (armed) SetArmed(false);
            SelectSkill(index);
        }

        internal void ArmSimulationBurn(int index)
        {
            burnSkillIndex = index;
            introEffect.Root.transform.SetParent(skills[index].transform, false);
            ToggleBurn();
        }

        internal void CancelSimulationBurn() => ToggleBurn();

        private void ToggleBurn()
        {
            if (armed)
            {
                SetArmed(false);
                useEffect.Root.SetActive(false);
                useParticles.Stop();
                ShowSelection(false);
                return;
            }
            ClearSelection();
            selected = burnSkillIndex;
            SetArmed(true);
            Play(useEffect, "animation", false);
            useParticles.Play();
            Play(introEffect, "on_intro", false);
            pendingBurnSelection = BurnSelectionDelay;
        }

        private void SelectSkill(int index)
        {
            if (armed) return;
            ClearSelection();
            selected = index;
            ShowSelection(false);
        }

        private void SetArmed(bool value)
        {
            armed = value;
            SetBurnArtwork(value);
            if (Simulation == null)
                for (int index = 0; index < skills.Length; index++)
                {
                    skills[index].interactable = !value || index == burnSkillIndex;
                    skills[index].targetGraphic.color = skills[index].interactable
                        ? Color.white : new Color(0.45f, 0.45f, 0.45f, 1f);
                }
            Play(buttonEffect, value ? "cancel_loop" : "burn_loop", true);
            if (value)
                return;

            pendingBurnSelection = -1f;
            introEffect.Root.SetActive(false);
            selectionEffect.Root.SetActive(false);
            foreach (var effect in burnParticles)
                effect.Stop();
        }

        private void SetBurnArtwork(bool cancel)
        {
            // Reset a pressed override before swapping the normal and pressed artwork.
            burnImage.overrideSprite = null;
            burnImage.sprite = cancel ? cancelNormal : burnNormal;
            var state = burn.spriteState;
            state.pressedSprite = cancel ? cancelPressed : burnPressed;
            burn.spriteState = state;
        }

        private void UpdateBurnButtonVisual()
        {
            // The Spine animation already contains the button and its caption.
            // Keep the static Image as the raycast target and reveal its art only while pressed.
            bool pressed = burnImage.overrideSprite == burn.spriteState.pressedSprite;
            burnImage.canvasRenderer.SetAlpha(pressed ? 1f : 0f);
            buttonEffect.Root.SetActive(!pressed && (Simulation == null || burn.interactable));
        }

        private void ShowSelection(bool burnSelection)
        {
            int index = burnSelection ? burnSkillIndex : selected;
            if (index < 0)
                return;

            Transform anchor = skills[index].transform;
            selectionEffect.Root.transform.SetParent(anchor, false);
            particleAnchor.SetParent(anchor, false);
            if (!burnSelection)
                selectedArtwork[index].gameObject.SetActive(true);
            Play(selectionEffect, burnSelection ? "burn" : "normal", true);
            foreach (var effect in burnSelection ? burnParticles : normalParticles)
                effect.Play();
        }

        private void ClearSelection()
        {
            if (selected >= 0)
                selectedArtwork[selected].gameObject.SetActive(false);
            selectionEffect.Root.SetActive(false);
            foreach (var effect in normalParticles)
                effect.Stop();
        }

        private ISkeletonPlayer Create(SpineEffect effect, Transform parent, string name, int sortingOrder, float scale = EffectScale)
        {
            var player = effect.Create(parent, name, scale, sortingOrder);
            scope.Add(player.Dispose);
            return player;
        }

        private void Bind(GameButton button, UnityAction listener)
        {
            button.onClick.AddListener(listener);
            scope.Add(() =>
            {
                if (button)
                    button.onClick.RemoveListener(listener);
            });
        }

        private static void Play(ISkeletonPlayer player, string animation, bool loop)
        {
            player.Play(animation, loop);
            player.Root.SetActive(true);
        }

        private static void TickOneShot(ISkeletonPlayer player, float delta)
        {
            if (!player.Root.activeSelf)
                return;
            player.Tick(delta);
            if (player.Time >= player.Duration)
                player.Root.SetActive(false);
        }

        public void Dispose() => scope.Dispose();
    }
}
