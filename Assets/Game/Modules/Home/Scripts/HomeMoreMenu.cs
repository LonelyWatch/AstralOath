using AstralOath.Client.Common;

namespace AstralOath.Client.Home
{
    public sealed class HomeMoreMenu : UIBaseView
    {
        public const string Address = "home/more-menu";
        public override string PrefabPath => Address;
        internal GameButton Heroes => Bindings.Require<GameButton>("heroes");
        internal GameText HeroesLabel => Bindings.Require<GameText>("heroesLabel");
        internal GameButton Close => Bindings.Require<GameButton>("close");
        internal GameButton Outside => Bindings.Require<GameButton>("outside");

        public override void Validate()
        {
            _ = Heroes; _ = HeroesLabel; _ = Close; _ = Outside;
        }
    }
}
