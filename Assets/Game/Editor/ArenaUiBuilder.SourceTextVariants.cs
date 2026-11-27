using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private const string SourceTextVariantsPath = Common + "Materials/SourceTextVariants.shadervariants";

        // Materials live outside Yoo's shared shader bundle. Retain the four actual
        // UI outline combinations explicitly instead of relying on scene usage.
        // ArenaUiBuilder collects this as common/source-text-variants with PackShaderVariants.
        // BindingMap intentionally accepts only GameObject/Component targets, not asset references.
        private static ShaderVariantCollection SourceTextVariants()
        {
            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(SourceTextVariantsPath);
            if (!collection)
            {
                collection = new ShaderVariantCollection { name = "SourceTextVariants" };
                AssetDatabase.CreateAsset(collection, SourceTextVariantsPath);
            }
            collection.Clear();
            var shader = Load<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader");
            foreach (var keywords in new[]
            {
                new[] { "OUTLINE_ON" },
                new[] { "OUTLINE_ON", "UNITY_UI_CLIP_RECT" },
                new[] { "OUTLINE_ON", "UNITY_UI_ALPHACLIP" },
                new[] { "OUTLINE_ON", "UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP" }
            })
            {
                var variant = new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keywords);
                if (!collection.Add(variant))
                    throw new InvalidDataException("Could not retain a required TMP UI outline variant.");
            }
            EditorUtility.SetDirty(collection);
            AssetDatabase.SaveAssetIfDirty(collection);
            return collection;
        }
    }
}
