using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using AstralOath.Editor;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabAddressRuleTests
    {
        private const string Asset = "Assets/Game/Modules/CharacterPresentation/Res/Characters/test/model.json";
        private static readonly DateTime Timestamp = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);
        private string directory;
        private string manifest;

        [SetUp] public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "AstralOath-address-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            manifest = Path.Combine(directory, "manifest.json");
            CharacterLabAddressRule.Invalidate();
            Write(manifest, "first");
        }

        [TearDown] public void TearDown()
        {
            CharacterLabAddressRule.Invalidate();
            if (directory != null && Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test] public void UnchangedManifestIsNotReadAgainForEachResource()
        {
            Require(Address(manifest) == "characterlab/source/first", "Initial manifest was not read.");
            using var file = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Require(Address(manifest) == "characterlab/source/first", "A cached manifest should resolve without reading the locked file.");
        }

        [Test] public void ChangedTimestampRefreshesAnEqualLengthManifest()
        {
            Address(manifest);
            long length = new FileInfo(manifest).Length;
            Write(manifest, "other");
            File.SetLastWriteTimeUtc(manifest, Timestamp.AddSeconds(10));
            Require(new FileInfo(manifest).Length == length, "This fixture must isolate the timestamp key.");
            Require(Address(manifest) == "characterlab/source/other", "An mtime change did not refresh the address.");
        }

        [Test] public void ChangedLengthRefreshesAManifestWithTheSameTimestamp()
        {
            Address(manifest);
            Write(manifest, "longer-address");
            Require(Address(manifest) == "characterlab/source/longer-address", "A size change with preserved mtime did not refresh the address.");
        }

        [Test] public void DifferentFullPathsDoNotShareAnEqualStampCacheEntry()
        {
            Address(manifest);
            string other = Path.Combine(directory, "other.json");
            Write(other, "other");
            Require(Address(other) == "characterlab/source/other", "A different manifest reused the previous address map.");
        }

        [Test] public void ExplicitInvalidationReloadsContentEvenWhenMetadataIsUnchanged()
        {
            Address(manifest);
            Write(manifest, "other");
            CharacterLabAddressRule.Invalidate();
            Require(Address(manifest) == "characterlab/source/other", "Explicit invalidation did not reload the manifest.");
        }

        private static void Write(string path, string address)
        {
            File.WriteAllText(path, "{\"schemaVersion\":1,\"entries\":[{\"path\":\"" + Asset + "\",\"address\":\"characterlab/source/" + address + "\"}]}");
            File.SetLastWriteTimeUtc(path, Timestamp);
        }

        private static string Address(string path)
        {
            try
            {
                var map = (Dictionary<string, string>)typeof(CharacterLabAddressRule)
                    .GetMethod("GetAddressMap", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { path });
                return map[Asset];
            }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
