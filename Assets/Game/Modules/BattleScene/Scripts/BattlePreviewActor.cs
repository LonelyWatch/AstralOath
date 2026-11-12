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
                instance.Play(idle, true);
                player = instance;
            }
            catch
            {
                instance.Dispose();
                throw;
            }
        }

        private void Update() => player?.Tick(Time.deltaTime);

        private void OnDisable()
        {
            player?.Dispose();
            player = null;
        }
    }
}
