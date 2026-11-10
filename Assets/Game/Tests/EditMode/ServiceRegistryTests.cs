using System;
using System.Collections.Generic;
using AstralOath.Client.Core;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class ServiceRegistryTests
    {
        private sealed class Owned : IDisposable
        {
            private readonly Action release;
            public Owned(Action release) => this.release = release;
            public void Dispose() => release();
        }

        [Test]
        public void RegistryIsScopedAndDisposesInReverseOrder()
        {
            var calls = new List<int>();
            var first = new Owned(() => calls.Add(1));
            var registry = new ServiceRegistry(default);
            registry.Add<Owned>(first);
            registry.Add<IDisposable>(new Owned(() => calls.Add(2)));
            Assert.That(registry.Get<Owned>(), Is.SameAs(first));
            registry.Dispose();
            registry.Dispose();
            Assert.That(calls, Is.EqualTo(new[] { 2, 1 }));
            Assert.Throws<ObjectDisposedException>(() => registry.Get<Owned>());
        }
    }
}