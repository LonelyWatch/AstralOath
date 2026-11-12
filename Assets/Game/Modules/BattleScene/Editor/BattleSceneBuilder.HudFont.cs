using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace AstralOath.Client.BattleScene.Editor
{
    public static partial class BattleSceneBuilder
    {
        private const string HealthFontDirectory = Module + "/Res/Fonts";

        private static void ImportOrderDetailsBorder()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(HudTextures + "_btn_small_black.png");
            // Convert Cocos's top-left cap inset (14,12,8,6) on a 36x34 sprite.
            var border = new Vector4(14, 16, 14, 12);
            if (importer.spriteBorder != border)
            {
                importer.spriteBorder = border;
                importer.SaveAndReimport();
            }
        }

        private static TMP_FontAsset BuildHealthDigits() =>
            BuildDigits("BattleHpDigits", "epic7num", "TMP_Bitmap.shader", "0123456789/", preserveColor: false);

        private static TMP_FontAsset BuildDamageDigits() =>
            BuildDigits("BattleDamageDigits", "damage", "TMP_Sprite.shader", "0123456789+", preserveColor: true);

        private static TMP_FontAsset BuildDigits(string assetName, string sourceName, string shaderName,
            string requiredCharacters, bool preserveColor)
        {
            string path = HealthFontDirectory + "/" + assetName + ".asset";
            var atlas = Asset<Texture2D>(HudTextures + "Fonts/" + sourceName + "_0.png");
            var description = Asset<TextAsset>(HudTextures + "Fonts/" + sourceName + ".fnt.txt");
            string[] lines = description.text.Split('\n');
            string common = lines.Single(line => line.StartsWith("common ", StringComparison.Ordinal));
            int lineHeight = Metric(common, "lineHeight");
            int baseline = Metric(common, "base");
            var glyphLines = lines.Where(line => line.StartsWith("char ", StringComparison.Ordinal)).ToArray();
            // The damage atlas contains 73-pixel digits but the source reports a 32-pixel line.
            // Fit the TMP face to its actual bitmap extents so centering preserves the original pixels.
            if (preserveColor)
                baseline = lineHeight = glyphLines.Max(line => Metric(line, "yoffset") + Metric(line, "height"));
            if (atlas.width != Metric(common, "scaleW") || atlas.height != Metric(common, "scaleH"))
                throw new InvalidDataException("The numeric atlas dimensions do not match the BMFont metadata.");

            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
                asset.name = assetName;
                AssetDatabase.CreateAsset(asset, path);
            }
            if (!asset.material)
            {
                var shader = Asset<Shader>("Assets/TextMesh Pro/Shaders/" + shaderName);
                var material = new Material(shader) { name = assetName + " Material" };
                AssetDatabase.AddObjectToAsset(material, asset);
                asset.material = material;
            }
            asset.material.mainTexture = atlas;
            EditorUtility.SetDirty(asset.material);
            asset.atlasTextures = new[] { atlas };
            asset.faceInfo = new FaceInfo
            {
                familyName = "EpicSeven",
                styleName = "Bitmap",
                pointSize = lineHeight,
                lineHeight = lineHeight,
                ascentLine = baseline,
                capLine = preserveColor ? baseline : 30,
                descentLine = baseline - lineHeight,
                scale = 1
            };
            asset.glyphTable.Clear();
            asset.characterTable.Clear();
            foreach (string line in glyphLines)
            {
                uint unicode = checked((uint)Metric(line, "id"));
                int width = Metric(line, "width");
                int height = Metric(line, "height");
                int x = Metric(line, "x");
                int y = atlas.height - Metric(line, "y") - height;
                var metrics = new GlyphMetrics(width, height, Metric(line, "xoffset"), baseline - Metric(line, "yoffset"),
                    Metric(line, "xadvance"));
                var glyph = new Glyph(unicode, metrics, new GlyphRect(x, y, width, height), 1, 0);
                asset.glyphTable.Add(glyph);
                asset.characterTable.Add(new TMP_Character(unicode, asset, glyph));
            }

            var serialized = new SerializedObject(asset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.FindProperty("m_AtlasWidth").intValue = atlas.width;
            serialized.FindProperty("m_AtlasHeight").intValue = atlas.height;
            serialized.FindProperty("m_AtlasRenderMode").intValue = (int)GlyphRenderMode.SMOOTH;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            asset.ReadFontAssetDefinition();
            if (requiredCharacters.Any(c => !asset.HasCharacter(c)))
                throw new InvalidDataException("The battle numeric atlas is missing a required glyph: " + assetName);
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static int Metric(string line, string key)
        {
            var match = Regex.Match(line, @"(?:^|\s)" + key + @"=(-?\d+)(?:\s|$)");
            if (!match.Success)
                throw new InvalidDataException("Missing BMFont metric " + key);
            return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        }
    }
}
