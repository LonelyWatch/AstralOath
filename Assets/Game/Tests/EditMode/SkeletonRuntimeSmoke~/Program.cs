using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AstralOath.Client.Skeletons;
using A = AstralOath.ThirdParty.Spine21;
using B = AstralOath.ThirdParty.Spine38;
class Loader : A.TextureLoader, B.TextureLoader {
 public void Load(A.AtlasPage p,string path) { if(p.width<=0||p.height<=0) throw new Exception("Missing size"); }
 public void Load(B.AtlasPage p,string path) { if(p.width<=0||p.height<=0) throw new Exception("Missing size"); }
 public void Unload(object value) {}
}
class Program {
 static long vertices; static int frames, deformed, reordered, events;
 static void Check(float[] v) { foreach(float x in v) if(float.IsNaN(x)||float.IsInfinity(x)) throw new Exception("Non-finite world vertex"); vertices+=v.Length/2; }
 static void CheckBone(float[] values) { foreach(float value in values) if(float.IsNaN(value)||float.IsInfinity(value)) throw new Exception("Non-finite bone matrix"); }
 static void AssertPose(A.Skeleton skeleton, A.Animation animation, string boneName, float time,
     float alpha, float expectedY, float startY = 0) {
  var bone = skeleton.FindBone(boneName);
  skeleton.SetToSetupPose();
  bone.Y = startY;
  animation.Mix(skeleton, -1, time, false, null, alpha);
  if (Math.Abs(bone.Y - expectedY) > .00005f)
   throw new InvalidOperationException($"{animation.Name}/{boneName} at {time}, alpha {alpha}: expected {expectedY}, got {bone.Y}");
 }
 static void CheckBaalPoses(string path, A.Skeleton skeleton, A.Animation animation) {
  if (animation == null) throw new InvalidOperationException("Missing Baal & Sezan source animation");
  switch (Path.GetFileName(path)) {
   case "baalcezanne_skill2_b.spine.json":
    AssertPose(skeleton, animation, "root", .6333f, 1, -7.695f);
    AssertPose(skeleton, animation, "root", 1.2666f, 1, -15.39f);
    AssertPose(skeleton, animation, "root", 2.9333f, 1, -15.39f);
    AssertPose(skeleton, animation, "root", 2.9333f, .5f, -7.695f);
    break;
   case "baalcezanne_skill2_f.spine.json":
    AssertPose(skeleton, animation, "root", .6333f, 1, -7.695f);
    AssertPose(skeleton, animation, "bone", .0333f, 1, 307.94f);
    AssertPose(skeleton, animation, "bone", .0333f, .5f, 154.4775f);
    break;
   case "BaalCezanne_skill3_cm.spine.json":
    AssertPose(skeleton, animation, "root", 3.799f, .5f, 16, 16);
    AssertPose(skeleton, animation, "root", 3.8f, .5f, 4, 16);
    break;
   default:
    throw new ArgumentException("Unexpected Baal & Sezan sample: " + path);
  }
 }
 static void Run21(string path,string atlasPath,bool checkBaal) {
  using System.IO.TextReader ar=string.IsNullOrEmpty(atlasPath)?new StringReader(""):File.OpenText(atlasPath); var atlas=new A.Atlas(ar,"",new Loader());
  var data=Spine21Json.Read(File.ReadAllText(path),atlas,1);
  var skeleton=new A.Skeleton(data); var state=new A.AnimationState(new A.AnimationStateData(data)); state.Event+=(_,__,___)=>events++;
  if (checkBaal) CheckBaalPoses(path, skeleton, data.FindAnimation("animation"));
  int ffd=data.Animations.Sum(x=>x.Timelines.Count(t=>t is A.FFDTimeline));
  int draw=data.Animations.Sum(x=>x.Timelines.Count(t=>t is A.DrawOrderTimeline));
  foreach(var skin in data.Skins.DefaultIfEmpty()) foreach(var anim in data.Animations) {
   skeleton.SetSkin(skin); skeleton.SetToSetupPose(); state.ClearTracks(); state.SetAnimation(0,anim,false);
   int count=(int)Math.Ceiling(anim.Duration*30)+1;
   for(int frame=0;frame<count;frame++) {
    if(frame>0) state.Update(1f/30); state.Apply(skeleton); skeleton.UpdateWorldTransform(); frames++;
    foreach(var bone in skeleton.Bones) CheckBone(new[]{bone.WorldX,bone.WorldY,bone.M00,bone.M01,bone.M10,bone.M11});
    if(!skeleton.DrawOrder.SequenceEqual(skeleton.Slots)) reordered++;
    foreach(var slot in skeleton.DrawOrder) {
     if(slot.AttachmentVerticesCount>0) deformed++;
     float[] v;
     if(slot.Attachment is A.RegionAttachment r) { v=new float[8];r.ComputeWorldVertices(slot.Bone,v); }
     else if(slot.Attachment is A.MeshAttachment m) { v=new float[m.UVs.Length];m.ComputeWorldVertices(slot,v); }
     else if(slot.Attachment is A.SkinnedMeshAttachment w) { v=new float[w.UVs.Length];w.ComputeWorldVertices(slot,v); }
     else continue; Check(v);
    }
   }
  }
  Console.WriteLine(JsonSerializer.Serialize(new{file=path,version=data.Version,animations=data.Animations.Count,skins=data.Skins.Count,ffdTimelines=ffd,drawOrderTimelines=draw,frames,deformed,reordered,events,vertices}));
  atlas.Dispose();
 }
 static void Run38(string path,string atlasPath) {
  using System.IO.TextReader ar=string.IsNullOrEmpty(atlasPath)?new StringReader(""):File.OpenText(atlasPath); var atlas=new B.Atlas(ar,"",new Loader());
  using var jr=File.OpenText(path); var data=new B.SkeletonJson(atlas).ReadSkeletonData(jr);
  var skeleton=new B.Skeleton(data); var state=new B.AnimationState(new B.AnimationStateData(data));state.Event+=(_,__)=>events++;
  int ffd=data.Animations.Sum(x=>x.Timelines.Count(t=>t is B.DeformTimeline));
  int draw=data.Animations.Sum(x=>x.Timelines.Count(t=>t is B.DrawOrderTimeline));
  foreach(var skin in data.Skins.DefaultIfEmpty()) foreach(var anim in data.Animations) {
   skeleton.SetSkin(skin);skeleton.SetToSetupPose();state.ClearTracks();state.SetAnimation(0,anim,false);
   int count=(int)Math.Ceiling(anim.Duration*30)+1;
   for(int frame=0;frame<count;frame++) {
    if(frame>0)state.Update(1f/30);state.Apply(skeleton);skeleton.UpdateWorldTransform();frames++;
    foreach(var bone in skeleton.Bones) CheckBone(new[]{bone.WorldX,bone.WorldY,bone.A,bone.B,bone.C,bone.D});
    if(!skeleton.DrawOrder.SequenceEqual(skeleton.Slots)) reordered++;
    foreach(var slot in skeleton.DrawOrder) {
     if(slot.Deform.Count>0) deformed++;
     float[] v;
     if(slot.Attachment is B.RegionAttachment r) {v=new float[8];r.ComputeWorldVertices(slot.Bone,v,0);}
     else if(slot.Attachment is B.MeshAttachment m) {v=new float[m.WorldVerticesLength];m.ComputeWorldVertices(slot,0,v.Length,v,0);}
     else continue;Check(v);
    }
   }
  }
  Console.WriteLine(JsonSerializer.Serialize(new{file=path,version=data.Version,animations=data.Animations.Count,skins=data.Skins.Count,ffdTimelines=ffd,drawOrderTimelines=draw,frames,deformed,reordered,events,vertices}));atlas.Dispose();
 }
 static void CheckOrderedTranslate() {
  const string json = """
  {"skeleton":{"spine":"2.1.27","hash":"","width":0,"height":0},"bones":[{"name":"root","y":3}],
   "slots":[],"skins":{"default":{}},
   "animations":{"clip":{"bones":{"root":{"translate":[{"time":0,"y":8,"curve":"stepped"},
   {"time":0.5,"y":-4,"curve":"stepped"},{"time":1}]}},
   "astralOathTranslateTracks":{"root":[[{"time":0.75,"y":-4}],[{"time":0.75,"y":-4}]]}}}}
  """;
  var atlas = new A.Atlas(new StringReader(""), "", new Loader());
  var data = Spine21Json.Read(json, atlas, 1);
  var skeleton = new A.Skeleton(data);
  var animation = data.FindAnimation("clip");
  (float time, float alpha, float y)[] samples = { (.25f, 1, 11), (.5f, 1, -1),
   (.749f, 1, -1), (.75f, 1, -1), (1, 1, -1), (.25f, .5f, 7), (1, .5f, 0) };
  foreach (var sample in samples) {
   skeleton.SetToSetupPose();
   animation.Mix(skeleton, -1, sample.time, false, null, sample.alpha);
   if (Math.Abs(skeleton.RootBone.Y - sample.y) > .00001f)
    throw new InvalidOperationException($"Ordered translation at {sample.time}, alpha {sample.alpha}: expected {sample.y}, got {skeleton.RootBone.Y}");
  }
  atlas.Dispose();
  Console.WriteLine("Ordered translate full-weight and mixed poses passed.");
 }
 static void Main(string[] args) {
  if (args.Length == 1 && args[0] == "--ordered-translate") { CheckOrderedTranslate(); return; }
  var inputs=new System.Collections.Generic.List<(string json,string atlas)>();
  bool checkBaal = args.Length == 3 && args[0] == "--baal-ordered-translate";
  if(args.Length==3 && (args[0]=="--manifest" || checkBaal)) {
   string project=Path.GetFullPath(args[2]);
   using var manifest=JsonDocument.Parse(File.ReadAllText(args[1]));
   var entries=manifest.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(e=>e.GetProperty("address").GetString(),e=>e);
   foreach(var item in entries.Values.Where(e=>e.GetProperty("type").GetString()=="skeleton")) {
    using var descriptor=JsonDocument.Parse(File.ReadAllText(Path.Combine(project,item.GetProperty("path").GetString())));
    var d=descriptor.RootElement;string json=Path.Combine(project,entries[d.GetProperty("jsonAddress").GetString()].GetProperty("path").GetString());
    string atlasAddress=d.GetProperty("atlasAddress").GetString();
    string atlas=string.IsNullOrEmpty(atlasAddress)?"":Path.Combine(project,entries[atlasAddress].GetProperty("path").GetString());
    inputs.Add((json,atlas));
   }
  } else if(args.Length==2) {
   foreach(string path in Directory.GetFiles(Path.GetFullPath(args[0]),"*.spine.json",SearchOption.AllDirectories)) {
    string group=new DirectoryInfo(Path.GetDirectoryName(path)).Name;
    string name=Path.GetFileName(path).Replace(".spine.json","");
    inputs.Add((path,Path.Combine(args[1],group,name+".atlas")));
   }
  } else throw new ArgumentException("Usage: <converted-json-root> <original-root> OR --manifest <manifest.json> <project-root>");
  if(inputs.Count==0)throw new InvalidOperationException("No converted skeleton files were found.");
  if(checkBaal && inputs.Count != 3) throw new InvalidOperationException("Expected three Baal & Sezan SCSP samples.");
  int passed=0;
  foreach(var input in inputs) {
   vertices=0;frames=deformed=reordered=events=0;
   using var doc=JsonDocument.Parse(File.ReadAllText(input.json));string version=doc.RootElement.GetProperty("skeleton").GetProperty("spine").GetString();
   try {
    if(version.StartsWith("2.1"))Run21(input.json,input.atlas,checkBaal);else if(version.StartsWith("3.8"))Run38(input.json,input.atlas);else throw new Exception("Unsupported version "+version);
    passed++;
   }catch(Exception e){Console.WriteLine(input.json+" FAILED "+e);Environment.ExitCode=1;}
  }
  Console.WriteLine(JsonSerializer.Serialize(new{result=passed==inputs.Count?"PASS":"FAIL",passed,total=inputs.Count}));
 }
}
