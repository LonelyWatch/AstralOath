using System;
using System.IO;
using System.Linq;
using AstralOath.Client.Summon;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Editor
{
    public static class SummonPresentationBuilder
    {
        public const string DirectoryPath = "Assets/Game/Modules/Summon/Res/Presentation/";
        public const string AssetPath = DirectoryPath + "SummonPresentation.asset";

        [Serializable] private sealed class Graph { public Group[] groups; public Skeleton[] skeletons; public Sound[] audio; }
        [Serializable] private sealed class Group { public string id; public Part[] parts; }
        [Serializable] private sealed class Part
        {
            public string source, animation;
            public float x, y, delay, rotate, speed, scale;
            public bool loop;
        }
        [Serializable] private sealed class Skeleton { public string id, document, atlas; public Page[] pages; }
        [Serializable] private sealed class Page { public string name, asset; }
        [Serializable] private sealed class Sound { public string id, asset; }

        [MenuItem("Tools/召唤/生成原版召唤演出")]
        public static void Generate()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Graph graph = JsonUtility.FromJson<Graph>(Read<TextAsset>("presentation.json").text);
            var skeletons = graph.skeletons.ToDictionary(item => item.id, StringComparer.Ordinal);
            var phases = graph.groups.Select(group => new SummonPresentation.Phase
            {
                id = group.id,
                parts = group.parts.Select(part =>
                {
                    Skeleton skeleton = skeletons[part.source];
                    return new SummonPresentation.Layer
                    {
                        animation = part.animation, skeleton = Read<TextAsset>(skeleton.document),
                        atlas = Read<TextAsset>(skeleton.atlas),
                        pageNames = skeleton.pages.Select(page => page.name).ToArray(),
                        pages = skeleton.pages.Select(page => Read<Texture2D>(page.asset)).ToArray(),
                        x = part.x, y = part.y, delay = part.delay, rotate = part.rotate, speed = part.speed,
                        scale = part.scale, loop = part.loop
                    };
                }).ToArray()
            }).ToArray();
            var cues = graph.audio.Select(entry => new SummonPresentation.Cue
            {
                id = entry.id, clip = Read<AudioClip>(entry.asset)
            }).ToArray();
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Game/Modules/Arena/Resources/AstralOathArenaDraftPortrait.shader");
            if (!shader) throw new FileNotFoundException("The original skeleton composite shader is missing.");
            var asset = AssetDatabase.LoadAssetAtPath<SummonPresentation>(AssetPath);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<SummonPresentation>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            asset.Configure(phases, cues, shader);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private static T Read<T>(string name) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(DirectoryPath + name);
            if (!asset) throw new FileNotFoundException("Missing original summon asset", DirectoryPath + name);
            return asset;
        }
    }
}
