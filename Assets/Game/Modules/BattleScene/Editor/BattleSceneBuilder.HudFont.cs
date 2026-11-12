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

        private static TMP_FontAsset BuildHealthDigits()
        {
            string path = HealthFontDirectory + "/BattleHpDigits.asset";
            var atlas = Asset<Texture2D>(HudTextures + "Fonts/epic7num_0.png");
            var description = Asset<TextAsset>(HudTextures + "Fonts/epic7num.fnt.txt");
            string[] lines = description.text.Split('\n');
            string common = lines.Single(line => line.StartsWith("common ", StringComparison.Ordinal));
            int lineHeight = Metric(common, "lineHeight");
            int baseline = Metric(common, "base");
            if (atlas.width != Metric(common, "scaleW") || atlas.height != Metric(common, "scaleH"))
                throw new InvalidDataException("The numeric atlas dimensions do not match the BMFont metadata.");

            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
                asset.name = "BattleHpDigits";
                AssetDatabase.CreateAsset(asset, path);
            }
            if (!asset.material)
            {
                var shader = Asset<Shader>("Assets/TextMesh Pro/Shaders/TMP_Bitmap.shader");
                var material = new Material(shader) { name = "BattleHpDigits Material" };
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
                capLine = 30,
                descentLine = baseline - lineHeight,
                scale = 1
            };
            asset.glyphTable.Clear();
            asset.characterTable.Clear();
            foreach (string line in lines.Where(line => line.StartsWith("char ", StringComparison.Ordinal)))
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
            if ("0123456789/".Any(c => !asset.HasCharacter(c)))
                throw new InvalidDataException("The battle numeric atlas is missing a digit or slash.");
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
