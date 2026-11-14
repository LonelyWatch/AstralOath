using System;
using UnityEngine;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    public sealed class DesktopDisplayMode : MonoBehaviour
    {
        private int windowWidth = 1280;
        private int windowHeight = 720;
        private bool fullScreen;

        public bool Available => Application.platform == RuntimePlatform.WindowsPlayer;
        public bool FullScreen => Screen.fullScreenMode != FullScreenMode.Windowed;
        public event Action Changed;

        private void Start() => fullScreen = FullScreen;

        private void Update()
        {
            if (!Available) return;
            bool observed = FullScreen;
            if (observed != fullScreen)
            {
                fullScreen = observed;
                Changed?.Invoke();
            }
            if (!observed)
            {
                windowWidth = Screen.width;
                windowHeight = Screen.height;
            }
            if (Input.GetKeyDown(KeyCode.F11)) Toggle();
        }

        public void Toggle()
        {
            if (!Available) return;
            var monitor = Screen.currentResolution;
            if (FullScreen)
            {
                int width = Mathf.Clamp(windowWidth, 1, monitor.width);
                int height = Mathf.Clamp(windowHeight, 1, monitor.height);
                Screen.SetResolution(width, height, FullScreenMode.Windowed);
            }
            else Screen.SetResolution(monitor.width, monitor.height, FullScreenMode.FullScreenWindow);
        }
    }
}
