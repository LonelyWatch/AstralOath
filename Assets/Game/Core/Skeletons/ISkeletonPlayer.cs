using System;
using System.Collections.Generic;
using UnityEngine;
namespace AstralOath.Client.Skeletons
{
    // Textures are borrowed. The caller keeps their asset handles alive until Dispose.
    public sealed class SkeletonPlayerSource
    {
        // Source-owned immutable parse cache. No static references or live playback state.
        internal object ParsedData;
        internal string ExportVersion;
        public string Json { get; }
        public string Atlas { get; }
        public IReadOnlyDictionary<string, Texture2D> Textures { get; }
        public bool PremultipliedAlpha { get; }
        public float Scale { get; }
        public SkeletonPlayerSource(string json, string atlas, IReadOnlyDictionary<string, Texture2D> textures,
            bool premultipliedAlpha = false, float scale = 0.01f)
        {
            Json = json ?? throw new ArgumentNullException(nameof(json));
            Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
            Textures = textures ?? throw new ArgumentNullException(nameof(textures));
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            Scale = scale; PremultipliedAlpha = premultipliedAlpha;
        }
    }
    public sealed class SkeletonAnimationInfo
    {
        public string Name { get; }
        public float Duration { get; }
        public SkeletonAnimationInfo(string name, float duration) { Name = name; Duration = duration; }
    }
    public readonly struct SkeletonBonePose
    {
        public Vector2 LocalPosition { get; }
        public float LocalRotation { get; }
        public Vector2 LocalScale { get; }
        public SkeletonBonePose(Vector2 position, float rotation, Vector2 scale)
        { LocalPosition = position; LocalRotation = rotation; LocalScale = scale; }
    }
    public enum SkeletonPlaybackEventKind { Event, Complete }
    public sealed class SkeletonPlaybackEvent
    {
        public SkeletonPlaybackEventKind Kind { get; }
        public string Name { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public string StringValue { get; }
        public SkeletonPlaybackEvent(SkeletonPlaybackEventKind kind, string name, int intValue = 0,
            float floatValue = 0, string stringValue = null)
        { Kind = kind; Name = name; IntValue = intValue; FloatValue = floatValue; StringValue = stringValue; }
    }
    public interface ISkeletonPlayerFactory
    {
        ISkeletonPlayer Create(SkeletonPlayerSource source, Transform parent);
    }
    // All calls run on the Unity main thread; Tick is driven by the owning Binder's lifetime.
    public interface ISkeletonPlayer : IDisposable
    {
        GameObject Root { get; }
        string Version { get; }
        IReadOnlyList<SkeletonAnimationInfo> Animations { get; }
        IReadOnlyList<string> Skins { get; }
        string CurrentAnimation { get; }
        float Time { get; }
        float Duration { get; }
        bool IsPaused { get; }
        event Action<SkeletonPlaybackEvent> Event;
        void Play(string animation, bool loop, float mixSeconds = 0);
        void SetSkin(string skin);
        void Pause(bool paused);
        void Stop();
        void Tick(float deltaSeconds);
        // Seek cancels blending, evaluates from setup pose and suppresses events.
        void Seek(float seconds);
        void SetFlip(bool x, bool y);
        bool TryGetBonePose(string bone, out SkeletonBonePose pose);
        // Evaluated parent chain in skeleton/mesh space, including source.Scale and runtime flips.
        // Does not include Root.transform; callers convert to Unity world with TransformPoint.
        bool TryGetBoneSkeletonPosition(string bone, out Vector2 position);
    }
}
