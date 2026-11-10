using System;
using System.Collections.Generic;
using AstralOath.Client.Core;
using DG.Tweening;
using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class DotweenAnimations : IAnimationService, IDisposable
    {
        private readonly Dictionary<Transform, Action> active = new Dictionary<Transform, Action>();
        private bool disposed;

        public void Pulse(Transform target, float seconds, BindingScope scope)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(DotweenAnimations));
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            scope.Token.ThrowIfCancellationRequested();
            if (active.TryGetValue(target, out var previous))
                previous();

            Vector3 initial = target.localScale;
            var tween = target.DOScale(initial * 0.92f, seconds * 0.5f)
                .SetLoops(2, LoopType.Yoyo).SetUpdate(true).SetEase(Ease.InOutSine);
            bool released = false;
            Action unregister = null;
            void Release()
            {
                if (released)
                    return;
                released = true;
                unregister?.Invoke();
                tween.Kill();
                if (target)
                    target.localScale = initial;
                active.Remove(target);
            }

            active.Add(target, Release);
            tween.OnComplete(Release);
            unregister = scope.Add(Release);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            foreach (var release in new List<Action>(active.Values))
                release();
        }
    }
}