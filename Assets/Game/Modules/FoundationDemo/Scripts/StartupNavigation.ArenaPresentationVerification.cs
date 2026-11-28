using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        internal static bool ArenaPresentationVerificationRequested =>
            !Application.isEditor && Debug.isDebugBuild &&
            Array.IndexOf(Environment.GetCommandLineArgs(), "-astral-arena-presentation-check") >= 0;

        internal void StartArenaPresentationVerification(CancellationToken ct) => _ = VerifyArenaPresentation(ct);

        private async Task VerifyArenaPresentation(CancellationToken lifetimeToken)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetimeToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(3));
            var ct = deadline.Token;
            try
            {
                if (!endpoint.IsLoopback || !int.TryParse(Environment.GetEnvironmentVariable("ASTRAL_ARENA_CASE_CHANNEL"), out int channel))
                    throw new InvalidOperationException("A local test server and prepared reward channel are required.");
                string output = Environment.GetEnvironmentVariable("ASTRAL_ARENA_PRESENTATION_DIR");
                if (string.IsNullOrEmpty(output) || !Path.IsPathRooted(output))
                    throw new ArgumentException("An absolute capture directory is required.");
                Directory.CreateDirectory(output);
                await OpenTower(ct);
                var tower = (await towerView.Get()).Root.GetComponent<BindingMap>();
                var rows = tower.Require<ScrollRect>("list").content;
                if (rows.childCount == 0) throw new InvalidDataException("Empty stage list.");
                await Capture("01-arena");
                tower.Require<GameButton>("challenge").onClick.Invoke();
                var formation = tower.Require<GameObject>("overlay").GetComponent<BindingMap>();
                await Until(() => formation.Require<GameButton>("confirm").isActiveAndEnabled &&
                    formation.Require<GameButton>("confirm").interactable);
                await Capture("02-formation");
                formation.Require<GameButton>("back").onClick.Invoke();
                await Until(() => !formation.gameObject.activeInHierarchy);

                tower.Require<GameButton>("shop").onClick.Invoke();
                var shop = (await shopView.Get()).Root.GetComponent<BindingMap>();
                var products = shop.Require<ScrollRect>("list");
                await Until(() => shop.gameObject.activeInHierarchy && products.content.childCount >= 17);
                await Capture("03-shop");
                products.horizontalNormalizedPosition = 1;
                await Capture("04-shop-end");
                var product = products.content.Cast<Transform>().Select(x => x.GetComponent<BindingMap>())
                    .First(x => x && x.Require<GameButton>("directory").isActiveAndEnabled);
                product.Require<GameButton>("directory").onClick.Invoke();
                await Capture("05-season-directory");
                if (!windows.TryCloseOverlay()) throw new InvalidOperationException("Season directory failed to open.");
                shop.Require<GameButton>("back").onClick.Invoke();
                await Until(() => tower.gameObject.activeInHierarchy && rows.childCount > 0);

                await chatSession.JoinAsync(channel, ct);
                tower.GetComponentsInChildren<BindingMap>(false).Single(x => x.name == "ChatEntry")
                    .Require<GameButton>("button").onClick.Invoke();
                await Until(() => chatPanel?.IsOpen == true && chatPanel.Parent
                    .GetComponentsInChildren<BindingMap>(false).Any(x => x.name.StartsWith("ChannelChat",StringComparison.Ordinal)
                        && x.Require<ScrollRect>("messages").content.childCount >= 2));
                var chat = chatPanel.Parent.GetComponentsInChildren<BindingMap>(false)
                    .First(x => x.gameObject.name.StartsWith("ChannelChat",StringComparison.Ordinal));
                await Capture("06-channel");
                foreach (string kind in new[] {"hero5","artifact5"})
                {
                    var entry = chatSession.Messages.Last(m => m.kind == kind);
                    var row = chat.Require<ScrollRect>("messages").content.Find("Message " + entry.id).GetComponent<BindingMap>();
                    row.Require<GameButton>("rewardButton").onClick.Invoke();
                    string prefix = kind == "hero5" ? "HeroInformation" : "ArtifactInformation";
                    await Until(() => informationPanel?.IsOpen == true && informationPanel.Parent
                        .GetComponentsInChildren<BindingMap>(false).Any(x => x.name.StartsWith(prefix,StringComparison.Ordinal)));
                    await Capture(kind == "hero5" ? "07-hero" : "08-artifact");
                    var info = informationPanel.Parent.GetComponentsInChildren<BindingMap>(false)
                        .Single(x => x.name.StartsWith(prefix,StringComparison.Ordinal));
                    if (kind == "hero5")
                    {
                        info.Require<GameButton>("skill2").onClick.Invoke();
                        await Capture("09-hero-skill");
                    }
                    else
                    {
                        info.Require<GameButton>("zoom").onClick.Invoke();
                        await Capture("10-artifact-painting");
                        info.Require<GameButton>("rotate").onClick.Invoke();
                        await Capture("11-artifact-rotated");
                    }
                    informationPanel.Dispose();
                }
                Debug.Log("ASTRAL_ARENA_PRESENTATION_OK");
                Application.Quit(0);

                async Task Capture(string name)
                {
                    await Task.Delay(420,ct);
                    string path = Path.Combine(output,name + ".png");
                    if (File.Exists(path)) throw new IOException("Capture already exists: " + name);
                    Canvas.ForceUpdateCanvases();
                    ScreenCapture.CaptureScreenshot(path);
                    await Until(() => File.Exists(path) && new FileInfo(path).Length > 1024);
                    Debug.Log("ASTRAL_ARENA_FRAME " + name + " " + Screen.width + "x" + Screen.height);
                }
                async Task Until(Func<bool> ready)
                {
                    while (!ready()) await Task.Delay(40,ct);
                }
            }
            catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Application.Quit(1);
            }
        }
    }
}
