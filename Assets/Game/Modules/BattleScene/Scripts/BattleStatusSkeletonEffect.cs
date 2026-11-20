using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    // The battle owns status state and the visual clock. One parsed definition can be played by multiple actors.
    internal sealed class BattleStatusSkeletonEffect : IDisposable
    {
        private static readonly SkeletonPlayerFactory Factory = new SkeletonPlayerFactory();
        private readonly Definition definition;
        private readonly Vector3 fallbackAnchor;
        private readonly string objectName;
        private ISkeletonPlayer player;
        private Renderer body;
        private MeshRenderer aura;
        private bool wasActive, disposed;

        internal sealed class Definition
        {
            internal readonly SkeletonPlayerSource Source;
            internal readonly string Animation;
            internal readonly Vector3 Position;
            internal readonly Vector3 Scale;
            internal readonly int SortingOffset;

            internal Definition(SkeletonPlayerSource source, string animation, Vector3 position, float scale, int sortingOffset)
            {
                Source = source;
                Animation = animation;
                Position = position;
                Scale = Vector3.one * scale;
                SortingOffset = sortingOffset;
            }
        }

        // Parse and prepare the source once for all units in this battle.
        public static Definition Prepare(BattleSkillPresentationAsset asset, TextAsset cfx, int defaultSortingOffset = 0)
            => PrepareAsset(asset, cfx, defaultSortingOffset, true);

        public static Definition PrepareOneShot(BattleSkillPresentationAsset asset, TextAsset cfx)
            => PrepareAsset(asset, cfx, 0, false);

        private static Definition PrepareAsset(BattleSkillPresentationAsset asset, TextAsset cfx, int defaultSortingOffset, bool loop)
        {
            if (asset == null && !cfx) return null;
            if (asset == null || !asset.document || !asset.atlas || !cfx ||
                asset.pageNames == null || asset.pages == null ||
                asset.pageNames.Length == 0 || asset.pageNames.Length != asset.pages.Length)
                throw new ArgumentException("A status effect requires CFX, Spine data, atlas and every atlas page.", nameof(asset));

            var primitives = JObject.Parse(cfx.text)["primitive"] as JArray;
            if (primitives?.Count != 1 || !(primitives[0] is JObject primitive) ||
                (string)primitive["format"] != "spine" || ((bool?)primitive["loop"] ?? false) != loop ||
                asset.source != "effect/" + (string)primitive["source"] + ".scsp")
                throw new FormatException("A status effect CFX must contain one Spine primitive matching the selected asset and playback mode.");

            float x = (float?)primitive["x"] ?? 0f;
            float y = (float?)primitive["y"] ?? 0f;
            float scale = (float?)primitive["scale"] ?? 1f;
            int order = (int?)primitive["z"] ?? defaultSortingOffset;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(scale) || scale <= 0f)
                throw new FormatException("A status effect CFX must have a finite position and a positive scale.");

            var pages = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            for (int index = 0; index < asset.pageNames.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(asset.pageNames[index]) || !asset.pages[index])
                    throw new ArgumentException("A status effect atlas page is missing.", nameof(asset));
                pages.Add(asset.pageNames[index], asset.pages[index]);
            }

            var source = new SkeletonPlayerSource(asset.document.text, asset.atlas.text, pages, asset.premultipliedAlpha, asset.scale);
            Factory.Prepare(source);
            string animation = (string)primitive["ani"] ?? "animation";
            return new Definition(source, animation, new Vector3(x, y, 0f) * asset.scale, scale, order);
        }

        public BattleStatusSkeletonEffect(Definition definition, Transform shadow, Renderer initialBody)
            : this(definition, shadow, initialBody, "Battle shield aura") { }

        public BattleStatusSkeletonEffect(Definition definition, Transform shadow, Renderer initialBody, string objectName)
        {
            this.definition = definition;
            this.objectName = objectName;
            // Calibrate to the authored foot point before a skill can separate the actor and its ground shadow.
            if (initialBody && shadow)
                fallbackAnchor = initialBody.transform.InverseTransformPoint(shadow.position);
        }

        public void Sync(Renderer currentBody, bool active, float visualDelta, Vector2? skeletonAnchor)
        {
            if (disposed || definition == null) return;
            if (body != currentBody || (player != null && !player.Root))
            {
                ReleasePlayer();
                body = currentBody;
            }

            bool visible = active && BattlePreviewActor.IsBodyVisible(body);
            if (!visible)
            {
                if (player != null && player.Root.activeSelf) player.Root.SetActive(false);
                if (!active) wasActive = false;
                return;
            }

            if (player == null) CreatePlayer();
            // LAYOUT owns Renderer.enabled. Use the effect object's visibility so a stage can restore
            // the renderer independently, including effects first applied while the actor was hidden.
            if (!player.Root.activeSelf) player.Root.SetActive(true);
            FollowAnchor(skeletonAnchor);
            if (!wasActive)
            {
                player.Play(definition.Animation, true);
                wasActive = true;
            }
            player.Tick(visualDelta);
        }

        public bool IsPlaying => player != null;

        public void StopOneShot() => ReleasePlayer();

        public void StartOneShot(Renderer currentBody, Vector2 anchor)
        {
            if (disposed || definition == null || !currentBody) return;
            ReleasePlayer();
            body = currentBody;
            CreatePlayer();
            FollowAnchor(anchor);
            player.Play(definition.Animation, false);
            player.Tick(0f);
            player.Root.SetActive(BattlePreviewActor.IsBodyVisible(body));
        }

        public void TickOneShot(Renderer currentBody, bool present, float visualDelta, Vector2? anchor)
        {
            if (player == null) return;
            if (!present || !anchor.HasValue || !currentBody || body != currentBody || !player.Root ||
                player.Time + visualDelta >= player.Duration)
            {
                ReleasePlayer();
                return;
            }
            FollowAnchor(anchor);
            player.Root.SetActive(BattlePreviewActor.IsBodyVisible(body));
            player.Tick(visualDelta);
        }

        private void FollowAnchor(Vector2? skeletonAnchor)
        {
            Vector3 anchor = skeletonAnchor.HasValue ? (Vector3)skeletonAnchor.Value : fallbackAnchor;
            Vector3 position = anchor + definition.Position;
            if (player.Root.transform.localPosition != position) player.Root.transform.localPosition = position;
            int layer = body.sortingLayerID;
            int order = body.sortingOrder + definition.SortingOffset;
            if (aura.sortingLayerID != layer) aura.sortingLayerID = layer;
            if (aura.sortingOrder != order) aura.sortingOrder = order;
        }

        private void CreatePlayer()
        {
            player = Factory.Create(definition.Source, body.transform);
            var root = player.Root;
            root.name = objectName;
            root.transform.localScale = definition.Scale;
            aura = root.GetComponent<MeshRenderer>();
        }

        private void ReleasePlayer()
        {
            if (player?.Root)
            {
                player.Root.SetActive(false);
                player.Root.transform.SetParent(null, false);
            }
            player?.Dispose();
            player = null;
            aura = null;
            body = null;
            wasActive = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleasePlayer();
        }
    }
}
