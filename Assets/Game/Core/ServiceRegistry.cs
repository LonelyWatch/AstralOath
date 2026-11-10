using System;
using System.Collections.Generic;
using System.Threading;

namespace AstralOath.Client.Core
{
    public sealed class ServiceRegistry : IDisposable
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly BindingScope lifetime;
        private bool disposed;

        public ServiceRegistry(CancellationToken parent)
        {
            lifetime = new BindingScope(parent);
        }

        public void Add<T>(T service) where T : class
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ServiceRegistry));
            services.Add(typeof(T), service ?? throw new ArgumentNullException(nameof(service)));
            if (service is IDisposable resource)
                lifetime.Add(resource.Dispose);
        }

        public T Get<T>() where T : class
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(ServiceRegistry));
            return (T)services[typeof(T)];
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            services.Clear();
            lifetime.Dispose();
        }
    }
}