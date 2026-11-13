using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AstralOath.Client.BattleScene
{
    internal static class SkillTimelineOverrides
    {
        internal const string AssetPath = "Assets/Game/Modules/BattleScene/Configs/skill-timeline-overrides.json";

        internal static JObject ReadCatalog(string json)
        {
            var root = JObject.Parse(json);
            if ((int?)root["version"] != 1 || !(root["graphs"] is JObject graphs) ||
                graphs.Properties().Any(graph => !(graph.Value is JObject)))
                throw new InvalidOperationException("Unsupported skill timeline override catalog.");
            return graphs;
        }
        internal const string AbsoluteTimeField = "_astralAtMs";
        internal const int MaximumMilliseconds = 180000;

        internal static readonly HashSet<string> EditableFields = new HashSet<string>(StringComparer.Ordinal)
        {
            "x", "y", "offset_x", "offset_y", "time", "duration", "fade", "fadeIn", "fadeOut",
            "scale", "opacity", "curve", "color", "localZ", "globalZ", "timeScale", "power",
            "powerX", "powerY", "location", "style", "mode", "self_x", "away_x", "rand_x", "rand_y", "accel"
        };

        internal static IEnumerable<JObject> Nodes(JObject graph) =>
            (graph["source"]?["entities"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                .Concat(graph["additions"]?["entities"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>());

        internal static JObject NewPatch(JObject graph) => new JObject
        {
            ["version"] = 1,
            ["graph"] = (string)graph["id"],
            ["sourceHash"] = Fingerprint(graph),
            ["nodes"] = new JObject()
        };

        public static JObject Apply(JObject original, JObject patch)
        {
            if (patch == null) return original;
            string id = (string)original["id"];
            if (id == null || (int?)patch["version"] != 1 || (string)patch["graph"] != id ||
                !(patch["nodes"] is JObject edits) ||
                (patch["sourceHash"] != null && (string)patch["sourceHash"] != Fingerprint(original)))
                throw new InvalidOperationException("Skill timeline patch is incompatible with source graph " + id + ".");

            var result = (JObject)original.DeepClone();
            var nodes = Nodes(result).ToDictionary(node => ((int)node["guid"]).ToString(CultureInfo.InvariantCulture));
            foreach (var entry in edits.Properties())
            {
                if (!nodes.TryGetValue(entry.Name, out var node) || !(entry.Value is JObject change) ||
                    change.Properties().Any(property => property.Name != "atMs" && property.Name != "fields"))
                    throw InvalidNode(id, entry.Name);

                if (change["atMs"] is JToken time)
                {
                    if (time.Type != JTokenType.Integer || (long)time < 0 || (long)time > MaximumMilliseconds)
                        throw InvalidNode(id, entry.Name);
                    node[AbsoluteTimeField] = time.DeepClone();
                }

                if (change["fields"] == null) continue;
                if (!(change["fields"] is JObject fields)) throw InvalidNode(id, entry.Name);
                foreach (var field in fields.Properties())
                {
                    if (!EditableFields.Contains(field.Name) || !ValidField(field.Value))
                        throw InvalidNode(id, entry.Name);
                    node[field.Name] = field.Value.DeepClone();
                }
            }
            return result;
        }

        private static bool ValidField(JToken value) => value.Type switch
        {
            JTokenType.Integer or JTokenType.Float => Math.Abs((double)value) <= MaximumMilliseconds,
            JTokenType.String => ((string)value).Length <= 512,
            JTokenType.Boolean => true,
            _ => false
        };

        private static InvalidOperationException InvalidNode(string graph, string node) =>
            new InvalidOperationException("Invalid skill timeline edit for " + graph + " / " + node + ".");

        private static string Fingerprint(JObject graph)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(graph.ToString(Formatting.None))))
                .Replace("-", string.Empty);
        }
    }
}
