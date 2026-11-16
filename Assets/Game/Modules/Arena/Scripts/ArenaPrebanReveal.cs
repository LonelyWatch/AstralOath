using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Client.Arena
{
    internal sealed class ArenaPrebanReveal : IDisposable
    {
        private const float PairSeconds = .83f;
        private const float DismissSeconds = .18f;
        private readonly GameObject root;
        private readonly CanvasGroup opacity;
        private readonly Portrait own, opponent;
        private readonly ArenaDraftPortrait stamp;
        private readonly Graphic divider, stampSurface;
        private ArenaDraftArt.Hero[] ownHeroes = Array.Empty<ArenaDraftArt.Hero>();
        private ArenaDraftArt.Hero[] opponentHeroes = Array.Empty<ArenaDraftArt.Hero>();
        private int pairCount, pairIndex;
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
            ownHeroes = local;
            opponentHeroes = remote;
            pairCount = Mathf.Max(local.Length, remote.Length);
            if (!IsPlaying) return;
            opacity.alpha = 1f;
            root.SetActive(true);
            ShowPair();
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
            else
            {
                int nextPair = (int)(elapsed / PairSeconds);
                if (pairIndex != nextPair)
                {
                    pairIndex = nextPair;
                    ShowPair();
                }
                float time = elapsed - pairIndex * PairSeconds;
                own.Update(time, seconds);
                opponent.Update(time, seconds);
            }
            stamp.Tick(seconds);
            return false;
        }

        private void ShowPair()
        {
            own.Show(pairIndex < ownHeroes.Length ? ownHeroes[pairIndex] : null);
            opponent.Show(pairIndex < opponentHeroes.Length ? opponentHeroes[pairIndex] : null);
        }

        internal void Clear()
        {
            if (disposed) return;
            pairCount = pairIndex = 0;
            elapsed = 0f;
            ownHeroes = opponentHeroes = Array.Empty<ArenaDraftArt.Hero>();
            own.Show(null);
            opponent.Show(null);
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
            private readonly string surfaceName;
            private readonly RawImage surface;
            private readonly Image fallback;
            private readonly ArenaDraftPortrait player;

            internal Portrait(RectTransform root, string side, bool upper)
            {
                this.upper = upper;
                var shape = Child<ArenaPrebanPortraitMask>(root, side + " diagonal half", Vector2.zero, Vector2.one);
                shape.Above = upper;
                shape.raycastTarget = false;
                shape.gameObject.AddComponent<Mask>().showMaskGraphic = false;

                Vector2 min = upper ? new Vector2(-.10f, -.005f) : new Vector2(.20f, -.48f);
                Vector2 max = upper ? new Vector2(.70f, 1.30f) : new Vector2(1.14f, .81f);
                surfaceName = side + " reveal portrait";
                fallback = Child<Image>(shape.rectTransform, side + " fallback illustration", min, max);
                fallback.raycastTarget = false;
                fallback.preserveAspect = true;
                surface = Child<RawImage>(shape.rectTransform, surfaceName, min, max);
                player = new ArenaDraftPortrait(surface);
            }

            internal void Show(ArenaDraftArt.Hero hero)
            {
                if (!surface || !fallback)
                {
                    player.Clear();
                    return;
                }
                surface.name = hero != null ? surfaceName + " " + hero.Id : surfaceName;
                fallback.sprite = hero != null ? hero.Illustration ? hero.Illustration : hero.Card : null;
                fallback.enabled = !player.Show(hero?.MakePortrait()) && fallback.sprite;
                Update(0f, 0f);
            }

            internal void Update(float time, float seconds)
            {
                if (!surface || !fallback) return;
                float slide = Mathf.SmoothStep(1f, 0f, time / .13f)
                    - Mathf.SmoothStep(0f, .78f, (time - .69f) / .14f);
                float offset = (upper ? -1f : 1f) * slide * surface.rectTransform.rect.width * .15f;
                surface.rectTransform.anchoredPosition = fallback.rectTransform.anchoredPosition = new Vector2(offset, 0);
                // The upper portrait reads as gray under the stamp; the lower starts warmer
                // and both finish almost monochrome before sliding to the next pair.
                float wash = Mathf.SmoothStep(0f, 1f, time / (upper ? .19f : .27f));
                player.SetDesaturation(Mathf.Lerp(upper ? .71f : .47f, .98f, wash));
                var tint = Color.Lerp(new Color(.91f, .92f, .96f), new Color(.73f, .75f, .79f), wash);
                if (fallback.color != tint) fallback.color = tint;
                player.Tick(seconds);
            }

            public void Dispose()
            {
                player.Dispose();
                if (surface) Object.Destroy(surface.transform.parent.gameObject);
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
