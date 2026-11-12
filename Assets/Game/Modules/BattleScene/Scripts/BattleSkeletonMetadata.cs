using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AstralOath.Client.BattleScene
{
    // Keep only animation metadata; skin/mesh keyframes never become a retained JSON DOM.
    internal sealed class BattleSkeletonMetadata
    {
        internal readonly Dictionary<string, float> Durations = new Dictionary<string, float>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Dictionary<string, float>> Events =
            new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
        internal string DefaultAnimation;

        internal static BattleSkeletonMetadata Read(string json, bool includeDurations = true)
        {
            var result = new BattleSkeletonMetadata();
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input);
            while (reader.Read())
            {
                if (reader.TokenType != JsonToken.PropertyName || reader.Depth != 1) continue;
                string section = (string)reader.Value;
                reader.Read();
                if (section != "animations") { reader.Skip(); continue; }
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    string clip = (string)reader.Value;
                    reader.Read();
                    var events = new Dictionary<string, float>(StringComparer.Ordinal);
                    float duration = 0;
                    int depth = reader.Depth;
                    while (reader.Read() && !(reader.TokenType == JsonToken.EndObject && reader.Depth == depth))
                    {
                        if (reader.TokenType != JsonToken.PropertyName) continue;
                        string property = (string)reader.Value;
                        bool direct = reader.Depth == depth + 1;
                        reader.Read();
                        if (property == "events" && direct)
                        {
                            foreach (var item in JArray.Load(reader))
                            {
                                string name = (string)item["name"];
                                float at = (float?)item["time"] ?? 0;
                                duration = Math.Max(duration, at);
                                if (name != null && (!events.TryGetValue(name, out float old) || at < old)) events[name] = at;
                            }
                        }
                        else if (!includeDurations) reader.Skip();
                        else if (property == "time" && reader.Value != null)
                            duration = Math.Max(duration, Convert.ToSingle(reader.Value, CultureInfo.InvariantCulture));
                    }
                    result.Events.Add(clip, events);
                    result.Durations.Add(clip, duration);
                    if (result.DefaultAnimation == null || clip == "animation" || (clip == "idle" && result.DefaultAnimation != "animation"))
                        result.DefaultAnimation = clip;
                }
            }
            return result;
        }

        internal float Duration(string name = null) =>
            Durations.TryGetValue(name ?? DefaultAnimation ?? "", out float value) ? value : 0;
    }
}
