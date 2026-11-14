using System.Collections.Generic;
using AstralOath.Client.Skeletons;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class UISkeletonPortrait : MonoBehaviour
    {
        [SerializeField] private RawImage output;
        [SerializeField] private TextAsset skeleton;
        [SerializeField] private TextAsset atlas;
        [SerializeField] private Texture2D page;
        [SerializeField] private string pageName, skin = "normal", animationName = "idle";
        [SerializeField] private Vector2 center;
        [SerializeField] private float halfHeight = 4.5f;

        private ISkeletonPlayer player;
        private GameObject stage;
        private RenderTexture target;

        private void OnEnable()
        {
            if (!output || !skeleton || !atlas || !page) return;
            stage = new GameObject("UI portrait stage") { hideFlags = HideFlags.DontSave };
            stage.transform.SetPositionAndRotation(new Vector3(0, 10000, 0), Quaternion.identity);
            var camera = new GameObject("UI portrait camera").AddComponent<Camera>();
            camera.transform.SetParent(stage.transform, false);
            camera.transform.localPosition = new Vector3(center.x, center.y, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.orthographic = true;
            camera.orthographicSize = halfHeight;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 20;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.depth = -100;
            target = new RenderTexture(1024, 1024, 16, RenderTextureFormat.ARGB32) { name = "UI portrait" };
            camera.targetTexture = target;
            output.texture = target;
            try
            {
                var source = new SkeletonPlayerSource(skeleton.text, atlas.text,
                    new Dictionary<string, Texture2D> { [pageName] = page }, premultipliedAlpha: true);
                player = new SkeletonPlayerFactory().Create(source, stage.transform);
                player.SetSkin(skin);
                player.Play(animationName, true);
                player.Tick(0);
            }
            catch
            {
                Release();
                throw;
            }
        }

        private void Update() => player?.Tick(Time.unscaledDeltaTime);
        private void OnDisable() => Release();

        private void Release()
        {
            player?.Dispose();
            player = null;
            if (output) output.texture = null;
            if (stage) { stage.SetActive(false); Destroy(stage); stage = null; }
            if (target) { target.Release(); Destroy(target); target = null; }
        }
    }
}
