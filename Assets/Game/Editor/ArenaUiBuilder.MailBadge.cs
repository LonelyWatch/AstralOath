using System.IO;
using AstralOath.Client.Common;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        // top_bar.csb: mail_count at (19,48), size (38,30), font Dinmed 21 at scale .82.
        // Keep the count independent of the header's aspect ratio and the icon's transparent padding.
        private static (RectTransform Counter, GameText Count) BuildMailEntryBadge(RectTransform parent)
        {
            const float scale = 1.5f;
            var counter = Area(parent, "Counter", 0, 0, 0, 0);
            counter.anchorMin = counter.anchorMax = Vector2.zero;
            counter.pivot = new Vector2(.5f, .5f);
            counter.anchoredPosition = new Vector2(19, 48) * scale;
            counter.sizeDelta = new Vector2(38, 30) * scale;
            var plate = Paint(counter, MailArt("_notification_num"), Color.white);
            plate.type = Image.Type.Sliced;
            plate.pixelsPerUnitMultiplier = 1f / scale;
            plate.raycastTarget = false;

            var count = Text(counter, "Count", null, 21 * .82f * scale, 0, 0, 0, 0,
                Color.white, TextAlignmentOptions.Center);
            var rect = count.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(-1, 2.5f) * scale;
            rect.sizeDelta = new Vector2(25, 27) * .82f * scale;
            count.font = MailCountFont();
            count.fontSharedMaterial = count.font.material;
            count.fontStyle = FontStyles.Normal;
            count.enableAutoSizing = false;
            count.enableWordWrapping = false;
            count.overflowMode = TextOverflowModes.Overflow;
            count.text = "";
            counter.gameObject.SetActive(false);
            return (counter, count);
        }

        private static TMP_FontAsset MailCountFont()
        {
            const string path = MailRes + "Fonts/MailCountDigits.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!asset)
            {
                var source = Load<Font>(MailRes + "Fonts/MailCount.ttf");
                asset = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA,
                    256, 256, AtlasPopulationMode.Dynamic, false);
                if (!asset || !asset.TryAddCharacters("0123456789_ "))
                    throw new InvalidDataException("Unable to bake the original mail count digits.");
                asset.name = "MailCountDigits";
                asset.atlasPopulationMode = AtlasPopulationMode.Static;
                asset.atlasTexture.name = asset.name + " Atlas";
                asset.material.name = asset.name + " Material";
                AssetDatabase.CreateAsset(asset, path);
                AssetDatabase.AddObjectToAsset(asset.atlasTexture, asset);
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }
            if(!asset.HasCharacter('_'))
            {
                // TMP initializes this support glyph even when no underline is displayed.
                // Update the existing atlas without replacing its GUID or subassets.
                asset.atlasPopulationMode=AtlasPopulationMode.Dynamic;
                try { if(!asset.TryAddCharacters("_ "))throw new InvalidDataException("Unable to bake mail count support glyphs."); }
                finally { asset.atlasPopulationMode=AtlasPopulationMode.Static; }
                EditorUtility.SetDirty(asset.atlasTexture);
            }
            asset.material.EnableKeyword("OUTLINE_ON");
            asset.material.SetFloat("_FaceDilate", 0);
            asset.material.SetFloat("_OutlineWidth", .32f);
            asset.material.SetColor("_OutlineColor", new Color32(110, 9, 9, 255));
            EditorUtility.SetDirty(asset);
            EditorUtility.SetDirty(asset.material);
            AssetDatabase.SaveAssets();
            return asset;
        }
    }
}
