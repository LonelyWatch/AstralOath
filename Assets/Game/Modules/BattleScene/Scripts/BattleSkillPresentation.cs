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
        public Shader blackCurtainShader;
        public bool premultipliedAlpha;
        public float scale;
    }

    // Read-only playback of already committed actions. The transform is the visual child of a slot.
    internal sealed partial class BattleSkillPresentation : IDisposable
    {
        private const float SourcePixel = 0.01f;
        // The native graphs reserve far instant home-relative placements of 4000+
        // source pixels for an offstage hold. Ordinary staged entrances are smaller.
        private const float OffstageParkingPixels = 4000f;
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
        private readonly List<(float Until, Action<float> Tick, Action Dispose, bool Afterglow)> active =
            new List<(float, Action<float>, Action, bool)>();
        private HashSet<string> diagnostics => cache.Diagnostics;
        private readonly SkeletonPlayerFactory skeletonFactory = new SkeletonPlayerFactory();
        private readonly System.Random cosmeticRandom = new System.Random(137);
        private BattlePreviewActor caster;
        private string currentPresentationId;
        private Vector3 home;
        private Transform casterShadow;
        private Vector3 shadowHome;
        private (Vector3 From, Vector3 GroundFrom, Vector3 To, float Start, float Duration, float Arc, Vector4? Curve)? motion;
        private float time;
        private float? choreographyEnd;

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
        internal float FullDuration { get; private set; }
        internal float CutinRemainingSeconds => cinematic.CutinRemainingSeconds;
        public IReadOnlyList<float> HitTimes { get; private set; } = Array.Empty<float>();

        public BattleSkillPresentation(Transform worldRoot, TextAsset[] graphs, BattleSkillPresentationAsset[] assets,
            Camera camera = null, Action<bool> cinematicChanged = null, GameObject background = null)
            : this(worldRoot, graphs, assets, camera, cinematicChanged, background, null) { }

        internal BattleSkillPresentation(Transform worldRoot, TextAsset[] graphs, BattleSkillPresentationAsset[] assets,
            Camera camera, Action<bool> cinematicChanged, GameObject background, TextAsset overrides)
            : this(worldRoot, ReadGraphs(graphs, overrides)
                .ToDictionary(graph => (string)graph["id"], StringComparer.Ordinal),
                (assets ?? Array.Empty<BattleSkillPresentationAsset>()).ToDictionary(asset => asset.source, StringComparer.Ordinal),
                new SessionCache(), camera, cinematicChanged, background) { }

        private static IEnumerable<JObject> ReadGraphs(IEnumerable<TextAsset> assets, TextAsset overrides)
        {
            JObject patches = null;
            if (overrides)
            {
                try { patches = SkillTimelineOverrides.ReadCatalog(overrides.text); }
                catch (Exception error) when (error is InvalidOperationException or Newtonsoft.Json.JsonException)
                {
                    Debug.LogError("Cannot read skill timeline overrides: " + error.Message, overrides);
                }
            }

            foreach (var asset in assets.Where(asset => asset))
            {
                var graph = JObject.Parse(asset.text);
                string id = (string)graph["id"];
                if (patches?[id] is JObject patch)
                {
                    try { graph = SkillTimelineOverrides.Apply(graph, patch); }
                    catch (Exception error) when (error is InvalidOperationException or Newtonsoft.Json.JsonException or FormatException or OverflowException)
                    {
                        Debug.LogError("Cannot apply skill timeline override for " + id + ": " + error.Message, overrides);
                    }
                }
                yield return graph;
            }
        }

#if UNITY_EDITOR
        internal IReadOnlyCollection<string> EditorDiagnostics => diagnostics;

        internal IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)> GetPreparedCues(
            BattlePreviewActor actor, string presentationId)
        {
            Prepare(actor, presentationId);
            var cues = cache.Cues[(actor, presentationId)];
            var derived = ModelTimelineFrames(graphs[presentationId], cues)
                .Where(frame => (frame.Function is "shake" or "event") ||
                    (frame.Function == "effect" && !string.IsNullOrEmpty((string)frame.Parameters["source"])))
                .Select(frame =>
                {
                    var node = (JObject)frame.Parameters.DeepClone();
                    node["etty"] = frame.Function == "effect" ? "EFFECT" : frame.Function == "shake" ? "SHAKE" : "ONEVENT";
                    node["trackSource"] = frame.Source;
                    float duration = frame.Function == "effect" ? EffectDuration((string)node["source"])
                        : frame.Function == "shake" ? ShakeDuration(node) : 0f;
                    return (frame.At, node, duration, Array.Empty<JObject>());
                });
            return cues.Concat(derived).OrderBy(cue => cue.At).ToArray();
        }
#endif

        private BattleSkillPresentation(Transform worldRoot, Dictionary<string, JObject> graphs,
            Dictionary<string, BattleSkillPresentationAsset> assets, SessionCache cache,
            Camera camera, Action<bool> cinematicChanged, GameObject background,
            Dictionary<Renderer, TargetBody> targetBodyHolds = null)
        {
            this.worldRoot = worldRoot; this.graphs = graphs; this.assets = assets; this.cache = cache;
            this.targetBodyHolds = targetBodyHolds ?? new Dictionary<Renderer, TargetBody>();
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
            var next = new BattleSkillPresentation(worldRoot, graphs, assets, cache, camera, cinematicChanged, background,
                targetBodyHolds);
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
                cache.Cues.Add((actor, presentationId), ReadCues(graph).ToArray());
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
            actor.UseGraphForm(presentationId);
            actor.BeginPresentationControl();
            currentPresentationId = string.IsNullOrEmpty(presentationId) ? actor.name : presentationId;
            home = actor.transform.localPosition;
            casterShadow = actor.PresentationShadow;
            shadowHome = casterShadow ? ShadowInSlot : Vector3.zero;
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
                FullDuration = end;
                Schedule(end, EndAction);
                if (primary) Schedule(impact, () => Cfx(BasicHit, primary, recipients, 5, 1f, Vector2.zero, true));
                return (impact, end);
            }

            var nodes = ((JObject)graph["source"])["entities"].OfType<JObject>().ToArray();
            if (nodes.Any(node => Kind(node) == "LAYOUT" && (int?)node["mode"] != 1 && Number(node, "away_x") < 1280f))
                Warn(presentationId + ": source LAYOUT uses a participant visibility mask; self_x/away_x placement and fade curves remain unverified and authored slots are retained.");
            if (nodes.Any(node => Kind(node) == "MOVE"))
                Warn(presentationId + ": source MOVE timing, offsets and authored curves are used. Default movement curves and jump arcs are reconstructed.");
            Prepare(actor, presentationId);
            var cues = cache.Cues[(actor, presentationId)];
            var main = cues.First(cue => Kind(cue.Node) == "ANI" &&
                ((string)cue.Node["ani"]).StartsWith("skill", StringComparison.Ordinal));
            string animation = (string)main.Node["ani"];
            float clipDuration = Mathf.Max(0.1f, actor.GetBattleAnimationDuration(animation));
            float lead = main.At;
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
                    case "MODEL_BODY":
                        string bodyKey = "model/" + (string)node["model_id"] + ".scsp";
                        if (assets.TryGetValue(bodyKey, out var body))
                            Schedule(at, () => actor.UseSourceBody(body));
                        else WarnSource("Missing MODEL_BODY skeleton " + bodyKey);
                        break;
                    case "HIT":
                        string hit = (string)node["effect"];
                        if (!string.IsNullOrEmpty(hit))
                            Schedule(at, () => Cfx(hit, primary, recipients, 6, 1f,
                                new Vector2(Number(node, "offset_x"), Number(node, "offset_y")), true,
                                new Vector2(Number(node, "rand_x"), Number(node, "rand_y"))));
                        break;
                    case "SHOT":
                        Schedule(at, () => PlayShot(node, primary, cue.Duration));
                        break;
                    case "EFFECT":
                        Schedule(at, () => Effect(node, primary, recipients));
                        break;
                    case "CUTIN":
                        string key = "cut/fhd/" + (string)node["args"] + ".webp";
                        if (assets.TryGetValue(key, out var cutin)) Schedule(at, () => cinematic.PlayCutinWithNode(cutin, node));
                        else Warn("Missing original CUTIN " + key);
                        break;
                    case "CAM": case "ZOOM": case "DARK": case "BGSHOW": case "BGHATCH": case "LAYOUT":
                        Schedule(at, () => cinematic.Play(node));
                        break;
                    case "STOP":
                        Schedule(at, () => { StopTargetLayer(node); cinematic.Play(node); });
                        break;
                    case "MOVE":
                        Schedule(at, () => PlayMove(node, primary, recipients, cue.Duration));
                        break;
                    case "NODE_ANI":
                        Schedule(at, () => PlayNodeAnimation(node, at, recipients));
                        break;
                    case "COLOR_BLEND":
                        if (IsTargetHide(node) && !nodes.Any(other => Kind(other) == "STOP" &&
                            (string)other["targetId"] == (string)node["id"] && Number(other, "time") != 0f &&
                            ((int?)node["blend_target"] != 55 || Number(other, "time") != Number(node, "fadeOut"))))
                            Schedule(at, () => HoldTargetLayer(node, recipients));
                        else WarnSource($"COLOR_BLEND {node["guid"]} tint layering is pending source verification.");
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
            // STG completion, camera cues, and hits remain authoritative for the visual action.
            // Model-timeline effects can leave particles after their authored emission windows.
            float fullEnd = actionEnd;
            foreach (var frame in ModelTimelineFrames(graph, cues))
            {
                var node = frame.Parameters;
                switch (frame.Function)
                {
                    case "effect" when !string.IsNullOrEmpty((string)node["source"]):
                        string source = (string)node["source"];
                        fullEnd = Mathf.Max(fullEnd, frame.At + EffectDuration(source));
                        actionEnd = Mathf.Max(actionEnd, frame.At + EffectDuration(source, includeAfterglow: false));
                        Schedule(frame.At, () => Cfx(source, actor, recipients, (int?)node["location"] ?? 1,
                            Number(node, "scale", 1f), new Vector2(Number(node, "x"), Number(node, "y")),
                            false, anchorName: (string)node["locationBone"] ?? (string)node["locationParam"],
                            modelTimeline: true));
                        break;
                    case "shake":
                        actionEnd = Mathf.Max(actionEnd, frame.At + ShakeDuration(node));
                        Schedule(frame.At, () => PlayShake(node));
                        break;
                }
            }

            FullDuration = Mathf.Max(actionEnd, fullEnd);
            choreographyEnd = actionEnd;
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

        private IEnumerable<(float At, JObject Node, float Duration, JObject[] Modulators)> ReadCues(JObject graph)
        {
            var source = graph["source"];
            var additions = graph["additions"];
            var nodes = source["entities"].OfType<JObject>()
                .Concat(additions?["entities"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).ToArray();
            if ((int?)graph["animationIndex"] is int animationIndex)
            {
                if (animationIndex < 1 || animationIndex > 3)
                    throw new FormatException("Unsupported source skill animation index " + animationIndex);
                for (int index = 0; index < nodes.Length; index++)
                {
                    string name = (string)nodes[index]["ani"];
                    if (name == null || !name.Contains("{idx}")) continue;
                    var resolved = (JObject)nodes[index].DeepClone();
                    resolved["ani"] = name.Replace("{idx}", animationIndex.ToString(CultureInfo.InvariantCulture));
                    nodes[index] = resolved;
                }
            }
            var byId = nodes.ToDictionary(node => (int)node["guid"]);
            var edges = source["connections"].OfType<JObject>()
                .Concat(additions?["connections"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>()).ToLookup(edge => (int)edge["from"]);
            var earliest = new Dictionary<int, (float At, int Order, float Duration)>();
            var model = caster.GraphMetadata((string)graph["id"]);
            var timelineEvents = (graph["assets"] ?? new JArray()).Where(item => (string)item["type"] == "timeline")
                .Select(item => Document((string)item["source"])?["timeline"]?["default"])
                .OfType<JObject>().ToArray();
            float EventTime(string animation, string name)
            {
                if (string.IsNullOrEmpty(animation) || string.IsNullOrEmpty(name)) return -1f;
                float spineTime = model == null ? caster.GetBattleAnimationEventTime(animation, name) :
                    model.Events.TryGetValue(animation, out var events) && events.TryGetValue(name, out var time) ? time : -1f;
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
                if (node[SkillTimelineOverrides.AbsoluteTimeField] != null)
                    at = Seconds(node, SkillTimelineOverrides.AbsoluteTimeField);
                if (path.Contains(id) || (earliest.TryGetValue(id, out var old) && old.At <= at)) return;
                float duration = durationOverride ?? Completion(node, model);
                float finish = at + duration;
                float? jumpStart = null;
                if (Kind(node) == "ANI") { animation = (string)node["ani"]; animationStart = at; }
                if (Kind(node) == "ANI_SCOPE")
                {
                    string from = (string)node["from"];
                    float start = string.IsNullOrEmpty(from) ? 0f : EventTime(animation, from);
                    float end = EventTime(animation, (string)node["to"]);
                    if (start >= 0f && end < 0f)
                    {
                        end = model?.Duration(animation) ?? caster.GetBattleAnimationDuration(animation);
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

        private float Completion(JObject node, BattleSkeletonMetadata model)
        {
            switch (Kind(node))
            {
                case "ANI": return model?.Duration((string)node["ani"]) ?? caster.GetBattleAnimationDuration((string)node["ani"]);
                case "ANI_POS": return Seconds(node, "delay");
                case "CUTIN": return 0f; // Wall-clock suspension; source successors remain at the same stage time.
                case "DARK": return Number(node, "duration", -1f) < 0f ? 0f : Seconds(node, "duration") + Seconds(node, "fadeOut");
                case "COLOR_BLEND" when IsTargetHide(node) && Number(node, "duration") > 0f:
                    return Seconds(node, "duration") + Seconds(node, "fadeOut");
                case "EFFECT": return EffectDuration((string)node["source"]);
                case "SHAKE": return ShakeDuration(node);
                case "NODE_ANI": return NodeAnimationDuration(node);
                case "PPEFFECT": return Seconds(node, "duration");
                default: return Seconds(node, "time");
            }
        }

        private float EffectDuration(string source, bool includeAfterglow = true)
        {
            if (string.IsNullOrEmpty(source)) return 0f;
            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            return Document(key)?["primitive"]?.OfType<JObject>()
                .Select(part => Mathf.Max(0f, UpperNumber(part, "delay")) / 1000f +
                    (includeAfterglow ? PrimitiveDuration(part) : ChoreographyPrimitiveDuration(part)))
                .DefaultIfEmpty(0f).Max() ?? 0f;
        }

        // Model-timeline effects preserve authored Spine clips, emission windows, and explicit lifetimes.
        private float ChoreographyPrimitiveDuration(JObject part)
        {
            if ((string)part["format"] != "particle" || UpperNumber(part, "lifeTime", -1f) >= 0f)
                return PrimitiveDuration(part);
            float speed = PrimitiveSpeed(part);
            if (speed <= 0f) return 0f;
            var emitters = Document(EffectPath + (string)part["source"] + ".particle")?["emitters"] as JArray;
            if (emitters == null) return PrimitiveDuration(part);
            float end = 0f;
            foreach (var emitter in emitters.OfType<JObject>().Where(item => (bool?)item["disable"] != true))
            {
                bool burst = (int?)emitter["burningType"] == 1;
                float duration = Number(emitter, "duration");
                // Unknown/unbounded emitters keep the existing explicit diagnostic fallback.
                if (!burst && duration < 0f) return PrimitiveDuration(part);
                float start = Mathf.Max(0f, Number(emitter, "startDelay") + Mathf.Abs(Number(emitter, "startDelayVar")));
                float repetitions = Mathf.Max(0f, Number(emitter, "repeat") - 1f) * Number(emitter, "interval");
                end = Mathf.Max(end, start + repetitions + (burst ? 0f : duration));
            }
            return end / speed;
        }

        private float PrimitiveDuration(JObject part)
        {
            float lifetime = UpperNumber(part, "lifeTime", -1f);
            string source = EffectPath + (string)part["source"];
            float speed = PrimitiveSpeed(part);
            if (speed <= 0f) return 0f;
            if ((string)part["format"] == "spine")
            {
                string key = source + ".scsp";
                var skeleton = Metadata(key);
                string animation = InitialSpineAnimation(part, key, skeleton);
                if (animation == null) return 0f;
                if (lifetime >= 0f) return lifetime / 1000f;
                float duration = skeleton.Duration(animation) / speed;
                if (UsesIntroLoop(part, skeleton)) duration += skeleton.Duration("loop") / speed;
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                    if ((string)action["action"] == "Opacity")
                        duration = Mathf.Max(duration, Seconds(action, "delay") + Seconds(action, "interval"));
                    else if (HasSpineAction(key, skeleton, (string)action["action"]))
                        duration = Mathf.Max(duration, Seconds(action, "delay") + skeleton.Duration((string)action["action"]) / speed);
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
                end = Mathf.Max(end, (start + repetitions + (burst ? 0f : duration) + life) / speed);
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
                if (UsesIntroLoop(part, skeleton)) return "intro";
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

        private static bool UsesIntroLoop(JObject part, BattleSkeletonMetadata skeleton) =>
            (string)part["ani"] == "intro,loop" && skeleton.Duration("intro") > 0f && skeleton.Duration("loop") > 0f;

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

        private IEnumerable<(float At, string Function, JObject Parameters, string Source)> ModelTimelineFrames(JObject graph,
            IReadOnlyList<(float At, JObject Node, float Duration, JObject[] Modulators)> cues)
        {
            foreach (var dependency in graph["assets"].Where(item => (string)item["type"] == "timeline"))
            {
                string source = (string)dependency["source"];
                var timeline = Document(source)?["timeline"]?["default"];
                foreach (var clip in cues.Where(cue => Kind(cue.Node) == "ANI"))
                {
                    if (!(timeline?[(string)clip.Node["ani"]]?["frames"] is JArray frames)) continue;
                    foreach (var frame in frames.OfType<JObject>())
                        if (frame["param"] is JObject parameters)
                            yield return (clip.At + Seconds(frame, "time"), (string)frame["func"], parameters, source);
                }
            }
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
                if (choreographyEnd is float end && time >= end)
                {
                    EndAction(preserveHandoff: false, preserveAfterglow: true);
                    continue;
                }
                if (remaining <= 0f) break;
                float step = pending.Count > 0 ? Mathf.Min(remaining, pending[0].At - time) : remaining;
                if (choreographyEnd is float finish) step = Mathf.Min(step, finish - time);
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
            AdvanceTargetLayers();
            if (caster) caster.AdvancePresentation(delta);
            if (motion is { } movement && caster)
            {
                float progress = Mathf.Clamp01((time - movement.Start) / movement.Duration);
                float eased = movement.Curve is { } curve
                    ? BattleCinematicStage.EvaluateCurve(progress, curve)
                    : Mathf.SmoothStep(0f, 1f, progress);
                caster.transform.localPosition = Vector3.LerpUnclamped(movement.From, movement.To, eased) +
                    Vector3.up * (Mathf.Sin(progress * Mathf.PI) * movement.Arc);
                if (progress >= 1f)
                {
                    caster.transform.localPosition = movement.To;
                    cinematic.SetCasterParked(false);
                    motion = null;
                }
                if (casterShadow)
                    ShadowInSlot = shadowHome + Vector3.LerpUnclamped(movement.GroundFrom, movement.To, eased) - home;
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

        // MOVE positions are authored in the slot. A prefab child needs the same ground path
        // as the original sibling shadow, independent of the airborne body transform.
        private Vector3 ShadowInSlot
        {
            get
            {
                var slot = caster.transform.parent;
                return slot ? slot.InverseTransformPoint(casterShadow.position) : casterShadow.position;
            }
            set
            {
                var slot = caster.transform.parent;
                casterShadow.position = slot ? slot.TransformPoint(value) : value;
            }
        }

        private void PlayMove(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets, float duration)
        {
            if (!caster) return;
            int style = (int?)node["style"] ?? 1;
            int location = (int?)node["location"] ?? 11;
            Vector3 destination = home;
            float approachDirection = 0f, minimumGap = 0f;
            if (location == 16)
            {
                if (!primary || primary == caster) return;
                Vector3 target = primary.transform.position;
                float horizontal = target.x - caster.transform.position.x;
                float gap = .9f;
                if (style is 1 or 2)
                {
                    approachDirection = horizontal == 0f ? caster.FacingDirection : Mathf.Sign(horizontal);
                    float footprint = (ShadowWidth(caster) + ShadowWidth(primary)) * .5f;
                    gap = Mathf.Clamp(footprint + .18f, 1.18f, 2.2f);
                    minimumGap = Mathf.Min(Mathf.Abs(horizontal), Mathf.Clamp(footprint, .9f, 2f));
                }
                float separation = Mathf.Clamp(horizontal, -gap, gap);
                destination = caster.transform.parent.InverseTransformPoint(target - Vector3.right * separation);
            }
            else if (location == 12 && primary)
                destination = caster.transform.parent.InverseTransformPoint(primary.transform.position);
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
            // Style 3 has no jump arc. It still carries authored placement offsets,
            // including deliberate offscreen staging before a timed entrance.
            float x = Number(node, "x"), y = Number(node, "y");
            destination += new Vector3(x * caster.FacingDirection, y, 0f) * SourcePixel;
            if (approachDirection != 0f)
            {
                // Preserve authored offsets while preventing a front approach from closing the ground gap.
                var parent = caster.transform.parent;
                var worldDestination = parent.TransformPoint(destination);
                float limit = primary.transform.position.x - approachDirection * minimumGap;
                if ((worldDestination.x - limit) * approachDirection > 0f)
                {
                    worldDestination.x = limit;
                    destination = parent.InverseTransformPoint(worldDestination);
                }
            }
            float arc = style == 3 ? 0f : Number(node, "h", style == 2 ? Number(node, "power") : 0f) * SourcePixel;
            if (duration > 0f)
            {
                Vector3 from = caster.transform.localPosition;
                Vector3 groundFrom = casterShadow ? home + ShadowInSlot - shadowHome : from;
                Vector4? curve = BattleCinematicStage.TryParseCurve((string)node["curve"], out var points) ? points : null;
                motion = (from, groundFrom, destination, time, duration, arc, curve);
            }
            else
            {
                motion = null;
                bool parksCaster = style == 3 && location == 11 &&
                    Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) >= OffstageParkingPixels;
                cinematic.SetCasterParked(parksCaster);
                caster.transform.localPosition = destination;
                if (casterShadow) ShadowInSlot = shadowHome + destination - home;
            }
        }

        private static float ShadowWidth(BattlePreviewActor actor)
        {
            var shadow = actor.PresentationShadow;
            return shadow && shadow.TryGetComponent<SpriteRenderer>(out var renderer) ? renderer.bounds.size.x : 0f;
        }

        private static Vector3 TargetCenter(BattlePreviewActor[] targets) =>
            targets.Aggregate(Vector3.zero, (sum, target) => sum + target.transform.position) / targets.Length;

        private void PlayShot(JObject node, BattlePreviewActor target, float duration)
        {
            string source = (string)node["source"];
            if (string.IsNullOrEmpty(source)) return;
            // Native ranged models may omit selfBone while supplying their launch anchor in the rig.
            string bone = (string)node["selfBone"];
            if (string.IsNullOrWhiteSpace(bone)) bone = "arrow_start";
            if (!target || duration <= 0f || !caster.TryGetPresentationAnchor(bone, out var launch))
            {
                WarnSource($"SHOT {node["guid"]} has no verified launch bone, target or duration.");
                return;
            }

            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            if (!(Document(key)?["primitive"] is JArray primitives)) return;

            // The native graph provides a launch bone and travel time. The target's visual center is
            // the preview convention until the default target bone and SHOT y semantics are verified.
            Vector3 arrival = target.Bounds.center + Vector3.up * (Number(node, "y") * SourcePixel);
            WarnSource($"SHOT {node["guid"]} uses the target bounds and authored y offset; original landing anchor is unverified.");
            float started = time;
            Vector3 Position() => Vector3.Lerp(launch, arrival, Mathf.Clamp01((time - started) / duration));
            float side = caster.FacingDirection;
            var sourceActor = caster;
            foreach (var raw in primitives.OfType<JObject>())
            {
                var part = ReadPrimitive(raw);
                if (part == null) continue;
                float delay = Seconds(part, "delay");
                if (delay >= duration) continue;
                float remaining = duration - delay;
                part["lifeTime"] = Mathf.Min(PrimitiveDuration(part), remaining) * 1000f;
                Schedule(time + delay, () => Spawn(key, part, Position, sourceActor, 1f, Vector2.zero, side, true));
            }
        }

        private void Effect(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets)
        {
            string source = (string)node["source"];
            if (string.IsNullOrEmpty(source)) return;
            int? location = (int?)node["location"];
            string anchorName = (string)node["locationParam"];
            var offset = new Vector2(Number(node, "x"), Number(node, "y"));
            bool disableAutoFlipX = (bool?)node["disableAutoFlipX"] == true;
            if (!location.HasValue && TryPlayEffectObject(node, primary, targets, offset))
                return;
            if (!location.HasValue && string.IsNullOrEmpty(anchorName) && source == "iseria_skill_03_gate.cfx")
            {
                // At 13:30.5 the outer sword lights span 55% of the 1920-wide reference.
                // The gate rig places them 1541 source units apart, with its origin near the lower edge.
                Cfx(source, primary, targets, null, Number(node, "scale", 1f) * .69f,
                    offset + new Vector2(0f, -350f), false, disableAutoFlipX: disableAutoFlipX);
                return;
            }
            // The ken_nbg_tx lettering stays at the same pixels through the source's 0.60 → 0.65 → 0.60 zoom.
            bool fixedArtwork = !location.HasValue && string.IsNullOrEmpty(anchorName) && disableAutoFlipX &&
                source == "ken_skill_03_tx.cfx";
            // The sky spans the recording frame through the wide pre-portrait shot. Its graph
            // gives a 20-pixel vertical offset and no actor anchor for either cloud primitive.
            bool fullSky = !location.HasValue && string.IsNullOrEmpty(anchorName) && source == "lunabianca_skill_03_sky.cfx";
            // Confirmed screen cues use the authored 216-unit vertical offset without an actor location.
            if (!location.HasValue && !fullSky && (offset.y != 216f || !string.IsNullOrEmpty(anchorName)))
            {
                WarnSource($"EFFECT {node["guid"]} has no confirmed default location. Using the primary target.");
                location = 5;
            }
            float scale = Number(node, "scale", 1f);
            if (fullSky && camera) scale *= Mathf.Max(1f, camera.aspect / (16f / 9f));
            if ((int?)node["location"] == 5 && SharesCasterMotion(node, source)) primary = caster;
            Cfx(source, primary, targets, location, scale, offset, false,
                anchorName: anchorName, disableAutoFlipX: disableAutoFlipX, fixedArtwork: fixedArtwork);
        }

        private bool SharesCasterMotion(JObject effect, string source)
        {
            var stage = effect.Ancestors().OfType<JObject>().FirstOrDefault();
            if (!(stage?["connections"] is JArray links)) return false;
            var starts = links.OfType<JObject>().Where(link => (int?)link["to"] == (int?)effect["guid"])
                .Select(Start).ToHashSet();
            var siblings = links.OfType<JObject>().Where(link => starts.Contains(Start(link)))
                .Select(link => (int?)link["to"]).ToHashSet();
            foreach (var node in stage["entities"].OfType<JObject>().Where(node => siblings.Contains((int?)node["guid"]) &&
                Kind(node) == "NODE_ANI" && (int?)node["target_node"] == 5))
            {
                string rig = System.IO.Path.GetFileNameWithoutExtension((string)node["source"]);
                const string suffix = "_node_self";
                if (rig == null || !rig.EndsWith(suffix, StringComparison.Ordinal)) continue;
                string family = rig.Substring(0, rig.Length - suffix.Length) + "_";
                string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
                if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
                if (Document(key)?["primitive"] is JArray parts && parts.OfType<JObject>().Any(part =>
                    ((string)part["source"])?.StartsWith(family, StringComparison.Ordinal) == true)) return true;
            }
            return false;

            static (int?, int, float) Start(JObject link) =>
                ((int?)link["from"], (int?)link["when"] ?? 0, Number(link, "delay"));
        }

        private bool TryPlayEffectObject(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets,
            Vector2 offset)
        {
            string id = (string)node["id"];
            if (string.IsNullOrEmpty(id) || !primary) return false;
            var tracks = cache.Cues[(caster, currentPresentationId)].Where(cue => (string)cue.Node["targetId"] == id &&
                Kind(cue.Node) is "OBJ_MOVE_BY" or "OBJ_SCALE_TO").ToArray();
            if (tracks.Length == 0) return false;

            var effectObject = new GameObject("Skill object " + id).transform;
            cinematic.FrameSourceObject();
            effectObject.SetParent(root, false);
            float side = caster.FacingDirection;
            primary.TryGetPresentationAnchor(null, out var anchor);
            effectObject.position = anchor + new Vector3(offset.x * side, offset.y) * SourcePixel;
            effectObject.localScale = Vector3.one * Number(node, "scale", 1f);
            string source = (string)node["source"];
            active.Add((time + EffectDuration(source), _ => { }, () =>
            {
                if (effectObject) DestroyVisual(effectObject.gameObject);
            }, false));
            Cfx(source, primary, targets, null, 1f, Vector2.zero, false, effectObject: effectObject);
            foreach (var track in tracks)
            {
                var motion = track.Node;
                Schedule(track.At, () =>
                {
                    if (!effectObject) return;
                    bool scale = Kind(motion) == "OBJ_SCALE_TO";
                    var start = scale ? effectObject.localScale : effectObject.localPosition;
                    var end = scale ? Vector3.one * Number(motion, "scale", 1f) :
                        start + new Vector3(side * Number(motion, "x"), Number(motion, "y")) * SourcePixel;
                    float from = time, duration = Seconds(motion, "time");
                    bool hasCurve = BattleCinematicStage.TryParseCurve((string)motion["curve"], out var curve);
                    void Set(float progress)
                    {
                        if (!effectObject) return;
                        float eased = hasCurve ? BattleCinematicStage.EvaluateCurve(progress, curve) : progress;
                        var value = Vector3.LerpUnclamped(start, end, eased);
                        if (scale) effectObject.localScale = value;
                        else effectObject.localPosition = value;
                    }
                    if (duration > 0f)
                        active.Add((from + duration, _ => Set(Mathf.Clamp01((time - from) / duration)), () => Set(1f), false));
                    else Set(1f);
                });
            }
            return true;
        }

        private void Cfx(string source, BattlePreviewActor primary, BattlePreviewActor[] targets, int? location,
            float scale, Vector2 offset, bool hit, Vector2 spread = default, string anchorName = null,
            bool modelTimeline = false, bool disableAutoFlipX = false, bool fixedArtwork = false,
            Transform effectObject = null)
        {
            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            if (!(Document(key)?["primitive"] is JArray primitives)) return;

            if (!location.HasValue)
            {
                if (!camera && !effectObject)
                {
                    WarnSource($"Screen CFX '{key}' needs a camera.");
                    return;
                }
                var parts = primitives.OfType<JObject>().Select(ReadPrimitive).Where(part => part != null).ToArray();
                var depths = parts.Select(part => Number(part, "globalZ", 1f)).Distinct().OrderBy(depth => depth).ToArray();
                var behind = depths.Where(depth => depth < 0f).ToArray();
                var inFront = depths.Where(depth => depth >= 0f).ToArray();
                float side = caster.FacingDirection;
                foreach (var part in parts)
                {
                    float depth = Number(part, "globalZ", 1f);
                    // Keep source layers ordered around the actors, below the full-screen flash and HUD.
                    int order = depth < 0f ? -10 + Array.IndexOf(behind, depth) : 65 + Array.IndexOf(inFront, depth);
                    Schedule(time + Seconds(part, "delay"), () =>
                        Spawn(key, part, null, null, scale, offset, side, true, order,
                            disableAutoFlipX: disableAutoFlipX, effectObject: effectObject));
                }
                return;
            }
            if (location == 32)
            {
                if (TryGetCasterFormationCenter(Position, out Vector3 center))
                    SpawnAt(() => center, caster, false);
                return;
            }
            if (location == 31)
            {
                var other = primary ? primary : targets.FirstOrDefault(target => target);
                if (caster && other)
                    SpawnAt(() => (Position(caster) + Position(other)) * .5f, caster, false);
                else if (caster)
                    SpawnAt(() => Position(caster), caster, false);
                return;
            }
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
                2 => targets.Length > 0 ? targets : new[] { primary },
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
                        () => Spawn(key, part, position, anchor, scale, offset + variation, side, follow,
                            modelTimeline: modelTimeline, disableAutoFlipX: disableAutoFlipX, fixedArtwork: fixedArtwork));
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
                    width != 0f && height != 0f)
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
            Vector2 offset, float side, bool follow, int? screenOrder = null, bool modelTimeline = false,
            bool disableAutoFlipX = false, bool fixedArtwork = false, Transform effectObject = null)
        {
            string source = (string)part["source"];
            if (string.IsNullOrWhiteSpace(source)) return;
            string format = (string)part["format"];
            var renderer = anchor ? anchor.PresentationRenderer : null;
            // Model timeline meshes and emitter offsets use the same source space as the visible body.
            float anchorScale = anchor ? Mathf.Abs(anchor.transform.lossyScale.x) : 1f;
            float bodyScale = modelTimeline && renderer ? Mathf.Abs(renderer.transform.lossyScale.x) : anchorScale;
            float size = Mathf.Max(0.01f, Number(part, "scale", 1f) * scale * bodyScale);
            int order = screenOrder ??
                ((renderer ? renderer.sortingOrder : 55) + (int)Mathf.Sign(Number(part, "globalZ", 1f)) * 2);
            var group = new GameObject("Skill CFX " + key.Substring(EffectPath.Length)).transform;
            group.SetParent(effectObject ? effectObject : root, false);
            // STG offsets locate the effect on the formation. Its primitives can retain their authored orientation.
            float visualFacing = disableAutoFlipX ? 1f : side;
            Vector3 translation = new Vector3(offset.x * side + Number(part, "x") * visualFacing,
                offset.y + Number(part, "y"), 0f) * (SourcePixel * (modelTimeline ? bodyScale : 1f));
            group.localRotation = Quaternion.Euler(0f, 0f, -Number(part, "rotate") * visualFacing);
            var partScale = new Vector3(Number(part, "scaleX", 1f), Number(part, "scaleY", 1f), 1f);
            Vector3 viewportAnchor = default;
            UpdatePosition(true);
            try
            {
                switch (format)
                {
                    case "spine":
                        SpawnSpine(source, part, group, size, order, visualFacing, () => UpdatePosition(false));
                        break;
                    case "particle":
                        SpawnParticles(source, part, group, size, order, () =>
                        {
                            if (!modelTimeline || caster) UpdatePosition(false);
                        }, modelTimeline);
                        break;
                    default:
                        throw new FormatException("Unsupported primitive format " + format);
                }
            }
            catch (Exception error) when (error is FormatException || error is InvalidOperationException || error is KeyNotFoundException)
            {
                if (group) DestroyVisual(group.gameObject);
                Warn(source + ": " + error.Message);
            }

            void UpdatePosition(bool initial)
            {
                if (effectObject)
                {
                    group.localPosition = translation;
                    group.localScale = partScale;
                }
                else if (screenOrder.HasValue)
                {
                    group.position = cinematic.SourceScreenPoint(root.position, translation);
                    group.localScale = partScale * cinematic.SourceScreenScale;
                }
                else if (fixedArtwork && camera)
                {
                    if (initial) viewportAnchor = camera.WorldToViewportPoint(position() + translation);
                    group.position = camera.ViewportToWorldPoint(viewportAnchor);
                    group.localScale = partScale * cinematic.SourceScreenScale;
                }
                else if (initial || follow)
                {
                    group.position = position() + translation;
                    if (initial) group.localScale = partScale;
                }
            }
        }

        private float PrimitiveSpeed(JObject part)
        {
            if (part["timeScale"] == null) return 1f;
            if (TryScalar(part["timeScale"].ToString(), out float speed) && speed > 0f) return speed;
            WarnSource($"Unsupported CFX timeScale={part["timeScale"]} for '{part["source"]}'.");
            return 0f;
        }

        private void SpawnSpine(string source, JObject part, Transform group, float size, int order, float side, Action follow)
        {
            string key = EffectPath + source + ".scsp";
            float speed = PrimitiveSpeed(part);
            if (speed <= 0f) throw new FormatException("Unplayable CFX speed in " + key);
            var metadata = Metadata(key);
            string animation = InitialSpineAnimation(part, key, metadata);
            if (animation == null) throw new FormatException($"Unplayable CFX animation '{(string)part["ani"] ?? "(default)"}' in '{key}'.");
            var player = skeletonFactory.Create(Skeleton(key, assets[key]), group);
            try
            {
                player.Root.name = source;
                player.Root.transform.localScale = new Vector3(side * size, size, size);
                var renderer = player.Root.GetComponent<MeshRenderer>();
                renderer.sortingOrder = order;
                MaterialPropertyBlock opacityBlock = null;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                {
                    opacityBlock = new MaterialPropertyBlock();
                    // Iseria's luminous rigs stack authored additive colours. At 13:30.72
                    // the gate's six white cores occupy 14% of the central shot.
                    if (source is "iseria_skill_03_gateopen" or "iseria_skill_03_swordswing_01")
                        opacityBlock.SetFloat("_PresentationBrightness", .80f);
                    else if (source is "iseria_skill_03_final_front" or "iseria_skill_03_final_back")
                        opacityBlock.SetFloat("_PresentationBrightness", .90f);
                    else opacityBlock.SetFloat("_SrgbVertexTint", 1f);
                    renderer.SetPropertyBlock(opacityBlock);
                }
                var updateOpacity = PrimitiveOpacity(part, value =>
                {
                    opacityBlock ??= new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(opacityBlock);
                    opacityBlock.SetFloat("_PresentationOpacity", value);
                    renderer.SetPropertyBlock(opacityBlock);
                });
                player.Play(animation, false);
                float lifetime = PrimitiveDuration(part);
                if (UsesIntroLoop(part, metadata) && metadata.Duration("intro") / speed < lifetime)
                    Schedule(time + metadata.Duration("intro") / speed, () =>
                    {
                        if (player.Root) player.Play("loop", true);
                    });
                foreach (var action in (part["actions"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string name = (string)action["action"];
                    if (name == "Opacity" || !HasSpineAction(key, metadata, name)) continue;
                    Schedule(time + Seconds(action, "delay"), () =>
                    {
                        if (player.Root) player.Play(name, false);
                    });
                }
                active.Add((time + lifetime, delta =>
                {
                    follow();
                    player.Tick(delta * speed);
                    updateOpacity?.Invoke();
                }, () =>
                {
                    player.Dispose();
                    if (group) DestroyVisual(group.gameObject);
                }, false));
            }
            catch
            {
                player.Dispose();
                throw;
            }
        }

        // Primitive actions use the stage clock in milliseconds, independently of Spine animation speed.
        private Action PrimitiveOpacity(JObject part, Action<float> set)
        {
            var fades = (part["actions"] as JArray)?.OfType<JObject>()
                .Where(action => (string)action["action"] == "Opacity")
                .Select(action => (Start: Seconds(action, "delay"), Duration: Seconds(action, "interval"),
                    From: Number(action, "from", 1f), To: Number(action, "to", 0f)))
                .OrderBy(fade => fade.Start).ToArray();
            float initial = Mathf.Clamp01(Number(part, "opacity", 1f));
            if (fades == null || fades.Length == 0)
            {
                if (!Mathf.Approximately(initial, 1f)) set(initial);
                return null;
            }
            float start = time;
            void Apply()
            {
                float elapsed = time - start;
                float value = initial;
                foreach (var fade in fades)
                {
                    if (elapsed < fade.Start) break;
                    float progress = fade.Duration > 0f ? (elapsed - fade.Start) / fade.Duration : 1f;
                    value = Mathf.Lerp(fade.From, fade.To, progress);
                }
                set(Mathf.Clamp01(value));
            }
            Apply();
            return Apply;
        }

        private void SpawnParticles(string source, JObject part, Transform group, float size, int order, Action follow,
            bool allowAfterglow)
        {
            string key = EffectPath + source + ".particle";
            float speed = PrimitiveSpeed(part);
            if (speed <= 0f) throw new FormatException("Unplayable CFX speed in " + key);
            var particles = new BattleHudParticles(assets[key].document, textures, group, Vector2.zero, size * SourcePixel, order);
            particles.Play();
            var updateOpacity = PrimitiveOpacity(part, particles.SetOpacity);
            active.Add((time + PrimitiveDuration(part), delta =>
            {
                follow();
                particles.Tick(delta * speed);
                updateOpacity?.Invoke();
            }, () =>
            {
                particles.Dispose();
                if (group) DestroyVisual(group.gameObject);
            }, allowAfterglow));
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

        private void EndAction(bool preserveHandoff, bool preserveAfterglow = false)
        {
            if (targetLayers.Count > 0)
                foreach (string id in targetLayers.Keys.ToArray()) ReleaseTargetLayer(id);
            SetCutinPaused(false);
            if (!preserveHandoff) cinematic.Reset();
            if (caster)
            {
                caster.transform.localPosition = home;
                caster.PlayBattleIdle();
                caster.EndPresentationControl();
            }
            if (caster && casterShadow) ShadowInSlot = shadowHome;
            casterShadow = null;
            caster = null;
            motion = null;
            choreographyEnd = null;
            pending.Clear();
            for (int index = active.Count - 1; index >= 0; index--)
            {
                var visual = active[index];
                if (preserveAfterglow && visual.Afterglow) continue;
                visual.Dispose();
                active.RemoveAt(index);
            }
        }

        private static void DestroyVisual(Object value)
        {
            if (Application.isPlaying) Object.Destroy(value);
            else Object.DestroyImmediate(value);
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
