using System;
using System.Collections.Generic;
using XLua;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class FoundationLua : IDisposable
    {
        private readonly LuaEnv vm;
        private readonly Dictionary<string, LuaFunction> exports = new Dictionary<string, LuaFunction>();

        public FoundationLua(string source)
        {
            vm = new LuaEnv();
            try
            {
                using var environment = vm.NewTable();
                var result = vm.DoString(source, "FoundationBinder", environment);
                if (result.Length != 1 || !(result[0] is LuaTable table))
                    throw new InvalidOperationException("Foundation binder must return an export table.");
                using (table)
                {
                    var allowed = new HashSet<string>(FoundationLuaExports.Names, StringComparer.Ordinal);
                    foreach (string name in table.GetKeys<string>())
                    {
                        if (!allowed.Remove(name))
                            throw new InvalidOperationException($"Unexpected binder export {name}.");
                        exports.Add(name, table.Get<LuaFunction>(name) ??
                            throw new InvalidOperationException($"Invalid binder export {name}."));
                    }
                    if (allowed.Count > 0)
                        throw new InvalidOperationException("Foundation binder exports are incomplete.");
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // The Lua environment receives only primitive values. No CS import, host object or
        // delegate bridge is required, including under IL2CPP.
        public object[] Call(string name, params object[] args) => exports[name].Call(args);
        public void Tick() => vm.Tick();

        public void Dispose()
        {
            foreach (var export in exports.Values)
                export.Dispose();
            exports.Clear();
            vm.Dispose();
        }
    }
}