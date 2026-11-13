using UnityEngine;

namespace AstralOath.Client.Common
{
    [DisallowMultipleComponent]
    internal sealed class GameUiAudio : MonoBehaviour
    {
        private AudioSource output;

        internal void Play(AudioClip clip, float volume)
        {
            if (!output)
            {
                output = gameObject.AddComponent<AudioSource>();
                output.playOnAwake = false;
                output.spatialBlend = 0f;
                output.ignoreListenerPause = true;
            }
            output.PlayOneShot(clip, volume);
        }

        private void OnDisable() { if (output) output.Stop(); }
        private void OnDestroy() { if (output) Destroy(output); }
    }
}
