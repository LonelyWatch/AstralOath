using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentStatRowTests
    {
        private const string RowPath = "Assets/Game/Common/Res/Prefab/EquipmentStatRow.prefab";
        private const string HeroPath = "Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab";

        [Test]
        public void EquipmentDetailsNestTheSameTwoColumnRowForMainAndSecondaryStats()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(RowPath);
            Assert.That(asset, Is.Not.Null, "The equipment detail needs an authored shared stat row.");
            var hero = AssetDatabase.LoadAssetAtPath<GameObject>(HeroPath);
            var browser = hero.GetComponent<BindingMap>().Require<BindingMap>("gearBrowser");
            var rows = browser.GetComponentsInChildren<BindingMap>(true)
                .Where(map => PrefabUtility.GetCorrespondingObjectFromSource(map.gameObject) == asset)
                .ToArray();
            Assert.That(rows.Length, Is.GreaterThanOrEqualTo(2));
            foreach (var row in rows)
            {
                var label = row.Require<GameText>("label");
                var value = row.Require<GameText>("value");
                Assert.That(label.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Left));
                Assert.That(value.horizontalAlignment, Is.EqualTo(HorizontalAlignmentOptions.Right));
                Assert.That(label.rectTransform.anchorMax.x, Is.LessThanOrEqualTo(value.rectTransform.anchorMin.x));
            }
        }
    }
}
