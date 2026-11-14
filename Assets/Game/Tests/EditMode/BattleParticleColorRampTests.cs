using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Tests
{
    public sealed class BattleParticleColorRampTests
    {
        private static Type Runtime => Type.GetType(
            "AstralOath.Client.BattleScene.BattleHudParticles, AstralOath.Client.BattleScene", true);

        [Test]
        public void EachParticleKeepsOneCompleteRampAndSeededReplayRestoresTheSameColors()
        {
            var definition = new TextAsset(JObject.Parse(@"{'emitters':[{
                'name':'two ramps','textureFileName':'fixture','maxParticles':32,'scale':1,
                'burningType':1,'repeat':1,'duration':0,'emissionRate':1,
                'blendFuncSource':770,'blendFuncDestination':1,'particleLifespan':2,
                'startParticleSize':10,'finishParticleSize':10,
                'colorBlends':['0,1,1,0,0;1,0,1,0,0','0,1,0,1,0;1,0,0,1,0']
            }]}").ToString());
            var texture = new Texture2D(2, 2);
            var root = new GameObject("Multiple source particle ramp regression");
            IDisposable particles = null;
            try
            {
                particles = (IDisposable)Activator.CreateInstance(Runtime, definition,
                    new Dictionary<string, Texture2D> { ["fixture"] = texture }, root.transform, Vector2.zero, 1f, 1);
                Runtime.GetMethod("Play").Invoke(particles, null);
                var mesh = root.GetComponentInChildren<MeshFilter>(true).sharedMesh;
                var first = mesh.colors;
                Assert.That(first.Length, Is.EqualTo(128));
                Assert.That(first.Count(color => color.r > .99f), Is.GreaterThan(0));
                Assert.That(first.Count(color => color.g > .99f), Is.GreaterThan(0), "Both independent source ramps are represented.");
                Runtime.GetMethod("Tick").Invoke(particles, new object[] { .5f });
                var later = mesh.colors;
                Assert.That(later.Length, Is.EqualTo(first.Length));
                for (int index = 0; index < later.Length; index++)
                {
                    Assert.That(later[index].r, Is.EqualTo(first[index].r));
                    Assert.That(later[index].g, Is.EqualTo(first[index].g));
                    Assert.That(later[index].a, Is.EqualTo(.75f).Within(.0001f));
                }
                Runtime.GetMethod("Play").Invoke(particles, null);
                Assert.That(mesh.colors, Is.EqualTo(first), "A replay must choose the same ramps without frame-by-frame random flicker.");
            }
            finally
            {
                particles?.Dispose();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void ANewRampMayRestartAtZeroButAnUnorderedRampIsStillRejected()
        {
            var read = Runtime.GetMethod("ReadRamps", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(read, Is.Not.Null);
            var result = (Array)read.Invoke(null, new object[] { new[] { "0,1,1,0,0;1,0,1,0,0", "0,1,0,1,0;1,0,0,1,0" } });
            Assert.That(result.Length, Is.EqualTo(2));
            var error = Assert.Throws<TargetInvocationException>(() => read.Invoke(null,
                new object[] { new[] { "0.5,1,1,0,0;0.1,0,1,0,0" } }));
            Assert.That(error.InnerException, Is.TypeOf<FormatException>());
        }
    }
}