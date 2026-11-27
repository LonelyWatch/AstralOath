using System.IO;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string SourceConfirmationPath = Common + "Prefab/SourceConfirmation.prefab";
        private const float SourceMessageButtonScale = 1.5f * .53f;
        private static readonly Vector2 SourceMessageButtonSize = new Vector2(388, 128) * SourceMessageButtonScale;

        private static string SourceMessageButtonPath(bool cancel) => Common + "Prefab/Styles/" +
            (cancel ? "RoundedGold/CancelButton.prefab" : "RoundedBlue/ConfirmButton.prefab");

        // msgbox.csb uses a 388x128 scale-nine button, then scales the node by .53.
        // Applying only the Canvas1.5 conversion makes its 100px end caps consume the
        // entire control width: the horizontal middle disappears and it becomes an ellipse.
        private static void BuildSourceMessageBoxControls()
        {
            SourceTextVariants();
            const string labelMaterialPath=Common+"Materials/SourceMessageButtonLabel.mat";
            var labelMaterial=AssetDatabase.LoadAssetAtPath<Material>(labelMaterialPath);
            if(!labelMaterial){labelMaterial=new Material(font.material);AssetDatabase.CreateAsset(labelMaterial,labelMaterialPath);}
            labelMaterial.CopyPropertiesFromMaterial(font.material);
            labelMaterial.EnableKeyword("OUTLINE_ON");
            labelMaterial.SetColor("_OutlineColor",Color.white);
            // Keep the original regular face. Cocos's 1px outline cannot be transferred
            // directly to a TMP SDF span: the old .28 white contour inflated both glyphs.
            // Calibrated against the reference and rendered candidate at the same frame height;
            // each final Player still needs its own visual check.
            labelMaterial.SetFloat("_FaceDilate",0);
            labelMaterial.SetFloat("_OutlineWidth",.06f);
            EditorUtility.SetDirty(labelMaterial);
            foreach (bool cancel in new[] { false, true })
                Directory.CreateDirectory(Path.GetDirectoryName(SourceMessageButtonPath(cancel)));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (bool cancel in new[] { false, true })
            {
                string path = SourceMessageButtonPath(cancel);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(
                    cancel ? CommonUiPrefabs.CancelPath : CommonUiPrefabs.ButtonPath));
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = SourceMessageButtonSize;
                var map = instance.GetComponent<BindingMap>();
                var button = map.Require<GameButton>("button");
                var image = (Image)button.targetGraphic;
                string art = cancel ? "_btn_gold_r_2" : "_btn_blue_r_3";
                string pressed = cancel ? "_btn_gold_r_p_2" : "_btn_blue_r_p_3";
                Sprite Skin(string name) => Load<Sprite>(Common + "Textures/MessageBoxButtons/" + name + ".png");
                image.sprite = Skin(art);
                image.type = Image.Type.Sliced;
                image.preserveAspect = false;
                image.color = Color.white;
                image.pixelsPerUnitMultiplier = 1f / SourceMessageButtonScale;
                PressSprites(button, image.sprite, Skin(pressed));
                // This is a message-box decision, whose source event differs from an ordinary click.
                // Normal selects the authored per-button AudioSource exactly once in GameUiFeedback.
                var audio = instance.GetComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.clip = Load<AudioClip>(Common + "Sounds/" + (cancel ? "UiCancel" : "UiAccept") + ".wav");
                button.FeedbackSound = GameUiFeedback.Sound.Normal;
                var label = map.Require<GameText>("label");
                label.font = font;
                label.fontSharedMaterial = labelMaterial;
                label.text = "";
                label.fontStyle = FontStyles.Normal;
                label.fontWeight = FontWeight.Regular;
                label.fontSize = 50f * .75f * SourceMessageButtonScale;
                label.alignment = TextAlignmentOptions.Center;
                Place(label.rectTransform, .04f, 0, .92f, 1);
                RecordInventoryButton(instance);
                Save(instance, path);
            }

            var panel = Root("SourceConfirmation");
            panel.sizeDelta = new Vector2(1920, 585);
            var surface = PaintSourceMessageBox(panel);
            // These are the leading question and the details in n_content/text, not
            // the separate white n_title heading. Preserve source RGB180 at full opacity.
            // Canvas already converts vertex color for the Linear renderer. Author
            // the source sRGB color once; pre-linearizing it made the captured ink too dark.
            Color promptInk=new Color32(180,180,180,255);
            var title = Text(panel, "Title", null, 29, .15f, .18f, .70f, .14f,
                promptInk, TextAlignmentOptions.Center, true);
            var body = Text(panel, "Body", null, 29, .15f, .35f, .70f, .29f,
                promptInk, TextAlignmentOptions.Center, true);
            title.richText = body.richText = false;
            GameButton AddButton(bool cancel, float x, out GameText label)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(SourceMessageButtonPath(cancel)), panel);
                instance.name = cancel ? "Cancel" : "Confirm";
                var rect = (RectTransform)instance.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
                rect.pivot = Vector2.one * .5f;
                rect.sizeDelta = SourceMessageButtonSize;
                rect.anchoredPosition = new Vector2(x, 40.5f + SourceMessageButtonSize.y * .5f);
                var map = instance.GetComponent<BindingMap>();
                label = map.Require<GameText>("label");
                RecordInventoryButton(instance);
                return map.Require<GameButton>("button");
            }
            const float halfGap=7.02f;
            var cancelButton = AddButton(true, -SourceMessageButtonSize.x * .5f - halfGap, out var cancelLabel);
            var confirmButton = AddButton(false, SourceMessageButtonSize.x * .5f + halfGap, out var confirmLabel);
            Bind(panel, ("surface", surface), ("title", title), ("body", body),
                ("cancel", cancelButton), ("confirm", confirmButton),
                ("cancelLabel", cancelLabel), ("confirmLabel", confirmLabel));
            Save(panel.gameObject, SourceConfirmationPath);
            AssetDatabase.SaveAssets();
        }

        private static BindingMap SourceConfirmation(RectTransform parent, string name,
            float x, float y, float width, float height)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(SourceConfirmationPath), parent);
            instance.name = name;
            Place((RectTransform)instance.transform, x, y, width, height);
            RecordInventoryButton(instance);
            return instance.GetComponent<BindingMap>();
        }
    }
}
