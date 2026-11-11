using AstralOath.Client.CharacterPresentation;
static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var clock = new PresentationClock();
clock.Play();
clock.Advance(.5);
Check(clock.Time == .5, "playing advances unified clock");
clock.Pause();
clock.Advance(2);
Check(clock.Time == .5, "pause freezes unified clock");
clock.SetSpeed(2);
Check(!clock.AudioAllowed, "speed change disables sound");
clock.Play();
clock.Advance(.25);
Check(clock.Time == 1, "speed applies to delta");
clock.SetSpeed(1);
Check(!clock.AudioAllowed, "return to 1x cannot resume desynchronized audio");
clock.ResetWithAudio();
Check(clock.AudioAllowed && clock.Time == 0 && clock.Speed == 1, "reset restores synchronized sound");
clock.Seek(3);
Check(!clock.AudioAllowed && clock.Time == 3 && !clock.IsPlaying, "seek enters silent paused mode");
clock.Step(1d / 30);
Check(Math.Abs(clock.Time - 3 - 1d/30) < 1e-9, "step advances exactly one frame");
var nodes = new [] {
 new PresentationNode { id="start",kind="animation",start=0,verified=true },
 new PresentationNode { id="parallel",kind="voice",start=0,verified=true },
 new PresentationNode { id="delayed",kind="move",start=.5,verified=true },
 new PresentationNode { id="event",kind="effect",eventName="hit",eventSource="caster",start=.1,verified=true },
 new PresentationNode { id="unknown",kind="effect",start=0,verified=false,diagnostic="unknown when" }
};
var graph = new PresentationGraph { nodes=nodes };
var runner = new PresentationGraphScheduler(graph);
var output = new List<string>(); runner.Execute += n => output.Add(n.id);
runner.Advance(0);
Check(output.SequenceEqual(new[]{"start","parallel"}), "parallel time-zero nodes execute once; unverified nodes do not");
runner.Advance(.6); runner.Advance(.6);
Check(output.SequenceEqual(new[]{"start","parallel","delayed"}), "delay executes once");
runner.Signal("other", "hit"); runner.Advance(.8);
Check(output.Count==3, "event actor must match");
runner.Signal("caster", "hit"); runner.Advance(.85);
Check(output.Count==3, "event delay starts at signal time");
runner.Advance(.91);
Check(output.Last()=="event", "ONEVENT executes after event-relative delay");
runner.Signal("caster","hit"); runner.Advance(2);
Check(output.Count==4, "graph nodes execute once per run");
runner.Reset(); output.Clear(); runner.Advance(0);
Check(output.Count==2, "replay resets fired nodes");
var fade = new PresentationFade(.7, 2, .4, .5);
Check(Math.Abs(fade.Evaluate(.2)-.35)<1e-9, "DARK linear fade-in preserves requested opacity");
Check(Math.Abs(fade.Evaluate(1)-.7)<1e-9, "DARK holds source opacity");
Check(Math.Abs(fade.Evaluate(1.75)-.35)<1e-9, "DARK fades out before configured end");
Check(fade.Evaluate(2)==0, "finite DARK finishes");
Check(new PresentationFade(.8,-1,.2,0).Evaluate(50)==.8, "negative-one duration holds until STOP");
var blur = new SpriteBlurSettings(35,3,.3f,1,-1,.5f);
Check(blur.Range==35 && blur.Samples==3 && blur.Ratio==.3f, "PPEFFECT preserves authored blur uniforms");
Check(blur.DirectionX==1 && blur.DirectionY==-1 && blur.Duration==.5f, "PPEFFECT preserves direction and duration");
Check(new SpriteBlurSettings(35,9,2,1,0,.5f).Samples==5, "source shader caps half-kernel at five samples");
Check(new SpriteBlurSettings(35,3,2,1,0,.5f).Ratio==1, "source shader clamps original-image ratio");
bool rejected=false;
try { _=new SpriteBlurSettings(35,0,0,1,0,.5f); } catch(ArgumentOutOfRangeException) { rejected=true; }
Check(rejected, "reject invalid sample count rather than divide-by-zero in original shader");
string cfxPath=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../Res/Shared/Effects/effect/hit_new_blunt_normal.cfx.json"));
using var cfx=System.Text.Json.JsonDocument.Parse(File.ReadAllText(cfxPath));
var ranged=cfx.RootElement.GetProperty("primitive")[0];
Check(!PresentationScalar.TryRead(ranged.GetProperty("scale").GetString(),out _), "real CFX scale range returns unsupported instead of throwing");
Check(!PresentationScalar.TryRead(ranged.GetProperty("rotate").GetString(),out _), "real CFX rotate range returns unsupported instead of throwing");
Check(PresentationScalar.TryRead(cfx.RootElement.GetProperty("primitive")[1].GetProperty("delay").GetString(),out var delay)&&delay==70,"valid following CFX particle remains schedulable");
Check(!PresentationScalar.TryRead("NaN",out _)&&!PresentationScalar.TryRead("Infinity",out _),"reject nonfinite source scalar");
bool halfSampleRejected=false;
try { _=new SpriteBlurSettings(35,.5f,0,1,0,.5f); } catch(ArgumentOutOfRangeException) { halfSampleRejected=true; }
Check(halfSampleRejected,"exact half-sample zero-weight kernel is rejected");
Check(new SpriteBlurSettings(35,.75f,0,1,0,.5f).Samples==.75f,"fractional samples above one-half remain unchanged");
Check(new SpriteBlurSettings(float.Epsilon,5,0,1,0,.5f).Range==float.Epsilon,"source range is preserved; shader handles step underflow without a CPU clamp");
Console.WriteLine("CharacterPresentation: 31 behavior assertions passed.");
