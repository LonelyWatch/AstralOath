using System;
using System.IO;
using UnityEngine;
using S = AstralOath.ThirdParty.Spine38;
namespace AstralOath.Client.Skeletons
{
    internal sealed class Spine38Runtime : SkeletonRuntime
    {
        private readonly S.Skeleton skeleton;
        private readonly S.AnimationState state;
        private readonly S.SkeletonClipping clipping = new S.SkeletonClipping();
        private string animation;
        private bool loop;
        internal Spine38Runtime(SkeletonPlayerSource source)
        {
            var data = Prepare(source);
            skeleton = new S.Skeleton(data); state = new S.AnimationState(new S.AnimationStateData(data));
            foreach (var item in data.Animations) Animations.Add(new SkeletonAnimationInfo(item.Name, item.Duration));
            foreach (var skin in data.Skins) Skins.Add(skin.Name);
            state.Event += OnEvent; state.Complete += OnComplete;
        }
        internal static S.SkeletonData Prepare(SkeletonPlayerSource source)
        {
            if (source.ParsedData is S.SkeletonData cached) return cached;
            S.Atlas atlas;
            using (var reader = new StringReader(source.Atlas)) atlas = new S.Atlas(reader, "", new SkeletonTextureLoader(source.Textures));
            try
            {
                // The pinned 3.8 RegionAttachment accepts only the native 0/90 layout.
                // Reject other angles before parsing instead of silently displaying wrong UVs.
                foreach (var region in atlas.Regions)
                    if (region.degrees != 0 && region.degrees != 90)
                        throw new NotSupportedException("Unsupported Spine 3.8 atlas rotation " + region.degrees + " for " + region.name);
                S.SkeletonData data;
                using (var reader = new StringReader(source.Json)) data = new S.SkeletonJson(atlas) { Scale = source.Scale }.ReadSkeletonData(reader);
                source.ParsedData = data;
                return data;
            }
            finally { atlas.Dispose(); } // Loader borrows textures; parsed attachments retain their atlas regions.
        }
        private void OnEvent(S.TrackEntry entry, S.Event value)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event, value.Data.Name, value.Int, value.Float, value.String)); }
        private void OnComplete(S.TrackEntry entry)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Complete, entry.Animation.Name)); }
        internal override void Play(string name, bool looping, float mix)
        {
            animation = name; loop = looping;
            var entry = state.SetAnimation(0, name, looping); entry.MixDuration = mix; state.Apply(skeleton);
        }
        internal override void Advance(float delta) { skeleton.Update(delta); state.Update(delta); state.Apply(skeleton); }
        internal override void Seek(float seconds)
        {
            state.ClearTracks(); skeleton.SetToSetupPose();
            var entry = state.SetAnimation(0, animation, loop);
            entry.TrackTime = seconds; entry.AnimationLast = seconds; entry.MixDuration = 0;
            skeleton.Time = seconds; state.Apply(skeleton); Events.Clear();
        }
        internal override void Stop() { state.ClearTracks(); animation = null; skeleton.SetToSetupPose(); skeleton.Time = 0; }
        internal override void Skin(string name)
        { skeleton.SetSkin(name); skeleton.SetSlotsToSetupPose(); state.Apply(skeleton); Events.Clear(); }
        internal override void Flip(bool x, bool y) { skeleton.ScaleX = x ? -1 : 1; skeleton.ScaleY = y ? -1 : 1; }
        internal override bool TryGetBonePose(string name, out SkeletonBonePose pose)
        {
            var bone = skeleton.FindBone(name);
            if (bone == null) { pose = default; return false; }
            pose = new SkeletonBonePose(new Vector2(bone.X, bone.Y), bone.Rotation, new Vector2(bone.ScaleX, bone.ScaleY));
            return true;
        }
        internal override void Draw(SkeletonMesh target)
        {
            skeleton.UpdateWorldTransform(); clipping.ClipEnd();
            try
            {
                foreach (var slot in skeleton.DrawOrder)
                {
                    if (!slot.Bone.Active) { clipping.ClipEnd(slot); continue; }
                    if (slot.Attachment is S.ClippingAttachment clip) { clipping.ClipStart(slot, clip); continue; }
                    float[] uv; int[] triangles; int length; S.AtlasRegion atlasRegion; Color attachmentColor;
                    if (slot.Attachment is S.RegionAttachment region)
                    {
                        length = 8; EnsureVertices(length); region.ComputeWorldVertices(slot.Bone, vertices, 0);
                        uv = region.UVs; triangles = QuadTriangles; atlasRegion = (S.AtlasRegion)region.RendererObject;
                        attachmentColor = new Color(region.R, region.G, region.B, region.A);
                    }
                    else if (slot.Attachment is S.MeshAttachment mesh)
                    {
                        length = mesh.WorldVerticesLength; EnsureVertices(length); mesh.ComputeWorldVertices(slot, 0, length, vertices, 0);
                        uv = mesh.UVs; triangles = mesh.Triangles; atlasRegion = (S.AtlasRegion)mesh.RendererObject;
                        attachmentColor = new Color(mesh.R, mesh.G, mesh.B, mesh.A);
                    }
                    else { clipping.ClipEnd(slot); continue; } // Paths, points and bounding boxes are non-rendering.
                    var light = attachmentColor * new Color(slot.R * skeleton.R, slot.G * skeleton.G, slot.B * skeleton.B, slot.A * skeleton.A);
                    var dark = slot.HasSecondColor ? new Color(slot.R2, slot.G2, slot.B2) : Color.black;
                    var texture = (Texture2D)atlasRegion.page.rendererObject;
                    var blend = Blend(slot.Data.BlendMode);
                    if (clipping.IsClipping)
                    {
                        clipping.ClipTriangles(vertices, length, triangles, triangles.Length, uv);
                        target.Add(texture, blend, clipping.ClippedVertices.Items, clipping.ClippedVertices.Count,
                            clipping.ClippedUVs.Items, clipping.ClippedTriangles.Items, clipping.ClippedTriangles.Count, light, dark);
                    }
                    else target.Add(texture, blend, vertices, length, uv, triangles, triangles.Length, light, dark);
                    clipping.ClipEnd(slot);
                }
            }
            finally { clipping.ClipEnd(); }
        }
        private static SkeletonBlend Blend(S.BlendMode mode)
        {
            switch (mode)
            {
                case S.BlendMode.Normal: return SkeletonBlend.Normal;
                case S.BlendMode.Additive: return SkeletonBlend.Additive;
                case S.BlendMode.Multiply: return SkeletonBlend.Multiply;
                case S.BlendMode.Screen: return SkeletonBlend.Screen;
                default: throw new NotSupportedException("Unknown Spine slot blend mode: " + mode);
            }
        }
        public override void Dispose()
        { state.Event -= OnEvent; state.Complete -= OnComplete; state.ClearTracks(); Events.Clear(); clipping.ClipEnd(); }
    }
}
