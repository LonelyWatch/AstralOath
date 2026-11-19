using System;
using System.Threading;
using System.Threading.Tasks;
using AstralOath.Client.Common;
using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    internal static class SummonPlayerSmoke
    {
        internal static bool IsRequested()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return false;
            var arguments = Environment.GetCommandLineArgs();
            return Array.IndexOf(arguments, "-astral-startup-smoke") >= 0 &&
                Array.IndexOf(arguments, "-astral-summon-smoke") >= 0;
        }

        internal static async Task RunAsync(GameLaunchContext context, StartupNavigation navigation, CancellationToken lifetime)
        {
            if (!context.Services.Get<GameServerEndpoint>().Address.IsLoopback)
                throw new InvalidOperationException("The Summon Player smoke requires a loopback server.");

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
            deadline.CancelAfter(TimeSpan.FromSeconds(45));
            await navigation.VerifySummonAsync(deadline.Token);
            Debug.Log("ASTRAL_SUMMON_PLAYER_OK");
        }
    }
}
