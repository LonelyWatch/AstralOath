using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    internal static class BattleSkillPresentationAssets
    {
        private const string Manifest = "Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json";

        public static void Collect(SerializedProperty property, IReadOnlyList<TextAsset> graphs)
        {
            var paths = AddressMap();
            var sources = graphs.SelectMany(graph => JObject.Parse(graph.text)["assets"])
                .Where(IsBattleAsset).GroupBy(item => (string)item["source"], StringComparer.Ordinal)
                .Select(group => group.First()).OrderBy(item => (string)item["source"], StringComparer.Ordinal).ToArray();

            property.arraySize = sources.Length + 3;
            for (int i = 0; i < sources.Length; i++)
            {
                var dependency = sources[i];
                var row = property.GetArrayElementAtIndex(i);
                string source = (string)dependency["source"];
                string type = (string)dependency["type"];
                string address = (string)dependency["address"];
                row.FindPropertyRelative("document").objectReferenceValue = null;
                row.FindPropertyRelative("atlas").objectReferenceValue = null;
                row.FindPropertyRelative("pages").arraySize = 0;
                row.FindPropertyRelative("pageNames").arraySize = 0;
                row.FindPropertyRelative("source").stringValue = source;
                if (type == "texture")
                {
                    row.FindPropertyRelative("texture").objectReferenceValue = Load<Texture2D>(address, paths);
                    continue;
                }
                row.FindPropertyRelative("texture").objectReferenceValue = null;
                if (type == "cutin")
                {
                    var document = Load<TextAsset>(address, paths);
                    row.FindPropertyRelative("document").objectReferenceValue = document;
                    var frames = JObject.Parse(document.text)["frames"].ToArray();
                    var frameTextures = row.FindPropertyRelative("pages");
                    frameTextures.arraySize = frames.Length;
                    for (int frame = 0; frame < frames.Length; frame++)
                        frameTextures.GetArrayElementAtIndex(frame).objectReferenceValue = Load<Texture2D>((string)frames[frame]["address"], paths);
                    continue;
                }
                if (type != "skeleton")
                {
                    row.FindPropertyRelative("document").objectReferenceValue = Load<TextAsset>(address, paths);
                    continue;
                }

                SetSkeleton(row, address, paths);
                row.FindPropertyRelative("source").stringValue = source;
            }

            for (int index = 0; index < 3; index++)
            {
                var row = property.GetArrayElementAtIndex(sources.Length + index);
                row.FindPropertyRelative("document").objectReferenceValue = null;
                row.FindPropertyRelative("atlas").objectReferenceValue = null;
                row.FindPropertyRelative("pages").arraySize = 0;
                row.FindPropertyRelative("pageNames").arraySize = 0;
                string filename = "bg_hatch_" + (index + 1) + ".png";
                row.FindPropertyRelative("source").stringValue = "effect/" + filename;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Game/Modules/BattleScene/Res/Effects/Cinematic/" + filename);
                if (!texture) throw new InvalidOperationException("Missing original cinematic background " + filename);
                row.FindPropertyRelative("texture").objectReferenceValue = texture;
            }
        }

        internal static Dictionary<string, string> AddressMap()
        {
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Manifest);
            if (!manifest) throw new InvalidOperationException("Character import manifest is missing.");
            return JObject.Parse(manifest.text)["entries"].ToDictionary(
                item => (string)item["address"], item => (string)item["path"], StringComparer.Ordinal);
        }

        internal static T Load<T>(string address, IReadOnlyDictionary<string, string> paths) where T : UnityEngine.Object
        {
            if (!paths.TryGetValue(address, out var path))
                throw new InvalidOperationException("Missing imported battle asset " + address);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!asset) throw new InvalidOperationException("Missing battle asset at " + path);
            return asset;
        }

        internal static void SetSkeleton(SerializedProperty row, string address, IReadOnlyDictionary<string, string> paths)
        {
            var descriptor = JObject.Parse(Load<TextAsset>(address, paths).text);
            row.FindPropertyRelative("source").stringValue = address;
            row.FindPropertyRelative("document").objectReferenceValue = Load<TextAsset>((string)descriptor["jsonAddress"], paths);
            string atlasAddress = (string)descriptor["atlasAddress"];
            row.FindPropertyRelative("atlas").objectReferenceValue = string.IsNullOrEmpty(atlasAddress) ? null : Load<TextAsset>(atlasAddress, paths);
            row.FindPropertyRelative("premultipliedAlpha").boolValue = (bool)descriptor["premultipliedAlpha"];
            row.FindPropertyRelative("scale").floatValue = (float)descriptor["scale"];
            var pages = descriptor["pages"].ToArray();
            var names = row.FindPropertyRelative("pageNames");
            var textures = row.FindPropertyRelative("pages");
            names.arraySize = textures.arraySize = pages.Length;
            for (int page = 0; page < pages.Length; page++)
            {
                names.GetArrayElementAtIndex(page).stringValue = (string)pages[page]["name"];
                textures.GetArrayElementAtIndex(page).objectReferenceValue = Load<Texture2D>((string)pages[page]["address"], paths);
            }
        }

        private static bool IsBattleAsset(JToken item)
        {
            string source = (string)item["source"];
            string type = (string)item["type"];
            return type == "effect" || type == "particle" || type == "timeline" || type == "cutin" ||
                (type == "skeleton" && (source.StartsWith("effect/", StringComparison.Ordinal) || source.StartsWith("camera/", StringComparison.Ordinal) || source.StartsWith("spani/", StringComparison.Ordinal))) ||
                (type == "texture" && source.StartsWith("particle/", StringComparison.Ordinal));
        }
    }
}
