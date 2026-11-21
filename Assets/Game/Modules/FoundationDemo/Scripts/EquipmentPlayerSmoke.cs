using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    internal static class EquipmentPlayerSmoke
    {
        internal static bool IsRequested()
        {
            var args = Environment.GetCommandLineArgs();
            return !Application.isEditor && Debug.isDebugBuild && Array.IndexOf(args, "-astral-arena-battle-smoke") >= 0 &&
                Array.IndexOf(args, "-astral-equipment-smoke") >= 0;
        }

        internal static void Start(GameLaunchContext context, CancellationToken token)
        {
            Application.runInBackground = true;
            _ = RunAndQuitAsync(context, token);
        }

        private static async Task RunAndQuitAsync(GameLaunchContext context, CancellationToken token)
        {
            try
            {
                await Task.Yield();
                var endpoint = context.Services.Get<GameServerEndpoint>().Address;
                if (!endpoint.IsLoopback)
                    throw new InvalidOperationException("Equipment UI smoke requires an isolated training server.");
                await RunAsync(context, token);
                Application.Quit(0);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
        }

        private static async Task RunAsync(GameLaunchContext context, CancellationToken token)
        {
            BindingMap Page(string name)
            {
                foreach (var map in context.ViewRoot.GetComponentsInChildren<BindingMap>())
                    if (map.name == name || map.name == name + "(Clone)") return map;
                return null;
            }

            Click(Page("HomeWindow"), "bag");
            await Until(() => Page("HeroInventory"), "open the bag", token);
            var bag = Page("HeroInventory");
            Click(bag, "tabEquipment");
            var equipment = bag.Require<BindingMap>("gearBrowser");
            var browser = equipment.Require<BindingMap>("bagPane");
            await Until(() => browser.gameObject.activeInHierarchy && CardCount(browser) == 7, "show seven training items", token);
            Click(browser, "filter_helmet");
            await Until(() => CardCount(browser) == 1, "filter to the training helmet", token);
            var helmet = Card(browser);
            Click(helmet, "select");
            var mainStat = equipment.Require<BindingMap>("bagMain").Require<GameText>("value");
            if (!helmet.Require<Image>("icon").sprite || string.IsNullOrEmpty(mainStat.text))
                throw new InvalidOperationException("The packaged helmet card is missing its image or attributes.");
            string instance = helmet.name;
            Click(bag, "back");
            await Until(() => Page("HomeWindow"), "return home", token);
            Click(Page("HomeWindow"), "more");
            await Until(() => Page("HomeMoreMenu"), "open the menu", token);
            Click(Page("HomeMoreMenu"), "heroes");
            await Until(() => Page("HeroInventory") && Page("HeroInventory").Require<GameButton>("gearOpen_helmet").isActiveAndEnabled,
                "open the hero", token);
            var hero = Page("HeroInventory");
            var item = hero.Require<BindingMap>("equipmentSlot_helm").Require<Image>("item");
            var health = hero.Require<GameText>("detailStat_health");
            await Until(() => int.TryParse(health.text, out _) && !item.enabled, "read the unequipped panel", token);
            int baseline = int.Parse(health.text);
            browser = hero.Require<BindingMap>("gearBrowser");
            var single = browser.Require<BindingMap>("singleSlot");
            Click(hero, "gearOpen_helmet");
            await Until(() => single.Require<GameObject>("comparison").activeInHierarchy &&
                CardCount(single, "candidates") == 1 && Card(single, "candidates").name == instance,
                "show the same helmet in the slot picker", token);
            Click(Card(single, "candidates"), "select");
            await Until(() => single.Require<BindingMap>("candidate").Require<GameObject>("detail").activeInHierarchy, "preview the candidate", token);
            Click(single, "equip");
            await Until(() => !browser.gameObject.activeSelf && item.enabled && item.sprite && health.text == (baseline + 80).ToString(), "equip and add 80 HP", token);
            Click(hero, "gearOpen_helmet");
            await Until(() => single.Require<GameObject>("popup").activeInHierarchy, "inspect the equipped helmet", token);
            Click(single, "popupRemove");
            await Until(() => !browser.gameObject.activeSelf && !item.enabled && health.text == baseline.ToString(), "restore the unequipped panel", token);
            Debug.Log("ASTRAL_EQUIPMENT_PLAYER_OK items=7 health=" + baseline + "," + (baseline + 80) + "," + baseline);
        }

        private static int CardCount(BindingMap browser, string list = "items")
        {
            int count = 0;
            foreach (Transform child in browser.Require<ScrollRect>(list).content)
                if (child.gameObject.activeInHierarchy) count++;
            return count;
        }

        private static BindingMap Card(BindingMap browser, string list = "items")
        {
            foreach (Transform child in browser.Require<ScrollRect>(list).content)
                if (child.gameObject.activeInHierarchy) return child.GetComponent<BindingMap>();
            throw new InvalidOperationException("The filtered item is missing.");
        }

        private static void Click(BindingMap map, string key)
        {
            var button = map.Require<GameButton>(key);
            if (!button.isActiveAndEnabled || !button.IsInteractable()) throw new InvalidOperationException("Unavailable equipment button: " + key);
            button.onClick.Invoke();
        }

        private static async Task Until(Func<bool> ready, string step, CancellationToken token)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (!ready())
            {
                token.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartupAsDouble >= deadline) throw new TimeoutException("Equipment UI smoke timed out waiting to " + step + ".");
                await Task.Delay(40, token);
            }
        }
    }
}
