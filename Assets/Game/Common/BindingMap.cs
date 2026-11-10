using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    public sealed class BindingMap : MonoBehaviour
    {
        [Serializable]
        private struct Entry
        {
            public string key;
            public Component component;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public T Require<T>(string key) where T : Component
        {
            Validate();
            foreach (var entry in entries)
            {
                if (entry.key == key && entry.component is T component)
                    return component;
            }
            throw new InvalidOperationException($"Missing binding {key} ({typeof(T).Name}) on {name}.");
        }

        public void Validate()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.key) || !seen.Add(entry.key) || !entry.component ||
                    !entry.component.transform.IsChildOf(transform))
                    throw new InvalidOperationException($"Invalid binding {entry.key} on {name}.");
            }
        }
    }
}