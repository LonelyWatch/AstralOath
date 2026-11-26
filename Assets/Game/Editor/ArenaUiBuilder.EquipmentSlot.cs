using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string BadgePrefabPath = Common + "Prefab/NotificationBadge.prefab";
        private const string EquipmentSlotPrefabPath = Common + "Prefab/EquipmentSlot.prefab";

        private static void BuildEquipmentSlotAssets()
        {
            var badge = Root("NotificationBadge");
            PlaceHeroControl(badge, .5f, .5f, 31, 30);
            var sourceGroup = Area(badge, "Authored source group", 0, 0, 1, 1);
            sourceGroup.anchorMin = sourceGroup.anchorMax = new Vector2(1,.5f);
            sourceGroup.pivot = new Vector2(1,.5f);
            sourceGroup.sizeDelta = new Vector2(44,30) * HomeDesignScale;
            sourceGroup.anchoredPosition = Vector2.zero;
            var scaleLayout = badge.gameObject.AddComponent<AuthoredHeightLayout>();
            var layoutSettings = new SerializedObject(scaleLayout);
            layoutSettings.FindProperty("content").objectReferenceValue = sourceGroup;
            layoutSettings.FindProperty("authoredHeight").floatValue = 30 * HomeDesignScale;
            layoutSettings.ApplyModifiedPropertiesWithoutUndo();
            var mark = Picture(sourceGroup, "Red mark", Load<Sprite>(Common + "Textures/_notification_num.png"), 0, 0, 1, 1, true);
            mark.color = new Color32(255, 48, 38, 255);
            mark.pixelsPerUnitMultiplier = 1f / HomeDesignScale;
            var label = Text(sourceGroup, "Enhancement", null, 22f, .045f, .05f, .91f, .90f,
                Color.white, TextAlignmentOptions.Center);
            // Source n_num uses shaded bitmap numerals, not the general UI font.
            label.font = EnhancementBitmapDigits();
            label.fontSharedMaterial = label.font.material;
            label.fontSize = 34f * .57f * HomeDesignScale;
            label.enableAutoSizing = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.CenterGeoAligned;
            label.enabled = false;
            // The source CSB draws individual sprite numerals. Keep the old label only as a readback contract.
            var numberRoot = Area(sourceGroup,"Original numeral sprites",0,0,0,0);
            numberRoot.anchorMin = numberRoot.anchorMax = new Vector2(1,.5f);
            numberRoot.pivot = new Vector2(.5f,.5f);
            numberRoot.sizeDelta = Vector2.zero;
            var prefix = Picture(numberRoot,"Prefix",Load<Sprite>(Common + "Textures/EnhancementDigits/itxt_numplus_b.png"),0,0,0,0,true);
            var template = Picture(numberRoot,"Digit template",Load<Sprite>(Common + "Textures/EnhancementDigits/itxt_num0_b.png"),0,0,0,0,true);
            template.gameObject.SetActive(false);
            var number = numberRoot.gameObject.AddComponent<BitmapNumber>();
            var numberSettings = new SerializedObject(number);
            var digitSprites = numberSettings.FindProperty("digitSprites");
            digitSprites.arraySize = 10;
            for (int i = 0; i < 10; i++)
                digitSprites.GetArrayElementAtIndex(i).objectReferenceValue = Load<Sprite>(Common + "Textures/EnhancementDigits/itxt_num" + i + "_b.png");
            numberSettings.FindProperty("prefixSprite").objectReferenceValue = prefix.sprite;
            numberSettings.FindProperty("prefixImage").objectReferenceValue = prefix;
            numberSettings.FindProperty("digitTemplate").objectReferenceValue = template;
            numberSettings.FindProperty("prefixAdvance").floatValue = 15;
            numberSettings.FindProperty("digitAdvance").floatValue = 16;
            numberSettings.ApplyModifiedPropertiesWithoutUndo();
            numberRoot.gameObject.SetActive(false);
            var normalPlate = Picture(sourceGroup, "Normal enhancement plate", Load<Sprite>(Common + "Textures/_upgrade_num_orange.png"), 0, 0, 1, 1, true);
            var maximumPlate = Picture(sourceGroup, "Maximum enhancement plate", Load<Sprite>(Common + "Textures/_upgrade_num_red.png"), 0, 0, 1, 1, true);
            normalPlate.gameObject.SetActive(false);
            maximumPlate.gameObject.SetActive(false);
            Bind(badge, ("mark", mark), ("label", label), ("normalPlate", normalPlate), ("maximumPlate", maximumPlate),
                ("sourceGroup",sourceGroup),("scaleLayout",scaleLayout),("number",number));
            Save(badge.gameObject, BadgePrefabPath);
            UIViewRegistry.ValidatePrefab<NotificationBadge>(Load<GameObject>(BadgePrefabPath));

            var slot = Root("EquipmentSlot");
            PlaceHeroControl(slot, .5f, .5f, 98, 99);
            var frame = Picture(slot, "Frame", CommonArt("_box_equip_0"), 0, 0, 1, 1, true);
            var placeholder = Picture(slot, "Slot symbol", null, 0, 0, 1, 1, true);
            placeholder.color = new Color32(255, 255, 255, 76);
            placeholder.enabled = false;
            var item = Picture(slot, "Equipped item", null, .04f, .05f, .92f, .90f, true);
            item.enabled = false;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(BadgePrefabPath), slot);
            var corner = (RectTransform)instance.transform;
            Place(corner, .77f, -.04f, 31f / 98f, 30f / 99f);
            instance.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(corner);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            Bind(slot, ("frame", frame), ("placeholder", placeholder), ("item", item), ("badge", instance.GetComponent<BindingMap>()));
            Save(slot.gameObject, EquipmentSlotPrefabPath);
            UIViewRegistry.ValidatePrefab<EquipmentSlot>(Load<GameObject>(EquipmentSlotPrefabPath));
        }

        private static void AuthorWornEquipmentCard(BindingMap slot, bool showMainStat)
        {
            var root = (RectTransform)slot.transform;
            var frame = slot.Require<Image>("frame");
            var placeholder = slot.Require<Image>("placeholder");
            var badge = slot.Require<BindingMap>("badge");
            // The equipment-only generator can also run against a previously generated inventory.
            var existing = root.Find("Worn equipment");
            if (existing) Object.DestroyImmediate(existing.gameObject);
            var holder = Area(root, "Worn equipment", 0, 0, 1, 1);
            holder.gameObject.AddComponent<CanvasGroup>().blocksRaycasts = false;
            var card = showMainStat
                ? InstantiateEquipmentItemCard(holder, "Worn equipment card")
                : SingleSlotThumbnail(holder, 0, 0, 1, 1);
            if (showMainStat) Place((RectTransform)card.transform, 0, 0, 1, 1);
            var select = card.Require<GameButton>("select");
            select.enabled = false;
            var icon = card.Require<Image>("icon");
            icon.enabled = false;
            card.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(select);
            PrefabUtility.RecordPrefabInstancePropertyModifications(icon);
            PrefabUtility.RecordPrefabInstancePropertyModifications(card.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(card.gameObject);
            Bind(root, ("frame", frame), ("placeholder", placeholder), ("item", icon),
                ("badge", badge), ("wornCard", card));
            PrefabUtility.RecordPrefabInstancePropertyModifications(slot);
        }

        private static RectTransform InstantiateEquipmentSlot(RectTransform parent, string name, Sprite frame,
            Sprite placeholder, bool rightAligned, bool artifact = false)
        {
            var bindings = InstantiateEquipmentSlot(parent, name, frame, placeholder);
            var symbol = bindings.Require<Image>("placeholder");
            float width = artifact ? 123f : 98f, height = artifact ? 137f : 99f;
            float centerX = artifact ? 61f : rightAligned ? 71f : 23f;
            float centerY = artifact ? 32f : 28f;
            Place(symbol.rectTransform, (centerX - 16.5f) / width, 1f - (centerY + 16.5f) / height,
                33f / width, 33f / height);
            var corner = (RectTransform)bindings.Require<BindingMap>("badge").transform;
            Place(corner, 1f - 23f / width, -4f / height, 31f / width, 30f / height);
            PrefabUtility.RecordPrefabInstancePropertyModifications(symbol.rectTransform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(corner);
            return (RectTransform)bindings.transform;
        }

        private static BindingMap InstantiateEquipmentSlot(RectTransform parent, string name, Sprite frame, Sprite placeholder)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(EquipmentSlotPrefabPath), parent);
            instance.name = name;
            var bindings = instance.GetComponent<BindingMap>();
            var plate = bindings.Require<Image>("frame");
            var symbol = bindings.Require<Image>("placeholder");
            plate.sprite = frame;
            symbol.sprite = placeholder;
            symbol.enabled = placeholder;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
            PrefabUtility.RecordPrefabInstancePropertyModifications(symbol);
            return bindings;
        }
    }
}
