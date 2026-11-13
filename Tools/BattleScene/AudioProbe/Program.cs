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
    static void AuditVariants(BattleVoice voice, FmodCharacterAudioService service, FMOD.Studio.System studio,
        FMOD.DSP meter, string json, string[] skills)
    {
        using var catalog = JsonDocument.Parse(json);
        var rows = catalog.RootElement.GetProperty("characters").EnumerateArray()
            .SelectMany(character => character.GetProperty("skills").EnumerateArray())
            .ToDictionary(skill => skill.GetProperty("id").GetString(), StringComparer.Ordinal);
        service.CapturePerformanceDiagnostics = true;
        foreach (string skill in skills)
        {
            string path = rows[skill].GetProperty("voiceEventPath").GetString();
            Check(studio.getEvent(path, out var description), "voice " + path);
            Check(description.getLength(out int length), "duration " + path);
            Check(description.getParameterDescriptionCount(out int parameterCount), "parameter count " + path);
            var parameters = new List<string>();
            for (int i = 0; i < parameterCount; i++)
            {
                Check(description.getParameterDescriptionByIndex(i, out var parameter), "parameter " + path);
                parameters.Add(parameter.name.ToString());
            }
            var takes = new HashSet<string>(StringComparer.Ordinal);
            for (int cast = 0; cast < 8; cast++)
            {
                service.ResetPerformanceDiagnostics();
                int before = voice.StartedVoiceCount;
                voice.Play(skill);
                Require(voice.StartedVoiceCount == before + 1, "Expected one native voice event: " + skill);
                // Some authored programmer events expose a 20 ms trigger while the clips continue.
                float peak = Meter(voice, meter, length <= 20 ? 3500 : Math.Max(length + 100, 1000));
                var keys = service.DiagnosticSoundKeys.Order().ToArray();
                Require(peak > 0.00001f && keys.Length != 0, "Skill produced no Japanese samples: " + skill);
                takes.Add(string.Join(",", keys));
                Console.WriteLine(JsonSerializer.Serialize(new { skill, cast, keys, length,
                    parameters, peak }));
            }
            Require(takes.Count > 1, "Repeated source skill event used fewer than two distinct Japanese takes: " + skill);
        }

        var contextualPlay = typeof(BattleVoice).GetMethod("PlaySkill", new[] { typeof(string), typeof(bool), typeof(bool) });
        Require(contextualPlay != null, "BattleVoice must accept an accepted ally call or answer cue");
        foreach (var cue in new[]
        {
            (Skill: "c_ras_skill_02", Calls: true, Answers: false, Prefix: "voc_c1001_call_"),
            (Skill: "c_mercedes_skill_01", Calls: false, Answers: true, Prefix: "voc_c1005_answer_"),
            (Skill: "c_kise_skill_02", Calls: true, Answers: false, Prefix: "voc_c1006_skill2_")
        })
        {
            service.ResetPerformanceDiagnostics();
            int before = voice.StartedVoiceCount;
            contextualPlay.Invoke(voice, new object[] { cue.Skill, cue.Calls, cue.Answers });
            Require(voice.StartedVoiceCount == before + 1, "Expected exactly one native event for " + cue.Skill);
            float peak = Meter(voice, meter, 1800);
            var keys = service.DiagnosticSoundKeys.Order().ToArray();
            Require(keys.Length != 0 && keys.All(key => key.StartsWith(cue.Prefix, StringComparison.Ordinal)) && peak > 0.00001f,
                "Unexpected Japanese samples for " + cue.Skill + ": " + string.Join(", ", keys));
            Console.WriteLine(JsonSerializer.Serialize(new { cue.Skill, cue.Calls, cue.Answers, keys, peak }));
        }
    }

    static void RecordButton(string sounds, string outputPath)
    {
        Check(FMOD.Studio.System.create(out var studio), "create button capture system");
        var output = Marshal.StringToCoTaskMemUTF8(outputPath);
        try
        {
            Check(studio.getCoreSystem(out var core), "button capture core");
            Check(core.setOutput(FMOD.OUTPUTTYPE.WAVWRITER), "button capture output");
            Check(studio.initialize(64, FMOD.Studio.INITFLAGS.SYNCHRONOUS_UPDATE, FMOD.INITFLAGS.NORMAL, output),
                "initialize button capture");
            foreach (string bank in new[] { "master.bank", "master.strings.bank", "ui1.bank" })
                Check(studio.loadBankMemory(File.ReadAllBytes(Path.Combine(sounds, bank)),
                    FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out _), bank);
            Check(studio.flushCommands(), "load button capture banks");
            Check(studio.getEvent("event:/ui/btn_normal", out var description), "button event");
            Check(description.getID(out var buttonId), "button event ID");
            Check(FMOD.Studio.Util.parseID("{1d184b0f-78fa-4e74-b456-1388254dc51f}", out var sourceId), "button source ID");
            Require(buttonId.Equals(sourceId), "Unexpected button event identity");
            Check(description.loadSampleData(), "preload button sample");
            Check(studio.flushSampleLoading(), "finish loading button sample");
            Check(description.createInstance(out var instance), "create button event");
            Check(instance.start(), "start button");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 900)
            {
                Check(studio.update(), "render button");
                Thread.Sleep(10);
            }
            Check(instance.release(), "release button event");
        }
        finally { studio.release(); Marshal.FreeCoTaskMem(output); }
    }

    static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("AudioProbe <project root> <read-only source sound root> [skill event IDs...]");
            string root = Path.GetFullPath(args[0]);
            string sounds = Path.GetFullPath(args[1]);
            string native = Path.Combine(root, "Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudioL.dll");
            NativeLibrary.SetDllImportResolver(typeof(FMOD.System).Assembly, (name, assembly, path) =>
                name.StartsWith("fmod", StringComparison.Ordinal) ? NativeLibrary.Load(native) : IntPtr.Zero);
            if (args.Length == 4 && args[2] == "--record-button")
            {
                RecordButton(sounds, args[3]);
                return 0;
            }
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
            var studio = Field<FMOD.Studio.System>(service, "studio");
            if (args.Length > 2)
            {
                AuditVariants(voice, service, studio, meter, json, args[2..]);
                return 0;
            }

            const string burnPath = "event:/battle/soulburn/ready";
            Check(studio.getEvent(burnPath, out var sourceBurn), "resolve the original Burn event");
            Check(sourceBurn.getID(out var burnId), "get the original Burn event ID");
            Check(FMOD.Studio.Util.parseID("{39f88ea3-22f3-4523-ad8a-16c691a45136}", out var expectedBurn), "parse source Burn event ID");
            Require(burnId.Equals(expectedBurn), "Burn event must match source events.yml");
            var loadedBanks = Field<List<FMOD.Studio.Bank>>(service, "banks");
            int impactIndex = Array.FindIndex(addresses, address => address.EndsWith("/gerneal.bank", StringComparison.Ordinal));
            Check(loadedBanks[impactIndex >= 0 ? impactIndex : loadedBanks.Count - 1].getEventList(out var impactEvents),
                "list the already loaded shared impact bank events");
            Require(impactEvents.Any(item => item.getID(out var id) == FMOD.RESULT.OK && id.Equals(burnId)),
                "Burn ready must be in the shared impact bank");
            Require(Meter(voice, meter, 150) <= 0.00001f, "Battle initialization must not pre-play the Burn ready cue");
            voice.PlayBurnReady();
            Require(voice.StartedBurnReadyCount == 1, "Arming Burn must start one original source cue");
            float burnReadyPeak = Meter(voice, meter, 1500);
            Require(burnReadyPeak > 0.00001f, "Burn ready produced no audio samples");
            Console.WriteLine(JsonSerializer.Serialize(new { burnReadyPeak, eventPath = burnPath }));
            Meter(voice, meter, 400);
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
            voice.PlayBurnReady();
            Require(voice.StartedBurnReadyCount == 1, "Disposed battle must not restart the Burn cue");
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
