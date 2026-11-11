using System;

namespace AstralOath.Client.Common
{
    public interface ICharacterAudioService
    {
        bool IsAvailable { get; }
        string Diagnostic { get; }
        ICharacterAudioPlayback PlayEvent(string eventPath);
        ICharacterAudioPlayback PlayVoice(string audioTableKey);
    }

    public interface ICharacterAudioPlayback : IDisposable
    {
        void Pause(bool paused);
        void Stop();
    }
}
