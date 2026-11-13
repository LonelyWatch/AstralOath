using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    public sealed class BattleSkillTimelineOverrideTests
    {
        private static JObject Graph() => JObject.Parse(@"{
            'id':'skill','source':{'entities':[{'guid':7,'etty':'CAM','time':350,'x':25}]},
            'additions':{'entities':[{'guid':9,'etty':'CUTIN','time':20}]}
        }");

        [Test]
        public void AppliesChangesByGuidWithoutMutatingImportedGraph()
        {
            var source = Graph();
            var patch = JObject.Parse(@"{
                'version':1,'graph':'skill','nodes':{
                    '7':{'atMs':625,'fields':{'time':450,'x':60}},
                    '9':{'fields':{'time':50}}
                }
            }");
            var result = Apply(source, patch);
            Assert.That((int)source["source"]["entities"][0]["time"], Is.EqualTo(350));
            Assert.That((int)result["source"]["entities"][0]["time"], Is.EqualTo(450));
            Assert.That((int)result["source"]["entities"][0]["x"], Is.EqualTo(60));
            Assert.That((int)result["source"]["entities"][0]["_astralAtMs"], Is.EqualTo(625));
            Assert.That((int)result["additions"]["entities"][0]["time"], Is.EqualTo(50));
        }

        [TestCase(@"{'version':1,'graph':'other','nodes':{}}")]
        [TestCase(@"{'version':1,'graph':'skill','nodes':{'8':{'fields':{'time':10}}}}")]
        [TestCase(@"{'version':1,'graph':'skill','nodes':{'7':{'atMs':-1}}}")]
        [TestCase(@"{'version':1,'graph':'skill','nodes':{'7':{'fields':{'guid':8}}}}")]
        [TestCase(@"{'version':1,'graph':'skill','nodes':{'7':{'fields':{'source':'missing.cfx'}}}}")]
        public void RejectsIncompatibleOrUnsafeChanges(string json) =>
            Assert.That(() => Apply(Graph(), JObject.Parse(json)), Throws.TypeOf<InvalidOperationException>());

        private static JObject Apply(JObject source, JObject patch)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Single(assembly => assembly.GetName().Name == "AstralOath.Client.BattleScene")
                .GetType("AstralOath.Client.BattleScene.SkillTimelineOverrides", throwOnError: true);
            try { return (JObject)type.GetMethod("Apply", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { source, patch }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
