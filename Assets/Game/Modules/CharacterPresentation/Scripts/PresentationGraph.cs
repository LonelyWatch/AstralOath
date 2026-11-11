using System;
using System.Collections.Generic;
namespace AstralOath.Client.CharacterPresentation
{
    [Serializable] public sealed class PresentationNode
    {
        public string id, kind, eventName, eventSource, actor, animation, assetAddress, sourceCommand, diagnostic;
        public double start, duration;
        public float x, y, scale = 1;
        public bool verified;
    }
    [Serializable] public sealed class PresentationGraph { public string id; public double duration; public PresentationNode[] nodes; public string[] diagnostics; }
    // Only verified timing is accepted here. Raw STG retains its separate conditional graph.
    public sealed class PresentationGraphScheduler
    {
        private readonly PresentationNode[] nodes;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> events = new Dictionary<string, double>(StringComparer.Ordinal);
        private double time;
        public event Action<PresentationNode> Execute;
        public PresentationGraphScheduler(PresentationGraph graph)
        {
            nodes = graph?.nodes ?? throw new ArgumentNullException(nameof(graph));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
                if (node == null || string.IsNullOrEmpty(node.id) || !ids.Add(node.id) || node.start < 0 || double.IsNaN(node.start) || double.IsInfinity(node.start))
                    throw new ArgumentException("Graph nodes require unique ids and finite nonnegative timing.");
        }
        public void Advance(double target)
        {
            if (target < time || double.IsNaN(target) || double.IsInfinity(target)) throw new ArgumentOutOfRangeException(nameof(target));
            time = target;
            foreach (var node in nodes)
            {
                if (!node.verified || fired.Contains(node.id)) continue;
                double origin = 0;
                if (!string.IsNullOrEmpty(node.eventName) && !events.TryGetValue(Key(node.eventSource, node.eventName), out origin)) continue;
                if (origin + node.start > time) continue;
                fired.Add(node.id);
                Execute?.Invoke(node);
            }
        }
        public void Signal(string actor, string name)
        {
            string key = Key(actor, name);
            if (!events.ContainsKey(key)) events.Add(key, time);
        }
        public void Reset() { time = 0; fired.Clear(); events.Clear(); }
        private static string Key(string actor, string name) => (actor ?? "") + "\0" + name;
    }
}
