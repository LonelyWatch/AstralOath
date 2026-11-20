using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using S = AstralOath.ThirdParty.Spine21;

namespace AstralOath.Client.Skeletons
{
    internal static class Spine21Json
    {
        private const string OrderedTranslateField = "astralOathTranslateTracks";

        private const string OrderedScaleField = "astralOathScaleTracks";
        private const string OrderedSlotColorField = "astralOathSlotColorTracks";

        internal static S.SkeletonData Read(string json, S.Atlas atlas, float scale)
        {
            using var reader = new StringReader(json);
            var data = new S.SkeletonJson(atlas) { Scale = scale }.ReadSkeletonData(reader);
            if (!json.Contains(OrderedTranslateField) && !json.Contains(OrderedSlotColorField) && !json.Contains(OrderedScaleField)) return data;

            using var extensionReader = new StringReader(json);
            var document = (Dictionary<string, object>)S.Json.Deserialize(extensionReader);
            if (!document.TryGetValue("animations", out var value)) return data;
            foreach (var clip in (Dictionary<string, object>)value)
            {
                var fields = (Dictionary<string, object>)clip.Value;
                var animation = data.FindAnimation(clip.Key);
                if (fields.TryGetValue(OrderedTranslateField, out var translations))
                    ReadOrderedBoneTracks(data, animation, translations, scale, false);
                if (fields.TryGetValue(OrderedScaleField, out var scales))
                    ReadOrderedBoneTracks(data, animation, scales, 1f, true);
                if (fields.TryGetValue(OrderedSlotColorField, out var colors))
                    ReadOrderedSlotColors(data, animation, colors);
            }
            return data;
        }

        private static void ReadOrderedBoneTracks(S.SkeletonData data, S.Animation animation, object extension, float scale, bool isScale)
        {
            if (!(extension is Dictionary<string, object> byBone) || byBone.Count == 0)
                throw new InvalidDataException($"Invalid ordered Spine 2.1 bone tracks in {animation.Name}.");

            var baseTracks = new HashSet<int>();
            foreach (var timeline in animation.Timelines)
                if (timeline is S.TranslateTimeline translate && (translate is S.ScaleTimeline) == isScale)
                    baseTracks.Add(translate.BoneIndex);

            // Other 2.1 timelines write independent local fields. Same-bone
            // translate tracks must apply in their original order and weight.
            foreach (var row in byBone)
            {
                string path = animation.Name + "/" + row.Key;
                int bone = data.FindBoneIndex(row.Key);
                if (!baseTracks.Contains(bone) || !(row.Value is List<object> tracks) || tracks.Count == 0)
                    throw new InvalidDataException("Invalid ordered Spine 2.1 bone track group: " + path);

                foreach (var track in tracks)
                {
                    if (!(track is List<object> frames) || frames.Count == 0)
                        throw new InvalidDataException("Empty ordered Spine 2.1 bone track: " + path);
                    S.TranslateTimeline timeline = isScale ? new S.ScaleTimeline(frames.Count) : new S.TranslateTimeline(frames.Count);
                    timeline.BoneIndex = bone;
                    float previous = 0;
                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (!(frames[i] is Dictionary<string, object> frame) ||
                            !frame.TryGetValue("time", out var timeValue))
                            throw new InvalidDataException("Missing Spine 2.1 bone frame time: " + path);
                        float time = Number(timeValue, path);
                        if (time < previous)
                            throw new InvalidDataException("Unordered Spine 2.1 bone frame: " + path);
                        float x = frame.TryGetValue("x", out var xValue) ? Number(xValue, path) : 0;
                        float y = frame.TryGetValue("y", out var yValue) ? Number(yValue, path) : 0;
                        timeline.SetFrame(i, time, Number(x * scale, path), Number(y * scale, path));
                        if (frame.TryGetValue("curve", out var curve))
                            ReadCurve(timeline, i, frames.Count, curve, path);
                        previous = time;
                    }
                    animation.Timelines.Add(timeline);
                    animation.Duration = Math.Max(animation.Duration, previous);
                }
            }
        }

        private static void ReadOrderedSlotColors(S.SkeletonData data, S.Animation animation, object extension)
        {
            if (!(extension is Dictionary<string, object> bySlot) || bySlot.Count == 0)
                throw new InvalidDataException("Invalid ordered color group in " + animation.Name);
            var baseTracks = new HashSet<int>();
            foreach (var timeline in animation.Timelines)
                if (timeline is S.ColorTimeline color) baseTracks.Add(color.SlotIndex);

            foreach (var row in bySlot)
            {
                string path = animation.Name + "/" + row.Key;
                int slot = data.FindSlotIndex(row.Key);
                if (!baseTracks.Contains(slot) || !(row.Value is List<object> tracks) || tracks.Count == 0)
                    throw new InvalidDataException("Invalid ordered slot color track group: " + path);
                foreach (var track in tracks)
                {
                    if (!(track is List<object> frames) || frames.Count == 0)
                        throw new InvalidDataException("Empty ordered slot color track: " + path);
                    var timeline = new S.ColorTimeline(frames.Count) { SlotIndex = slot };
                    float previous = 0f;
                    for (int i = 0; i < frames.Count; i++)
                    {
                        if (!(frames[i] is Dictionary<string, object> frame) ||
                            !frame.TryGetValue("time", out var timeValue) ||
                            !frame.TryGetValue("color", out var value) || !(value is string hex) ||
                            hex.Length != 8 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture, out uint rgba))
                            throw new InvalidDataException("Invalid slot color key: " + path);
                        float time = Number(timeValue, path);
                        if (time < previous) throw new InvalidDataException("Unordered slot color time: " + path);
                        timeline.SetFrame(i, time, (rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f,
                            ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
                        if (frame.TryGetValue("curve", out var curve)) ReadCurve(timeline, i, frames.Count, curve, path);
                        previous = time;
                    }
                    animation.Timelines.Add(timeline);
                    animation.Duration = Math.Max(animation.Duration, previous);
                }
            }
        }

        private static float Number(object value, string path)
        {
            if (value is float number && !float.IsNaN(number) && !float.IsInfinity(number)) return number;
            throw new InvalidDataException("Invalid Spine 2.1 timeline number: " + path);
        }

        private static void ReadCurve(S.CurveTimeline timeline, int frame, int count, object value, string path)
        {
            if (frame < count - 1)
            {
                if (Equals(value, "stepped"))
                {
                    timeline.SetStepped(frame);
                    return;
                }
                if (value is List<object> curve && curve.Count == 4)
                {
                    timeline.SetCurve(frame, Number(curve[0], path), Number(curve[1], path),
                        Number(curve[2], path), Number(curve[3], path));
                    return;
                }
            }
            throw new InvalidDataException("Invalid Spine 2.1 timeline curve: " + path);
        }
    }
}
