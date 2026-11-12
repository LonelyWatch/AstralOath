using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    [DisallowMultipleComponent]
    public sealed class BattlePreviewActor : MonoBehaviour
    {
        [SerializeField] private TextAsset skeleton;
        [SerializeField] private TextAsset atlas;
        [SerializeField] private Texture2D page;
        [SerializeField] private string pageName;
        [SerializeField] private string skin = "default";
        [SerializeField] private bool faceLeft;
        [SerializeField] private int drawOrder;
        private ISkeletonPlayer player;
        private string idleAnimation;
        private bool playingBattleAnimation;
        private Dictionary<string, Dictionary<string, float>> eventTimes;

        public Bounds Bounds => player != null
            ? player.Root.GetComponent<MeshRenderer>().bounds : new Bounds(transform.position, Vector3.zero);

        internal float FacingDirection => faceLeft ? -1f : 1f;

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
            if (player == null || !player.TryGetBonePose(anchor, out var pose)) return false;
            position = player.Root.transform.TransformPoint(pose.LocalPosition);
            return true;
        }

        internal void PlayBattleIdle()
        {
            if (player == null || !playingBattleAnimation) return;
            playingBattleAnimation = false;
            player.Play(idleAnimation, true);
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
            if (eventTimes == null)
            {
                // Retain only event timings. The source skeleton can contain megabytes of mesh keys.
                eventTimes = new Dictionary<string, Dictionary<string, float>>(StringComparer.Ordinal);
                var animations = (JObject)JObject.Parse(skeleton.text)["animations"];
                foreach (var clip in animations.Properties())
                {
                    var times = new Dictionary<string, float>(StringComparer.Ordinal);
                    if (clip.Value["events"] is JArray events)
                        foreach (var item in events)
                        {
                            string name = (string)item["name"];
                            float at = (float?)item["time"] ?? 0f;
                            if (name != null && (!times.TryGetValue(name, out float previous) || at < previous))
                                times[name] = at;
                        }
                    eventTimes.Add(clip.Name, times);
                }
            }
            return eventTimes.TryGetValue(animation, out var values) && values.TryGetValue(eventName, out float value)
                ? value : -1f;
        }

        public float PlayBattleAnimation(string animation)
        {
            if (player == null) return 0f;
            foreach (var clip in player.Animations)
                if (clip.Name == animation && clip.Duration > 0)
                {
                    player.Play(animation, false);
                    playingBattleAnimation = true;
                    return clip.Duration;
                }
            return 0f;
        }

        private void OnEnable()
        {
            eventTimes = null;
            if (!skeleton || !atlas || !page || string.IsNullOrWhiteSpace(pageName))
                throw new InvalidOperationException($"Battle preview actor {name} has incomplete Spine data.");

            var textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal) { { pageName, page } };
            var source = new SkeletonPlayerSource(skeleton.text, atlas.text, textures, premultipliedAlpha: true);
            var instance = new SkeletonPlayerFactory().Create(source, transform);
            try
            {
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
                if (faceLeft)
                    instance.Root.transform.localScale = new Vector3(-1, 1, 1);
                idleAnimation = idle;
                playingBattleAnimation = false;
                instance.Play(idle, true);
                player = instance;
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }

        private void Update()
        {
            if (player == null) return;
            player.Tick(playingBattleAnimation ? Time.unscaledDeltaTime : Time.deltaTime);
            if (playingBattleAnimation && player.Time >= player.Duration)
                PlayBattleIdle();
        }

        private void OnDisable()
        {
            player?.Dispose();
            player = null;
        }
    }
}
