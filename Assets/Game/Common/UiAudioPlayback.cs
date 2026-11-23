using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralOath.Client.Common
{
    /// <summary>One 2D UI clip with a lifetime independent of the window that started it.</summary>
    [DisallowMultipleComponent]
    public sealed class UiAudioPlayback : MonoBehaviour
    {
        public const float FadeSeconds = .20f;
        private AudioSource output, owner;
        private double fadeStart = -1d;
        private float startVolume;
        private bool paused;
        private Action releaseClip;

        // The content provider supplies a separate handle only for clips owned by its current package.
        public static Func<AudioClip, Action> AcquireClipLease { private get; set; }
        public static string ClipAddress(string clipName) => "ui/audio/" + clipName.ToLowerInvariant();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetContentProvider() => AcquireClipLease = null;

        public bool IsPlaying => output && output.isPlaying;
        public int SamplePosition => IsPlaying ? output.timeSamples : -1;

        public static UiAudioPlayback Play(AudioSource settings, AudioClip clip, bool loop = false, int startSample = 0) =>
            Play(settings, clip, loop, startSample, 1f);

        private static UiAudioPlayback Play(AudioSource settings, AudioClip clip, bool loop, int startSample, float gain)
        {
            if (!settings || !settings.isActiveAndEnabled || !clip) return null;
            var host = new GameObject("UI audio: " + clip.name);
            DontDestroyOnLoad(host);
            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = settings.outputAudioMixerGroup;
            source.volume = settings.volume * gain;
            source.pitch = settings.pitch;
            source.panStereo = settings.panStereo;
            source.priority = settings.priority;
            source.mute = settings.mute;
            source.ignoreListenerPause = settings.ignoreListenerPause;
            source.ignoreListenerVolume = settings.ignoreListenerVolume;
            source.bypassEffects = settings.bypassEffects;
            source.bypassListenerEffects = settings.bypassListenerEffects;
            source.bypassReverbZones = settings.bypassReverbZones;
            source.reverbZoneMix = settings.reverbZoneMix;
            source.loop = loop;
            source.clip = clip;
            if (startSample > 0) source.timeSamples = Mathf.Min(startSample, clip.samples - 1);
            var playback = host.AddComponent<UiAudioPlayback>();
            playback.owner = settings;
            playback.output = source;
            try
            {
                playback.releaseClip = AcquireClipLease?.Invoke(clip);
                source.Play();
            }
            catch { Destroy(host); throw; }
            return playback;
        }

        public static void End(ref UiAudioPlayback current)
        {
            if (current) current.FadeOut();
            current = null;
        }

        // A window can start several overlapping cues, then pause or release only the ones it owns.
        public sealed class OneShots
        {
            private readonly List<UiAudioPlayback> playing = new List<UiAudioPlayback>();

            public UiAudioPlayback Play(AudioSource settings, AudioClip clip, float volumeScale = 1f)
            {
                playing.RemoveAll(item => !item);
                var playback = UiAudioPlayback.Play(settings, clip, false, 0, volumeScale);
                if (playback) playing.Add(playback);
                return playback;
            }

            public void SetPaused(bool value)
            {
                foreach (var item in playing)
                    if (item) item.SetPaused(value);
            }

            public void FadeOut()
            {
                foreach (var item in playing)
                    if (item) item.FadeOut();
                playing.Clear();
            }
        }

        private void SetPaused(bool value)
        {
            if (!output || fadeStart >= 0d || paused == value || (value && !IsPlaying)) return;
            paused = value;
            if (paused) output.Pause();
            else output.UnPause();
        }

        public void FadeOut()
        {
            if (fadeStart >= 0d || !enabled) return;
            if (!IsPlaying) { Release(); return; }
            startVolume = output.volume;
            fadeStart = Time.realtimeSinceStartupAsDouble;
            owner = null;
        }

        private void Update()
        {
            bool listenerPaused = output && !output.ignoreListenerPause && AudioListener.pause;
            if (!IsPlaying && !listenerPaused && !paused) { Release(); return; }
            if (fadeStart < 0d)
            {
                if (!owner || !owner.isActiveAndEnabled) FadeOut();
                return;
            }
            float ratio = (float)((Time.realtimeSinceStartupAsDouble - fadeStart) / FadeSeconds);
            output.volume = startVolume * (1f - Mathf.SmoothStep(0f, 1f, ratio));
            if (ratio >= 1f) Release();
        }

        private void Release()
        {
            enabled = false;
            if (output) { output.Stop(); output.clip = null; }
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            var release = releaseClip;
            releaseClip = null;
            release?.Invoke();
        }
    }
}
