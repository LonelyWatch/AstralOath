#if UNITY_EDITOR
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class ChannelChatEntryUnreadTests
    {
        [TestCase("Assets/Game/Modules/ChannelChat/Res/Prefab/ChatEntry.prefab", "unreadBadge")]
        [TestCase("Assets/Game/Modules/Arena/Res/Prefab/ArenaRoom.prefab", "channelUnreadBadge")]
        public void EveryPublicChatEntryUsesTheSharedNotificationBadge(string path, string key)
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<BindingMap>();
            Assert.DoesNotThrow(() => map.Require<BindingMap>(key), "Public chat entries need the same login-owned unread count.");
            var badge = map.Require<BindingMap>(key);
            Assert.That(badge.Require<UnityEngine.UI.Image>("mark").sprite, Is.Not.Null, "Use the authored badge sprite, never a generated solid circle.");
            Assert.That(badge.Require<GameText>("label").font.name, Does.Contain("MailCount"), "Use the existing original count digits.");
        }

        [Test] public void RoomHasLeftPublicChannelAndRightArenaButtons()
        {
            var map = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Modules/Arena/Res/Prefab/ArenaRoom.prefab").GetComponent<BindingMap>();
            Assert.DoesNotThrow(() => map.Require<GameButton>("channelTab"), "Room needs a public-channel tab alongside its existing arena chat.");
            var channel = map.Require<GameButton>("channelTab"); var arena = map.Require<GameButton>("arenaTab");
            Assert.That(channel.transform.position.x, Is.LessThan(arena.transform.position.x));
        }
    }
}
#endif
