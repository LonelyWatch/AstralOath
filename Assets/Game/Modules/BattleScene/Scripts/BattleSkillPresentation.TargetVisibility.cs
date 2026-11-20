using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleSkillPresentation
    {
        private readonly Dictionary<string, Renderer[]> targetLayers = new Dictionary<string, Renderer[]>();
        // A dual-attack successor can start while the previous action still owns its recovery.
        private readonly Dictionary<Renderer, (int Count, bool WasHidden)> targetBodyHolds;

        private static bool IsImmediateTargetHide(JObject node) =>
            (int?)node["blend_target"] == 66 && Number(node, "alpha_channel", 1f) == 0f &&
            Number(node, "duration") == -1f && Number(node, "fadeIn") == 0f && Number(node, "fadeOut") == 0f &&
            !string.IsNullOrEmpty((string)node["id"]);

        private void HoldTargetLayer(string id, BattlePreviewActor[] recipients)
        {
            if (targetLayers.ContainsKey(id)) return;
            var bodies = recipients.Where(actor => actor && actor.PresentationRenderer)
                .Select(actor => actor.PresentationRenderer).ToArray();
            targetLayers.Add(id, bodies);
            foreach (var body in bodies)
            {
                bool held = targetBodyHolds.TryGetValue(body, out var previous);
                targetBodyHolds[body] = (previous.Count + 1, held ? previous.WasHidden : body.forceRenderingOff);
                body.forceRenderingOff = true;
            }
        }

        private void ReleaseTargetLayer(string id)
        {
            if (id == null || !targetLayers.TryGetValue(id, out var bodies)) return;
            targetLayers.Remove(id);
            foreach (var body in bodies)
            {
                var hold = targetBodyHolds[body];
                if (hold.Count > 1) targetBodyHolds[body] = (hold.Count - 1, hold.WasHidden);
                else
                {
                    targetBodyHolds.Remove(body);
                    if (body) body.forceRenderingOff = hold.WasHidden;
                }
            }
        }
    }
}
