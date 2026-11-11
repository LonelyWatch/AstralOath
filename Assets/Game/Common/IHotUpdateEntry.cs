using System;
using System.Threading.Tasks;
using AstralOath.Client.Core;

namespace AstralOath.Client.Common
{
    public interface IHotUpdateEntry : IDisposable
    {
        Task StartAsync(GameLaunchContext context);
        Task<EchoResult> EchoAsync();
    }
}
