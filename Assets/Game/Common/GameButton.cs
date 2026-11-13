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
        private BindingScope current;

        public override void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left &&
                TryGetComponent<GameLongPress>(out var hold) && hold.ConsumedClick(eventData)) return;
            base.OnPointerClick(eventData);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (clickAudio) onClick.AddListener(PlayClickSound);
        }

        private void PlayClickSound()
        {
            if (!clickAudio || !clickAudio.clip || !IsActive() || !IsInteractable()) return;
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas)
            {
                clickAudio.PlayOneShot(clickAudio.clip);
                return;
            }

            // The screen can close as part of this click; keep the sound on its owning Canvas.
            var root = canvas.rootCanvas.gameObject;
            if (!root.TryGetComponent<GameUiAudio>(out var output))
                output = root.AddComponent<GameUiAudio>();
            output.Play(clickAudio.clip, clickAudio.volume);
        }

        public void Bind(BindingScope owner, Func<CancellationToken, Task> handler)
        {
            Unbind();
            owner.Token.ThrowIfCancellationRequested();
            var binding = new BindingScope(owner.Token);
            current = binding;
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
            try { Unbind(); }
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
            binding?.Dispose();
        }
    }
}