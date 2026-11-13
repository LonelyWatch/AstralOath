using System;
using System.Collections.Generic;
using UnityEngine;
using S = AstralOath.ThirdParty.Spine21;

namespace AstralOath.Client.Skeletons
{
    // Spine 2.1 mixes only channels authored in the incoming clip. A standalone
    // clip in this bridge starts from setup, so its omitted channels must also
    // converge to setup when entered through a mix. Source timelines stay immutable.
    internal sealed class Spine21SetupTimeline
    {
        private readonly float[] rotate, translate, scale, flipX, flipY, color, attachment, ik;
        private readonly Dictionary<S.Attachment, float>[] deform;
        private readonly S.AttachmentTimeline[] attachmentKeys;
        private readonly S.FlipXTimeline[] flipXKeys;
        private readonly S.FlipYTimeline[] flipYKeys;
        private float drawOrder = float.PositiveInfinity;
        private static float[] Missing(int count)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++) values[i] = float.PositiveInfinity;
            return values;
        }
        internal Spine21SetupTimeline(S.SkeletonData data, S.Animation animation)
        {
            int bones = data.Bones.Count, slots = data.Slots.Count;
            rotate = Missing(bones); translate = Missing(bones); scale = Missing(bones);
            flipX = Missing(bones); flipY = Missing(bones); color = Missing(slots);
            attachment = Missing(slots); ik = Missing(data.IkConstraints.Count);
            deform = new Dictionary<S.Attachment, float>[slots];
            attachmentKeys = new S.AttachmentTimeline[slots];
            flipXKeys = new S.FlipXTimeline[bones]; flipYKeys = new S.FlipYTimeline[bones];
            foreach (var timeline in animation.Timelines)
            {
                switch (timeline)
                {
                    case S.RotateTimeline t: rotate[t.BoneIndex] = t.Frames[0]; break;
                    case S.ScaleTimeline t: scale[t.BoneIndex] = t.Frames[0]; break;
                    case S.TranslateTimeline t: translate[t.BoneIndex] = Math.Min(translate[t.BoneIndex], t.Frames[0]); break;
                    case S.FlipYTimeline t: flipY[t.BoneIndex] = t.Frames[0]; flipYKeys[t.BoneIndex] = t; break;
                    case S.FlipXTimeline t: flipX[t.BoneIndex] = t.Frames[0]; flipXKeys[t.BoneIndex] = t; break;
                    case S.ColorTimeline t: color[t.SlotIndex] = t.Frames[0]; break;
                    case S.AttachmentTimeline t: attachment[t.SlotIndex] = t.Frames[0]; attachmentKeys[t.SlotIndex] = t; break;
                    case S.IkConstraintTimeline t: ik[t.IkConstraintIndex] = t.Frames[0]; break;
                    case S.DrawOrderTimeline t: drawOrder = t.Frames[0]; break;
                    case S.FFDTimeline t:
                        var values = deform[t.SlotIndex] ??= new Dictionary<S.Attachment, float>();
                        values[t.Attachment] = t.Frames[0];
                        break;
                }
            }
        }
        internal sealed class Playback : S.Timeline
        {
            private readonly Spine21SetupTimeline defaults;
            private readonly S.Animation source;
            internal readonly S.Animation Animation;
            internal S.TrackEntry Entry;
            internal Playback(Spine21SetupTimeline defaults, S.Animation source)
            {
                this.defaults = defaults; this.source = source;
                // One forwarding timeline avoids copying the source's complete timeline list.
                Animation = new S.Animation(source.Name, new List<S.Timeline>(1) { this }, source.Duration);
            }
            public void Apply(S.Skeleton skeleton, float lastTime, float time, List<S.Event> events, float alpha)
            {
                // Native Animation has already wrapped time/lastTime. The unwrapped
                // owning entry distinguishes first-entry defaults from a later loop,
                // whose late first keys intentionally retain the preceding tail pose.
                bool firstLoop = Entry == null || !Entry.Loop || Entry.Time < source.Duration;
                defaults.Apply(skeleton, time, alpha, firstLoop);
                source.Mix(skeleton, lastTime, time, false, events, alpha);
            }
        }
        private static bool NeedsSetup(float time, float firstKey, bool firstLoop) =>
            float.IsPositiveInfinity(firstKey) || (firstLoop && time < firstKey);

        private void Apply(S.Skeleton skeleton, float time, float alpha, bool firstLoop)
        {
            if (alpha <= 0) return;
            for (int i = 0; i < rotate.Length; i++)
            {
                var bone = skeleton.Bones[i]; var setup = bone.Data;
                if (NeedsSetup(time, rotate[i], firstLoop)) bone.Rotation += Mathf.DeltaAngle(bone.Rotation, setup.Rotation) * alpha;
                if (NeedsSetup(time, translate[i], firstLoop))
                {
                    bone.X += (setup.X - bone.X) * alpha; bone.Y += (setup.Y - bone.Y) * alpha;
                }
                if (NeedsSetup(time, scale[i], firstLoop))
                {
                    bone.ScaleX += (setup.ScaleX - bone.ScaleX) * alpha;
                    bone.ScaleY += (setup.ScaleY - bone.ScaleY) * alpha;
                }
                if (alpha >= .5f)
                {
                    if (NeedsSetup(time, flipX[i], firstLoop)) bone.FlipX = setup.FlipX;
                    if (NeedsSetup(time, flipY[i], firstLoop)) bone.FlipY = setup.FlipY;
                }
                // The previous track can reapply a key at its held last frame. Target
                // flip keys describe persistent state even after their key was crossed.
                if (flipXKeys[i] != null && !NeedsSetup(time, flipX[i], firstLoop))
                    flipXKeys[i].Apply(skeleton, -1, time < flipX[i] ? float.MaxValue : time, null, alpha);
                if (flipYKeys[i] != null && !NeedsSetup(time, flipY[i], firstLoop))
                    flipYKeys[i].Apply(skeleton, -1, time < flipY[i] ? float.MaxValue : time, null, alpha);
            }
            for (int i = 0; i < color.Length; i++)
            {
                var slot = skeleton.Slots[i]; var setup = slot.Data;
                if (NeedsSetup(time, color[i], firstLoop))
                {
                    slot.R += (setup.R - slot.R) * alpha; slot.G += (setup.G - slot.G) * alpha;
                    slot.B += (setup.B - slot.B) * alpha; slot.A += (setup.A - slot.A) * alpha;
                }
                // Attachments, flips and draw order are discrete in the source runtime.
                // Switch omitted channels at the midpoint; never interpolate incompatible topology.
                if (alpha >= .5f && NeedsSetup(time, attachment[i], firstLoop))
                {
                    var target = setup.AttachmentName == null ? null : skeleton.GetAttachment(i, setup.AttachmentName);
                    if (!ReferenceEquals(slot.Attachment, target)) slot.Attachment = target;
                }
                var keyed = attachmentKeys[i];
                if (keyed != null && !NeedsSetup(time, attachment[i], firstLoop))
                {
                    int key = keyed.Frames.Length - 1;
                    if (time >= keyed.Frames[0])
                    {
                        int low = 0, high = keyed.Frames.Length;
                        while (low < high)
                        {
                            int middle = (low + high) / 2;
                            if (keyed.Frames[middle] <= time) low = middle + 1; else high = middle;
                        }
                        key = low - 1;
                    }
                    string name = keyed.AttachmentNames[key];
                    var target = name == null ? null : skeleton.GetAttachment(i, name);
                    // Reassigning an identical attachment would clear FFD and restart
                    // attachment time. The original timeline still handles actual keys
                    // with its original lastTime and therefore keeps source semantics.
                    if (!ReferenceEquals(slot.Attachment, target)) slot.Attachment = target;
                }
                if (slot.AttachmentVerticesCount > 0 &&
                    (slot.Attachment == null || deform[i] == null || !deform[i].TryGetValue(slot.Attachment, out float first) || (firstLoop && time < first)))
                {
                    var vertices = slot.AttachmentVertices;
                    var setupVertices = (slot.Attachment as S.MeshAttachment)?.Vertices;
                    for (int vertex = 0; vertex < slot.AttachmentVerticesCount; vertex++)
                    {
                        float target = setupVertices != null && vertex < setupVertices.Length ? setupVertices[vertex] : 0f;
                        vertices[vertex] += (target - vertices[vertex]) * alpha;
                    }
                    if (alpha >= 1f) slot.AttachmentVerticesCount = 0;
                }
            }
            for (int i = 0; i < ik.Length; i++)
            {
                if (!NeedsSetup(time, ik[i], firstLoop)) continue;
                var constraint = skeleton.IkConstraints[i];
                constraint.Mix += (constraint.Data.Mix - constraint.Mix) * alpha;
                if (alpha >= .5f) constraint.BendDirection = constraint.Data.BendDirection;
            }
            if (alpha >= .5f && NeedsSetup(time, drawOrder, firstLoop))
                for (int i = 0; i < skeleton.Slots.Count; i++) skeleton.DrawOrder[i] = skeleton.Slots[i];
        }
    }
}
