using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Shared.Updates;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class HotUpdateCodeLoaderTests
    {
        [Test]
        public void EntryConstructorFailureRequiresANewProcessBeforeAnySecondAttempt()
        {
            var type = typeof(FoundationBootstrap).Assembly.GetType("AstralOath.Client.Bootstrap.HotUpdateCodeLoader", true);
            const BindingFlags fields = BindingFlags.Static | BindingFlags.NonPublic;
            var attempted = type.GetField("loadAttempted", fields);
            object previous = attempted.GetValue(null);
            try
            {
                attempted.SetValue(null, false);
                var assembly = typeof(FailingCodeEntry).Assembly;
                string name = assembly.GetName().Name;
                var manifest = new HotUpdateManifest
                {
                    entryAssembly = name,
                    entryType = typeof(FailingCodeEntry).FullName,
                    aotMetadata = Array.Empty<HotUpdateFile>(),
                    assemblies = new[] { new HotUpdateAssembly { name = name, dependencies = Array.Empty<string>() } }
                };
                var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
                var loader = constructor.Invoke(constructor.GetParameters().Length == 1
                    ? new object[] { manifest } : new object[] { manifest, "regression-entry-failure" });
                var bytes = (Dictionary<string, byte[]>)type.GetField("bytes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(loader);
                bytes.Add(name, File.ReadAllBytes(assembly.Location));
                var create = type.GetMethod("CreateEntry", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.Throws<TargetInvocationException>(() => create.Invoke(loader, null));
                var retry = Assert.Throws<TargetInvocationException>(() => create.Invoke(loader, null));
                Assert.That(retry.InnerException, Is.TypeOf<InvalidOperationException>(),
                    "A failed constructor must not be invoked again using an already loaded assembly.");
                StringAssert.Contains("Restart", retry.InnerException.Message);
            }
            finally { attempted.SetValue(null, previous); }
        }
    }

    public sealed class FailingCodeEntry : IHotUpdateEntry
    {
        public FailingCodeEntry() => throw new InvalidOperationException("Deliberate test entry construction failure.");
        public Task StartAsync(GameLaunchContext context) => Task.CompletedTask;
        public Task<EchoResult> EchoAsync() => Task.FromResult<EchoResult>(null);
        public void Dispose() { }
    }
}
