using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Skeletons;
using AstralOath.Shared.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        [SerializeField] private Transform shadow;
        [SerializeField] private Vector2 shadowSizeMultiplier = Vector2.one;
        [SerializeField] private Vector3 shadowOffset;
        private bool shadowNormalized;
        private ISkeletonPlayer player;
        private float battleDisplayScale = 1f;
        private string resolvedHeroId;
        private string idleAnimation;
        private string turnIdleAnimation;
        private string turnEntryAnimation;
        private string incapacitatedAnimation;
        private bool turnReady, turnEntryPending, actionBlocked;
        private bool playingBattleAnimation;
        private bool defeatPose;
        private object sourceReaction;
        // Recovery is presentation-only: authored hit/return tails do not necessarily
        // end at the battle idle pose. Keep the displayed pose while entering idle.
        private const float IdleRecoveryMixSeconds = .15f;
        private bool paused;
        private bool externallyClocked;
        private int presentationPauseCount;
        private int presentationControlCount;
        private SharedSource sourceLease, warmedSource;
        private BattleHudPreview.ModelForm baseForm;
        private BattleHudPreview.ModelForm[] forms = Array.Empty<BattleHudPreview.ModelForm>();

        // Only live actors retain entries. Scene identity prevents independent battles from
        // sharing a lifetime; each player still owns its pose, tracks, mesh and materials.
        private static readonly Dictionary<SourceKey, SharedSource> SharedSources =
            new Dictionary<SourceKey, SharedSource>();

        internal readonly struct SourceKey : IEquatable<SourceKey>
        {
            private readonly int scene, skeleton, atlas, page;
            private readonly string pageName;
            private readonly bool premultipliedAlpha;
            private readonly float scale;
            internal SourceKey(BattlePreviewActor actor)
                : this(actor.gameObject.scene, actor.skeleton, actor.atlas, actor.page, actor.pageName,
                    actor.premultipliedAlpha, actor.sourceScale) { }

            internal SourceKey(Scene scene, TextAsset skeleton, TextAsset atlas, Texture2D page, string pageName,
                bool premultipliedAlpha, float scale)
            {
                this.scene = scene.handle;
                this.skeleton = skeleton.GetInstanceID();
                this.atlas = atlas.GetInstanceID();
                this.page = page.GetInstanceID();
                this.pageName = pageName;
                this.premultipliedAlpha = premultipliedAlpha;
                this.scale = scale;
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

        internal sealed class SharedSource
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
            var prepared = warmedSource;
            warmedSource = null;
            if (!SharedSources.TryGetValue(key, out var entry))
            {
                entry = prepared?.Key.Equals(key) == true ? prepared : new SharedSource(key,
                    CreateModelSource(skeleton, atlas, page, pageName, premultipliedAlpha, sourceScale));
                SharedSources.Add(key, entry);
            }
            entry.Owners++;
            return entry;
        }

        private static SkeletonPlayerSource CreateModelSource(TextAsset skeleton, TextAsset atlas, Texture2D page,
            string pageName, bool premultipliedAlpha, float scale)
        {
            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { pageName, page } };
            return new SkeletonPlayerSource(skeleton.text, atlas.text, textures, premultipliedAlpha, scale);
        }

        internal static async Task<SharedSource> WarmModelAsync(Scene scene, BattleSkillPresentationAsset model, string skin,
            string label, SemaphoreSlim workers, CancellationToken token)
        {
            await workers.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                ValidateModel(model, skin, label);
                var key = new SourceKey(scene, model.document, model.atlas, model.pages[0], model.pageNames[0],
                    model.premultipliedAlpha, model.scale);
                var source = CreateModelSource(model.document, model.atlas, model.pages[0], model.pageNames[0],
                    model.premultipliedAlpha, model.scale);
                await new SkeletonPlayerFactory().PrepareAsync(source, token);
                var events = await Task.Run(() => BattleSkeletonMetadata.Read(source.Json, false).Events, token);
                token.ThrowIfCancellationRequested();
                return new SharedSource(key, source) { Events = events };
            }
            finally { workers.Release(); }
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

        private static void ValidateModel(BattleSkillPresentationAsset model, string skinName, string label)
        {
            if (model == null || !model.document || !model.atlas || model.pages?.Length != 1 ||
                !model.pages[0] || model.pageNames?.Length != 1 || string.IsNullOrEmpty(model.pageNames[0]) ||
                !float.IsFinite(model.scale) || model.scale <= 0f || string.IsNullOrEmpty(skinName))
                throw new ArgumentException("Battle actor requires a complete single-page model: " + label, nameof(model));
        }

        internal void UseModel(BattleSkillPresentationAsset model, string skinName, string label) =>
            UseBattleModel(model, skinName, label, null);

        internal void UseBattleModel(BattleSkillPresentationAsset model, string skinName, string label, string definitionId,
            SharedSource prepared = null)
        {
            ValidateModel(model, skinName, label);
            baseForm = null;
            forms = Array.Empty<BattleHudPreview.ModelForm>();
            resolvedHeroId = definitionId;
            warmedSource = prepared;
            if (skeleton == model.document && atlas == model.atlas && page == model.pages[0] &&
                pageName == model.pageNames[0] && skin == skinName && sourceScale == model.scale &&
                premultipliedAlpha == model.premultipliedAlpha) return;

            bool wasEnabled = enabled;
            enabled = false;
            AssignModel(model, skinName);
            name = label;
            enabled = wasEnabled;
        }

        private void AssignModel(BattleSkillPresentationAsset model, string skinName)
        {
            skeleton = model.document;
            atlas = model.atlas;
            page = model.pages[0];
            pageName = model.pageNames[0];
            skin = skinName;
            sourceScale = model.scale;
            premultipliedAlpha = model.premultipliedAlpha;
        }

        internal void ConfigureForms(BattleSkillPresentationAsset ordinary, string normalSkin, BattleHudPreview.ModelForm[] variants)
        {
            if (variants == null || variants.Length == 0) return;
            forms = variants;
            baseForm = new BattleHudPreview.ModelForm { skeleton = ordinary, skin = normalSkin, prepared = sourceLease };
            foreach (var form in forms)
            {
                if (form.prepared != null) continue;
                var model = form.skeleton;
                ValidateModel(model, form.skin, name);
                var key = new SourceKey(gameObject.scene, model.document, model.atlas, model.pages[0],
                    model.pageNames[0], model.premultipliedAlpha, model.scale);
                var source = CreateModelSource(model.document, model.atlas, model.pages[0], model.pageNames[0],
                    model.premultipliedAlpha, model.scale);
                new SkeletonPlayerFactory().Prepare(source);
                form.prepared = new SharedSource(key, source);
            }
        }

        private BattleHudPreview.ModelForm GraphForm(string id) =>
            forms.FirstOrDefault(form => Array.IndexOf(form.graphs, id) >= 0) ?? baseForm;

        internal BattleSkeletonMetadata GraphMetadata(string id)
        {
            var form = GraphForm(id);
            if (form == null) return null;
            return form.metadata ??= BattleSkeletonMetadata.Read(form.skeleton.document.text);
        }

        internal void UseGraphForm(string id)
        {
            var form = GraphForm(id);
            if (form != null) SwapBody(form.skeleton, form.skin, form.prepared);
        }

        internal void ReconcileForm(BattleUnitSnapshot state)
        {
            if (baseForm == null) return;
            var form = forms.FirstOrDefault(item => Enum.TryParse<BattleStatusKind>(item.status, out var kind) &&
                state.GetStatus(kind) != null) ?? baseForm;
            SwapBody(form.skeleton, form.skin, form.prepared);
        }

        internal void UseSourceBody(BattleSkillPresentationAsset model)
        {
            var form = forms.FirstOrDefault(candidate => candidate.skeleton.document == model.document);
            SwapBody(model, form?.skin ?? skin, form?.prepared);
        }

        private void SwapBody(BattleSkillPresentationAsset model, string skinName, SharedSource prepared)
        {
            if (skeleton == model.document && skin == skinName) return;
            ValidateModel(model, skinName, name);
            bool loaded = player != null;
            if (loaded) ReleasePlayer();
            AssignModel(model, skinName);
            warmedSource = prepared;
            if (!loaded) return;
            InitializePlayer();
            ApplyTurnIdleIfFree();
        }

        internal void BeginArrivalRun()
        {
            BeginPresentationControl();
            if (GetBattleAnimationDuration("run") > 0f)
                player.Play("run", true);
        }

        internal void EndArrivalPose()
        {
            PlayIdleLoop();
            EndPresentationControl();
        }

        internal void BeginPresentationControl() => presentationControlCount++;
        internal void EndPresentationControl()
        {
            presentationControlCount = Math.Max(0, presentationControlCount - 1);
            ApplyTurnIdleIfFree();
        }

        internal void AdvancePresentation(float delta)
        {
            if (player == null || paused || defeatPose || presentationPauseCount > 0) return;
            player.Tick(delta);
        }

        internal bool IsDefeatPoseActive => defeatPose;

        internal void BeginDefeatPose()
        {
            if (!playingBattleAnimation) PlayBattleAnimation("attacked");
            defeatPose = true;
        }

        internal void TickDefeatPose(float delta)
        {
            // Advance the accepted hit pose only while the body is darkening.
            if (delta > 0f && defeatPose && playingBattleAnimation && player != null && player.Time < player.Duration)
                player.Tick(Mathf.Min(delta, player.Duration - player.Time));
        }

        internal void EndDefeatPose()
        {
            if (!defeatPose) return;
            defeatPose = false;
            PlayBattleIdle();
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (!value) ApplyTurnIdleIfFree();
        }

        internal void SetPresentationPaused(bool value)
        {
            presentationPauseCount = Math.Max(0, presentationPauseCount + (value ? 1 : -1));
            if (!value) ApplyTurnIdleIfFree();
        }

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

        internal void SetBattleDisplayScale(float value)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value));
            battleDisplayScale = value;
            if (player != null) ApplyBattleDisplayScale(player);
        }

        private void ApplyBattleDisplayScale(ISkeletonPlayer model) =>
            model.Root.transform.localScale = new Vector3(FacingDirection * battleDisplayScale, battleDisplayScale, 1f);

        internal string HeroId => resolvedHeroId ?? heroId;
        internal string AuthoredHeroId => heroId;
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
        internal int PresentationDrawOrder => drawOrder;
        internal event Action<BattlePreviewActor, SkeletonPlaybackEvent> TurnEntryCue;

        internal Transform PresentationShadow
        {
            get
            {
                if (shadow) return shadow;
                // Older battle scenes and source playback fixtures authored the shadow on the slot.
                var slot = transform.parent;
                if (slot)
                    foreach (Transform child in slot)
                        if (child.TryGetComponent<SpriteRenderer>(out _))
                            return child;
                return null;
            }
        }

        private void NormalizeShadow()
        {
            if (!shadow || shadowNormalized) return;
            Vector3 scale = transform.localScale;
            if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) ||
                Mathf.Approximately(scale.z, 0f)) return;
            // The common shadow is authored at slot size. The hero's root scale fits its model
            // to the formation, so compensate once when the instance begins presentation.
            shadow.localPosition = Matrix4x4.TRS(Vector3.zero, transform.localRotation, scale)
                .inverse.MultiplyPoint3x4(shadow.localPosition + shadowOffset);
            shadow.localScale = Vector3.Scale(shadow.localScale, new Vector3(
                shadowSizeMultiplier.x / Mathf.Abs(scale.x),
                shadowSizeMultiplier.y / Mathf.Abs(scale.y), 1f / Mathf.Abs(scale.z)));
            shadowNormalized = true;
        }

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
            if (!TryGetSkeletonAnchor(anchor, out var skeletonPosition)) return false;
            position = player.Root.transform.TransformPoint(skeletonPosition);
            return true;
        }

        internal bool TryGetSkeletonAnchor(string anchor, out Vector2 position)
        {
            position = default;
            return player != null && player.TryGetBoneSkeletonPosition(anchor, out position);
        }

        private bool PlayingTurnEntry => turnEntryAnimation != null && player.CurrentAnimation == turnEntryAnimation;
        internal bool IsTurnEntryPlaying => player != null &&
            (turnEntryPending || PlayingTurnEntry && player.Time < player.Duration);

        internal void SetTurnReady(bool value)
        {
            if (turnReady == value) return;
            turnReady = value;
            turnEntryPending = value && !actionBlocked && turnEntryAnimation != null;
            ApplyTurnIdleIfFree();
        }

        internal void SetActionBlocked(bool value)
        {
            if (actionBlocked == value) return;
            actionBlocked = value;
            if (value) turnEntryPending = false;
            ApplyTurnIdleIfFree();
        }

        private void ApplyTurnIdleIfFree()
        {
            if (player == null || playingBattleAnimation || paused || defeatPose ||
                presentationPauseCount > 0 || presentationControlCount > 0)
                return;

            if (turnEntryPending)
            {
                turnEntryPending = false;
                player.Play(turnEntryAnimation, false, IdleRecoveryMixSeconds);
            }
            else if (actionBlocked || !turnReady || !PlayingTurnEntry || player.Time >= player.Duration)
                PlayIdleLoop();
        }

        private void PlayIdleLoop(float mixSeconds = IdleRecoveryMixSeconds)
        {
            string animation = actionBlocked ? incapacitatedAnimation ?? idleAnimation : turnReady ? turnIdleAnimation : idleAnimation;
            if (player.CurrentAnimation != animation)
                player.Play(animation, true, mixSeconds);
        }

        internal void PlayBattleIdle()
        {
            if (player == null || defeatPose || !playingBattleAnimation) return;
            playingBattleAnimation = false;
            sourceReaction = null;
            if (turnEntryPending && !paused && presentationPauseCount == 0 && presentationControlCount == 0)
                ApplyTurnIdleIfFree();
            else
            {
                var body = PresentationRenderer;
                PlayIdleLoop(body && body.forceRenderingOff ? 0f : IdleRecoveryMixSeconds);
            }
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
            if (player == null || defeatPose) return 0f;
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
            if (instance == null || defeatPose) return null;
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

#if UNITY_EDITOR
        internal void EnableEditorPreview() => OnEnable();
        internal void DisableEditorPreview() => OnDisable();
        internal void AdvanceEditorPreview(float delta) => AdvanceAmbient(delta, delta);
#endif

        private void OnEnable()
        {
            OnDisable(); // Also makes a repeated activation safe when editor reload options change.
            InitializePlayer();
        }

        private void InitializePlayer()
        {
            // The additive scene briefly enables the authored roots before the staging
            // callback parks them. Only the verified online roster may initialize rigs.
            if (BattleOnlineLaunch.IsStaging(gameObject.scene)) return;
            NormalizeShadow();
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
                string neutralIdle = null;
                string battleIdle = null;
                foreach (var animation in instance.Animations)
                {
                    if (animation.Duration <= 0)
                        continue;
                    switch (animation.Name)
                    {
                        case "b_idle": battleIdle = animation.Name; break;
                        case "b_idle_ready": turnEntryAnimation = animation.Name; break;
                        case "idle": neutralIdle = animation.Name; break;
                        case "groggy": incapacitatedAnimation = animation.Name; break;
                    }
                }
                turnIdleAnimation = battleIdle ?? neutralIdle;
                // The authored entry joins neutral idle to battle idle. Legacy rigs without
                // that entry retain their existing idle selection.
                idleAnimation = turnEntryAnimation != null ? neutralIdle ?? battleIdle : turnIdleAnimation;
                if (idleAnimation == null)
                    throw new InvalidOperationException($"Battle preview actor {name} has no playable idle animation.");

                instance.Root.GetComponent<MeshRenderer>().sortingOrder = drawOrder;
                ApplyBattleDisplayScale(instance);
                instance.Play(idleAnimation, true);
                PreparePresentation();
                player = instance;
                player.Event += OnPlaybackEvent;
            }
            catch
            {
                try { instance?.Dispose(); }
                finally { ReleaseSource(); eventTimes = null; }
                throw;
            }
        }

        private void OnPlaybackEvent(SkeletonPlaybackEvent item)
        {
            if (turnReady && !playingBattleAnimation && PlayingTurnEntry && item.Kind == SkeletonPlaybackEventKind.Event)
                TurnEntryCue?.Invoke(this, item);
        }

        internal void ConfigureOffscreenPreview(BattleSkillPresentationAsset model, string modelSkin,
            string label, string definitionId)
        {
            externallyClocked = true;
            drawOrder = 55;
            UseBattleModel(model, modelSkin, label, definitionId);
        }

        internal void StepPreviewAmbient(float delta) => AdvanceAmbient(delta, delta);

        private void Update()
        {
            if (!externallyClocked) AdvanceAmbient(Time.deltaTime, Time.unscaledDeltaTime);
        }

        private void AdvanceAmbient(float idleDelta, float battleDelta)
        {
            if (player == null || paused || defeatPose || presentationPauseCount > 0 || presentationControlCount > 0) return;
            if (sourceReaction != null && player.Time >= player.Duration) return;
            player.Tick(playingBattleAnimation ? battleDelta : idleDelta);
            if (sourceReaction == null && playingBattleAnimation && player.Time >= player.Duration)
                PlayBattleIdle();
            else if (!playingBattleAnimation && PlayingTurnEntry && player.Time >= player.Duration)
                ApplyTurnIdleIfFree();
        }

        private void OnDisable()
        {
            try { ReleasePlayer(); }
            finally
            {
                actionBlocked = false;
                turnReady = false;
                turnEntryPending = false;
                paused = false;
                defeatPose = false;
                presentationPauseCount = 0;
                presentationControlCount = 0;
            }
        }

        private void ReleasePlayer()
        {
            try
            {
                if (player != null)
                {
                    player.Event -= OnPlaybackEvent;
                    if (player.Root) player.Root.SetActive(false);
                    player.Dispose();
                }
            }
            finally
            {
                player = null;
                ReleaseSource();
                eventTimes = null;
                idleAnimation = null;
                turnIdleAnimation = null;
                turnEntryAnimation = null;
                incapacitatedAnimation = null;
                playingBattleAnimation = false;
                sourceReaction = null;
            }
        }
    }
}
