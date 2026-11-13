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

        private void PlayNodeAnimation(JObject node, float at, BattlePreviewActor[] recipients)
        {
            bool affectedActors = node["target_node"] == null;
            if (!affectedActors && (int?)node["target_node"] != 5)
            {
                WarnSource($"Unmapped NODE_ANI target_node {node["target_node"]}.");
                return;
            }

            if (affectedActors) cinematic.RevealLayoutTargets();

            string source = NodeAnimationSource(node);
            string bone = (string)node["bone"];
            if (source == null || string.IsNullOrEmpty(bone) || !assets.TryGetValue(source, out var asset) || !asset.document)
            {
                WarnSource($"Missing NODE_ANI skeleton or bone for node {node["guid"]} ({source}).");
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
            active.Add((holdPose ? float.PositiveInfinity : until, UpdatePose, Release));

            void ScheduleReactionTracks(BattlePreviewActor actor, NodeActorBinding binding, JArray frames)
            {
                foreach (string entry in frames.Values<string>())
                {
                    string[] fields = entry?.Split(',');
                    if (fields?.Length != 3 || !TryScalar(fields[0], out float delay) || delay < 0f || fields[2] != "1")
                    {
                        WarnSource($"Unmapped NODE_ANI timeline entry '{entry}'.");
                        continue;
                    }
                    Schedule(at + delay / 1000f, () =>
                    {
                        if (!Owns(binding)) return;
                        var previous = binding.ReleaseReaction;
                        binding.ReleaseReaction = actor.HoldSourceReaction(fields[1]);
                        previous?.Invoke();
                        if (binding.ReleaseReaction == null)
                            WarnSource($"NODE_ANI reaction '{fields[1]}' is absent on '{actor.name}'.");
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
                    var parent = binding.Body.parent;
                    Vector3 origin = parent.TransformPoint(binding.Position);
                    if (screenPivot)
                    {
                        float depth = camera.WorldToViewportPoint(origin).z;
                        origin = camera.ViewportToWorldPoint(new Vector3(.5f, .5f, depth)) + formation[index];
                    }
                    binding.Body.position = origin + translation;
                    binding.Body.localRotation = binding.Rotation * rotation;
                    binding.Body.localScale = Vector3.Scale(binding.Scale, scale);
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
