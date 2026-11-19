using System;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    public sealed class SummonSkillLoop : ScriptableObject
    {
        public const string Address = "summon/iseria-skill";
        [SerializeField] private Texture2D[] pages;
        [SerializeField] private AudioClip originalAudio;
        [SerializeField] private int frameCount, columns, rows, gutterPixels;
        [SerializeField] private float framesPerSecond;

        private const double PosterSeconds = 3.25d;
        private const double PosterFade = .375d;
        private const double MovieFade = .45d;
        private const double HeroReturn = 1.10d;
        private const double HeroHold = 2.65d;
        private const double ExitFade = .5d;
        private const double PosterReturn = .65d;
        private double SkillSeconds => frameCount / (double)framesPerSecond;
        private int PageCapacity => columns * rows;

        public void Configure(Texture2D[] pages, int frameCount, float framesPerSecond, int columns, int rows,
            int gutterPixels, AudioClip originalAudio = null)
        {
            this.pages = pages;
            this.frameCount = frameCount;
            this.framesPerSecond = framesPerSecond;
            this.columns = columns;
            this.rows = rows;
            this.gutterPixels = gutterPixels;
            this.originalAudio = originalAudio;
            Validate();
        }

        public Player CreatePlayer(RawImage output, AudioSource audio, BindingScope owner,
            CanvasGroup scenery, CanvasGroup hero, CanvasGroup offer)
        {
            owner.Token.ThrowIfCancellationRequested();
            Validate();
            var player = new Player(this, output, audio, scenery, hero, offer);
            owner.Add(player.Dispose);
            return player;
        }

        private void Validate()
        {
            if (frameCount < 1 || frameCount > 1200 || !float.IsFinite(framesPerSecond) || framesPerSecond <= 0f ||
                columns < 1 || columns > 16 || rows < 1 || rows > 16 || gutterPixels < 0 ||
                pages == null || pages.Length != (frameCount + PageCapacity - 1) / PageCapacity)
                throw new InvalidOperationException("Invalid summon skill loop frame layout.");

            foreach (var page in pages)
                if (!page || page.width % columns != 0 || page.height % rows != 0 ||
                    page.width / columns <= 2 * gutterPixels + 1 || page.height / rows <= 2 * gutterPixels + 1)
                    throw new InvalidOperationException("Invalid summon skill loop atlas.");
        }

        private void Render(RawImage output, int frame)
        {
            int tile = frame % PageCapacity;
            var page = pages[frame / PageCapacity];
            float width = page.width / columns, height = page.height / rows;
            float inset = gutterPixels + .5f;
            output.texture = page;
            var tileRect = new Rect(
                ((tile % columns) * width + inset) / page.width,
                ((rows - 1 - tile / columns) * height + inset) / page.height,
                (width - 2f * inset) / page.width, (height - 2f * inset) / page.height);
            Rect viewport = output.rectTransform.rect;
            if (viewport.width > 0f && viewport.height > 0f)
            {
                float relativeAspect = (viewport.width / viewport.height) /
                    ((width - 2f * gutterPixels) / (height - 2f * gutterPixels));
                Vector2 center = tileRect.center;
                if (relativeAspect > 1f) tileRect.height /= relativeAspect;
                else tileRect.width *= relativeAspect;
                tileRect.center = center;
            }
            output.uvRect = tileRect;
        }

        public sealed class Player : IDisposable
        {
            private readonly SummonSkillLoop clip;
            private readonly RawImage output;
            private readonly AudioSource audio;
            private readonly CanvasGroup scenery, hero, offer;
            private readonly Color originalColor;
            private readonly Texture savedTexture;
            private readonly Rect savedUv;
            private readonly bool savedEnabled, savedRaycast;
            private readonly AudioClip savedAudio;
            private readonly bool savedLoop;
            private bool selected, paused, disposed;
            private double elapsed, lastTime;
            private int displayedFrame = -1;

            internal Player(SummonSkillLoop clip, RawImage output, AudioSource audio, CanvasGroup scenery, CanvasGroup hero, CanvasGroup offer)
            {
                this.clip = clip;
                this.output = output ? output : throw new ArgumentNullException(nameof(output));
                this.audio = audio;
                this.scenery = scenery;
                this.hero = hero;
                this.offer = offer;
                originalColor = output.color;
                savedTexture = output.texture;
                savedUv = output.uvRect;
                savedEnabled = output.enabled;
                savedRaycast = output.raycastTarget;
                output.enabled = false;
                output.raycastTarget = false;
                if (audio)
                {
                    savedAudio = audio.clip;
                    savedLoop = audio.loop;
                    audio.clip = clip.originalAudio;
                    audio.loop = false;
                }
                Canvas.willRenderCanvases += Tick;
            }

            public void SetState(bool showFeaturedBanner, bool pause)
            {
                if (disposed) return;
                if (selected == showFeaturedBanner && paused == pause) return;
                lastTime = Time.realtimeSinceStartupAsDouble;
                if (selected != showFeaturedBanner)
                {
                    elapsed = 0;
                    HideSkill();
                }
                else if (audio && clip.originalAudio && displayedFrame >= 0)
                {
                    if (pause) audio.Pause();
                    else audio.UnPause();
                }
                selected = showFeaturedBanner;
                paused = pause;
            }

            private void Tick()
            {
                if (disposed || !selected || paused || !output) return;
                double now = Time.realtimeSinceStartupAsDouble;
                elapsed += Math.Max(0d, now - lastTime);
                lastTime = now;

                double movieStart = PosterSeconds + PosterFade;
                double movieTime = elapsed - movieStart;
                double exitTime = movieTime - clip.SkillSeconds - HeroHold;
                if (exitTime >= ExitFade + PosterReturn)
                {
                    elapsed %= movieStart + clip.SkillSeconds + HeroHold + ExitFade + PosterReturn;
                    HideSkill();
                    return;
                }

                if (movieTime < 0 || exitTime >= ExitFade)
                {
                    float poster = movieTime < 0 ? Ramp(-movieTime / PosterFade) : Ramp((exitTime - ExitFade) / PosterReturn);
                    scenery.alpha = hero.alpha = offer.alpha = poster;
                    output.enabled = false;
                    return;
                }

                float exitOpacity = 1f - Ramp(exitTime / ExitFade);
                scenery.alpha = offer.alpha = 0f;
                hero.alpha = Ramp((movieTime - clip.SkillSeconds + HeroReturn) / HeroReturn) * exitOpacity;
                var tint = originalColor;
                tint.a *= Ramp(movieTime / MovieFade) * exitOpacity;
                output.color = tint;
                int frame = Mathf.Min(clip.frameCount - 1, (int)(movieTime * clip.framesPerSecond));
                clip.Render(output, frame);
                if (displayedFrame < 0)
                {
                    output.enabled = true;
                    if (audio && clip.originalAudio)
                    {
                        audio.time = Mathf.Clamp((float)movieTime, 0f,
                            Mathf.Max(0f, clip.originalAudio.length - .001f));
                        audio.Play();
                    }
                }
                displayedFrame = frame;
            }

            private static float Ramp(double value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)value));

            private void HideSkill()
            {
                if (output) { output.enabled = false; output.color = originalColor; }
                if (scenery) scenery.alpha = 1f;
                if (hero) hero.alpha = 1f;
                if (offer) offer.alpha = 1f;
                if (audio && displayedFrame >= 0) audio.Stop();
                displayedFrame = -1;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Canvas.willRenderCanvases -= Tick;
                HideSkill();
                if (output)
                {
                    output.texture = savedTexture;
                    output.uvRect = savedUv;
                    output.enabled = savedEnabled;
                    output.raycastTarget = savedRaycast;
                }
                if (audio)
                {
                    audio.clip = savedAudio;
                    audio.loop = savedLoop;
                }
            }
        }
    }
}
