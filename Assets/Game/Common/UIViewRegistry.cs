using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Common
{
    // Register and create views on Unity's main thread, as with BindingScope cleanup.
    public sealed class UIViewRegistry
    {
        private readonly Dictionary<Type, string> addresses = new Dictionary<Type, string>();

        public void Register<T>() where T : UIView, new()
        {
            var type = typeof(T);
            if (addresses.ContainsKey(type))
                throw new InvalidOperationException($"UIView '{type.Name}' is already registered.");
            string address = new T().PrefabPath;
            if (string.IsNullOrWhiteSpace(address))
                throw new InvalidOperationException($"UIView '{type.Name}' requires a prefab address.");
            if (addresses.ContainsValue(address))
                throw new InvalidOperationException($"UIView prefab address '{address}' is already registered.");
            addresses.Add(type, address);
        }

        public async Task<T> CreateAsync<T>(IAssetService assets, Transform parent, BindingScope owner)
            where T : UIView, new()
        {
            if (assets == null)
                throw new ArgumentNullException(nameof(assets));
            if (!parent)
                throw new ArgumentNullException(nameof(parent));
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));
            if (!addresses.TryGetValue(typeof(T), out string address))
                throw new InvalidOperationException($"UIView '{typeof(T).Name}' is not registered.");

            owner.Token.ThrowIfCancellationRequested();
            var scope = new BindingScope(owner.Token);
            Action detach = owner.Add(scope.Dispose);
            try
            {
                var prefab = await assets.LoadAsync<GameObject>(address, scope);
                scope.Token.ThrowIfCancellationRequested();
                if (!parent)
                    throw new InvalidOperationException($"UIView parent was destroyed while loading '{address}'.");
                if (!prefab)
                    throw new InvalidOperationException($"UIView prefab '{address}' could not be loaded.");

                var root = InstantiateHidden(prefab, parent, scope);
                scope.Token.ThrowIfCancellationRequested();
                var view = new T();
                view.Attach(root);
                scope.Token.ThrowIfCancellationRequested();
                return view;
            }
            catch (Exception error)
            {
                detach();
                try { scope.Dispose(); }
                catch (Exception cleanupError) { throw new AggregateException(error, cleanupError); }
                throw;
            }
        }

        public static void ValidatePrefab<T>(GameObject prefab) where T : UIView, new()
        {
            if (!prefab)
                throw new ArgumentNullException(nameof(prefab));
            new T().Attach(prefab);
        }

        private static GameObject InstantiateHidden(GameObject prefab, Transform parent, BindingScope scope)
        {
            GameObject staging = null;
            try
            {
                Transform initialParent = parent;
                if (prefab.activeSelf)
                {
                    // Keep an active source's clone inactive before Unity can call OnEnable.
                    staging = new GameObject("UIView Staging", typeof(RectTransform));
                    staging.SetActive(false);
                    initialParent = staging.transform;
                }

                var root = Object.Instantiate(prefab, initialParent, false);
                scope.Add(() => Release(root));
                if (!staging)
                    return root;

                scope.Token.ThrowIfCancellationRequested();
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                scope.Token.ThrowIfCancellationRequested();
                if (prefab.transform is RectTransform source)
                {
                    // Anchored offsets are relative to the parent rect, which changed during staging.
                    var target = (RectTransform)root.transform;
                    target.anchorMin = source.anchorMin;
                    target.anchorMax = source.anchorMax;
                    target.pivot = source.pivot;
                    target.sizeDelta = source.sizeDelta;
                    target.anchoredPosition3D = source.anchoredPosition3D;
                }
                return root;
            }
            finally
            {
                Release(staging);
            }
        }

        private static void Release(GameObject root)
        {
            if (!root)
                return;
            root.SetActive(false);
            if (Application.isPlaying)
                Object.Destroy(root);
            else
                Object.DestroyImmediate(root);
        }
    }
}
