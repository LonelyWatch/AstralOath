using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent, AddComponentMenu("UI/Game Button")]
    public sealed class GameButton : Button
    {
        private BindingScope current;

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