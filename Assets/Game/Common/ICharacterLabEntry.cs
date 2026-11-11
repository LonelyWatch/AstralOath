using System;
using System.Threading.Tasks;
using AstralOath.Client.Core;
using AstralOath.Client.Skeletons;
using UnityEngine;

namespace AstralOath.Client.Common
{
    public interface ICharacterLabEntry : IDisposable
    {
        Task StartAsync(CharacterLabLaunchContext context);
        void Tick(float deltaSeconds);
    }

    public sealed class CharacterLabLaunchContext
    {
        public ServiceRegistry Services { get; }
        public BindingScope Lifetime { get; }
        public Transform ViewRoot { get; }
        public Transform WorldRoot { get; }
        public Camera PreviewCamera { get; }
        public ISkeletonPlayerFactory Skeletons { get; }
        public ICharacterAudioService Audio { get; }

        public CharacterLabLaunchContext(ServiceRegistry services, BindingScope lifetime, Transform viewRoot,
            Transform worldRoot, Camera previewCamera, ISkeletonPlayerFactory skeletons, ICharacterAudioService audio)
        {
            Services = services ?? throw new ArgumentNullException(nameof(services));
            Lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
            ViewRoot = viewRoot ? viewRoot : throw new ArgumentNullException(nameof(viewRoot));
            WorldRoot = worldRoot ? worldRoot : throw new ArgumentNullException(nameof(worldRoot));
            PreviewCamera = previewCamera ? previewCamera : throw new ArgumentNullException(nameof(previewCamera));
            Skeletons = skeletons ?? throw new ArgumentNullException(nameof(skeletons));
            Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        }
    }
}
