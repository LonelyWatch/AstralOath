using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string EquipmentItemCardPath = Common + "Prefab/EquipmentItemCard.prefab";

        [MenuItem("Tools/游戏主页/生成通用装备卡")]
        public static void BuildEquipmentItemCard()
        {
            var numberFont = Load<TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var card = Root("EquipmentItemCard");
            PlaceHeroControl(card, .5f, .5f, 86, 111);
            var button = Button(card, Load<Sprite>(Common + "Textures/cm_item_slot.png"), Color.white);
            button.transition = Selectable.Transition.None;
            var selected = Picture(card, "Selected shell", Load<Sprite>(Common + "Textures/cm_item_slot_select.png"), 0, 0, 1, 1);
            selected.type = Image.Type.Sliced;
            selected.color = new Color32(49, 53, 58, 255);
            selected.enabled = false;
            var highlight = Picture(card, "Selection outline", Load<Sprite>(Common + "Textures/_select.png"),
                -.033f, -.025f, 1.066f, 1.05f);
            highlight.type = Image.Type.Sliced;
            highlight.pixelsPerUnitMultiplier = 1.25f;
            highlight.enabled = false;

            // A 96px grade sprite has a visible 84px plate. Keep the source 84px item inside it.
            var art = Area(card, "Square illustration", 0, 0, 1, 86f / 111f);
            var grade = Picture(art, "Quality", null, 0, 0, 1, 1, true);
            var icon = Picture(art, "Equipment", null, 6f/96, 6f/96, 84f/96, 84f/96, true);
            var set = Picture(art, "Equipment set", null, .628f, .578f, 44f/96, 44f/96, true);
            var wearer = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(CommonUiPrefabs.AvatarPath), art);
            var wearerRect = (RectTransform)wearer.transform;
            Place(wearerRect, -.055f, .55f, .50f, .50f);
            var avatar = wearer.GetComponent<BindingMap>();
            var rim = avatar.Require<Image>("frame");
            rim.sprite = CommonArt("_hero_s_frame_ally");
            wearer.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rim);
            PrefabUtility.RecordPrefabInstancePropertyModifications(wearerRect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(wearer);
            var level = EquipmentCardNumber(Area(art, "Item level", .14f, .094f, .55f, .223f),
                EquipmentLevelDigits(), 22,
                new Color32(255, 221, 128, 255), TextAlignmentOptions.MidlineLeft);
            level.fontStyle = FontStyles.Normal;
            level.overflowMode = TextOverflowModes.Overflow;

            var badgeInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(BadgePrefabPath), art);
            var badgeRect = (RectTransform)badgeInstance.transform;
            Place(badgeRect, .67f, -.055f, .405f, .29f);
            badgeInstance.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(badgeRect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(badgeInstance);
            var badge = badgeInstance.GetComponent<BindingMap>();

            var footer = Area(card, "Main stat", .03f, .75f, .94f, .215f);
            var layout = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            layout.spacing = 4;
            var statIcon = Picture(footer, "Stat icon", null, 0, 0, 1, 1, true);
            var glyphSize = statIcon.gameObject.AddComponent<LayoutElement>();
            glyphSize.preferredWidth = glyphSize.preferredHeight = 26;
            var stat = EquipmentCardNumber(Area(footer, "Stat value", 0, 0, 1, 1), numberFont, 21,
                Color.white, TextAlignmentOptions.Center);
            Bind(card, ("select", button), ("art", art), ("icon", icon), ("gradeFrame", grade), ("setIcon", set),
                ("wearer", avatar), ("wearerIcon", avatar.Require<Image>("portrait")), ("statIcon", statIcon), ("stat", stat), ("level", level),
                ("badge", badge), ("enhancement", badge.Require<GameText>("label")),
                ("selectedPlate", selected), ("highlight", highlight));
            Save(card.gameObject, EquipmentItemCardPath);
            UIViewRegistry.ValidatePrefab<EquipmentItemCard>(Load<GameObject>(EquipmentItemCardPath));
        }

        private static TMP_FontAsset EquipmentLevelDigits()
        {
            const string path = Common + "Fonts/EquipmentItemLevelDigits.asset";
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!font)
            {
                font = TMP_FontAsset.CreateFontAsset(Load<Font>(Common + "Fonts/EpicSeven.ttf"),
                    64, 8, GlyphRenderMode.SDFAA, 256, 256, AtlasPopulationMode.Dynamic, false);
                if (!font || !font.TryAddCharacters("0123456789"))
                    throw new System.IO.InvalidDataException("Could not bake the ten equipment level digits.");
                font.name = "EquipmentItemLevelDigits";
                font.atlasPopulationMode = AtlasPopulationMode.Static;
                font.atlasTexture.name = font.name + " Atlas";
                font.material.name = font.name + " Material";
                AssetDatabase.CreateAsset(font, path);
                AssetDatabase.AddObjectToAsset(font.atlasTexture, font);
                AssetDatabase.AddObjectToAsset(font.material, font);
                EditorUtility.SetDirty(font);
            }
            var material = font.material;
            material.EnableKeyword("OUTLINE_ON");
            material.EnableKeyword("UNDERLAY_ON");
            material.SetFloat("_FaceDilate", .21f);
            material.SetFloat("_OutlineWidth", .25f);
            material.SetColor("_OutlineColor", new Color32(3, 2, 1, 255));
            material.SetFloat("_UnderlayOffsetX", .12f);
            material.SetFloat("_UnderlayOffsetY", -.54f);
            material.SetColor("_UnderlayColor", new Color32(14, 7, 3, 230));
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(font);
            AssetDatabase.SaveAssetIfDirty(material);
            return font;
        }

        private static GameText EquipmentCardNumber(RectTransform rect, TMP_FontAsset numberFont, int size,
            Color color, TextAlignmentOptions alignment)
        {
            var text = rect.gameObject.AddComponent<GameText>();
            text.font = numberFont;
            text.fontSharedMaterial = numberFont.material;
            text.fontSize = size;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static BindingMap SingleSlotThumbnail(RectTransform parent, float x, float y, float w, float h)
        {
            var holder = Area(parent,"Square equipment art",x,y,w,h);
            var square = Area(holder,"Square",0,0,1,1);
            square.gameObject.SetActive(false);
            var aspect = square.gameObject.AddComponent<AspectRatioFitter>();
            // In edit-mode None measures width/height, both zero before the prefab is on a canvas.
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1;
            square.gameObject.SetActive(true);
            var card = InstantiateEquipmentItemCard(square,"Equipment thumbnail");
            var rect = (RectTransform)card.transform;
            Place(rect,0,0,1,1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            // Single-slot rows carry the value beside the artwork, so use the square portion of the common card.
            var select = card.Require<GameButton>("select");
            select.interactable = false;
            select.targetGraphic.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(select);
            PrefabUtility.RecordPrefabInstancePropertyModifications(select.targetGraphic);
            var art = card.Require<RectTransform>("art");
            Place(art,0,0,1,1);
            PrefabUtility.RecordPrefabInstancePropertyModifications(art);
            var footer = card.Require<GameText>("stat").transform.parent.gameObject;
            footer.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(footer);
            return card;
        }

        private static BindingMap InstantiateEquipmentItemCard(RectTransform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(EquipmentItemCardPath), parent);
            instance.name = name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            return instance.GetComponent<BindingMap>();
        }
    }
}
