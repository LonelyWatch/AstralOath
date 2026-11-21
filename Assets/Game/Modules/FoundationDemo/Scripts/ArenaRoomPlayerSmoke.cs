using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.FoundationDemo
{
    // Drives the packaged prefab buttons in two independent Windows guest sessions.
    internal static class ArenaRoomPlayerSmoke
    {
        internal static bool IsRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            return Debug.isDebugBuild && Array.IndexOf(args, "-astral-startup-smoke") >= 0 &&
                Array.IndexOf(args, "-astral-arena-room-smoke") >= 0;
        }

        internal static async Task RunAsync(GameLaunchContext context, CancellationToken token)
        {
            if (!context.Services.Get<GameServerEndpoint>().Address.IsLoopback)
                throw new InvalidOperationException("The room UI smoke requires a loopback server.");
            string role = Environment.GetEnvironmentVariable("ASTRAL_ROOM_SMOKE_ROLE");
            string run = Environment.GetEnvironmentVariable("ASTRAL_ROOM_SMOKE_ID");
            if ((role != "host" && role != "guest") || !Guid.TryParseExact(run, "N", out _))
                throw new InvalidOperationException("The room UI smoke requires a role and a unique run ID.");
            string roomName = "smoke-" + run.Substring(0, 16);
            BindingMap Page(string name)
            {
                foreach (var map in context.ViewRoot.GetComponentsInChildren<BindingMap>())
                    if (map.name == name || map.name == name + "(Clone)") return map;
                return null;
            }

            Page("HomeWindow").Require<GameButton>("arena").onClick.Invoke();
            await Wait(() => Page("ArenaDirectory")?.Require<GameButton>("create").interactable == true,
                20, "open the room directory", token);
            var directory = Page("ArenaDirectory");
            if (role == "host")
            {
                directory.Require<GameButton>("create").onClick.Invoke();
                var create = Page("ArenaCreateRoom");
                create.Require<TMP_InputField>("name").text = roomName;
                create.Require<GameButton>("confirm").onClick.Invoke();
            }
            else
            {
                var content = directory.Require<ScrollRect>("list").content;
                GameButton join = null;
                foreach (Transform child in content)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    var row = child.GetComponent<BindingMap>();
                    if (row && row.Require<GameText>("name").text == roomName)
                        join = row.Require<GameButton>("join");
                }
                if (!join) throw new InvalidOperationException("Test room was absent from the visible directory.");
                join.onClick.Invoke();
            }
            await Wait(() => Page("ArenaRoom")?.Require<GameText>("name").text == roomName,
                15, "enter the test room", token);
            var room = Page("ArenaRoom");
            Debug.Log("ASTRAL_ROOM_ENTERED " + role);
            var count = room.Require<GameText>("count");
            await Wait(() => count.text == "2/50", role == "host" ? 100 : 10, "render both members", token);
            var history = room.Require<ScrollRect>("messages").content;
            int Copies(string text)
            {
                int found = 0;
                foreach (var label in history.GetComponentsInChildren<GameText>())
                    if (label.text == text) ++found;
                return found;
            }
            void Send(string text)
            {
                room.Require<TMP_InputField>("message").text = text;
                room.Require<GameButton>("send").onClick.Invoke();
            }

            string guestMessage = "guest-" + run;
            string hostMessage = "host-" + run;
            if (role == "guest") Send(guestMessage);
            await Wait(() => Copies(guestMessage) == 1, 10, "display the guest message", token);
            if (role == "host") Send(hostMessage);
            await Wait(() => Copies(hostMessage) == 1, 10, "display the host message", token);
            if (role == "host") await Wait(() => count.text == "1/50", 10, "display the guest leaving", token);
            if (Copies(guestMessage) != 1 || Copies(hostMessage) != 1)
                throw new InvalidOperationException("Chat history contains a duplicated or missing test message.");
            room.Require<GameButton>("leave").onClick.Invoke();
            await Wait(() => Page("ArenaDirectory") != null, 10, "leave the room", token);
            Debug.Log("ASTRAL_ROOM_EVENTS_OK " + role);
        }

        private static async Task Wait(Func<bool> predicate, int seconds, string step, CancellationToken token)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!predicate())
            {
                if (Time.realtimeSinceStartupAsDouble >= deadline)
                    throw new TimeoutException("Room UI smoke timed out waiting to " + step + ".");
                await Task.Delay(40, token);
            }
        }
    }
}
