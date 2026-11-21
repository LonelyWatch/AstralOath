using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    internal static class BattleActionFeedbackAssets
    {
        private const string Root = "Assets/Game/Modules/BattleScene/Res/Effects/ActionFeedback/";

        internal static BattleSkillPresentationAsset[] Load()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "source.json");
            if (!manifest) throw new InvalidOperationException("Run ImportActionFeedback.py before updating the battle scene.");
            var rows = JObject.Parse(manifest.text)["files"].ToArray();
            var result = new List<BattleSkillPresentationAsset>();
            foreach (var row in rows)
            {
                string source = ((string)row["source"]).Substring("原始资源/".Length);
                string suffix = Path.GetExtension(source);
                if (suffix == ".atlas") continue;
                string path = Root + (string)row["asset"];
                var asset = new BattleSkillPresentationAsset { source = source, scale = .01f, premultipliedAlpha = true };
                if (suffix == ".scsp")
                {
                    asset.document = Required<TextAsset>(path);
                    asset.atlas = Required<TextAsset>(Root + (string)row["dependencies"][0]);
                    asset.pageNames = row["pages"].Select(p => (string)p["pageName"]).ToArray();
                    asset.pages = row["pages"].Select(p => Required<Texture2D>(Root + (string)p["asset"])).ToArray();
                }
                else if (suffix == ".cfx" || suffix == ".particle") asset.document = Required<TextAsset>(path);
                else
                {
                    PreserveFeedbackTexture(path);
                    asset.texture = Required<Texture2D>(path);
                }
                result.Add(asset);
            }
            return result.ToArray();
        }

        private static void PreserveFeedbackTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var windows = importer.GetPlatformTextureSettings("Standalone");
            if (importer.userData == "AstralOath.ImportOverride" && !importer.mipmapEnabled &&
                !importer.alphaIsTransparency && importer.sRGBTexture && importer.npotScale == TextureImporterNPOTScale.None &&
                importer.wrapMode == TextureWrapMode.Clamp && importer.textureCompression == TextureImporterCompression.Uncompressed &&
                windows.overridden && windows.format == TextureImporterFormat.RGBA32 &&
                windows.textureCompression == TextureImporterCompression.Uncompressed) return;

            // PMA color and coverage must stay paired. A Windows DXT5 override otherwise
            // compresses the two channels independently when the page dimensions allow it.
            importer.userData = "AstralOath.ImportOverride";
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = false;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            windows.overridden = true;
            windows.format = TextureImporterFormat.RGBA32;
            windows.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SetPlatformTextureSettings(windows);
            importer.SaveAndReimport();
        }

        // Called after the existing skill collector; it also supports repeated scene regeneration.
        internal static void AppendTo(SerializedProperty property)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < property.arraySize; i++) indices[property.GetArrayElementAtIndex(i).FindPropertyRelative("source").stringValue] = i;
            foreach (var asset in Load())
            {
                if (!indices.TryGetValue(asset.source, out int index)) { index = property.arraySize++; indices.Add(asset.source, index); }
                var row = property.GetArrayElementAtIndex(index);
                row.FindPropertyRelative("source").stringValue = asset.source;
                row.FindPropertyRelative("document").objectReferenceValue = asset.document;
                row.FindPropertyRelative("atlas").objectReferenceValue = asset.atlas;
                row.FindPropertyRelative("texture").objectReferenceValue = asset.texture;
                row.FindPropertyRelative("scale").floatValue = asset.scale;
                row.FindPropertyRelative("premultipliedAlpha").boolValue = asset.premultipliedAlpha;
                var names = row.FindPropertyRelative("pageNames"); var pages = row.FindPropertyRelative("pages");
                names.arraySize = pages.arraySize = asset.pages?.Length ?? 0;
                for (int p = 0; p < pages.arraySize; p++)
                { names.GetArrayElementAtIndex(p).stringValue = asset.pageNames[p]; pages.GetArrayElementAtIndex(p).objectReferenceValue = asset.pages[p]; }
            }
        }

        private static T Required<T>(string path) where T : UnityEngine.Object
            => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing native feedback asset: " + path);
    }
}
