using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static TMP_FontAsset EnhancementBitmapDigits()
        {
            const string path = Common + "Fonts/EquipmentEnhancementDigits.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing && existing.characterTable.Count >= 12 && existing.faceInfo.pointSize == 34 &&
                existing.material.shader.name == "TextMeshPro/Bitmap Custom Atlas" &&
                existing.characterLookupTable['+'].glyph.metrics.horizontalAdvance == 15) return existing;
            var atlas = new Texture2D(512, 64, TextureFormat.RGBA32, false) { name = "EquipmentEnhancementDigits Atlas", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels32(new Color32[512 * 64]);
            var glyphs = new List<Glyph>();
            var characters = new List<TMP_Character>();
            string tokens = "+0123456789";
            for (int index = 0; index < tokens.Length; index++)
            {
                char token = tokens[index];
                string source = Common + "Textures/EnhancementDigits/itxt_num" + (token == '+' ? "plus" : token.ToString()) + "_b.png";
                var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!image.LoadImage(File.ReadAllBytes(source)) || image.width != 26 || image.height != 34)
                    throw new InvalidDataException("Unexpected original enhancement numeral dimensions: " + source);
                int x = 2 + index * 28;
                atlas.SetPixels(x, 2, 26, 34, image.GetPixels());
                if (token == '+')
                {
                    // TMP reserves U+005F for underline geometry. Reuse the original plus's
                    // horizontal pixels so the bitmap font has real coverage rather than an empty glyph.
                    int fullestRow = 0, fullestCount = -1;
                    for (int yy = 0; yy < 34; yy++)
                    {
                        int covered = 0;
                        for (int xx = 0; xx < 26; xx++) if (image.GetPixel(xx, yy).a > .5f) covered++;
                        if (covered > fullestCount) { fullestCount = covered; fullestRow = yy; }
                    }
                    var line = image.GetPixels(0, fullestRow, 26, 1);
                    atlas.SetPixels(320, 2, 26, 1, line);
                    atlas.SetPixels(320, 3, 26, 1, line);
                    var underline = new Glyph(100, new GlyphMetrics(26, 2, 0, -2, 26), new GlyphRect(320, 2, 26, 2), 1, 0);
                    glyphs.Add(underline);
                    characters.Add(new TMP_Character('_', underline));
                }
                // The original nodes deliberately overlap their transparent 26px quads:
                // plus→first digit is 15 source units; tens→ones is 16. Do not trim by alpha.
                float advance = token == '+' ? 15 : 16;
                var glyph = new Glyph((uint)index, new GlyphMetrics(26, 34, 0, 34, advance), new GlyphRect(x, 2, 26, 34), 1, 0);
                glyphs.Add(glyph);
                characters.Add(new TMP_Character(token, glyph));
                Object.DestroyImmediate(image);
            }
            atlas.Apply(false, false);
            var fontAsset = existing ? existing : ScriptableObject.CreateInstance<TMP_FontAsset>();
            if (existing)
                foreach (var subasset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (subasset is Texture2D || subasset is Material) Object.DestroyImmediate(subasset, true);
            // An unset version makes TMP run its legacy upgrader, replacing the hand-authored tables.
            var serialized = new SerializedObject(fontAsset);
            serialized.FindProperty("m_Version").stringValue = "1.1.0";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            fontAsset.name = "EquipmentEnhancementDigits";
            fontAsset.faceInfo = new FaceInfo { familyName = "Epic Seven enhancement bitmap", styleName = "Original", pointSize = 34, scale = 1,
                lineHeight = 34, ascentLine = 34, capLine = 30, meanLine = 24, baseline = 0, descentLine = 0,
                underlineOffset = -2, underlineThickness = 2 };
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;
            fontAsset.atlasTextures = new[] { atlas };
            fontAsset.glyphTable.Clear();
            fontAsset.characterTable.Clear();
            fontAsset.glyphTable.AddRange(glyphs);
            fontAsset.characterTable.AddRange(characters);
            serialized.Update();
            serialized.FindProperty("m_AtlasWidth").intValue = 512;
            serialized.FindProperty("m_AtlasHeight").intValue = 64;
            serialized.FindProperty("m_AtlasRenderMode").intValue = (int)UnityEngine.TextCore.LowLevel.GlyphRenderMode.RASTER;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var shader = Shader.Find("TextMeshPro/Bitmap Custom Atlas");
            if (!shader) throw new InvalidDataException("The TMP bitmap shader is unavailable.");
            var material = new Material(shader) { name = "EquipmentEnhancementDigits Material", mainTexture = atlas };
            fontAsset.material = material;
            fontAsset.ReadFontAssetDefinition();
            if (!existing) AssetDatabase.CreateAsset(fontAsset, path);
            AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            AssetDatabase.AddObjectToAsset(material, fontAsset);
            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssetIfDirty(fontAsset);
            return fontAsset;
        }
    }
}
