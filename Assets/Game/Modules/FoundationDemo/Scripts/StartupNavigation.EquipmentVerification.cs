using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal sealed partial class StartupNavigation
    {
        private Func<CancellationToken, Task<string>> equipmentSmokeBearer;

        // Explicit development-only, loopback-only player verification; normal login never creates this delegate.
        internal async Task VerifyEquipmentEnhancementAsync(CancellationToken cancellation)
        {
            if (!EquipmentPlayerSmoke.EnhancementRequested || !endpoint.IsLoopback || equipmentSmokeBearer == null)
                throw new InvalidOperationException("Equipment enhancement verification requires an isolated development session.");
            using var gm = new DevelopmentGmApi(endpoint, equipmentSmokeBearer);
            var gmState = await gm.SendAsync(null, cancellation);
            foreach (string kind in new[] { "gold", "ma_est1e", "ma_est2e", "ma_est4e", "ma_est6e" })
                await gm.SendAsync(new GmAction { requestId = Guid.NewGuid().ToString("N"), action = "set", key = kind,
                    value = kind == "gold" ? 5000000 : 100 }, cancellation);
            var before = await equipment.FetchAsync(cancellation);
            await gm.SendAsync(new GmForgeAction { requestId = Guid.NewGuid().ToString("N"), action = "forgeEquipment", key = "boots", value = 1,
                forge = new GmForgeRecipe { itemLevel = 85, enhanceLevel = 0, grade = "epic", setId = "speed", mainKind = "healthPct",
                    rulesVersion = gmState.forgeRules.enhancementRulesVersion, substats = new[] {
                        new GmForgeSubstat { kind = "attack", value = 34, rolls = 1 },
                        new GmForgeSubstat { kind = "critChanceBp", value = 400, rolls = 1 },
                        new GmForgeSubstat { kind = "defensePct", value = 500, rolls = 1 },
                        new GmForgeSubstat { kind = "critDamageBp", value = 700, rolls = 1 } } } }, cancellation);
            var fresh = await equipment.FetchAsync(cancellation);
            var item = fresh.items.Single(row => !before.items.Any(old => old.instanceId == row.instanceId));
            if (!item.enhancementSupported) throw new InvalidDataException("The source-forged equipment is not enhanceable.");
            firstReads.ApplyEquipment(fresh);
            await OpenInventory(false, cancellation);
            BindingMap page = parent.GetComponentsInChildren<BindingMap>().First(map => map.name.StartsWith("HeroInventory", StringComparison.Ordinal));
            VerifyClick(page, "tabEquipment");
            var pane = page.Require<BindingMap>("gearBrowser").Require<BindingMap>("bagPane");
            VerifyClick(pane, "filter_boots");
            BindingMap chosen = null;
            await VerifyUntil(() =>
            {
                chosen = pane.Require<ScrollRect>("items").content.Cast<Transform>()
                    .Where(child => child.gameObject.activeInHierarchy && child.name == item.instanceId)
                    .Select(child => child.GetComponent<BindingMap>()).FirstOrDefault();
                return chosen;
            }, cancellation);
            VerifyClick(chosen, "select"); VerifyClick(pane, "enhance");
            var enhancement = page.Require<BindingMap>("gearEnhancement");
            await VerifyUntil(() => enhancement.gameObject.activeInHierarchy && enhancement.Require<GameButton>("auto").interactable, cancellation);
            await CaptureEquipmentVerification("idle", cancellation);
            VerifyClick(enhancement.Require<BindingMap>("row0"), "select");
            await CaptureEquipmentVerification("quantity", cancellation);
            VerifyClick(enhancement, "quantityConfirm"); VerifyClick(enhancement, "submit");
            await VerifyUntil(() => enhancement.Require<GameButton>("auto").interactable &&
                !enhancement.Require<GameText>("experience").text.Contains("exp 0 /"), cancellation);
            var partial = await equipment.FetchAsync(cancellation);
            var partialItem = partial.items.Single(row => row.instanceId == item.instanceId);
            if (partialItem.enhanceLevel != 0 || partialItem.xp < 100 || partialItem.xp > 200 ||
                partial.materials.gold != fresh.materials.gold - 1600)
                throw new InvalidDataException("Partial enhancement experience or cost did not persist.");
            await CaptureEquipmentVerification("partial-xp", cancellation);
            VerifyClick(enhancement, "auto"); VerifyClick(enhancement, "auto3");
            await CaptureEquipmentVerification("preview-plus3", cancellation);
            VerifyClick(enhancement, "submit");
            await VerifyUntil(() => enhancement.Require<GameButton>("auto").interactable &&
                enhancement.Require<BindingMap>("card").Require<BindingMap>("badge").Require<GameText>("label").text != "", cancellation);
            var raised = (await equipment.FetchAsync(cancellation)).items.Single(row => row.instanceId == item.instanceId);
            if (raised.enhanceLevel < 3 || raised.enhanceLevel >= 15 ||
                enhancement.Require<BindingMap>("card").Require<BindingMap>("badge").Require<Image>("mark").sprite.name != "_upgrade_num_orange")
                throw new InvalidDataException("Nonmaximum enhancement did not use the original orange badge.");
            await CaptureEquipmentVerification("orange", cancellation);
            VerifyClick(enhancement, "auto"); VerifyClick(enhancement, "auto15"); VerifyClick(enhancement, "submit");
            await VerifyUntil(() => enhancement.Require<BindingMap>("card").Require<BindingMap>("badge").Require<GameText>("label").text == "+15", cancellation);
            var maximum = (await equipment.FetchAsync(cancellation)).items.Single(row => row.instanceId == item.instanceId);
            if (maximum.enhanceLevel != 15 || maximum.mainStat.value != 6000 ||
                enhancement.Require<BindingMap>("card").Require<BindingMap>("badge").Require<Image>("mark").sprite.name != "_upgrade_num_red" ||
                enhancement.Require<GameButton>("submit").interactable)
                throw new InvalidDataException("Maximum enhancement or cap display is incorrect.");
            await CaptureEquipmentVerification("red-plus15", cancellation);
            VerifyClick(enhancement, "back");
            if (enhancement.gameObject.activeSelf || !pane.gameObject.activeInHierarchy)
                throw new InvalidDataException("Enhancement did not restore the original bag page.");
            Debug.Log("ASTRAL_EQUIPMENT_ENHANCEMENT_PLAYER_OK partialXp=" + partialItem.xp + " intermediate=" + raised.enhanceLevel +
                " maximum=" + maximum.enhanceLevel + " main=" + maximum.mainStat.value + " screen=" + Screen.width + "x" + Screen.height);
        }

        private static void VerifyClick(BindingMap map, string key)
        {
            var button = map.Require<GameButton>(key);
            if (!button.isActiveAndEnabled || !button.IsInteractable()) throw new InvalidOperationException("Unavailable verification button: " + key);
            button.onClick.Invoke();
        }

        private static async Task VerifyUntil(Func<bool> predicate, CancellationToken cancellation)
        {
            double until = Time.realtimeSinceStartupAsDouble + 30;
            while (!predicate())
            {
                if (Time.realtimeSinceStartupAsDouble > until) throw new TimeoutException("Equipment enhancement player verification timed out.");
                await Task.Delay(40, cancellation);
            }
        }

        private static async Task CaptureEquipmentVerification(string name, CancellationToken cancellation)
        {
            string directory = Environment.GetEnvironmentVariable("ASTRAL_EQUIPMENT_CAPTURE_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            directory = Path.GetFullPath(directory);
            if (!directory.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Player captures must stay on D drive.");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            await VerifyUntil(() => File.Exists(path), cancellation);
        }
    }
}
