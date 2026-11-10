using System;
using System.Collections.Generic;
using System.Threading;

namespace AstralOath.Client.Core
{
    // Owns main-thread binding cleanup, most-recent binding first.
    public sealed class BindingScope : IDisposable
    {
        private readonly CancellationTokenSource source;
        private readonly LinkedList<Action> cleanup = new LinkedList<Action>();
        private bool disposed;

        public BindingScope(CancellationToken parent)
        {
            source = CancellationTokenSource.CreateLinkedTokenSource(parent);
            Token = source.Token;
        }

        public CancellationToken Token { get; }

        public Action Add(Action release)
        {
            if (release == null)
                throw new ArgumentNullException(nameof(release));
            if (disposed)
            {
                release();
                return () => { };
            }
            var entry = cleanup.AddLast(release);
            return () => entry.List?.Remove(entry);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            var errors = new List<Exception>();
            Try(source.Cancel, errors);
            while (cleanup.Last != null)
            {
                Action release = cleanup.Last.Value;
                cleanup.RemoveLast();
                Try(release, errors);
            }
            source.Dispose();
            if (errors.Count > 0)
                throw new AggregateException(errors);
        }

        private static void Try(Action action, List<Exception> errors)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
    }
}