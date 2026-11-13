using System;
using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.BattleScene
{
    // The battle owns shield state and the visual clock. This class only plays a borrowed effect.
    internal sealed class BattleShieldAura : IDisposable
    {
        private static readonly SkeletonPlayerFactory Factory = new SkeletonPlayerFactory();
        private readonly Definition definition;
        private readonly Vector3 fallbackAnchor;
        private readonly string objectName;
        private ISkeletonPlayer player;
        private Renderer body;
        private MeshRenderer aura;
        private bool hadShield, disposed;

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
        public static Definition Prepare(BattleSkillPresentationAsset asset, TextAsset cfx)
        {
            if (asset == null && !cfx) return null;
            if (asset == null || !asset.document || !asset.atlas || !cfx ||
                asset.pageNames == null || asset.pages == null ||
                asset.pageNames.Length == 0 || asset.pageNames.Length != asset.pages.Length)
                throw new ArgumentException("The shield aura requires CFX, Spine data, atlas and every atlas page.", nameof(asset));

            var primitives = JObject.Parse(cfx.text)["primitive"] as JArray;
            if (primitives?.Count != 1 || !(primitives[0] is JObject primitive) ||
                (string)primitive["format"] != "spine" || (bool?)primitive["loop"] != true ||
                asset.source != "effect/" + (string)primitive["source"] + ".scsp")
                throw new FormatException("The shield aura CFX must contain one looping Spine primitive matching the selected asset.");

            float x = (float?)primitive["x"] ?? 0f;
            float y = (float?)primitive["y"] ?? 0f;
            float scale = (float?)primitive["scale"] ?? 1f;
            int order = (int?)primitive["z"] ?? 0;
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(scale) || scale <= 0f)
                throw new FormatException("The shield aura CFX must have a finite position and a positive scale.");

            var pages = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            for (int index = 0; index < asset.pageNames.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(asset.pageNames[index]) || !asset.pages[index])
                    throw new ArgumentException("A shield aura atlas page is missing.", nameof(asset));
                pages.Add(asset.pageNames[index], asset.pages[index]);
            }

            var source = new SkeletonPlayerSource(asset.document.text, asset.atlas.text, pages, asset.premultipliedAlpha, asset.scale);
            Factory.Prepare(source);
            string animation = (string)primitive["ani"] ?? "animation";
            return new Definition(source, animation, new Vector3(x, y, 0f) * asset.scale, scale, order);
        }

        public BattleShieldAura(Definition definition, Transform shadow, Renderer initialBody)
            : this(definition, shadow, initialBody, "Battle shield aura") { }

        public BattleShieldAura(Definition definition, Transform shadow, Renderer initialBody, string objectName)
        {
            this.definition = definition;
            this.objectName = objectName;
            // Calibrate to the authored foot point before a skill can separate the actor and its ground shadow.
            if (initialBody && shadow)
                fallbackAnchor = initialBody.transform.InverseTransformPoint(shadow.position);
        }

        public void Sync(Renderer currentBody, bool shielded, float visualDelta, Vector2? skeletonAnchor)
        {
            if (disposed || definition == null) return;
            if (body != currentBody || (player != null && !player.Root))
            {
                ReleasePlayer();
                body = currentBody;
            }

            bool visible = shielded && BattlePreviewActor.IsBodyVisible(body);
            if (!visible)
            {
                if (player != null && player.Root.activeSelf) player.Root.SetActive(false);
                if (!shielded) hadShield = false;
                return;
            }

            if (player == null) CreatePlayer();
            // LAYOUT owns Renderer.enabled. Use the effect object's visibility so a stage can restore
            // the renderer independently, including effects first applied while the actor was hidden.
            if (!player.Root.activeSelf) player.Root.SetActive(true);
            Vector3 anchor = skeletonAnchor.HasValue ? (Vector3)skeletonAnchor.Value : fallbackAnchor;
            Vector3 position = anchor + definition.Position;
            if (player.Root.transform.localPosition != position) player.Root.transform.localPosition = position;
            int layer = body.sortingLayerID;
            int order = body.sortingOrder + definition.SortingOffset;
            if (aura.sortingLayerID != layer) aura.sortingLayerID = layer;
            if (aura.sortingOrder != order) aura.sortingOrder = order;
            if (!hadShield)
            {
                player.Play(definition.Animation, true);
                hadShield = true;
            }
            player.Tick(visualDelta);
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
            hadShield = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ReleasePlayer();
        }
    }
}
