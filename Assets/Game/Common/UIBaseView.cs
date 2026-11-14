using System;
using UnityEngine;

namespace AstralOath.Client.Common
{
    public abstract class UIBaseView
    {
        public abstract string PrefabPath { get; }
        public GameObject Root { get; private set; }
        protected BindingMap Bindings { get; private set; }

        public void Show() => Root.SetActive(true);
        public void Hide() => Root.SetActive(false);

        // Override to require the keys and reference types that this view consumes.
        public virtual void Validate() { }

        protected void BindTo(GameObject root) => Attach(root);

        internal void Attach(GameObject root)
        {
            Root = root;
            Bindings = root.GetComponent<BindingMap>();
            if (!Bindings)
                throw new InvalidOperationException($"UIBaseView '{PrefabPath}' requires a BindingMap on the root of '{root.name}'.");
            Bindings.Validate();
            Validate();
        }
    }
}
