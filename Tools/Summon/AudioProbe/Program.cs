using System.Runtime.InteropServices;

if (args.Length < 4 || (args.Length - 2) % 4 != 0)
    throw new ArgumentException("Expected project root, sound directory and repeating event path, source ID, bank name, output WAV.");
string root = Path.GetFullPath(args[0]);
string source = Path.GetFullPath(args[1]);
string native = Path.Combine(root, "Assets/ThirdParty/FMOD/Plugins/x86_64/fmodstudioL.dll");
IntPtr library = NativeLibrary.Load(native);
NativeLibrary.SetDllImportResolver(typeof(FMOD.System).Assembly, (name, _, _) =>
    name.StartsWith("fmod", StringComparison.Ordinal) ? library : IntPtr.Zero);
for (int index = 2; index < args.Length; index += 4)
    Record(args[index], args[index + 1], args[index + 2], args[index + 3]);

void Record(string path, string guid, string bank, string target)
{
    Check(FMOD.Studio.System.create(out var studio), "Create recording system");
    var events = path.Split('|');
    var guids = guid.Split('|');
    if (events.Length != guids.Length)
        throw new ArgumentException("Every layered event needs its original GUID.");
    var keys = new List<string>();
    Exception soundError = null;
    FMOD.Studio.EVENT_CALLBACK callback = null;
    IntPtr output = Marshal.StringToCoTaskMemUTF8(target);
    try
    {
        Check(studio.getCoreSystem(out var core), "Get recording mixer");
        Check(core.setSoftwareFormat(48000, FMOD.SPEAKERMODE.STEREO, 0), "48 kHz stereo");
        Check(core.setOutput(FMOD.OUTPUTTYPE.WAVWRITER_NRT), "Offline WAV writer");
        Check(studio.initialize(32, FMOD.Studio.INITFLAGS.SYNCHRONOUS_UPDATE, FMOD.INITFLAGS.STREAM_FROM_UPDATE, output), "Initialize recording");
        foreach (string file in (path.StartsWith("event:/", StringComparison.Ordinal)
            ? new[] { "master.bank", "master.strings.bank" }.Concat(bank.Split('|').Select(entry => entry + ".bank"))
            : new[] { "master.bank", "master.strings.bank", "voc.event.bank", bank + ".bank" }))
            Check(studio.loadBankMemory(File.ReadAllBytes(Path.Combine(source, file)),
                FMOD.Studio.LOAD_BANK_FLAGS.NORMAL, out _), file);
        Check(studio.flushCommands(), "Load banks");
        if (events.Length > 1)
        {
            Check(core.getMasterChannelGroup(out var master), "Get native mix output");
            Check(master.setVolume(.45f), "Leave mix headroom for simultaneous original events");
        }
        int length;
        if (path.StartsWith("event:/", StringComparison.Ordinal))
        {
            length = 0;
            for (int index = 0; index < events.Length; index++)
            {
                string eventPath = events[index];
                Check(studio.getEvent(eventPath, out var description), eventPath);
                Check(description.getID(out var actual), "Read source GUID");
                Check(FMOD.Studio.Util.parseID(guids[index], out var expected), "Parse source GUID");
                if (!actual.Equals(expected)) throw new InvalidDataException("Wrong source event GUID: " + eventPath);
                Check(description.getLength(out int duration), "Read event duration");
                length = Math.Max(length, duration);
                Check(description.loadSampleData(), "Load sample data");
                Check(studio.flushSampleLoading(), "Finish loading samples");
                Check(description.createInstance(out var instance), "Create authored event");
                if (eventPath.StartsWith("event:/voc/", StringComparison.Ordinal))
                {
                    callback ??= (type, _, parameters) =>
                    {
                        try
                        {
                            var properties = Marshal.PtrToStructure<FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES>(parameters);
                            if (type == FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND)
                                return properties.sound == IntPtr.Zero ? FMOD.RESULT.OK : new FMOD.Sound(properties.sound).release();

                            string key = properties.name;
                            key = key.EndsWith("(zhs)", StringComparison.OrdinalIgnoreCase) ? key[..^5] : key;
                            Check(studio.getSoundInfo(key, out var info), "Resolve original Japanese programmer key " + key);
                            Check(core.createSound(info.name_or_data, info.mode | FMOD.MODE.CREATECOMPRESSEDSAMPLE,
                                ref info.exinfo, out var sound), "Create original Japanese programmer sound");
                            properties.sound = sound.handle;
                            properties.subsoundIndex = info.subsoundindex;
                            Marshal.StructureToPtr(properties, parameters, false);
                            keys.Add((string)properties.name + " -> " + key);
                            return FMOD.RESULT.OK;
                        }
                        catch (Exception error)
                        {
                            soundError = error;
                            return FMOD.RESULT.ERR_INTERNAL;
                        }
                    };
                    Check(instance.setCallback(callback, FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND |
                        FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND), "Set Japanese programmer callback");
                }
                Check(instance.start(), "Start authored event");
                Check(instance.release(), "Release authored event after its playback finishes");
            }
        }
        else
        {
            Check(studio.getSoundInfo(path, out var sample), "Resolve original Japanese sample");
            Check(core.createSound(sample.name_or_data, sample.mode | FMOD.MODE.CREATESAMPLE | FMOD.MODE.LOOP_OFF,
                ref sample.exinfo, out var voice), "Load original acquisition voice");
            var chosen = voice;
            if (sample.subsoundindex >= 0) Check(voice.getSubSound(sample.subsoundindex, out chosen), "Resolve localized subsound");
            Check(chosen.getLength(out uint duration, FMOD.TIMEUNIT.MS), "Read source voice duration");
            length = checked((int)duration);
            Check(core.playSound(chosen, default, false, out _), "Play original acquisition voice");
        }
        if (length <= 0 || length > 180000) throw new InvalidDataException("Unexpected authored sound length: " + path);
        Check(core.getDSPBufferSize(out uint samples, out _), "Read mixer buffer size");
        int updates = (int)Math.Ceiling((length + 600.0) * 48 / samples);
        for (int frame = 0; frame < updates; frame++) Check(studio.update(), "Render native mixer buffer");
        if (soundError != null) throw new InvalidDataException("Original programmer audio failed.", soundError);
        if (callback != null)
        {
            if (keys.Count == 0) throw new InvalidDataException("The native voice event never requested a sample.");
            Console.WriteLine("Japanese programmer samples: " + string.Join(", ", keys));
        }
        Console.WriteLine($"{path}\t{length}\t{samples}");
    }
    finally
    {
        studio.release();
        GC.KeepAlive(callback);
        Marshal.FreeCoTaskMem(output);
    }
}

static void Check(FMOD.RESULT result, string action)
{
    if (result != FMOD.RESULT.OK) throw new InvalidOperationException($"{action}: {result}");
}
