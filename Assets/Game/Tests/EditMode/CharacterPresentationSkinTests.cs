using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Client.CharacterLab;
using AstralOath.Client.Skeletons;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterPresentationSkinTests
    {
        private readonly List<ISkeletonPlayer> players=new List<ISkeletonPlayer>();
        private readonly List<Texture2D> textures=new List<Texture2D>();
        private GameObject parent;
        private Dictionary<string,string> paths;
        [SetUp] public void SetUp()
        {
            parent=new GameObject("portrait-skin-test");
            var manifest=JObject.Parse(File.ReadAllText("Assets/Game/Modules/CharacterPresentation/Configs/import-manifest.json"));
            paths=((JArray)manifest["entries"]).ToDictionary(e=>(string)e["address"],e=>(string)e["path"]);
        }
        [TearDown] public void TearDown()
        {
            foreach(var player in players)player.Dispose();players.Clear();
            foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);textures.Clear();
            UnityEngine.Object.DestroyImmediate(parent);
        }
        [TestCase("c1001","eye_06")]
        [TestCase("c1005","normal/normal1")]
        [TestCase("c1062","eye_nm")]
        public void ConfiguredPortraitSkinRendersRealFaceAttachments(string id,string faceSlot)
        {
            var catalog=JsonUtility.FromJson<CharacterLabCatalog>(File.ReadAllText("Assets/Game/Modules/CharacterLab/Configs/catalog.json"));
            var character=catalog.characters.Single(c=>c.id==id);
            Assert.That(character.portraitSkin,Is.EqualTo("normal"),"The source table must explicitly select the expression skin.");
            Assert.That(character.battleSkin,Is.EqualTo("default"));
            var source=Load(character.portraitSkeletonAddress);
            var json=JObject.Parse(source.Json);
            Assert.That(Skin(json,"default")[faceSlot],Is.Null,"This fixture must reproduce the shared-only default skin.");
            Assert.That(Skin(json,"normal")[faceSlot],Is.Not.Null,"The selected expression owns a real facial attachment.");
            var player=Create(source);
            int bodyVertices=Vertices(player);
            int index=CharacterSkinSelection.Apply(character,true,player);
            Assert.That(player.Skins[index],Is.EqualTo("normal"));
            Assert.That(Vertices(player),Is.GreaterThan(bodyVertices),"Applying the configured expression must add its geometry.");
            int completeFaceVertices=Vertices(player);
            ((JObject)((JArray)json["slots"]).Single(slot=>(string)slot["name"]==faceSlot)).Remove("attachment");
            var missingFace=Create(new SkeletonPlayerSource(json.ToString(),source.Atlas,source.Textures,source.PremultipliedAlpha,source.Scale));
            CharacterSkinSelection.Apply(character,true,missingFace);
            Assert.That(Vertices(missingFace),Is.LessThan(completeFaceVertices),"The facial attachment must reach the rendered mesh, not merely exist in JSON.");
            // Exercise both explicit fields on one real skeleton.
            CharacterSkinSelection.Apply(character,false,player);
            Assert.That(Vertices(player),Is.EqualTo(bodyVertices));
            character.portraitSkin="angry";
            index=CharacterSkinSelection.Apply(character,true,player);
            Assert.That(player.Skins[index],Is.EqualTo("angry"),"The configured value must not be hardcoded to normal.");
            foreach(string invalid in new[]{null," ","absent-skin"})
            {
                character.portraitSkin=invalid;
                Assert.Throws<InvalidDataException>(()=>CharacterSkinSelection.Apply(character,true,player));
            }
        }
        private static JObject Skin(JObject json,string name) => json["skins"] is JArray array
            ? (JObject)array.Single(s=>(string)s["name"]==name)["attachments"]
            : (JObject)json["skins"][name];
        private static int Vertices(ISkeletonPlayer player) => player.Root.GetComponent<MeshFilter>().sharedMesh.vertexCount;
        private ISkeletonPlayer Create(SkeletonPlayerSource source)
        {
            var player=new SkeletonPlayerFactory().Create(source,parent.transform);players.Add(player);return player;
        }
        private SkeletonPlayerSource Load(string address)
        {
            var descriptor=JsonUtility.FromJson<SkeletonDescriptor>(File.ReadAllText(paths[address]));
            var pages=new Dictionary<string,Texture2D>(StringComparer.Ordinal);
            foreach(var page in descriptor.pages)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);textures.Add(texture);
                Assert.That(ImageConversion.LoadImage(texture,File.ReadAllBytes(paths[page.address]),false),Is.True);
                pages.Add(page.name,texture);
            }
            return new SkeletonPlayerSource(File.ReadAllText(paths[descriptor.jsonAddress]),
                File.ReadAllText(paths[descriptor.atlasAddress]),pages,descriptor.premultipliedAlpha,descriptor.scale);
        }
    }
}
