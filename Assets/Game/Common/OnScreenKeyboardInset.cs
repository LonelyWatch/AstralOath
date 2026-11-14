using TMPro;
using UnityEngine;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class OnScreenKeyboardInset : MonoBehaviour
    {
        [SerializeField] private TMP_InputField input;
        private RectTransform rect;
        private Canvas canvas;
        private float inset;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject decor, visibleFrame;
        private int sdk, imeType;
        private float nextMeasure, androidTop;
#endif

        private void Awake()
        {
            rect = (RectTransform)transform;
            var owner = GetComponentInParent<Canvas>();
            if (owner) canvas = owner.rootCanvas;
        }

        private void LateUpdate()
        {
            if (!Application.isMobilePlatform || !input || !input.isFocused || !canvas || !TouchScreenKeyboard.visible)
            {
                Apply(0);
#if UNITY_ANDROID && !UNITY_EDITOR
                nextMeasure = androidTop = 0;
#endif
                return;
            }
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector3 bottom = rect.TransformPoint(new Vector3(rect.rect.center.x, rect.rect.yMin, 0));
            bottom -= rect.parent.TransformVector(Vector3.up * inset);
            float baseY = RectTransformUtility.WorldToScreenPoint(camera, bottom).y;
            Apply(Mathf.Max(0, KeyboardTop() - baseY) / canvas.scaleFactor);
        }

        private float KeyboardTop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Time.unscaledTime < nextMeasure) return androidTop;
            nextMeasure = Time.unscaledTime + .05f;
            if (decor == null)
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var window = activity.Call<AndroidJavaObject>("getWindow");
                decor = window.Call<AndroidJavaObject>("getDecorView");
                using var version = new AndroidJavaClass("android.os.Build$VERSION");
                sdk = version.GetStatic<int>("SDK_INT");
                if (sdk >= 30)
                {
                    using var insetType = new AndroidJavaClass("android.view.WindowInsets$Type");
                    imeType = insetType.CallStatic<int>("ime");
                }
                else visibleFrame = new AndroidJavaObject("android.graphics.Rect");
            }
            int windowHeight = decor.Call<int>("getHeight");
            int height;
            if (sdk >= 30)
            {
                using var insets = decor.Call<AndroidJavaObject>("getRootWindowInsets");
                if (insets == null || !insets.Call<bool>("isVisible", imeType)) return androidTop = 0;
                using var keyboard = insets.Call<AndroidJavaObject>("getInsets", imeType);
                height = keyboard.Get<int>("bottom");
            }
            else
            {
                decor.Call("getWindowVisibleDisplayFrame", visibleFrame);
                height = windowHeight - visibleFrame.Get<int>("bottom");
            }
            return androidTop = windowHeight > 0 ? Mathf.Clamp01((float)height / windowHeight) * Screen.height : 0;
#else
            var area = TouchScreenKeyboard.area;
            return area.height > 0 ? area.yMax : 0;
#endif
        }

        private void OnDisable() => Apply(0);

#if UNITY_ANDROID && !UNITY_EDITOR
        private void OnDestroy()
        {
            decor?.Dispose();
            visibleFrame?.Dispose();
        }
#endif

        private void Apply(float target)
        {
            if (!rect || Mathf.Approximately(target, inset)) return;
            rect.anchoredPosition += Vector2.up * (target - inset);
            inset = target;
        }
    }
}
