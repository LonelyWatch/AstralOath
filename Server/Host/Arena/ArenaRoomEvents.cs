using System.Threading.Channels;

namespace AstralOath.Server.Arena;

[Flags]
internal enum RoomChange { Room = 1, Chat = 2 }

// Subscribers consume invalidations. The initial sync and all payloads are read through authorized HTTP queries.
internal sealed class ArenaRoomEvents
{
    private readonly object gate = new();
    private readonly Dictionary<string, HashSet<Subscription>> rooms = new(StringComparer.Ordinal);

    public Subscription Subscribe(string roomId, RoomChange interests)
    {
        var subscription = new Subscription(this, roomId, interests);
        lock (gate)
        {
            if (!rooms.TryGetValue(roomId, out var members)) rooms.Add(roomId, members = []);
            members.Add(subscription);
        }
        return subscription;
    }

    public void Publish(string roomId, RoomChange changes)
    {
        lock (gate)
            if (rooms.TryGetValue(roomId, out var members))
                foreach (var member in members) member.Notify(changes);
    }

    internal sealed class Subscription(ArenaRoomEvents owner, string roomId, RoomChange interests) : IDisposable
    {
        private readonly Channel<RoomChange> queue = Channel.CreateBounded<RoomChange>(1);
        public ValueTask<RoomChange> ReadAsync(CancellationToken token) => queue.Reader.ReadAsync(token);

        internal void Notify(RoomChange changes)
        {
            changes &= interests;
            if (changes == 0) return;
            // Publishers hold the room-events lock. Merge both dirty bits if the reader is busy.
            queue.Reader.TryRead(out var pending);
            queue.Writer.TryWrite(changes | pending);
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (owner.rooms.TryGetValue(roomId, out var members) && members.Remove(this) && members.Count == 0)
                    owner.rooms.Remove(roomId);
                queue.Writer.TryComplete();
            }
        }
    }
}
