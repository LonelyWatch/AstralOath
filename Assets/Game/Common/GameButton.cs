using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent, AddComponentMenu("UI/Game Button")]
    public sealed class GameButton : Button
    {
        [SerializeField] private AudioSource clickAudio;
        [SerializeField] private GameUiFeedback.Sound feedbackSound;
        [SerializeField] private bool blockedFeedback;
        [Serializable] private struct PressDecoration
        {
            public Image image;
            public Sprite pressed;
        }
        [SerializeField] private PressDecoration[] pressDecorations = Array.Empty<PressDecoration>();
        [SerializeField] private RectTransform pressedContent;
        [SerializeField] private Vector2 pressedOffset;
        private Vector2 restingPosition;
        private bool feedbackPressed;
        private List<(Graphic graphic, Color tint)> pressedGraphics;
        private static readonly Color PressTint = new Color(.7f, .7f, .7f, 1f);
        private BindingScope current;
        private bool retainBindingWhileHidden;
        private bool dispatching;

        public GameUiFeedback.Sound FeedbackSound { get => feedbackSound; set => feedbackSound = value; }
        public bool BlockedFeedback { get => blockedFeedback; set => blockedFeedback = value; }

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !CanActivate()) return;
            if (TryGetComponent<GameLongPress>(out var hold) && hold.ConsumedClick(eventData)) return;
            PlayClickSound();
            dispatching = true;
            try { base.OnPointerClick(eventData); }
            finally { dispatching = false; }
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (!CanActivate()) return;
            PlayClickSound();
            dispatching = true;
            try { base.OnSubmit(eventData); }
            finally { dispatching = false; }
        }

        private bool CanActivate()
        {
            if (!IsActive()) return false;
            if (IsInteractable()) return true;
            if (blockedFeedback) GameUiFeedback.Play(this, GameUiFeedback.Sound.Blocked);
            return false;
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            bool pressed = state == SelectionState.Pressed;
            if (pressed && transition == Transition.ColorTint && targetGraphic)
                targetGraphic.CrossFadeColor(colors.pressedColor * colors.colorMultiplier * PressTint, 0f, true, true);
            SetPressFeedback(pressed);
        }

        protected override void InstantClearState()
        {
            SetPressFeedback(false);
            base.InstantClearState();
        }

        private void SetPressFeedback(bool pressed)
        {
            if (feedbackPressed == pressed) return;
            feedbackPressed = pressed;
            if (pressedContent)
            {
                if (pressed) restingPosition = pressedContent.anchoredPosition;
                pressedContent.anchoredPosition = pressed ? restingPosition + pressedOffset : restingPosition;
            }
            foreach (var decoration in pressDecorations)
                if (decoration.image) decoration.image.overrideSprite = pressed ? decoration.pressed : null;
            if (pressed)
            {
                pressedGraphics ??= new List<(Graphic, Color)>();
                TintOwnedGraphics(transform);
            }
            else if (pressedGraphics != null)
            {
                foreach (var (graphic, tint) in pressedGraphics)
                    if (graphic) graphic.canvasRenderer.SetColor(tint);
                pressedGraphics.Clear();
            }
        }

        private void TintOwnedGraphics(Transform node)
        {
            if (node != transform && node.TryGetComponent<Selectable>(out var nested) &&
                nested.isActiveAndEnabled && nested.IsInteractable()) return;
            if (node.TryGetComponent<Graphic>(out var graphic) && !HasAuthoredPressedTint(graphic))
            {
                var renderer = graphic.canvasRenderer;
                var tint = renderer.GetColor();
                pressedGraphics.Add((graphic, tint));
                renderer.SetColor(tint * PressTint);
            }
            for (int i = 0; i < node.childCount; i++)
                TintOwnedGraphics(node.GetChild(i));
        }

        private bool HasAuthoredPressedTint(Graphic graphic)
        {
            if (graphic == targetGraphic && (transition == Transition.ColorTint ||
                transition == Transition.SpriteSwap && spriteState.pressedSprite && graphic is Image image &&
                image.sprite != spriteState.pressedSprite)) return true;
            foreach (var decoration in pressDecorations)
                if (decoration.image == graphic && decoration.pressed && decoration.image.sprite != decoration.pressed) return true;
            return false;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            onClick.AddListener(PlayClickSound);
        }

        private void PlayClickSound()
        {
            if (!dispatching && IsActive() && IsInteractable()) GameUiFeedback.Play(this, feedbackSound, clickAudio);
        }

        // A header can be covered by an exclusive child page. Its navigation action belongs
        // to the window scope, including a return already in flight when it is covered.
        public void BindBack(BindingScope owner, Func<Task> navigate)
        {
            if (navigate == null) throw new ArgumentNullException(nameof(navigate));
            Bind(owner, _ => navigate(), retainWhileHidden: true);
        }

        public void Bind(BindingScope owner, Func<CancellationToken, Task> handler) =>
            Bind(owner, handler, retainWhileHidden: false);

        private void Bind(BindingScope owner, Func<CancellationToken, Task> handler, bool retainWhileHidden)
        {
            Unbind();
            owner.Token.ThrowIfCancellationRequested();
            var binding = new BindingScope(owner.Token);
            current = binding;
            retainBindingWhileHidden = retainWhileHidden;
            Button button = this;
            bool wasInteractable = button.interactable;
            bool busy = false;

            async void Click()
            {
                if (busy || binding.Token.IsCancellationRequested || !isActiveAndEnabled || !button.IsInteractable())
                    return;
                busy = true;
                button.interactable = false;
                try
                {
                    await handler(binding.Token);
                }
                catch (OperationCanceledException) when (binding.Token.IsCancellationRequested) { }
                catch (Exception error) { Debug.LogException(error, this); }
                finally
                {
                    busy = false;
                    if (current == binding && button)
                        button.interactable = wasInteractable;
                }
            }

            UnityAction listener = Click;
            button.onClick.AddListener(listener);
            binding.Add(() =>
            {
                if (!button)
                    return;
                button.onClick.RemoveListener(listener);
                button.interactable = wasInteractable;
            });
            binding.Add(owner.Add(() =>
            {
                if (current == binding)
                    Unbind();
            }));
        }

        protected override void OnDisable()
        {
            onClick.RemoveListener(PlayClickSound);
            if (clickAudio) clickAudio.Stop();
            try { if (!retainBindingWhileHidden) Unbind(); }
            finally { base.OnDisable(); }
        }

        protected override void OnDestroy()
        {
            try { Unbind(); }
            finally { base.OnDestroy(); }
        }

        private void Unbind()
        {
            BindingScope binding = current;
            current = null;
            retainBindingWhileHidden = false;
            binding?.Dispose();
        }
    }
}
