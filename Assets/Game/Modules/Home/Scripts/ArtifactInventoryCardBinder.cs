using AstralOath.Client.Common;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed class ArtifactInventoryCardBinder
    {
        private readonly ArtifactInventoryCard view;
        private readonly NotificationBadgeBinder enhancement;
        private readonly CenteredRarityStars rarity;
        internal RectTransform Root { get; }
        internal GameButton Button => view.Select;
        internal ArtifactInventoryItem Item { get; private set; }

        internal ArtifactInventoryCardBinder(GameObject instance)
        {
            Root = (RectTransform)instance.transform;
            view = new ArtifactInventoryCard(instance);
            enhancement = new NotificationBadgeBinder(view.Enhancement);
            rarity = new CenteredRarityStars(view.Stars, view.Emblem.rectTransform);
        }

        internal void Display(ArtifactInventoryItem item, ArtifactArtCatalog.Entry entry, Sprite roleIcon,
            string skillLevel, bool selected, Sprite wearer = null)
        {
            Item = item;
            Root.name = item.instanceId;
            view.Emblem.sprite = entry.combatIcon;
            view.Emblem.enabled = entry.combatIcon;
            view.Banner.sprite = entry.portrait;
            view.Banner.enabled = entry.portrait;
            view.Role.sprite = roleIcon;
            view.Role.enabled = roleIcon;
            view.Name.text = entry.template.Name;
            view.SkillLevel.text = skillLevel;
            view.SkillLevel.enabled = true;
            view.MaterialCount.enabled = view.MaterialExp.enabled = false;
            view.Wearer.sprite = wearer;
            view.Wearer.enabled = wearer;
            view.WearerFrame.SetActive(wearer);
            enhancement.Set(false, item.level);
            rarity.Show(entry.template.Stars);
            for (int i = 0; i < view.Breaks.Length; i++)
            {
                view.Breaks[i].enabled = i < item.limitBreaks;
                view.BreakSlots[i].enabled = true;
            }
            view.Lock.enabled = item.locked;
            view.Selection.enabled = selected;
            view.Show();
        }

        internal void DisplayMaterial(string key, Sprite emblem, Sprite banner, string title, string amount)
        {
            Item = null;
            Root.name = key;
            view.Emblem.sprite = emblem;
            view.Emblem.enabled = emblem;
            view.Banner.sprite = banner;
            view.Banner.enabled = banner;
            view.Name.text = title;
            view.SkillLevel.enabled = false;
            view.MaterialCount.text = amount;
            view.MaterialCount.enabled = true;
            view.MaterialExp.enabled = key != "bottle";
            view.Role.enabled = view.Lock.enabled = view.Selection.enabled = false;
            view.WearerFrame.SetActive(false);
            enhancement.Set(false);
            rarity.Show(0);
            for (int i = 0; i < view.Breaks.Length; i++)
                view.Breaks[i].enabled = view.BreakSlots[i].enabled = false;
            view.Show();
        }
    }
}
