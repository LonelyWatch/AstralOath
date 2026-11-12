using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
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

        public Bounds Bounds => player != null
            ? player.Root.GetComponent<MeshRenderer>().bounds : new Bounds(transform.position, Vector3.zero);

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
            {
                playingBattleAnimation = false;
                player.Play(idleAnimation, true);
            }
        }

        private void OnDisable()
        {
            player?.Dispose();
            player = null;
        }
    }
}
