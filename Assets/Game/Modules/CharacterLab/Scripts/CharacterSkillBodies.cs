using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AstralOath.Client.CharacterLab
{
    // A MODEL_BODY node changes the visible body while the original ANI clock still drives its graph.
    internal sealed class CharacterSkillBodies
    {
        private readonly CharacterRecord character;
        private readonly Dictionary<string,ISkeletonPlayer> models=new Dictionary<string,ISkeletonPlayer>(StringComparer.Ordinal);
        public ISkeletonPlayer Source {get;private set;}
        public ISkeletonPlayer Visible {get;private set;}
        public string VisibleAddress {get;private set;}
        public CharacterStatusForm VisibleForm=>character.statusForms.FirstOrDefault(form=>form.modelAddress==VisibleAddress);

        public CharacterSkillBodies(CharacterRecord character,ISkeletonPlayer original,BindingScope scope)
        {
            this.character=character;
            models.Add(character.battleSkeletonAddress,original);
            Visible=Source=original;VisibleAddress=character.battleSkeletonAddress;
            scope.Add(()=>{if(original.Root)original.Root.SetActive(true);});
        }

        public async Task Prepare(JObject graph,CharacterSkillRecord skill,IAssetService assets,
            ISkeletonPlayerFactory factory,BindingScope owner,bool flipped)
        {
            var requested=new HashSet<string>(StringComparer.Ordinal);
            if(!string.IsNullOrEmpty(skill.modelAddress))requested.Add(skill.modelAddress);
            foreach(var node in graph["source"]["entities"].OfType<JObject>().Where(n=>(string)n["etty"]=="MODEL_BODY"))
                requested.Add(Address((string)node["model_id"]));
            foreach(string address in requested)
            {
                if(models.ContainsKey(address))continue;
                var form=character.statusForms.SingleOrDefault(row=>row.modelAddress==address);
                if(form==null)throw new InvalidDataException("Undeclared character model: "+address);
                var model=await CharacterSkeletonLoader.LoadAsync(address,assets,factory,Source.Root.transform.parent,owner);
                owner.Token.ThrowIfCancellationRequested();
                model.SetSkin(form.skin);model.SetFlip(flipped,false);
                model.Root.SetActive(false);
                models.Add(address,model);
            }
            if(!string.IsNullOrEmpty(skill.modelAddress))Show(skill.modelAddress);
            Source=Visible;
        }
        public void ShowSourceModel(string id)=>Show(Address(id));
        private static string Address(string id)=>"characterlab/source/model/"+id+".scsp";
        private void Show(string address)
        {
            if(VisibleAddress==address)return;
            var next=models[address];
            CopyTransform(Visible.Root.transform,next.Root.transform);
            Visible.Root.SetActive(false);next.Root.SetActive(true);
            Visible=next;VisibleAddress=address;
            string idle=Visible.Animations.FirstOrDefault(a=>a.Name=="b_idle")?.Name;
            if(idle!=null)Visible.Play(idle,true);
        }
        private static void CopyTransform(Transform origin,Transform destination)
        {
            destination.localPosition=origin.localPosition;
            destination.localRotation=origin.localRotation;
            destination.localScale=origin.localScale;
        }
        public void Tick(float delta)
        {
            Source.Tick(delta);
            if(Visible==Source)return;
            CopyTransform(Source.Root.transform,Visible.Root.transform);
            Visible.Tick(delta);
        }
        public void Pause(bool value){Source.Pause(value);if(Visible!=Source)Visible.Pause(value);}
        public void Flip(bool value){foreach(var body in models.Values)body.SetFlip(value,false);}
    }
}
