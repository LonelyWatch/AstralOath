#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using AstralOath.Client.Bootstrap;
using AstralOath.Client.Common;
using AstralOath.Client.Skeletons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    // Opt-in diagnostic captures, not an original-game visual correctness assertion.
    public sealed class CharacterAnimationVisualTests
    {
        private const BindingFlags Members=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private Scene scene;
        private CharacterLabBootstrap bootstrap;
        private FmodCharacterAudioService audio;
        private BindingMap map;
        private object binder;
        private ICharacterLabEntry entry;
        private Camera camera;
        private RenderTexture previousTarget,captureTarget;
        private float previousAspect;
        private string output;
        private ISkeletonPlayer Player => (ISkeletonPlayer)Read(binder,"player");
        private static object Read(object owner,string name)
        {
            Assert.That(owner,Is.Not.Null,"Missing reflection owner for "+name);
            var type=owner.GetType();
            var field=type.GetField(name,Members);
            if(field!=null)return field.GetValue(owner);
            var property=type.GetProperty(name,Members);
            Assert.That(property,Is.Not.Null,type.FullName+" has no "+name);
            return property.GetValue(owner);
        }
        private static float Number(object owner,string name)=>Convert.ToSingle(Read(owner,name),CultureInfo.InvariantCulture);
        private static IEnumerator Until(Func<bool> ready,string message,float seconds=60)
        {
            float deadline=Time.realtimeSinceStartup+seconds;
            while(!ready()&&Time.realtimeSinceStartup<deadline)yield return null;
            Assert.That(ready(),Is.True,message);
        }
        private void Click(string key)=>map.Require<GameButton>(key).GetComponent<Button>().onClick.Invoke();
        private Button[] Rows(string key)=>map.Require<RectTransform>(key).GetComponentsInChildren<Button>(false);

        [UnityTest] public IEnumerator CaptureActualUiAnimationFramesForBeforeAfterInspection()
        {
            string destination=Environment.GetEnvironmentVariable("ASTRAL_ANIMATION_CAPTURE_DIR");
            if(string.IsNullOrWhiteSpace(destination))Assert.Ignore("Set ASTRAL_ANIMATION_CAPTURE_DIR to an ignored artifact directory for GPU captures.");
            Assert.That(Path.IsPathRooted(destination),Is.True,"Capture destination must be an absolute ignored artifact path.");
            string directory=Path.GetFullPath(destination);
            string assets=Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            Assert.That(directory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,Does.Not.StartWith(assets),"Capture files must not be imported under Assets.");
            Assert.That(SystemInfo.graphicsDeviceType,Is.Not.EqualTo(GraphicsDeviceType.Null),"This capture requires an actual graphics device.");
            output=Path.Combine(directory,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(output);
            Debug.Log("ASTRAL_ANIMATION_CAPTURE_DIRECTORY "+output);
            scene=EditorSceneManager.LoadSceneInPlayMode("Assets/Game/Modules/CharacterLab/Scenes/CharacterLab.unity",new LoadSceneParameters(LoadSceneMode.Additive));
            yield return Until(()=>bootstrap=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<CharacterLabBootstrap>(true)).SingleOrDefault(),"Bootstrap was not created.");
            yield return Until(()=>bootstrap.IsReady||!string.IsNullOrEmpty(bootstrap.StartupError),"Lab did not finish startup.");
            Assert.That(bootstrap.IsReady,Is.True,bootstrap.StartupError);
            // Asset package updates and SRP rendering continue; only the owner animation clock stops.
            bootstrap.enabled=false;
            entry=(ICharacterLabEntry)Read(bootstrap,"entry");
            binder=Read(entry,"binder");
            camera=(Camera)Read(bootstrap,"previewCamera");
            previousTarget=camera.targetTexture;previousAspect=camera.aspect;
            captureTarget=new RenderTexture(1920,1080,24,RenderTextureFormat.ARGB32)
                {name="Character Animation GPU Capture",useMipMap=false,autoGenerateMips=false};
            Assert.That(captureTarget.Create(),Is.True,"Could not create the full-resolution GPU capture target.");
            camera.targetTexture=captureTarget;camera.aspect=1920f/1080;
            map=scene.GetRootGameObjects().SelectMany(root=>root.GetComponentsInChildren<BindingMap>(true)).Single();
            audio=(FmodCharacterAudioService)Read(bootstrap,"audio");
            Assert.That(audio.IsAvailable,Is.True,audio.Diagnostic);
            Assert.That(((FMOD.System)Read(audio,"core")).setOutput(FMOD.OUTPUTTYPE.NOSOUND),Is.EqualTo(FMOD.RESULT.OK));
            string group=Environment.GetEnvironmentVariable("ASTRAL_ANIMATION_CAPTURE_GROUP");
            if(group=="mercedes-s3")
            {
                yield return CaptureMercedesS3LoopAndStrictSkill();
                Debug.Log("ASTRAL_ANIMATION_CAPTURE_COMPLETE "+output+"; Mercedes S3 diagnostic, not original-game validation.");
                yield break;
            }
            Assert.That(string.IsNullOrEmpty(group),Is.True,"Unknown ASTRAL_ANIMATION_CAPTURE_GROUP: "+group);
            if((bool)Read(binder,"loop"))Click("loop");

            yield return SelectCharacter("c1001");
            foreach(float time in new[]{0f,.25f,.516646f,1f})
            {
                yield return SelectAnimation("idle",true);
                Player.Tick(time);
                yield return Capture("ras-idle-fresh-tick",time);
                Player.Seek(time);
                yield return Capture("ras-idle-same-time-seek",time);
                // Rebuild the same time independently: a Seek defect must not contaminate the skin case.
                yield return SelectAnimation("idle",true);
                Player.Tick(time);
                Assert.That(Player.IsPaused,Is.False);
                Player.SetSkin("default");
                yield return Capture("ras-idle-same-time-skin-default",time);
            }
            foreach(string idle in new[]{"b_idle","b_idle_ready"})
                foreach(float phase in new[]{0f,.2f,.4f,.6f,.8f})
                {
                    yield return SelectAnimation(idle,true);
                    float target=Player.Duration*phase;
                    string sample=phase.ToString("0.0",CultureInfo.InvariantCulture).Replace('.','_');
                    // Duration-relative samples avoid duplicated targets on the shorter ready animation.
                    const float step=1f/60;
                    while(Player.Time+step<target)
                    {
                        Player.Tick(step);
                        yield return null;
                    }
                    Player.Tick(Mathf.Max(0,target-Player.Time));
                    Assert.That(Player.Time,Is.EqualTo(target).Within(.000001f));
                    yield return Capture("ras-"+idle+"-fresh-60hz-phase-"+sample,target);
                    // Recreate the same clean pose before the single-delta control.
                    yield return SelectAnimation(idle,true);
                    Player.Tick(target);
                    yield return Capture("ras-"+idle+"-fresh-single-tick-phase-"+sample,target);
                }
            yield return SelectMode("portrait","3.8");
            foreach(float time in new[]{0f,.5f,1f,1.5f,2f,2.5f})
            {
                yield return SelectAnimation("idle",true);
                Player.Tick(time);
                yield return Capture("ras-portrait38-idle-fresh-tick",time);
            }
            yield return SelectMode("battle","2.1");
            yield return SelectCharacter("c1005");
            foreach(float time in new[]{.2f,1f,2f,3.2f})
            {
                yield return SelectAnimation("skill3",true);
                Player.Tick(time);
                yield return Capture("mercedes-skill3-fresh-tick",time);
                Player.Seek(time);
                yield return Capture("mercedes-skill3-same-time-seek",time);
            }
            foreach(string previous in new[]{"idle","skill3"})
                foreach(float time in new[]{.2f,1f,2f,3.2f})
                {
                    yield return SelectAnimation(previous,true);
                    Player.Tick(Player.Duration*.63f);
                    // Deliberately use the same UI switch/replay path without Stop between animations.
                    yield return SelectAnimation("skill3",false);
                    Player.Tick(time);
                    yield return Capture("mercedes-"+previous+"-to-skill3",time);
                }
            Assert.That(Directory.GetFiles(output,"*.png").Length,Is.EqualTo(54));
            Debug.Log("ASTRAL_ANIMATION_CAPTURE_COMPLETE "+output+"; diagnostic frames only, not original-game validation.");
        }
        private double ClockTime=>Convert.ToDouble(Read(Read(binder,"clock"),"Time"),CultureInfo.InvariantCulture);
        private IEnumerator CaptureMercedesS3LoopAndStrictSkill()
        {
            if(!(bool)Read(binder,"loop"))Click("loop");
            if((bool)Read(binder,"experimental"))Click("experimental");
            yield return SelectCharacter("c1005");
            yield return SelectAnimation("skill3",true);
            float duration=Player.Duration;
            Assert.That(duration,Is.GreaterThan(4));
            var samples=Enumerable.Range(0,(int)(duration/.2f)+1).Select(i=>i*.2f)
                .Concat(new[]{1.081f,1.36f,duration}).Distinct().OrderBy(t=>t).ToArray();
            // Independent source poses across the entire clip, including its looping end boundary.
            foreach(float time in samples)
            {
                yield return SelectAnimation("skill3",true);
                entry.Tick(time);
                yield return Capture("mercedes-s3-loop-fresh-single",time);
            }
            // A loop wraps at Duration. Also retain the actual non-looping terminal pose explicitly.
            Click("loop");yield return SelectAnimation("skill3",true);entry.Tick(duration);
            yield return Capture("mercedes-s3-nonloop-terminal",duration);
            Click("loop");yield return SelectAnimation("skill3",true);
            const float step=1f/60;
            const double end=62.08;
            // No reset/seek across these 3,724 steps. Rendering pauses do not advance the owner clock.
            for(int frame=1;(frame*(double)step)<=end;frame++)
            {
                Player.Pause(false);entry.Tick(step);
                if(frame%60==0)yield return Capture("mercedes-s3-loop-continuous-60hz",frame/60f);
            }
            Player.Pause(false);entry.Tick((float)(end-ClockTime));
            Assert.That(ClockTime,Is.EqualTo(end).Within(.000001));
            float phase=Player.Time;
            yield return Capture("mercedes-s3-loop-continuous-60hz",(float)end);
            yield return SelectAnimation("skill3",true);entry.Tick(phase);
            yield return Capture("mercedes-s3-loop-final-phase-fresh",phase);
            Player.Seek(phase);
            yield return Capture("mercedes-s3-loop-final-phase-seek",phase);

            // Real skill button, strict mapping; the visible Loop toggle remains on but STG ANI is single-shot.
            Click("stop");
            var skill=Rows("skills")[2];skill.onClick.Invoke();
            yield return Until(()=>Read(binder,"performance")!=null&&skill.interactable,"Mercedes S3 UI did not start a strict STG session.");
            Assert.That((string)Read(Read(binder,"selectedSkill"),"id"),Is.EqualTo("c_mercedes_skill_03"));
            Assert.That((bool)Read(binder,"experimental"),Is.False);
            Assert.That((bool)Read(binder,"loop"),Is.True);
            Assert.That(ClockTime,Is.EqualTo(0));
            foreach(float time in samples)
            {
                yield return AdvanceUiTo(time);
                yield return Capture("mercedes-s3-strict-skill-60hz",time);
            }
            yield return AdvanceUiTo(end);
            yield return Capture("mercedes-s3-strict-skill-60hz",(float)end);
            int expected=samples.Length*2+67;
            Assert.That(Directory.GetFiles(output,"*.png").Length,Is.EqualTo(expected));
        }
        private IEnumerator AdvanceUiTo(double target)
        {
            const float step=1f/60;
            int frames=0;
            // Exact requested checkpoints use a final partial step; all other deltas are 1/60 second.
            while(ClockTime+step<target)
            {
                Player.Pause(false);entry.Tick(step);
                if(++frames%240==0)yield return null;
            }
            Player.Pause(false);entry.Tick((float)Math.Max(0,target-ClockTime));
            Assert.That(ClockTime,Is.EqualTo(target).Within(.000001));
        }
        private IEnumerator SelectCharacter(string id)
        {
            var old=Player;
            var button=Rows("characters").Single(row => row.GetComponentInChildren<GameText>().text.StartsWith(id + " · ", StringComparison.Ordinal));
            button.onClick.Invoke();
            yield return Until(()=>Player!=null&&!ReferenceEquals(Player,old)&&button.interactable,"Character UI did not finish switching.");
            yield return null; // Retired row objects and meshes use deferred Destroy.
            Assert.That(Player.Root.GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh&&f.sharedMesh.vertexCount>0),Is.True);
        }
        private IEnumerator SelectMode(string key,string version)
        {
            var old=Player;
            var button=map.Require<GameButton>(key).GetComponent<Button>();button.onClick.Invoke();
            yield return Until(()=>Player!=null&&!ReferenceEquals(Player,old)&&button.interactable&&Player.Version.StartsWith(version,StringComparison.Ordinal),
                "Mode UI did not replace the player with Spine "+version);
            yield return null;
            Assert.That(Player.Root.GetComponentsInChildren<MeshFilter>().Any(f=>f.sharedMesh&&f.sharedMesh.vertexCount>0),Is.True);
        }
        private IEnumerator SelectAnimation(string name,bool reset)
        {
            if(reset)Click("stop");
            int index=Player.Animations.Select((animation,i)=>(animation,i)).Single(item=>item.animation.Name==name).i;
            var button=Rows("animations")[index];button.onClick.Invoke();
            yield return Until(()=>button.interactable&&Player.CurrentAnimation==name,"Animation UI did not select "+name);
            Assert.That(Player.Time,Is.EqualTo(0).Within(.00001f));
            Assert.That(bootstrap.enabled,Is.False);
        }
        private IEnumerator Capture(string scenario,float requested)
        {
            var player=Player;player.Pause(true);
            float fixedTime=player.Time;
            // Two complete frame boundaries let SRP render the changed mesh into its owned target.
            // Camera.Render is intentionally not used: it is unsupported for this URP path.
            yield return null;yield return null;
            Assert.That(bootstrap.enabled,Is.False);
            Assert.That(Player,Is.SameAs(player));
            Assert.That(player.Time,Is.EqualTo(fixedTime),"Animation advanced while awaiting the GPU frame.");
            var target=camera.targetTexture;
            Assert.That(target&&target.IsCreated(),Is.True,"Preview RenderTexture is not available.");
            Assert.That(target,Is.SameAs(captureTarget));
            Assert.That(target.width,Is.EqualTo(1920));Assert.That(target.height,Is.EqualTo(1080));
            var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            var active=RenderTexture.active;
            byte[] png;
            try
            {
                RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0,false);texture.Apply(false,false);
                CharacterLabPlayerProbe.ValidateCapturePixels(texture.GetPixels32());
                png=texture.EncodeToPNG();
            }
            finally {RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);}
            string stem=scenario+"-"+requested.ToString("0.000000",CultureInfo.InvariantCulture).Replace('.','_');
            WriteNew(Path.Combine(output,stem+".png"),png);
            var metadata=Describe(player,scenario,requested,target);
            WriteNew(Path.Combine(output,stem+".json"),System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(metadata,true)));
            Debug.Log("ASTRAL_ANIMATION_FRAME "+stem+" time="+fixedTime.ToString("R",CultureInfo.InvariantCulture));
        }
        private static void WriteNew(string path,byte[] bytes)
        {using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))file.Write(bytes,0,bytes.Length);}
        private Frame Describe(ISkeletonPlayer player,string scenario,float requested,RenderTexture target)
        {
            var runtime=Read(player,"runtime");
            var skeleton=Read(runtime,"skeleton");
            var state=Read(runtime,"state");
            var track=state.GetType().GetMethod("GetCurrent",Members).Invoke(state,new object[]{0});
            bool legacy=player.Version.StartsWith("2.1",StringComparison.Ordinal);
            var selectedSkill=Read(binder,"selectedSkill");
            var bones=((IEnumerable)Read(skeleton,"Bones")).Cast<object>().Select(bone=>{
                var xy=new Vector2(Number(bone,"WorldX"),Number(bone,"WorldY"));
                return new BoneFrame {name=(string)Read(Read(bone,"Data"),"Name"),localPosition=new Vector2(Number(bone,"X"),Number(bone,"Y")),
                    localRotation=Number(bone,"Rotation"),localScale=new Vector2(Number(bone,"ScaleX"),Number(bone,"ScaleY")),
                    skeletonPosition=xy,worldPosition=player.Root.transform.TransformPoint(new Vector3(xy.x,xy.y,0)),
                    skeletonMatrix=new Vector4(Number(bone,legacy?"M00":"A"),Number(bone,legacy?"M01":"B"),Number(bone,legacy?"M10":"C"),Number(bone,legacy?"M11":"D")),
                    localFlipX=legacy&&(bool)Read(bone,"FlipX"),localFlipY=legacy&&(bool)Read(bone,"FlipY"),
                    worldFlipX=legacy&&(bool)Read(bone,"WorldFlipX"),worldFlipY=legacy&&(bool)Read(bone,"WorldFlipY")};
            }).ToArray();
            var slots=((IEnumerable)Read(skeleton,"Slots")).Cast<object>().Select(slot=>{
                var attachment=Read(slot,"Attachment");
                return new SlotFrame {name=(string)Read(Read(slot,"Data"),"Name"),bone=(string)Read(Read(Read(slot,"Bone"),"Data"),"Name"),
                    attachment=attachment==null?null:(string)Read(attachment,"Name"),alpha=Number(slot,"A")};
            }).ToArray();
            return new Frame {scenario=scenario,requestedTime=requested,animation=player.CurrentAnimation,time=player.Time,duration=player.Duration,version=player.Version,
                clockTime=ClockTime,clockSpeed=Number(Read(binder,"clock"),"Speed"),uiLoop=(bool)Read(binder,"loop"),experimental=(bool)Read(binder,"experimental"),
                skillSession=Read(binder,"performance")!=null,selectedSkillId=selectedSkill==null?null:(string)Read(selectedSkill,"id"),
                runtimeTrackTime=Number(track,legacy?"Time":"TrackTime"),runtimeLoop=(bool)Read(track,"Loop"),
                characterId=(string)Read(Read(binder,"selected"),"id"),rootPosition=player.Root.transform.position,rootScale=player.Root.transform.lossyScale,
                cameraPosition=camera.transform.position,cameraRotation=camera.transform.rotation,cameraSize=camera.orthographicSize,cameraAspect=camera.aspect,
                width=target.width,height=target.height,graphicsDevice=SystemInfo.graphicsDeviceType.ToString(),bones=bones,slots=slots,
                meshes=player.Root.GetComponentsInChildren<MeshFilter>().Select(f=>new MeshFrame{name=f.name,vertices=f.sharedMesh?f.sharedMesh.vertexCount:0,
                    localBounds=f.sharedMesh?f.sharedMesh.bounds:default,worldBounds=f.GetComponent<Renderer>()?f.GetComponent<Renderer>().bounds:default}).ToArray()};
        }
        [Serializable] private sealed class Frame
        {
            public string scenario,characterId,animation,version,graphicsDevice;
            public float requestedTime,time,duration,cameraSize,cameraAspect,clockSpeed,runtimeTrackTime;
            public double clockTime;
            public bool uiLoop,experimental,skillSession,runtimeLoop;
            public string selectedSkillId;
            public int width,height;
            public Vector3 rootPosition,rootScale,cameraPosition;
            public Quaternion cameraRotation;
            public BoneFrame[] bones;public SlotFrame[] slots;public MeshFrame[] meshes;
        }
        [Serializable] private sealed class BoneFrame
        {
            public string name;public Vector2 localPosition,localScale,skeletonPosition;public float localRotation;public Vector3 worldPosition;
            public Vector4 skeletonMatrix;public bool localFlipX,localFlipY,worldFlipX,worldFlipY;
        }
        [Serializable] private sealed class SlotFrame {public string name,bone,attachment;public float alpha;}
        [Serializable] private sealed class MeshFrame {public string name;public int vertices;public Bounds localBounds,worldBounds;}
        [UnityTearDown] public IEnumerator Cleanup()
        {
            // Restore ownership before Bootstrap disposes its original preview texture.
            if(camera&&camera.targetTexture==captureTarget)
            {camera.targetTexture=previousTarget;camera.aspect=previousAspect;}
            try {if(bootstrap)bootstrap.SendMessage("OnApplicationQuit",SendMessageOptions.DontRequireReceiver);}
            finally
            {
                if(captureTarget){captureTarget.Release();UnityEngine.Object.DestroyImmediate(captureTarget);}
                captureTarget=null;previousTarget=null;
            }
            if(scene.IsValid()&&scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
            yield return null;
            Assert.That(audio==null||!audio.IsAvailable,Is.True,"Capture left native audio alive.");
            scene=default;bootstrap=null;audio=null;map=null;binder=null;entry=null;camera=null;
        }
    }
}
#endif
