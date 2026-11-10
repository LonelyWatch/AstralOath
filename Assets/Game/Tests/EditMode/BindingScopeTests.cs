using System;
using System.Collections.Generic;
using AstralOath.Client.Core;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class BindingScopeTests
    {
        [Test]
        public void ClosingCancelsBeforeUnbindingAndReopeningStartsFresh()
        {
            var calls = new List<string>();
            var closed = new BindingScope(default);
            var token = closed.Token;
            Action signal = null;
            Action handler = () => calls.Add("event");
            signal += handler;
            closed.Add(() => signal -= handler);
            closed.Add(() => calls.Add(token.IsCancellationRequested ? "cancelled" : "live"));
            signal();
            closed.Dispose();
            closed.Dispose();
            signal?.Invoke();

            using var reopened = new BindingScope(default);
            Assert.That(reopened.Token.IsCancellationRequested, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "event", "cancelled" }));
        }

        [Test]
        public void ThrowingUnbindDoesNotPreventRemainingCleanup()
        {
            var scope = new BindingScope(default);
            bool cleaned = false;
            scope.Add(() => cleaned = true);
            scope.Add(() => throw new InvalidOperationException());
            Assert.Throws<AggregateException>(() => scope.Dispose());
            Assert.That(cleaned, Is.True);
        }
    }
}