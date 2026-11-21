using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string EquipmentStatRowPath = Common + "Prefab/EquipmentStatRow.prefab";

        private static void BuildEquipmentStatRow()
        {
            var row = Root("EquipmentStatRow");
            PlaceHeroControl(row, .5f, .5f, 310, 28);
            var label = Text(row, "Label", null, 21, 0, 0, .69f, 1, Color.white, TextAlignmentOptions.MidlineLeft);
            var value = Text(row, "Value", null, 21, .70f, 0, .30f, 1, Color.white, TextAlignmentOptions.MidlineRight);
            Bind(row, ("label", label), ("value", value));
            Save(row.gameObject, EquipmentStatRowPath);
            UIViewRegistry.ValidatePrefab<EquipmentStatRow>(Load<GameObject>(EquipmentStatRowPath));
        }

        private static GameObject AddEquipmentStatRow(Transform parent, string name)
        {
            var source = Load<GameObject>(EquipmentStatRowPath);
            var row = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            row.name = name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(row);
            return row;
        }
    }
}
