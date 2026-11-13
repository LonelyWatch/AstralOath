using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using AstralOath.Client.CharacterPresentation;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace AstralOath.Client.CharacterLab
{
    public sealed class CharacterLabWindowBinder : IDisposable
    {
        private readonly CharacterLabWindow view;
        private readonly CharacterLabLaunchContext context;
        private readonly BindingScope scope;
        private readonly IAssetService assets;
        private readonly ILocaleService locale;
        private readonly Action close;
        private readonly PresentationClock clock = new PresentationClock();
        private readonly Dictionary<string,BindingScope> listScopes = new Dictionary<string,BindingScope>();
        private readonly List<string> diagnostics = new List<string>();
        private readonly List<ICharacterAudioPlayback> sounds = new List<ICharacterAudioPlayback>();
        private BindingScope selectionScope;
        private BindingScope performanceScope;
        private ISkeletonPlayer player;
        private CharacterLabCatalog catalog;
        private CharacterRecord selected;
        private CharacterSkillRecord selectedSkill;
        private StgPlaybackSession performance;
        private CharacterEffectStage effects;
        private CharacterBlurPreview blurPreview;
        private bool portrait, loop = true, flipped, experimental, disposed;
        private string animation;
        private int skinIndex, backgroundIndex;
        private float transportRefresh;
        private float defaultZoom=5;
        private Vector3 defaultCameraPosition=new Vector3(0,1,-10);
        public CharacterLabWindowBinder(CharacterLabWindow view, CharacterLabLaunchContext context, BindingScope owner, ILocaleService locale, Action close)
        {
            this.view = view; this.context = context; this.locale=locale; this.close=close;
            scope = new BindingScope(owner.Token);
            scope.Add(owner.Add(Dispose));
            assets = context.Services.Get<IAssetService>();
        }
        public async Task OpenAsync()
        {
            view.Show();
            foreach (string key in new[]{"title","subtitle","targets"}) Show(key,"lab."+key);
            foreach (string key in new[]{"animations","voices","skills","diagnostics"})
                Show(key+"Heading","lab."+key);
            Show("searchPlaceholder","lab.search");
            Bind("battle", () => SelectMode(false)); Bind("portrait", () => SelectMode(true));
            BindAsync("play", PlayAsync); Bind("pause", Pause); Bind("stop", Stop); Bind("close", close);
            Bind("step", Step); Bind("slower", () => Speed(-.25)); Bind("faster", () => Speed(.25));
            Bind("seekBack", () => Seek(-.5)); Bind("seekForward", () => Seek(.5));
            Bind("loop", () => { loop=!loop; UpdateLoop(); if (player!=null && performance==null) RestartAnimation(false); });
            Bind("zoomIn", () => context.PreviewCamera.orthographicSize = Mathf.Max(.5f,context.PreviewCamera.orthographicSize*.8f));
            Bind("zoomOut", () => context.PreviewCamera.orthographicSize = Mathf.Min(20,context.PreviewCamera.orthographicSize*1.25f));
            Bind("flip", () => { flipped=!flipped; player?.SetFlip(flipped,false); });
            Bind("background", () => {
                var colors = new[]{new Color32(18,25,38,255),new Color32(92,96,105,255),new Color32(235,232,220,255),new Color32(35,96,79,255)};
                context.PreviewCamera.backgroundColor=colors[++backgroundIndex%colors.Length];
            });
            BindAsync("reset", Reset);
            Bind("skin", () => {
                if(player==null || player.Skins.Count==0) return;
                skinIndex=(skinIndex+1)%player.Skins.Count; player.SetSkin(player.Skins[skinIndex]);
                Show("skinLabel","lab.skin",player.Skins[skinIndex]);
            });
            Bind("experimental", () => { experimental=!experimental; Show("experimentalLabel","lab.experimental",locale.Resolve(experimental?"lab.on":"lab.off")); if(experimental) Diagnose(locale.Resolve("lab.experimental_warning")); });
            Show("skinLabel","lab.skin_empty");
            Show("experimentalLabel","lab.experimental",locale.Resolve("lab.off"));
            UpdateLoop();
            UnityAction<string> search = _ => RenderCharacters();
            var searchField=view.Search;
            searchField.onValueChanged.AddListener(search);
            scope.Add(() => {if(searchField)searchField.onValueChanged.RemoveListener(search);});
            var blurMaterial=await assets.LoadAsync<Material>("characterlab/sprite-blur-material",scope);
            scope.Token.ThrowIfCancellationRequested();
            blurPreview=new CharacterBlurPreview(context.PreviewCamera,view.Preview,blurMaterial);scope.Add(blurPreview.Dispose);
            var text = await assets.LoadAsync<TextAsset>("characterlab/catalog",scope);
            catalog=JsonUtility.FromJson<CharacterLabCatalog>(text.text);
            if (catalog?.characters == null || catalog.characters.Length == 0) throw new System.IO.InvalidDataException("CharacterLab catalog is empty.");
            RenderCharacters();
            await SelectCharacter(catalog.characters[0]);
        }
        private void Show(string element,string key,params object[] args) => view.Text(element).Bind(locale,scope,key,args);
        private void Bind(string key,Action action) => BindAsync(key,() => {action();return Task.CompletedTask;});
        private void Bind(string key,Func<Task> action) => BindAsync(key,action);
        private void BindAsync(string key,Func<Task> action)
        {
            if(key!="skin" && key!="loop" && key!="experimental") Show(key+"Label",CaptionKey(key));
            view.Button(key).Bind(scope, _ => Guard(action));
        }
        private static string CaptionKey(string bindingKey) => bindingKey switch
        {
            "zoomIn" => "lab.zoom_in",
            "zoomOut" => "lab.zoom_out",
            "seekBack" => "lab.seek_back",
            "seekForward" => "lab.seek_forward",
            _ => "lab."+bindingKey
        };
        private async Task Guard(Func<Task> action)
        {
            try { await action(); }
            catch (OperationCanceledException) { if(!scope.Token.IsCancellationRequested) Diagnose("Cancelled or timed out."); }
            catch(Exception error) { if(!scope.Token.IsCancellationRequested) {Show("status","lab.error",error.Message);Diagnose(error.Message);} Debug.LogException(error); }
        }
        private void RenderCharacters()
        {
            if(catalog==null) return;
            string query = view.Search.text?.Trim() ?? "";
            Rows("characters",catalog.characters.Where(c => (c.id+" "+Name(c)).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)
                .Select(c => (c.id+" · "+Name(c),(Func<Task>)(() => SelectCharacter(c)))));
        }
        private string Name(CharacterRecord record) => DisplayName(record.displayNameKey,string.IsNullOrWhiteSpace(record.name)?record.id:record.name);
        private string DisplayName(string key,string fallback)
        {
            if(string.IsNullOrWhiteSpace(key))return fallback;
            try {return locale.Resolve(key);} catch(KeyNotFoundException) {return fallback;}
        }
        private string ResourceLabel(string key,string id)
        {
            string name=DisplayName(key,id);
            return name==id?id:name+" · "+id;
        }
        private void Rows(string list,IEnumerable<(string text,Func<Task> action)> values)
        {
            if(listScopes.TryGetValue(list,out var previous)) previous.Dispose();
            var rows = new BindingScope(scope.Token); listScopes[list]=rows;
            foreach(var value in values)
            {
                var row = UnityEngine.Object.Instantiate(view.RowTemplate.gameObject,view.List(list),false);
                row.name=list+" row"; row.SetActive(true);
                rows.Add(() => UnityEngine.Object.Destroy(row));
                row.GetComponentInChildren<GameText>(true).Bind(locale,rows,"lab.raw",value.text);
                row.GetComponent<GameButton>().Bind(rows,_ => Guard(value.action));
            }
        }
        private async Task SelectMode(bool value) { portrait=value; if(selected!=null) await SelectCharacter(selected); }
        private async Task SelectCharacter(CharacterRecord character)
        {
            Stop(); selected=character; selectedSkill=null; player=null;
            selectionScope?.Dispose();
            selectionScope=new BindingScope(scope.Token);
            var owner=selectionScope;
            diagnostics.Clear(); Show("diagnostics","lab.raw","");
            string address=portrait?character.portraitSkeletonAddress:character.battleSkeletonAddress;
            Rows("animations",Array.Empty<(string,Func<Task>)>());
            Rows("voices",(character.voices??Array.Empty<CharacterVoiceRecord>()).Select(v=>(ResourceLabel(v.displayNameKey,v.id),(Func<Task>)(() => {PlayVoice(v);return Task.CompletedTask;}))));
            Rows("skills",(character.skills??Array.Empty<CharacterSkillRecord>()).Select(s=>(ResourceLabel(s.displayNameKey,s.id),(Func<Task>)(() => PlaySkill(s)))));
            if(string.IsNullOrWhiteSpace(address)) {Show("status","lab.missing",portrait?"portrait":"battle");return;}
            Show("status","lab.loading",Name(character));
            var loaded=await CharacterSkeletonLoader.LoadAsync(address,assets,context.Skeletons,context.WorldRoot,owner);
            owner.Token.ThrowIfCancellationRequested();
            if(selectionScope!=owner) return;
            skinIndex=CharacterSkinSelection.Apply(character,portrait,loaded);
            player=loaded; player.Root.transform.localPosition=new Vector3(0,-2,0); player.SetFlip(flipped,false);
            FitPreview();
            player.Event+=OnSkeletonEvent; owner.Add(() => loaded.Event-=OnSkeletonEvent);
            Rows("animations",player.Animations.Select(a=>(a.Name+"  "+(a.Duration==0?locale.Resolve("lab.static_pose"):a.Duration.ToString("F2")+"s"),(Func<Task>)(() => {SelectAnimation(a.Name);return Task.CompletedTask;}))));
            Show("skinLabel","lab.skin",player.Skins[skinIndex]);
            Show("status","lab.ready",Name(character),player.Version);
            animation=player.Animations.FirstOrDefault(a=>a.Name=="idle" || a.Name=="b_idle")?.Name ?? player.Animations.FirstOrDefault()?.Name;
            if(animation!=null) SelectAnimation(animation);
            if(!context.Audio.IsAvailable) Diagnose(locale.Resolve("lab.unavailable").Replace("{0}",context.Audio.Diagnostic));
        }
        private void FitPreview()
        {
            var renderers=player.Root.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)return;
            var bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            if(bounds.size.sqrMagnitude<.001f)return;
            defaultZoom=Mathf.Clamp(Mathf.Max(bounds.extents.y/.58f,bounds.extents.x/(Mathf.Max(.1f,context.PreviewCamera.aspect)*.45f))*1.1f,1,20);
            defaultCameraPosition=new Vector3(bounds.center.x,bounds.center.y-defaultZoom*.16f,-10);
            context.PreviewCamera.orthographicSize=defaultZoom;context.PreviewCamera.transform.position=defaultCameraPosition;
        }
        private void SelectAnimation(string name)
        {
            EndPerformance(); selectedSkill=null; animation=name;
            clock.ResetWithAudio(); RestartAnimation(true);
        }
        private void RestartAnimation(bool reset)
        {
            if(player==null || string.IsNullOrEmpty(animation)) return;
            if(reset) clock.ResetWithAudio();
            StopSounds(); player.Play(animation,loop); clock.Play();
        }
        private async Task PlayAsync()
        {
            if(selectedSkill!=null && performance==null){await PlaySkill(selectedSkill);return;}
            Play();
        }
        private void Play()
        {
            if(player==null) {Show("status","lab.choose_animation");return;}
            if(string.IsNullOrEmpty(player.CurrentAnimation) && animation!=null) player.Play(animation,loop);
            player.Pause(false); foreach(var sound in sounds) sound.Pause(false); clock.Play();
        }
        private void Pause() {clock.Pause();player?.Pause(true);foreach(var sound in sounds)sound.Pause(true);}
        private void Stop()
        {
            clock.ResetWithAudio(); StopSounds(); EndPerformance(); player?.Stop();
        }
        private void StopSounds() {foreach(var sound in sounds) sound.Dispose();sounds.Clear();}
        private void Speed(double change) {clock.SetSpeed(Math.Max(.25,Math.Min(4,clock.Speed+change)));StopSounds();}
        private void Seek(double change)
        {
            if(player==null) return;
            if(performance!=null) {Diagnose(locale.Resolve("lab.not_supported").Replace("{0}","STG seek: use frame stepping or reset"));return;}
            StopSounds();clock.Seek(Math.Max(0,player.Time+change));player.Seek((float)clock.Time);
        }
        private void Step()
        {
            if(player==null) return;
            Pause();StopSounds();
            clock.Step(1d/30);
            effects?.BeginFrame();performance?.Advance(1f/30); player.Pause(false);player.Tick(1f/30);player.Pause(true);
            effects?.Tick(1f/30);effects?.BeginFrame();performance?.Advance(0);effects?.Tick(0);
        }
        private async Task Reset()
        {
            context.PreviewCamera.orthographicSize=defaultZoom;
            context.PreviewCamera.transform.position=defaultCameraPosition;
            flipped=false;player?.SetFlip(false,false);
            if(player!=null) player.Root.transform.localPosition=new Vector3(0,-2,0);
            clock.ResetWithAudio();StopSounds();
            if(selectedSkill!=null) await PlaySkill(selectedSkill); else RestartAnimation(true);
        }
        private void PlayVoice(CharacterVoiceRecord voice)
        {
            if(!clock.AudioAllowed){Show("status","lab.silent");return;}
            StopSounds();
            if(!context.Audio.IsAvailable) {Show("status","lab.unavailable",context.Audio.Diagnostic);return;}
            var sound=!string.IsNullOrEmpty(voice.audioTableKey)?context.Audio.PlayVoice(voice.audioTableKey):context.Audio.PlayEvent(voice.eventPath);
            sounds.Add(sound);Show("status","lab.voice_status",voice.id);
        }
        private async Task PlaySkill(CharacterSkillRecord skill)
        {
            if(player==null || portrait) {Diagnose(locale.Resolve("lab.not_supported").Replace("{0}","skill requires battle skeleton"));return;}
            EndPerformance();StopSounds();selectedSkill=skill;
            var owner=new BindingScope(selectionScope.Token);performanceScope=owner;
            var text=await assets.LoadAsync<TextAsset>(skill.graphAddress,owner);
            owner.Token.ThrowIfCancellationRequested();
            if(performanceScope!=owner) return;
            clock.ResetWithAudio();
            var graph=JObject.Parse(text.text);
            effects=new CharacterEffectStage(context,owner,player,(string)graph["id"]??skill.id,experimental,Diagnose,blurPreview);
            owner.Add(effects.Dispose);
            await effects.PrepareAsync(graph,selected,catalog);
            owner.Token.ThrowIfCancellationRequested();
            if(performanceScope!=owner)return;
            performance=new StgPlaybackSession(graph,player,context.PreviewCamera,experimental,Diagnose);
            performance.ResolveLocation=effects.ResolveLocation;
            performance.Timeline=effects.Timeline;
            performance.Effect+=effects.Effect;
            performance.Animation+=effects.Animation;
            owner.Add(performance.Dispose);
            performance.Start();
            clock.Play();
            foreach(string eventPath in new[]{skill.eventPath,skill.voiceEventPath})
                if(context.Audio.IsAvailable && !string.IsNullOrWhiteSpace(eventPath)) sounds.Add(context.Audio.PlayEvent(eventPath));
            Show("status","lab.skill_diagnostic",skill.id);
        }
        private void OnSkeletonEvent(SkeletonPlaybackEvent value)
        {
            if(value.Kind==SkeletonPlaybackEventKind.Event) {effects?.Assist(value.Name); Diagnose("EVENT "+value.Name+" @ "+player.Time.ToString("F3")+"s");}
        }
        private void EndPerformance() {performance=null;effects=null;var old=performanceScope;performanceScope=null;old?.Dispose();}
        private void Diagnose(string message)
        {
            if(disposed) return;
            diagnostics.Add(message);if(diagnostics.Count>80)diagnostics.RemoveAt(0);
            Show("diagnostics","lab.raw",string.Join("\n",diagnostics));
        }
        private void UpdateLoop() => Show("loopLabel","lab.loop",locale.Resolve(loop?"lab.on":"lab.off"));
        public void Tick(float delta)
        {
            if(disposed || scope.Token.IsCancellationRequested) return;
            blurPreview?.Resize();
            float step=(float)clock.Advance(delta);
            if(step>0) {effects?.BeginFrame();performance?.Advance(step);player?.Tick(step);effects?.Tick(step);effects?.BeginFrame();performance?.Advance(0);effects?.Tick(0);}
            transportRefresh+=delta;
            if(transportRefresh>.1f) {transportRefresh=0;Show("transport","lab.transport",clock.Time,clock.Speed,locale.Resolve(clock.AudioAllowed?"lab.sound":"lab.silent"));}
        }
        private static void Release(Action release,List<Exception> errors)
        {try{release();}catch(Exception error){errors.Add(error);}}
        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // Destruction can arrive after the scene's renderers/canvas have already gone.
            // Stop() is a user operation that redraws setup pose, so disposal must not call it.
            clock.Pause();player=null;performance=null;effects=null;
            var oldPerformance=performanceScope;performanceScope=null;
            var oldSelection=selectionScope;selectionScope=null;
            var oldSounds=sounds.ToArray();sounds.Clear();
            var oldRows=listScopes.Values.ToArray();listScopes.Clear();
            var errors=new List<Exception>();
            foreach(var sound in oldSounds)Release(sound.Dispose,errors);
            if(oldPerformance!=null)Release(oldPerformance.Dispose,errors);
            if(oldSelection!=null)Release(oldSelection.Dispose,errors);
            foreach(var rows in oldRows)Release(rows.Dispose,errors);
            Release(scope.Dispose,errors);
            Release(()=>{if(view.Root)view.Hide();},errors);
            if(errors.Count>0)throw new AggregateException(errors);
        }
    }
}
