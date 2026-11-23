using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed partial class EquipmentInventoryBinder
    {
        private void PaintRosterSets(string hero)
        {
            int line = 0;
            foreach (var group in EquipmentStatCalculator.ActiveSets(Pieces(hero)))
                for (int i = 0; i < group.Value; i++)
                    Paint(line++, SetName(group.Key), Get<Image>("set_" + group.Key).sprite);
            string empty = locale.Resolve("home.hero_inventory.roster_no_set");
            while (line < 3) Paint(line++, empty, null);

            void Paint(int index, string name, Sprite emblem)
            {
                var label = page.Require<GameText>("heroSetLabel" + index);
                var icon = page.Require<Image>("heroSetIcon" + index);
                label.text = name;
                icon.overrideSprite = emblem;
                icon.color = label.color = emblem ? Color.white : new Color32(151, 163, 181, 255);
            }
        }
    }
}
