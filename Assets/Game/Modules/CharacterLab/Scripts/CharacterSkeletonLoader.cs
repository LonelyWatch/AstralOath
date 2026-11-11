using System.Collections.Generic;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using UnityEngine;
namespace AstralOath.Client.CharacterLab
{
    public static class CharacterSkeletonLoader
    {
        public static async Task<SkeletonPlayerSource> LoadSourceAsync(string address,IAssetService assets,BindingScope scope)
        {
            var text=await assets.LoadAsync<TextAsset>(address,scope);
            var descriptor=JsonUtility.FromJson<SkeletonDescriptor>(text.text);
            if(descriptor?.pages==null||string.IsNullOrWhiteSpace(descriptor.jsonAddress))throw new System.IO.InvalidDataException("Invalid skeleton descriptor: "+address);
            var json=await assets.LoadAsync<TextAsset>(descriptor.jsonAddress,scope);
            string atlas=string.IsNullOrWhiteSpace(descriptor.atlasAddress)?"":(await assets.LoadAsync<TextAsset>(descriptor.atlasAddress,scope)).text;
            var pages=new Dictionary<string,Texture2D>(System.StringComparer.Ordinal);
            foreach(var page in descriptor.pages)pages.Add(page.name,await assets.LoadAsync<Texture2D>(page.address,scope));
            scope.Token.ThrowIfCancellationRequested();
            return new SkeletonPlayerSource(json.text,atlas,pages,descriptor.premultipliedAlpha,descriptor.scale);
        }
        public static async Task<ISkeletonPlayer> LoadAsync(string address,IAssetService assets,ISkeletonPlayerFactory factory,Transform parent,BindingScope scope)
        {
            var source=await LoadSourceAsync(address,assets,scope);scope.Token.ThrowIfCancellationRequested();
            var player=factory.Create(source,parent);scope.Add(player.Dispose);return player;
        }
    }
}
