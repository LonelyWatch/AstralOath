using System.Threading.Channels;

namespace AstralOath.Server.Social;

// DI-owned, bounded invalidations only. No message bodies or durable authority.
internal sealed class ChannelChatEvents
{
    private readonly object gate=new();
    private readonly Dictionary<(string,int),HashSet<Subscription>> channels=[];
    internal int SubscriptionCount { get { lock(gate)return channels.Values.Sum(x=>x.Count); } }
    internal Subscription Subscribe(string realm,int channel)
    {
        lock(gate){var key=(realm,channel);if(!channels.TryGetValue(key,out var set))channels.Add(key,set=[]);var item=new Subscription(this,key);set.Add(item);return item;}
    }
    internal void Publish(string realm,int channel)
    {
        lock(gate)if(channels.TryGetValue((realm,channel),out var set))foreach(var item in set)item.Notify();
    }
    internal sealed class Subscription(ChannelChatEvents owner,(string,int) key):IDisposable
    {
        private readonly Channel<bool> queue=Channel.CreateBounded<bool>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest});
        internal void Notify()=>queue.Writer.TryWrite(true);
        internal async Task Wait(CancellationToken ct)=>await queue.Reader.ReadAsync(ct);
        public void Dispose(){lock(owner.gate){if(owner.channels.TryGetValue(key,out var set)&&set.Remove(this)&&set.Count==0)owner.channels.Remove(key);queue.Writer.TryComplete();}}
    }
}
