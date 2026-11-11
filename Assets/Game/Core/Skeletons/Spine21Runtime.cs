using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using S21 = AstralOath.ThirdParty.Spine21;
using S38 = AstralOath.ThirdParty.Spine38;
namespace AstralOath.Client.Skeletons
{
    internal sealed class SkeletonTextureLoader : S21.TextureLoader, S38.TextureLoader
    {
        private readonly IReadOnlyDictionary<string, Texture2D> textures;
        internal SkeletonTextureLoader(IReadOnlyDictionary<string, Texture2D> textures) { this.textures = textures; }
        private Texture2D Find(string name)
        {
            if (!textures.TryGetValue(name, out var value) || value == null)
                throw new ArgumentException("Missing atlas page texture: " + name);
            return value;
        }
        public void Load(S21.AtlasPage page, string path)
        {
            var texture = Find(page.name); page.rendererObject = texture;
            if (page.width == 0) page.width = texture.width;
            if (page.height == 0) page.height = texture.height;
        }
        public void Load(S38.AtlasPage page, string path)
        {
            var texture = Find(page.name); page.rendererObject = texture;
            if (page.width == 0) page.width = texture.width;
            if (page.height == 0) page.height = texture.height;
        }
        public void Unload(object texture) { /* Borrowed asset: released by the caller's BindingScope. */ }
    }
    internal sealed class Spine21Runtime : SkeletonRuntime
    {
        private readonly S21.Atlas atlas;
        private readonly S21.Skeleton skeleton;
        private readonly S21.AnimationState state;
        private string animation;
        private bool loop;
        internal Spine21Runtime(SkeletonPlayerSource source)
        {
            using (var reader = new StringReader(source.Atlas)) atlas = new S21.Atlas(reader, "", new SkeletonTextureLoader(source.Textures));
            try
            {
                S21.SkeletonData data;
                using (var reader = new StringReader(source.Json)) data = new S21.SkeletonJson(atlas) { Scale = source.Scale }.ReadSkeletonData(reader);
                skeleton = new S21.Skeleton(data); state = new S21.AnimationState(new S21.AnimationStateData(data));
                foreach (var item in data.Animations) Animations.Add(new SkeletonAnimationInfo(item.Name, item.Duration));
                foreach (var skin in data.Skins) Skins.Add(skin.Name);
                state.Event += OnEvent; state.Complete += OnComplete;
            }
            catch { atlas.Dispose(); throw; }
        }
        private void OnEvent(S21.AnimationState sender, int track, S21.Event value)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event, value.Data.Name, value.Int, value.Float, value.String)); }
        private void OnComplete(S21.AnimationState sender, int track, int count)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Complete, animation, count)); }
        internal override void Play(string name, bool looping, float mix)
        {
            if (animation != null) state.Data.SetMix(animation, name, mix);
            animation = name; loop = looping; state.SetAnimation(0, name, looping); state.Apply(skeleton);
        }
        internal override void Advance(float delta) { skeleton.Update(delta); state.Update(delta); state.Apply(skeleton); }
        internal override void Seek(float seconds)
        {
            state.ClearTracks(); skeleton.SetToSetupPose();
            var entry = state.SetAnimation(0, animation, loop); entry.Time = seconds; entry.LastTime = seconds;
            skeleton.Time = seconds; state.Apply(skeleton); Events.Clear();
        }
        internal override void Stop() { state.ClearTracks(); animation = null; skeleton.SetToSetupPose(); skeleton.Time = 0; }
        internal override void Skin(string name)
        {
            skeleton.SetSkin(name); skeleton.SetSlotsToSetupPose(); state.Apply(skeleton); Events.Clear();
        }
        internal override void Flip(bool x, bool y) { skeleton.FlipX = x; skeleton.FlipY = y; }
        internal override bool TryGetBonePose(string name, out SkeletonBonePose pose)
        {
            var bone = skeleton.FindBone(name);
            if (bone == null) { pose = default; return false; }
            pose = new SkeletonBonePose(new Vector2(bone.X, bone.Y), bone.Rotation, new Vector2(bone.ScaleX, bone.ScaleY));
            return true;
        }
        internal override void Draw(SkeletonMesh target)
        {
            skeleton.UpdateWorldTransform();
            foreach (var slot in skeleton.DrawOrder)
            {
                float[] uv; int[] triangles; int length; S21.AtlasRegion atlasRegion; Color attachmentColor;
                if (slot.Attachment is S21.RegionAttachment region)
                {
                    length = 8; EnsureVertices(length); region.ComputeWorldVertices(slot.Bone, vertices);
                    uv = region.UVs; triangles = QuadTriangles; atlasRegion = (S21.AtlasRegion)region.RendererObject;
                    attachmentColor = new Color(region.R, region.G, region.B, region.A);
                }
                else if (slot.Attachment is S21.MeshAttachment mesh)
                {
                    length = mesh.UVs.Length; EnsureVertices(length); mesh.ComputeWorldVertices(slot, vertices);
                    uv = mesh.UVs; triangles = mesh.Triangles; atlasRegion = (S21.AtlasRegion)mesh.RendererObject;
                    attachmentColor = new Color(mesh.R, mesh.G, mesh.B, mesh.A);
                }
                else if (slot.Attachment is S21.SkinnedMeshAttachment weighted)
                {
                    length = weighted.UVs.Length; EnsureVertices(length); weighted.ComputeWorldVertices(slot, vertices);
                    uv = weighted.UVs; triangles = weighted.Triangles; atlasRegion = (S21.AtlasRegion)weighted.RendererObject;
                    attachmentColor = new Color(weighted.R, weighted.G, weighted.B, weighted.A);
                }
                else continue; // Bounding boxes have no visible geometry.
                var light = attachmentColor * new Color(slot.R * skeleton.R, slot.G * skeleton.G, slot.B * skeleton.B, slot.A * skeleton.A);
                target.Add((Texture2D)atlasRegion.page.rendererObject, slot.Data.AdditiveBlending ? SkeletonBlend.Additive : SkeletonBlend.Normal,
                    vertices, length, uv, triangles, triangles.Length, light, Color.black);
            }
        }
        public override void Dispose()
        {
            state.Event -= OnEvent; state.Complete -= OnComplete; state.ClearTracks(); Events.Clear(); atlas.Dispose();
        }
    }
}
