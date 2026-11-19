using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AstralOath.Client.Common
{
    // A window owns one instance. Pages replace each other; overlays retain the current page.
    public sealed class UIPageLayers
    {
        private readonly IReadOnlyList<GameObject> pages;
        private readonly IReadOnlyList<GameObject> overlays;
        private readonly List<(GameObject Root, Func<Task> Back)> stack = new List<(GameObject, Func<Task>)>();
        private GameObject page;
        private bool returning;

        public UIPageLayers(IReadOnlyList<GameObject> pages, IReadOnlyList<GameObject> overlays)
        {
            this.pages = pages ?? throw new ArgumentNullException(nameof(pages));
            this.overlays = overlays ?? throw new ArgumentNullException(nameof(overlays));
            foreach (var overlay in overlays) overlay.SetActive(false);
        }

        public void ShowPage(GameObject next)
        {
            RequireMember(pages, next);
            while (stack.Count != 0) CloseOverlay();
            foreach (var root in pages) root.SetActive(root == next);
            page = next;
        }

        public void ShowOverlay(GameObject overlay, Func<Task> back)
        {
            if (!page) throw new InvalidOperationException("Open a page before showing an overlay.");
            RequireMember(overlays, overlay);
            if (back == null) throw new ArgumentNullException(nameof(back));
            int prior = stack.FindIndex(layer => layer.Root == overlay);
            if (prior >= 0)
            {
                while (stack.Count > prior + 1) CloseOverlay();
                return;
            }
            overlay.transform.SetAsLastSibling();
            stack.Add((overlay, back));
            overlay.SetActive(true);
        }

        public void CloseOverlay()
        {
            int last = stack.Count - 1;
            if (last < 0) return;
            var root = stack[last].Root;
            stack.RemoveAt(last);
            root.SetActive(false);
        }

        public async Task BackAsync(Func<Task> pageBack)
        {
            if (returning) return;
            returning = true;
            try
            {
                var back = stack.Count > 0 ? stack[stack.Count - 1].Back : pageBack;
                if (back != null) await back();
            }
            finally { returning = false; }
        }

        private static void RequireMember(IReadOnlyList<GameObject> roots, GameObject root)
        {
            if (root)
                for (int i = 0; i < roots.Count; ++i)
                    if (roots[i] == root) return;
            throw new ArgumentException("The layer does not belong to this window.", nameof(root));
        }
    }
}
