using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    public sealed class BindingMap : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public string key;
            public Object component;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public T Require<T>(string key) where T : Object
        {
            Validate();
            foreach (var entry in entries)
            {
                if (entry.key != key)
                    continue;
                if (entry.component is T value)
                    return value;
                throw new InvalidOperationException(
                    $"Binding '{key}' on '{name}' expects {typeof(T).Name} but references {entry.component.GetType().Name}.");
            }
            throw new InvalidOperationException($"Missing binding '{key}' ({typeof(T).Name}) on '{name}'.");
        }

        public void Validate()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (string.IsNullOrWhiteSpace(entry.key))
                    throw new InvalidOperationException($"Binding at index {i} on '{name}' has an empty key.");
                if (!seen.Add(entry.key))
                    throw new InvalidOperationException($"Duplicate binding '{entry.key}' on '{name}'.");
                if (!entry.component)
                    throw new InvalidOperationException($"Binding '{entry.key}' on '{name}' has a missing reference.");

                Transform target = entry.component switch
                {
                    GameObject gameObject => gameObject.transform,
                    Component component => component.transform,
                    _ => throw new InvalidOperationException($"Binding '{entry.key}' on '{name}' must reference a GameObject or Component.")
                };
                if (!target.IsChildOf(transform))
                    throw new InvalidOperationException($"Binding '{entry.key}' on '{name}' references '{target.name}' outside its hierarchy.");
            }
        }
    }
}
