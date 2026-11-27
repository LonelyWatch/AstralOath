using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Arena;
using AstralOath.Client.ChannelChat;
using AstralOath.Client.Common;
using AstralOath.Client.Home;
using AstralOath.Client.PlayerProfile;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    // Explicit development-only installed-Player diagnostic. Never invoked by ordinary navigation.
    internal static class SocialPlayerSmoke
    {
        internal sealed class Hooks
        {
            internal Uri Endpoint;
            internal Transform Root;
            internal PlayerProfileSession ProfileSession;
            internal ChannelChatSession ChatSession;
            internal Func<CancellationToken, Task<string>> GetBearerAsync;
            internal Func<Task> ShowHomeAsync;
            internal Func<string, CancellationToken, Task> OpenProfileAsync;
            internal Func<CancellationToken, Task> OpenChatAsync, OpenMoreAsync;
            internal Action CloseProfile, CloseChat, CloseNotice;
            internal string CaptureDirectory, BuildIdentifier;
        }

        internal static bool Requested => Enabled(Application.isEditor, Debug.isDebugBuild, Environment.GetCommandLineArgs());
        internal static bool Enabled(bool editor, bool development, string[] args) =>
            !editor && development && args != null && Array.IndexOf(args, "-astral-social-smoke") >= 0;

        internal static void CheckTarget(Uri endpoint)
        {
            if (endpoint == null || !endpoint.IsAbsoluteUri || endpoint.Host != "127.0.0.6" ||
                !endpoint.IsLoopback || (endpoint.Scheme != "http" && endpoint.Scheme != "https") ||
                !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query))
                throw new InvalidOperationException("Social Player verification requires the dedicated 127.0.0.6 QA service.");
        }

        internal static async Task VerifyAsync(Hooks hooks, CancellationToken cancellation)
        {
            if (!Requested) throw new InvalidOperationException("Explicit Native Development social verification is required.");
            if (hooks == null) throw new ArgumentNullException(nameof(hooks));
            CheckTarget(hooks.Endpoint);
            if (!hooks.Root || hooks.ProfileSession == null || hooks.ChatSession == null || hooks.GetBearerAsync == null ||
                hooks.ShowHomeAsync == null || hooks.OpenProfileAsync == null || hooks.OpenChatAsync == null ||
                hooks.OpenMoreAsync == null || hooks.CloseProfile == null || hooks.CloseChat == null || hooks.CloseNotice == null)
                throw new ArgumentException("Social verification needs all installed navigation and session hooks.");
            if (Application.platform != RuntimePlatform.WindowsPlayer || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                throw new InvalidOperationException("Social verification requires a graphics-enabled Windows Player.");
            string requestedDirectory = hooks.CaptureDirectory ?? Environment.GetEnvironmentVariable("ASTRAL_SOCIAL_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(requestedDirectory)) throw new InvalidOperationException("Social verification capture directory is required.");
            string output = Path.GetFullPath(requestedDirectory);
            if (!output.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Social evidence must stay on D drive.");
            Directory.CreateDirectory(output);
            if (Directory.EnumerateFileSystemEntries(output).Any())
                throw new IOException("Use a new empty social verification directory; evidence is never overwritten.");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(TimeSpan.FromMinutes(4));
            var run = new Run(hooks, output, deadline.Token);
            await run.VerifyAsync();
        }

        private sealed class Run
        {
            private readonly Hooks h;
            private readonly string output;
            private readonly CancellationToken token;
            private readonly List<string> states = new List<string>(), captures = new List<string>();
            private readonly List<string> notices = new List<string>();
            private string primaryId, secondaryId, textId, stickerId, incomingId;
            internal Run(Hooks hooks, string directory, CancellationToken cancellation) { h = hooks; output = directory; token = cancellation; }

            internal async Task VerifyAsync()
            {
                using var authoritative = new PlayerProfileApi(h.Endpoint, h.GetBearerAsync);
                var initial = await authoritative.MeAsync(token);
                Require(initial != null && initial.isSelf && initial.canEdit && !string.IsNullOrEmpty(initial.playerId), "primary authenticated editable profile");
                primaryId = initial.playerId;
                await h.ProfileSession.ReadAsync(null, token);
                Require(h.ProfileSession.Self.playerId == primaryId, "profile session identity");
                await h.ShowHomeAsync();
                var home = await Page("HomeWindow");
                Require(home.Require<GameText>("profile").text == initial.displayName, "home authoritative nickname");
                Require(home.Require<GameText>("profileRank").text.Contains("—") && string.IsNullOrEmpty(home.Require<GameText>("profileExperiencePercent").text), "unknown rank and experience remain unknown");
                Require(!home.Require<GameButton>("displayMode").gameObject.activeInHierarchy, "visible F11 debug control hidden");
                await Capture("01-home");

                // A second real HTTP session allocates an independent QA account, never a fabricated DTO.
                using var guest = new ArenaApi(h.Endpoint, token);
                string guestBearer = await guest.GetSessionTokenAsync("SocialQA" + Guid.NewGuid().ToString("N").Substring(0, 10), token);
                using var otherProfile = new PlayerProfileApi(h.Endpoint, _ => Task.FromResult(guestBearer));
                using var otherChat = new ChannelChatApi(h.Endpoint, _ => Task.FromResult(guestBearer));
                var other = await otherProfile.MeAsync(token); secondaryId = other.playerId;
                Require(primaryId != secondaryId && initial.realmId == other.realmId, "two independent QA players in the same realm");

                Click(home, "profileOpen");
                var profile = await Profile(primaryId);
                Require(profile.Require<GameObject>("selfActions").activeInHierarchy, "self edit actions visible");
                await Capture("02-profile-self");
                string clipboard = GUIUtility.systemCopyBuffer;
                try { Click(profile, "copyId"); Require(GUIUtility.systemCopyBuffer == primaryId, "copy actual player id"); }
                finally { GUIUtility.systemCopyBuffer = clipboard; }

                string nickname = "Qa" + new string(Guid.NewGuid().ToByteArray().Take(8).Select(b => (char)('a' + b % 26)).ToArray());
                Click(profile, "nickname");
                Require(profile.Require<GameObject>("nicknameLayer").activeInHierarchy, "nickname editor opened");
                profile.Require<TMP_InputField>("nicknameInput").text = nickname;
                Click(profile, "nicknameCheck");
                await Until(() => VisibleTexts(profile).Contains(PlayerProfileLabels.Text("nickname_available")), "nickname availability receipt");
                await Capture("03-profile-nickname");
                long version = initial.version;
                Click(profile, "nicknameSave");
                await Until(() => h.ProfileSession.Self?.displayName == nickname && h.ProfileSession.Self.version > version, "nickname UI authority receipt");
                var renamed = await authoritative.MeAsync(token);
                Require(renamed.displayName == nickname && renamed.version > version, "nickname persisted independently");
                Require(home.Require<GameText>("profile").text == nickname, "home nickname refreshed without reopening");

                string greeting = "开发验收 " + Guid.NewGuid().ToString("N").Substring(0, 8);
                Click(profile, "greeting");
                Require(profile.Require<GameObject>("greetingLayer").activeInHierarchy, "greeting editor opened");
                profile.Require<TMP_InputField>("greetingInput").text = greeting;
                await Capture("04-profile-greeting");
                Click(profile, "greetingSave");
                await Until(() => h.ProfileSession.Self?.greeting == greeting && !profile.Require<GameObject>("greetingLayer").activeInHierarchy, "greeting UI receipt");
                Require((await authoritative.MeAsync(token)).greeting == greeting, "greeting persisted independently");
                bool season = h.ProfileSession.Self.visibility.seasonVisible, best = h.ProfileSession.Self.visibility.bestVisible;
                Click(profile, "seasonPrivacy");
                await Until(() => h.ProfileSession.Self.visibility.seasonVisible != season, "season privacy receipt");
                Require((await authoritative.MeAsync(token)).visibility.seasonVisible != season, "season privacy persisted");
                Click(profile, "bestPrivacy");
                await Until(() => h.ProfileSession.Self.visibility.bestVisible != best, "best privacy receipt");
                Require((await authoritative.MeAsync(token)).visibility.bestVisible != best, "best privacy persisted");
                await Capture("05-profile-privacy");
                foreach (string key in new[] { "representative", "support", "frame", "customCard" })
                { Click(profile, key); await Notice("profile." + key, key == "representative" ? "06-profile-unavailable" : null); }
                h.CloseProfile(); await Absent("PlayerProfile");
                await h.OpenProfileAsync(null, token); profile = await Profile(primaryId);
                Require(profile.Require<GameText>("displayName").text == nickname && profile.Require<GameText>("greetingText").text == greeting, "profile reopen retains persisted edits");
                h.CloseProfile(); await Absent("PlayerProfile");
                await h.OpenProfileAsync(secondaryId, token); profile = await Profile(secondaryId);
                Require(!profile.Require<GameObject>("selfActions").activeInHierarchy && !profile.Require<GameButton>("nickname").gameObject.activeInHierarchy && !profile.Require<GameButton>("seasonPrivacy").gameObject.activeInHierarchy, "other profile is read only");
                Require(profile.Require<GameText>("displayName").text == other.displayName, "other profile identity");
                await Capture("07-profile-other"); h.CloseProfile(); await Absent("PlayerProfile");

                foreach (string id in HomeFeatureLabels.Main)
                {
                    var button = home.Require<GameButton>("feature." + id); Require(button.IsInteractable(), "main target " + id);
                    if (HomeFeatureLabels.MainAvailable(id)) continue;
                    Require(Dimmed(button), "unfinished main target dimmed " + id); Click(home, "feature." + id);
                    await Notice(HomeFeatureLabels.Key(id), id == "episode" ? "08-home-feature-notice" : null);
                }
                foreach (string id in HomeFeatureLabels.Rail)
                { Click(home, "feature." + id); await Notice(HomeFeatureLabels.Key(id), null); }
                await h.OpenMoreAsync(token); var more = await Page("HomeMoreMenu"); await Capture("09-more");
                foreach (string id in HomeFeatureLabels.Main.Concat(HomeFeatureLabels.Drawer).Concat(HomeFeatureLabels.Bottom).Distinct())
                {
                    if (id == "arena" || id == "summon" || id == "heroes") continue;
                    var button = more.Require<GameButton>("feature." + id);
                    Require(Dimmed(button) && button.IsInteractable(), "unfinished More target dimmed and clickable " + id);
                    Click(more, "feature." + id); await Notice(HomeFeatureLabels.Key(id), null);
                }
                Click(more, "close"); await Absent("HomeMoreMenu");

                // Enter through the same nested ChatEntry used by ordinary pages.
                var chatEntry = home.GetComponentsInChildren<BindingMap>().FirstOrDefault(m => m.name == "ChatEntry");
                Require(chatEntry, "home ChatEntry installed"); Click(chatEntry, "button");
                var chat = await Page("ChannelChat"); await Until(() => chat.Require<GameButton>("send").IsInteractable(), "chat open");
                Require(h.ChatSession.Channel == 1 && chat.Require<GameText>("channelName").text == ChannelChatLabels.Text("channel").Replace("{0}", "1"), "default channel one");
                await Capture("10-chat-channel-one");
                string marker = "Social QA " + Guid.NewGuid().ToString("N");
                chat.Require<TMP_InputField>("input").text = marker; Click(chat, "send");
                await Until(() => h.ChatSession.Messages.Any(m => m.player.id == primaryId && m.kind == "text" && m.text == marker), "text send UI receipt");
                var sent = h.ChatSession.Messages.Single(m => m.player.id == primaryId && m.kind == "text" && m.text == marker); textId = sent.id;
                await UntilAsync(async () => (await otherChat.ListAsync(1, "0", 0, token)).messages.Any(m => m.id == textId && m.player.id == primaryId && m.text == marker), "second client receives UI text");
                Require(Row(chat, textId).Require<GameText>("body").text == marker, "actual text row rendered");
                Click(chat, "emoji"); Require(chat.Require<GameObject>("stickerLayer").activeInHierarchy, "sticker picker opened");
                var cell = chat.Require<ScrollRect>("stickerScroll").content.GetComponentsInChildren<BindingMap>().FirstOrDefault(m => m.gameObject.activeInHierarchy && m.name != "StickerCell");
                Require(cell, "real sticker catalogue available"); string stickerKey = cell.name;
                await Capture("11-chat-sticker-picker");
                // The real service enforces 500 ms between one player's sends.
                await Task.Delay(550, token); Click(cell, "button");
                await Until(() => h.ChatSession.Messages.Any(m => m.player.id == primaryId && m.kind == "sticker" && m.stickerId == stickerKey), "sticker send UI receipt");
                var sticker = h.ChatSession.Messages.Last(m => m.player.id == primaryId && m.kind == "sticker" && m.stickerId == stickerKey); stickerId = sticker.id;
                await UntilAsync(async () => (await otherChat.ListAsync(1, "0", 0, token)).messages.Any(m => m.id == stickerId && m.kind == "sticker" && m.stickerId == stickerKey), "second client receives UI sticker");
                Require(Row(chat, stickerId).Require<Image>("sticker").sprite, "actual sticker row has source sprite");
                // No explicit refresh: this proves installed live invalidation learns a different HTTP producer.
                var incoming = await otherChat.SendAsync(1, new ChatSendRequest { requestId = Guid.NewGuid().ToString("N"), kind = "text", text = "QA peer " + Guid.NewGuid().ToString("N") }, token);
                incomingId = incoming.id;
                await Until(() => h.ChatSession.Messages.Any(m => m.id == incomingId && m.player.id == secondaryId), "live incoming message from second client");
                await Until(() => Row(chat, incomingId), "incoming UI row");
                await Capture("12-chat-text-and-sticker");
                Click(Row(chat, incomingId), "avatar"); profile = await Profile(secondaryId);
                Require(!profile.Require<GameObject>("selfActions").activeInHierarchy, "chat avatar opens shared read-only profile");
                await Capture("13-chat-avatar-profile"); h.CloseProfile(); await Absent("PlayerProfile");
                Require(Find("ChannelChat"), "closing profile restores chat");
                Click(chat, "guildTab"); await Notice("chat.guild", "14-chat-guild-notice");
                await SocialChatSettingsSmoke.VerifyAsync(chat, h.ChatSession, h.Endpoint, h.GetBearerAsync, Capture, token);
                Click(chat, "close"); await Absent("ChannelChat");
                await h.OpenChatAsync(token); chat = await Page("ChannelChat");
                Require(h.ChatSession.Channel == 1 && h.ChatSession.Messages.Any(m => m.id == incomingId), "chat reopen retains channel history");
                await Capture("15-chat-reopened"); h.CloseChat(); await Absent("ChannelChat");
                await Capture("16-home-after-social");

                var receipt = new Receipt { appVersion = Application.version, buildIdentifier = h.BuildIdentifier, endpoint = h.Endpoint.GetLeftPart(UriPartial.Authority),
                    playerId = primaryId, secondaryPlayerId = secondaryId, width = Screen.width, height = Screen.height,
                    source = "installed Windows Development Player framebuffer", checks = states.ToArray(), notices = notices.ToArray(), captures = captures.ToArray(),
                    sentTextMessageId = textId, sentStickerMessageId = stickerId, incomingMessageId = incomingId,
                    actualVisualReview = "pending independent image review", actualAudioReview = "not performed by this diagnostic" };
                File.WriteAllText(Path.Combine(output, "social-player.json"), JsonConvert.SerializeObject(receipt, Formatting.Indented));
            }

            private async Task<BindingMap> Profile(string id)
            {
                var map = await Page("PlayerProfile");
                await Until(() => map.Require<GameObject>("profileContent").activeInHierarchy && map.Require<GameText>("serverId").text.Contains(id), "profile identity loaded");
                return map;
            }
            private async Task Notice(string key, string capture)
            {
                var map = await Page("FeatureNotice");
                Require(map.Require<GameText>("title").text == SocialLabels.Title(key), "notice title " + key);
                Require(map.Require<GameText>("message").text == SocialLabels.Text("unavailable"), "common unavailable message " + key);
                notices.Add(key); if (capture != null) await Capture(capture);
                Click(map, "dismiss"); await Absent("FeatureNotice");
            }
            private BindingMap Find(string name) => h.Root.GetComponentsInChildren<BindingMap>(true).FirstOrDefault(m => m.gameObject.activeInHierarchy && (m.name == name || m.name == name + "(Clone)"));
            private async Task<BindingMap> Page(string name) { await Until(() => Find(name), "open " + name); return Find(name); }
            private Task Absent(string name) => Until(() => !Find(name), "close " + name);
            private static BindingMap Row(BindingMap chat, string id) => chat.Require<ScrollRect>("messages").content.GetComponentsInChildren<BindingMap>().FirstOrDefault(m => m.name == "Message " + id);
            private static string[] VisibleTexts(BindingMap map) => map.GetComponentsInChildren<GameText>().Select(t => t.text).ToArray();
            private static bool Dimmed(GameButton button) => button.GetComponentsInChildren<GameText>().Any(t => t.color.a < .6f) || button.GetComponentsInChildren<CanvasGroup>().Any(g => g.alpha < .6f) || button.GetComponentsInChildren<Image>().Any(i => i.color.a < .6f);
            private static void Click(BindingMap map, string key)
            {
                if (!map) throw new InvalidOperationException("Missing verification map for " + key);
                var button = map.Require<GameButton>(key);
                if (!button.isActiveAndEnabled || !button.IsInteractable()) throw new InvalidOperationException("Unavailable verification control: " + key);
                button.onClick.Invoke();
            }
            private void Require(bool passed, string description) { if (!passed) throw new InvalidDataException("Social verification failed: " + description); states.Add(description); }
            private async Task Until(Func<bool> predicate, string step)
            {
                double end = Time.realtimeSinceStartupAsDouble + 25;
                while (!predicate()) { token.ThrowIfCancellationRequested(); if (Time.realtimeSinceStartupAsDouble >= end) throw new TimeoutException("Social verification timed out: " + step); await Task.Delay(40, token); }
            }
            private async Task UntilAsync(Func<Task<bool>> predicate, string step)
            {
                double end = Time.realtimeSinceStartupAsDouble + 25;
                while (!await predicate()) { token.ThrowIfCancellationRequested(); if (Time.realtimeSinceStartupAsDouble >= end) throw new TimeoutException("Social verification timed out: " + step); await Task.Delay(100, token); }
            }
            private async Task Capture(string name)
            {
                string path = Path.Combine(output, name + ".png"); if (File.Exists(path)) throw new IOException("Capture already exists: " + name);
                await Task.Delay(260, token); ScreenCapture.CaptureScreenshot(path);
                await Until(() => File.Exists(path) && new FileInfo(path).Length > 1024, "framebuffer capture " + name);
                captures.Add(name + ".png"); await Task.Delay(80, token);
            }
        }

        [Serializable]
        private sealed class Receipt
        {
            public string appVersion, buildIdentifier, endpoint, playerId, secondaryPlayerId, source;
            public int width, height;
            public string[] checks, notices, captures;
            public string sentTextMessageId, sentStickerMessageId, incomingMessageId, actualVisualReview, actualAudioReview;
        }
    }
}
