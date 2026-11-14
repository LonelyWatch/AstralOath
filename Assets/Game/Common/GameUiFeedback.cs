using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
    public sealed class GameUiFeedback : MonoBehaviour
    {
        public enum Sound { Normal, Confirm, Cancel, Back, Silent, Blocked }

        private const int PoolSize = 10;
        private const float SourceSpeed = 1.3f;
        private const float SourceLifetime = .5333f;
        private const float SourceOpacity = .30f;
        private const int PulseFrameRate = 60, PulseFrameCount = 32, PulseColumns = 8, PulseRows = 4;
        private const float PulseFrameSize = 135;
        private const string OverlayName = "Click feedback";
        [SerializeField] private Sprite ring, glow, spark;
        [SerializeField] private AudioClip normal, confirm, cancel, back, blocked;
        [SerializeField] private Texture2D pulseFrames;
        [SerializeField] private Material pulseBlend;
        private readonly Pulse[] pulses = new Pulse[PoolSize];
        private readonly Dictionary<int, Press> beginnings = new Dictionary<int, Press>();
        private readonly List<RaycastResult> hits = new List<RaycastResult>();
        private Canvas canvas;
        private RectTransform overlay;
        private PointerEventData pointer;
        private EventSystem eventSystem;
        private int nextPulse;
        private float sparkAngle;

        public void Configure(Sprite ring, Sprite glow, AudioClip normal, AudioClip confirm, AudioClip cancel, AudioClip back = null,
            Sprite spark = null, Texture2D pulseFrames = null, Material pulseBlend = null, AudioClip blocked = null)
        {
            this.ring = ring;
            this.glow = glow;
            this.normal = normal;
            this.confirm = confirm;
            this.cancel = cancel;
            this.back = back;
            this.spark = spark;
            this.pulseFrames = pulseFrames;
            this.pulseBlend = pulseBlend;
            this.blocked = blocked;
        }

        public static void Play(Component source, Sound sound = Sound.Normal) => Play(source, sound, null);

        internal static void Play(Component source, Sound sound, AudioSource custom)
        {
            if (!source || sound == Sound.Silent) return;
            var nearest = source.GetComponentInParent<Canvas>();
            var root = nearest ? nearest.rootCanvas : null;
            var feedback = root ? root.GetComponent<GameUiFeedback>() : null;
            var clip = sound == Sound.Normal && custom && custom.clip ? custom.clip : feedback?.Resolve(sound);
            if (!clip && custom && sound != Sound.Blocked) clip = custom.clip;
            if (!clip) return;
            if (!root)
            {
                if (custom) custom.PlayOneShot(clip);
                return;
            }
            if (!root.TryGetComponent<GameUiAudio>(out var output)) output = root.gameObject.AddComponent<GameUiAudio>();
            output.Play(clip, custom ? custom.volume : 1);
        }

        private AudioClip Resolve(Sound sound)
        {
            return sound switch
            {
                Sound.Blocked => blocked,
                Sound.Cancel or Sound.Back => cancel ? cancel : back,
                Sound.Confirm => normal ? normal : confirm,
                _ => normal
            };
        }

        private void Awake() => canvas = GetComponent<Canvas>();

        private void LateUpdate()
        {
            if (Input.touchCount != 0)
            {
                beginnings.Remove(-1);
                for (int i = 0; i < Input.touchCount; ++i)
                {
                    var touch = Input.GetTouch(i);
                    Track(touch.fingerId, touch.position, touch.phase == TouchPhase.Began,
                        touch.phase == TouchPhase.Ended, touch.phase == TouchPhase.Canceled);
                }
            }
            else if (Input.GetMouseButtonDown(0) || Input.GetMouseButton(0) || Input.GetMouseButtonUp(0))
                Track(-1, Input.mousePosition, Input.GetMouseButtonDown(0), Input.GetMouseButtonUp(0), false);

            for (int i = 0; i < pulses.Length; ++i)
                if (pulses[i] != null && pulses[i].Root.gameObject.activeSelf) Render(pulses[i], Time.unscaledTime);
        }

        // Drag-cancel is permanent for this pointer, including when it returns to the down position.
        private void Track(int id, Vector2 position, bool down, bool up, bool cancel)
        {
            if (down) beginnings[id] = new Press(position, FindPlainControl(position));
            if (!beginnings.TryGetValue(id, out var start)) return;
            float threshold = 12 * Mathf.Max(.75f, canvas.scaleFactor);
            if (cancel || (position - start.Position).sqrMagnitude > threshold * threshold)
            {
                beginnings.Remove(id);
                return;
            }
            if (!up) return;
            beginnings.Remove(id);
            ShowAt(position);
            if (start.Control && start.Control == FindPlainControl(position)) Play(start.Control);
        }

        private Selectable FindPlainControl(Vector2 position)
        {
            var system = EventSystem.current;
            if (!system) return null;
            if (system != eventSystem)
            {
                eventSystem = system;
                pointer = new PointerEventData(system);
            }
            pointer.Reset();
            pointer.position = position;
            system.RaycastAll(pointer, hits);
            if (hits.Count == 0) return null;
            var hit = hits[0].gameObject;
            hits.Clear();
            var control = hit.GetComponentInParent<Selectable>();
            if (!control || control is GameButton || !control.isActiveAndEnabled || !control.IsInteractable()) return null;
            var nearest = control.GetComponentInParent<Canvas>();
            if (!nearest || nearest.rootCanvas != canvas) return null;
            return control is Toggle || control is TMP_InputField || control is InputField || control is Dropdown ||
                control is TMP_Dropdown || control is Button ? control : null;
        }

        private void ShowAt(Vector2 screenPosition)
        {
            if (!canvas || ((!pulseFrames || !pulseBlend) && (!ring || !glow))) return;
            var root = (RectTransform)transform;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (canvas.renderMode == RenderMode.WorldSpace &&
                !RectTransformUtility.RectangleContainsScreenPoint(root, screenPosition, camera)) return;
            if (!overlay)
            {
                overlay = Child(root, OverlayName);
                overlay.anchorMin = Vector2.zero;
                overlay.anchorMax = Vector2.one;
                overlay.offsetMin = overlay.offsetMax = Vector2.zero;
                var layer = overlay.gameObject.AddComponent<Canvas>();
                layer.overrideSorting = true;
                layer.sortingLayerID = canvas.sortingLayerID;
                layer.sortingOrder = Mathf.Min(short.MaxValue, canvas.sortingOrder + 100);
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenPosition, camera, out var local)) return;
            int index = nextPulse;
            nextPulse = (nextPulse + 1) % PoolSize;
            sparkAngle = (sparkAngle + 137.5f) % 360f;
            if (pulses[index] == null) pulses[index] = new Pulse(overlay, ring, glow, spark, pulseFrames, pulseBlend);
            var pulse = pulses[index];
            pulse.Root.localPosition = local;
            pulse.Root.SetAsLastSibling();
            pulse.Root.gameObject.SetActive(true);
            pulse.Start = Time.unscaledTime;
            pulse.Angle = sparkAngle;
            if (pulse.Authored) pulse.Authored.rectTransform.localRotation = Quaternion.Euler(0, 0, pulse.Angle);
            Render(pulse, pulse.Start);
        }

        private static void Render(Pulse pulse, float now)
        {
            float elapsed = now - pulse.Start;
            float age = elapsed * SourceSpeed;
            if (pulse.Authored ? elapsed >= PulseFrameCount / (float)PulseFrameRate : age >= SourceLifetime)
            {
                pulse.Root.gameObject.SetActive(false);
                return;
            }
            float expansion = Mathf.InverseLerp(0, .4333f, age);
            float eased = 1f - Mathf.Pow(1f - expansion, 2.3f);
            float ringFade = SourceOpacity * (1f - Mathf.InverseLerp(.3666f, SourceLifetime, age));
            float glowFade = SourceOpacity * (1f - Mathf.InverseLerp(.10f, SourceLifetime, age));
            if (pulse.Authored)
            {
                int frame = Mathf.Min((int)(elapsed * PulseFrameRate), PulseFrameCount - 1);
                if (frame != pulse.Frame)
                {
                    pulse.Frame = frame;
                    pulse.Authored.uvRect = new Rect((frame % PulseColumns) / (float)PulseColumns,
                        1f - (frame / PulseColumns + 1f) / PulseRows, 1f / PulseColumns, 1f / PulseRows);
                }
            }
            else
            {
                Paint(pulse.Glow, Mathf.Lerp(35f, 142f, eased), new Color(.45f, .46f, 1f, glowFade * .86f));
                Paint(pulse.Ring, Mathf.Lerp(19f, 115f, eased), new Color(.72f, .91f, 1f, ringFade));
            }
            for (int i = 0; i < pulse.Sparks.Length; i++)
            {
                float angle = (pulse.Angle + i * 72f) * Mathf.Deg2Rad;
                var item = pulse.Sparks[i];
                float radius = Mathf.Lerp(8f + i % 3 * 3f, 40f + (i % 3) * 11f, eased);
                item.rectTransform.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                item.rectTransform.localRotation = Quaternion.Euler(0, 0, pulse.Angle + i * 31f + age * 120f);
                Paint(item, Mathf.Lerp(13f + i % 3, 3f, expansion), new Color(.86f, .93f, 1f, glowFade * 1.7f));
            }
        }

        private static void Paint(Image image, float size, Color color)
        {
            image.rectTransform.sizeDelta = new Vector2(size, size);
            image.color = color;
        }

        private static RectTransform Child(RectTransform owner, string name)
        {
            var child = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            child.SetParent(owner, false);
            return child;
        }

        private void OnDisable()
        {
            beginnings.Clear();
            hits.Clear();
            foreach (var pulse in pulses) if (pulse != null && pulse.Root) pulse.Root.gameObject.SetActive(false);
        }

        private void OnApplicationFocus(bool focused) { if (!focused) beginnings.Clear(); }

        private void OnDestroy() { if (overlay) Destroy(overlay.gameObject); }

        private readonly struct Press
        {
            internal readonly Vector2 Position;
            internal readonly Selectable Control;

            internal Press(Vector2 position, Selectable control)
            {
                Position = position;
                Control = control;
            }
        }

        private sealed class Pulse
        {
            internal readonly RectTransform Root;
            internal readonly Image Ring, Glow;
            internal readonly RawImage Authored;
            internal int Frame = -1;
            internal readonly Image[] Sparks;
            internal float Start, Angle;

            internal Pulse(RectTransform owner, Sprite ring, Sprite glow, Sprite spark, Texture2D atlas, Material blend)
            {
                Root = Child(owner, "Click pulse");
                Root.sizeDelta = Vector2.zero;
                if (atlas && blend)
                {
                    var quad = Child(Root, "Native pulse");
                    quad.sizeDelta = Vector2.one * PulseFrameSize;
                    Authored = quad.gameObject.AddComponent<RawImage>();
                    Authored.texture = atlas;
                    Authored.material = blend;
                    Authored.raycastTarget = false;
                }
                else
                {
                    Glow = MakeImage(Root, "Glow", glow);
                    Ring = MakeImage(Root, "Ring", ring);
                }
                Sparks = new Image[spark && !Authored ? 5 : 0];
                for (int i = 0; i < Sparks.Length; ++i) Sparks[i] = MakeImage(Root, "Spark", spark);
            }

            private static Image MakeImage(RectTransform owner, string name, Sprite art)
            {
                var image = Child(owner, name).gameObject.AddComponent<Image>();
                image.sprite = art;
                image.raycastTarget = false;
                return image;
            }
        }
    }
}
