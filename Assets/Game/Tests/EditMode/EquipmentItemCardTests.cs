using System;
using System.IO;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class EquipmentItemCardTests
    {
        private const string PathToCard = "Assets/Game/Common/Res/Prefab/EquipmentItemCard.prefab";
        private const string GearArt = "Assets/Game/Modules/Home/Res/Textures/Inventory/Equipment/";

        [Test]
        public void ThumbnailKeepsTheQualityBorderSetAndFramedWearerInSourceProportions()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathToCard);
            var instance = Object.Instantiate(prefab);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                var quality = map.Require<Image>("gradeFrame").rectTransform;
                var equipment = map.Require<Image>("icon").rectTransform;
                var set = map.Require<Image>("setIcon").rectTransform;
                var avatar = map.Require<BindingMap>("wearer");
                Assert.That(equipment.parent, Is.SameAs(quality.parent));
                Assert.That(equipment.rect.width / quality.rect.width, Is.EqualTo(84f / 96f).Within(.005f));
                Assert.That(set.rect.width / quality.rect.width, Is.EqualTo(44f / 96f).Within(.005f));
                Assert.That(((RectTransform)avatar.transform).rect.width / quality.rect.width,
                    Is.EqualTo(.50f).Within(.005f));
                Assert.That(avatar.Require<Image>("frame").sprite.name, Is.EqualTo("_hero_s_frame_ally"));
                Assert.That(avatar.Require<Image>("portrait").GetComponentInParent<Mask>(true), Is.Not.Null);
                var binder = new EquipmentItemCardBinder(map);
                var icon = LoadGear("icon_eq_weapon_arena026");
                binder.Set(icon, LoadGear("_itembg_equip_2"), null, null, icon, 15, 0, null);
                Assert.That(avatar.gameObject.activeSelf, Is.True);
                binder.Set(icon, LoadGear("_itembg_equip_2"), null, null, null, 15, 0, null);
                Assert.That(avatar.gameObject.activeSelf, Is.False, "The plate and gold rim must clear with the wearer.");
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void CardUsesIndependentSelectionAndAnAuthoredEnhancementBadge()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathToCard);
            UIViewRegistry.ValidatePrefab<EquipmentItemCard>(prefab);
            foreach (var dependency in AssetDatabase.GetDependencies(PathToCard, true))
                Assert.That(dependency, Does.Not.StartWith("Assets/Game/Modules/"), dependency);
            var instance = Object.Instantiate(prefab);
            try
            {
                var map = instance.GetComponent<BindingMap>();
                var card = new EquipmentItemCardBinder(map);
                var badge = map.Require<BindingMap>("badge");
                var icon = LoadGear("icon_eq_weapon_arena026");
                var grade = LoadGear("_itembg_equip_1");
                var set = LoadGear("icon_set_speed");
                var stat = LoadGear("cm_icon_stat_att");
                card.Set(icon, grade, set, stat, icon, 85, 15, "42");
                card.SetSelected(true);
                Assert.That(map.Require<GameText>("enhancement"), Is.SameAs(badge.Require<GameText>("label")));
                Assert.That(badge.gameObject.activeSelf, Is.True);
                Assert.That(map.Require<GameText>("enhancement").text, Is.EqualTo("+15"));
                Assert.That(map.Require<GameText>("level").text, Is.EqualTo("85"));
                Assert.That(map.Require<GameText>("stat").text, Is.EqualTo("42"));
                Assert.That(map.Require<Image>("gradeFrame").sprite, Is.SameAs(grade));
                Assert.That(map.Require<Image>("setIcon").sprite, Is.SameAs(set));
                Assert.That(map.Require<Image>("statIcon").sprite, Is.SameAs(stat));
                Assert.That(map.Require<Image>("wearerIcon").enabled, Is.True);
                Assert.That(map.Require<Image>("highlight").sprite.name, Is.EqualTo("_select"));
                Assert.That(map.Require<Image>("selectedPlate").sprite.name, Is.EqualTo("cm_item_slot_select"));
                var artwork = map.Require<Image>("icon").rectTransform.rect;
                Assert.That(artwork.width / artwork.height, Is.EqualTo(1).Within(.02));

                card.Set(null, null, null, null, null, 0, 0, null);
                card.SetSelected(false);
                foreach (var key in new[] { "icon", "gradeFrame", "setIcon", "statIcon", "wearerIcon", "selectedPlate", "highlight" })
                    Assert.That(map.Require<Image>(key).enabled, Is.False, key);
                Assert.That(badge.gameObject.activeSelf, Is.False);
                Assert.That(map.Require<GameText>("stat").text, Is.Empty);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void RenderedCardHasSquareArtworkStatFooterAndASeparateSelectionEdge()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device for render readback.");
            const int width = 360, height = 240;
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = RenderTexture.active;
            var cameraObject = new GameObject("Equipment card camera", typeof(Camera));
            var canvasObject = new GameObject("Equipment card canvas", typeof(RectTransform), typeof(Canvas));
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                var camera = cameraObject.GetComponent<Camera>();
                camera.targetTexture = target;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(16, 20, 25, 255);
                camera.cullingMask = 1 << 29;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PathToCard);
                for (int i = 0; i < 2; ++i)
                {
                    var instance = Object.Instantiate(asset, canvas.transform, false);
                    ((RectTransform)instance.transform).anchoredPosition = new Vector2(i == 0 ? -78 : 78, 0);
                    var card = new EquipmentItemCardBinder(instance.GetComponent<BindingMap>());
                    card.Set(LoadGear("icon_eq_weapon_arena026"), LoadGear("_itembg_equip_1"),
                        LoadGear("icon_set_speed"), LoadGear("cm_icon_stat_att"), null, 85, 0, "42");
                    card.SetSelected(i == 1);
                }
                foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 29;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                string directory = Environment.GetEnvironmentVariable("ASTRAL_INVENTORY_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    File.WriteAllBytes(Path.Combine(directory, "equipment-item-card.png"), image.EncodeToPNG());
                }
                // Coordinates are in the rendered 360x240 canvas, starting at its lower left.
                int brightFooter = 0, brightSelectedFooter = 0, selectedEdge = 0, ordinaryEdge = 0;
                for (int y = 46; y < 76; ++y)
                    for (int x = 56; x < 147; ++x)
                    {
                        Color color = image.GetPixel(x, y);
                        if (Mathf.Min(color.r, color.g, color.b) > .65f) ++brightFooter;
                        Color other = image.GetPixel(x + 156, y);
                        if (Mathf.Min(other.r, other.g, other.b) > .65f) ++brightSelectedFooter;
                    }
                for (int x = 50; x < 154; ++x)
                    for (int y = 200; y < 208; ++y)
                    {
                        if (WarmEdge(image.GetPixel(x, y))) ++ordinaryEdge;
                        if (WarmEdge(image.GetPixel(x + 156, y))) ++selectedEdge;
                    }
                Assert.That(brightFooter, Is.GreaterThan(30), "Main stat icon and text must render under the equipment.");
                Assert.That(brightSelectedFooter, Is.GreaterThan(brightFooter * .9f), "Selection must not dim the stat text.");
                Assert.That(selectedEdge, Is.GreaterThan(ordinaryEdge + 100), "The selected outline must reach beyond the top of the card.");
            }
            finally
            {
                cameraObject.GetComponent<Camera>().targetTexture = null;
                RenderTexture.active = previousTarget;
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        private static Sprite LoadGear(string name) => AssetDatabase.LoadAssetAtPath<Sprite>(GearArt + name + ".png");
        private static bool WarmEdge(Color color) => color.r > .65f && color.g > .62f && color.r > color.b + .09f;
    }
}
