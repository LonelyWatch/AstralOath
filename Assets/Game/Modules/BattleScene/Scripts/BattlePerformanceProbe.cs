#if ASTRAL_BATTLE_PERFORMANCE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Shared.Battle;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;

namespace AstralOath.Client.BattleScene
{
    // Included only by BattlePerformanceBuild.extraScriptingDefines; never in a release player.
    // The driver submits normal UI commands. Reflection only observes private presentation state.
    [DefaultExecutionOrder(32000)]
    public sealed class BattlePerformanceProbe : MonoBehaviour
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo SimulationField = typeof(BattleHudPreview).GetField("simulation", Private);
        private static long runtimeStarted;
        private readonly Report report = new Report();
        private readonly List<FrameSample> frames = new List<FrameSample>(20000);
        private readonly HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
        private readonly string[] expected = { "c1001.s1", "c1001.s2", "c1001.s3", "c1005.s1", "c1005.s2", "c1005.s3", "c1062.s1", "c1062.s2", "c1062.s3" };
        private static readonly int[] SkillPriority = { 1, 2, 0 };
        private BattleHudPreview host;
        private BindingMap map;
        private GameButton[] buttons;
        private Func<bool> busy;
        private Func<bool> canAct;
        private Queue<BattleFlowEvent> events;
        private FmodCharacterAudioService audio;
        private object observedSimulation;
        private BattleVoice observedVoice;
        private bool restartRequested;
        private string matchTimingMode = "probe Awake to rendered interactive readiness";
        private ProfilerRecorder allocated, playerLoop;
        private bool automatic, ready, finished;
        private ulong observedSequence;
        private long lastFrame, matchStarted;
        private int readyRenderedFrames, initialCollections;
        private InputSample pending;
        private string output;
        private int voiceTotal, soundTotal, hitTotal, programmerCallbacks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void BeginRuntime() => runtimeStarted = Stopwatch.GetTimestamp();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Application.isEditor) return;
            var root = new GameObject("Battle performance diagnostic probe");
            DontDestroyOnLoad(root);
            root.AddComponent<BattlePerformanceProbe>();
        }

        private void Awake()
        {
            if (runtimeStarted == 0) runtimeStarted = Stopwatch.GetTimestamp();
            automatic = Environment.GetCommandLineArgs().Contains("-astral-perf-auto");
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            output = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Results");
            if (!string.Equals(Path.GetPathRoot(output), @"D:\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("性能报告必须位于 D 盘。");
            Directory.CreateDirectory(output);
            // These two files belong exclusively to this diagnostic probe, never to releases.
            File.Delete(Path.Combine(output, "ready.json"));
            File.Delete(Path.Combine(output, "report.json"));
            report.runId = Guid.NewGuid().ToString("N");
            report.utcStarted = DateTime.UtcNow.ToString("O");
            report.runtimeBackend = "Mono (diagnostic only; not a HybridCLR release)";
            report.isEditor = Application.isEditor;
            report.gpu = SystemInfo.graphicsDeviceName;
            report.graphicsApi = SystemInfo.graphicsDeviceType.ToString();
            report.cpu = SystemInfo.processorType;
            report.unity = Application.unityVersion;
            report.targetFPS = Application.targetFrameRate;
            report.automatic = automatic;
            report.frameMeasurement = "Stopwatch between rendered end-of-frame samples; includes rendering, waiting and probe overhead. PlayerLoop/GC are the latest completed ProfilerRecorder frame.";
            report.inputPath = "EventSystem pointerClickHandler -> GameButton listener; TrySelectBattleTarget -> ChooseTarget -> authoritative Submit. OS mouse injection, target raycast and UI occlusion are not exercised.";
            initialCollections = GC.CollectionCount(0);
            allocated = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            playerLoop = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop", 1);
            report.gcRecorderValid = allocated.Valid;
            report.playerLoopRecorderValid = playerLoop.Valid;
            Application.logMessageReceived += OnLog;
            lastFrame = matchStarted = Stopwatch.GetTimestamp();
        }

        private IEnumerator Start()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Finish(false, "Diagnostic player requires a rendered window; -nographics is unsupported.");
                yield break;
            }
            var endOfFrame = new WaitForEndOfFrame();
            while (!finished)
            {
                yield return endOfFrame;
                if (finished) yield break;
                if (finished) yield break;
                long now = Stopwatch.GetTimestamp();
                long frameStarted = lastFrame;
                frames.Add(new FrameSample
                {
                    elapsedMs = Milliseconds(runtimeStarted, now), frameMs = Milliseconds(lastFrame, now),
                    gcAllocatedBytes = allocated.Valid && allocated.Count > 0 ? allocated.LastValue : -1,
                    playerLoopMs = playerLoop.Valid && playerLoop.Count > 0 ? playerLoop.LastValue / 1000000d : -1,
                    match = report.matches, input = report.inputs.Count,
                    phase = ready ? "playable" : report.firstReadyFrame > 0 ? "restart" : "startup"
                });
                lastFrame = now;
                if (automatic && Milliseconds(runtimeStarted, now) > 240000)
                {
                    Finish(false, "Automatic diagnostic exceeded 240 seconds, including startup and restart readiness.");
                    continue;
                }
                if (!host)
                {
                    host = FindObjectOfType<BattleHudPreview>();
                    if (host)
                    {
                        map = host.gameObject.scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<BindingMap>(true)).Single();
                        buttons = Enumerable.Range(1, 3).Select(index => map.Require<GameButton>("skill" + index)).ToArray();
                    }
                }
                if (!host || host.SimulationSnapshot == null)
                {
                    if (Milliseconds(runtimeStarted, now) > 60000) Finish(false, "No battle became available within 60 seconds.");
                    continue;
                }
                object currentSimulation = SimulationField.GetValue(host);
                if (!ReferenceEquals(currentSimulation, observedSimulation))
                    BindPresentationObservation(currentSimulation, frameStarted);
                if (audio == null || !audio.IsAvailable)
                {
                    report.errors.Add("FMOD unavailable: " + (audio?.Diagnostic ?? "BattleVoice has no native service"));
                    Finish(false, "Native audio readiness or runtime diagnostic failed.");
                    continue;
                }
                ObserveCommands();
                if (!ready)
                {
                    // Readiness includes visible, enabled inputs and two actual rendered frames.
                    if (!canAct() || !buttons.Any(button => button.IsActive() && button.IsInteractable()))
                    { readyRenderedFrames = 0; continue; }
                    if (++readyRenderedFrames < 2) continue;
                    ready = true;
                    double elapsed = Milliseconds(matchStarted, now);
                    report.matchReadyMs.Add(elapsed);
                    report.matchReadyTimingModes.Add(matchTimingMode);
                    if (report.matches == 1)
                    {
                        report.runtimeStartToReadyMs = Milliseconds(runtimeStarted, now);
                        report.firstReadyFrame = frames.Count;
                        File.WriteAllText(Path.Combine(output, "ready.json"), JsonUtility.ToJson(report, true));
                    }
                }
                if (pending != null && !busy()) CompletePending();
                if (!automatic) continue;
                if (covered.Count == expected.Length && pending == null && !busy())
                {
                    Finish(true, "Nine skills completed through the normal battle command and presentation paths.");
                    continue;
                }
                if (Milliseconds(runtimeStarted, now) > 240000 || report.inputs.Count >= 80 || report.matches >= 8 && host.SimulationSnapshot.Outcome != BattleOutcome.InProgress)
                {
                    Finish(false, "Automatic coverage budget exceeded (240 seconds / 80 inputs / 8 matches).");
                    continue;
                }
                if (!busy() && host.SimulationSnapshot.Outcome != BattleOutcome.InProgress)
                {
                    restartRequested = true;
                    matchStarted = Stopwatch.GetTimestamp();
                    ready = false;
                    readyRenderedFrames = 0;
                    host.RestartBattle();
                    continue;
                }
                if (canAct() && pending == null) SubmitNextSkill();
            }
        }

        private void Update()
        {
            if (!finished && automatic && Milliseconds(runtimeStarted, Stopwatch.GetTimestamp()) > 240000)
            {
                Finish(false, "Independent Update watchdog exceeded 240 seconds.");
                return;
            }
            if (!finished && automatic && host && host.IsPaused)
            {
                Finish(false, "Automatic run interrupted by external pause; paused frames are not a performance baseline.");
                return;
            }
            // If another component or the coroutine throws, still emit a bounded failure report.
            if (!finished && automatic && report.errors.Count != 0)
                Finish(false, "Runtime error captured; inspect errors in this report.");
        }

        private void BindPresentationObservation(object simulation, long frameStarted)
        {
            if (simulation == null) throw new InvalidOperationException("Battle simulation is absent.");
            if (observedSimulation != null)
            {
                voiceTotal += observedVoice.StartedVoiceCount;
                soundTotal += observedVoice.StartedEffectCount;
                hitTotal += observedVoice.StartedHitCount;
                programmerCallbacks += audio?.ProgrammerCreateSoundCount ?? 0;
                if (pending != null)
                {
                    pending.aborted = true;
                    pending.endFrame = frames.Count;
                    pending = null;
                }
                if (!restartRequested) matchStarted = frameStarted;
                matchTimingMode = restartRequested ? "RestartBattle call to rendered interactive readiness" :
                    "frame containing externally requested restart to rendered readiness; restart-call timestamp unavailable";
                ready = false;
                readyRenderedFrames = 0;
                observedSequence = 0;
                // A manual restart happened during this measured interval, before identity detection.
                var sample = frames[frames.Count - 1];
                sample.phase = "restart";
                sample.match = report.matches + 1;
                frames[frames.Count - 1] = sample;
            }
            restartRequested = false;
            observedSimulation = simulation;
            var type = simulation.GetType();
            busy = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), simulation, type.GetProperty("Busy", Private).GetGetMethod(true));
            canAct = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), simulation, type.GetProperty("CanAct", Private).GetGetMethod(true));
            events = (Queue<BattleFlowEvent>)type.GetField("playback", Private).GetValue(simulation);
            observedVoice = (BattleVoice)type.GetField("voice", Private).GetValue(simulation);
            audio = (FmodCharacterAudioService)typeof(BattleVoice).GetField("service", Private).GetValue(observedVoice);
            if (audio != null) audio.CapturePerformanceDiagnostics = true;
            report.matches++;
        }

        private void ObserveCommands()
        {
            ulong sequence = host.SimulationSnapshot.LastCommandSequence;
            if (sequence == observedSequence) return;
            if (sequence < observedSequence)
            {
                report.errors.Add("Command sequence reset without a new simulation identity.");
                observedSequence = sequence;
                return;
            }
            report.acceptedCommands += (int)(sequence - observedSequence);
            observedSequence = sequence;
            foreach (var item in events)
            {
                if (item.Kind != BattleFlowEventKind.Action) continue;
                if (item.Origin == BattleActionOrigin.DualAttack) report.dualAttackEvents++;
                if (item.Origin == BattleActionOrigin.Counter) report.counterEvents++;
                if (host.SimulationSnapshot.GetUnit(item.SourceId)?.Slot.Side == BattleSide.Enemy) report.enemyActionEvents++;
            }
        }

        private void SubmitNextSkill()
        {
            var snapshot = host.SimulationSnapshot;
            var actor = snapshot.GetUnit(snapshot.CurrentActorId);
            int selected = -1;
            // Across matches, choose genuinely unplayed skills before falling back to S1.
            foreach (int index in SkillPriority)
                if (buttons[index].IsActive() && buttons[index].IsInteractable() && !covered.Contains(actor.Form.SkillIds[index]))
                { selected = index; break; }
            if (selected < 0) selected = 0;
            if (!buttons[selected].IsActive() || !buttons[selected].IsInteractable()) return;
            string skill = actor.Form.SkillIds[selected];
            bool friendly = actor.DefinitionId == "c1062" && selected > 0;
            var targets = snapshot.Units.Where(unit => unit.IsAlive && (unit.Slot.Side == actor.Slot.Side) == friendly);
            var target = friendly ? targets.FirstOrDefault() : targets.OrderByDescending(unit => unit.CurrentHealth).FirstOrDefault();
            if (target == null) { Finish(false, "No legal target for " + skill); return; }
            if (EventSystem.current == null) { Finish(false, "Missing EventSystem."); return; }
            var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
            ulong previousSequence = snapshot.LastCommandSequence;
            long started = Stopwatch.GetTimestamp();
            ExecuteEvents.Execute(buttons[selected].gameObject, pointer, ExecuteEvents.pointerClickHandler);
            bool accepted = host.TrySelectBattleTarget(target.InstanceId);
            double submitMs = Milliseconds(started, Stopwatch.GetTimestamp());
            var acceptedAction = events.FirstOrDefault(item => item.Kind == BattleFlowEventKind.Action &&
                item.Origin == BattleActionOrigin.Primary && item.SourceId == actor.InstanceId && item.SkillId == skill);
            var sample = new InputSample
            {
                skill = skill, actor = actor.InstanceId, target = target.InstanceId,
                acceptedSkill = acceptedAction?.SkillId,
                match = report.matches, startFrame = frames.Count,
                submitMs = submitMs, accepted = accepted
            };
            report.inputs.Add(sample);
            if (!accepted || host.SimulationSnapshot.LastCommandSequence != previousSequence + 1 || acceptedAction == null)
            { Finish(false, "Normal skill/target input was rejected: " + skill); return; }
            pending = sample;
            ObserveCommands();
        }

        private void CompletePending()
        {
            pending.endFrame = frames.Count;
            pending.completed = true;
            covered.Add(pending.skill);
            pending = null;
        }

        private static Summary Summarize(IEnumerable<FrameSample> source)
        {
            var samples = source.ToArray();
            var durations = samples.Select(sample => sample.frameMs).OrderBy(value => value).ToArray();
            if (durations.Length == 0) return new Summary();
            return new Summary
            {
                count = durations.Length, p95Ms = durations[(int)((durations.Length - 1) * .95)],
                p99Ms = durations[(int)((durations.Length - 1) * .99)], maxMs = durations[durations.Length - 1],
                over33ms = durations.Count(value => value > 33.333), over100ms = durations.Count(value => value > 100),
                recordedGcBytes = samples.Where(sample => sample.gcAllocatedBytes >= 0).Sum(sample => sample.gcAllocatedBytes)
            };
        }
        private static double Milliseconds(long from, long to) => (to - from) * (1000d / Stopwatch.Frequency);

        private void OnLog(string text, string trace, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert ||
                text.Contains("ASTRAL_BATTLE_AUDIO") ||
                type == LogType.Warning && text.IndexOf("FMOD", StringComparison.OrdinalIgnoreCase) >= 0) && report.errors.Count < 32)
                report.errors.Add(text);
        }
        private void Finish(bool success, string reason)
        {
            if (finished) return;
            finished = true;
            report.success = success && report.errors.Count == 0;
            report.reason = reason;
            report.utcFinished = DateTime.UtcNow.ToString("O");
            report.coveredSkills = covered.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            report.missingSkills = expected.Where(value => !covered.Contains(value)).ToArray();
            report.allNineCovered = report.missingSkills.Length == 0;
            report.nativeVoiceStarts = voiceTotal + (host ? host.StartedSkillVoices : 0);
            report.nativeEffectStarts = soundTotal + (host ? host.StartedSkillSounds : 0);
            report.nativeHitStarts = hitTotal + (host ? host.StartedHitSounds : 0);
            report.programmerSoundCreates = programmerCallbacks + (audio?.ProgrammerCreateSoundCount ?? 0);
            report.audioDiagnostic = audio?.Diagnostic ?? "Native audio service unavailable";
            report.nativeAudio = audio?.IsAvailable == true && report.programmerSoundCreates > 0 &&
                report.nativeVoiceStarts > 0 && report.nativeEffectStarts > 0 && report.errors.Count == 0;
            report.audioVerification = "FMOD service availability, successful event starts, actual programmer sound creation and runtime warnings/errors checked. Physical speaker output is not acoustically verified.";
            if (automatic && (!report.allNineCovered || !report.nativeAudio)) report.success = false;
            report.notCovered = new[] { report.dualAttackEvents == 0 ? "dual attack not observed" : "dual attack event observed; timing fidelity not evaluated", "counter timing not evaluated", "formal HybridCLR/IL2CPP release pipeline", "OS process creation to runtime initialization", "OS target mouse raycast and UI occlusion" };
            report.gcCollections = GC.CollectionCount(0) - initialCollections;
            foreach (var input in report.inputs.Where(input => input.completed || input.aborted))
                input.frames = Summarize(frames.Skip(input.startFrame).Take(input.endFrame - input.startFrame));
            report.allFrames = Summarize(frames);
            report.playableFrames = Summarize(frames.Where(frame => frame.phase == "playable"));
            report.startupFrames = Summarize(frames.Where(frame => frame.phase == "startup"));
            report.restartFrames = Summarize(frames.Where(frame => frame.phase == "restart"));
            report.slowFrames = frames.Where(frame => frame.frameMs > 33.333).ToArray();
            report.frameSamples = frames.ToArray();
            File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            allocated.Dispose(); playerLoop.Dispose();
            Application.logMessageReceived -= OnLog;
            if (automatic) Application.Quit(report.success && report.allNineCovered && report.nativeAudio ? 0 : 2);
        }
        private void OnApplicationQuit() { if (!finished) Finish(false, "Manual session ended; automated coverage not asserted."); }
        private void OnDestroy() { allocated.Dispose(); playerLoop.Dispose(); Application.logMessageReceived -= OnLog; }

        [Serializable] private sealed class Report
        {
            public int schemaVersion = 1, targetFPS, matches, acceptedCommands, enemyActionEvents, dualAttackEvents, counterEvents;
            public string runId, utcStarted, utcFinished, runtimeBackend, gpu, graphicsApi, cpu, unity, reason, frameMeasurement, inputPath, audioDiagnostic, audioVerification;
            public bool isEditor, automatic, success, nativeAudio, allNineCovered, gcRecorderValid, playerLoopRecorderValid;
            public double runtimeStartToReadyMs;
            public int firstReadyFrame, gcCollections, nativeVoiceStarts, nativeEffectStarts, nativeHitStarts, programmerSoundCreates;
            public List<double> matchReadyMs = new List<double>();
            public List<string> matchReadyTimingModes = new List<string>();
            public List<InputSample> inputs = new List<InputSample>();
            public List<string> errors = new List<string>();
            public string[] coveredSkills, missingSkills, notCovered;
            public Summary allFrames, playableFrames, startupFrames, restartFrames;
            public FrameSample[] slowFrames, frameSamples;
        }
        [Serializable] private sealed class InputSample
        {
            public string skill, acceptedSkill, actor, target;
            public int match, startFrame, endFrame;
            public double submitMs;
            public bool accepted, completed, aborted;
            public Summary frames;
        }
        [Serializable] private struct FrameSample
        {
            public string phase;
            public double elapsedMs, frameMs, playerLoopMs;
            public long gcAllocatedBytes;
            public int match, input;
        }
        [Serializable] private sealed class Summary
        {
            public int count, over33ms, over100ms;
            public double p95Ms, p99Ms, maxMs;
            public long recordedGcBytes;
        }
    }
}
#endif
