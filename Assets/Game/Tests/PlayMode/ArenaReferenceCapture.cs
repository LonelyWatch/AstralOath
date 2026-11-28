#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    internal static class ArenaReferenceCapture
    {
        [Serializable]
        private sealed class BoundsFile
        {
            public int width, height;
            public List<Element> elements = new List<Element>();
        }

        [Serializable]
        private sealed class Element
        {
            public string path;
            public float x, y, width, height;
        }

        internal static Vector2Int Size(int defaultWidth, int defaultHeight)
        {
            string value = Environment.GetEnvironmentVariable("ASTRAL_ARENA_CAPTURE_SIZE");
            if (string.IsNullOrEmpty(value)) return new Vector2Int(defaultWidth, defaultHeight);
            string[] parts = value.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h) &&
                w >= 640 && h >= 360 && w <= 3840 && h <= 2160) return new Vector2Int(w, h);
            throw new ArgumentException("Invalid ASTRAL_ARENA_CAPTURE_SIZE.");
        }

        internal static void Measure(string directory, string name, Transform root, Camera camera, RenderTexture target)
        {
            if (string.IsNullOrEmpty(directory)) return;
            var file = new BoundsFile {width=target.width, height=target.height};
            var corners = new Vector3[4];
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(false))
            {
                rect.GetWorldCorners(corners);
                var a = camera.WorldToScreenPoint(corners[0]);
                var b = camera.WorldToScreenPoint(corners[2]);
                if (b.x < 0 || a.x > target.width || b.y < 0 || a.y > target.height) continue;
                string path = rect.name;
                for (Transform t = rect.parent; t != null && t != root; t = t.parent)
                    path = t.name + "/" + path;
                file.elements.Add(new Element {path=path, x=a.x, y=target.height-b.y, width=b.x-a.x, height=b.y-a.y});
            }
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, name + ".bounds.json"), JsonUtility.ToJson(file));
        }
    }
}
#endif
