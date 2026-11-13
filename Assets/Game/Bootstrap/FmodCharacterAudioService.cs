using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;
using Unity.Profiling;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace AstralOath.Client.Bootstrap
{
    // Scene-owned bank/event lifetime. No RuntimeManager singleton or network dependency.
    public sealed class FmodCharacterAudioService : ICharacterAudioService, IDisposable
    {
        private FMOD.Studio.System studio;
        private FMOD.System core;
        private readonly List<FMOD.Studio.Bank> banks = new List<FMOD.Studio.Bank>();
        private readonly HashSet<Playback> active = new HashSet<Playback>();
        private readonly HashSet<Playback> callbackOwners = new HashSet<Playback>();
        private readonly List<Playback> finished = new List<Playback>();
        private readonly BindingScope assetsLifetime;
        private readonly HashSet<string> missingJapaneseKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> reportedMissingJapaneseKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> pendingMissingJapaneseWarnings = new List<string>();
        private static readonly byte[] SilentProgrammerPcm = new byte[320]; // 20 ms, 8 kHz, mono PCM16.
        private bool disposed;
        private bool initializationStarted;
        private string diagnostic = "FMOD 尚未初始化";
        private string callbackError;
        public bool IsAvailable { get; private set; }
        public string Diagnostic => callbackError ?? diagnostic;
        private static readonly FMOD.Studio.EVENT_CALLBACK ProgrammerCallback = OnProgrammerSound;
        private static readonly ProfilerMarker InitializeMarker = new ProfilerMarker("Astral.Audio.InitializeNative");
        private static readonly ProfilerMarker BankMarker = new ProfilerMarker("Astral.Audio.LoadBankMemory");
        private static readonly ProfilerMarker EventMarker = new ProfilerMarker("Astral.Audio.PlayEvent");
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("Astral.Audio.StudioUpdate");
        private static readonly ProfilerMarker LookupMarker = new ProfilerMarker("Astral.Audio.GetSoundInfo");
        private static readonly ProfilerMarker SoundMarker = new ProfilerMarker("Astral.Audio.CreateSound");
        private static readonly ProfilerMarker CallbackMarker = new ProfilerMarker("Astral.Audio.ProgrammerCallback");
        private static readonly ProfilerMarker TickMarker = new ProfilerMarker("Astral.Audio.Tick");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Opt-in, bounded by an explicit measurement window. Never log from the audio hot path.
        public bool CapturePerformanceDiagnostics { get; set; }
        public double PeakPlayEventMilliseconds { get; private set; }
        public double PeakUpdateMilliseconds { get; private set; }
        public double PeakCreateSoundMilliseconds { get; private set; }
        public double PeakProgrammerCallbackMilliseconds { get; private set; }
        public double PeakTickMilliseconds { get; private set; }
        public int ProgrammerCreateSoundCount { get; private set; }
        public int DiagnosticActivePlaybackCount => active.Count;
        private readonly HashSet<string> diagnosticSoundKeys = new HashSet<string>(StringComparer.Ordinal);
        public IEnumerable<string> DiagnosticSoundKeys => diagnosticSoundKeys;
        public void ResetPerformanceDiagnostics()
        {
            PeakPlayEventMilliseconds = PeakUpdateMilliseconds = PeakCreateSoundMilliseconds = 0;
            PeakProgrammerCallbackMilliseconds = PeakTickMilliseconds = 0;
            ProgrammerCreateSoundCount = 0;
            diagnosticSoundKeys.Clear();
        }
#endif
        private static double ElapsedMilliseconds(long started) =>
            (Stopwatch.GetTimestamp() - started) * (1000d / Stopwatch.Frequency);

        private void UpdateStudio(string operation)
        {
            using var marker = UpdateMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = CapturePerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0;
#endif
            try { Check(studio.update(), operation); }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (started != 0) PeakUpdateMilliseconds = Math.Max(PeakUpdateMilliseconds, ElapsedMilliseconds(started));
#endif
            }
        }

        private FMOD.RESULT GetSoundInfo(string key, out FMOD.Studio.SOUND_INFO info)
        {
            using var marker = LookupMarker.Auto();
            return studio.getSoundInfo(key, out info);
        }

        private FMOD.RESULT CreateSound(ref FMOD.Studio.SOUND_INFO info, out FMOD.Sound sound)
        {
            using var marker = SoundMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = CapturePerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0;
#endif
            try { return core.createSound(info.name_or_data, info.mode | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF, ref info.exinfo, out sound); }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (started != 0) PeakCreateSoundMilliseconds = Math.Max(PeakCreateSoundMilliseconds, ElapsedMilliseconds(started));
#endif
            }
        }

        public FmodCharacterAudioService(CancellationToken token) => assetsLifetime = new BindingScope(token);

        public Task InitializeAsync(IAssetService assets, IEnumerable<string> addresses) =>
            InitializeAsync(assets, addresses, null);

        public async Task InitializeAsync(IAssetService assets, IEnumerable<string> addresses, IEnumerable<string> absentJapaneseKeys)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FmodCharacterAudioService));
            if (initializationStarted) throw new InvalidOperationException("FMOD 初始化只能执行一次");
            initializationStarted = true;
            try
            {
                foreach (string key in absentJapaneseKeys ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("voc_", StringComparison.Ordinal) ||
                        !string.Equals(key, JapaneseAudioTableKey(key), StringComparison.Ordinal) || !missingJapaneseKeys.Add(key))
                        throw new InvalidOperationException("无效的缺失日语音频键：" + key);
                }
                // A Profiler scope must not cross await: native initialization is one synchronous segment.
                using (InitializeMarker.Auto())
                {
                    Check(FMOD.Studio.System.create(out studio), "create");
                    Check(studio.getCoreSystem(out core), "getCoreSystem");
                    Check(core.getVersion(out uint version), "getVersion");
                    if (version != FMOD.VERSION.number)
                        throw new InvalidOperationException($"FMOD 版本不符：{version:x8}，需要 {FMOD.VERSION.number:x8}");
                    Check(studio.initialize(128, FMOD.Studio.INITFLAGS.SYNCHRONOUS_UPDATE,
                        FMOD.INITFLAGS.NORMAL, IntPtr.Zero), "initialize");
                }
                foreach (string address in addresses.Distinct(StringComparer.Ordinal))
                {
                    assetsLifetime.Token.ThrowIfCancellationRequested();
                    var bytes = await assets.LoadAsync<TextAsset>(address, assetsLifetime);
                    using (BankMarker.Auto())
                    {
                        Check(studio.loadBankMemory(bytes.bytes, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out var bank), address);
                        banks.Add(bank);
                    }
                }
                Check(studio.flushCommands(), "flushCommands");
                IsAvailable = true;
                diagnostic = "FMOD 2.03.15 / 日语音频库已加载";
            }
            catch (Exception ex)
            {
                diagnostic = "FMOD 初始化失败：" + ex.Message;
                Dispose();
                throw;
            }
        }

        public ICharacterAudioPlayback PlayEvent(string eventPath)
        {
            using var marker = EventMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = CapturePerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0;
#endif
            try { return PlayEventCore(eventPath); }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (started != 0) PeakPlayEventMilliseconds = Math.Max(PeakPlayEventMilliseconds, ElapsedMilliseconds(started));
#endif
            }
        }

        private ICharacterAudioPlayback PlayEventCore(string eventPath)
        {
            RequireReady();
            if (string.IsNullOrWhiteSpace(eventPath)) throw new ArgumentException("音频事件路径为空", nameof(eventPath));
            eventPath = eventPath.Replace('\\', '/');
            if (!eventPath.StartsWith("event:/", StringComparison.Ordinal)) eventPath = "event:/" + eventPath.TrimStart('/');
            Check(studio.getEvent(eventPath, out var description), eventPath);
            Check(description.createInstance(out var instance), "createInstance");
            var playback = new Playback(this, instance);
            try
            {
                playback.AttachProgrammerCallback();
                active.Add(playback);
                Check(instance.start(), "start " + eventPath);
                UpdateStudio("update");
                return playback;
            }
            catch { playback.Dispose(); throw; }
        }

        public ICharacterAudioPlayback PlayVoice(string audioTableKey)
        {
            RequireReady();
            if (string.IsNullOrWhiteSpace(audioTableKey)) throw new ArgumentException("语音 key 为空", nameof(audioTableKey));
            Check(GetSoundInfo(audioTableKey, out var info), "getSoundInfo " + audioTableKey);
            Check(CreateSound(ref info, out var sound), "createSound " + audioTableKey);
            try
            {
                var selected = sound;
                if (info.subsoundindex >= 0) Check(sound.getSubSound(info.subsoundindex, out selected), "getSubSound");
                Check(core.playSound(selected, default, false, out var channel), "playSound");
                var playback = new Playback(this, sound, channel);
                active.Add(playback);
                return playback;
            }
            catch { sound.release(); throw; }
        }

        // Pause only this scene-owned audio system, never global Unity or other FMOD sessions.
        public void PauseAll(bool paused)
        {
            if (!IsAvailable || disposed) return;
            foreach (var playback in active) playback.Pause(paused);
            UpdateStudio("pause update");
        }

        public void Tick()
        {
            using var marker = TickMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = CapturePerformanceDiagnostics ? Stopwatch.GetTimestamp() : 0;
#endif
            try { TickCore(); }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (started != 0) PeakTickMilliseconds = Math.Max(PeakTickMilliseconds, ElapsedMilliseconds(started));
#endif
            }
        }

        private void TickCore()
        {
            if (!IsAvailable || disposed) return;
            UpdateStudio("update");
            foreach (string key in pendingMissingJapaneseWarnings)
                Debug.LogWarning("FMOD 源音频表缺少日语片段，跳过该段：" + key);
            pendingMissingJapaneseWarnings.Clear();
            // Collect first: Dispose removes the instance from active. Reuse storage across frames.
            finished.Clear();
            foreach (var playback in active)
                if (playback.HasFinished()) finished.Add(playback);
            foreach (var playback in finished) playback.Dispose();
            finished.Clear();
            if (callbackError != null)
            {
                diagnostic = callbackError;
                // Dispose from the main-thread Tick after native callbacks return. Merely
                // disabling Tick would strand queued stops with SYNCHRONOUS_UPDATE.
                Dispose();
            }
        }

        internal void DisableAfterRuntimeFailure(Exception error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));
            diagnostic = "FMOD 运行期失败：" + error.Message;
            callbackError = diagnostic;
            Dispose();
        }
        private void RequireReady()
        {
            if (disposed) throw new ObjectDisposedException(nameof(FmodCharacterAudioService));
            if (!IsAvailable) throw new InvalidOperationException(Diagnostic);
        }

        private static void Check(FMOD.RESULT result, string operation)
        {
            if (result != FMOD.RESULT.OK)
                throw new InvalidOperationException($"FMOD {operation}: {result} ({FMOD.Error.String(result)})");
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.Studio.EVENT_CALLBACK))]
        private static FMOD.RESULT OnProgrammerSound(FMOD.Studio.EVENT_CALLBACK_TYPE type, IntPtr eventPtr, IntPtr parameters)
        {
            using var marker = CallbackMarker.Auto();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            long started = Stopwatch.GetTimestamp();
#endif
            Playback playback = null;
            try
            {
                var instance = new FMOD.Studio.EventInstance(eventPtr);
                var result = instance.getUserData(out var user);
                if (result != FMOD.RESULT.OK || user == IntPtr.Zero) return result;
                var handle = GCHandle.FromIntPtr(user);
                playback = handle.Target as Playback;
                if (playback == null) return FMOD.RESULT.ERR_INVALID_HANDLE;
                if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND)
                {
                    var properties = Marshal.PtrToStructure<FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES>(parameters);
                    string requestedKey = properties.name;
                    string key = JapaneseAudioTableKey(requestedKey);
                    var lookup = playback.owner.GetSoundInfo(key, out var info);
                    if (lookup == FMOD.RESULT.ERR_EVENT_NOTFOUND && playback.owner.missingJapaneseKeys.Contains(key))
                    {
                        // Give FMOD a valid silent sound so it can continue the source event timeline.
                        // The destroy callback owns this handle just like an ordinary programmer sound.
                        var silence = new FMOD.CREATESOUNDEXINFO
                        {
                            cbsize = Marshal.SizeOf<FMOD.CREATESOUNDEXINFO>(), length = (uint)SilentProgrammerPcm.Length,
                            defaultfrequency = 8000, numchannels = 1, format = FMOD.SOUND_FORMAT.PCM16
                        };
                        Check(playback.owner.core.createSound(SilentProgrammerPcm,
                            FMOD.MODE.OPENRAW | FMOD.MODE.OPENMEMORY | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_NORMAL,
                            ref silence, out var silentSound), "programmer create silent interval");
                        properties.sound = silentSound.handle;
                        properties.subsoundIndex = -1;
                        Marshal.StructureToPtr(properties, parameters, false);
                        if (playback.owner.reportedMissingJapaneseKeys.Add(key))
                            playback.owner.pendingMissingJapaneseWarnings.Add(key);
                        return FMOD.RESULT.OK;
                    }
                    Check(lookup, "Japanese programmer key " + key);
                    Check(playback.owner.CreateSound(ref info, out var sound), "programmer createSound");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (playback.owner.CapturePerformanceDiagnostics)
                    {
                        playback.owner.ProgrammerCreateSoundCount++;
                        playback.owner.diagnosticSoundKeys.Add(key);
                    }
#endif
                    properties.sound = sound.handle;
                    properties.subsoundIndex = info.subsoundindex;
                    Marshal.StructureToPtr(properties, parameters, false);
                }
                else if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND)
                {
                    var properties = Marshal.PtrToStructure<FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES>(parameters);
                    if (properties.sound != IntPtr.Zero) new FMOD.Sound(properties.sound).release();
                }
                else if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED)
                {
                    playback.nativeDestroyed = true;
                    instance.setUserData(IntPtr.Zero);
                    if (handle.IsAllocated) handle.Free();
                    playback.hasHandle = false;
                    playback.owner.callbackOwners.Remove(playback);
                }
                return FMOD.RESULT.OK;
            }
            catch (Exception ex)
            {
                if (playback != null) playback.owner.callbackError = "FMOD 回调失败：" + ex.Message;
                return FMOD.RESULT.ERR_INTERNAL;
            }
            finally
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (playback?.owner.CapturePerformanceDiagnostics == true)
                    playback.owner.PeakProgrammerCallbackMilliseconds = Math.Max(
                        playback.owner.PeakProgrammerCallbackMilliseconds, ElapsedMilliseconds(started));
#endif
            }
        }

        private static string JapaneseAudioTableKey(string requestedKey)
        {
            if (string.IsNullOrWhiteSpace(requestedKey))
                throw new ArgumentException("Programmer sound key is empty", nameof(requestedKey));
            // These locale markers occur in the imported source event tables; no arbitrary
            // parenthesized suffix is stripped and a missing canonical key remains an error.
            foreach (string suffix in new[] { "(zhs)", "(en)", "(kr)", "(ja)", "(zht)", "(zl)" })
                if (requestedKey.EndsWith(suffix, StringComparison.Ordinal))
                    return requestedKey.Substring(0, requestedKey.Length - suffix.Length);
            return requestedKey;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            IsAvailable = false;
            var releasing = active.ToArray();
            foreach (var playback in releasing) playback.Dispose();
            if (studio.isValid())
            {
                studio.flushCommands();
                studio.update();
                for (int i = banks.Count - 1; i >= 0; --i) banks[i].unload();
                studio.release();
                studio.clearHandle();
            }
            // After system release no native callback can retain these handles.
            foreach (var playback in callbackOwners.ToArray()) playback.FreeHandleAfterSystemRelease();
            callbackOwners.Clear();
            active.Clear(); finished.Clear(); banks.Clear(); assetsLifetime.Dispose();
        }

        private sealed class Playback : ICharacterAudioPlayback
        {
            internal readonly FmodCharacterAudioService owner;
            private FMOD.Studio.EventInstance instance;
            private FMOD.Sound sound;
            private FMOD.Channel channel;
            private readonly bool isEvent;
            private bool disposed;
            internal bool nativeDestroyed;
            internal bool hasHandle;
            private GCHandle handle;
            internal Playback(FmodCharacterAudioService owner, FMOD.Studio.EventInstance instance)
            { this.owner = owner; this.instance = instance; isEvent = true; }
            internal Playback(FmodCharacterAudioService owner, FMOD.Sound sound, FMOD.Channel channel)
            { this.owner = owner; this.sound = sound; this.channel = channel; }
            internal void AttachProgrammerCallback()
            {
                handle = GCHandle.Alloc(this); hasHandle = true;
                owner.callbackOwners.Add(this);
                try
                {
                    Check(instance.setUserData(GCHandle.ToIntPtr(handle)), "setUserData");
                    Check(instance.setCallback(ProgrammerCallback, FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND |
                        FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND | FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED), "setCallback");
                }
                catch { instance.setUserData(IntPtr.Zero); handle.Free(); hasHandle = false; owner.callbackOwners.Remove(this); throw; }
            }
            public void Pause(bool paused)
            {
                if (disposed) return;
                Check(isEvent ? instance.setPaused(paused) : channel.setPaused(paused), "pause");
            }
            internal bool HasFinished()
            {
                if (disposed) return true;
                if (isEvent)
                    return nativeDestroyed || instance.getPlaybackState(out var state) == FMOD.RESULT.OK && state == FMOD.Studio.PLAYBACK_STATE.STOPPED;
                return channel.isPlaying(out bool playing) != FMOD.RESULT.OK || !playing;
            }
            public void Stop() => Dispose();
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (isEvent && instance.isValid() && !nativeDestroyed)
                { instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE); instance.release(); }
                else if (!isEvent)
                { if (channel.hasHandle()) channel.stop(); if (sound.hasHandle()) sound.release(); }
                owner.active.Remove(this);
            }
            internal void FreeHandleAfterSystemRelease()
            { if (hasHandle) { handle.Free(); hasHandle = false; } }
        }
    }
}
