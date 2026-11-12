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
            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>(Manifest);
            if (!manifest) throw new InvalidOperationException("Character import manifest is missing.");
            var paths = JObject.Parse(manifest.text)["entries"].ToDictionary(
                item => (string)item["address"], item => (string)item["path"], StringComparer.Ordinal);
            var sources = graphs.SelectMany(graph => JObject.Parse(graph.text)["assets"])
                .Where(IsBattleAsset).GroupBy(item => (string)item["source"], StringComparer.Ordinal)
                .Select(group => group.First()).OrderBy(item => (string)item["source"], StringComparer.Ordinal).ToArray();

            property.arraySize = sources.Length;
            for (int i = 0; i < sources.Length; i++)
            {
                var dependency = sources[i];
                var row = property.GetArrayElementAtIndex(i);
                string source = (string)dependency["source"];
                string type = (string)dependency["type"];
                string address = (string)dependency["address"];
                row.FindPropertyRelative("source").stringValue = source;
                if (type == "texture")
                {
                    row.FindPropertyRelative("texture").objectReferenceValue = Load<Texture2D>(address);
                    continue;
                }
                row.FindPropertyRelative("texture").objectReferenceValue = null;
                if (type != "skeleton")
                {
                    row.FindPropertyRelative("document").objectReferenceValue = Load<TextAsset>(address);
                    continue;
                }

                var descriptor = JObject.Parse(Load<TextAsset>(address).text);
                row.FindPropertyRelative("document").objectReferenceValue = Load<TextAsset>((string)descriptor["jsonAddress"]);
                row.FindPropertyRelative("atlas").objectReferenceValue = Load<TextAsset>((string)descriptor["atlasAddress"]);
                row.FindPropertyRelative("premultipliedAlpha").boolValue = (bool)descriptor["premultipliedAlpha"];
                row.FindPropertyRelative("scale").floatValue = (float)descriptor["scale"];
                var pages = descriptor["pages"].ToArray();
                var names = row.FindPropertyRelative("pageNames");
                var textures = row.FindPropertyRelative("pages");
                names.arraySize = textures.arraySize = pages.Length;
                for (int page = 0; page < pages.Length; page++)
                {
                    names.GetArrayElementAtIndex(page).stringValue = (string)pages[page]["name"];
                    textures.GetArrayElementAtIndex(page).objectReferenceValue = Load<Texture2D>((string)pages[page]["address"]);
                }
            }

            T Load<T>(string address) where T : UnityEngine.Object
            {
                if (!paths.TryGetValue(address, out var path))
                    throw new InvalidOperationException("Missing imported battle asset " + address);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (!asset) throw new InvalidOperationException("Missing battle asset at " + path);
                return asset;
            }
        }

        private static bool IsBattleAsset(JToken item)
        {
            string source = (string)item["source"];
            string type = (string)item["type"];
            return type == "effect" || type == "particle" || type == "timeline" ||
                (type == "skeleton" && source.StartsWith("effect/", StringComparison.Ordinal)) ||
                (type == "texture" && source.StartsWith("particle/", StringComparison.Ordinal));
        }
    }
}
