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
            internal float Start, FadeSeconds, Opacity;
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
            (int?)node["blend_target"] == 66 && Number(node, "alpha_channel", 1f) == 0f &&
            Number(node, "duration") == -1f && Number(node, "fadeIn") >= 0f && Number(node, "fadeOut") == 0f &&
            !string.IsNullOrEmpty((string)node["id"]);

        private void HoldTargetLayer(JObject node, BattlePreviewActor[] recipients)
        {
            string id = (string)node["id"];
            if (targetLayers.ContainsKey(id)) return;
            var layer = new TargetLayer
            {
                Bodies = recipients.Where(actor => actor && actor.PresentationRenderer)
                    .Select(actor => actor.PresentationRenderer).Distinct().ToArray(),
                Start = time,
                FadeSeconds = Seconds(node, "fadeIn")
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
        }

        private void AdvanceTargetLayers()
        {
            foreach (var layer in targetLayers.Values)
            {
                if (layer.Opacity <= 0f) continue;
                layer.Opacity = 1f - Mathf.Clamp01((time - layer.Start) / layer.FadeSeconds);
                foreach (var renderer in layer.Bodies)
                    if (renderer) ApplyTargetLayers(renderer, targetBodyHolds[renderer]);
            }
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
