using AstralOath.Client.Common;

namespace AstralOath.Client.FoundationDemo
{
    public sealed class FoundationView : UIView
    {
        public const string PrefabAddress = "foundation/foundation-window";

        public override string PrefabPath => PrefabAddress;
        public GameText Title => Bindings.Require<GameText>("title");
        public GameText Description => Bindings.Require<GameText>("description");
        public GameText Status => Bindings.Require<GameText>("status");
        public GameButton Echo => Bindings.Require<GameButton>("echo");
        public GameText EchoLabel => Bindings.Require<GameText>("echoLabel");
        public GameButton Close => Bindings.Require<GameButton>("close");
        public GameText CloseLabel => Bindings.Require<GameText>("closeLabel");

        public override void Validate()
        {
            _ = Title;
            _ = Description;
            _ = Status;
            _ = Echo;
            _ = EchoLabel;
            _ = Close;
            _ = CloseLabel;
        }

    }
}
