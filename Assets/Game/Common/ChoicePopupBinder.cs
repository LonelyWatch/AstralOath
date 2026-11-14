using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Common
{
    public sealed class ChoicePopupBinder : IDisposable
    {
        private readonly ChoicePopup view;
        private readonly List<(GameButton Button, UnityAction Listener)> choices = new();
        private readonly UnityAction dismiss;

        public ChoicePopupBinder(ChoicePopup view)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            dismiss = Close;
            view.Dismiss.onClick.AddListener(dismiss);
        }

        public void Open(IReadOnlyList<string> labels, int selected, Action<int> choose)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            if (choose == null) throw new ArgumentNullException(nameof(choose));
            ClearChoices();

            const float rowHeight = 42f;
            const float spacing = 6f;
            const float top = 12f;
            float height = top * 2 + labels.Count * (rowHeight + spacing) - spacing;
            view.Body.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(top * 2, height));
            view.Show();
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var row = Object.Instantiate(view.ChoiceTemplate, view.Body, false);
                var rowRect = (RectTransform)row.transform;
                rowRect.anchoredPosition -= new Vector2(0f, i * (rowHeight + spacing));
                var bindings = row.GetComponent<BindingMap>();
                bindings.Require<GameText>("label").text = labels[i];
                bindings.Require<Image>("check").gameObject.SetActive(i == selected);
                UnityAction listener = () =>
                {
                    Close();
                    choose(index);
                };
                row.gameObject.SetActive(true);
                row.onClick.AddListener(listener);
                choices.Add((row, listener));
            }
        }

        public void Close()
        {
            if (view.Root) view.Hide();
            ClearChoices();
        }

        public void Dispose()
        {
            if (view.Dismiss) view.Dismiss.onClick.RemoveListener(dismiss);
            Close();
        }

        private void ClearChoices()
        {
            foreach (var (button, listener) in choices)
            {
                if (!button) continue;
                button.onClick.RemoveListener(listener);
                button.gameObject.SetActive(false);
                if (Application.isPlaying) Object.Destroy(button.gameObject);
                else Object.DestroyImmediate(button.gameObject);
            }
            choices.Clear();
        }
    }
}
