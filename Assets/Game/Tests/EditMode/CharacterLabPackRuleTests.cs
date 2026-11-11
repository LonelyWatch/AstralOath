using System;
using AstralOath.Editor;
using NUnit.Framework;
using YooAsset.Editor;

namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabPackRuleTests
    {
        private const string Frames = "Assets/Game/Modules/CharacterPresentation/Res/Characters/c1001/Effects/cut_fhd_sk_c1001_3/frames";
        private const string Banks = "Assets/Game/Modules/CharacterPresentation/Res/Shared/Sounds/sound";
        // Exact cache prefix from the failed Windows build. Keep the full default output root.
        private const string Cache = "D:/UnityProjects/AstralOath/.artifacts/characterlab-builds/Win64/player-v1-cb0467d6473f458caca45708cc2145b7/bundles/StandaloneWindows64/CharacterLab/OutputCache/";

        [Test]
        public void PreviouslyFailingFrameBundleFitsTheActualWindowsCachePath()
        {
            string oldName = ((IPackRule)new PackDirectory()).GetPackRuleResult(Data(Frames + "/0001.png"))
                .GetBundleName("CharacterLab", false);
            Require((Cache + oldName).Length >= 260, "The fixture must reproduce the original path overflow.");
            string name = Name(Frames + "/0001.png");
            Require((Cache + name + ".manifest").Length < 260, "Bundle and manifest must fit at the original output root.");
            Require(name.Length + ".manifest".Length <= 64, "The collector must honor the build's 64-character filename budget.");
        }

        [Test]
        public void AssetsInOneDirectoryShareABundleButOtherDirectoriesRemainSeparate()
        {
            Require(Name(Frames + "/0001.png") == Name(Frames + "/0002.png"), "Frame directory was split.");
            Require(Name(Frames + "/0001.png") != Name(Frames + "/nested/0001.png"), "Different directories were merged.");
            Require(Name(Frames + "/0001.png") != Name(Frames.Replace("c1001", "c1005") + "/0001.png"), "Different characters were merged.");
        }

        [Test]
        public void EveryBankKeepsItsOwnBundleWithinTheSameDirectory()
        {
            string master = Name(Banks + "/master.bank.bytes");
            Require(master != Name(Banks + "/master.strings.bank.bytes"), "Master and strings banks were merged.");
            Require(master != Name(Banks + "/sfx1.bank.bytes"), "Independent banks were merged.");
            Require(master != Name(Banks + "/readme.txt"), "A bank was merged with its parent directory.");
            Require(master.Length + ".manifest".Length <= 64, "A bank filename exceeds the cache budget.");
        }

        [Test]
        public void BundleIdentityIsStableAcrossPathSeparatorsCaseAndCollectorMetadata()
        {
            const string expected = "lab_2bdf2165442a55ad4a3523c54dc22827.bundle";
            Require(Name(Frames + "/0001.png") == expected, "Bundle identity changed for an unchanged asset group.");
            var data = new PackRuleData((Frames + "/0002.png").ToUpperInvariant().Replace('/', '\\'), "Other/Collector", "OtherGroup", "OtherData");
            Require(new CharacterLabPackRule().GetPackRuleResult(data).GetBundleName("CharacterLab", false) == expected,
                "Platform path spelling or collector metadata changed the group identity.");
        }

        [Test]
        public void SameLeafDirectoryInDifferentAncestorsCannotCollide()
        {
            Require(Name("Assets/Game/Characters/a_b/frames/0001.png") != Name("Assets/Game/Characters/a/b/frames/0001.png"),
                "Underscore and path separators must not collapse different groups.");
        }

        private static PackRuleData Data(string path) => new PackRuleData(path, path, "CharacterLabAssets", "");
        private static string Name(string path) => new CharacterLabPackRule().GetPackRuleResult(Data(path)).GetBundleName("CharacterLab", false);

        // A plain assertion exception also lets the exact NUnit cases run without loading Unity.
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new AssertionException(message);
        }
    }
}
