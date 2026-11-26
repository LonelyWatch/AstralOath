using AstralOath.Client.Common;
using AstralOath.Client.Home;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class FixedEquipmentPanelsTests
    {
        [Test]
        public void ManagementCandidateRetainsItsDesignSizeWhenWindowWidthChanges()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var root = (RectTransform)instance.transform;
                var browser = instance.GetComponent<BindingMap>().Require<BindingMap>("gearBrowser");
                var detail = (RectTransform)browser.Require<GameObject>("detail").transform;
                browser.gameObject.SetActive(true);
                browser.Require<GameObject>("pickerSurface").SetActive(true);
                detail.gameObject.SetActive(true);
                root.sizeDelta = new Vector2(1920, 1080);
                Canvas.ForceUpdateCanvases();
                Vector2 designSize = detail.rect.size;
                foreach (float width in new[] { 1600f, 2560f, 3840f })
                {
                    root.sizeDelta = new Vector2(width, 1080);
                    Canvas.ForceUpdateCanvases();
                    Assert.That(detail.rect.width, Is.EqualTo(designSize.x).Within(.1f), "The card must not widen with the window.");
                    Assert.That(detail.rect.height, Is.EqualTo(designSize.y).Within(.1f), "The card must retain its design height.");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void WornDetailRetainsItsDesignSizeWhenAvailableHeightChanges()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/SingleSlotEquipment.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var root = (RectTransform)instance.transform;
                var map = instance.GetComponent<BindingMap>();
                var detail = (RectTransform)map.Require<GameObject>("popup").transform.Find("Detail safe area/Equipment details");
                map.Require<GameObject>("popup").SetActive(true);
                root.sizeDelta = new Vector2(1920, 1080);
                Canvas.ForceUpdateCanvases();
                Vector2 designSize = detail.rect.size;
                foreach (var size in new[] { new Vector2(1600, 1000), new Vector2(2560, 1440), new Vector2(1040, 960) })
                {
                    root.sizeDelta = size;
                    Canvas.ForceUpdateCanvases();
                    Assert.That(detail.rect.width, Is.EqualTo(designSize.x).Within(.1f));
                    Assert.That(detail.rect.height, Is.EqualTo(designSize.y).Within(.1f));
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void ManagementSafeFitScalesTheWholeCompositionAndKeepsTheCandidateClearOfPreviewSlots()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HeroInventory.prefab");
            var instance = Object.Instantiate(prefab);
            try
            {
                var root = (RectTransform)instance.transform;
                root.sizeDelta = new Vector2(1920,1080);
                var view = new HeroInventory();
                const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(UIBaseView).GetMethod("Attach",hidden).Invoke(view,new object[] { instance });
                var browser = instance.GetComponent<BindingMap>().Require<BindingMap>("gearBrowser");
                browser.gameObject.SetActive(true);
                browser.Require<GameObject>("pickerSurface").SetActive(true);
                var info = (RectTransform)browser.Require<GameObject>("detail").transform;
                var composition = browser.Require<RectTransform>("manageComposition");
                var preview = (RectTransform)browser.Require<BindingMap>("managePreview_weapon").transform;
                Rect InRoot(RectTransform rect)
                {
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    Vector2 min = root.InverseTransformPoint(corners[0]), max = root.InverseTransformPoint(corners[2]);
                    return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
                }
                foreach (var size in new[] { new Vector2(1040,960), new Vector2(1600,1000), new Vector2(3840,1080) })
                {
                    root.sizeDelta = size;
                    typeof(HeroInventory).GetMethod("FitEquipmentLayout",hidden).Invoke(view,null);
                    Canvas.ForceUpdateCanvases();
                    Rect fit = InRoot(composition);
                    Assert.That(composition.localScale.x, Is.EqualTo(composition.localScale.y).Within(.0001f));
                    Assert.That(fit.xMin, Is.GreaterThanOrEqualTo(root.rect.xMin - .1f));
                    Assert.That(fit.xMax, Is.LessThanOrEqualTo(root.rect.xMax + .1f));
                    Assert.That(fit.yMin, Is.GreaterThanOrEqualTo(root.rect.yMin - .1f));
                    Assert.That(fit.yMax, Is.LessThanOrEqualTo(root.rect.yMax + .1f));
                    Assert.That(InRoot(info).xMax, Is.LessThan(InRoot(preview).xMin), "The fixed detail must not cover the proposed weapon.");
                }
            }
            finally { Object.DestroyImmediate(instance); }
        }
    }
}
