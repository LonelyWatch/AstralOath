using System;
namespace AstralOath.Client.CharacterPresentation
{
    // Lab envelope: duration is the total visible interval. Original duration/fade overlap semantics remain diagnostic.
    public sealed class PresentationFade
    {
        private readonly double opacity,duration,fadeIn,fadeOut;
        public PresentationFade(double opacity,double duration,double fadeIn,double fadeOut)
        {
            if(double.IsNaN(opacity)||double.IsInfinity(opacity)||opacity<0||opacity>1)throw new ArgumentOutOfRangeException(nameof(opacity));
            if(double.IsNaN(duration)||double.IsInfinity(duration)||(duration<0&&duration!=-1))throw new ArgumentOutOfRangeException(nameof(duration));
            if(double.IsNaN(fadeIn)||double.IsInfinity(fadeIn)||fadeIn<0||double.IsNaN(fadeOut)||double.IsInfinity(fadeOut)||fadeOut<0)throw new ArgumentOutOfRangeException(nameof(fadeIn));
            this.opacity=opacity;this.duration=duration;this.fadeIn=fadeIn;this.fadeOut=fadeOut;
        }
        public double Evaluate(double time)
        {
            if(double.IsNaN(time)||double.IsInfinity(time)||time<0)throw new ArgumentOutOfRangeException(nameof(time));
            if(duration>=0&&time>=duration)return 0;
            double entering=fadeIn==0?1:Math.Min(1,time/fadeIn);
            double exiting=duration<0||fadeOut==0?1:Math.Min(1,(duration-time)/fadeOut);
            return opacity*Math.Min(entering,exiting);
        }
    }
}
