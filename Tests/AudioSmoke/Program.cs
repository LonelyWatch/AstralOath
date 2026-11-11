using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Core;

static class Program {
 static readonly string[] Banks={"master.bank","master.strings.bank","sfx1.bank","voc.event.bank","ja/voc1.audio_ja.bank"};
 static readonly List<FMOD.Studio.EVENT_CALLBACK> Observers=new();
 static int created,destroyed,nativeDestroyed,initialSoundDestroys;static readonly HashSet<IntPtr> ObservedSounds=new();static readonly List<string> Keys=new();static Exception callbackError;
 static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);
 static void Check(FMOD.RESULT value,string operation){if(value!=FMOD.RESULT.OK)throw new Exception(operation+": "+value+" "+FMOD.Error.String(value));}
 static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
 static void CleanupUnexpectedNative(FmodCharacterAudioService service){var system=Field<FMOD.Studio.System>(service,"studio");if(system.isValid())system.release();}
 static async Task GuardOnly(string source){
  var service=new FmodCharacterAudioService(CancellationToken.None);service.Dispose();
  bool correct=false;
  try{await service.InitializeAsync(new FileAssets(source),Banks);}
  catch(ObjectDisposedException){correct=true;}
  catch(Exception error){Console.WriteLine("Wrong rejection after Dispose: "+error.GetType().Name);}
  finally{CleanupUnexpectedNative(service);}
  Require(correct,"Initialize after Dispose must reject before creating native state");
  Console.WriteLine("Disposed initialization guard PASS");
 }
 static void Observe(ICharacterAudioPlayback playback,FMOD.Studio.System studio){
  var instance=Field<FMOD.Studio.EventInstance>(playback,"instance");
  Check(instance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE),"prepare observation stop");Check(studio.flushCommands(),"prepare flush");
  var original=(FMOD.Studio.EVENT_CALLBACK)typeof(FmodCharacterAudioService).GetField("ProgrammerCallback",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
  FMOD.Studio.EVENT_CALLBACK observer=(type,ptr,parameter)=>{
   try{
    var result=original(type,ptr,parameter);
    if(type==FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND){
     var value=Marshal.PtrToStructure<FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES>(parameter);
     Require(result==FMOD.RESULT.OK && value.sound!=IntPtr.Zero,"Programmer callback did not create a sound: key="+(string)value.name+", result="+result);created++;Require(ObservedSounds.Add(value.sound),"Observed duplicate native sound handle");Keys.Add(value.name);
    }
    if(type==FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND){
     var value=Marshal.PtrToStructure<FMOD.Studio.PROGRAMMER_SOUND_PROPERTIES>(parameter);
     if(ObservedSounds.Remove(value.sound))destroyed++;else initialSoundDestroys++;
    }
    if(type==FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED)nativeDestroyed++;
    return result;
   }catch(Exception error){callbackError=error;return FMOD.RESULT.ERR_INTERNAL;}
  };
  Observers.Add(observer);
  Check(instance.setCallback(observer,FMOD.Studio.EVENT_CALLBACK_TYPE.CREATE_PROGRAMMER_SOUND|FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROY_PROGRAMMER_SOUND|FMOD.Studio.EVENT_CALLBACK_TYPE.DESTROYED),"observed callback");
  Check(instance.start(),"observed start");Check(studio.update(),"observed update");
 }
 static float Meter(FmodCharacterAudioService service,FMOD.DSP meter,int milliseconds){
  float maximum=0;var timer=System.Diagnostics.Stopwatch.StartNew();
  while(timer.ElapsedMilliseconds<milliseconds){
   service.Tick();Check(meter.getMeteringInfo(IntPtr.Zero,out var info),"getMeteringInfo");
   for(int i=0;i<info.numchannels;i++)maximum=Math.Max(maximum,info.peaklevel[i]);
   if(callbackError!=null)throw callbackError;
   Require(service.IsAvailable,service.Diagnostic);Thread.Sleep(10);
  }
  return maximum;
 }
 static void ClearTail(FmodCharacterAudioService service,FMOD.Studio.System studio,FMOD.System core){
  Check(studio.flushCommands(),"stop flush");for(int i=0;i<10;i++){service.Tick();Thread.Sleep(10);}
 }
 static async Task Cycle(string source,int cycle){
  var assets=new FileAssets(source);var service=new FmodCharacterAudioService(CancellationToken.None);
  try{
   await service.InitializeAsync(assets,Banks);
   var studio=Field<FMOD.Studio.System>(service,"studio");var core=Field<FMOD.System>(service,"core");
   // No event is started until the native backend confirms silent output.
   Check(core.setOutput(FMOD.OUTPUTTYPE.NOSOUND),"setOutput NOSOUND");
   Check(core.getOutput(out var output),"getOutput");Require(output==FMOD.OUTPUTTYPE.NOSOUND,"Silent output was not selected");
   Check(core.getMasterChannelGroup(out var master),"master group");Check(master.getDSP(0,out var meter),"master DSP");Check(meter.setMeteringEnabled(false,true),"enable meter");
   bool duplicateRejected=false;try{await service.InitializeAsync(assets,Banks);}catch(InvalidOperationException){duplicateRejected=true;}
   Require(duplicateRejected,"Duplicate Initialize must reject");Require(Field<FMOD.Studio.System>(service,"studio").handle==studio.handle,"Duplicate initialize replaced native system");
   foreach(string hero in new[]{"ras","mercedes","angelica"})for(int skill=1;skill<=3;skill++)foreach(string group in new[]{"skill/","voc/skill/"}){
    string path=$"event:/{group}c_{hero}_skill_{skill:00}";int before=created;
    if(group.StartsWith("voc")) {
     Check(studio.getEvent(path,out var description),"voice description");Check(description.getParameterDescriptionCount(out int pc),"parameter count");
     for(int pi=0;pi<pc;pi++){Check(description.getParameterDescriptionByIndex(pi,out var pd),"parameter description");Console.WriteLine("PARAM "+pd.name+" default="+pd.defaultvalue+" min="+pd.minimum+" max="+pd.maximum+" flags="+pd.flags);}
    }
    var playback=service.PlayEvent(path);Observe(playback,studio);
    playback.Pause(true);var instance=Field<FMOD.Studio.EventInstance>(playback,"instance");Check(instance.getPaused(out bool paused),"event getPaused");Require(paused,"Event did not pause");playback.Pause(false);
    float peak=Meter(service,meter,3500);
    Check(core.getChannelsPlaying(out int channels,out int real),"channels");
    var stateResult=instance.getPlaybackState(out var playingState);var timelineResult=instance.getTimelinePosition(out int position);
    Check(meter.getMeteringInfo(IntPtr.Zero,out var debugMeter),"meter debug");
    Console.WriteLine(JsonSerializer.Serialize(new{path,channels,real,playingState,position,samples=debugMeter.numsamples,meterChannels=debugMeter.numchannels,peak,created}));
    Require(peak>0.00001f,"DSP produced no samples: "+path);
    if(group.StartsWith("voc"))Require(created>before,"Voice event did not invoke programmer create: "+path);
    playback.Stop();playback.Dispose();playback.Stop();ClearTail(service,studio,core);
    Console.WriteLine(JsonSerializer.Serialize(new{cycle,path,peak,programmerCreates=created-before}));
   }
   string key="voc_c1001_answer_1";Check(studio.getSoundInfo(key,out var info),"selected getSoundInfo");Require(info.name_or_data!=IntPtr.Zero,"Selected voice table entry is empty");
   var voice=service.PlayVoice(key);voice.Pause(true);var channel=Field<FMOD.Channel>(voice,"channel");Check(channel.getPaused(out bool voicePaused),"voice getPaused");Require(voicePaused,"Direct voice did not pause");voice.Pause(false);
   float directPeak=Meter(service,meter,1000);Require(directPeak>0.00001f,"Selected voice produced no DSP samples");voice.Stop();voice.Dispose();ClearTail(service,studio,core);
   Console.WriteLine(JsonSerializer.Serialize(new{cycle,key,info.subsoundindex,directPeak}));
  }finally{
   service.Dispose();service.Dispose();service.Tick();
   Require(assets.Loaded==assets.Released,"Bank asset scopes were not all released");
   var owners=Field<object>(service,"callbackOwners");Require((int)owners.GetType().GetProperty("Count").GetValue(owners)==0,"Programmer callback GCHandle owners remain");
   Require(!service.IsAvailable,"Disposed service remained available");
  }
  Console.WriteLine($"Cycle {cycle} clean disposal PASS");
 }
 static async Task FailureCase(string source){
  var assets=new FileAssets(source);var service=new FmodCharacterAudioService(CancellationToken.None);
  try{
   await service.InitializeAsync(assets,Banks);var core=Field<FMOD.System>(service,"core");Check(core.setOutput(FMOD.OUTPUTTYPE.NOSOUND),"failure case silent output");
   var otherEvent=service.PlayEvent("event:/skill/c_ras_skill_03");var otherVoice=service.PlayVoice("voc_c1001_answer_1");
   typeof(FmodCharacterAudioService).GetField("callbackError",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(service,"Injected programmer callback failure");
   service.Tick();
   Require(Field<bool>(service,"disposed"),"Callback error must release native state from Tick");
   Require(!Field<FMOD.Studio.System>(service,"studio").isValid(),"Callback error retained Studio system");
   Require(Field<bool>(otherEvent,"disposed")&&Field<bool>(otherVoice,"disposed"),"Callback error left other audio active");
   Require(assets.Loaded==assets.Released,"Callback error retained bank scopes");
   Require(service.Diagnostic=="Injected programmer callback failure","Callback diagnostic was lost during disposal");
   otherEvent.Stop();otherVoice.Stop();service.Tick();service.Dispose();
  }finally{service.Dispose();}
  var partialAssets=new FileAssets(source);var partial=new FmodCharacterAudioService(CancellationToken.None);
  try{
   bool failed=false;try{await partial.InitializeAsync(partialAssets,new[]{Banks[0],"does-not-exist.bank"});}catch(FileNotFoundException){failed=true;}
   Require(failed,"Missing bank initialization should fail");Require(!Field<FMOD.Studio.System>(partial,"studio").isValid(),"Failed initialize retained native system");
   Require(partialAssets.Loaded==1&&partialAssets.Released==1,"Failed initialize retained loaded bank asset");
  }finally{partial.Dispose();}
  Console.WriteLine("Callback error and partial initialization release PASS");
 }
 static async Task<int> Main(string[] args){
  if(args.Length<2){Console.Error.WriteLine("Usage: AudioSmoke <absolute fmodstudio.dll> <read-only sound root> [--guard-only]");return 2;}
  string native=Path.GetFullPath(args[0]);Require(Path.IsPathFullyQualified(args[0])&&File.Exists(native),"Native DLL must be an explicit existing absolute path");
  NativeLibrary.SetDllImportResolver(typeof(FMOD.System).Assembly,(name,assembly,path)=>name.StartsWith("fmod",StringComparison.Ordinal)?NativeLibrary.Load(native):IntPtr.Zero);
  try{
   if(args.Contains("--guard-only")){await GuardOnly(args[1]);return 0;}
   if(args.Contains("--failure-only")){await FailureCase(args[1]);return 0;}
   await GuardOnly(args[1]);await FailureCase(args[1]);for(int i=1;i<=2;i++)await Cycle(args[1],i);
   Require(created>0&&destroyed==created&&ObservedSounds.Count==0,$"Observed programmer sound create/destroy counts differ: {created}/{destroyed}, pending {ObservedSounds.Count}");Require(nativeDestroyed==36,"Event instance destruction callbacks missing");
   Console.WriteLine(JsonSerializer.Serialize(new{result="PASS",nativeVersion=FMOD.VERSION.number,created,destroyed,nativeDestroyed,initialSoundDestroys,programmerKeys=Keys.Distinct().ToArray(),output="NOSOUND"}));
   GC.KeepAlive(Observers);return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
