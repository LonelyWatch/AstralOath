using System;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Summon
{
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true, "AstralOath.Client.Summon", "AstralOath.Client.Home", "SummonSkillLoop")]
    public sealed class SummonSkillLoop : ScriptableObject
    {
        public const string Address = "summon/iseria-skill";
        [SerializeField] private BattleSkillPreviewDefinition preview;
        [SerializeField] private AudioClip originalAudio;

        private const double PosterSeconds = 3.25d;
        private const double PosterFade = .375d;
        private const double SkillFade = .45d;
        private const double HeroReturn = 1.10d;
        private const double HeroHold = 2.65d;
        private const double ExitFade = .5d;
        private const double PosterReturn = .65d;

        public void Configure(BattleSkillPreviewDefinition preview, AudioClip originalAudio)
        {
            this.preview = preview ? preview : throw new ArgumentNullException(nameof(preview));
            this.originalAudio = originalAudio;
        }

        public Player CreatePlayer(RawImage output, AudioSource audio, BindingScope owner,
            CanvasGroup scenery, CanvasGroup hero, CanvasGroup offer)
        {
            owner.Token.ThrowIfCancellationRequested();
            if (!preview) throw new InvalidOperationException("Summon banner has no battle skill preview.");
            var player = new Player(this, output, audio, scenery, hero, offer);
            owner.Add(player.Dispose);
            return player;
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
            private UiAudioPlayback sound;
            private int resumeSample = -1;
            private bool selected, paused, disposed;
            private double elapsed, lastTime;
            private BattleSkillPreviewSession battle;
            private int lastRenderedFrame = -1;
            private double skillSeconds;

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
                else if (audio && clip.originalAudio && battle != null)
                {
                    if (pause)
                    {
                        resumeSample = sound ? sound.SamplePosition : -1;
                        UiAudioPlayback.End(ref sound);
                    }
                    else if (resumeSample >= 0)
                    {
                        sound = UiAudioPlayback.Play(audio, clip.originalAudio, startSample: resumeSample);
                        resumeSample = -1;
                    }
                }
                selected = showFeaturedBanner;
                paused = pause;
            }

            private void Tick()
            {
                if (disposed || !selected || paused || !output || lastRenderedFrame == Time.frameCount) return;
                lastRenderedFrame = Time.frameCount;
                try { Advance(); }
                catch (Exception error)
                {
                    // Allow the page to remain usable if the local preview could not be rendered.
                    selected = false;
                    HideSkill();
                    Debug.LogException(error);
                }
            }

            private void Advance()
            {
                double now = Time.realtimeSinceStartupAsDouble;
                double frameSeconds = Math.Max(0d, now - lastTime);
                elapsed += frameSeconds;
                lastTime = now;

                double skillStart = PosterSeconds + PosterFade;
                double skillTime = elapsed - skillStart;
                if (skillTime < 0)
                {
                    scenery.alpha = hero.alpha = offer.alpha = Ramp(-skillTime / PosterFade);
                    return;
                }
                if (battle == null)
                {
                    battle = clip.preview.Create(output);
                    skillSeconds = battle.Duration;
                    output.enabled = true;
                    sound = UiAudioPlayback.Play(audio, clip.originalAudio);
                    // Resource preparation can take time on the first visit. Start sound and
                    // motion together once the live stage can present its first frame.
                    elapsed = skillStart;
                    lastTime = Time.realtimeSinceStartupAsDouble;
                    skillTime = 0;
                    frameSeconds = 0;
                }
                double exitTime = skillTime - skillSeconds - HeroHold;
                if (exitTime >= ExitFade + PosterReturn)
                {
                    elapsed = 0d;
                    HideSkill();
                    return;
                }

                if (exitTime >= ExitFade)
                {
                    scenery.alpha = hero.alpha = offer.alpha = Ramp((exitTime - ExitFade) / PosterReturn);
                    output.enabled = false;
                    return;
                }

                float exitOpacity = 1f - Ramp(exitTime / ExitFade);
                scenery.alpha = offer.alpha = 0f;
                hero.alpha = Ramp((skillTime - skillSeconds + HeroReturn) / HeroReturn) * exitOpacity;
                var tint = originalColor;
                tint.a *= Ramp(skillTime / SkillFade) * exitOpacity;
                output.color = tint;
                battle.Step((float)frameSeconds);
            }

            private static float Ramp(double value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)value));

            private void HideSkill()
            {
                UiAudioPlayback.End(ref sound);
                resumeSample = -1;
                if (battle != null)
                {
                    battle.Dispose();
                    battle = null;
                }
                if (output) { output.enabled = false; output.color = originalColor; }
                if (scenery) scenery.alpha = 1f;
                if (hero) hero.alpha = 1f;
                if (offer) offer.alpha = 1f;
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
            }
        }
    }
}
