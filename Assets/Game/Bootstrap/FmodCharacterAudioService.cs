using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

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
        private readonly BindingScope assetsLifetime;
        private bool disposed;
        private bool initializationStarted;
        private string diagnostic = "FMOD 尚未初始化";
        private string callbackError;
        public bool IsAvailable { get; private set; }
        public string Diagnostic => callbackError ?? diagnostic;
        private static readonly FMOD.Studio.EVENT_CALLBACK ProgrammerCallback = OnProgrammerSound;

        public FmodCharacterAudioService(CancellationToken token) => assetsLifetime = new BindingScope(token);

        public async Task InitializeAsync(IAssetService assets, IEnumerable<string> addresses)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FmodCharacterAudioService));
            if (initializationStarted) throw new InvalidOperationException("FMOD 初始化只能执行一次");
            initializationStarted = true;
            try
            {
                Check(FMOD.Studio.System.create(out studio), "create");
                Check(studio.getCoreSystem(out core), "getCoreSystem");
                Check(core.getVersion(out uint version), "getVersion");
                if (version != FMOD.VERSION.number)
                    throw new InvalidOperationException($"FMOD 版本不符：{version:x8}，需要 {FMOD.VERSION.number:x8}");
                Check(studio.initialize(128, FMOD.Studio.INITFLAGS.SYNCHRONOUS_UPDATE,
                    FMOD.INITFLAGS.NORMAL, IntPtr.Zero), "initialize");
                foreach (string address in addresses.Distinct(StringComparer.Ordinal))
                {
                    assetsLifetime.Token.ThrowIfCancellationRequested();
                    var bytes = await assets.LoadAsync<TextAsset>(address, assetsLifetime);
                    Check(studio.loadBankMemory(bytes.bytes, FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out var bank), address);
                    banks.Add(bank);
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
                Check(studio.update(), "update");
                return playback;
            }
            catch { playback.Dispose(); throw; }
        }

        public ICharacterAudioPlayback PlayVoice(string audioTableKey)
        {
            RequireReady();
            if (string.IsNullOrWhiteSpace(audioTableKey)) throw new ArgumentException("语音 key 为空", nameof(audioTableKey));
            Check(studio.getSoundInfo(audioTableKey, out var info), "getSoundInfo " + audioTableKey);
            Check(core.createSound(info.name_or_data, info.mode | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF,
                ref info.exinfo, out var sound), "createSound " + audioTableKey);
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
            Check(studio.update(), "pause update");
        }

        public void Tick()
        {
            if (!IsAvailable || disposed) return;
            Check(studio.update(), "update");
            foreach (var playback in active.ToArray())
                if (playback.HasFinished()) playback.Dispose();
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
                    var originalLookup = playback.owner.studio.getSoundInfo(requestedKey, out var info);
                    string key = JapaneseAudioTableKey(requestedKey);
                    // Original events randomly contain locale-tagged programmer instruments.
                    // The selected Japanese bank exposes canonical bare keys. Always resolve
                    // the Japanese key even if a tagged key exists in another loaded table.
                    if (!string.Equals(key, requestedKey, StringComparison.Ordinal))
                        Check(playback.owner.studio.getSoundInfo(key, out info), "Japanese programmer key " + key);
                    else
                        Check(originalLookup, "programmer key " + key);
                    Check(playback.owner.core.createSound(info.name_or_data,
                        info.mode | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF, ref info.exinfo, out var sound), "programmer createSound");
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
            active.Clear(); banks.Clear(); assetsLifetime.Dispose();
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
