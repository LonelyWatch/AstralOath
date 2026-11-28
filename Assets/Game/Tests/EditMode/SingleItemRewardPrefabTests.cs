using System;
using System.IO;
using System.Linq;
using System.Threading;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using AstralOath.Client.Mail;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object=UnityEngine.Object;
namespace AstralOath.Client.Tests
{
    public sealed class SingleItemRewardPrefabTests
    {
        private const string Prefab="Assets/Game/Common/Res/Prefab/SingleItemReward.prefab";
        private const string Data="Assets/Game/Common/Res/SingleItemReward/source-particles.json";
        private static GameObject Asset(){var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Prefab);Assert.That(prefab,Is.Not.Null,"单项奖励必须是Common通用预制，不能继续临时拼邮件蓝条");return prefab;}

        [Test] public void SharedPrefabUsesOnlyCommonAssetsAndOriginalEightyImageParticles()
        {
            var prefab=Asset();var map=prefab.GetComponent<BindingMap>();Assert.That(map,Is.Not.Null);
            Assert.That(map.Require<Image>("effect").sprite,Is.Not.Null);map.Require<Image>("icon");
            foreach(string key in new[]{"quantity","kind","text"})Assert.That(map.Require<GameText>(key).richText,Is.False);
            var images=map.Require<RectTransform>("particles").GetComponentsInChildren<Image>(true);
            Assert.That(images.Length,Is.EqualTo(80),"两个源burst emitter max50+30，只建80个UGUI Image，不复用礼包粒子");
            Assert.That(images.All(i=>!i.raycastTarget&&!i.enabled&&i.sprite&&i.material),Is.True);
            Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true),Is.Empty);
            Assert.That(prefab.GetComponentsInChildren<RawImage>(true),Is.Empty,"全部Canvas图片使用UGUI Image");
            // The existing shared font has a legacy FoundationFont fallback in a module
            // directory. Both font files have user edits outside this task. Reusing the
            // shared face must not add a new Mail/other business dependency or rewrite it.
            var inheritedFontDependencies=AssetDatabase.GetDependencies("Assets/Game/Common/Res/Fonts/EpicSeven.asset",true);
            foreach(var label in prefab.GetComponentsInChildren<GameText>(true))
                Assert.That(AssetDatabase.GetAssetPath(label.font).StartsWith("Assets/Game/Common/Res/Fonts/"),Is.True);
            foreach(string dependency in AssetDatabase.GetDependencies(Prefab,true).Where(p=>p.StartsWith("Assets/")))
                Assert.That(dependency.StartsWith("Assets/Game/Common/")||dependency.StartsWith("Assets/Game/Core/")||
                    dependency.StartsWith("Assets/ThirdParty/Fonts/")||dependency.StartsWith("Assets/TextMesh Pro/")||
                    inheritedFontDependencies.Contains(dependency),Is.True,"共享奖励不得新增业务素材依赖："+dependency);
        }

        [Test] public void SourceParticleContractPreservesBurstDelayFivePointArgbAndAcceleration()
        {
            Assert.That(File.Exists(Data),Is.True,"需独立单项奖励原粒子数据");var data=JObject.Parse(File.ReadAllText(Data));
            Assert.That((int)data["sourcePrimitiveDelayMilliseconds"],Is.EqualTo(267));Assert.That((float)data["sourceY"],Is.EqualTo(35));Assert.That((int)data["sourceZ"],Is.EqualTo(8));
            Assert.That((float)data["sourceToDesignScale"],Is.EqualTo(.68f));Assert.That((bool)data["texturePremultiplied"],Is.True);
            var emitters=(JArray)data["emitters"];Assert.That(emitters.Count,Is.EqualTo(2));
            for(int i=0;i<2;i++)
            {
                var e=emitters[i];Assert.That((int)e["burningType"],Is.EqualTo(1),"burst，不能按duration-1持续冒随机星点");
                Assert.That((float)e["emissionRate"],Is.EqualTo(i==0?50:30));Assert.That((int)e["maxParticles"],Is.EqualTo(i==0?50:30));
                Assert.That((float)e["particleLifespan"],Is.EqualTo(.7f));Assert.That((float)e["particleLifespanVariance"],Is.EqualTo(.5f));
                Assert.That((float)e["radialAcceleration"],Is.EqualTo(i==0?40:2500));Assert.That((float)e["radialAccelVariance"],Is.EqualTo(i==0?39:2000));
                Assert.That((float)e["tangentialAccelVariance"],Is.EqualTo(200));Assert.That((int)e["blendFuncSource"],Is.EqualTo(770));Assert.That((int)e["blendFuncDestination"],Is.EqualTo(1));
                Assert.That((string)e["colorBlends"][0],Is.EqualTo("0,0,0.24313726,1,1;0.030201342,1,0,0.98039216,1;0.6006711,1,0,0.45490196,0.92941177;0.96644294,0.88235295,0,0,1;1,0,0,0,0.007843138"));
            }
            var generated=typeof(MailWindow).Assembly.GetType("AstralOath.Client.Mail.SingleItemRewardSource");Assert.That(generated,Is.Not.Null);
            var json=(string)generated.GetField("Json",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetRawConstantValue();
            Assert.That(JToken.DeepEquals(data,JObject.Parse(json)),Is.True,"生成C#数据与审计JSON必须由同一原源生成，Native不用JsonUtility业务类型反射");
        }

        [Test] public void ExistingAtlasGuidAndSixtyFourSourceFramesArePreserved()
        {
            var map=Asset().GetComponent<BindingMap>();var sheet=map.Require<Image>("effect").sprite.texture;
            Assert.That(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sheet)),Is.EqualTo("2541e29b5dbb58759159550eb674bb7f"));
            Assert.That(sheet.width,Is.EqualTo(3072));Assert.That(sheet.height,Is.EqualTo(3072));
            Assert.That(((RectTransform)map.Require<Image>("effect").transform).rect.size,Is.EqualTo(new Vector2(1024,256)));
            var mail=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Mail/Res/Prefab/MailWindow.prefab").GetComponent<BindingMap>();
            foreach(string key in new[]{"singleReward","singleEffect","singleIcon","singleQuantity","singleKind","singleText"})Assert.That(mail.Require<Object>(key),Is.Not.Null);
            var nested=mail.Require<GameObject>("singleReward");Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(nested),Is.EqualTo(Asset()));
            Assert.That(mail.Require<Image>("singleEffect"),Is.Not.Null);
        }

        [Test] public void OrdinaryBinderUsesElapsedTimeBurstReentryAndOwnerCleanup()
        {
            var assembly=typeof(MailWindow).Assembly;var viewType=assembly.GetType("AstralOath.Client.Mail.SingleItemReward");var binderType=assembly.GetType("AstralOath.Client.Mail.SingleItemRewardBinder");
            Assert.That(viewType,Is.Not.Null);Assert.That(binderType,Is.Not.Null);Assert.That(typeof(MonoBehaviour).IsAssignableFrom(viewType),Is.False);
            var instance=Object.Instantiate(Asset());var scope=new BindingScope(CancellationToken.None);
            try
            {
                var view=(UIBaseView)viewType.GetMethod("ForInstance").Invoke(null,new object[]{instance});var binder=Activator.CreateInstance(binderType,view,scope);
                void Update(float elapsed)=>binderType.GetMethod("Update").Invoke(binder,new object[]{elapsed});
                int Count()=>(int)binderType.GetProperty("ActiveParticleCount").GetValue(binder);
                void Play()=>binderType.GetMethod("Play").Invoke(binder,new object[]{null,"42","currency","<b>literal</b>"});
                Play();Update(.266f);Assert.That(Count(),Is.Zero);Update(.277f);Assert.That(Count(),Is.EqualTo(80));
                var map=instance.GetComponent<BindingMap>();Assert.That(map.Require<GameText>("text").text,Is.EqualTo("<b>literal</b>"));
                var images=map.Require<RectTransform>("particles").GetComponentsInChildren<Image>(true);
                Assert.That(images.Any(i=>i.color.r<.25f&&i.color.g>.8f&&i.color.b>.95f&&i.color.a>.1f),Is.True,"原t/alpha/RGB曲线必须产生淡入的青蓝星，不能误读为RGBA或固定白点");
                var positions=images.Select(i=>i.rectTransform.anchoredPosition).ToArray();Update(.477f);
                Assert.That(images.Skip(50).Select((image,index)=>image.enabled&&Vector2.Distance(image.rectTransform.anchoredPosition,positions[index+50])>10).Any(moved=>moved),Is.True,"第二源emitter的径向2500±2000必须实际推进，不能只放静态随机点");
                Update(1.468f);Assert.That(Count(),Is.Zero,"所有源粒子最大寿命1.2+delay.267后结束，不能一直刷");
                Play();Update(.277f);Assert.That(Count(),Is.EqualTo(80),"再次领取重置而非叠加两批");
                binderType.GetMethod("Stop").Invoke(binder,null);Assert.That(Count(),Is.Zero);Play();scope.Dispose();Assert.That(instance.activeSelf,Is.False);
            }
            finally{scope.Dispose();Object.DestroyImmediate(instance);}
        }
    }
}
