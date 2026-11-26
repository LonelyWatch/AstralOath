using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        private const string MailOverlay = "mail-window";
        private MailSession mailSession;
        private Task<MailWindow> mailViewLoading;
        private MailWindow mailView;
        private GameObject mailPortal;
        private CanvasGroup mailPortalFade;
        private BindingScope mailOpening;
        private Task mailRefresh, mailClosing;
        private bool mailRefreshQueued, mailReconcileQueued;
        private Task mailReconcile;
        private Func<CancellationToken, Task> reconcileMailPage;

        private void InstallMail(Func<CancellationToken, Task<string>> bearer)
        {
            views.Register<MailWindow>();
            var client = new MailApi(endpoint, bearer);
            mailSession = new MailSession(client);
            lifetime.Add(client.Dispose);
            lifetime.Add(mailSession.Dispose);
            mailSession.Invalidated += QueueMailRefresh;
            mailSession.Claimed += OnMailClaimed;
            mailSession.Uncertain += ReconcileAfterMail;
            lifetime.Add(() =>
            {
                mailSession.Invalidated -= QueueMailRefresh;
                mailSession.Claimed -= OnMailClaimed;
                mailSession.Uncertain -= ReconcileAfterMail;
                if (mailPortal) UnityEngine.Object.Destroy(mailPortal);
            });
        }

        private async Task PrepareMailAsync()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { await mailSession.RefreshAsync(deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                Debug.LogWarning("Mail preparation: " + error.GetType().Name);
            }
            if (!token.IsCancellationRequested) _ = mailSession.ObserveAsync(token);
        }

        private void QueueMailRefresh(long version)
        {
            if (token.IsCancellationRequested || windows.IsOverlayOpen(MailOverlay)) return;
            mailRefreshQueued = true;
            if (mailRefresh == null || mailRefresh.IsCompleted) mailRefresh = RefreshMailAsync();
        }

        private async Task RefreshMailAsync()
        {
            while (mailRefreshQueued && !token.IsCancellationRequested)
            {
                mailRefreshQueued = false;
                try { await mailSession.RefreshAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    Debug.LogWarning("Mail summary: " + error.GetType().Name);
                }
            }
        }

        // Login-owned receipt handling also runs when the initiating page has already closed.
        private void OnMailClaimed(MailClaimReceipt receipt)
        {
            if (token.IsCancellationRequested || receipt == null) return;
            ReconcileAfterMail();
        }

        private void ReconcileAfterMail()
        {
            if (token.IsCancellationRequested) return;
            firstReads.ReconcileInventories();
            firstReads.ReconcileEquipment();
            mailReconcileQueued = true;
            if (mailReconcile == null || mailReconcile.IsCompleted) mailReconcile = RefreshEquipmentAfterMailAsync();
        }

        private async Task RefreshEquipmentAfterMailAsync()
        {
            while (mailReconcileQueued && !token.IsCancellationRequested)
            {
                mailReconcileQueued = false;
                var refresh = reconcileMailPage;
                try
                {
                    await mailSession.RefreshAsync(token);
                    // Reload the existing binder; rebuilding the page would discard its selection and scroll.
                    if (refresh != null) await refresh(token);
                }
                catch (OperationCanceledException) { }
                catch (Exception error) when (!token.IsCancellationRequested)
                {
                    Debug.LogWarning("Mail inventory calibration: " + error.GetType().Name);
                }
            }
        }

        private async Task ShowPageWithMail(Func<BindingScope, Task> open)
        {
            await windows.ShowPageAsync(async scope =>
            {
                var bound = new HashSet<BindingMap>();
                BindMailEntries(parent.gameObject, scope, bound);
                await open(scope);
                scope.Token.ThrowIfCancellationRequested();
                BindMailEntries(parent.gameObject, scope, bound);
            });
        }

        private void BindMailEntries(GameObject root, BindingScope scope, HashSet<BindingMap> bound)
        {
            if (mailSession == null) return;
            foreach (var map in root.GetComponentsInChildren<BindingMap>(true))
            {
                if (map.name != "MailEntry" || (mailPortal && map.transform.IsChildOf(mailPortal.transform)) || !bound.Add(map)) continue;
                MailEntryBinder.Bind(map, scope, mailSession, OpenMailAsync);
            }
        }

        private async Task<MailWindow> LoadMailViewAsync()
        {
            if (!mailPortal)
            {
                mailPortal = new GameObject("Mail overlay", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup));
                var rect = (RectTransform)mailPortal.transform;
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var canvas = mailPortal.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 100;
                mailPortalFade = mailPortal.GetComponent<CanvasGroup>();
                mailPortal.SetActive(false);
            }
            return await views.CreateAsync<MailWindow>(assets, mailPortal.transform, lifetime);
        }

        private async Task OpenMailAsync(CancellationToken cancellation)
        {
            if (windows.IsOverlayOpen(MailOverlay)) return;
            if (mailViewLoading == null || mailViewLoading.IsCanceled || mailViewLoading.IsFaulted)
                mailViewLoading = LoadMailViewAsync();
            var loaded = await mailViewLoading;
            cancellation.ThrowIfCancellationRequested();
            mailView = loaded;
            Task initialize = Task.CompletedTask;
            windows.ShowOverlay(MailOverlay, scope =>
            {
                mailOpening = scope;
                mailPortal.SetActive(true);
                mailPortalFade.alpha = 1;
                scope.Add(() =>
                {
                    if (mailOpening == scope) mailOpening = null;
                    loaded.Hide();
                    if (mailPortal) mailPortal.SetActive(false);
                });
                foreach (var map in loaded.Root.GetComponentsInChildren<BindingMap>(true).Where(item => item.name == "MailEntry"))
                {
                    void Update(MailSummary summary)
                    {
                        if (!map || scope.Token.IsCancellationRequested) return;
                        int count = summary?.effectiveCount ?? 0;
                        map.Require<GameObject>("counter").SetActive(count > 0);
                        map.Require<GameText>("count").text = count > 0 ? count.ToString(CultureInfo.InvariantCulture) : "";
                    }
                    mailSession.Changed += Update;
                    scope.Add(() => mailSession.Changed -= Update);
                    Update(mailSession.Summary);
                }
                var binder = new MailWindowBinder(loaded, scope, locale, mailSession,
                    () => { if (mailClosing == null || mailClosing.IsCompleted) mailClosing = CloseMailAsync(scope); },
                    (receipt, ct) => Task.CompletedTask);
                initialize = binder.InitializeAsync(scope.Token);
            });
            try { await initialize; }
            catch { windows.CloseOverlay(MailOverlay); throw; }
        }

        private async Task CloseMailAsync(BindingScope opening)
        {
            try
            {
                double start = Time.realtimeSinceStartupAsDouble;
                while (!opening.Token.IsCancellationRequested && Time.realtimeSinceStartupAsDouble - start < .2)
                {
                    if (mailPortalFade) mailPortalFade.alpha = 1f - (float)((Time.realtimeSinceStartupAsDouble - start) / .2);
                    await Task.Delay(16, opening.Token);
                }
                if (mailOpening != opening) return;
                windows.CloseOverlay(MailOverlay);
            }
            catch (OperationCanceledException) when (opening.Token.IsCancellationRequested || token.IsCancellationRequested) { }
            catch (Exception error) when (!token.IsCancellationRequested)
            {
                Debug.LogWarning("Mail close: " + error.GetType().Name);
            }
        }
    }
}
