using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace AstralOath.Client.Skeletons
{
    internal enum SkeletonBlend { Normal, Additive, Multiply, Screen }
    // A single renderer keeps contiguous submeshes in source draw order, including A/B/A page changes.
    internal sealed class SkeletonMesh : IDisposable
    {
        private readonly Mesh mesh;
        private readonly MeshRenderer renderer;
        private readonly Shader shader;
        private readonly bool pma;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector4> darkColors = new List<Vector4>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<List<int>> triangles = new List<List<int>>();
        private readonly Dictionary<(Texture2D, SkeletonBlend), Material> cache = new Dictionary<(Texture2D, SkeletonBlend), Material>();
        internal SkeletonMesh(GameObject root, bool pma)
        {
            this.pma = pma;
            shader = Resources.Load<Shader>("AstralOathSkeleton");
            if (shader == null) throw new InvalidOperationException("Missing AOT Resources/AstralOathSkeleton shader.");
            mesh = new Mesh { name = "Skeleton dynamic mesh", indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic(); root.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = root.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        internal void Clear()
        {
            vertices.Clear(); uvs.Clear(); colors.Clear(); darkColors.Clear(); materials.Clear();
            foreach (var indices in triangles) indices.Clear();
        }
        internal void Add(Texture2D texture, SkeletonBlend blend, float[] xy, int floatCount, float[] uv,
            int[] indices, int indexCount, Color light, Color dark)
        {
            if (floatCount == 0 || indexCount == 0) return;
            if (!cache.TryGetValue((texture, blend), out var material))
            {
                material = new Material(shader) { name = "Skeleton " + blend, mainTexture = texture };
                material.SetFloat("_PremultipliedAlpha", pma ? 1 : 0);
                material.SetFloat("_PmaSrgb", pma && texture.isDataSRGB && QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 : 0);
                material.SetInt("_SrcBlend", (int)(blend == SkeletonBlend.Multiply ? BlendMode.DstColor : BlendMode.One));
                material.SetInt("_DstBlend", (int)(blend == SkeletonBlend.Additive ? BlendMode.One :
                    blend == SkeletonBlend.Screen ? BlendMode.OneMinusSrcColor : BlendMode.OneMinusSrcAlpha));
                cache.Add((texture, blend), material);
            }
            int segment = materials.Count - 1;
            if (segment < 0 || materials[segment] != material)
            {
                segment = materials.Count; materials.Add(material);
                if (triangles.Count <= segment) triangles.Add(new List<int>());
            }
            int offset = vertices.Count;
            for (int i = 0; i < floatCount; i += 2)
            {
                vertices.Add(new Vector3(xy[i], xy[i + 1], 0));
                // Official atlas coordinates are top-down; Unity textures are bottom-up.
                uvs.Add(new Vector2(uv[i], 1 - uv[i + 1])); colors.Add(light);
                darkColors.Add(new Vector4(dark.r, dark.g, dark.b, 0));
            }
            var output = triangles[segment];
            for (int i = 0; i < indexCount; i++) output.Add(offset + indices[i]);
        }
        internal void Commit()
        {
            mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetColors(colors); mesh.SetUVs(1, darkColors);
            mesh.subMeshCount = materials.Count;
            for (int i = 0; i < materials.Count; i++) mesh.SetTriangles(triangles[i], i, false);
            mesh.RecalculateBounds(); renderer.SetSharedMaterials(materials);
        }
        public void Dispose()
        {
            foreach (var material in cache.Values) SkeletonPlayer.Destroy(material);
            cache.Clear(); SkeletonPlayer.Destroy(mesh);
        }
    }
}
