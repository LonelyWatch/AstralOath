using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Bootstrap
{
    internal enum EntryAuthKind { Back, Login, OpenRegistration, RequestCode, Register }

    internal readonly struct EntryAuthAction
    {
        public EntryAuthAction(EntryAuthKind kind, string account, string password, string email, string code)
        {
            Kind = kind;
            Account = account;
            Password = password;
            Email = email;
            Code = code;
        }

        public EntryAuthKind Kind { get; }
        public string Account { get; }
        public string Password { get; }
        public string Email { get; }
        public string Code { get; }
    }

    /// <summary>Stable startup view. Actions complete the current prompt and leave network and activation to the caller.</summary>
    internal sealed class EntryScreen : IDisposable
    {
        private static readonly Color White = new Color32(244, 248, 254, 255);
        private static readonly Color Pale = new Color32(173, 200, 217, 255);
        private static readonly Color DarkInk = new Color32(22, 32, 43, 255);
        private static readonly Color PanelColor = new Color32(12, 28, 51, 247);
        private static readonly Color Scrim = new Color32(0, 0, 0, 127);
        private static readonly LocaleService Locale = new LocaleService(
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["zh-Hans"] = new Dictionary<string, string>
                {
                    ["entry.title"] = "AstralOath",
                    ["entry.login"] = "登录",
                    ["entry.account"] = "账号",
                    ["entry.login.account"] = "输入账号或邮箱",
                    ["entry.register.account"] = "输入账号",
                    ["entry.password"] = "密码",
                    ["entry.password.input"] = "输入密码",
                    ["entry.register"] = "注册账号",
                    ["entry.back"] = "返回",
                    ["entry.email"] = "邮箱",
                    ["entry.email.input"] = "输入邮箱地址",
                    ["entry.code"] = "验证码",
                    ["entry.code.input"] = "输入邮箱验证码",
                    ["entry.code.send"] = "发送验证码",
                    ["entry.auth.pending"] = "正在处理，请稍候……",
                    ["entry.realm"] = "选择服务器",
                    ["entry.realm.select"] = "选择",
                    ["entry.patch"] = "更新补丁",
                    ["entry.patch.checking"] = "正在收集更新下载信息……",
                    ["entry.patch.checking.status"] = "正在检查更新信息",
                    ["entry.patch.ask"] = "检测到新的补丁，\n要进行下载吗？",
                    ["entry.patch.size"] = "补丁大小：{0}",
                    ["entry.patch.wifi"] = "（建议使用 Wi-Fi 下载）",
                    ["entry.patch.verify"] = "下载完成后将自动校验资源。",
                    ["entry.patch.cancel"] = "取消",
                    ["entry.patch.continue"] = "进行",
                    ["entry.patch.downloading"] = "正在下载资源",
                    ["entry.patch.validating"] = "正在校验资源……",
                    ["entry.patch.progress"] = "{0} / {1}   {2}/s",
                    ["entry.patch.percent"] = "{0:0}%",
                    ["entry.patch.restart.title"] = "更新完成",
                    ["entry.patch.restart.message"] = "资源校验完成。请退出并重新打开游戏，启用更新内容。",
                    ["entry.patch.restart.exit"] = "退出游戏",
                    ["entry.force.title"] = "需要更新客户端",
                    ["entry.force.message"] = "当前客户端版本过低，请前往QQ群下载最新版。",
                    ["entry.force.minimum"] = "最低客户端版本：{0}",
                    ["entry.force.exit"] = "退出游戏",
                    ["entry.error.title"] = "连接或资源更新失败",
                    ["entry.error.retry"] = "重试",
                    ["entry.ready"] = "轻触画面开始"
                }
            }, "zh-Hans", "ja", "ja");

        private readonly RectTransform root;
        private readonly RectTransform safe;
        private readonly Image backdrop;
        private readonly AspectRatioFitter backdropAspect;
        private readonly Sprite titleBackground;
        private readonly GameText version;
        private readonly TMP_FontAsset font;
        private readonly Material contrastMaterial;
        private readonly Material readyMaterial;
        private readonly GameUiFeedback startupFeedback;
        private readonly Dictionary<string, Sprite> art = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private RectTransform panel;
        private CanvasGroup interaction;
        private Action cancelPrompt;
        private GameText progressStatus;
        private GameText progressAmount;
        private GameText progressPercent;
        private RectTransform progressFill;
        private bool disposed;

        public EntryScreen(Transform parent, GameText template)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            font = template && template.font ? template.font : TMP_Settings.defaultFontAsset;
            if (font && font.material && font.material.HasProperty("_UnderlayColor"))
            {
                contrastMaterial = new Material(font.material) { name = "Startup text shadow" };
                contrastMaterial.EnableKeyword("UNDERLAY_ON");
                contrastMaterial.SetColor("_UnderlayColor", new Color(0, 0, 0, .90f));
                contrastMaterial.SetFloat("_UnderlayOffsetX", 0f);
                contrastMaterial.SetFloat("_UnderlayOffsetY", -.65f);
                contrastMaterial.SetFloat("_UnderlayDilate", .27f);
                contrastMaterial.SetFloat("_UnderlaySoftness", .21f);
                readyMaterial = new Material(contrastMaterial) { name = "Startup ready text" };
                readyMaterial.SetColor("_UnderlayColor", new Color(.07f, .53f, .91f, .94f));
                readyMaterial.SetFloat("_UnderlayOffsetY", 0);
                readyMaterial.SetFloat("_UnderlayDilate", .56f);
                readyMaterial.SetFloat("_UnderlaySoftness", .55f);
            }

            var canvas = parent.GetComponentInParent<Canvas>();
            var canvasRoot = canvas ? canvas.rootCanvas : null;
            root = Box("Startup screen", canvasRoot ? canvasRoot.transform : parent);
            if (canvasRoot && !canvasRoot.TryGetComponent<GameUiFeedback>(out _))
            {
                var click = Resources.Load<AudioClip>("Entry/UiConfirm");
                var cancel = Resources.Load<AudioClip>("Entry/UiCancel");
                if (click || cancel)
                {
                    startupFeedback = canvasRoot.gameObject.AddComponent<GameUiFeedback>();
                    startupFeedback.Configure(null, null, click, click, cancel,
                        blocked: Resources.Load<AudioClip>("Entry/UiBlocked"));
                }
            }
            var curtain = Picture(Box("Background matte", root), null, DarkInk);
            curtain.raycastTarget = true;
            backdrop = Picture(Box("Title background", root), null, White);
            backdrop.enabled = false;
            backdropAspect = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            backdropAspect.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            titleBackground = Art("TitleBackground");
            SetBackground(titleBackground);

            safe = Box("Startup safe area", root);
            safe.gameObject.AddComponent<SafeAreaLayout>();
            var logo = Art("TitleLogo");
            if (logo)
            {
                var image = Picture(Box("Title logo", safe, .30f, .463f, .70f, .638f), logo, White);
                image.preserveAspect = true;
            }
            else
            {
                Label(Box("Title logo", safe, .25f, .45f, .75f, .65f), L("entry.title"), 74,
                    White, TextAlignmentOptions.Center, true, true);
            }

            version = Label(Box("Client version", safe, 0, .956f, .63f, 1, 24, 0, -8, 0),
                "", 18, White, TextAlignmentOptions.Left, contrast: true);
        }

        public string Version { get => version.text; set => version.text = value ?? ""; }

        public Task WaitForLoginAsync(CancellationToken token)
        {
            SetBackground(titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            var button = Button(Box("Login", panel, .39f, .790f, .61f, .871f), L("entry.login"),
                "LoginButton", new Color32(26, 45, 63, 182), GameUiFeedback.Sound.Normal,
                () => Complete(prompt, true), 28, "LoginButtonPressed");
            button.Select();
            var key = Art("Key");
            if (key)
            {
                var icon = Picture(Box("Login key", button.transform, .05f, .16f, .20f, .84f), key, White);
                icon.preserveAspect = true;
            }
            return prompt.Task;
        }

        public Task<EntryAuthAction> ShowAuthAsync(bool registration, string account, string email,
            string message, CancellationToken token)
        {
            ResetPanel();
            var prompt = Prompt<EntryAuthAction>(token);
            var form = Dialog(registration ? "Registration dialog" : "Login dialog",
                L(registration ? "entry.register" : "entry.login"), .27f, registration ? .135f : .235f,
                .73f, registration ? .865f : .765f);
            var user = RowInput(form, "entry.account", registration ? "entry.register.account" : "entry.login.account",
                .205f, .315f, registration ? 24 : 254, TMP_InputField.ContentType.Standard, account);
            var password = RowInput(form, "entry.password", "entry.password.input",
                .345f, .455f, 128, TMP_InputField.ContentType.Password);
            TMP_InputField emailInput = null;
            TMP_InputField code = null;
            if (registration)
            {
                emailInput = RowInput(form, "entry.email", "entry.email.input", .485f, .595f, 254,
                    TMP_InputField.ContentType.EmailAddress, email);
                code = RowInput(form, "entry.code", "entry.code.input", .625f, .735f, 6,
                    TMP_InputField.ContentType.IntegerNumber, null, .65f);
                Button(Box("Send code", form, .67f, .625f, .93f, .735f), L("entry.code.send"),
                    "ButtonGreen", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Normal,
                    () => Submit(EntryAuthKind.RequestCode), 25, "ButtonGreenPressed");
            }
            if (!string.IsNullOrEmpty(message))
                Label(Box("Authentication result", form, .08f, registration ? .738f : .515f, .92f,
                        registration ? .827f : .65f),
                    message, 23, Pale, TextAlignmentOptions.Center, wrap: true);

            var a = GameUiFeedback.Sound.Cancel;
            Button(Box("Return from account form", form, .08f, .839f, .31f, .951f), L("entry.back"),
                "ButtonGold", new Color32(85, 65, 46, 255), a, () => Submit(EntryAuthKind.Back), 26,
                "ButtonGoldPressed");
            if (!registration)
                Button(Box("Open registration", form, .355f, .839f, .645f, .951f), L("entry.register"),
                    "ButtonGold", new Color32(85, 65, 46, 255), GameUiFeedback.Sound.Normal,
                    () => Submit(EntryAuthKind.OpenRegistration), 26, "ButtonGoldPressed");
            Button(Box("Submit account form", form, .69f, .839f, .92f, .951f),
                L(registration ? "entry.register" : "entry.login"), "ButtonGreen",
                new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Confirm,
                () => Submit(registration ? EntryAuthKind.Register : EntryAuthKind.Login), 26,
                "ButtonGreenPressed");
            return prompt.Task;

            void Submit(EntryAuthKind kind)
            {
                bool needsPassword = kind == EntryAuthKind.Login || kind == EntryAuthKind.Register;
                if (!Complete(prompt, new EntryAuthAction(kind, user.text, needsPassword ? password.text : "",
                    emailInput ? emailInput.text : "", code ? code.text : "")))
                    return;
                password.SetTextWithoutNotify("");
                if (code) code.SetTextWithoutNotify("");
            }
        }

        public void ShowAuthBusy()
        {
            ResetPanel();
            var dialog = Dialog("Authentication pending", L("entry.login"), .30f, .31f, .70f, .69f);
            Label(Box("Authentication pending text", dialog, .08f, .34f, .92f, .74f),
                L("entry.auth.pending"), 28, White, TextAlignmentOptions.Center);
        }

        public Task<bool> SelectRealmAsync(string realmName, CancellationToken token)
        {
            ResetPanel();
            var prompt = Prompt<bool>(token);
            var dialog = Dialog("Realm selection", L("entry.realm"), .27f, .255f, .73f, .745f);
            Label(Box("Realm name", dialog, .11f, .365f, .56f, .66f), realmName, 34, White,
                TextAlignmentOptions.Left, true, wrap: true);
            Picture(Box("Realm divider", dialog, .61f, .385f, .612f, .64f), null,
                new Color32(158, 183, 201, 100));
            Button(Box("Select realm", dialog, .715f, .435f, .925f, .595f), L("entry.realm.select"),
                "ButtonGreen", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Confirm,
                () => Complete(prompt, true), 27, "ButtonGreenPressed");
            Button(Box("Back from realm selection", dialog, .385f, .765f, .615f, .925f), L("entry.back"),
                "ButtonGold", new Color32(85, 65, 46, 255), GameUiFeedback.Sound.Back,
                () => Complete(prompt, false), 25, "ButtonGoldPressed");
            return prompt.Task;
        }

        public void ShowChecking()
        {
            SetBackground(titleBackground);
            ResetPanel();
            RectTransform band = PatchBand(L("entry.patch"));
            Label(Box("Checking message", band, .24f, .220f, .76f, .330f),
                L("entry.patch.checking"), 32, White, TextAlignmentOptions.Center);
            Label(Box("Checking status", band, .24f, .432f, .76f, .560f),
                L("entry.patch.checking.status"), 26, Pale, TextAlignmentOptions.Center);
        }

        public Task<bool> ConfirmDownloadAsync(long bytes, CancellationToken token)
        {
            SetBackground(titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            RectTransform band = PatchBand(L("entry.patch"));
            Label(Box("Download question", band, .21f, .21f, .79f, .405f),
                L("entry.patch.ask"), 32, White, TextAlignmentOptions.Center, wrap: true);
            Label(Box("Download size", band, .25f, .458f, .75f, .546f),
                L("entry.patch.size", Bytes(bytes)), 29, White, TextAlignmentOptions.Center);
            Label(Box("Wi-Fi note", band, .25f, .538f, .75f, .603f),
                L("entry.patch.wifi"), 23, Pale, TextAlignmentOptions.Center);
            Label(Box("Verification note", band, .25f, .66f, .75f, .761f),
                L("entry.patch.verify"), 24, White, TextAlignmentOptions.Center);
            Button(Box("Cancel download", band, .342f, .812f, .490f, .930f), L("entry.patch.cancel"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Cancel,
                () => Complete(prompt, false), 28, "ButtonGreenRoundPressed");
            Button(Box("Accept download", band, .510f, .812f, .658f, .930f), L("entry.patch.continue"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Confirm,
                () => Complete(prompt, true), 28, "ButtonGreenRoundPressed");
            return prompt.Task;
        }

        public void ShowDownload(long receivedBytes, long totalBytes, double bytesPerSecond)
        {
            if (!progressFill)
            {
                SetBackground(titleBackground);
                ResetPanel();
                progressStatus = Label(Box("Download status", panel, .22f, .829f, .78f, .884f),
                    "", 26, White, TextAlignmentOptions.Center, contrast: true);
                progressAmount = Label(Box("Download throughput", panel, .217f, .878f, .63f, .921f),
                    "", 23, White, TextAlignmentOptions.Left, contrast: true);
                progressPercent = Label(Box("Download percentage", panel, .65f, .878f, .783f, .921f),
                    "", 23, White, TextAlignmentOptions.Right, contrast: true);
                var bar = Box("Download track", panel, .217f, .918f, .783f, .928f);
                var track = Art("ProgressTrack");
                Picture(bar, track, track ? White : new Color32(151, 191, 224, 155), sliced: true);
                var mask = Box("Download clip", bar);
                mask.gameObject.AddComponent<RectMask2D>();
                var fill = Art("ProgressFill");
                progressFill = Picture(Box("Download fill", mask), fill,
                    fill ? White : new Color32(119, 230, 255, 255), sliced: true).rectTransform;
            }
            double fraction = totalBytes > 0 ? Math.Max(0, Math.Min(1, (double)receivedBytes / totalBytes)) : 0;
            progressStatus.text = L(fraction >= 1 ? "entry.patch.validating" : "entry.patch.downloading");
            progressAmount.text = L("entry.patch.progress", Bytes(receivedBytes), Bytes(totalBytes), Bytes(bytesPerSecond));
            progressPercent.text = L("entry.patch.percent", Math.Floor(fraction * 100));
            progressFill.anchorMax = new Vector2((float)fraction, 1);
        }

        public Task ShowRestartAsync(CancellationToken token)
        {
            SetBackground(titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            RectTransform band = PatchBand(L("entry.patch.restart.title"));
            Label(Box("Restart instructions", band, .19f, .28f, .81f, .68f),
                L("entry.patch.restart.message"), 31, White, TextAlignmentOptions.Center, wrap: true);
            Button(Box("Quit after hot update", band, .417f, .806f, .583f, .927f), L("entry.patch.restart.exit"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Confirm,
                () => Complete(prompt, true), 28, "ButtonGreenRoundPressed");
            return prompt.Task;
        }

        public Task ShowForceAsync(string detail, CancellationToken token)
        {
            SetBackground(titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            RectTransform band = PatchBand(L("entry.force.title"));
            Label(Box("QQ update instruction", band, .20f, .245f, .80f, .445f),
                L("entry.force.message"), 32, White, TextAlignmentOptions.Center, wrap: true);
            if (!string.IsNullOrWhiteSpace(detail))
                Label(Box("Required version", band, .22f, .46f, .78f, .63f), L("entry.force.minimum", detail), 25, Pale,
                    TextAlignmentOptions.Center, wrap: true);
            Button(Box("Exit on mandatory update", band, .417f, .806f, .583f, .927f), L("entry.force.exit"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Cancel,
                () =>
                {
                    if (!Complete(prompt, true)) return;
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                    Application.Quit();
#endif
                }, 28, "ButtonGreenRoundPressed");
            return prompt.Task;
        }

        public Task<bool> ShowFailureAsync(string message, CancellationToken token)
        {
            SetBackground(titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            RectTransform band = PatchBand(L("entry.error.title"));
            Label(Box("Failure message", band, .185f, .26f, .815f, .70f), message, 29, White,
                TextAlignmentOptions.Center, wrap: true);
            Button(Box("Back after failure", band, .342f, .812f, .490f, .930f), L("entry.back"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Back,
                () => Complete(prompt, false), 28, "ButtonGreenRoundPressed");
            Button(Box("Retry after failure", band, .510f, .812f, .658f, .930f), L("entry.error.retry"),
                "ButtonGreenRound", new Color32(25, 75, 46, 255), GameUiFeedback.Sound.Confirm,
                () => Complete(prompt, true), 28, "ButtonGreenRoundPressed");
            return prompt.Task;
        }

        public Task WaitForStartAsync(Sprite background, CancellationToken token)
        {
            SetBackground(background ? background : titleBackground);
            ResetPanel();
            var prompt = Prompt<bool>(token);
            var target = Box("Tap to start", panel);
            var clickArea = Picture(target, null, Color.clear);
            clickArea.raycastTarget = true;
            var button = target.gameObject.AddComponent<GameButton>();
            button.targetGraphic = clickArea;
            button.transition = Selectable.Transition.None;
            button.FeedbackSound = GameUiFeedback.Sound.Confirm;
            button.onClick.AddListener(() => Complete(prompt, true));
            button.Select();
            var hint = Label(Box("Start prompt", panel, .27f, .785f, .73f, .872f), L("entry.ready"),
                30, White, TextAlignmentOptions.Center, bold: true, contrast: true);
            if (readyMaterial) hint.fontSharedMaterial = readyMaterial;
            hint.gameObject.AddComponent<EntryScreenPulse>().Initialize(hint);
            return prompt.Task;
        }

        public void Hide()
        {
            ReleasePanel();
            if (root) root.gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Hide();
            if (root) Object.Destroy(root.gameObject);
            if (contrastMaterial) Object.Destroy(contrastMaterial);
            if (readyMaterial) Object.Destroy(readyMaterial);
            if (startupFeedback)
            {
                startupFeedback.enabled = false;
                Object.Destroy(startupFeedback);
            }
        }

        private void SetBackground(Sprite sprite)
        {
            if (backdrop.sprite == sprite) return;
            backdrop.sprite = sprite;
            backdrop.enabled = sprite;
            if (sprite) backdropAspect.aspectRatio = sprite.rect.width / sprite.rect.height;
        }

        private void ResetPanel()
        {
            if (disposed) throw new ObjectDisposedException(nameof(EntryScreen));
            ReleasePanel();
            root.gameObject.SetActive(true);
            panel = Box("Startup page", safe);
            interaction = panel.gameObject.AddComponent<CanvasGroup>();
            version.transform.SetAsLastSibling();
        }

        private void ReleasePanel()
        {
            cancelPrompt?.Invoke();
            cancelPrompt = null;
            if (panel)
            {
                panel.gameObject.SetActive(false);
                Object.Destroy(panel.gameObject);
                panel = null;
            }
            progressFill = null;
        }

        private TaskCompletionSource<T> Prompt<T>(CancellationToken token)
        {
            var prompt = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = token.Register(() => prompt.TrySetCanceled());
            _ = prompt.Task.ContinueWith(_ => registration.Dispose(), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            cancelPrompt = () => prompt.TrySetCanceled();
            return prompt;
        }

        private bool Complete<T>(TaskCompletionSource<T> prompt, T value)
        {
            if (!prompt.TrySetResult(value)) return false;
            interaction.interactable = false;
            cancelPrompt = null;
            return true;
        }

        private RectTransform Dialog(string name, string title, float left, float top, float right, float bottom)
        {
            ScrimLayer(panel);
            RectTransform dialog = Box(name, panel, left, top, right, bottom);
            var frame = Art("PanelFrame");
            Picture(dialog, frame, frame ? White : PanelColor, sliced: true).raycastTarget = true;
            Label(Box("Dialog title", dialog, .08f, .025f, .92f, .169f),
                title, 37, White, TextAlignmentOptions.Center, true);
            Ornaments(Box("Dialog divider", dialog, .05f, .156f, .95f, .19f));
            return dialog;
        }

        private RectTransform PatchBand(string title)
        {
            ScrimLayer(panel, matchSourceBrightness: true);
            RectTransform band = Box("Update strip", panel, 0f, .145f, 1f, .855f);
            var frame = Art("PatchPanel");
            Color window = frame ? (Color)new Color32(255, 255, 255, 216) : new Color32(7, 26, 53, 194);
            Picture(band, frame, SourceOpacity(window), sliced: true).raycastTarget = true;
            if (!frame)
            {
                var edge = new Color32(168, 195, 222, 135);
                Picture(Box("Upper edge", band, 0, 0, 1, .004f), null, edge);
                Picture(Box("Lower edge", band, 0, .996f, 1, 1f), null, edge);
            }
            Label(Box("Patch title", band, .20f, .041f, .80f, .134f), title, 41, White,
                TextAlignmentOptions.Center, true);
            Ornaments(Box("Patch divider", band, .367f, .147f, .633f, .172f));
            return band;
        }

        private static void ScrimLayer(RectTransform page, bool matchSourceBrightness = false) =>
            Picture(Box("Modal dimmer", page), null,
                matchSourceBrightness ? SourceOpacity(Scrim) : Scrim).raycastTarget = true;

        // Cocos authored alpha against gamma-encoded colors. Match its transmitted light in a linear project.
        private static Color SourceOpacity(Color tint)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                tint.a = 1f - Mathf.GammaToLinearSpace(1f - tint.a);
            return tint;
        }

        private void Ornaments(RectTransform line)
        {
            var sprite = Art("Divider");
            Picture(line, sprite, sprite ? new Color32(198, 216, 230, 201) :
                new Color32(151, 184, 205, 115), sliced: true);
            if (!sprite)
            {
                line.anchorMin = new Vector2(line.anchorMin.x, (line.anchorMin.y + line.anchorMax.y) * .5f);
                line.anchorMax = new Vector2(line.anchorMax.x, line.anchorMin.y);
                line.sizeDelta = new Vector2(0f, 1.5f);
            }
        }

        private TMP_InputField RowInput(RectTransform parent, string titleKey, string placeholderKey,
            float top, float bottom, int limit, TMP_InputField.ContentType kind, string value = null, float right = .93f)
        {
            Label(Box("Field label", parent, .075f, top, .315f, bottom), L(titleKey), 27, Pale,
                TextAlignmentOptions.Left);
            var rect = Box(titleKey, parent, .32f, top, right, bottom);
            var fieldSprite = Art("InputField");
            var surface = Picture(rect, fieldSprite, fieldSprite ? White : new Color32(233, 237, 242, 255),
                sliced: true);
            surface.raycastTarget = true;
            var viewport = Box("Input viewport", rect, 0, 0, 1, 1, 14, 4, -14, -4);
            viewport.gameObject.AddComponent<RectMask2D>();
            var edit = Label(Box("Input text", viewport), "", 26, DarkInk, TextAlignmentOptions.Left);
            var placeholder = Label(Box("Placeholder", viewport), L(placeholderKey), 25,
                new Color32(93, 111, 128, 255), TextAlignmentOptions.MidlineLeft);

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = surface;
            input.textViewport = viewport;
            input.textComponent = edit;
            input.placeholder = placeholder;
            input.characterLimit = limit;
            input.contentType = kind;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.SetTextWithoutNotify(value ?? "");
            return input;
        }

        private GameButton Button(RectTransform rect, string title, string artName, Color fallback,
            GameUiFeedback.Sound sound, Action action, float fontSize, string downArt)
        {
            var sprite = Art(artName);
            var image = Picture(rect, sprite, sprite ? White : fallback, sliced: true);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<GameButton>();
            button.targetGraphic = image;
            button.FeedbackSound = sound;
            var pressed = Art(downArt);
            if (pressed)
            {
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState { pressedSprite = pressed, selectedSprite = sprite,
                    highlightedSprite = sprite };
            }
            else
            {
                var colors = ColorBlock.defaultColorBlock;
                colors.highlightedColor = new Color(.96f, .99f, 1f);
                colors.pressedColor = new Color(.74f, .83f, .91f);
                button.colors = colors;
            }
            bool patchButton = artName == "ButtonGreenRound";
            Color caption = patchButton ? (Color)new Color32(220, 226, 223, 255) : White;
            Label(Box("Button label", rect, .06f, .04f, .94f, .96f), title, fontSize, caption,
                TextAlignmentOptions.Center, !patchButton && artName != "LoginButton", !patchButton);
            button.onClick.AddListener(() => action());
            return button;
        }

        private GameText Label(RectTransform rect, string text, float size, Color color, TextAlignmentOptions alignment,
            bool bold = false, bool contrast = false, bool wrap = false)
        {
            var label = rect.gameObject.AddComponent<GameText>();
            if (font) label.font = font;
            if (contrast && contrastMaterial) label.fontSharedMaterial = contrastMaterial;
            label.richText = false;
            label.raycastTarget = false;
            label.enableWordWrapping = wrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.fontSize = size;
            label.fontSizeMin = Mathf.Max(12, size * .58f);
            label.fontSizeMax = size;
            label.enableAutoSizing = true;
            label.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            label.alignment = alignment;
            label.color = color;
            label.text = text ?? "";
            return label;
        }

        private Sprite Art(string name)
        {
            if (!art.TryGetValue(name, out Sprite sprite))
            {
                sprite = Resources.Load<Sprite>("Entry/" + name);
                art.Add(name, sprite);
            }
            return sprite;
        }

        private static Image Picture(RectTransform rect, Sprite sprite, Color color, bool sliced = false)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            if (sliced && sprite && sprite.border.sqrMagnitude > 0) image.type = Image.Type.Sliced;
            return image;
        }

        // Coordinates follow the reference screen and are measured downwards from the top left.
        private static RectTransform Box(string name, Transform parent, float left = 0, float top = 0,
            float right = 1, float bottom = 1, float insetLeft = 0, float insetBottom = 0, float insetRight = 0,
            float insetTop = 0)
        {
            var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(left, 1 - bottom);
            rect.anchorMax = new Vector2(right, 1 - top);
            rect.offsetMin = new Vector2(insetLeft, insetBottom);
            rect.offsetMax = new Vector2(insetRight, insetTop);
            return rect;
        }

        private static string L(string key, params object[] args) => GameText.Format(Locale, key, args);

        private static string Bytes(double bytes)
        {
            bytes = Math.Max(0, bytes);
            if (bytes >= 1073741824) return $"{bytes / 1073741824:0.00} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024:0.0} KB";
            return $"{bytes:0} B";
        }
    }

    [AddComponentMenu("")]
    internal sealed class EntryScreenPulse : MonoBehaviour
    {
        private GameText target;
        private float start;
        internal void Initialize(GameText label) { target = label; start = Time.unscaledTime; }

        private void Update()
        {
            if (target)
                target.alpha = Mathf.Lerp(.56f, 1, .5f + .5f * Mathf.Cos((Time.unscaledTime - start) * 3.8f));
        }
    }
}
