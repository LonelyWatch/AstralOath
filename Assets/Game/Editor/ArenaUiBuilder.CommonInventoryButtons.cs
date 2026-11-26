using System.Collections.Generic;
using System.IO;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string InventoryButtonStyles = Common + "Prefab/Styles/";
        private static Sprite InventoryButtonArt(string name) => Load<Sprite>(Common + "Textures/InventoryButtons/" + name + ".png");
        private static string InventoryButtonPath(string style) => InventoryButtonStyles + style + "/" +
            (style == "Gold" ? "CancelButton" : "ConfirmButton") + ".prefab";

        // Keep the existing button/label contract and original Common assets unchanged.
        // These are prefab variants, so pages share one authored shape for each source style.
        private static void BuildCommonInventoryButtons()
        {
            foreach (var style in new[] { "Blue", "Black", "Green", "GreenCost", "Gold", "Check" })
                Directory.CreateDirectory(Path.GetDirectoryName(InventoryButtonPath(style)));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var style in new[] { "Blue", "Black", "Green", "GreenCost", "Gold", "Check" })
            {
                bool cost = style == "GreenCost", check = style == "Check";
                string basePath = style == "Gold" ? CommonUiPrefabs.CancelPath : cost ? InventoryButtonPath("Green") : CommonUiPrefabs.ButtonPath;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(basePath));
                var root = (RectTransform)instance.transform;
                var map = instance.GetComponent<BindingMap>();
                var button = map.Require<GameButton>("button");
                var label = map.Require<GameText>("label");
                var image = (Image)button.targetGraphic;
                root.sizeDelta = new Vector2(137, 55) * BagDesignScale;
                root.anchorMin = root.anchorMax = Vector2.one * .5f;
                string sprite = style == "Blue" ? "_btn_blue" : style == "Black" ? "_btn_small_black" :
                    style == "Gold" ? "_btn_gold" : "_btn_green";
                image.sprite = check ? null : InventoryButtonArt(sprite);
                image.color = check ? Color.clear : Color.white;
                image.type = check ? Image.Type.Simple : Image.Type.Sliced;
                image.preserveAspect = false;
                image.pixelsPerUnitMultiplier = 1f / BagDesignScale;
                if (check) { button.transition = Selectable.Transition.None; button.spriteState = default; }
                else PressSprites(button, image.sprite, InventoryButtonArt(sprite + "_p"));
                var settings = new SerializedObject(button);
                settings.FindProperty("clickAudio").objectReferenceValue = null;
                settings.ApplyModifiedPropertiesWithoutUndo();
                label.font = font; label.fontSharedMaterial = font.material;
                label.fontStyle = FontStyles.Bold; label.text = "";
                label.color = Color.white; label.richText = true;
                Place(label.rectTransform, .03f, 0, .94f, 1);
                var keys = new List<(string Key, Object Target)> { ("button", button), ("label", label) };
                if (style == "Black")
                {
                    var selected = Paint(Area(root, "Selected", 0, 0, 1, 1), InventoryButtonArt("_btn_small_white"),
                        new Color32(118, 84, 40, 255), sliced: true);
                    selected.pixelsPerUnitMultiplier = 1f / BagDesignScale;
                    selected.transform.SetAsFirstSibling(); selected.enabled = false;
                    keys.Add(("selected", selected));
                }
                if (check)
                {
                    root.sizeDelta = new Vector2(243, 67) * BagDesignScale;
                    var frame = Area(root, "Checkbox", 30f / 243, .5f, 0, 0);
                    frame.sizeDelta = new Vector2(34, 35) * BagDesignScale;
                    var checkFrame = Paint(frame, CommonArt("cm_checkbg"), Color.white);
                    var mark = Picture(frame, "Check", CommonArt("cm_checkicon2"), 0, 0, 1, 1, true);
                    mark.enabled = false;
                    // UGUI vertex colors here use the source gamma-authored tint directly.
                    // Pre-converting RGB again darkens the original gold text and selected fill.
                    label.color = new Color32(171, 135, 89, 255);
                    label.fontStyle = FontStyles.Normal; label.alignment = TextAlignmentOptions.MidlineLeft;
                    Place(label.rectTransform, 54f / 243, 0, 1 - 54f / 243, 1);
                    keys.Add(("checkFrame", checkFrame)); keys.Add(("check", mark));
                }
                if (cost)
                {
                    root.sizeDelta = new Vector2(339, 72) * BagDesignScale;
                    Place(label.rectTransform, .64f, 0, .34f, 1);
                    var plate = Paint(Area(root, "Price backing", .03f, .18f, .48f, .64f), InventoryButtonArt("cost_plate"), Color.white, sliced: true);
                    plate.pixelsPerUnitMultiplier = 1f / BagDesignScale;
                    var coin = Picture(root, "Gold price", InventoryButtonArt("token_gold"), .02f, .12f, .18f, .76f, true);
                    var amount = Text(root, "Cost", null, 27, .18f, .12f, .34f, .76f,
                        new Color32(255, 218, 75, 255), TextAlignmentOptions.Center);
                    keys.Add(("costPlate", plate)); keys.Add(("costIcon", coin)); keys.Add(("cost", amount));
                }
                Bind(root, keys.ToArray());
                RecordInventoryButton(instance);
                Save(instance, InventoryButtonPath(style));
            }
        }

        private static GameButton InventoryAction(RectTransform parent, string name, string key, float size,
            float x, float y, float width, float height, out GameText label, string style = "Blue")
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(InventoryButtonPath(style)), parent);
            instance.name = name;
            Place((RectTransform)instance.transform, x, y, width, height);
            var map = instance.GetComponent<BindingMap>();
            label = map.Require<GameText>("label");
            label.text = key == null ? "" : labels[key]; label.fontSize = size;
            RecordInventoryButton(instance);
            return map.Require<GameButton>("button");
        }

        private static void RecordInventoryButton(GameObject instance)
        {
            foreach (var component in instance.GetComponentsInChildren<Component>(true))
                if (component && PrefabUtility.IsPartOfPrefabInstance(component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }
}
