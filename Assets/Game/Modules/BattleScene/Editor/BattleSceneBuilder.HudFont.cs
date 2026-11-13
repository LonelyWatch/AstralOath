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

        [MenuItem("Tools/战斗场景/重建数值字体")]
        public static void GenerateNumericFonts()
        {
            Directory.CreateDirectory(HealthFontDirectory);
            var health = BuildHealthDigits();
            var damage = BuildDamageDigits();
            // Do not save unrelated dirty editor assets (including the general UI font).
            AssetDatabase.SaveAssetIfDirty(health);
            AssetDatabase.SaveAssetIfDirty(damage);
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

            AddUnderlineGlyph(asset, atlas, lineHeight);

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

        private static void AddUnderlineGlyph(TMP_FontAsset asset, Texture2D atlas, int lineHeight)
        {
            const uint underscore = 0x5F;
            if (asset.characterTable.Any(character => character.unicode == underscore)) return;
            // TMP 3.0.7 requires U+005F in the primary font on every font assignment;
            // it deliberately does not search fallback fonts for this special character.
            // Reuse a real horizontal stroke without changing original atlas pixels or glyphs.
            var stroke = asset.characterTable.FirstOrDefault(character => character.unicode == '-') ??
                asset.characterTable.FirstOrDefault(character => character.unicode == '+');
            if (stroke == null)
                throw new InvalidDataException("No horizontal stroke for numeric-font underline: " + asset.name);
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            GlyphRect underlineRect;
            try
            {
                if (!readable.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(atlas))))
                    throw new InvalidDataException("Could not read numeric font atlas: " + asset.name);
                var pixels = readable.GetPixels32();
                var bounds = stroke.glyph.glyphRect;
                underlineRect = new GlyphRect();
                for (int y = bounds.y; y < bounds.y + bounds.height; y++)
                {
                    int start = -1;
                    for (int x = bounds.x; x <= bounds.x + bounds.width; x++)
                    {
                        bool ink = false;
                        if (x < bounds.x + bounds.width)
                        {
                            var pixel = pixels[y * readable.width + x];
                            ink = pixel.a >= 250 && Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)) >= 160;
                        }
                        if (ink && start < 0) start = x;
                        if (!ink && start >= 0)
                        {
                            if (x - start > underlineRect.width)
                                underlineRect = new GlyphRect(start, y, x - start, 1);
                            start = -1;
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(readable); }
            if (underlineRect.width < 3)
                throw new InvalidDataException("Numeric font atlas has no visible underline stroke: " + asset.name);
            float thickness = Mathf.Max(1f, Mathf.Round(lineHeight / 20f));
            var metrics = new GlyphMetrics(underlineRect.width, thickness, 0, -thickness, underlineRect.width);
            var glyph = new Glyph(underscore, metrics, underlineRect, 1, 0);
            asset.glyphTable.Add(glyph);
            asset.characterTable.Add(new TMP_Character(underscore, asset, glyph));
            var face = asset.faceInfo;
            face.underlineOffset = -thickness;
            face.underlineThickness = thickness;
            asset.faceInfo = face;
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
