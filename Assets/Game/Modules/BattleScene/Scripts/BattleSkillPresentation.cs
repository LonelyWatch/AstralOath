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
        private Vector3 home;
        private (Vector3 From, Vector3 To, float Start, float Duration, float Arc)? motion;
        private float time;

        public BattleSkillPresentation(Transform worldRoot, TextAsset[] graphs, BattleSkillPresentationAsset[] assets)
        {
            root = new GameObject("Battle Skill Effects").transform;
            root.SetParent(worldRoot, false);
            this.graphs = graphs.Where(graph => graph).Select(graph => JObject.Parse(graph.text))
                .ToDictionary(graph => (string)graph["id"], StringComparer.Ordinal);
            this.assets = (assets ?? Array.Empty<BattleSkillPresentationAsset>()).ToDictionary(asset => asset.source,
                StringComparer.Ordinal);
            foreach (var item in this.assets.Values)
                if (item.texture) textures.Add(item.source, item.texture);
        }

        public (float ImpactSeconds, float Duration) Begin(string presentationId, BattlePreviewActor actor,
            BattlePreviewActor primaryTarget, IReadOnlyList<BattlePreviewActor> targets)
        {
            EndAction();
            caster = actor;
            home = actor.transform.localPosition;
            time = 0f;
            var recipients = targets.Where(target => target).Distinct().ToArray();
            if (recipients.Length == 0 && primaryTarget)
                recipients = new[] { primaryTarget };
            var primary = primaryTarget ? primaryTarget : recipients.FirstOrDefault();

            if (!graphs.TryGetValue(presentationId ?? "", out var graph))
            {
                float duration = actor.PlayBattleAnimation("skill1");
                float impact = Mathf.Clamp(duration * 0.5f, 0.12f, 0.45f);
                if (primary) Schedule(impact, () => Cfx(BasicHit, primary, recipients, 5, 1f, Vector2.zero, true));
                return (impact, Mathf.Max(duration, impact + 0.15f));
            }

            var nodes = ((JObject)graph["source"])["entities"].OfType<JObject>().ToArray();
            var main = nodes.First(node => Kind(node) == "ANI" &&
                ((string)node["ani"]).StartsWith("skill", StringComparison.Ordinal));
            string animation = (string)main["ani"];
            float clipDuration = Mathf.Max(0.1f, actor.GetBattleAnimationDuration(animation));
            float jumpDuration = actor.GetBattleAnimationDuration("jump_forward");
            bool rush = nodes.Any(node => Kind(node) == "MOVE" && (int?)node["location"] == 16) && primary && primary != actor;
            bool jumpLead = rush && nodes.Any(node => Kind(node) == "ANI" && (string)node["ani"] == "jump_forward");
            float lead = jumpLead ? Mathf.Clamp(jumpDuration, 0.18f, 0.65f) : 0f;
            float direction = Facing(primary);
            if (jumpLead)
            {
                actor.PlayBattleAnimation("jump_forward");
                Schedule(0f, () => MoveNear(primary, direction, lead));
            }
            Schedule(lead, () => actor.PlayBattleAnimation(animation));

            var cues = ReadCues(graph, nodes, main, clipDuration).ToArray();
            float[] hits = cues.Where(cue => Kind(cue.Node) == "HIT").Select(cue => cue.At).ToArray();
            float impactTime = lead + (hits.Length == 0 ? Mathf.Min(0.65f, clipDuration * 0.8f) : hits[0]);
            float lastHit = hits.Length == 0 ? impactTime : lead + hits[hits.Length - 1];
            float actionEnd = Mathf.Max(lead + clipDuration, lastHit + 0.15f);
            if (rush && !jumpLead)
            {
                var move = nodes.First(node => Kind(node) == "MOVE" && (int?)node["location"] == 16);
                float duration = Seconds(move, "time", 0.3f);
                Schedule(Mathf.Max(0f, impactTime - duration - 0.12f), () => MoveNear(primary, direction, duration));
            }
            bool moved = rush;
            foreach (var cue in cues)
            {
                var node = cue.Node;
                float at = lead + cue.At;
                switch (Kind(node))
                {
                    case "HIT":
                        string hit = (string)node["effect"];
                        if (!string.IsNullOrEmpty(hit))
                            Schedule(at, () => Cfx(hit, primary, recipients, 6, 1f,
                                new Vector2(Number(node, "offset_x"), Number(node, "offset_y")), true,
                                new Vector2(Number(node, "rand_x"), Number(node, "rand_y"))));
                        break;
                    case "EFFECT":
                        Schedule(at, () => Effect(node, primary, recipients));
                        actionEnd = Mathf.Max(actionEnd, at + 0.25f);
                        break;
                    case "MOVE" when (int?)node["location"] == 11 && node["x"] != null:
                        moved = true;
                        Schedule(at, () => Move(home + new Vector3(Number(node, "x") * SourcePixel * direction, 0f),
                            Seconds(node, "time", 0.2f), 0f));
                        break;
                }
            }

            foreach (var dependency in graph["assets"].Where(item => (string)item["type"] == "timeline"))
            {
                var timeline = Document((string)dependency["source"]);
                if (!(timeline?["timeline"]?["default"]?[animation]?["frames"] is JArray frames)) continue;
                foreach (var frame in frames.OfType<JObject>().Where(frame => (string)frame["func"] == "effect"))
                {
                    var node = (JObject)frame["param"];
                    Schedule(lead + Seconds(frame, "time"), () => Effect(node, primary, recipients));
                }
            }

            if (moved)
            {
                float returnDuration = Mathf.Clamp(actor.GetBattleAnimationDuration("jump_back"), 0.22f, 0.55f);
                Schedule(actionEnd, () =>
                {
                    if (rush) actor.PlayBattleAnimation("jump_back");
                    Move(home, returnDuration, rush ? 0.24f : 0f);
                });
                actionEnd += returnDuration;
            }
            Tick(0f);
            return (impactTime, actionEnd);
        }

        private static IEnumerable<(float At, JObject Node)> ReadCues(JObject graph, JObject[] nodes, JObject main,
            float clipDuration)
        {
            var byId = nodes.ToDictionary(node => (int)node["guid"]);
            var edges = graph["source"]["connections"].OfType<JObject>().ToLookup(edge => (int)edge["from"]);
            var earliest = new Dictionary<int, float>();
            void Visit(JObject node, float at, HashSet<int> path)
            {
                int id = (int)node["guid"];
                if (path.Contains(id) || (earliest.TryGetValue(id, out float old) && old <= at)) return;
                earliest[id] = at;
                path.Add(id);
                foreach (var edge in edges[id])
                {
                    int when = (int?)edge["when"] ?? 0;
                    if (when != 0 && when != 1) continue;
                    float completion = when == 0 ? Completion(node, main, clipDuration) : 0f;
                    Visit(byId[(int)edge["to"]], at + completion + Seconds(edge, "delay"), path);
                }
                path.Remove(id);
            }
            Visit(main, 0f, new HashSet<int>());
            return earliest.OrderBy(cue => cue.Value).ThenBy(cue => cue.Key)
                .Select(cue => (cue.Value, byId[cue.Key]));
        }

        private static float Completion(JObject node, JObject main, float clipDuration)
        {
            if (node == main) return clipDuration;
            if (Kind(node) == "CUTIN") return 0.45f;
            return Seconds(node, "time");
        }

        public void Tick(float delta)
        {
            time += delta;
            if (motion is { } movement && caster)
            {
                float progress = Mathf.Clamp01((time - movement.Start) / movement.Duration);
                float eased = Mathf.SmoothStep(0f, 1f, progress);
                caster.transform.localPosition = Vector3.Lerp(movement.From, movement.To, eased) +
                    Vector3.up * (Mathf.Sin(progress * Mathf.PI) * movement.Arc);
                if (progress >= 1f)
                {
                    caster.transform.localPosition = movement.To;
                    motion = null;
                }
            }
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
            while (pending.Count > 0 && pending[0].At <= time)
            {
                var task = pending[0];
                pending.RemoveAt(0);
                task.Run();
            }
        }

        private void Schedule(float at, Action run)
        {
            int index = pending.FindIndex(item => item.At > at);
            if (index < 0) pending.Add((at, run));
            else pending.Insert(index, (at, run));
        }

        private float Facing(BattlePreviewActor target) =>
            target && target.transform.position.x < caster.transform.position.x ? -1f : 1f;

        private void MoveNear(BattlePreviewActor target, float direction, float duration)
        {
            if (!target || !caster) return;
            Vector3 near = target.transform.position - Vector3.right * (0.9f * direction);
            Move(caster.transform.parent.InverseTransformPoint(near), duration, 0.33f);
        }

        private void Move(Vector3 to, float duration, float arc)
        {
            if (caster) motion = (caster.transform.localPosition, to, time, Mathf.Max(0.01f, duration), arc);
        }

        private void Effect(JObject node, BattlePreviewActor primary, BattlePreviewActor[] targets)
        {
            string source = (string)node["source"];
            if (string.IsNullOrEmpty(source)) return;
            Cfx(source, primary, targets, (int?)node["location"] ?? 5, Number(node, "scale", 1f),
                new Vector2(Number(node, "x"), Number(node, "y")), false);
        }

        private void Cfx(string source, BattlePreviewActor primary, BattlePreviewActor[] targets, int location,
            float scale, Vector2 offset, bool hit, Vector2 spread = default)
        {
            string key = source.StartsWith(EffectPath, StringComparison.Ordinal) ? source : EffectPath + source;
            if (!key.EndsWith(".cfx", StringComparison.Ordinal)) key += ".cfx";
            if (!(Document(key)?["primitive"] is JArray primitives)) return;

            if (location == 42)
            {
                var occupied = targets.Where(target => target).ToArray();
                if (occupied.Length == 0) return;
                Vector3 center = occupied.Aggregate(Vector3.zero, (sum, item) => sum + item.transform.position) / occupied.Length;
                SpawnAt(() => center, occupied[0], false);
                return;
            }
            IEnumerable<BattlePreviewActor> anchors = location switch
            {
                1 => new[] { caster },
                11 => new[] { caster },
                5 => new[] { primary },
                6 => targets,
                _ => Array.Empty<BattlePreviewActor>()
            };
            foreach (var anchor in anchors.Where(item => item))
            {
                bool follow = location == 1 || location == 11;
                Vector3 initial = Position(anchor);
                SpawnAt(() => follow && anchor ? Position(anchor) : initial, anchor, follow);
            }

            Vector3 Position(BattlePreviewActor anchor)
            {
                Vector3 position = anchor.transform.position;
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
                    float side = Facing(primary);
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
            if (!skeletons.TryGetValue(key, out var data))
            {
                var asset = assets[key];
                var pages = asset.pageNames.Zip(asset.pages, (name, page) => (name, page))
                    .ToDictionary(pair => pair.name, pair => pair.page, StringComparer.Ordinal);
                data = new SkeletonPlayerSource(asset.document.text, asset.atlas.text, pages, asset.premultipliedAlpha, asset.scale);
                skeletons.Add(key, data);
            }
            var player = skeletonFactory.Create(data, group);
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
                float lifetime = Seconds(part, "lifeTime", Mathf.Max(0.15f, player.Duration));
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
            active.Add((time + Seconds(part, "lifeTime", 1.8f), delta =>
            {
                follow();
                particles.Tick(delta);
            }, () =>
            {
                particles.Dispose();
                if (group) Object.Destroy(group.gameObject);
            }));
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

        private void Warn(string message)
        {
            if (diagnostics.Add(message)) Debug.LogWarning(message);
        }

        private void EndAction()
        {
            if (caster) caster.transform.localPosition = home;
            motion = null;
            pending.Clear();
            foreach (var visual in active) visual.Dispose();
            active.Clear();
        }

        public void Dispose()
        {
            EndAction();
            if (root) Object.Destroy(root.gameObject);
        }

        private static string Kind(JObject node) => (string)node["etty"];
        private static float Number(JObject node, string key, float fallback = 0f) =>
            float.TryParse(node[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
                float.IsFinite(value) ? value : fallback;
        private static float Seconds(JObject node, string key, float fallback = 0f) =>
            Mathf.Max(0f, Number(node, key, fallback * 1000f) / 1000f);
    }
}
