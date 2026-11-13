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
        internal sealed class ParsedSource
        {
            internal S21.SkeletonData Data { get; }
            internal IReadOnlyDictionary<S21.SlotData, SkeletonBlend> ExtendedBlends { get; }
            internal ParsedSource(S21.SkeletonData data, IReadOnlyDictionary<S21.SlotData, SkeletonBlend> extendedBlends)
            {
                Data = data;
                ExtendedBlends = extendedBlends;
            }
        }

        private readonly S21.Skeleton skeleton;
        private readonly S21.AnimationState state;
        private readonly IReadOnlyDictionary<S21.SlotData, SkeletonBlend> extendedBlends;
        private string animation;
        private bool loop;
        private readonly Dictionary<string, Spine21SetupTimeline> completeMixTargets = new Dictionary<string, Spine21SetupTimeline>(StringComparer.Ordinal);
        internal Spine21Runtime(SkeletonPlayerSource source)
        {
            var prepared = Prepare(source);
            var data = prepared.Data;
            extendedBlends = prepared.ExtendedBlends;
            skeleton = new S21.Skeleton(data); skeleton.SetSkin(data.DefaultSkin); state = new S21.AnimationState(new S21.AnimationStateData(data));
            foreach (var item in data.Animations) Animations.Add(new SkeletonAnimationInfo(item.Name, item.Duration));
            foreach (var skin in data.Skins) Skins.Add(skin.Name);
            state.Event += OnEvent; state.Complete += OnComplete;
        }
        internal static ParsedSource Prepare(SkeletonPlayerSource source)
        {
            if (source.ParsedData is ParsedSource cached) return cached;
            S21.Atlas atlas;
            using (var reader = new StringReader(source.Atlas)) atlas = new S21.Atlas(reader, "", new SkeletonTextureLoader(source.Textures));
            try
            {
                S21.SkeletonData data;
                using (var reader = new StringReader(source.Json)) data = new S21.SkeletonJson(atlas) { Scale = source.Scale }.ReadSkeletonData(reader);
                // The complete parser can observe a later duplicate root header. Never cache
                // data for a different version than the factory used to select this runtime.
                if (source.ExportVersion == null || !string.Equals(data.Version, source.ExportVersion, StringComparison.Ordinal))
                    throw new InvalidDataException("Parsed Spine version does not match dispatch header: " +
                        source.ExportVersion + " -> " + data.Version);
                CompleteSkinLookups(data);
                var prepared = new ParsedSource(data, ReadExtendedBlends(source.Json, data));
                source.ParsedData = prepared;
                return prepared;
            }
            finally { atlas.Dispose(); } // Loader borrows textures; parsed attachments retain their atlas regions.
        }
        private static IReadOnlyDictionary<S21.SlotData, SkeletonBlend> ReadExtendedBlends(string json, S21.SkeletonData data)
        {
            // The official 2.1 player exposes only additive. SCSP also carries the two
            // extra blend modes; the converter emits them as named slot metadata.
            if (!json.Contains("\"blend\"")) return null;
            using var reader = new StringReader(json);
            var document = (Dictionary<string, object>)S21.Json.Deserialize(reader);
            if (!document.TryGetValue("slots", out var value)) return null;
            var modes = new Dictionary<S21.SlotData, SkeletonBlend>();
            foreach (Dictionary<string, object> row in (List<object>)value)
            {
                if (!row.TryGetValue("blend", out var blend)) continue;
                var slot = data.FindSlot((string)row["name"]);
                modes.Add(slot, blend switch
                {
                    "normal" => SkeletonBlend.Normal,
                    "additive" => SkeletonBlend.Additive,
                    "multiply" => SkeletonBlend.Multiply,
                    "screen" => SkeletonBlend.Screen,
                    _ => throw new InvalidDataException("Unsupported Spine 2.1 slot blend: " + blend)
                });
            }
            return modes;
        }

        private void OnEvent(S21.AnimationState sender, int track, S21.Event value)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Event, value.Data.Name, value.Int, value.Float, value.String)); }
        private void OnComplete(S21.AnimationState sender, int track, int count)
        { Events.Enqueue(new SkeletonPlaybackEvent(SkeletonPlaybackEventKind.Complete, animation, count)); }
        internal override void Play(string name, bool looping, float mix)
        {
            if (mix == 0)
            {
                // A new unblended clip must not inherit unkeyed attachments, bones or deformation.
                state.ClearTracks(); skeleton.SetToSetupPose();
            }
            var target = skeleton.Data.FindAnimation(name);
            Spine21SetupTimeline.Playback playback = null;
            if (mix > 0)
            {
                if (!completeMixTargets.TryGetValue(name, out var defaults))
                {
                    defaults = new Spine21SetupTimeline(skeleton.Data, target);
                    completeMixTargets.Add(name, defaults);
                }
                playback = new Spine21SetupTimeline.Playback(defaults, target);
                target = playback.Animation;
            }
            // No queued tracks: SetAnimation captures the mix immediately. Using the
            // instance default avoids retaining a dictionary of unique wrapper pairs.
            state.Data.DefaultMix = mix;
            animation = name; loop = looping;
            var entry = state.SetAnimation(0, target, looping);
            if (playback != null) playback.Entry = entry;
            state.Apply(skeleton);
        }
        internal override void Advance(float delta)
        {
            // The public player holds a completed non-looping pose until Play/Stop.
            // Preserve its native track as the next mix source instead of letting 2.1
            // discard it one update after completion. The original completion has
            // already fired; neither held frames nor Seek replay it.
            var current = state.GetCurrent(0);
            if (current != null && !current.Loop && current.LastTime >= current.Animation.Duration)
                current.EndTime = float.PositiveInfinity;
            skeleton.Update(delta); state.Update(delta); state.Apply(skeleton);
        }
        internal override void Seek(float seconds)
        {
            state.ClearTracks(); skeleton.SetToSetupPose();
            // Attachment keys before the target must be applied; the event queue is discarded below.
            var entry = state.SetAnimation(0, animation, loop); entry.Time = seconds; entry.LastTime = -1;
            skeleton.Time = seconds; state.Apply(skeleton); Events.Clear();
        }
        internal override void Stop() { state.ClearTracks(); animation = null; skeleton.SetToSetupPose(); skeleton.Time = 0; }
        // A missing named attachment must remain distinguishable from an animation's explicit null.
        // These per-player lookup entries have no geometry and preserve the original attachment identities.
        private sealed class MissingSkinAttachment : S21.Attachment
        {
            internal MissingSkinAttachment(string name) : base(name) { }
        }
        private static void CompleteSkinLookups(S21.SkeletonData data)
        {
            var skins = new List<S21.Skin>(data.Skins);
            if (data.DefaultSkin == null)
            {
                data.DefaultSkin = new S21.Skin("<runtime-default>");
                skins.Add(data.DefaultSkin); // Internal fallback, not an extra user-selectable skin.
            }
            for (int slot = 0; slot < data.Slots.Count; slot++)
            {
                var names = new List<string>();
                if (data.Slots[slot].AttachmentName != null) names.Add(data.Slots[slot].AttachmentName);
                foreach (var skin in skins) skin.FindNamesForSlot(slot, names);
                foreach (var name in new HashSet<string>(names, StringComparer.Ordinal))
                {
                    var fallback = data.DefaultSkin?.GetAttachment(slot, name) ?? new MissingSkinAttachment(name);
                    foreach (var skin in skins)
                        if (skin.GetAttachment(slot, name) == null) skin.AddAttachment(slot, name, fallback);
                }
            }
        }
        internal override void Skin(string name)
        {
            var selected = skeleton.Data.FindSkin(name);
            if (selected == null) throw new ArgumentException("Skin not found: " + name, nameof(name));
            if (skeleton.Skin == selected) return;
            var attachments = new S21.Attachment[skeleton.Slots.Count];
            var counts = new int[attachments.Length];
            var times = new float[attachments.Length];
            for (int i = 0; i < attachments.Length; i++)
            {
                var slot = skeleton.Slots[i]; attachments[i] = slot.Attachment;
                counts[i] = slot.AttachmentVerticesCount; times[i] = slot.AttachmentTime;
            }
            skeleton.SetSkin(selected);
            var changedMeshes = new List<int>();
            for (int i = 0; i < attachments.Length; i++)
            {
                var slot = skeleton.Slots[i];
                if (slot.Attachment == attachments[i])
                {
                    // Native AttachAll clears FFD even when the fallback object did not change.
                    slot.AttachmentVerticesCount = counts[i]; slot.AttachmentTime = times[i];
                }
                else if (slot.Attachment is S21.MeshAttachment || slot.Attachment is S21.SkinnedMeshAttachment)
                    changedMeshes.Add(i);
            }
            if (changedMeshes.Count == 0) return;
            // A new mesh uses only the current animation/mix (or completed last frame), not retired-track FFD.
            // Evaluate only newly selected meshes off-screen: applying the mix again to the real
            // skeleton would re-blend bones/IK/colors when the previous clip lacks those properties.
            var deformationPose = new S21.Skeleton(skeleton.Data);
            deformationPose.SetSkin(skeleton.Skin);
            for (int i = 0; i < attachments.Length; i++)
                deformationPose.Slots[i].Attachment = skeleton.Slots[i].Attachment;
            if (state.GetCurrent(0) != null) state.Apply(deformationPose);
            else if (animation != null)
            {
                // Non-looping tracks are retired after completion; the player still displays their last pose.
                var completed = skeleton.Data.FindAnimation(animation);
                completed.Apply(deformationPose, -1, completed.Duration, false, null);
            }
            Events.Clear();
            foreach (int i in changedMeshes)
            {
                var from = deformationPose.Slots[i]; var to = skeleton.Slots[i];
                if (from.Attachment != to.Attachment) continue;
                to.AttachmentVertices = from.AttachmentVertices;
                to.AttachmentVerticesCount = from.AttachmentVerticesCount;
            }
        }
        internal override void Flip(bool x, bool y) { skeleton.FlipX = x; skeleton.FlipY = y; }
        internal override bool TryGetBonePose(string name, out SkeletonBonePose pose)
        {
            var bone = skeleton.FindBone(name);
            if (bone == null) { pose = default; return false; }
            pose = new SkeletonBonePose(new Vector2(bone.X, bone.Y), bone.Rotation, new Vector2(bone.ScaleX, bone.ScaleY));
            return true;
        }
        internal override bool TryGetBoneSkeletonPosition(string name, out Vector2 position)
        {
            var bone = skeleton.FindBone(name);
            if (bone == null) { position = default; return false; }
            // Every public pose mutation redraws and evaluates the official world transforms.
            // Reuse that pose so querying an anchor never advances IK or animation state.
            position = new Vector2(bone.WorldX, bone.WorldY);
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
                var blend = slot.Data.AdditiveBlending ? SkeletonBlend.Additive : SkeletonBlend.Normal;
                if (extendedBlends != null && extendedBlends.TryGetValue(slot.Data, out var extended)) blend = extended;
                target.Add((Texture2D)atlasRegion.page.rendererObject, blend, vertices, length, uv,
                    triangles, triangles.Length, light, Color.black);
            }
        }
        public override void Dispose()
        {
            state.Event -= OnEvent; state.Complete -= OnComplete; state.ClearTracks(); Events.Clear(); completeMixTargets.Clear();
        }
    }
}
