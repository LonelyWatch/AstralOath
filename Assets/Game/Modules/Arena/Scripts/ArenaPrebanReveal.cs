using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaPrebanReveal : IDisposable
    {
        // 30 fps reference: the second pair enters 28 frames after the first.
        private const float PairSeconds = 28f / 30f;
        private const float DismissSeconds = 8f / 30f;
        private const float CrossSeconds = 3f / 30f;
        private readonly GameObject root;
        private readonly CanvasGroup opacity;
        private readonly Portrait own, opponent;
        private readonly ArenaDraftPortrait stamp;
        private readonly Graphic divider, stampSurface;
        private int pairCount;
        private float elapsed;
        private bool disposed;

        internal ArenaPrebanReveal(GameObject root, GameObject legacyChips)
        {
            this.root = root;
            legacyChips.SetActive(false);
            var layout = (RectTransform)root.transform;
            var caption = layout.Find("Preban result caption");
            if (caption) caption.gameObject.SetActive(false);
            var shadow = layout.Find("Reveal shadow");
            var slash = layout.Find("Central slash") as RectTransform;
            if (slash) Place(slash, new Vector2(-.022f, .171f), new Vector2(1.022f, .911f));
            opacity = root.GetComponent<CanvasGroup>();
            if (!opacity) opacity = root.AddComponent<CanvasGroup>();
            own = new Portrait(layout, "Own", upper: true);
            opponent = new Portrait(layout, "Opponent", upper: false);
            if (shadow) shadow.SetAsLastSibling();
            if (slash) slash.SetAsLastSibling();
            divider = Child<ArenaPrebanDivider>(layout, "Lit diagonal seam", Vector2.zero, Vector2.one);
            divider.raycastTarget = false;
            var output = Child<RawImage>(layout, "Preban BANNED stamp", Vector2.zero, Vector2.one);
            stampSurface = output;
            // P2 4:06: the settled stamp covers approximately 31% of the 16:9 stage.
            stamp = new ArenaDraftPortrait(output, nativeEffectSize: new Vector2(1040, 620) * 1.86f,
                centralBanLight: true);
        }

        internal bool IsPlaying => pairCount > 0;

        internal void Start(ArenaDraftArt.Hero[] local, ArenaDraftArt.Hero[] remote, ArenaDraftArt.NativeEffect effect,
            IReadOnlyDictionary<string, Texture2D> particles)
        {
            if (disposed || !root || !opacity) return;
            Clear();
            pairCount = Mathf.Max(local.Length, remote.Length);
            if (!IsPlaying) return;
            opacity.alpha = 1f;
            root.SetActive(true);
            own.Prepare(local);
            opponent.Prepare(remote);
            own.Update(0f, 0f);
            opponent.Update(0f, 0f);
            if (effect != null && stamp.PlayOneShot(effect.MakeSource(), "intro", loopAfter: "loop"))
                stamp.AddParticles(effect.Particles, particles, .10f, 7f);
        }

        // Returns true once, when the binder can reveal the permanent footer and re-enable input.
        internal bool Tick(float seconds)
        {
            if (!IsPlaying) return false;
            if (!root || !opacity)
            {
                Clear();
                return false;
            }
            // Loading the battle scene can stall a frame long enough to skip an entire pair.
            seconds = Mathf.Min(seconds, .10f);
            elapsed += seconds;
            float sequenceEnd = pairCount * PairSeconds;
            if (elapsed >= sequenceEnd + DismissSeconds)
            {
                Clear();
                return true;
            }
            if (elapsed >= sequenceEnd)
            {
                opacity.alpha = 1f - Mathf.SmoothStep(0f, 1f, (elapsed - sequenceEnd) / DismissSeconds);
            }
            own.Update(elapsed, seconds);
            opponent.Update(elapsed, seconds);
            stamp.Tick(seconds);
            return false;
        }

        internal void Clear()
        {
            if (disposed) return;
            pairCount = 0;
            elapsed = 0f;
            own.Clear();
            opponent.Clear();
            stamp.Clear();
            if (root) root.SetActive(false);
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear();
            disposed = true;
            own.Dispose();
            opponent.Dispose();
            stamp.Dispose();
            if (divider) Object.Destroy(divider.gameObject);
            if (stampSurface) Object.Destroy(stampSurface.gameObject);
        }

        private static T Child<T>(RectTransform parent, string name, Vector2 min, Vector2 max) where T : Graphic
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rect = (RectTransform)node.transform;
            rect.SetParent(parent, false);
            Place(rect, min, max);
            return node.AddComponent<T>();
        }

        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private sealed class Portrait : IDisposable
        {
            private readonly bool upper;
            private readonly string side;
            private readonly RectTransform mask;
            private readonly List<Layer> layers = new();
            private static readonly AnimationCurve Slide = new(
                new Keyframe(0f, .76f, -4f, -4f),
                new Keyframe(.20f, .21f, -.85f, -.85f),
                new Keyframe(.67f, -.08f, -.65f, -.65f),
                new Keyframe(.83f, -.23f, -1.55f, -1.55f),
                new Keyframe(PairSeconds + CrossSeconds, -.91f, -4f, -4f));

            private sealed class Layer
            {
                internal RawImage Surface;
                internal Image Fallback;
                internal ArenaDraftPortrait Player;
                internal bool HasSkeleton;
            }

            internal Portrait(RectTransform root, string side, bool upper)
            {
                this.upper = upper;
                this.side = side;
                var shape = Child<ArenaPrebanPortraitMask>(root, side + " diagonal half", Vector2.zero, Vector2.one);
                shape.Above = upper;
                shape.raycastTarget = false;
                shape.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                mask = shape.rectTransform;
            }

            internal void Prepare(ArenaDraftArt.Hero[] heroes)
            {
                Vector2 min = upper ? new Vector2(-.10f, -.005f) : new Vector2(.20f, -.48f);
                Vector2 max = upper ? new Vector2(.70f, 1.30f) : new Vector2(1.14f, .81f);
                foreach (var hero in heroes)
                {
                    var surface = Child<RawImage>(mask, side + " reveal portrait " + hero?.Id, min, max);
                    var fallback = Child<Image>(mask, side + " fallback illustration", min, max);
                    fallback.raycastTarget = false;
                    fallback.preserveAspect = true;
                    fallback.sprite = hero != null ? hero.Illustration ? hero.Illustration : hero.Card : null;
                    var player = new ArenaDraftPortrait(surface);
                    player.SetRenderCameraActive(false);
                    bool ready = player.Show(hero?.MakePortrait());
                    surface.enabled = fallback.enabled = false;
                    layers.Add(new Layer { Surface = surface, Fallback = fallback, Player = player, HasSkeleton = ready });
                }
            }

            internal void Update(float time, float seconds)
            {
                for (int index = 0; index < layers.Count; index++)
                {
                    var layer = layers[index];
                    float local = time - index * PairSeconds;
                    bool visible = local >= 0 && local < PairSeconds + CrossSeconds;
                    layer.Player.SetRenderCameraActive(visible);
                    layer.Surface.enabled = visible && layer.HasSkeleton;
                    layer.Fallback.enabled = visible && !layer.HasSkeleton && layer.Fallback.sprite;
                    if (!visible) continue;
                    float x = Slide.Evaluate(local) * mask.rect.width * (upper ? 1f : -1f);
                    layer.Surface.rectTransform.anchoredPosition = layer.Fallback.rectTransform.anchoredPosition = new Vector2(x, 0f);
                    float wash = Mathf.SmoothStep(.12f, .93f, Mathf.InverseLerp(.17f, .85f, local));
                    layer.Player.SetDesaturation(wash);
                    layer.Fallback.color = Color.Lerp(Color.white, new Color(.76f, .76f, .78f), wash);
                    layer.Player.Tick(seconds);
                }
            }

            internal void Clear()
            {
                foreach (var layer in layers)
                {
                    layer.Player.Dispose();
                    if (layer.Surface) Object.Destroy(layer.Surface.gameObject);
                    if (layer.Fallback)
                    {
                        layer.Fallback.enabled = false;
                        Object.Destroy(layer.Fallback.gameObject);
                    }
                }
                layers.Clear();
            }

            public void Dispose()
            {
                Clear();
                if (mask) Object.Destroy(mask.gameObject);
            }
        }
    }

    internal sealed class ArenaPrebanPortraitMask : MaskableGraphic
    {
        internal const float NearEdge = .19f, FarEdge = .89f;
        internal bool Above { get; set; }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var area = GetPixelAdjustedRect();
            float leftCut = area.yMin + area.height * NearEdge;
            float rightCut = area.yMin + area.height * FarEdge;
            float edge = Above ? area.yMax : area.yMin;
            if (Above)
            {
                Add(area.xMin, leftCut); Add(area.xMin, edge); Add(area.xMax, edge); Add(area.xMax, rightCut);
            }
            else
            {
                Add(area.xMin, edge); Add(area.xMin, leftCut); Add(area.xMax, rightCut); Add(area.xMax, edge);
            }
            vertices.AddTriangle(0, 1, 2);
            vertices.AddTriangle(2, 3, 0);

            void Add(float x, float y) => vertices.AddVert(new Vector3(x, y), Color.white, Vector2.zero);
        }
    }

    internal sealed class ArenaPrebanDivider : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            var area = GetPixelAdjustedRect();
            AddBand(.014f, new Color32(33, 99, 255, 0), new Color32(255, 27, 49, 0));
            AddBand(.0027f, new Color32(48, 119, 255, 245), new Color32(255, 29, 49, 245));
            AddBand(.0008f, new Color32(101, 185, 255, 255), new Color32(255, 95, 105, 255));
            AddBand(.013f, new Color32(33, 99, 255, 0), new Color32(255, 27, 49, 0), lower: true);

            void AddBand(float height, Color32 left, Color32 right, bool lower = false)
            {
                int start = vertices.currentVertCount;
                float direction = lower ? -1f : 1f;
                Add(0f, ArenaPrebanPortraitMask.NearEdge, left);
                Add(.49f, (ArenaPrebanPortraitMask.NearEdge + ArenaPrebanPortraitMask.FarEdge) * .5f,
                    new Color32(255, 238, 248, (byte)((left.a + right.a) / 2)));
                Add(1f, ArenaPrebanPortraitMask.FarEdge, right);
                vertices.AddTriangle(start, start + 1, start + 3);
                vertices.AddTriangle(start + 1, start + 4, start + 3);
                vertices.AddTriangle(start + 3, start + 4, start + 2);
                vertices.AddTriangle(start + 4, start + 5, start + 2);

                void Add(float x, float y, Color32 color)
                {
                    vertices.AddVert(new Vector3(area.xMin + area.width * x, area.yMin + area.height * y),
                        new Color32(color.r, color.g, color.b, 220), Vector2.zero);
                    vertices.AddVert(new Vector3(area.xMin + area.width * x, area.yMin + area.height * (y + height * direction)),
                        color, Vector2.zero);
                }
            }
        }
    }
}
