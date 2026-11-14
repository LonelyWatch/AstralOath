using AstralOath.Client.Core;
using UnityEngine;

namespace AstralOath.Client.Common
{
    public sealed class GameLaunchContext
    {
        public GameLaunchContext(ServiceRegistry services, BindingScope lifetime, Transform viewRoot,
            GameButton openButton, GameText openLabel, GameText startupStatus, string textLocale, string voiceLocale,
            bool recovered, bool showHome = false)
        {
            Services = services;
            Lifetime = lifetime;
            ViewRoot = viewRoot;
            OpenButton = openButton;
            OpenLabel = openLabel;
            StartupStatus = startupStatus;
            TextLocale = textLocale;
            VoiceLocale = voiceLocale;
            Recovered = recovered;
            ShowHome = showHome;
        }

        public ServiceRegistry Services { get; }
        public BindingScope Lifetime { get; }
        public Transform ViewRoot { get; }
        public GameButton OpenButton { get; }
        public GameText OpenLabel { get; }
        public GameText StartupStatus { get; }
        public string TextLocale { get; }
        public string VoiceLocale { get; }
        public bool Recovered { get; }
        public bool ShowHome { get; }
    }
}
