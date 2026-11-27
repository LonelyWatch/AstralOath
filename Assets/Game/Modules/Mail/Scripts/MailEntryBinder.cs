using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using UnityEngine;

namespace AstralOath.Client.Mail
{
    public static class MailEntryBinder
    {
        public static void Bind(BindingMap map, BindingScope scope, MailSession session,
            Func<CancellationToken, Task> open)
        {
            var button = map.Require<GameButton>("button");
            button.FeedbackSound = GameUiFeedback.Sound.Silent;
            // The cached More panel is hidden between uses; its page still owns this listener.
            bool opening = false;
            async void Click()
            {
                if (opening || scope.Token.IsCancellationRequested) return;
                opening = true;
                try { await open(scope.Token); }
                catch (OperationCanceledException) when (scope.Token.IsCancellationRequested) { }
                catch (Exception error) when (!scope.Token.IsCancellationRequested)
                {
                    Debug.LogWarning("Mail entry: " + error.GetType().Name);
                }
                finally { opening = false; }
            }
            button.onClick.AddListener(Click);
            scope.Add(() => { if (button) button.onClick.RemoveListener(Click); });
            void Update(MailSummary summary)
            {
                if (!map || scope.Token.IsCancellationRequested) return;
                int count = summary?.effectiveCount ?? 0;
                var counter = map.Require<GameObject>("counter");
                var label = map.Require<GameText>("count");
                label.text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
                // The source keeps circular end caps: one digit is round, extra digits extend only the centre.
                // Share the header's design scale instead of deriving a squashed plate from relative anchors.
                var rect = (RectTransform)counter.transform;
                float scale = rect.sizeDelta.y / 30f;
                float width = count < 10 ? 31 : count < 100 ? 38 : 48;
                rect.sizeDelta = new Vector2(width * scale, 30 * scale);
                counter.SetActive(count > 0);
            }
            session.Changed += Update;
            scope.Add(() => session.Changed -= Update);
            Update(session.Summary);
        }
    }
}
