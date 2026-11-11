using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using AstralOath.Editor;
using NUnit.Framework;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabBuildProfileTests
    {
        [Test] public void LaboratoryRejectsFoundationAndLegacyBaselines()
        {
            var lab = CharacterLabBuildProfile.CharacterLab;
            Assert.That(lab.AcceptsBaseline(null, null, null, null), Is.False);
            Assert.That(lab.AcceptsBaseline("foundation", "Foundation", "AstralOath.Client.FoundationDemo", "AstralOath.Client.FoundationDemo.FoundationEntry"), Is.False);
            Assert.That(lab.AcceptsBaseline("characterlab", "CharacterLab", "AstralOath.Client.CharacterLab", "AstralOath.Client.CharacterLab.CharacterLabEntry"), Is.True);
            Assert.That(lab.AcceptsBaseline("characterlab", "Foundation", "AstralOath.Client.CharacterLab", "AstralOath.Client.CharacterLab.CharacterLabEntry"), Is.False);
        }
        [Test] public void FoundationPreservesLegacyCompatibilityButRejectsLaboratory()
        {
            var foundation = CharacterLabBuildProfile.Foundation;
            Assert.That(foundation.AcceptsBaseline(null, null, null, null), Is.True);
            Assert.That(foundation.AcceptsBaseline("characterlab", "CharacterLab", "AstralOath.Client.CharacterLab", "AstralOath.Client.CharacterLab.CharacterLabEntry"), Is.False);
        }
        [Test] public void LaboratorySettingsIgnoreFoundationSigningEnvironment()
        {
            string prior = Environment.GetEnvironmentVariable("ASTRAL_SIGNING_KEY");
            try
            {
                Environment.SetEnvironmentVariable("ASTRAL_SIGNING_KEY", "do-not-inherit.key");
                var settings = CharacterLabBuildSettings.Parse(null, Array.Empty<string>());
                Assert.That(settings.outputRoot, Is.EqualTo(".artifacts/characterlab-builds"));
                Assert.That(settings.baselinePath, Is.Empty); Assert.That(settings.signingKey, Is.Empty);
            }
            finally { Environment.SetEnvironmentVariable("ASTRAL_SIGNING_KEY", prior); }
        }
        [Test] public void LaboratoryUsesExplicitArgumentsAndRejectsSigning()
        {
            var settings = CharacterLabBuildSettings.Parse("{\"contentVersion\":2,\"outputRoot\":\"lab-local\"}",
                new[]{"-astral-content-version","7","-astral-output-root","lab-cli","-astral-development"});
            Assert.That(settings.contentVersion, Is.EqualTo(7)); Assert.That(settings.outputRoot, Is.EqualTo("lab-cli"));
            Assert.That(settings.development, Is.True);
            Assert.Throws<InvalidDataException>(() => CharacterLabBuildSettings.Parse("{\"signingKey\":\"secret.key\"}", Array.Empty<string>()));
        }
        [Test] public void FailedBuildRestoresHybridClrAssemblySelection()
        {
            ScriptableObject settings = null; string original = null;
            try
            {
                Assert.Throws<InvalidOperationException>(() =>
                {
                    using var scope = new CharacterLabBuildSettingsScope();
                    settings = Resources.FindObjectsOfTypeAll<ScriptableObject>()
                        .Single(item => item.GetType().FullName == "HybridCLR.Settings.HybridCLRSettings");
                    original = EditorJsonUtility.ToJson(settings);
                    settings.GetType().GetField("hotUpdateAssemblies").SetValue(settings, new[] { "Failed.Lab.Build" });
                    settings.GetType().GetField("useGlobalIl2cpp").SetValue(settings, true);
                    throw new InvalidOperationException("Simulated build failure");
                });
                Assert.That(EditorJsonUtility.ToJson(settings), Is.EqualTo(original));
            }
            finally { if (settings != null && original != null) EditorJsonUtility.FromJsonOverwrite(original, settings); }
        }
        [Test] public void LaboratoryRejectsIncompleteAndInvalidArguments()
        {
            Assert.Throws<ArgumentException>(() => CharacterLabBuildSettings.Parse(null, new[]{"-astral-output-root"}));
            Assert.Throws<ArgumentException>(() => CharacterLabBuildSettings.Parse(null, new[]{"-astral-content-version","broken"}));
        }
    }
}
