using AstralOath.Client.Common;

namespace AstralOath.Client.Home
{
    public sealed class HomeWindow : UIBaseView
    {
        public const string Address = "home/main";
        public override string PrefabPath => Address;
        public GameButton Arena => Bindings.Require<GameButton>("arena");
        public GameText ArenaLabel => Bindings.Require<GameText>("arenaLabel");
        public GameText Profile => Bindings.Require<GameText>("profile");
        public override void Validate() { _ = Arena; _ = ArenaLabel; _ = Profile; }
    }
}
