using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AstralOath.Editor;
using NUnit.Framework;
namespace AstralOath.Client.Tests
{
    public sealed class CharacterLabAssetValidationTests
    {
        public const string Character = "{\"id\":\"c1001\",\"name\":\"Hero\",\"displayNameKey\":\"hero\",\"battleSkeletonAddress\":\"model\",\"portraitSkeletonAddress\":\"portrait\",\"battleSkin\":\"default\",\"portraitSkin\":\"normal\",\"voiceBankAddress\":\"bank\",\"timelineAddress\":\"\",\"voices\":[{\"id\":\"v\",\"audioTableKey\":\"voice\",\"eventPath\":\"\",\"displayNameKey\":\"voice\"}],\"skills\":[{\"id\":\"s1\",\"displayNameKey\":\"skill\",\"graphAddress\":\"graph\",\"eventPath\":\"\",\"voiceEventPath\":\"\"}]}";
        public const string Catalog = "{\"schemaVersion\":1,\"sharedBanks\":[\"bank\"],\"characters\":[" + Character + "]}";
        private static readonly Dictionary<string,string> Resources = new Dictionary<string,string>
        { ["model"]="skeleton", ["portrait"]="skeleton", ["model-json"]="spine-json", ["portrait-json"]="spine-json",
            ["bank"]="bank", ["graph"]="graph", ["unloaded-bank"]="bank" };
        internal static string ResourceJson(string address) => address switch
        {
            "model" => "{\"jsonAddress\":\"model-json\"}",
            "portrait" => "{\"jsonAddress\":\"portrait-json\"}",
            "model-json" => "{\"skeleton\":{\"spine\":\"2.1.27\"},\"skins\":{\"default\":{}}}",
            "portrait-json" => "{\"skeleton\":{\"spine\":\"3.8.99\"},\"skins\":[{\"name\":\"normal\",\"attachments\":{}}]}",
            _ => throw new InvalidDataException("Unexpected fixture address: " + address)
        };
        public static string Locale()
        {
            const string keys = "hero voice skill lab.title lab.close lab.subtitle lab.search lab.animations lab.voices lab.skills lab.diagnostics lab.battle lab.portrait lab.play lab.pause lab.stop lab.step lab.loop lab.on lab.off lab.slower lab.faster lab.zoom_in lab.zoom_out lab.flip lab.background lab.reset lab.skin lab.skin_empty lab.loading lab.ready lab.missing lab.error lab.transport lab.sound lab.silent lab.raw lab.voice_status lab.unavailable lab.choose_animation lab.empty lab.skill lab.skill_diagnostic lab.not_supported lab.selection lab.targets lab.experimental lab.experimental_warning lab.seek_back lab.seek_forward lab.static_pose";
            return "{\"rows\":[" + string.Join(",",keys.Split(' ').Select(key=>"{\"key\":\""+key+"\",\"zhCn\":\"Text\"}")) + "]}";
        }
        [Test] public void ValidCatalogResolvesTypedResourcesAndLocaleKeys()
        { Assert.DoesNotThrow(()=>CharacterLabAssetValidation.Validate(Catalog,Locale(),Resources,ResourceJson)); }
        [TestCase("malformed")][TestCase("missing-banks")][TestCase("missing-resource")]
        [TestCase("duplicate-character")][TestCase("missing-locale")][TestCase("missing-ui-locale")]
        [TestCase("wrong-kind")][TestCase("wrong-schema")][TestCase("unloaded-voice-bank")]
        [TestCase("duplicate-locale")][TestCase("empty-characters")]
        public void DamagedCatalogCannotReachBuildSuccess(string damage)
        {
            string catalog=Catalog, locale=Locale();
            switch(damage)
            {
                case "malformed": catalog="{"; break;
                case "missing-banks": catalog=Catalog.Replace("\"sharedBanks\":[\"bank\"],",""); break;
                case "missing-resource": catalog=Catalog.Replace("\"model\"","\"absent\""); break;
                case "duplicate-character": catalog=Catalog.Replace(Character,Character+","+Character); break;
                case "missing-locale": locale=locale.Replace("\"key\":\"hero\"","\"key\":\"other\""); break;
                case "missing-ui-locale": locale=locale.Replace("\"key\":\"lab.title\"","\"key\":\"other\""); break;
                case "wrong-kind": catalog=Catalog.Replace("\"battleSkeletonAddress\":\"model\"","\"battleSkeletonAddress\":\"bank\""); break;
                case "wrong-schema": catalog=Catalog.Replace("\"schemaVersion\":1","\"schemaVersion\":9"); break;
                case "unloaded-voice-bank": catalog=Catalog.Replace("\"voiceBankAddress\":\"bank\"","\"voiceBankAddress\":\"unloaded-bank\""); break;
                case "duplicate-locale": locale=locale.Replace("\"key\":\"voice\"","\"key\":\"hero\""); break;
                case "empty-characters": catalog=Catalog.Replace(Character,""); break;
                default: throw new ArgumentException(damage);
            }
            Assert.Throws<InvalidDataException>(()=>CharacterLabAssetValidation.Validate(catalog,locale,Resources,ResourceJson));
        }

        [TestCase("battleSkin", "missing")][TestCase("portraitSkin", "missing")]
        [TestCase("battleSkin", "empty")][TestCase("portraitSkin", "empty")]
        [TestCase("battleSkin", "blank")][TestCase("portraitSkin", "blank")]
        [TestCase("battleSkin", "unknown")][TestCase("portraitSkin", "unknown")]
        public void CatalogSkinMustBeExplicitAndExistInItsOwnSkeleton(string field, string damage)
        {
            var catalog = Newtonsoft.Json.Linq.JObject.Parse(Catalog);
            var character = (Newtonsoft.Json.Linq.JObject)catalog["characters"][0];
            if (damage == "missing") character.Remove(field);
            else character[field] = damage == "empty" ? "" : damage == "blank" ? " \t " : field == "battleSkin" ? "normal" : "default";
            try { CharacterLabAssetValidation.Validate(catalog.ToString(), Locale(), Resources, ResourceJson); }
            catch (InvalidDataException error)
            {
                if (!error.Message.Contains(field)) throw new AssertionException("Skin error must identify the catalog field: " + error.Message);
                return;
            }
            throw new AssertionException("Invalid " + field + " (" + damage + ") reached build success.");
        }
    }
}
