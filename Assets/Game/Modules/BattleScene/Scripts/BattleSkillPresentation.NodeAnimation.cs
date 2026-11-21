using System;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleSkillPresentation
    {
        private readonly Dictionary<Transform, NodeActorBinding> nodeActorBindings = new Dictionary<Transform, NodeActorBinding>();

        private sealed class NodeActorBinding
        {
            public readonly Transform Body;
            public readonly Vector3 Position;
            public readonly Quaternion Rotation;
            public readonly Vector3 Scale;
            public Action ReleaseReaction;
            public Action<float> AdvanceReaction;

            public NodeActorBinding(Transform body, NodeActorBinding previous)
            {
                Body = body;
                Position = previous?.Position ?? body.localPosition;
                Rotation = previous?.Rotation ?? body.localRotation;
                Scale = previous?.Scale ?? body.localScale;
            }

            public void Restore()
            {
                if (!Body) return;
                Body.localPosition = Position;
                Body.localRotation = Rotation;
                Body.localScale = Scale;
            }
        }

        internal bool OwnsNodeAnimationPose(BattlePreviewActor actor)
        {
            var renderer = actor ? actor.PresentationRenderer : null;
            return renderer && nodeActorBindings.TryGetValue(renderer.transform, out var binding) && binding.ReleaseReaction != null;
        }

        private static string NodeAnimationSource(JObject node)
        {
            string source = (string)node["source"];
            if (string.IsNullOrWhiteSpace(source)) return null;
            if (!source.StartsWith("spani/", StringComparison.Ordinal)) source = "spani/" + source;
            return source.EndsWith(".scsp", StringComparison.Ordinal) ? source : source + ".scsp";
        }

        private float NodeAnimationDuration(JObject node)
        {
            string source = NodeAnimationSource(node);
            return source == null ? 0f : Metadata(source).Duration("animation");
        }

        private string NodeAnimationBone(JObject node, string source)
        {
            if (node["bone"] != null) return (string)node["bone"];
            // The root→bone rigs used for target displacement also occur with an explicit "bone".
            // An omitted name is unambiguous only when that child owns the motion.
            var rig = Document(source);
            var tracks = rig?["animations"]?["animation"]?["bones"];
            if (!(rig?["bones"] is JArray bones) || bones.Count != 2 ||
                (string)bones[0]["name"] != "root" || (string)bones[1]["parent"] != "root" ||
                (string)bones[1]["name"] != "bone" || !(tracks?["bone"] is JObject child)) return null;

            bool Moves(JObject track) => track != null && track.Properties().SelectMany(property =>
                property.Value.Children<JObject>()).Any(key => (float?)key["time"] > 0f);
            return Moves(child) && !Moves(tracks["root"] as JObject) ? "bone" : null;
        }

        private void PlayNodeAnimation(JObject node, float at, BattlePreviewActor[] recipients)
        {
            // r4262 names node 76 "teamtarget" (Alencia) and "node_target" (Cerise).
            // Its transform acts on the target formation, including the gaps between bodies.
            bool targetFormation = (int?)node["target_node"] == 76;
            bool affectedActors = node["target_node"] == null || targetFormation;
            if (!affectedActors && (int?)node["target_node"] != 5)
            {
                WarnSource($"Unmapped NODE_ANI target_node {node["target_node"]}.");
                return;
            }

            if (affectedActors) cinematic.RevealLayoutTargets();

            string source = NodeAnimationSource(node);
            if (source == null || !assets.TryGetValue(source, out var asset) || !asset.document)
            {
                WarnSource($"Missing NODE_ANI skeleton or bone for node {node["guid"]} ({source}).");
                return;
            }
            string bone = NodeAnimationBone(node, source);
            if (string.IsNullOrEmpty(bone))
            {
                WarnSource($"Unresolved NODE_ANI bone for node {node["guid"]} ({source}).");
                return;
            }

            var actors = (affectedActors ? recipients : new[] { caster })
                .Where(actor => actor && (!affectedActors || actor != caster) && actor.PresentationRenderer).ToArray();
            if (actors.Length == 0) return;
            bool screenPivot = (bool?)node["screen_pivot"] ?? false;
            if (screenPivot && !camera)
            {
                WarnSource($"NODE_ANI {node["guid"]} needs a camera for its screen pivot.");
                return;
            }
            if (screenPivot)
                WarnSource($"NODE_ANI {node["guid"]} uses a reconstructed screen-center pivot.");

            ISkeletonPlayer sampler = skeletonFactory.Create(Skeleton(source, asset), root);
            if (!sampler.Animations.Any(clip => clip.Name == "animation" && clip.Duration > 0f))
            {
                sampler.Dispose();
                WarnSource($"NODE_ANI skeleton '{source}' has no playable animation clip.");
                return;
            }
            sampler.Play("animation", false);
            if (!sampler.TryGetBonePose(bone, out _))
            {
                sampler.Dispose();
                WarnSource($"NODE_ANI bone '{bone}' is absent in '{source}'.");
                return;
            }

            float facing = caster ? caster.FacingDirection : 1f;
            float until = time + sampler.Duration;
            bool holdPose = (bool?)node["disable_restore"] ?? false;
            Vector2 offset = new Vector2(Number(node, "offset_x"), Number(node, "offset_y")) * SourcePixel;
            var bindings = new NodeActorBinding[actors.Length];
            var contactTrack = source == "spani/kylon_sk3eff_node.scsp"
                ? Document(source)?["animations"]?["animation"]?["bones"]?[bone]?["translate"]?.Last as JObject : null;
            var contactEnd = contactTrack == null ? Vector2.zero :
                new Vector2(Number(contactTrack, "x"), Number(contactTrack, "y")) * asset.scale;
            var formation = new Vector3[actors.Length];
            Vector3 center = actors.Aggregate(Vector3.zero, (sum, actor) => sum + actor.transform.position) / actors.Length;
            for (int index = 0; index < actors.Length; index++)
            {
                var body = actors[index].PresentationRenderer.transform;
                nodeActorBindings.TryGetValue(body, out var previous);
                previous?.ReleaseReaction?.Invoke();
                var binding = new NodeActorBinding(body, previous);
                bindings[index] = binding;
                nodeActorBindings[body] = binding;
                if (affectedActors && node["timeline"] is JArray frames)
                    ScheduleReactionTracks(actors[index], binding, frames);
                formation[index] = actors[index].transform.position - center;
            }
            UpdatePose(0f);
            active.Add((holdPose ? float.PositiveInfinity : until, UpdatePose, Release, false));

            void ScheduleReactionTracks(BattlePreviewActor actor, NodeActorBinding binding, JArray frames)
            {
                foreach (string entry in frames.Values<string>())
                {
                    string[] fields = entry?.Split(',');
                    if (fields?.Length != 3 || !TryScalar(fields[0], out float delay) || delay < 0f)
                    {
                        WarnSource($"Unmapped NODE_ANI timeline entry '{entry}'.");
                        continue;
                    }
                    string clip = fields[1];
                    float duration = actor.GetBattleAnimationDuration(clip);
                    bool firstFlyingPass = fields[2] == "0" && clip == "flying" && duration > 0f;
                    if (fields[2] != "1" && !(fields[2] == "0" && duration == 0f) && !firstFlyingPass)
                    {
                        WarnSource($"Unmapped NODE_ANI timeline entry '{entry}'.");
                        continue;
                    }
                    Schedule(at + delay / 1000f, () =>
                    {
                        if (!Owns(binding) || actor.IsDefeatPoseActive) return;
                        var previous = binding.ReleaseReaction;
                        binding.ReleaseReaction = actor.HoldSourceReaction(clip);
                        binding.AdvanceReaction = null;
                        previous?.Invoke();
                        if (binding.ReleaseReaction == null)
                            WarnSource($"NODE_ANI reaction '{clip}' is absent on '{actor.name}'.");
                        else if (firstFlyingPass)
                        {
                            // The cue names a moving pose. Only its first pass is established for flag 0.
                            var releasePose = binding.ReleaseReaction;
                            actor.BeginPresentationControl();
                            binding.AdvanceReaction = actor.AdvancePresentation;
                            Action release = () =>
                            {
                                actor.EndPresentationControl();
                                releasePose();
                            };
                            binding.ReleaseReaction = release;
                            Schedule(time + duration, () =>
                            {
                                if (!Owns(binding) || binding.ReleaseReaction != release) return;
                                binding.AdvanceReaction = null;
                                binding.ReleaseReaction = null;
                                release();
                            });
                        }
                    });
                }
            }

            void UpdatePose(float delta)
            {
                if (sampler.Time < sampler.Duration) sampler.Tick(Mathf.Min(delta, sampler.Duration - sampler.Time));
                if (!sampler.TryGetBoneSkeletonPosition(bone, out var position) || !sampler.TryGetBonePose(bone, out var pose)) return;
                var translation = new Vector3((position.x + offset.x) * facing, position.y + offset.y, 0f);
                var rotation = Quaternion.Euler(0f, 0f, pose.LocalRotation * facing);
                var scale = new Vector3(pose.LocalScale.x, pose.LocalScale.y, 1f);
                for (int index = 0; index < bindings.Length; index++)
                {
                    var binding = bindings[index];
                    if (!Owns(binding)) continue;
                    binding.AdvanceReaction?.Invoke(delta);
                    var parent = binding.Body.parent;
                    Vector3 origin = parent.TransformPoint(binding.Position);
                    Vector3 relative = formation[index];
                    Vector3 transformed = targetFormation ? rotation * Vector3.Scale(relative, scale) : relative;
                    binding.Body.position = screenPivot
                        ? cinematic.SourceScreenPoint(origin, translation + transformed)
                        : origin + translation + (transformed - relative);
                    binding.Body.localRotation = binding.Rotation * rotation;
                    binding.Body.localScale = Vector3.Scale(binding.Scale, scale) *
                        (screenPivot ? cinematic.SourceScreenScale : 1f);
                    if (contactTrack != null && graspStage && sampler.Time >= graspCut &&
                        actors[index].TryGetPresentationAnchor("target", out var torso))
                    {
                        Vector2 localContact = graspPoint + position - contactEnd;
                        Vector3 deltaToContact = graspStage.TransformPoint(localContact) - torso;
                        binding.Body.position += new Vector3(deltaToContact.x, deltaToContact.y, 0f);
                    }
                }
            }

            bool Owns(NodeActorBinding binding) => binding.Body &&
                nodeActorBindings.TryGetValue(binding.Body, out var current) && ReferenceEquals(current, binding);

            void Release()
            {
                sampler.Dispose();
                foreach (var binding in bindings)
                {
                    // A later cue inherits the original pose. An older cue may release its sampler,
                    // but must never reset the body owned by that replacement.
                    if (nodeActorBindings.TryGetValue(binding.Body, out var current) && ReferenceEquals(current, binding))
                    {
                        nodeActorBindings.Remove(binding.Body);
                        binding.Restore();
                        binding.ReleaseReaction?.Invoke();
                    }
                }
            }
        }
    }
}
