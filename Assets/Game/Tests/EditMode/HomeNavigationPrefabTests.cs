using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Tests
{
    public sealed class HomeNavigationPrefabTests
    {
        [Test]public void MainHasSixUsableNavigationTargetsAndAuthorityHeaderFields()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HomeWindow.prefab");var map=prefab.GetComponent<BindingMap>();
            foreach(string id in new[]{"episode","side_story","battle","arena","summon","shop"})Assert.That(map.Require<GameButton>("feature."+id),Is.Not.Null);
            foreach(string id in new[]{"episode","side_story","battle","shop"})
            {var button=map.Require<GameButton>("feature."+id);Assert.That(button.interactable,Is.True,"未完成功能仍能点击得到说明");Assert.That(map.Require<GameText>("featureLabel."+id).color.a,Is.LessThan(.6f));}
            Assert.That(map.Require<GameButton>("profileOpen"),Is.Not.Null);Assert.That(map.Require<GameText>("profileRank").text,Does.Not.Contain("4"));
            Assert.That(map.Require<GameText>("profileExperiencePercent").text,Is.Empty,"无经验权威不能造29.3%或其他样例");
            Assert.That(map.Require<Image>("profileExperienceFill").enabled,Is.False);
            Assert.That(prefab.GetComponentsInChildren<BindingMap>(true).Any(m=>AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(m))=="Assets/Game/Modules/ChannelChat/Res/Prefab/ChatEntry.prefab"),Is.True);
        }
        [Test]public void MoreUnavailableGridAndBottomActionsAreRealButtons()
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Home/Res/Prefab/HomeMoreMenu.prefab");var map=prefab.GetComponent<BindingMap>();
            foreach(string id in new[]{"guide","collection","growth","pets","recruit","friends","guild","forge","achievements","waiting_room","imprint","notice","lobby_custom","pub","settings","music","music_prev","music_play","music_next","episode","side_story","battle","shop"})
            {var button=map.Require<GameButton>("feature."+id);Assert.That(button.interactable,Is.True,id);}
            Assert.That(map.Require<GameButton>("feature.arena"),Is.Not.Null);Assert.That(map.Require<GameButton>("summon"),Is.EqualTo(map.Require<GameButton>("feature.summon")));
        }
    }
}
