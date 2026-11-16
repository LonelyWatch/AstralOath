using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Shared.Updates;
using UnityEngine;
using UnityEngine.Scripting;
using Debug = UnityEngine.Debug;

namespace AstralOath.Client.Bootstrap
{
    internal sealed class EntryFlow
    {
        internal const string ThemeAddress = "entry/theme";
        private readonly EntryWebClient web;
        private readonly EntryScreen screen;
        private readonly bool production;
        private readonly bool guestEntry;
        private readonly string appVersion;

        internal EntryFlow(EntryWebClient web, EntryScreen screen, string appVersion, bool production, bool guestEntry)
        {
            this.web = web;
            this.screen = screen;
            this.production = production;
            this.guestEntry = guestEntry;
            this.appVersion = appVersion;
        }

        internal async Task<(StartupContent content, GameAccountSession session)> OpenAsync(
            Func<Func<long, CancellationToken, Task<bool>>, Action<ContentDownloadProgress>, Task<StartupContent>> openContent,
            CancellationToken lifetime)
        {
            var version = Resources.Load<TextAsset>(PlayerBaseline.ResourceName);
            long bundled = version ? (JsonUtility.FromJson<PlayerBaseline>(version.text)?.contentVersion ?? 0) : 0;
            screen.Version = FormatVersion(appVersion, bundled.ToString(CultureInfo.InvariantCulture));
            while (true)
            {
                await screen.WaitForLoginAsync(lifetime);
                var admission = guestEntry ? null : await AuthenticateAsync(lifetime);
                if (!guestEntry && admission == null) continue;

                while (guestEntry || await screen.SelectRealmAsync("不凋零回忆", lifetime))
                {
                    GameAccountSession session;
                    StartupContent content;
                    try
                    {
                        screen.ShowChecking();
                        var policy = guestEntry
                            ? await web.ReadGuestPolicyAsync(lifetime)
                            : await web.ReadPolicyAsync(lifetime);
                        if ((policy != null && policy.realmId != EntryWebClient.RealmId) || (policy == null && !guestEntry))
                            throw new EntryRequestException("invalid_realm");
                        if (EntryWebClient.RequiresNewPlayer(appVersion, policy?.minimumAppVersion))
                        {
                            // There is no continuation to game content on an obsolete installation.
                            await screen.ShowForceAsync(policy.minimumAppVersion, lifetime);
                            if (guestEntry) break;
                            continue;
                        }

                        session = guestEntry ? null : await web.SelectRealmAsync(admission, appVersion, lifetime);
                        content = await LoadContentAsync(openContent, lifetime);
                        if (content == null)
                        {
                            if (guestEntry) break;
                            continue;
                        }
                    }
                    catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
                    catch (EntryRequestException error)
                    {
                        if (error.Code == "client_update_required")
                        {
                            await screen.ShowForceAsync(null, lifetime);
                            continue;
                        }
                        bool retry = await screen.ShowFailureAsync(Message(error.Code), lifetime);
                        if (!retry || error.Code == "unauthorized") break;
                        continue;
                    }
                    catch (StartupContentUnavailableException error)
                    {
                        if (error.InnerException != null)
                            Debug.LogWarning("Entry content check failed: " + error.InnerException.GetType().Name);
                        if (!await screen.ShowFailureAsync(error.Message, lifetime)) break;
                        continue;
                    }
                    catch (Exception error)
                    {
                        Debug.LogWarning("Entry content check failed: " + error.GetType().Name);
                        if (!await screen.ShowFailureAsync("检查或更新资源失败，请检查网络和存储空间后重试。", lifetime)) break;
                        continue;
                    }

                    try
                    {
                        string actualVersion = content.ReleaseVersion?.ToString(CultureInfo.InvariantCulture)
                            ?? (bundled > 0 ? bundled.ToString(CultureInfo.InvariantCulture) :
                                (production ? content.PackageVersion : "Editor"));
                        screen.Version = FormatVersion(appVersion, actualVersion);
                        if (content.RestartRequired)
                        {
                            Debug.Log($"ASTRAL_CONTENT_RESTART_REQUIRED version={content.ReleaseVersion}");
                            await screen.ShowRestartAsync(lifetime);
                            content.Dispose();
                            return default;
                        }

                        if (!guestEntry)
                        {
                            using var display = new BindingScope(lifetime);
                            var background = await ReadThemeAsync(content, display);
                            await screen.WaitForStartAsync(background, lifetime);
                        }
                        return (content, session);
                    }
                    catch
                    {
                        content.Dispose();
                        throw;
                    }
                }
            }
        }

        private async Task<string> AuthenticateAsync(CancellationToken lifetime)
        {
            bool registration = false;
            string account = "", email = "", message = "";
            while (true)
            {
                var action = await screen.ShowAuthAsync(registration, account, email, message, lifetime);
                message = "";
                if (action.Kind == EntryAuthKind.Back)
                {
                    if (!registration) return null;
                    registration = false;
                    continue;
                }
                account = action.Account?.Trim() ?? "";
                email = action.Email?.Trim() ?? "";
                if (action.Kind == EntryAuthKind.OpenRegistration)
                {
                    registration = true;
                    continue;
                }

                try
                {
                    screen.ShowAuthBusy();
                    if (action.Kind == EntryAuthKind.RequestCode)
                    {
                        await web.RequestCodeAsync(email, lifetime);
                        message = "如该邮箱可注册，验证码将发送至邮箱。";
                    }
                    else return await web.AuthenticateAsync(action, action.Kind == EntryAuthKind.Register, lifetime);
                }
                catch (EntryRequestException error) { message = Message(error.Code); }
            }
        }

        private async Task<StartupContent> LoadContentAsync(
            Func<Func<long, CancellationToken, Task<bool>>, Action<ContentDownloadProgress>, Task<StartupContent>> openContent,
            CancellationToken lifetime)
        {
            var context = SynchronizationContext.Current;
            var clock = new Stopwatch();
            long lastReport = -100, previousBytes = 0, previousTime = 0;
            bool displaying = true;
            async Task<bool> ConfirmAsync(long bytes, CancellationToken token)
            {
                bool proceed = await screen.ConfirmDownloadAsync(bytes, token);
                if (!proceed) return false;
                clock.Start();
                screen.ShowDownload(0, bytes, 0);
                return true;
            }
            void Report(ContentDownloadProgress status)
            {
                long now = clock.ElapsedMilliseconds;
                if (status.Total <= 0 || (now - lastReport < 100 && status.Received < status.Total)) return;
                double rate = previousTime > 0 && now > previousTime
                    ? Math.Max(0, status.Received - previousBytes) * 1000d / (now - previousTime) : 0;
                previousTime = lastReport = now;
                previousBytes = status.Received;
                context.Post(_ =>
                {
                    if (displaying && !lifetime.IsCancellationRequested) screen.ShowDownload(status.Received, status.Total, rate);
                }, null);
            }
            try { return await openContent(ConfirmAsync, Report); }
            finally { displaying = false; }
        }

        private static async Task<Sprite> ReadThemeAsync(StartupContent content, BindingScope display)
        {
            try
            {
                var json = await content.Assets.LoadAsync<TextAsset>(ThemeAddress, display);
                var theme = JsonUtility.FromJson<Theme>(json.text);
                if (string.IsNullOrEmpty(theme?.backgroundAddress)) throw new InvalidOperationException("Empty title background address.");
                return await content.Assets.LoadAsync<Sprite>(theme.backgroundAddress, display);
            }
            catch (OperationCanceledException) when (display.Token.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                Debug.LogWarning("Entry theme could not be loaded; using built-in background: " + error.Message);
                return null;
            }
        }

        private static string FormatVersion(string app, string resources) => $"App:{app}  R:{resources}";

        private static string Message(string code)
        {
            switch (code)
            {
                case "invalid_email": return "请输入有效的邮箱地址。";
                case "invalid_registration": return "账号需为 3–24 位字母、数字或下划线，密码需为 12–128 个字符。";
                case "invalid_credentials": return "账号或密码错误。";
                case "invalid_code": return "验证码无效或已过期，请重新获取。";
                case "verification_cooldown":
                case "rate_limited": return "操作过于频繁，请稍后重试。";
                case "account_unavailable": return "该账号或邮箱无法注册，请检查后重试。";
                case "smtp_unavailable": return "暂时无法发送验证码，请稍后重试。";
                case "invalid_realm": return "区服信息有变，请联系管理员。";
                case "unauthorized": return "登录已过期，请重新登录。";
                case "database_unavailable": return "账号服务暂时不可用，请稍后重试。";
                case "invalid_version": return "服务器版本配置有误，请联系管理员。";
                default: return "连接游戏服务器失败，请检查网络后重试。";
            }
        }

        [Serializable, Preserve] private sealed class Theme { public string backgroundAddress; }
    }
}
