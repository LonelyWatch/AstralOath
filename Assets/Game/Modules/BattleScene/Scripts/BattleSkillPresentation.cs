using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AstralOath.Client.BattleScene
{
    [Serializable]
    public sealed class BattleSkillPresentationAsset
    {
        public string source;
        public TextAsset document;
        public TextAsset atlas;
        public string[] pageNames;
        public Texture2D[] pages;
        public Texture2D texture;
        public bool premultipliedAlpha;
        public float scale;
    }

    // Read-only playback of already committed actions. The transform is the visual child of a slot.
    internal sealed class BattleSkillPresentation : IDisposable
    {
        private const float SourcePixel = 0.01f;
        private const string EffectPath = "effect/";
        private const string BasicHit = "hit_new_blunt_normal.cfx";
        private readonly Transform root;
        private readonly BattleCinematicStage cinematic;
        private readonly Dictionary<string, JObject> graphs;
        private readonly Dictionary<string, JObject> documents = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, BattleSkillPresentationAsset> assets;
        private readonly Dictionary<string, SkeletonPlayerSource> skeletons = new Dictionary<string, SkeletonPlayerSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly List<(float At, Action Run)> pending = new List<(float, Action)>();
        private readonly List<(float Until, Action<float> Tick, Action Dispose)> active =
            new List<(float, Action<float>, Action)>();
        private readonly HashSet<string> diagnostics = new HashSet<string>(StringComparer.Ordinal);
        private readonly SkeletonPlayerFactory skeletonFactory = new SkeletonPlayerFactory();
        private readonly System.Random cosmeticRandom = new System.Random(137);
        private BattlePreviewActor caster;
        private string currentPresentationId;
        private Vector3 home;
        private Transform casterShadow;
        private Vector3 shadowHome;
        private (Vector3 From, Vector3 GroundFrom, Vector3 To, float Start, float Duration, float Arc)? motion;
        private float time;

        public IReadOnlyList<float> HitTimes { get; private set; } = Array.Empty<float>();

        public BattleSkillPresentation(Transform worldRoot, TextAsset[] graphs, BattleSkillPresentationAsset[] assets,
            Camera camera = null, Action<bool> cinematicChanged = null, GameObject background = null)
        {
            root = new GameObject("Battle Skill Effects").transform;
            root.SetParent(worldRoot, false);
            this.graphs = graphs.Where(graph => graph).Select(graph => JObject.Parse(graph.text))
                .ToDictionary(graph => (string)graph["id"], StringComparer.Ordinal);
            this.assets = (assets ?? Array.Empty<BattleSkillPresentationAsset>()).ToDictionary(asset => asset.source,
                StringComparer.Ordinal);
            cinematic = new BattleCinematicStage(camera, cinematicChanged, background);
            cinematic.SetParticipants(worldRoot.GetComponentsInChildren<BattlePreviewActor>(true).Select(actor => actor.transform.parent).ToArray());
            cinematic.SetHatchTextures(Enumerable.Range(1, 3).Select(index => this.assets.TryGetValue("effect/bg_hatch_" + index + ".png", out var asset) ? asset.texture : null).ToArray());
            foreach (var item in this.assets.Values)
                if (item.texture) textures.Add(item.source, item.texture);
        }

        public (float ImpactSeconds, float Duration) Begin(string presentationId, BattlePreviewActor actor,
            BattlePreviewActor primaryTarget, IReadOnlyList<BattlePreviewActor> targets)
        {
            EndAction();
            caster = actor;
            currentPresentationId = string.IsNullOrEmpty(presentationId) ? actor.name : presentationId;
            home = actor.transform.localPosition;
            casterShadow = actor.PresentationShadow;
            shadowHome = casterShadow ? casterShadow.localPosition : Vector3.zero;
            time = 0f;
            var recipients = targets.Where(target => target).Distinct().ToArray();
            if (recipients.Length == 0 && primaryTarget)
                recipients = new[] { primaryTarget };
            var primary = primaryTarget ? primaryTarget : recipients.FirstOrDefault();

            if (!graphs.TryGetValue(presentationId ?? "", out var graph))
            {
                float duration = actor.PlayBattleAnimation("skill1");
                float sourceHit = actor.GetBattleAnimationEventTime("skill1", "hit");
                float impact = sourceHit >= 0f ? sourceHit : Mathf.Clamp(duration * 0.5f, 0.12f, 0.45f);
                HitTimes = new[] { impact };
                if (primary) Schedule(impact, () => Cfx(BasicHit, primary, recipients, 5, 1f, Vector2.zero, true));
                return (impact, Mathf.Max(duration, impact + 0.15f));
            }

            var nodes = ((JObject)graph["source"])["entities"].OfType<JObject>().ToArray();
            if (nodes.Any(node => Kind(node) == "LAYOUT" && (int?)node["mode"] != 1 && Number(node, "away_x") < 1280f))
                Warn(presentationId + ": source LAYOUT uses a participant visibility mask; self_x/away_x placement and fade curves remain unverified and authored slots are retained.");
            if (nodes.Any(node => Kind(node) == "MOVE"))
                Warn(presentationId + ": source MOVE timing and anchors are used with reconstructed movement curves.");
            var main = nodes.First(node => Kind(node) == "ANI" &&
                ((string)node["ani"]).StartsWith("skill", StringComparison.Ordinal));
            string animation = (string)main["ani"];
            float clipDuration = Mathf.Max(0.1f, actor.GetBattleAnimationDuration(animation));
            var cues = ReadCues(graph, nodes).ToArray();
            float lead = cues.First(cue => cue.Node == main).At;
            float[] hits = cues.Where(cue => Kind(cue.Node) == "HIT").Select(cue => cue.At).Distinct().ToArray();
            float impactTime = hits.Length == 0 ? lead + Mathf.Min(.65f, clipDuration * .8f) : hits[0];
            HitTimes = hits.Length == 0 ? new[] { impactTime } : hits;
            float actionEnd = Mathf.Max(lead + clipDuration, hits.Length == 0 ? impactTime + .15f : hits[hits.Length - 1] + .15f);
            cinematic.Begin(actor.transform, recipients.Select(target => target.transform).ToArray(),
                cues.Any(cue => Kind(cue.Node) == "CAM" || Kind(cue.Node) == "CUTIN" || Kind(cue.Node) == "BGHATCH"));
            foreach (var cue in cues)
            {
                var node = cue.Node;
                float at = cue.At;
                actionEnd = Mathf.Max(actionEnd, at + cue.Duration);
                switch (Kind(node))
                {
                    case "ANI":
                        Schedule(at, () => actor.PlayBattleAnimation((string)node["ani"]));
                        break;
                    case "IDLE":
                        Schedule(at, actor.PlayBattleIdle);
                        break;
                    case "HIT":
                        string hit = (string)node["effect"];
                        if (!string.IsNullOrEmpty(hit))
                            Schedule(at, () => Cfx(hit, primary, recipients, 6, 1f,
                                new Vector2(Number(node, "offset_x"), Number(node, "offset_y")), true,
                                new Vector2(Number(node, "rand_x"), Number(node, "rand_y"))));
                        break;
                    case "EFFECT":
                        Schedule(at, () => Effect(node, primary, recipients));
                        break;
                    case "CUTIN":
                        string key = "cut/fhd/" + (string)node["args"] + ".webp";
                        if (assets.TryGetValue(key, out var cutin)) Schedule(at, () => cinematic.PlayCutin(cutin));
                        else Warn("Missing original CUTIN " + key);
                        break;
                    case "CAM": case "ZOOM": case "DARK": case "STOP": case "BGSHOW": case "BGHATCH": case "LAYOUT":
                        Schedule(at, () => cinematic.Play(node));
                        break;
                    case "MOVE":
                        Schedule(at, () => PlayMove(node, primary, cue.Duration));
                        break;
                    case "SHAKE":
                        Schedule(at, () => PlayShake(node));
                        break;
                    case "PPEFFECT":
                        Schedule(at, () =>
                        {
                            if (!cinematic.PlayPostEffect(node, cue.Modulators))
                                WarnSource($"PPEFFECT program '{node["program"]}' could not play at node {node["guid"]}.");
                        });
                        break;
                }
            }
            foreach (var dependency in graph["assets"].Where(item => (string)item["type"] == "timeline"))
            {
                var timeline = Document((string)dependency["source"]);
                if (!(timeline?["timeline"]?["default"]?[animation]?["frames"] is JArray frames)) continue;
                foreach (var frame in frames.OfType<JObject>())
                {
                    if (!(frame["param"] is JObject node)) continue;
                    float at = lead + Seconds(frame, "time");
                    switch ((string)frame["func"])
                    {
                        case "effect":
                            actionEnd = Mathf.Max(actionEnd, at + EffectDuration((string)node["source"]));
                            Schedule(at, () => Effect(node, primary, recipients));
                            break;
                        case "shake":
                            actionEnd = Mathf.Max(actionEnd, at + ShakeDuration(node));
                            Schedule(at, () => PlayShake(node));
                            break;
                    }
                }
            }

            Schedule(actionEnd, EndAction);
            Tick(0f);
            return (impactTime, actionEnd);
        }

        private IEnumerable<(float At, JObject Node, float Duration, JObject[] Modulators)> ReadCues(JObject graph, JObject[] nodes)
        {
            var byId = nodes.ToDictionary(node => (int)node["guid"]);
            var edges = graph["source"]["connections"].OfType<JObject>().ToLookup(edge => (int)edge["from"]);
            var earliest = new Dictionary<int, (float At, int Order, float Duration)>();
            int ordinal = 0;
            void Visit(JObject node, float at, HashSet<int> path, string animation = null,
                float animationStart = 0f, float? durationOverride = null)
            {
                int id = (int)node["guid"];
                if (path.Contains(id) || (earliest.TryGetValue(id, out var old) && old.At <= at)) return;
                float duration = durationOverride ?? Completion(node);
                float finish = at + duration;
                float? jumpStart = null;
                if (Kind(node) == "ANI") { animation = (string)node["ani"]; animationStart = at; }
                if (Kind(node) == "ANI_SCOPE")
                {
                    float start = caster.GetBattleAnimationEventTime(animation, (string)node["from"]);
                    float end = caster.GetBattleAnimationEventTime(animation, (string)node["to"]);
                    if (end >= 0f) finish = Mathf.Max(at, animationStart + end);
                    else Warn("Missing source ANI_SCOPE end event " + animation + "/" + node["to"]);
                    if (start >= 0f && end >= start) jumpStart = Mathf.Clamp(animationStart + start, at, finish);
                }
                earliest[id] = (at, ordinal++, finish - at);
                path.Add(id);
                foreach (var edge in edges[id])
                {
                    int when = (int?)edge["when"] ?? 0;
                    if (when == 2 && Kind(node) == "PPEFFECT") continue;
                    if (when != 0 && when != 1)
                    {
                        WarnSource($"Unsupported STG connection when={when} ({id} -> {edge["to"]}).");
                        continue;
                    }
                    var next = byId[(int)edge["to"]];
                    float nextAt = when == 0 ? finish : at;
                    float? nextDuration = null;
                    // Style 2 travels inside the named jump/landing window. Other successors
                    // retain their start or completion connection to the scope.
                    if (when == 0 && jumpStart.HasValue && Kind(next) == "MOVE" && (int?)next["style"] == 2)
                    {
                        nextAt = jumpStart.Value;
                        nextDuration = Seconds(next, "time", finish - nextAt);
                    }
                    Visit(next, nextAt + Seconds(edge, "delay"), path, animation, animationStart, nextDuration);
                }
                if (Kind(node) == "ANI")
                    foreach (var listener in nodes.Where(item => Kind(item) == "ONEVENT" && (string)item["ani"] == animation))
                    {
                        float eventTime = caster.GetBattleAnimationEventTime(animation, (string)listener["name"]);
                        if (eventTime >= 0) Visit(listener, at + eventTime, path, animation, at);
                    }
                path.Remove(id);
            }
            Visit(byId[(int)graph["source"]["entrypoint"]["start"]], 0f, new HashSet<int>());
            IEnumerable<JObject> PostModulators(int parent)
            {
                foreach (var edge in edges[parent].Where(item => (int?)item["when"] == 2))
                {
                    if (Seconds(edge, "delay") > 0f)
                    {
                        WarnSource("Unsupported delayed source PPEFFECT modulation at " + parent);
                        continue;
                    }
                    yield return byId[(int)edge["to"]];
                }
            }

            return earliest.OrderBy(cue => cue.Value.At).ThenBy(cue => cue.Value.Order).Select(cue =>
            {
                var node = byId[cue.Key];
                var modulators = Kind(node) == "PPEFFECT" ? PostModulators(cue.Key).ToArray() : Array.Empty<JObject>();
                return (cue.Value.At, node, cue.Value.Duration, modulators);
            });
        }

        private float Completion(JObject node)
        {
            switch (Kind(node))
            {
                case "ANI": return caster.GetBattleAnimationDuration((string)node["ani"]);
                case "CUTIN": return BattleCinematicStage.CutinDuration(Document("cut/fhd/" + (string)node["args"] + ".webp"));
                case "EFFECT": return EffectDuration((string)node["source"]);
                case "SHAKE": return ShakeDuration(node);
                case "PPEFFECT": return Seconds(node, "duration");
                default: return Seconds(node, "time");
            }
        }

        private float EffectDuration(string source)
        {
            if (string.IsNullOrEmpty(source)) return 0f;
            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            return Document(key)?["primitive"]?.OfType<JObject>()
                .Select(part => Mathf.Max(0f, UpperNumber(part, "delay")) / 1000f + PrimitiveDuration(part))
                .DefaultIfEmpty(0f).Max() ?? 0f;
        }

        private float PrimitiveDuration(JObject part)
        {
            float lifetime = UpperNumber(part, "lifeTime", -1f);
            if (lifetime >= 0f) return lifetime / 1000f;
            string source = EffectPath + (string)part["source"];
            if ((string)part["format"] == "spine")
            {
                var skeleton = Document(source + ".scsp");
                float duration = SkeletonAnimationDuration(skeleton);
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                    duration = Mathf.Max(duration, Seconds(action, "delay") +
                        SkeletonAnimationDuration(skeleton, (string)action["action"]));
                return duration;
            }
            if ((string)part["format"] != "particle") return 0f;

            var emitters = Document(source + ".particle")?["emitters"] as JArray;
            if (emitters == null) return 1.8f;
            float end = 0f;
            foreach (var emitter in emitters.OfType<JObject>().Where(item => (bool?)item["disable"] != true))
            {
                bool burst = (int?)emitter["burningType"] == 1;
                float duration = Number(emitter, "duration");
                if (!burst && duration < 0f)
                {
                    end = Mathf.Max(end, 1.8f);
                    continue;
                }
                float start = Mathf.Max(0f, Number(emitter, "startDelay") + Mathf.Abs(Number(emitter, "startDelayVar")));
                float repetitions = Mathf.Max(0f, Number(emitter, "repeat") - 1f) * Number(emitter, "interval");
                float life = Mathf.Max(.01f, Number(emitter, "particleLifespan") + Mathf.Abs(Number(emitter, "particleLifespanVariance")));
                end = Mathf.Max(end, start + repetitions + (burst ? 0f : duration) + life);
            }
            return end;
        }

        private static float UpperNumber(JObject node, string name, float fallback = 0f)
        {
            string text = node[name]?.ToString();
            if (TryScalar(text, out float value)) return value;
            var range = text?.Split('~');
            return range?.Length == 2 && TryScalar(range[0], out float a) && TryScalar(range[1], out float b)
                ? Mathf.Max(a, b) : fallback;
        }

        private static float SkeletonAnimationDuration(JObject skeleton, string name = null)
        {
            var animations = skeleton?["animations"] as JObject;
            var animation = name == null
                ? animations?["animation"] ?? animations?["idle"] ?? animations?.Properties().FirstOrDefault()?.Value
                : animations?[name];
            return animation?.SelectTokens("$..time").Select(item => (float?)item ?? 0f).DefaultIfEmpty(0f).Max() ?? 0f;
        }

        private static string ShakeSource(JObject node)
        {
            string source = (string)node["source"];
            if (string.IsNullOrEmpty(source)) return null;
            if (!source.StartsWith("camera/", StringComparison.Ordinal)) source = "camera/" + source;
            return source.EndsWith(".scsp", StringComparison.Ordinal) ? source : source + ".scsp";
        }

        private float ShakeDuration(JObject node)
        {
            float duration = Number(node, "duration", -1f);
            if (duration >= 0f) return duration / 1000f;
            string key = ShakeSource(node);
            float speed = Number(node, "timeScale", 1f);
            return key != null && speed > 0f ? SkeletonAnimationDuration(Document(key)) / speed : 0f;
        }

        private void PlayShake(JObject node)
        {
            string key = ShakeSource(node);
            if (key == null || !assets.TryGetValue(key, out var asset) || !asset.document)
            {
                Warn("Missing source camera skeleton " + (key ?? "(unspecified)"));
                return;
            }
            cinematic.PlayShake(node, Skeleton(key, asset));
        }
        public void Tick(float delta)
        {
            float frameEnd = time + Mathf.Max(0f, delta);
            while (pending.Count > 0 && pending[0].At <= frameEnd)
            {
                Advance(Mathf.Max(0f, pending[0].At - time));
                var task = pending[0];
                pending.RemoveAt(0);
                task.Run();
            }
            Advance(Mathf.Max(0f, frameEnd - time));
        }

        private void Advance(float delta)
        {
            time += delta;
            if (motion is { } movement && caster)
            {
                float progress = Mathf.Clamp01((time - movement.Start) / movement.Duration);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                caster.transform.localPosition = Vector3.Lerp(movement.From, movement.To, eased) +
                    Vector3.up * (Mathf.Sin(progress * Mathf.PI) * movement.Arc);
                if (casterShadow)
                    casterShadow.localPosition = shadowHome + Vector3.Lerp(movement.GroundFrom, movement.To, eased) - home;
                if (progress >= 1f)
                {
                    caster.transform.localPosition = movement.To;
                    motion = null;
                }
            }
            cinematic.Tick(delta);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var visual = active[i];
                if (time >= visual.Until)
                {
                    visual.Dispose();
                    active.RemoveAt(i);
                }
                else visual.Tick(delta);
            }

        }

        private void Schedule(float at, Action run)
        {
            int index = pending.FindIndex(item => item.At > at);
            if (index < 0) pending.Add((at, run));
            else pending.Insert(index, (at, run));
        }

        private void PlayMove(JObject node, BattlePreviewActor primary, float duration)
        {
            if (!caster) return;
            int style = (int?)node["style"] ?? 1;
            int location = (int?)node["location"] ?? 11;
            Vector3 destination = home;
            if (location == 16)
            {
                if (!primary || primary == caster) return;
                Vector3 target = primary.transform.position;
                float separation = Mathf.Clamp(target.x - caster.transform.position.x, -.9f, .9f);
                destination = caster.transform.parent.InverseTransformPoint(target - Vector3.right * separation);
            }
            else if (location != 11)
            {
                Warn("Unsupported source MOVE location " + location);
                return;
            }
            if (style < 1 || style > 3)
            {
                Warn("Unsupported source MOVE style " + style);
                return;
            }
            if (style != 3)
                destination += new Vector3(Number(node, "x") * caster.FacingDirection, Number(node, "y"), 0f) * SourcePixel;
            float arc = style == 3 ? 0f : Number(node, "h", style == 2 ? Number(node, "power") : 0f) * SourcePixel;
            if (duration > 0f)
            {
                Vector3 from = caster.transform.localPosition;
                Vector3 groundFrom = casterShadow ? home + casterShadow.localPosition - shadowHome : from;
                motion = (from, groundFrom, destination, time, duration, arc);
            }
            else
            {
                motion = null;
                caster.transform.localPosition = destination;
                if (casterShadow) casterShadow.localPosition = shadowHome + destination - home;
            }
        }

        private void Effect(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets)
        {
            string source = (string)node["source"];
            if (string.IsNullOrEmpty(source)) return;
            Cfx(source, primary, targets, (int?)node["location"] ?? 5, Number(node, "scale", 1f),
                new Vector2(Number(node, "x"), Number(node, "y")), false, anchorName: (string)node["locationParam"]);
        }

        private void Cfx(string source, BattlePreviewActor primary, BattlePreviewActor[] targets, int location,
            float scale, Vector2 offset, bool hit, Vector2 spread = default, string anchorName = null)
        {
            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            if (!(Document(key)?["primitive"] is JArray primitives)) return;

            if (location == 42)
            {
                var occupied = targets.Where(target => target).ToArray();
                if (occupied.Length == 0) return;
                Vector3 center = occupied.Aggregate(Vector3.zero, (sum, item) => sum + Position(item)) / occupied.Length;
                SpawnAt(() => center, occupied[0], false);
                return;
            }
            IEnumerable<BattlePreviewActor> anchors = location switch
            {
                1 => new[] { caster },
                11 => new[] { caster },
                5 => new[] { primary },
                6 => targets,
                _ => null
            };
            if (anchors == null)
            {
                WarnSource($"Unsupported CFX location {location} for '{key}'.");
                return;
            }
            foreach (var anchor in anchors.Where(item => item))
            {
                bool follow = location == 1 || location == 11;
                Vector3 initial = Position(anchor);
                SpawnAt(() => follow && anchor ? Position(anchor) : initial, anchor, follow);
            }

            Vector3 Position(BattlePreviewActor anchor)
            {
                if (!anchor.TryGetPresentationAnchor(anchorName, out var position))
                    WarnSource($"CFX '{key}' locationParam '{anchorName}' is unavailable on '{anchor.name}'. Using the visual actor origin.");
                if (hit) position.y = anchor.Bounds.center.y;
                return position;
            }

            void SpawnAt(Func<Vector3> position, BattlePreviewActor anchor, bool follow)
            {
                Vector2 variation = new Vector2(Sample(-spread.x, spread.x), Sample(-spread.y, spread.y));
                foreach (var raw in primitives.OfType<JObject>())
                {
                    var part = ReadPrimitive(raw);
                    if (part == null) continue;
                    float side = caster.FacingDirection;
                    Schedule(time + Seconds(part, "delay"),
                        () => Spawn(key, part, position, anchor, scale, offset + variation, side, follow));
                }
            }
        }

        private JObject ReadPrimitive(JObject raw)
        {
            var part = (JObject)raw.DeepClone();
            foreach (string field in new[] { "delay", "x", "y", "globalZ", "z", "lifeTime", "scale", "rotate" })
            {
                if (part[field] == null) continue;
                string text = part[field].ToString();
                if (TryScalar(text, out float value))
                {
                    part[field] = value;
                    continue;
                }

                string[] range = text.Split('~');
                if (range.Length == 2 && TryScalar(range[0], out float lower) && TryScalar(range[1], out float upper))
                {
                    part[field] = Sample(lower, upper);
                    continue;
                }

                string[] axes = text.Split(',');
                if (field == "scale" && axes.Length == 2 &&
                    TryScalar(axes[0], out float width) && TryScalar(axes[1], out float height) &&
                    width > 0f && height > 0f)
                {
                    part["scale"] = 1f;
                    part["scaleX"] = width;
                    part["scaleY"] = height;
                    continue;
                }

                Warn($"{part["source"]} uses unsupported CFX {field}={part[field]}");
                return null;
            }
            return part;
        }

        // Variation affects presentation only and never consumes the battle RNG.
        private float Sample(float lower, float upper) => Mathf.Lerp(lower, upper, (float)cosmeticRandom.NextDouble());

        private static bool TryScalar(string text, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);

        private void Spawn(string key, JObject part, Func<Vector3> position, BattlePreviewActor anchor, float scale,
            Vector2 offset, float side, bool follow)
        {
            string source = (string)part["source"];
            if (string.IsNullOrWhiteSpace(source)) return;
            string format = (string)part["format"];
            float size = Mathf.Max(0.01f, Number(part, "scale", 1f) * scale *
                (anchor ? Mathf.Abs(anchor.transform.lossyScale.x) : 1f));
            var renderer = anchor ? anchor.GetComponentInChildren<MeshRenderer>() : null;
            int order = (renderer ? renderer.sortingOrder : 55) + (int)Mathf.Sign(Number(part, "globalZ", 1f)) * 2;
            var group = new GameObject("Skill CFX " + key.Substring(EffectPath.Length)).transform;
            group.SetParent(root, false);
            Vector3 translation = new Vector3((offset.x + Number(part, "x")) * side,
                offset.y + Number(part, "y"), 0f) * SourcePixel;
            group.position = position() + translation;
            group.localRotation = Quaternion.Euler(0f, 0f, -Number(part, "rotate") * side);
            group.localScale = new Vector3(Number(part, "scaleX", 1f), Number(part, "scaleY", 1f), 1f);
            try
            {
                switch (format)
                {
                    case "spine":
                        SpawnSpine(source, part, group, size, order, side, UpdatePosition);
                        break;
                    case "particle":
                        SpawnParticles(source, part, group, size, order, UpdatePosition);
                        break;
                    default:
                        throw new FormatException("Unsupported primitive format " + format);
                }
            }
            catch (Exception error) when (error is FormatException || error is InvalidOperationException || error is KeyNotFoundException)
            {
                if (group) Object.Destroy(group.gameObject);
                Warn(source + ": " + error.Message);
            }

            void UpdatePosition()
            {
                if (follow) group.position = position() + translation;
            }
        }

        private void SpawnSpine(string source, JObject part, Transform group, float size, int order, float side, Action follow)
        {
            string key = EffectPath + source + ".scsp";
            var player = skeletonFactory.Create(Skeleton(key, assets[key]), group);
            try
            {
                player.Root.name = source;
                player.Root.transform.localScale = new Vector3(side * size, size, size);
                player.Root.GetComponent<MeshRenderer>().sortingOrder = order;
                string animation = player.Animations.FirstOrDefault(clip => clip.Duration > 0f &&
                    (clip.Name == "animation" || clip.Name == "idle"))?.Name ??
                    player.Animations.FirstOrDefault(clip => clip.Duration > 0f)?.Name;
                if (animation == null) throw new FormatException("No playable CFX animation");
                player.Play(animation, false);
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string name = (string)action["action"];
                    if (!player.Animations.Any(clip => clip.Name == name && clip.Duration > 0f))
                    {
                        Warn(source + " has an unmapped CFX action " + name);
                        continue;
                    }
                    Schedule(time + Seconds(action, "delay"), () =>
                    {
                        if (player.Root) player.Play(name, false);
                    });
                }
                float lifetime = PrimitiveDuration(part);
                active.Add((time + lifetime, delta =>
                {
                    follow();
                    player.Tick(delta);
                }, () =>
                {
                    player.Dispose();
                    if (group) Object.Destroy(group.gameObject);
                }));
            }
            catch
            {
                player.Dispose();
                throw;
            }
        }

        private void SpawnParticles(string source, JObject part, Transform group, float size, int order, Action follow)
        {
            string key = EffectPath + source + ".particle";
            var particles = new BattleHudParticles(assets[key].document, textures, group, Vector2.zero, size * SourcePixel, order);
            particles.Play();
            active.Add((time + PrimitiveDuration(part), delta =>
            {
                follow();
                particles.Tick(delta);
            }, () =>
            {
                particles.Dispose();
                if (group) Object.Destroy(group.gameObject);
            }));
        }

        private SkeletonPlayerSource Skeleton(string key, BattleSkillPresentationAsset asset)
        {
            if (skeletons.TryGetValue(key, out var source)) return source;
            var names = asset.pageNames ?? Array.Empty<string>();
            var textures = asset.pages ?? Array.Empty<Texture2D>();
            var pages = names.Zip(textures, (name, page) => (name, page))
                .ToDictionary(pair => pair.name, pair => pair.page, StringComparer.Ordinal);
            source = new SkeletonPlayerSource(asset.document.text, asset.atlas ? asset.atlas.text : string.Empty,
                pages, asset.premultipliedAlpha, asset.scale);
            skeletons.Add(key, source);
            return source;
        }

        private JObject Document(string source)
        {
            if (documents.TryGetValue(source, out var value)) return value;
            if (!assets.TryGetValue(source, out var asset) || !asset.document)
            {
                Warn("Missing skill presentation asset " + source);
                return null;
            }
            value = JObject.Parse(asset.document.text);
            documents.Add(source, value);
            return value;
        }

        private void WarnSource(string message) => Warn(currentPresentationId + ": " + message);

        private void Warn(string message)
        {
            if (diagnostics.Add(message)) Debug.LogWarning(message);
        }

        private void EndAction()
        {
            cinematic.Reset();
            if (caster)
            {
                caster.transform.localPosition = home;
                caster.PlayBattleIdle();
            }
            if (casterShadow) casterShadow.localPosition = shadowHome;
            casterShadow = null;
            caster = null;
            motion = null;
            pending.Clear();
            foreach (var visual in active) visual.Dispose();
            active.Clear();
        }

        public void Dispose()
        {
            EndAction();
            if (root)
            {
                if (Application.isPlaying) Object.Destroy(root.gameObject);
                else Object.DestroyImmediate(root.gameObject);
            }
        }

        private static string Kind(JObject node) => (string)node["etty"];
        private static float Number(JObject node, string key, float fallback = 0f) =>
            float.TryParse(node[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                float.IsFinite(value) ? value : fallback;
        private static float Seconds(JObject node, string key, float fallback = 0f) =>
            Mathf.Max(0f, Number(node, key, fallback * 1000f) / 1000f);
    }
}
