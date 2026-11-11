using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace AstralOath.Client.Skeletons
{
    public sealed class SkeletonPlayerFactory : ISkeletonPlayerFactory
    {
        public ISkeletonPlayer Create(SkeletonPlayerSource source, Transform parent)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            string version;
            using (var reader = new StringReader(source.Json))
            {
                var root = AstralOath.ThirdParty.Spine38.Json.Deserialize(reader) as Dictionary<string, object>;
                if (root == null || !root.TryGetValue("skeleton", out var header) ||
                    !(header is Dictionary<string, object> skeleton) ||
                    !skeleton.TryGetValue("spine", out var value) || !(value is string text))
                    throw new ArgumentException("Skeleton JSON must declare skeleton.spine.", nameof(source));
                version = text;
            }
            // Decide from the actual export header, never a filename or caller-supplied guess.
            SkeletonRuntime runtime;
            if (version == "2.1" || version.StartsWith("2.1.", StringComparison.Ordinal)) runtime = new Spine21Runtime(source);
            else if (version == "3.8" || version.StartsWith("3.8.", StringComparison.Ordinal)) runtime = new Spine38Runtime(source);
            else throw new NotSupportedException("Unsupported Spine export version: " + version);
            try { return new SkeletonPlayer(runtime, source.PremultipliedAlpha, version, parent); }
            catch { runtime.Dispose(); throw; }
        }
    }
    internal abstract class SkeletonRuntime : IDisposable
    {
        internal readonly List<SkeletonAnimationInfo> Animations = new List<SkeletonAnimationInfo>();
        internal readonly List<string> Skins = new List<string>();
        internal readonly Queue<SkeletonPlaybackEvent> Events = new Queue<SkeletonPlaybackEvent>();
        protected float[] vertices = new float[8];
        protected static readonly int[] QuadTriangles = { 0, 1, 2, 2, 3, 0 };
        protected void EnsureVertices(int count) { if (vertices.Length < count) vertices = new float[count]; }
        internal abstract void Play(string name, bool loop, float mix);
        internal abstract void Advance(float delta);
        internal abstract void Seek(float seconds);
        internal abstract void Stop();
        internal abstract void Skin(string name);
        internal abstract void Flip(bool x, bool y);
        internal abstract void Draw(SkeletonMesh target);
        internal abstract bool TryGetBonePose(string bone, out SkeletonBonePose pose);
        public abstract void Dispose();
    }
    internal sealed class SkeletonPlayer : ISkeletonPlayer
    {
        private readonly SkeletonRuntime runtime;
        private readonly SkeletonMesh mesh;
        private bool disposed;
        private bool loop;
        private int playbackRevision;
        public GameObject Root { get; }
        public string Version { get; }
        public IReadOnlyList<SkeletonAnimationInfo> Animations { get; }
        public IReadOnlyList<string> Skins { get; }
        public string CurrentAnimation { get; private set; }
        public float Time { get; private set; }
        public float Duration { get; private set; }
        public bool IsPaused { get; private set; }
        public event Action<SkeletonPlaybackEvent> Event;
        internal SkeletonPlayer(SkeletonRuntime runtime, bool pma, string version, Transform parent)
        {
            this.runtime = runtime; Version = version;
            Animations = runtime.Animations.AsReadOnly(); Skins = runtime.Skins.AsReadOnly();
            Root = new GameObject("Skeleton " + version);
            Root.transform.SetParent(parent, false); Root.layer = parent.gameObject.layer;
            try { mesh = new SkeletonMesh(Root, pma); Redraw(); }
            catch { mesh?.Dispose(); Destroy(Root); throw; }
        }
        public void Play(string animation, bool loop, float mixSeconds = 0)
        {
            Check(); CheckTime(mixSeconds);
            SkeletonAnimationInfo found = null;
            foreach (var item in Animations) if (item.Name == animation) { found = item; break; }
            if (found == null) throw new ArgumentException("Unknown animation: " + animation, nameof(animation));
            playbackRevision++; runtime.Play(animation, loop, mixSeconds);
            this.loop = loop; CurrentAnimation = animation; Duration = found.Duration; Time = 0; IsPaused = false;
            Redraw(); Dispatch();
        }
        public void SetSkin(string skin) { Check(); runtime.Skin(skin); Redraw(); }
        public void SetFlip(bool x, bool y) { Check(); runtime.Flip(x, y); Redraw(); }
        public bool TryGetBonePose(string bone, out SkeletonBonePose pose)
        {
            Check(); pose = default;
            return !string.IsNullOrEmpty(bone) && runtime.TryGetBonePose(bone, out pose);
        }
        public void Pause(bool paused) { Check(); IsPaused = paused; }
        public void Stop()
        {
            Check(); playbackRevision++; runtime.Stop(); runtime.Events.Clear(); CurrentAnimation = null; Time = Duration = 0;
            IsPaused = false; Redraw();
        }
        public void Tick(float deltaSeconds)
        {
            Check(); CheckTime(deltaSeconds);
            if (IsPaused || CurrentAnimation == null || deltaSeconds == 0) return;
            // The runtime owns timeline state; the public clock remains at the last frame on completion.
            Time = loop && Duration > 0 ? (Time + deltaSeconds) % Duration : Mathf.Min(Time + deltaSeconds, Duration);
            runtime.Advance(deltaSeconds); Redraw(); Dispatch();
        }
        public void Seek(float seconds)
        {
            Check(); CheckTime(seconds);
            if (CurrentAnimation == null) throw new InvalidOperationException("Play an animation before seeking.");
            Time = loop && Duration > 0 ? seconds % Duration : Mathf.Min(seconds, Duration);
            playbackRevision++; runtime.Seek(Time); runtime.Events.Clear(); Redraw();
        }
        private void Redraw() { mesh.Clear(); runtime.Draw(mesh); mesh.Commit(); }
        private void Dispatch()
        {
            // Copy before invoking user code: handlers may seek, stop, switch animation or dispose.
            int revision = playbackRevision;
            var pending = runtime.Events.ToArray(); runtime.Events.Clear();
            foreach (var item in pending) { if (disposed || revision != playbackRevision) break; Event?.Invoke(item); }
        }
        private void Check() { if (disposed) throw new ObjectDisposedException(nameof(ISkeletonPlayer)); }
        private static void CheckTime(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value)); }
        public void Dispose()
        {
            if (disposed) return; disposed = true; Event = null; runtime.Dispose(); mesh.Dispose();
            if (Root != null) { Root.SetActive(false); Destroy(Root); }
        }
        internal static void Destroy(UnityEngine.Object value)
        { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
    }
}
