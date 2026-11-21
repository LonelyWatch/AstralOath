using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class ArtifactInventoryBinder
    {
        private sealed class ListOptions
        {
            public int order;
            public bool reversed;
            public readonly HashSet<int> stars = new HashSet<int>();
            public readonly bool[] options = new bool[4];
        }

        private readonly ListOptions bagOptions = new ListOptions(), pickerOptions = new ListOptions();
        private BindingMap listOptionsView;
        private GameObject capacityNotice;
        private ListOptions CurrentOptions => pickerHero == null ? bagOptions : pickerOptions;
        private readonly string[] sortCaptionKeys = { "home.gear.singleSort", "arena.draft.filter.acquired",
            "home.hero_inventory.bag_grade", "arena.draft.filter.ability", "arena.draft.filter.name" };

        private void BindListOptions(BindingScope scope)
        {
            listOptionsView = ui.Get<BindingMap>("ListOptions");
            capacityNotice = ui.Get<GameObject>("CapacityNotice");
            void Listen(GameButton button, Action action) => BindAction(scope, button, _ =>
            {
                action();
                return Task.CompletedTask;
            });
            foreach (var button in new[] { ui.Get<GameButton>("CapacityClick"), HeroControl<GameButton>("capacityClick") })
                Listen(button, () => capacityNotice.SetActive(true));
            foreach (string key in new[] { "CapacityOk", "CapacityDismiss" })
                Listen(ui.Get<GameButton>(key), () => capacityNotice.SetActive(false));
            Listen(listOptionsView.Require<GameButton>("dismiss"), () => listOptionsView.gameObject.SetActive(false));
            Listen(ui.Get<GameButton>("Sort"), OpenListOptions);
            Listen(HeroControl<GameButton>("sort"), OpenListOptions);
            for (int i = 0; i < 6; i++)
            {
                int stars = i == 0 ? 0 : 6 - i;
                Listen(listOptionsView.Require<GameButton>("rarity" + i), () =>
                {
                    if (stars == 0) CurrentOptions.stars.Clear();
                    else if (!CurrentOptions.stars.Add(stars)) CurrentOptions.stars.Remove(stars);
                    ApplyListOptions();
                });
            }
            for (int i = 0; i < 4; i++)
            {
                int option = i;
                Listen(listOptionsView.Require<GameButton>("option" + i), () =>
                {
                    CurrentOptions.options[option] = !CurrentOptions.options[option];
                    ApplyListOptions();
                });
            }
            for (int i = 0; i < sortCaptionKeys.Length; i++)
            {
                int index = i;
                Listen(listOptionsView.Require<GameButton>("order" + i), () =>
                {
                    CurrentOptions.reversed = CurrentOptions.order == index && !CurrentOptions.reversed;
                    CurrentOptions.order = index;
                    ApplyListOptions();
                });
            }
            scope.Add(() => { if (capacityNotice) capacityNotice.SetActive(false); if (listOptionsView) listOptionsView.gameObject.SetActive(false); });
        }

        private bool CloseListOptions()
        {
            if (capacityNotice && capacityNotice.activeSelf) capacityNotice.SetActive(false);
            else if (listOptionsView && listOptionsView.gameObject.activeSelf) listOptionsView.gameObject.SetActive(false);
            else return false;
            return true;
        }

        private void OpenListOptions()
        {
            listOptionsView.gameObject.SetActive(true);
            PaintListOptions();
        }

        private void ApplyListOptions()
        {
            Refresh(true);
            PaintListOptions();
        }

        private void PaintListOptions()
        {
            var options = CurrentOptions;
            var on = new Color32(250, 218, 143, 255);
            for (int i = 0; i < 6; i++)
                listOptionsView.Require<GameText>("rarity" + i + "Text").color =
                    (i == 0 ? options.stars.Count == 0 : options.stars.Contains(6 - i)) ? on : Color.white;
            for (int i = 0; i < 4; i++)
            {
                var label = listOptionsView.Require<GameText>("option" + i + "Text");
                listOptionsView.Require<Image>("check" + i).enabled = options.options[i];
                label.color = options.options[i] ? on : Color.white;
            }
            for (int i = 0; i < sortCaptionKeys.Length; i++)
            {
                var label = listOptionsView.Require<GameText>("order" + i + "Text");
                label.text = locale.Resolve(sortCaptionKeys[i]) + (i == options.order ? options.reversed ? " ▲" : " ▼" : "");
                label.color = options.order == i ? on : Color.white;
            }
        }

        private ArtifactInventoryItem[] OrderedArtifacts(IEnumerable<ArtifactInventoryItem> items, ListOptions options)
        {
            var filtered = items.Where(item =>
            {
                var template = art.Find(item.templateId).template;
                return (options.stars.Count == 0 || options.stars.Contains(template.Stars)) &&
                    (!options.options[0] || !HasWearer(item)) && (!options.options[2] || template.HeroClass.Length == 0) &&
                    // The current catalog contains combat artifacts only; it has no growth bonus artifacts.
                    !options.options[1];
            });
            IEnumerable<ArtifactInventoryItem> ordered = options.order switch
            {
                1 => filtered.Reverse(), // The snapshot is ordered by persistent acquisition_order.
                2 => filtered.OrderByDescending(item => art.Find(item.templateId).template.Stars).ThenByDescending(item => item.level),
                3 => filtered.OrderByDescending(item => art.Find(item.templateId).template.AttackAt(item.level))
                    .ThenByDescending(item => art.Find(item.templateId).template.HealthAt(item.level)),
                4 => filtered.OrderBy(item => art.Find(item.templateId).template.Name, StringComparer.CurrentCulture),
                _ => filtered.OrderByDescending(item => item.level).ThenByDescending(item => art.Find(item.templateId).template.Stars)
            };
            return (options.reversed ? ordered.Reverse() : ordered).ToArray();
        }
    }
}
