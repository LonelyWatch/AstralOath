using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using dnlib.DotNet;
using HybridCLR.CodeGen.AOT;
using HybridCLR.Meta;

namespace AstralOath.Editor
{
    public static class HotUpdateAotMetadata
    {
        private const int AnalysisIterations = 20;
        public static string[] GetRequiredNames(string compiled, string[] hotAssemblies, string archive,
            IEnumerable<string> archivedAssemblies, IEnumerable<string> initialMetadata)
        {
            var archiveNames = new HashSet<string>(archivedAssemblies, StringComparer.Ordinal);
            var resolver = new BuildAssemblyResolver(compiled, hotAssemblies, archive, archiveNames);
            var collector = new AssemblyReferenceDeepCollector(resolver, hotAssemblies.ToList());
            try
            {
                foreach (var module in collector.GetLoadedModulesOfRootAssemblies())
                    RequireDeclaredAotConstructors(module, archiveNames);
                var bounded = Analyze(collector, AnalysisIterations);
                var analyzer = Analyze(collector, AnalysisIterations + 1);
                // HybridCLR 9.0.3 enqueues work only when its public GenericMethods set grows.
                // Equal counts after one extra iteration establish that the traversal reached a fixed point.
                if (bounded.GenericMethods.Count != analyzer.GenericMethods.Count ||
                    bounded.GenericTypes.Count != analyzer.GenericTypes.Count)
                    throw new InvalidDataException("热更泛型元数据分析未在限制内收敛，无法确认母包兼容性。");

                var required = analyzer.AotGenericTypes.Select(type => type.Type.Module.Assembly.Name.String)
                    .Concat(analyzer.AotGenericMethods.Select(method => method.Method.Module.Assembly.Name.String))
                    .Concat(initialMetadata)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                foreach (string name in required)
                {
                    if (!archiveNames.Contains(name))
                        throw new FileNotFoundException("母包未归档所需补充元数据：" + name);
                }
                return required;
            }
            finally
            {
                foreach (var module in collector.LoadedModules.Values)
                    module.Dispose();
            }
        }

        private static void RequireDeclaredAotConstructors(ModuleDefMD hot, HashSet<string> archiveNames)
        {
            foreach (var member in hot.GetMemberRefs())
            {
                if (member.Name != ".ctor" || member.MethodSig == null) continue;
                var type = member.DeclaringType;
                // Array constructors are supplied by the runtime. Generic constructors belong to the open type.
                if (type.ToTypeSig().ElementType is ElementType.Array or ElementType.SZArray) continue;
                var owner = type.ScopeType.ResolveTypeDef();
                if (owner == null || !archiveNames.Contains(owner.Module.Assembly.Name.String)) continue;
                // dnlib can resolve a stripped derived constructor to the identically shaped base constructor.
                if (member.ResolveMethod()?.DeclaringType != owner)
                    throw new InvalidDataException($"{hot.Name} 引用的构造方法未保留在母包声明类型上：{member.FullName}");
            }
        }

        private static AOTReferenceAnalyzer Analyze(AssemblyReferenceDeepCollector collector, int iterations)
        {
            var analyzer = new AOTReferenceAnalyzer(new AOTReferenceAnalyzer.Options
            {
                Collector = collector,
                MaxIterationCount = iterations
            });
            analyzer.Run();
            return analyzer;
        }

        private sealed class BuildAssemblyResolver : HybridCLR.Mod.IAssemblyResolver
        {
            private readonly Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);

            public BuildAssemblyResolver(string compiled, IEnumerable<string> hot, string archive, IEnumerable<string> aot)
            {
                foreach (string name in hot)
                    paths.Add(name, Path.Combine(compiled, name + ".dll"));
                foreach (string name in aot)
                    paths.Add(name, Path.Combine(archive, name + ".dll"));
            }

            public string ResolveAssembly(string assemblyName, bool throwExIfNotFind)
            {
                if (paths.TryGetValue(assemblyName, out string path) && File.Exists(path))
                    return path;
                if (throwExIfNotFind)
                    throw new FileNotFoundException("热更引用的程序集不在新热更 DLL 或母包 AOT 归档中：" + assemblyName);
                return null;
            }
        }
    }
}
