using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.Common;
using AstralOath.Client.Summon;
using UnityEngine.UI;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    internal static class SummonPlayerSmoke
    {
        private const string ResizeFlag = "-astral-summon-resize-smoke";

        internal static bool IsRequested()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return false;
            var arguments = Environment.GetCommandLineArgs();
            return Array.IndexOf(arguments, "-astral-startup-smoke") >= 0 &&
                (Array.IndexOf(arguments, "-astral-summon-smoke") >= 0 || Array.IndexOf(arguments, ResizeFlag) >= 0);
        }

        internal static async Task RunAsync(GameLaunchContext context, StartupNavigation navigation, CancellationToken lifetime)
        {
            if (!context.Services.Get<GameServerEndpoint>().Address.IsLoopback)
                throw new InvalidOperationException("The Summon Player smoke requires a loopback server.");

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            bool resize = Array.IndexOf(Environment.GetCommandLineArgs(), ResizeFlag) >= 0;
            deadline.CancelAfter(TimeSpan.FromSeconds(resize ? 100 : 45));
            if (resize)
            {
                if (Application.platform != RuntimePlatform.WindowsPlayer ||
                    SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                    throw new InvalidOperationException("The resize smoke needs a windowed Windows Player with graphics.");
                var endpoint = context.Services.Get<GameServerEndpoint>().Address;
                // Never reuse the account bearer or persistent device guest. This development endpoint
                // allocates a new player and random session for each POST, with its own test balance.
                using var guest = new ArenaApi(endpoint, deadline.Token);
                string guestName = "ResizeQA" + Guid.NewGuid().ToString("N").Substring(0, 12);
                string bearer = await guest.GetSessionTokenAsync(guestName, deadline.Token);
                using var draws = new SummonApi(endpoint, _ => Task.FromResult(bearer), deadline.Token);
                await navigation.VerifySummonAsync(deadline.Token, draws);
            }
            else await navigation.VerifySummonAsync(deadline.Token);
            Debug.Log("ASTRAL_SUMMON_PLAYER_OK");
        }

        internal static async Task VerifyResultResizeAsync(BindingMap ui, CancellationToken token)
        {
            void Click(string key)
            {
                var button = ui.Require<GameButton>(key);
                if (!button.isActiveAndEnabled || !button.IsInteractable())
                    throw new InvalidOperationException("Unavailable during resize smoke: " + key);
                button.onClick.Invoke();
            }
            async Task Until(Func<bool> ready)
            {
                while (!ready())
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Yield();
                }
            }
            Click("ten");
            Click("confirmYes");
            var scene = ui.Require<RawImage>("effectCanvas");
            var banner = ui.Require<GameObject>("mainPage");
            var result = ui.Require<GameObject>("resultPanel");
            await Until(() => ui.Require<GameObject>("revealPanel").activeInHierarchy && scene.enabled && scene.texture);
            Click("skip");
            await Until(() => result.activeInHierarchy);
            if (ui.Require<RectTransform>("resultItems").GetComponentsInChildren<BindingMap>().Length != 10)
                throw new InvalidOperationException("Expected ten settled summon cards.");

            var startSize = new Vector2Int(Screen.width, Screen.height);
            FullScreenMode startMode = Screen.fullScreenMode;
            var sample = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            Texture last = null;
            string failure = null;
            int replacements = 0;
            void InspectFrame()
            {
                if (failure != null) return;
                if (banner.activeInHierarchy || !result.activeInHierarchy || !scene.isActiveAndEnabled)
                {
                    failure = "The wrong page was visible during native window resizing.";
                    return;
                }
                var texture = scene.texture as RenderTexture;
                if (!texture || !texture.IsCreated()) { failure = "The result has no live camera image."; return; }
                if (texture == last) return;
                last = texture;
                ++replacements;
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = texture;
                    sample.ReadPixels(new Rect(texture.width / 2, texture.height / 2, 1, 1), 0, 0);
                    sample.Apply();
                    Color pixel = sample.GetPixel(0, 0);
                    if (pixel.a < .5f || Mathf.Max(pixel.r, pixel.g, pixel.b) < .02f)
                        failure = "A blank camera image reached the result view during resize.";
                }
                finally { RenderTexture.active = previous; }
            }
            try
            {
                await Task.Yield();
                Canvas.willRenderCanvases += InspectFrame;
                foreach (var size in new[] { new Vector2Int(960, 540), new Vector2Int(960, 720),
                    new Vector2Int(1100, 620), new Vector2Int(1024, 640), new Vector2Int(1040, 600) })
                {
                    Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
                    await Until(() => failure != null || Screen.width == size.x && Screen.height == size.y);
                    for (int frame = 0; frame < 3; ++frame) { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                    if (failure != null) throw new InvalidOperationException(failure);
                    Debug.Log($"ASTRAL_SUMMON_RESIZE_FRAME {Screen.width}x{Screen.height}");
                }
                if (replacements < 3) throw new InvalidOperationException("Too few actual render target changes.");
            }
            finally
            {
                Canvas.willRenderCanvases -= InspectFrame;
                Screen.SetResolution(startSize.x, startSize.y, startMode);
                UnityEngine.Object.Destroy(sample);
            }

            Click("resultAgain");
            if (banner.activeInHierarchy || !result.activeInHierarchy || !ui.Require<GameObject>("confirmPanel").activeInHierarchy)
                throw new InvalidOperationException("The repeat confirmation lost the result page.");
            Click("confirmNo");
            Click("resultClose");
            if (!banner.activeInHierarchy || result.activeInHierarchy)
                throw new InvalidOperationException("The result back action failed to restore the summon lobby.");
            Click("back");
            await Until(() => !ui || !ui.gameObject.activeInHierarchy);
            Debug.Log($"ASTRAL_SUMMON_RESIZE_OK sizes=5 textures={replacements} cards=10");
        }
    }
}
