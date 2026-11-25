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
            var mark = Picture(badge, "Red mark", Load<Sprite>(Common + "Textures/_notification_num.png"), 0, 0, 1, 1, true);
            mark.color = new Color32(255, 48, 38, 255);
            var label = Text(badge, "Enhancement", null, 22f, .045f, .05f, .91f, .90f,
                Color.white, TextAlignmentOptions.Center);
            // This label only displays an ASCII plus sign and decimal digits.
            // Its static atlas has no dependency on a module's localization fallback.
            label.font = Load<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            label.fontSharedMaterial = label.font.material;
            label.enableAutoSizing = true;
            label.fontSizeMin = 13f;
            label.fontSizeMax = 22f;
            label.enabled = false;
            var normalPlate = Picture(badge, "Normal enhancement plate", Load<Sprite>(Common + "Textures/_upgrade_num_orange.png"), 0, 0, 1, 1, true);
            var maximumPlate = Picture(badge, "Maximum enhancement plate", Load<Sprite>(Common + "Textures/_upgrade_num_red.png"), 0, 0, 1, 1, true);
            normalPlate.gameObject.SetActive(false);
            maximumPlate.gameObject.SetActive(false);
            Bind(badge, ("mark", mark), ("label", label), ("normalPlate", normalPlate), ("maximumPlate", maximumPlate));
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
            Place(corner, .73f, -.04f, 31f / 98f, 30f / 99f);
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
            Place(corner, 1f - 27f / width, -4f / height, 31f / width, 30f / height);
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
