using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatPrefabTests
    {
        private const string Res="Assets/Game/Modules/ChannelChat/Res/Prefab/";
        [Test] public void HeaderEntryUsesOriginalArtAndOneConfiguredNormalSound()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(Res+"ChatEntry.prefab");Assert.That(root,Is.Not.Null,"公共频道顶栏入口尚未生成");
            var map=root.GetComponent<BindingMap>();var button=map.Require<GameButton>("button");var source=map.Require<AudioSource>("source");
            Assert.That(((Image)button.targetGraphic).sprite.name,Is.EqualTo("HeaderChat"));Assert.That(button.FeedbackSound,Is.EqualTo(GameUiFeedback.Sound.Normal));
            Assert.That(source.playOnAwake,Is.False);Assert.That(source.clip,Is.Not.Null);Assert.That(new SerializedObject(button).FindProperty("clickAudio").objectReferenceValue,Is.SameAs(source));
        }
        [Test] public void DrawerRetainsOriginalFixedSourceWidthAndPlainTextBindings()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(Res+"ChannelChat.prefab");Assert.That(root,Is.Not.Null,"公共频道页面尚未生成");var map=root.GetComponent<BindingMap>();
            foreach(string key in new[]{"close","favorite","channelSwitch","opacity","settings","publicTab","guildTab","send","emoji","favoriteSave","channelSave","allNotifications","notification0","notification1","notification2","notification3","notificationTab","guildNotificationTab","keywordTab"})Assert.That(map.Require<GameButton>(key),Is.Not.Null,key);
            foreach(string key in new[]{"input","favoriteInput","channelInput","keyword0","keyword1","keyword2","keyword3","keyword4"})Assert.That(map.Require<TMP_InputField>(key).richText,Is.False,key);
            Assert.That(map.Require<RectTransform>("drawer").sizeDelta,Is.EqualTo(new Vector2(924,1080)));
            var rewardText = map.Require<GameObject>("row").GetComponent<BindingMap>().Require<GameText>("rewardText");
            Assert.That(rewardText.richText, Is.True, "服务端奖励播报使用模板着色");
            Assert.That(root.GetComponentsInChildren<GameText>(true).Where(t => t != rewardText).All(t => !t.richText),
                Is.True, "自由文本、昵称和关键词不可解析富文本");
            var frame=map.Require<Image>("drawerSurface");Assert.That(frame.sprite.name,Is.EqualTo("DrawerFrame"));Assert.That(frame.type,Is.EqualTo(Image.Type.Sliced));Assert.That(frame.pixelsPerUnitMultiplier,Is.EqualTo(1/1.5f).Within(.0001));
            Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c&&c.GetType().Name=="ChannelChat"),Is.False);
        }
        [Test] public void MessageRowsAndStickerPickerAreReusableSourceLayouts()
        {
            var root=AssetDatabase.LoadAssetAtPath<GameObject>(Res+"ChannelChat.prefab");Assert.That(root,Is.Not.Null);var map=root.GetComponent<BindingMap>();
            var template=map.Require<GameObject>("row");var row=template.GetComponent<BindingMap>();Assert.That(template.activeSelf,Is.False);
            foreach(string key in new[]{"sender","time","body"})Assert.That(row.Require<GameText>(key).richText,Is.False);
            Assert.That(row.Require<GameButton>("avatar"),Is.Not.Null);Assert.That(row.Require<Image>("sticker"),Is.Not.Null);
            Assert.That(map.Require<GameObject>("stickerLayer").activeSelf,Is.False);Assert.That(map.Require<GameObject>("stickerCell").activeSelf,Is.False);
            Assert.That(map.Require<ScrollRect>("stickerScroll").content.GetComponent<GridLayoutGroup>().constraintCount,Is.EqualTo(3));
            var catalogue=map.Require<BindingMap>("stickerArt");Assert.That(catalogue,Is.Not.Null);
        }
    }
}
