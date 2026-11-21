using System;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    internal sealed partial class BattleSkillPresentation
    {
        private Transform graspStage;
        private Vector2 graspPoint;
        private float graspCut;

        private Action BindShotArtwork(string key, string source, ISkeletonPlayer player)
        {
            if (source == "kylon_sk3_2eff_f")
            {
                // The first attack-point key coincides with the cut in the target's displacement rig.
                var keyframe = Document(key)?["animations"]?["animation"]?["bones"]?["ap"]?["translate"]?.First as JObject;
                if (keyframe == null) return null;
                var stage = player.Root.transform;
                graspStage = stage;
                graspCut = Number(keyframe, "time");
                graspPoint = new Vector2(Number(keyframe, "x"), Number(keyframe, "y")) * assets[key].scale;
                return () => { if (graspStage == stage) graspStage = null; };
            }
            if (source != "kylon_sk3_1eff_b") return null;

            const string upper = "rock_ky_3_1_bg2", lower = "rock_ky_3_1_bg1";
            var skin = Document(key)?["skins"]?["default"];
            float HalfWidth(string slot) => ((float?)skin?[slot]?[slot]?["width"] ?? 0f) * assets[key].scale * .5f;
            float upperWidth = HalfWidth(upper), lowerWidth = HalfWidth(lower);
            if (upperWidth <= 0f || lowerWidth <= 0f) return null;
            Vector2 Plate(string name, float width)
            {
                if (!player.TryGetBoneSkeletonPosition(name, out var center) || !player.TryGetBonePose(name, out var pose))
                    return Vector2.zero;
                var body = player.Root.transform;
                float x = body.TransformPoint(center).x;
                float extent = Mathf.Abs(body.lossyScale.x * pose.LocalScale.x) * width;
                return new Vector2(x - extent, x + extent);
            }
            Func<Vector2> range = () =>
            {
                var top = Plate(upper, upperWidth);
                var bottom = Plate(lower, lowerWidth);
                return new Vector2(Mathf.Max(top.x, bottom.x), Mathf.Min(top.y, bottom.y));
            };
            cinematic.OpeningPlateRange = range;
            return () => { if (cinematic.OpeningPlateRange == range) cinematic.OpeningPlateRange = null; };
        }
    }
}
