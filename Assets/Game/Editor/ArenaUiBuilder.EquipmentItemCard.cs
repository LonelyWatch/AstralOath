using AstralOath.Client.Common;
using AstralOath.Client.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
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

            // The original top field is square. The right-hand set emblem straddles its lower edge.
            var grade = Picture(card, "Quality", null, .055f, .045f, .89f, .69f, true);
            var icon = Picture(card, "Equipment", null, .055f, .045f, .89f, .69f, true);
            var set = Picture(card, "Equipment set", null, .68f, .47f, .35f, .296f, true);
            var wearer = Picture(card, "Wearer", null, -.015f, .48f, .33f, .26f, true);
            var level = EquipmentCardNumber(Area(card, "Item level", .095f, .059f, .55f, .172f),
                numberFont, 21, new Color32(255, 222, 114, 255), TextAlignmentOptions.MidlineLeft);

            var badgeInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(BadgePrefabPath), card);
            var badgeRect = (RectTransform)badgeInstance.transform;
            Place(badgeRect, .70f, -.025f, .34f, .235f);
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
            Bind(card, ("select", button), ("icon", icon), ("gradeFrame", grade), ("setIcon", set),
                ("wearerIcon", wearer), ("statIcon", statIcon), ("stat", stat), ("level", level),
                ("badge", badge), ("enhancement", badge.Require<GameText>("label")),
                ("selectedPlate", selected), ("highlight", highlight));
            Save(card.gameObject, EquipmentItemCardPath);
            UIViewRegistry.ValidatePrefab<EquipmentItemCard>(Load<GameObject>(EquipmentItemCardPath));
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

        private static BindingMap InstantiateEquipmentItemCard(RectTransform parent, string name)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(EquipmentItemCardPath), parent);
            instance.name = name;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            return instance.GetComponent<BindingMap>();
        }
    }
}
