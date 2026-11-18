using System;
using System.IO;
using UnityEngine;

namespace AstralOath.Client.BattleScene.Editor
{
    public static class SummonFeaturedSkillCapture
    {
        private const int FramesPerSecond = 30;

        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("ASTRAL_SUMMON_SKILL_FRAMES");
            if (string.IsNullOrWhiteSpace(folder)) throw new InvalidOperationException("Frame path required");
            using var preview = new SkillTimelinePreviewSession();
            // c1024.s3 is one_enemy in Config/Tables/battle_skills.csv.
            preview.Load("c1024", "c_iseria_skill_03", null, "c1001");
            var render = preview.Texture;
            var frame = new Texture2D(render.width, render.height, TextureFormat.RGB24, false);
            int count = Mathf.CeilToInt(preview.Duration * FramesPerSecond) + 1;
            try
            {
                for (int index = 0; index < count; ++index)
                {
                    preview.Seek((float)index / FramesPerSecond);
                    preview.Render();
                    var previous = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = render;
                        frame.ReadPixels(new Rect(0, 0, render.width, render.height), 0, 0);
                        frame.Apply(false);
                        File.WriteAllBytes(Path.Combine(folder, $"frame-{index:D4}.png"), frame.EncodeToPNG());
                    }
                    finally { RenderTexture.active = previous; }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(frame); }
            Debug.Log($"Summon featured capture {render.width}x{render.height} {count} frames, {preview.Duration:F3}s; " +
                $"stage {preview.StageDuration:F3}s; cutin {string.Join(", ", preview.Cutins)}; hit frames {string.Join(", ", preview.HitTimes)}; " +
                $"warnings {string.Join("; ", preview.Diagnostics)}");
        }
    }
}