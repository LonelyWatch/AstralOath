using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    [DisallowMultipleComponent]
    public sealed class BattlePreviewActor : MonoBehaviour
    {
        [SerializeField, Tooltip("在战斗配表中查找此角色 ID，开始战斗时自动装载模型。")]
        private string heroId;
        [SerializeField] private TextAsset skeleton;
        [SerializeField] private TextAsset atlas;
        [SerializeField] private Texture2D page;
        [SerializeField] private string pageName;
        [SerializeField] private string skin = "default";
        [SerializeField] private bool faceLeft;
        [SerializeField] private int drawOrder;
        [SerializeField] private bool premultipliedAlpha = true;
        [SerializeField] private float sourceScale = .01f;
        private ISkeletonPlayer player;
        private string idleAnimation;
        private bool playingBattleAnimation;
        private object sourceReaction;
        // Recovery is presentation-only: authored hit/return tails do not necessarily
        // end at the battle idle pose. Keep the displayed pose while entering idle.
        private const float IdleRecoveryMixSeconds = .15f;
        private bool paused;
        private int presentationPauseCount;
        private int presentationControlCount;
        private SharedSource sourceLease;

        // Only live actors retain entries. Scene identity prevents independent battles from
        // sharing a lifetime; each player still owns its pose, tracks, mesh and materials.
        private static readonly Dictionary<SourceKey, SharedSource> SharedSources =
            new Dictionary<SourceKey, SharedSource>();

        private readonly struct SourceKey : IEquatable<SourceKey>
        {
            private readonly int scene, skeleton, atlas, page;
            private readonly string pageName;
            private readonly bool premultipliedAlpha;
            private readonly float scale;
            internal SourceKey(BattlePreviewActor actor)
            {
                scene = actor.gameObject.scene.handle;
                skeleton = actor.skeleton.GetInstanceID();
                atlas = actor.atlas.GetInstanceID();
                page = actor.page.GetInstanceID();
                pageName = actor.pageName;
                premultipliedAlpha = actor.premultipliedAlpha;
                scale = actor.sourceScale;
            }
            public bool Equals(SourceKey other) => scene == other.scene && skeleton == other.skeleton &&
                atlas == other.atlas && page == other.page && premultipliedAlpha == other.premultipliedAlpha &&
                scale.Equals(other.scale) && StringComparer.Ordinal.Equals(pageName, other.pageName);
            public override bool Equals(object value) => value is SourceKey other && Equals(other);
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = scene;
                    hash = hash * 397 ^ skeleton; hash = hash * 397 ^ atlas;
                    hash = hash * 397 ^ page;
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(pageName);
                    hash = hash * 397 ^ premultipliedAlpha.GetHashCode();
                    return hash * 397 ^ scale.GetHashCode();
                }
            }
        }

        private sealed class SharedSource
        {
            internal readonly SourceKey Key;
            internal readonly SkeletonPlayerSource Source;
            internal Dictionary<string, Dictionary<string, float>> Events;
            internal int Owners;
            internal SharedSource(SourceKey key, SkeletonPlayerSource source) { Key = key; Source = source; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSharedSources() => SharedSources.Clear();

        private SharedSource AcquireSource()
        {
            var key = new SourceKey(this);
            if (!SharedSources.TryGetValue(key, out var entry))
            {
                var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { pageName, page } };
                var source = new SkeletonPlayerSource(skeleton.text, atlas.text, textures, premultipliedAlpha, sourceScale);
                entry = new SharedSource(key, source);
                SharedSources.Add(key, entry);
            }
            entry.Owners++;
            return entry;
        }

        private void ReleaseSource()
        {
            var entry = sourceLease;
            sourceLease = null;
            if (entry == null) return;
            entry.Owners--;
            // Old players may still own an entry after SubsystemRegistration. Their release
            // must not evict a different entry created with the same asset IDs in the new session.
            if (entry.Owners == 0 && SharedSources.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry))
                SharedSources.Remove(entry.Key);
        }

        internal void UseModel(BattleSkillPresentationAsset model, string skinName, string label)
        {
            if (model == null || !model.document || !model.atlas || model.pages?.Length != 1 ||
                !model.pages[0] || model.pageNames?.Length != 1 || string.IsNullOrEmpty(model.pageNames[0]) ||
                !float.IsFinite(model.scale) || model.scale <= 0f || string.IsNullOrEmpty(skinName))
                throw new ArgumentException("Battle actor requires a complete single-page model: " + label, nameof(model));
            if (skeleton == model.document && atlas == model.atlas && page == model.pages[0] &&
                pageName == model.pageNames[0] && skin == skinName && sourceScale == model.scale &&
                premultipliedAlpha == model.premultipliedAlpha) return;

            bool wasEnabled = enabled;
            enabled = false;
            skeleton = model.document;
            atlas = model.atlas;
            page = model.pages[0];
            pageName = model.pageNames[0];
            skin = skinName;
            sourceScale = model.scale;
            premultipliedAlpha = model.premultipliedAlpha;
            name = label;
            enabled = wasEnabled;
        }

        internal void BeginPresentationControl() => presentationControlCount++;
        internal void EndPresentationControl() => presentationControlCount = Math.Max(0, presentationControlCount - 1);
        internal void AdvancePresentation(float delta)
        {
            if (player == null || paused || presentationPauseCount > 0) return;
            player.Tick(delta);
        }

        public void SetPaused(bool value) => paused = value;
        internal void SetPresentationPaused(bool value) => presentationPauseCount = Math.Max(0, presentationPauseCount + (value ? 1 : -1));
        internal void PreparePresentation()
        {
            if (eventTimes != null) return;
            // The event dictionaries are read-only after construction; no player clock or pose is stored here.
            eventTimes = sourceLease != null
                ? sourceLease.Events ??= BattleSkeletonMetadata.Read(sourceLease.Source.Json, false).Events
                : BattleSkeletonMetadata.Read(skeleton.text, false).Events;
        }
        private Dictionary<string, Dictionary<string, float>> eventTimes;

        internal Renderer PresentationRenderer => player?.Root ? player.Root.GetComponent<Renderer>() : null;

        internal static bool IsBodyVisible(Renderer body) =>
            body && body.enabled && !body.forceRenderingOff && body.gameObject.activeInHierarchy;

        public Bounds Bounds => player != null
            ? player.Root.GetComponent<MeshRenderer>().bounds : new Bounds(transform.position, Vector3.zero);

        internal string HeroId => heroId;
        private bool FacesLeft
        {
            get
            {
                var slot = transform.parent;
                var team = slot ? slot.parent : null;
                if (team && team.parent && team.parent.name == "Units")
                    return team.name == "Enemies";
                return faceLeft;
            }
        }
        internal float FacingDirection => FacesLeft ? -1f : 1f;

        internal Transform PresentationShadow => transform.parent
            ? transform.parent.GetComponentInChildren<SpriteRenderer>(true)?.transform : null;

        internal bool TryGetPresentationAnchor(string anchor, out Vector3 position)
        {
            position = transform.position;
            if (string.IsNullOrEmpty(anchor)) return true;
            if (anchor == "shadow")
            {
                var shadow = PresentationShadow;
                if (!shadow) return false;
                position = shadow.position;
                return true;
            }
            if (player == null || !player.TryGetBoneSkeletonPosition(anchor, out var skeletonPosition)) return false;
            position = player.Root.transform.TransformPoint(skeletonPosition);
            return true;
        }

        internal void PlayBattleIdle()
        {
            if (player == null || !playingBattleAnimation) return;
            playingBattleAnimation = false;
            sourceReaction = null;
            player.Play(idleAnimation, true, IdleRecoveryMixSeconds);
        }

        internal float GetBattleAnimationDuration(string animation)
        {
            if (player != null)
                foreach (var clip in player.Animations)
                    if (clip.Name == animation && clip.Duration > 0f)
                        return clip.Duration;
            return 0f;
        }

        internal float GetBattleAnimationEventTime(string animation, string eventName)
        {
            PreparePresentation();
            return eventTimes.TryGetValue(animation, out var values) && values.TryGetValue(eventName, out float value)
                ? value : -1f;
        }

        public float PlayBattleAnimation(string animation)
        {
            if (player == null) return 0f;
            foreach (var clip in player.Animations)
                if (clip.Name == animation && clip.Duration > 0)
                {
                    sourceReaction = null;
                    player.Play(animation, false);
                    playingBattleAnimation = true;
                    return clip.Duration;
                }
            return 0f;
        }

        internal Action HoldSourceReaction(string animation)
        {
            var instance = player;
            if (instance == null) return null;
            foreach (var clip in instance.Animations)
            {
                if (clip.Name != animation) continue;
                var ownership = new object();
                sourceReaction = ownership;
                instance.Play(animation, false);
                playingBattleAnimation = true;
                return () =>
                {
                    if (player != instance || sourceReaction != ownership) return;
                    sourceReaction = null;
                    if (!paused) PlayBattleIdle();
                };
            }
            return null;
        }

        private void OnEnable()
        {
            OnDisable(); // Also makes a repeated activation safe when editor reload options change.
            // A newly placed generic prefab can have only a Hero ID. The battle host resolves
            // the corresponding catalog model before it creates the simulation.
            if (!skeleton && !atlas && !page) return;
            if (!skeleton || !atlas || !page || string.IsNullOrWhiteSpace(pageName))
                throw new InvalidOperationException($"Battle preview actor {name} has incomplete Spine data.");

            ISkeletonPlayer instance = null;
            try
            {
                sourceLease = AcquireSource();
                instance = new SkeletonPlayerFactory().Create(sourceLease.Source, transform);
                instance.SetSkin(skin);
                string idle = null;
                foreach (var animation in instance.Animations)
                {
                    if (animation.Duration <= 0)
                        continue;
                    if (animation.Name == "b_idle")
                    {
                        idle = animation.Name;
                        break;
                    }
                    if (animation.Name == "idle")
                        idle = animation.Name;
                }
                if (idle == null)
                    throw new InvalidOperationException($"Battle preview actor {name} has no playable idle animation.");

                instance.Root.GetComponent<MeshRenderer>().sortingOrder = drawOrder;
                if (FacesLeft)
                    instance.Root.transform.localScale = new Vector3(-1, 1, 1);
                idleAnimation = idle;
                playingBattleAnimation = false;
                instance.Play(idle, true);
                PreparePresentation();
                player = instance;
            }
            catch
            {
                try { instance?.Dispose(); }
                finally { ReleaseSource(); eventTimes = null; }
                throw;
            }
        }

        private void Update()
        {
            if (player == null || paused || presentationPauseCount > 0 || presentationControlCount > 0) return;
            if (sourceReaction != null && player.Time >= player.Duration) return;
            player.Tick(playingBattleAnimation ? Time.unscaledDeltaTime : Time.deltaTime);
            if (sourceReaction == null && playingBattleAnimation && player.Time >= player.Duration)
                PlayBattleIdle();
        }

        private void OnDisable()
        {
            try { player?.Dispose(); }
            finally
            {
                player = null;
                ReleaseSource();
                eventTimes = null;
                paused = false;
                presentationPauseCount = 0;
                presentationControlCount = 0;
                sourceReaction = null;
            }
        }
    }
}
