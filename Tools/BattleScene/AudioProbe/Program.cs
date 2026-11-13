using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AstralOath.Client.BattleScene;
using AstralOath.Client.Bootstrap;
using UnityEngine;

static class Program
{
    static long lastAudibleMilliseconds;
    static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Check(FMOD.RESULT result, string operation) { Require(result == FMOD.RESULT.OK, operation + ": " + result); }
    static float Meter(BattleVoice voice, FMOD.DSP meter, int milliseconds)
    {
        float peak = 0;
        lastAudibleMilliseconds = 0;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            voice.Tick();
            Require(voice.IsAvailable, "FMOD became unavailable during playback");
            Check(meter.getMeteringInfo(IntPtr.Zero, out var info), "get metering");
            for (int i = 0; i < info.numchannels; i++)
            {
                peak = Math.Max(peak, info.peaklevel[i]);
                if (info.peaklevel[i] > 0.00001f) lastAudibleMilliseconds = timer.ElapsedMilliseconds;
            }
            Thread.Sleep(10);
        }
        return peak;
    }
    static int Main(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("AudioProbe <project root> <read-only source sound root>");
            string root = Path.GetFullPath(args[0]);
            string sounds = Path.GetFullPath(args[1]);
            string native = Path.Combine(root, "Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudioL.dll");
            NativeLibrary.SetDllImportResolver(typeof(FMOD.System).Assembly, (name, assembly, path) =>
                name.StartsWith("fmod", StringComparison.Ordinal) ? NativeLibrary.Load(native) : IntPtr.Zero);
            string json = File.ReadAllText(Path.Combine(root, "Assets/Game/Modules/CharacterLab/Configs/catalog.json"));
            var addresses = JsonDocument.Parse(json).RootElement.GetProperty("sharedBanks").EnumerateArray().Select(a => a.GetString()).ToArray();
            var assets = addresses.Select(address => new TextAsset(File.ReadAllBytes(Path.Combine(sounds,
                address.Substring("characterlab/source/sound/".Length))), Path.GetFileName(address))).ToArray();
            var catalog = new TextAsset(System.Text.Encoding.UTF8.GetBytes(json), "catalog");
            var impact = new TextAsset(File.ReadAllBytes(Path.Combine(sounds, "gerneal.bank")), "gerneal.bank");
            var constructor = typeof(BattleVoice).GetConstructor(new[] { typeof(TextAsset), typeof(TextAsset[]), typeof(TextAsset) });
            using var voice = constructor == null ? new BattleVoice(catalog, assets) : (BattleVoice)constructor.Invoke(new object[] { catalog, assets, impact });
            Require(voice.IsAvailable, "BattleVoice must initialize source banks");
            var service = Field<FmodCharacterAudioService>(voice, "service");
            var core = Field<FMOD.System>(service, "core");
            Check(core.setOutput(FMOD.OUTPUTTYPE.NOSOUND), "select silent output");
            Check(core.getMasterChannelGroup(out var master), "master group");
            Check(master.getDSP(0, out var meter), "master DSP");
            Check(meter.setMeteringEnabled(false, true), "enable metering");
            int effects = voice.StartedEffectCount;
            voice.Play("preview.slime.s1");
            Require(voice.StartedEffectCount == effects + 1, "Slime basic attack must start its source model/ani/skill1 event");
            float attackPeak = Meter(voice, meter, 1300);
            Require(attackPeak > 0.00001f, "Slime attack produced no audio samples");
            var playHit = typeof(BattleVoice).GetMethod("PlayHit", new[] { typeof(string), typeof(string) });
            Require(playHit != null, "Committed damage must have a target hit audio entry point");
            var hitCount = typeof(BattleVoice).GetProperty("StartedHitCount");
            Require(hitCount != null, "Hit playback must expose diagnostics");
            foreach (string target in new[] { "c1001", "c1005", "c1062", "preview.slime" })
            {
                Meter(voice, meter, 200);
                int before = (int)hitCount.GetValue(voice);
                playHit.Invoke(voice, new object[] { target, target == "preview.slime" ? "preview.slime.s1" : null });
                Require((int)hitCount.GetValue(voice) == before + 1, "Hit must start exactly one source event for " + target);
                float peak = Meter(voice, meter, 1800);
                Require(peak > 0.00001f, "Hit produced no audio samples for " + target);
                Require(target == "preview.slime" || lastAudibleMilliseconds > 100,
                    "Attacked voice was truncated at its short FMOD event timeline: " + target);
                Console.WriteLine(JsonSerializer.Serialize(new { target, peak, lastAudibleMilliseconds }));
            }
            int count = (int)hitCount.GetValue(voice);
            playHit.Invoke(voice, new object[] { "unknown", "unknown" });
            Require((int)hitCount.GetValue(voice) == count, "Unknown units must not substitute unrelated voices");
            voice.Dispose();
            voice.Play("preview.slime.s1");
            playHit.Invoke(voice, new object[] { "c1001", "preview.slime.s1" });
            Require(!voice.IsAvailable && (int)hitCount.GetValue(voice) == count, "Disposed battle must not restart audio");
            Require(!Field<FMOD.Studio.System>(service, "studio").isValid(), "Disposed battle retained its native system");
            // Exercise production callbacks for all roster paths using the same loaded source banks.
            using var rosterVoice = new BattleVoice(catalog, assets, impact);
            Require(rosterVoice.IsAvailable, "Roster audio could not initialize");
            var rosterAudio = Field<FmodCharacterAudioService>(rosterVoice, "service");
            var rosterCore = Field<FMOD.System>(rosterAudio, "core");
            Check(rosterCore.setOutput(FMOD.OUTPUTTYPE.NOSOUND), "select roster silent output");
            Check(rosterCore.getMasterChannelGroup(out var rosterMaster), "roster master group");
            Check(rosterMaster.getDSP(0, out var rosterMeter), "roster master DSP");
            Check(rosterMeter.setMeteringEnabled(false, true), "enable roster metering");
            rosterAudio.CapturePerformanceDiagnostics = true;
            using var parsedCatalog = JsonDocument.Parse(json);
            var skills = parsedCatalog.RootElement.GetProperty("characters").EnumerateArray()
                .SelectMany(character => character.GetProperty("skills").EnumerateArray()).ToArray();
            foreach (var skill in skills) rosterVoice.Play(skill.GetProperty("id").GetString());
            Require(rosterVoice.StartedEffectCount == skills.Length && rosterVoice.StartedVoiceCount == skills.Length,
                "All catalog skill effects and voices must begin");
            float rosterPeak = Meter(rosterVoice, rosterMeter, 7000);
            Require(rosterPeak > 0.00001f && rosterAudio.ProgrammerCreateSoundCount >= skills.Length,
                "The roster must produce real samples while source-missing intervals are skipped");
            Console.WriteLine(JsonSerializer.Serialize(new { rosterSkills = skills.Length,
                voiceCallbacks = rosterAudio.ProgrammerCreateSoundCount, rosterPeak, healthy = rosterVoice.IsAvailable }));
            Console.WriteLine(JsonSerializer.Serialize(new { result = "PASS", attackPeak, output = "NOSOUND" }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
