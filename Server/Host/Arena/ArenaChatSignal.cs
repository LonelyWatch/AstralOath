namespace AstralOath.Server.Arena;

// A wake-up hint for this host. History and authorization still come from MySQL.
internal sealed class ArenaChatSignal
{
    private readonly object gate = new();
    private readonly Dictionary<string, HashSet<TaskCompletionSource>> waiting = new(StringComparer.Ordinal);

    public Subscription Subscribe(string roomId)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (!waiting.TryGetValue(roomId, out var readers))
                waiting.Add(roomId, readers = new HashSet<TaskCompletionSource>());
            readers.Add(completion);
        }
        return new Subscription(this, roomId, completion);
    }

    public void Publish(string roomId)
    {
        HashSet<TaskCompletionSource>? readers;
        lock (gate)
        {
            if (!waiting.Remove(roomId, out readers)) return;
        }
        foreach (var reader in readers)
            reader.TrySetResult();
    }

    private void Unsubscribe(string roomId, TaskCompletionSource completion)
    {
        lock (gate)
        {
            if (waiting.TryGetValue(roomId, out var readers) && readers.Remove(completion) && readers.Count == 0)
                waiting.Remove(roomId);
        }
    }

    internal sealed class Subscription(ArenaChatSignal signal, string roomId, TaskCompletionSource completion) : IDisposable
    {
        public Task Changed => completion.Task;
        public void Dispose() => signal.Unsubscribe(roomId, completion);
    }
}
