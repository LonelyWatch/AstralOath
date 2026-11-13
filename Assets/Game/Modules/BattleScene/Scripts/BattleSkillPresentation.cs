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
    internal sealed partial class BattleSkillPresentation : IDisposable
    {
        private const float SourcePixel = 0.01f;
        private const string EffectPath = "effect/";
        private const string BasicHit = "hit_new_blunt_normal.cfx";
        private readonly Transform root;
        private readonly BattleCinematicStage cinematic;
        private readonly Dictionary<string, JObject> graphs;
        private Dictionary<string, JObject> documents => cache.Documents;
        private readonly Dictionary<string, BattleSkillPresentationAsset> assets;
        private Dictionary<string, SkeletonPlayerSource> skeletons => cache.Skeletons;
        private Dictionary<string, Texture2D> textures => cache.Textures;
        private readonly List<(float At, Action Run)> pending = new List<(float, Action)>();
        private readonly List<(float Until, Action<float> Tick, Action Dispose)> active =
            new List<(float, Action<float>, Action)>();
        private HashSet<string> diagnostics => cache.Diagnostics;
        private readonly SkeletonPlayerFactory skeletonFactory = new SkeletonPlayerFactory();
        private readonly System.Random cosmeticRandom = new System.Random(137);
        private BattlePreviewActor caster;
        private string currentPresentationId;
        private Vector3 home;
        private Transform casterShadow;
        private Vector3 shadowHome;
        private (Vector3 From, Vector3 GroundFrom, Vector3 To, float Start, float Duration, float Arc)? motion;
        private float time;

        // One battle owns parsed data. Successive actions share it, never their clocks or live visuals.
        private sealed class SessionCache
        {
            internal readonly Dictionary<string, JObject> Documents = new Dictionary<string, JObject>(StringComparer.Ordinal);
            internal readonly Dictionary<string, SkeletonPlayerSource> Skeletons = new Dictionary<string, SkeletonPlayerSource>(StringComparer.Ordinal);
            internal readonly Dictionary<string, BattleSkeletonMetadata> Metadata = new Dictionary<string, BattleSkeletonMetadata>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            internal readonly Dictionary<(BattlePreviewActor, string), (float At, JObject Node, float Duration, JObject[] Modulators)[]> Cues =
                new Dictionary<(BattlePreviewActor, string), (float, JObject, float, JObject[])[]>();
            internal readonly HashSet<string> Diagnostics = new HashSet<string>(StringComparer.Ordinal);
            internal readonly HashSet<string> PreparedAssets = new HashSet<string>(StringComparer.Ordinal);
        }
        private readonly SessionCache cache;
        private readonly Transform worldRoot;
        private readonly Camera camera;
        private readonly Action<bool> cinematicChanged;
        private readonly GameObject background;
        private readonly BattlePreviewActor[] participants;
        private bool cutinPaused;
        private bool incomingHandoff;
        public float StageTime => time;
        internal float CutinRemainingSeconds => cinematic.CutinRemainingSeconds;
        public IReadOnlyList<float> HitTimes { get; private set; } = Array.Empty<float>();

        public BattleSkillPresentation(Transform worldRoot, TextAsset[] graphs, BattleSkillPresentationAsset[] assets,
            Camera camera = null, Action<bool> cinematicChanged = null, GameObject background = null)
            : this(worldRoot, graphs.Where(graph => graph).Select(graph => JObject.Parse(graph.text))
                .ToDictionary(graph => (string)graph["id"], StringComparer.Ordinal),
                (assets ?? Array.Empty<BattleSkillPresentationAsset>()).ToDictionary(asset => asset.source, StringComparer.Ordinal),
                new SessionCache(), camera, cinematicChanged, background) { }

        private BattleSkillPresentation(Transform worldRoot, Dictionary<string, JObject> graphs,
            Dictionary<string, BattleSkillPresentationAsset> assets, SessionCache cache,
            Camera camera, Action<bool> cinematicChanged, GameObject background)
        {
            this.worldRoot = worldRoot; this.graphs = graphs; this.assets = assets; this.cache = cache;
            this.camera = camera; this.cinematicChanged = cinematicChanged; this.background = background;
            root = new GameObject("Battle Skill Effects").transform;
            root.SetParent(worldRoot, false);
            cinematic = new BattleCinematicStage(camera, cinematicChanged, background);
            participants = worldRoot.GetComponentsInChildren<BattlePreviewActor>(true);
            cinematic.SetParticipants(participants.Select(actor => actor.transform.parent).ToArray());
            cinematic.SetHatchTextures(Enumerable.Range(1, 3).Select(index => assets.TryGetValue("effect/bg_hatch_" + index + ".png", out var asset) ? asset.texture : null).ToArray());
            foreach (var item in assets.Values)
                if (item.texture) textures[item.source] = item.texture;
        }

        public BattleSkillPresentation CreateSuccessor()
        {
            var next = new BattleSkillPresentation(worldRoot, graphs, assets, cache, camera, cinematicChanged, background);
            cinematic.HandoffTo(next.cinematic);
            next.incomingHandoff = true;
            return next;
        }

        // Called while entering the battle, before input is enabled; no animation or scene mutation.
        public void Prepare(BattlePreviewActor actor, string presentationId)
        {
            actor.PreparePresentation();
            PrepareFallbackHit();
            if (!graphs.TryGetValue(presentationId ?? "", out var graph) || cache.Cues.ContainsKey((actor, presentationId))) return;
            if (!cache.PreparedAssets.Contains(presentationId))
            {
                foreach (var dependency in graph["assets"])
                {
                    string key = (string)dependency["source"];
                    string kind = (string)dependency["type"];
                    if (!assets.TryGetValue(key, out var asset) || !asset.document) continue;
                    if (kind == "skeleton" && (key.StartsWith("effect/", StringComparison.Ordinal) || key.StartsWith("camera/", StringComparison.Ordinal) || key.StartsWith("spani/", StringComparison.Ordinal)))
                    {
                        skeletonFactory.Prepare(Skeleton(key, asset));
                        Metadata(key);
                    }
                    else if (kind == "effect" || kind == "particle" || kind == "timeline" || kind == "cutin") Document(key);
                }
                cache.PreparedAssets.Add(presentationId);
            }
            var previous = caster;
            string previousId = currentPresentationId;
            try
            {
                caster = actor; currentPresentationId = presentationId;
                cache.Cues.Add((actor, presentationId), ReadCues(graph, graph["source"]["entities"].OfType<JObject>().ToArray()).ToArray());
            }
            finally { caster = previous; currentPresentationId = previousId; }
        }

        private void PrepareFallbackHit()
        {
            string key = EffectPath + BasicHit;
            if (cache.PreparedAssets.Contains(key) || !assets.ContainsKey(key)) return;
            if (Document(key)?["primitive"] is JArray primitives)
                foreach (var part in primitives.OfType<JObject>())
                {
                    string source = EffectPath + (string)part["source"];
                    if ((string)part["format"] == "spine" && assets.TryGetValue(source + ".scsp", out var asset))
                    {
                        skeletonFactory.Prepare(Skeleton(source + ".scsp", asset));
                        Metadata(source + ".scsp");
                    }
                    else if ((string)part["format"] == "particle") Document(source + ".particle");
                }
            cache.PreparedAssets.Add(key);
        }

        public (float ImpactSeconds, float Duration) Begin(string presentationId, BattlePreviewActor actor,
            BattlePreviewActor primaryTarget, IReadOnlyList<BattlePreviewActor> targets)
        {
            EndAction(incomingHandoff);
            incomingHandoff = false;
            caster = actor;
            actor.BeginPresentationControl();
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
                cinematic.Begin(actor.transform, recipients.Select(target => target.transform).ToArray(), false);
                cinematic.ReturnToBaseline(.2f);
                float end = Mathf.Max(duration, impact + .15f);
                Schedule(end, EndAction);
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
            Prepare(actor, presentationId);
            var cues = cache.Cues[(actor, presentationId)];
            float lead = cues.First(cue => cue.Node == main).At;
            float[] hits = cues.Where(cue => Kind(cue.Node) == "HIT").Select(cue => cue.At).Distinct().ToArray();
            float impactTime = hits.Length == 0 ? lead + Mathf.Min(.65f, clipDuration * .8f) : hits[0];
            HitTimes = hits.Length == 0 ? new[] { impactTime } : hits;
            float actionEnd = Mathf.Max(lead + clipDuration, hits.Length == 0 ? impactTime + .15f : hits[hits.Length - 1] + .15f);
            cinematic.Begin(actor.transform, recipients.Select(target => target.transform).ToArray(),
                cues.Any(cue => Kind(cue.Node) == "CAM" || Kind(cue.Node) == "CUTIN" || Kind(cue.Node) == "BGHATCH"));
            if (!cues.Any(cue => Kind(cue.Node) == "CAM" || Kind(cue.Node) == "ZOOM")) cinematic.ReturnToBaseline(.2f);
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
                        if (assets.TryGetValue(key, out var cutin)) Schedule(at, () => cinematic.PlayCutinWithNode(cutin, node));
                        else Warn("Missing original CUTIN " + key);
                        break;
                    case "CAM": case "ZOOM": case "DARK": case "STOP": case "BGSHOW": case "BGHATCH": case "LAYOUT":
                        Schedule(at, () => cinematic.Play(node));
                        break;
                    case "MOVE":
                        Schedule(at, () => PlayMove(node, primary, recipients, cue.Duration));
                        break;
                    case "NODE_ANI":
                        Schedule(at, () => PlayNodeAnimation(node, at, recipients));
                        break;
                    case "COLOR_BLEND":
                        WarnSource($"COLOR_BLEND {node["guid"]} tint layering is pending source verification.");
                        break;
                    case "SHAKE":
                        Schedule(at, () => PlayShake(node));
                        break;
                    case "PPEFFECT":
                        Schedule(at, () =>
                        {
                            if (!cinematic.PlayPostEffect(node, cue.Modulators.Select(ResolvePostModulator).ToArray()))
                                WarnSource($"PPEFFECT program '{node["program"]}' could not play at node {node["guid"]}.");
                        });
                        break;
                }
            }
            foreach (var dependency in graph["assets"].Where(item => (string)item["type"] == "timeline"))
            {
                var timeline = Document((string)dependency["source"])?["timeline"]?["default"];
                foreach (var clip in cues.Where(cue => Kind(cue.Node) == "ANI"))
                {
                    if (!(timeline?[(string)clip.Node["ani"]]?["frames"] is JArray frames)) continue;
                    foreach (var frame in frames.OfType<JObject>())
                    {
                        if (!(frame["param"] is JObject node)) continue;
                        float at = clip.At + Seconds(frame, "time");
                        switch ((string)frame["func"])
                        {
                            case "effect":
                                actionEnd = Mathf.Max(actionEnd, at + EffectDuration((string)node["source"]));
                                Schedule(at, () => Cfx((string)node["source"], actor, recipients, 1,
                                    Number(node, "scale", 1f), new Vector2(Number(node, "x"), Number(node, "y")),
                                    false, anchorName: (string)node["locationBone"] ?? (string)node["locationParam"]));
                                break;
                            case "shake":
                                actionEnd = Mathf.Max(actionEnd, at + ShakeDuration(node));
                                Schedule(at, () => PlayShake(node));
                                break;
                        }
                    }
                }
            }

            Schedule(actionEnd, EndAction);
            Tick(0f);
            return (impactTime, actionEnd);
        }

        private JObject ResolvePostModulator(JObject modulator)
        {
            if (Kind(modulator) != "RANDOM_VALUE" || (string)modulator["name"] != "u_range") return modulator;
            // Use one cosmetic sample for the cue. The source sampling cadence is unverified.
            var prepared = (JObject)modulator.DeepClone();
            prepared["value"] = Sample(Number(modulator, "lower"), Number(modulator, "upper"));
            return prepared;
        }

        private IEnumerable<(float At, JObject Node, float Duration, JObject[] Modulators)> ReadCues(JObject graph, JObject[] nodes)
        {
            var byId = nodes.ToDictionary(node => (int)node["guid"]);
            var edges = graph["source"]["connections"].OfType<JObject>().ToLookup(edge => (int)edge["from"]);
            var earliest = new Dictionary<int, (float At, int Order, float Duration)>();
            var timelineEvents = (graph["assets"] ?? new JArray()).Where(item => (string)item["type"] == "timeline")
                .Select(item => Document((string)item["source"])?["timeline"]?["default"])
                .OfType<JObject>().ToArray();
            float EventTime(string animation, string name)
            {
                float spineTime = caster.GetBattleAnimationEventTime(animation, name);
                if (spineTime >= 0f) return spineTime;
                return timelineEvents.SelectMany(timeline => timeline[animation]?["frames"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>())
                    .Where(frame => (string)frame["func"] == "event" && (string)frame["param"]?["name"] == name)
                    .Select(frame => Seconds(frame, "time")).DefaultIfEmpty(-1f).Min();
            }
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
                    float start = EventTime(animation, (string)node["from"]);
                    float end = EventTime(animation, (string)node["to"]);
                    if (start >= 0f && end < 0f)
                    {
                        end = caster.GetBattleAnimationDuration(animation);
                        WarnSource($"Missing {animation}/{node["to"]}; the scope ends with the original clip.");
                    }
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
                    // Style 2 travel and camera commands without their own time occupy the
                    // named scope window. An explicit camera time (including zero) retains the
                    // declared completion edge; start edges and delays retain their source meaning.
                    string nextKind = Kind(next);
                    bool scopeTravel = nextKind == "MOVE" && (int?)next["style"] == 2;
                    bool scopeCamera = (nextKind == "CAM" || nextKind == "ZOOM") && next["time"] == null;
                    if (when == 0 && jumpStart.HasValue && (scopeTravel || scopeCamera))
                    {
                        nextAt = jumpStart.Value;
                        nextDuration = Seconds(next, "time", finish - nextAt);
                    }
                    Visit(next, nextAt + Seconds(edge, "delay"), path, animation, animationStart, nextDuration);
                }
                if (Kind(node) == "ANI")
                    foreach (var listener in nodes.Where(item => Kind(item) == "ONEVENT" && (string)item["ani"] == animation))
                    {
                        float eventTime = EventTime(animation, (string)listener["name"]);
                        if (eventTime >= 0) Visit(listener, at + eventTime, path, animation, at);
                        else WarnSource($"Missing source event {animation}/{listener["name"]}.");
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
                string kind = Kind(node);
                if ((kind == "CAM" || kind == "ZOOM") && node["time"] == null && cue.Value.Duration > 0f)
                {
                    // Cinematic playback consumes node time. Materialize inherited timing only
                    // in the prepared cue, leaving the shared source graph immutable.
                    node = (JObject)node.DeepClone();
                    node["time"] = cue.Value.Duration * 1000f;
                }
                var modulators = kind == "PPEFFECT" ? PostModulators(cue.Key).ToArray() : Array.Empty<JObject>();
                return (cue.Value.At, node, cue.Value.Duration, modulators);
            });
        }

        private float Completion(JObject node)
        {
            switch (Kind(node))
            {
                case "ANI": return caster.GetBattleAnimationDuration((string)node["ani"]);
                case "CUTIN": return 0f; // Wall-clock suspension; source successors remain at the same stage time.
                case "DARK": return Number(node, "duration", -1f) < 0f ? 0f : Seconds(node, "duration") + Seconds(node, "fadeOut");
                case "EFFECT": return EffectDuration((string)node["source"]);
                case "SHAKE": return ShakeDuration(node);
                case "NODE_ANI": return NodeAnimationDuration(node);
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
            string source = EffectPath + (string)part["source"];
            if ((string)part["format"] == "spine")
            {
                string key = source + ".scsp";
                var skeleton = Metadata(key);
                string animation = InitialSpineAnimation(part, key, skeleton);
                if (animation == null) return 0f;
                if (lifetime >= 0f) return lifetime / 1000f;
                float duration = skeleton.Duration(animation);
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                    if (HasSpineAction(key, skeleton, (string)action["action"]))
                        duration = Mathf.Max(duration, Seconds(action, "delay") + skeleton.Duration((string)action["action"]));
                return duration;
            }
            if (lifetime >= 0f) return lifetime / 1000f;
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

        // CFX ani selects the initial clip; actions remain separately delayed transitions.
        // Use the same choice for graph completion and native playback so the visual is not retired early.
        private string InitialSpineAnimation(JObject part, string key, BattleSkeletonMetadata skeleton)
        {
            string requested = (string)part["ani"];
            if (requested != null)
            {
                if (skeleton.Durations.ContainsKey(requested)) return requested;
                WarnSource($"Unknown CFX animation '{requested}' in '{key}'.");
                return null;
            }
            if (skeleton.Duration("animation") > 0f) return "animation";
            if (skeleton.Duration("idle") > 0f) return "idle";
            foreach (var clip in skeleton.Durations)
                if (clip.Value > 0f) return clip.Key;
            WarnSource($"No playable CFX animation in '{key}'.");
            return null;
        }

        private bool HasSpineAction(string key, BattleSkeletonMetadata skeleton, string name)
        {
            if (name != null && skeleton.Durations.ContainsKey(name)) return true;
            WarnSource($"Unmapped CFX action '{name}' in '{key}'.");
            return false;
        }

        private static float UpperNumber(JObject node, string name, float fallback = 0f)
        {
            string text = node[name]?.ToString();
            if (TryScalar(text, out float value)) return value;
            var range = text?.Split('~');
            return range?.Length == 2 && TryScalar(range[0], out float a) && TryScalar(range[1], out float b)
                ? Mathf.Max(a, b) : fallback;
        }

        private BattleSkeletonMetadata Metadata(string key)
        {
            if (cache.Metadata.TryGetValue(key, out var value)) return value;
            value = assets.TryGetValue(key, out var asset) && asset.document
                ? BattleSkeletonMetadata.Read(asset.document.text) : new BattleSkeletonMetadata();
            cache.Metadata.Add(key, value);
            return value;
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
            return key != null && speed > 0f ? Metadata(key).Duration() / speed : 0f;
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
            float remaining = Mathf.Max(0f, delta);
            while (true)
            {
                float cutin = cinematic.CutinRemainingSeconds;
                SetCutinPaused(cutin > 0f);
                if (cutin > 0f)
                {
                    if (remaining <= 0f) break;
                    float consumed = Mathf.Min(remaining, cutin);
                    cinematic.TickCutin(consumed);
                    remaining -= consumed;
                    SetCutinPaused(cinematic.CutinRemainingSeconds > 0f);
                    if (cutinPaused) break;
                    continue;
                }
                if (pending.Count > 0 && pending[0].At <= time)
                {
                    var task = pending[0];
                    pending.RemoveAt(0);
                    task.Run();
                    continue;
                }
                if (remaining <= 0f) break;
                float step = pending.Count > 0 ? Mathf.Min(remaining, Mathf.Max(0f, pending[0].At - time)) : remaining;
                Advance(step);
                remaining -= step;
            }
        }

        private void SetCutinPaused(bool value)
        {
            if (cutinPaused == value) return;
            cutinPaused = value;
            foreach (var actor in participants) if (actor) actor.SetPresentationPaused(value);
        }

        private void Advance(float delta)
        {
            time += delta;
            if (caster) caster.AdvancePresentation(delta);
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

        private void PlayMove(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets, float duration)
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
            else if (location == 31 && primary)
                destination = caster.transform.parent.InverseTransformPoint((caster.transform.position + primary.transform.position) * .5f);
            else if ((location == 41 || location == 42) && targets.Length > 0)
                destination = caster.transform.parent.InverseTransformPoint(TargetCenter(targets));
            else if (location != 11)
            {
                WarnSource("Unsupported or unoccupied MOVE location " + location);
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

        private static Vector3 TargetCenter(BattlePreviewActor[] targets) =>
            targets.Aggregate(Vector3.zero, (sum, target) => sum + target.transform.position) / targets.Length;

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

            if (location == 41 || location == 42)
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
                5 or 12 => new[] { primary },
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
            var metadata = Metadata(key);
            string animation = InitialSpineAnimation(part, key, metadata);
            if (animation == null) throw new FormatException($"Unplayable CFX animation '{(string)part["ani"] ?? "(default)"}' in '{key}'.");
            var player = skeletonFactory.Create(Skeleton(key, assets[key]), group);
            try
            {
                player.Root.name = source;
                player.Root.transform.localScale = new Vector3(side * size, size, size);
                player.Root.GetComponent<MeshRenderer>().sortingOrder = order;
                player.Play(animation, false);
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string name = (string)action["action"];
                    if (!HasSpineAction(key, metadata, name)) continue;
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

        private void EndAction() => EndAction(false);

        private void EndAction(bool preserveHandoff)
        {
            SetCutinPaused(false);
            if (!preserveHandoff) cinematic.Reset();
            if (caster)
            {
                caster.transform.localPosition = home;
                caster.PlayBattleIdle();
                caster.EndPresentationControl();
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
