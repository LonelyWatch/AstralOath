#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using AstralOath.Client.Arena;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Tests
{
    public sealed class ArenaDraftPreviewTests
    {
        [UnityTest]
        public IEnumerator EveryRosterMiniatureKeepsItsFeetAnchoredWithoutClipping()
        {
            var art = AssetDatabase.LoadAssetAtPath<ArenaDraftArt>("Assets/Game/Modules/Arena/Configs/ArenaDraftArt.asset");
            var type = typeof(ArenaDraftArt).Assembly.GetType("AstralOath.Client.Arena.ArenaDraftPortrait", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Preview regression", typeof(RectTransform), typeof(RawImage));
            var image = root.GetComponent<RawImage>();
            image.rectTransform.sizeDelta = new Vector2(240, 240);
            var preview = (IDisposable)Activator.CreateInstance(type, flags, null, new object[] { image, true }, null);
            try
            {
                foreach (var hero in art.Heroes)
                {
                    var source = typeof(ArenaDraftArt.Hero).GetMethod("MakeModel", flags).Invoke(hero, null);
                    Assert.That((bool)type.GetMethod("Show", flags).Invoke(preview, new[] { source, null, null }), Is.True, hero.Id);
                    var camera = (Camera)type.GetField("camera", flags).GetValue(preview);
                    var player = (ISkeletonPlayer)type.GetField("player", flags).GetValue(preview);
                    Vector3 foot = camera.WorldToViewportPoint(player.Root.transform.position);
                    Assert.That(foot.x, Is.EqualTo(.5f).Within(.001f), hero.Id);
                    Assert.That(foot.y, Is.EqualTo(50f / 240f).Within(.001f), hero.Id);
                    for (int frame = 0; frame < 8; frame++)
                    {
                        type.GetMethod("Tick", flags).Invoke(preview, new object[] { .12f });
                        var bounds = player.Root.GetComponent<MeshFilter>().sharedMesh.bounds;
                        var min = camera.WorldToViewportPoint(player.Root.transform.TransformPoint(bounds.min));
                        var max = camera.WorldToViewportPoint(player.Root.transform.TransformPoint(bounds.max));
                        Assert.That(min.x, Is.GreaterThanOrEqualTo(0), hero.Id + " left crop");
                        Assert.That(min.y, Is.GreaterThanOrEqualTo(0), hero.Id + " feet crop");
                        Assert.That(max.x, Is.LessThanOrEqualTo(1), hero.Id + " right crop");
                        Assert.That(max.y, Is.LessThanOrEqualTo(1), hero.Id + " head crop");
                    }
                    yield return null;
                }
            }
            finally { preview.Dispose(); Object.DestroyImmediate(root); }
        }
    }
}
#endif
