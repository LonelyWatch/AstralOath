using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AstralOath.Client.Core
{
    public interface IAssetService
    {
        Task<T> LoadAsync<T>(string address, BindingScope scope) where T : UnityEngine.Object;
    }

    public interface IAudioService
    {
        Task PlayVoiceAsync(string address, BindingScope scope);
    }

    public interface ILocaleService
    {
        string TextLocale { get; }
        string VoiceLocale { get; }
        event Action TextChanged;
        void SelectText(string locale);
        void SelectVoice(string locale);
        string Resolve(string key);
    }

    public interface IEchoService
    {
        Task<EchoResult> EchoAsync(string message, CancellationToken cancellation);
    }

    public sealed class EchoResult
    {
        public EchoResult(string message, ulong sequence)
        {
            Message = message;
            Sequence = sequence;
        }

        public string Message { get; }
        public ulong Sequence { get; }
    }

    public interface IConfigService
    {
        Task<T> ReadAsync<T>(string address, BindingScope scope) where T : class;
    }

    public interface IAnimationService
    {
        void Pulse(Transform target, float seconds, BindingScope scope);
    }
}