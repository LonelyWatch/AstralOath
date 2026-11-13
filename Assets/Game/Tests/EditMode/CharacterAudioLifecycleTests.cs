using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Core;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterAudioLifecycleTests
    {
        private sealed class UnusedAssets : IAssetService
        {
            public int Calls;
            public Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object
            { Calls++; throw new InvalidOperationException("Assets must not be loaded after disposal"); }
        }

        [Test]
        public void DisposedServiceRejectsInitializationBeforeNativeOrAssetWork()
        {
            var assets = new UnusedAssets();
            var service = new FmodCharacterAudioService(CancellationToken.None);
            service.Dispose();
            Assert.Throws<ObjectDisposedException>(() => service.InitializeAsync(assets, new[] { "bank" }).GetAwaiter().GetResult());
            Assert.AreEqual(0, assets.Calls);
            Assert.IsFalse(service.IsAvailable);
            Assert.DoesNotThrow(() => { service.Dispose(); service.Tick(); });
        }

        [TestCase("")]
        [TestCase("invalid-key")]
        [TestCase("voc_c1009_sk3_1_p2(zhs)")]
        public void InvalidMissingJapaneseKeyFailsBeforeNativeWork(string key)
        {
            var assets = new UnusedAssets();
            using var service = new FmodCharacterAudioService(CancellationToken.None);
            Assert.Throws<InvalidOperationException>(() => service.InitializeAsync(assets, new[] { "bank" }, new[] { key })
                .GetAwaiter().GetResult());
            Assert.That(assets.Calls, Is.Zero);
            Assert.That(service.IsAvailable, Is.False);
        }

        [TestCase("voc_c1001_shout_1(zhs)", "voc_c1001_shout_1")]
        [TestCase("voc_c1001_shout_1(ja)", "voc_c1001_shout_1")]
        [TestCase("voc_c1001_shout_1(en)", "voc_c1001_shout_1")]
        [TestCase("voc_c1001_shout_1", "voc_c1001_shout_1")]
        [TestCase("voc_c1001_shout_1(unknown)", "voc_c1001_shout_1(unknown)")]
        public void JapaneseProgrammerSelectionOnlyNormalizesKnownLocaleSuffixes(string source, string expected)
        {
            var method = typeof(FmodCharacterAudioService).GetMethod("JapaneseAudioTableKey", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            Assert.AreEqual(expected, method.Invoke(null, new object[] { source }));
        }
    }
}
