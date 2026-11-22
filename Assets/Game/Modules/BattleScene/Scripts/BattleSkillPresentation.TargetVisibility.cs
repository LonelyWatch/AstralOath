using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleSkillPresentation
    {
        private sealed class TargetLayer
        {
            internal Renderer[] Bodies;
            internal float Start, FadeSeconds, HoldSeconds, FadeOutSeconds, Opacity;
            internal float? ReleaseStart;
            internal float ReleaseOpacity;
        }

        private sealed class TargetBody
        {
            internal readonly List<TargetLayer> Layers = new List<TargetLayer>();
            internal bool WasHidden;
            internal float OriginalOpacity;
        }

        private static readonly int SkillBodyOpacity = Shader.PropertyToID("_SkillBodyOpacity");
        private readonly MaterialPropertyBlock skillBodyBlock = new MaterialPropertyBlock();
        private readonly Dictionary<string, TargetLayer> targetLayers = new Dictionary<string, TargetLayer>();
        // A dual-attack successor can start while the previous action still owns its recovery.
        private readonly Dictionary<Renderer, TargetBody> targetBodyHolds;

        private static bool IsTargetHide(JObject node) =>
            (int?)node["blend_target"] is 55 or 66 && Number(node, "alpha_channel", 1f) == 0f &&
            Number(node, "fadeIn") >= 0f && Number(node, "fadeOut") >= 0f &&
            ((Number(node, "duration") == -1f && ((int?)node["blend_target"] == 55 || Number(node, "fadeOut") == 0f) &&
                !string.IsNullOrEmpty((string)node["id"])) ||
             (Number(node, "duration") > 0f && string.IsNullOrEmpty((string)node["id"])));

        private static bool IsCasterHideUntilIdle(JObject node, float at,
            (float At, JObject Node, float Duration, JObject[] Modulators)[] cues)
        {
            string id = (string)node["id"];
            if (node["blend_target"] != null || node["category"] != null || node["color"] != null ||
                string.IsNullOrEmpty(id) || Number(node, "alpha_channel", 1f) != 0f ||
                Number(node, "duration") != -1f || Number(node, "fadeIn") != 0f || Number(node, "fadeOut") != 0f)
                return false;
            // Diene's sky cut hides her body until the neutral pose and its matching STOP share a frame.
            return cues.Any(stop => stop.At >= at && Kind(stop.Node) == "STOP" &&
                (string)stop.Node["targetId"] == id && Number(stop.Node, "time") == 0f &&
                cues.Any(idle => idle.At == stop.At && Kind(idle.Node) == "IDLE"));
        }

        private void HoldTargetLayer(JObject node, BattlePreviewActor[] recipients)
        {
            string id = (string)node["id"];
            if (string.IsNullOrEmpty(id)) id = "\0" + (int)node["guid"];
            if (targetLayers.ContainsKey(id)) return;
            // The fixed graphs explicitly call 55 "cb_without_self" and 66 "target_hide".
            IEnumerable<BattlePreviewActor> affected = (int?)node["blend_target"] switch
            {
                null => new[] { caster },
                55 => participants.Where(actor => actor != caster),
                _ => recipients
            };
            var layer = new TargetLayer
            {
                Bodies = affected.Where(actor => actor && actor.PresentationRenderer)
                    .Select(actor => actor.PresentationRenderer).Distinct().ToArray(),
                Start = time,
                FadeSeconds = Seconds(node, "fadeIn"),
                HoldSeconds = Number(node, "duration") / 1000f,
                FadeOutSeconds = Seconds(node, "fadeOut")
            };
            layer.Opacity = layer.FadeSeconds > 0f ? 1f : 0f;
            targetLayers.Add(id, layer);
            foreach (var renderer in layer.Bodies)
            {
                if (!targetBodyHolds.TryGetValue(renderer, out var body))
                {
                    renderer.GetPropertyBlock(skillBodyBlock);
                    body = new TargetBody
                    {
                        WasHidden = renderer.forceRenderingOff,
                        OriginalOpacity = skillBodyBlock.HasFloat(SkillBodyOpacity) ? skillBodyBlock.GetFloat(SkillBodyOpacity) : 1f
                    };
                    targetBodyHolds.Add(renderer, body);
                }
                body.Layers.Add(layer);
                ApplyTargetLayers(renderer, body);
            }
            if (layer.HoldSeconds > 0f)
                Schedule(time + layer.HoldSeconds + layer.FadeOutSeconds, () => ReleaseTargetLayer(id));
        }

        private void AdvanceTargetLayers()
        {
            foreach (var layer in targetLayers.Values)
            {
                float elapsed = time - layer.Start;
                float opacity = layer.FadeSeconds > 0f ? 1f - Mathf.Clamp01(elapsed / layer.FadeSeconds) : 0f;
                if (layer.HoldSeconds > 0f && elapsed >= layer.HoldSeconds)
                    opacity = layer.FadeOutSeconds > 0f ? Mathf.Clamp01((elapsed - layer.HoldSeconds) / layer.FadeOutSeconds) : 1f;
                if (layer.ReleaseStart is float start)
                    opacity = Mathf.Lerp(layer.ReleaseOpacity, 1f, (time - start) / layer.FadeOutSeconds);
                if (opacity == layer.Opacity) continue;
                layer.Opacity = opacity;
                foreach (var renderer in layer.Bodies)
                    if (renderer) ApplyTargetLayers(renderer, targetBodyHolds[renderer]);
            }
        }

        private void StopTargetLayer(JObject stop)
        {
            string id = (string)stop["targetId"];
            if (id == null || !targetLayers.TryGetValue(id, out var layer)) return;
            float duration = Seconds(stop, "time");
            if (duration <= 0f) { ReleaseTargetLayer(id); return; }
            if (layer.ReleaseStart.HasValue) return;
            layer.ReleaseStart = time;
            layer.ReleaseOpacity = layer.Opacity;
            layer.FadeOutSeconds = duration;
            Schedule(time + duration, () =>
            {
                if (targetLayers.TryGetValue(id, out var current) && current == layer) ReleaseTargetLayer(id);
            });
        }

        private void ApplyTargetLayers(Renderer renderer, TargetBody body)
        {
            float opacity = 1f;
            foreach (var layer in body.Layers) opacity = Mathf.Min(opacity, layer.Opacity);
            renderer.forceRenderingOff = body.WasHidden || opacity <= 0f;
            renderer.GetPropertyBlock(skillBodyBlock);
            skillBodyBlock.SetFloat(SkillBodyOpacity, body.OriginalOpacity * opacity);
            renderer.SetPropertyBlock(skillBodyBlock);
        }

        private void ReleaseTargetLayer(string id)
        {
            if (id == null || !targetLayers.TryGetValue(id, out var layer)) return;
            targetLayers.Remove(id);
            foreach (var renderer in layer.Bodies)
            {
                var body = targetBodyHolds[renderer];
                body.Layers.Remove(layer);
                if (renderer) ApplyTargetLayers(renderer, body);
                if (body.Layers.Count == 0) targetBodyHolds.Remove(renderer);
            }
        }
    }
}
