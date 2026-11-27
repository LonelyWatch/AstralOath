using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using AstralOath.Client.Common;
using Newtonsoft.Json;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        internal void StartSocialVerification(CancellationToken cancellation) => _ = RunSocialVerificationAsync(cancellation);

        private async Task RunSocialVerificationAsync(CancellationToken cancellation)
        {
            try
            {
                await SocialPlayerSmoke.VerifyAsync(new SocialPlayerSmoke.Hooks
                {
                    Endpoint = endpoint, Root = parent, ProfileSession = profileSession, ChatSession = chatSession,
                    GetBearerAsync = socialBearer, ShowHomeAsync = ShowHome,
                    OpenProfileAsync = OpenPlayerProfileAsync, OpenChatAsync = OpenChatAsync, OpenMoreAsync = OpenMore,
                    CloseProfile = () => profilePanel?.Dispose(), CloseChat = () => chatPanel?.Dispose(),
                    CloseNotice = () => noticePanel?.Dispose(),
                    CaptureDirectory = Environment.GetEnvironmentVariable("ASTRAL_SOCIAL_CAPTURE_DIR"),
                    BuildIdentifier = Environment.GetEnvironmentVariable("ASTRAL_SOCIAL_BUILD_ID") ?? "R44 candidate"
                }, cancellation);
                await VerifySummonRosterPlayerAsync(cancellation);
                Debug.Log("ASTRAL_SOCIAL_PLAYER_OK");
                Application.Quit(0);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception error)
            {
                Debug.LogException(error);
                Application.Quit(1);
            }
        }

        private async Task VerifySummonRosterPlayerAsync(CancellationToken cancellation)
        {
            SocialPlayerSmoke.CheckTarget(endpoint);
            var view = await ShowSummonPageAsync(cancellation);
            var map = view.Root.GetComponent<BindingMap>();
            map.Require<GameButton>("covenant").onClick.Invoke();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            var targets = Enumerable.Range(0, 7).Select(i => map.Require<RawImage>("wishHero" + i)).ToArray();
            while (targets.Any(image => !image.isActiveAndEnabled || !(image.texture is RenderTexture texture) || !texture.IsCreated()))
                await Task.Delay(40, deadline.Token);
            await Task.Delay(200, deadline.Token);
            var dimensions = targets.Select(image =>
            {
                var texture = (RenderTexture)image.texture;
                var corners = new Vector3[4];
                image.rectTransform.GetWorldCorners(corners);
                var canvas = image.canvas.rootCanvas;
                var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                var low = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                var top = RectTransformUtility.WorldToScreenPoint(camera, corners[1]);
                var right = RectTransformUtility.WorldToScreenPoint(camera, corners[3]);
                var pixels = new Vector2Int(Mathf.CeilToInt(Vector2.Distance(low, right)), Mathf.CeilToInt(Vector2.Distance(low, top)));
                int expected = Mathf.Clamp(Mathf.Max(480, Mathf.Max(pixels.x, pixels.y)), 1, Mathf.Min(4096, SystemInfo.maxTextureSize));
                if (Mathf.Abs(texture.width - expected) > 1 || texture.width != texture.height)
                    throw new InvalidDataException("Summon portrait resolution differs from its installed projected size.");
                return new { width = texture.width, height = texture.height, projectedWidth = pixels.x, projectedHeight = pixels.y };
            }).ToArray();
            foreach (int i in Enumerable.Range(0, 7))
            {
                var text = map.Require<GameText>("wishHero" + i + "Name");
                text.ForceMeshUpdate();
                var glyph = text.textInfo.characterInfo.Take(text.textInfo.characterCount).First(item => item.isVisible);
                float shear = (glyph.topLeft.x - glyph.bottomLeft.x) / (glyph.topLeft.y - glyph.bottomLeft.y);
                if (Mathf.Abs(shear - .18f) > .01f) throw new InvalidDataException("Installed summon name skew differs from the reference.");
            }
            string output = Path.Combine(Environment.GetEnvironmentVariable("ASTRAL_SOCIAL_CAPTURE_DIR"), "summon-roster");
            Directory.CreateDirectory(output);
            if (Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Summon evidence directory already exists.");
            var frameTimes = new List<float>();
            int frame = -1;
            void Sample()
            {
                if (frame == Time.frameCount) return;
                frame = Time.frameCount;
                frameTimes.Add(Time.unscaledDeltaTime * 1000);
            }
            Canvas.willRenderCanvases += Sample;
            try { await Task.Delay(1800, deadline.Token); }
            finally { Canvas.willRenderCanvases -= Sample; }
            // Retain an actual consecutive sequence rather than calling one still an animation check.
            for (int i = 0; i < 12; i++)
            {
                ScreenCapture.CaptureScreenshot(Path.Combine(output, "idle-" + i.ToString("D2") + ".png"));
                await Task.Delay(100, deadline.Token);
            }
            frameTimes.Sort();
            float Percentile(float value) => frameTimes.Count == 0 ? -1 : frameTimes[Mathf.Min(frameTimes.Count - 1, (int)Math.Ceiling(frameTimes.Count * value) - 1)];
            File.WriteAllText(Path.Combine(output, "roster-player.json"), JsonConvert.SerializeObject(new
            {
                width = Screen.width, height = Screen.height, targets = dimensions,
                rgbaSurfaceBytes = dimensions.Sum(item => (long)item.width * item.height * 4),
                frameSamples = frameTimes.Count, p95Ms = Percentile(.95f), p99Ms = Percentile(.99f),
                maximumMs = frameTimes.Count == 0 ? -1 : frameTimes[frameTimes.Count - 1],
                measurementScope = "Native idle UI for 1.8 seconds before PNG capture; no before-performance baseline or GPU timing claim",
                animationEvidence = "12 actual Player framebuffer captures at requested 100ms intervals; capture overhead affects exact cadence",
                audioReview = "not performed", buildIdentifier = Environment.GetEnvironmentVariable("ASTRAL_SOCIAL_BUILD_ID")
            }, Formatting.Indented));
        }
    }
}
