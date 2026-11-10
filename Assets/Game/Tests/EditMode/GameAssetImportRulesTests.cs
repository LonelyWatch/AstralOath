using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class GameAssetImportRulesTests
    {
        private string root;

        [SetUp]
        public void SetUp()
        {
            string name = "ImportRulesTest-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/Game", name);
            root = "Assets/Game/" + name;
        }

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(root);

        [TestCase("Sounds", 1, AudioClipLoadType.DecompressOnLoad)]
        [TestCase("Sounds/Voices", 1, AudioClipLoadType.CompressedInMemory)]
        [TestCase("Sounds/Music", 60, AudioClipLoadType.Streaming)]
        [TestCase("Sounds/Voices/Long", 60, AudioClipLoadType.Streaming)]
        public void AudioPolicySurvivesReimport(string directory, int seconds, AudioClipLoadType expected)
        {
            string path = root + "/" + directory + "/clip.wav";
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WriteWave(path, seconds);
            Import(path);
            Assert.That(AudioSettings(path).loadType, Is.EqualTo(expected));
            Import(path);
            Assert.That(AudioSettings(path).loadType, Is.EqualTo(expected));
        }

        [Test]
        public void AudioOverrideSurvivesReimport()
        {
            string path = root + "/clip.wav";
            WriteWave(path, 1);
            Import(path);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.userData = "AstralOath.ImportOverride";
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.Streaming;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            Import(path);
            Assert.That(AudioSettings(path).loadType, Is.EqualTo(AudioClipLoadType.Streaming));
        }

        [Test]
        public void SpriteUsesPlatformDefaultsAndKeepsExplicitException()
        {
            string path = root + "/sprite.png";
            var texture = new Texture2D(8, 8);
            try { File.WriteAllBytes(path, texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            Import(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.isReadable = true;
            importer.mipmapEnabled = true;
            importer.SaveAndReimport();
            importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.isReadable, Is.False);
            Assert.That(importer.mipmapEnabled, Is.False);
            foreach (string platform in new[] { "Standalone", "Android" })
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                Assert.That(settings.overridden, Is.True, platform);
                Assert.That(settings.maxTextureSize, Is.EqualTo(2048), platform);
                Assert.That(settings.format, Is.EqualTo(TextureImporterFormat.Automatic), platform);
                Assert.That(settings.textureCompression, Is.EqualTo(TextureImporterCompression.Compressed), platform);
            }

            importer.userData = "AstralOath.ImportOverride";
            importer.isReadable = true;
            importer.SaveAndReimport();
            Import(path);
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).isReadable, Is.True);
        }

        private static void Import(string path) => AssetDatabase.ImportAsset(path,
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        private static AudioImporterSampleSettings AudioSettings(string path) =>
            ((AudioImporter)AssetImporter.GetAtPath(path)).defaultSampleSettings;

        private static void WriteWave(string path, int seconds)
        {
            const int sampleRate = 8000;
            int size = seconds * sampleRate * sizeof(short);
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(size + 36);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(size);
            writer.Write(new byte[size]);
        }
    }
}
