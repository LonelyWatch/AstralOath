using AstralOath.Client.Common;
using UnityEngine;

namespace AstralOath.Client.FoundationDemo
{
    [DisallowMultipleComponent, RequireComponent(typeof(BindingMap))]
    public sealed class FoundationView : MonoBehaviour
    {
        private BindingMap Bindings => GetComponent<BindingMap>();
        public GameText Title => Bindings.Require<GameText>("title");
        public GameText Description => Bindings.Require<GameText>("description");
        public GameText Status => Bindings.Require<GameText>("status");
        public GameButton Echo => Bindings.Require<GameButton>("echo");
        public GameText EchoLabel => Bindings.Require<GameText>("echoLabel");
        public GameButton Close => Bindings.Require<GameButton>("close");
        public GameText CloseLabel => Bindings.Require<GameText>("closeLabel");

        public void Validate()
        {
            _ = Title;
            _ = Description;
            _ = Status;
            _ = Echo;
            _ = EchoLabel;
            _ = Close;
            _ = CloseLabel;
        }

        public void Show() => gameObject.SetActive(true);
        public void Hide() => gameObject.SetActive(false);
    }
}