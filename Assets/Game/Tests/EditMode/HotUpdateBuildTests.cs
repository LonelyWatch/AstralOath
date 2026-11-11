using System;
using System.IO;
using AstralOath.Editor;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class HotUpdateBuildTests
    {
        [TestCase("../parent")]
        [TestCase("..")]
        [TestCase("broken version")]
        [TestCase("NUL")]
        public void BuildIdentityCannotEscapeTheOutputDirectory(string identity)
        {
            Assert.That(HotUpdateBuildInputs.IsSafeId(identity), Is.False);
            Assert.That(HotUpdateBuildInputs.IsSafeId("win64-1.2.0"), Is.True);
        }

        [Test]
        public void RepeatedFullBuildsGenerateDistinctBaselineIdentities()
        {
            string first = HotUpdateBuildInputs.NewBaselineId();
            string second = HotUpdateBuildInputs.NewBaselineId();
            Assert.That(Guid.TryParseExact(first, "N", out _), Is.True);
            Assert.That(second, Is.Not.EqualTo(first));
        }

        [Test]
        public void WindowsPathBudgetIncludesTheYooAssetCacheBundle()
        {
            Assert.That(HotUpdateBuildInputs.FitsWindowsPathBudget(new string('x', 195), 64), Is.True);
            Assert.That(HotUpdateBuildInputs.FitsWindowsPathBudget(new string('x', 196), 64), Is.False);
        }

        [Test]
        public void ADirectoryWithoutCompletionRecordCannotBeUsedAsBaseline()
        {
            Assert.That(HotUpdateBuildInputs.HasCompletedBaseline(Application.temporaryCachePath), Is.False);
        }

        [TestCase("Android", "ARM64", "Android-ETC2", "Android", "Android-ASTC")]
        [TestCase("Android", "ARM64", "Android-ETC2", "StandaloneWindows64", "Win64")]
        [TestCase("StandaloneWindows64", "ARM64", "Win64", "StandaloneWindows64", "Win64")]
        public void AotBaselineCannotBeReusedForAnIncompatibleTarget(string platform, string architecture,
            string variant, string target, string targetVariant)
        {
            Assert.That(HotUpdateBuildInputs.SupportsTarget(platform, architecture, variant, target, targetVariant), Is.False);
            Assert.That(HotUpdateBuildInputs.SupportsTarget("Android", "ARM64", "Android-ASTC", "Android", "Android-ASTC"), Is.True);
        }

        [TestCase(1)]
        [TestCase(23)]
        public void GenericMetadataAnalysisMustResolveTheArchiveAndReachAFixedPoint(int chainLength)
        {
            string root = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString("N"));
            string aot = Directory.CreateDirectory(Path.Combine(root, "aot")).FullName;
            string hot = Directory.CreateDirectory(Path.Combine(root, "hot")).FullName;
            try
            {
                File.Copy(typeof(object).Assembly.Location, Path.Combine(aot, "mscorlib.dll"));
                using var archived = NewFixtureModule("ArchiveProbe");
                var api = AddFixtureType(archived, "Api");
                MethodDefUser generic = null;
                for (int i = chainLength - 1; i >= 0; i--)
                {
                    var previous = generic;
                    generic = AddFixtureMethod(api, "Identity" + i,
                        MethodSig.CreateStaticGeneric(1, new GenericMVar(0), new GenericMVar(0)));
                    generic.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
                    generic.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                    if (previous != null)
                    {
                        var next = new MethodSpecUser(previous, new GenericInstMethodSig(new GenericMVar(0)));
                        generic.Body.Instructions.Add(Instruction.Create(OpCodes.Call, next));
                    }
                    generic.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                }
                archived.Write(Path.Combine(aot, "ArchiveProbe.dll"));

                using var candidate = NewFixtureModule("HotProbe");
                var archiveRef = new AssemblyRefUser("ArchiveProbe", new Version(1, 0, 0, 0));
                var apiRef = new TypeRefUser(candidate, "Fixture", "Api", archiveRef);
                var identity = new MemberRefUser(candidate, generic.Name, generic.MethodSig, apiRef);
                var specialized = new MethodSpecUser(identity, new GenericInstMethodSig(candidate.CorLibTypes.Int32));
                var caller = AddFixtureMethod(AddFixtureType(candidate, "Consumer"), "Call",
                    MethodSig.CreateStatic(candidate.CorLibTypes.Int32));
                caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
                caller.Body.Instructions.Add(Instruction.Create(OpCodes.Call, specialized));
                caller.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                candidate.Write(Path.Combine(hot, "HotProbe.dll"));

                if (chainLength > 20)
                {
                    Assert.Throws<InvalidDataException>(() => HotUpdateAotMetadata.GetRequiredNames(hot,
                        new[] { "HotProbe" }, aot, new[] { "mscorlib", "ArchiveProbe" }, new[] { "mscorlib" }));
                    return;
                }

                var metadata = HotUpdateAotMetadata.GetRequiredNames(hot, new[] { "HotProbe" }, aot,
                    new[] { "mscorlib", "ArchiveProbe" }, new[] { "mscorlib" });
                Assert.That(metadata, Is.EqualTo(new[] { "ArchiveProbe", "mscorlib" }));
                Assert.Throws<FileNotFoundException>(() => HotUpdateAotMetadata.GetRequiredNames(hot,
                    new[] { "HotProbe" }, aot, new[] { "mscorlib" }, new[] { "mscorlib" }));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static ModuleDefUser NewFixtureModule(string name)
        {
            var module = new ModuleDefUser(name + ".dll") { Kind = ModuleKind.Dll };
            new AssemblyDefUser(name, new Version(1, 0, 0, 0)).Modules.Add(module);
            return module;
        }

        private static TypeDefUser AddFixtureType(ModuleDef module, string name)
        {
            var type = new TypeDefUser("Fixture", name, module.CorLibTypes.Object.TypeDefOrRef)
            {
                Attributes = TypeAttributes.Public
            };
            module.Types.Add(type);
            return type;
        }

        private static MethodDefUser AddFixtureMethod(TypeDef type, string name, MethodSig signature)
        {
            var method = new MethodDefUser(name, signature, MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static)
            {
                Body = new CilBody()
            };
            type.Methods.Add(method);
            return method;
        }

        [Test]
        public void ModifiedAotBytesFailVerification()
        {
            string file = Path.GetTempFileName();
            try
            {
                const string hash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
                File.WriteAllText(file, "abc");
                Assert.That(HotUpdateBuildInputs.HasMatchingHash(file, hash), Is.True);
                File.WriteAllText(file, "abd");
                Assert.That(HotUpdateBuildInputs.HasMatchingHash(file, hash), Is.False);
            }
            finally
            {
                File.Delete(file);
            }
        }
    }
}
