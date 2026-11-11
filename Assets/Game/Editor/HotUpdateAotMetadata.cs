using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.CodeGen.AOT;
using HybridCLR.Meta;
using HybridCLR.Mod;

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

        private sealed class BuildAssemblyResolver : IAssemblyResolver
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
