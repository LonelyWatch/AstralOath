using System;
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
        private bool contentPressed;
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
            if (pressedContent && contentPressed != pressed)
            {
                if (pressed) restingPosition = pressedContent.anchoredPosition;
                pressedContent.anchoredPosition = pressed ? restingPosition + pressedOffset : restingPosition;
                contentPressed = pressed;
            }
            foreach (var decoration in pressDecorations)
                if (decoration.image) decoration.image.overrideSprite = state == SelectionState.Pressed ? decoration.pressed : null;
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