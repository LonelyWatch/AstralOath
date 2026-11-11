using System;
namespace AstralOath.Client.CharacterPresentation
{
    // Uniform constraints follow system/program/sprite_blur.frag. The original shader is undefined for zero sample count.
    public sealed class SpriteBlurSettings
    {
        public float Range{get;} public float Samples{get;} public float Ratio{get;}
        public float DirectionX{get;} public float DirectionY{get;} public float Duration{get;}
        public SpriteBlurSettings(float range,float samples,float ratio,float directionX,float directionY,float durationSeconds)
        {
            Finite(range,nameof(range));Finite(samples,nameof(samples));Finite(ratio,nameof(ratio));
            Finite(directionX,nameof(directionX));Finite(directionY,nameof(directionY));Finite(durationSeconds,nameof(durationSeconds));
            if(samples<=.5f)throw new ArgumentOutOfRangeException(nameof(samples),"The source weighted kernel requires a positive nonzero-weight sample.");
            if(durationSeconds<=0)throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            Range=range;Samples=Math.Min(5,samples);Ratio=Math.Max(0,Math.Min(1,ratio));
            DirectionX=directionX;DirectionY=directionY;Duration=durationSeconds;
        }
        private static void Finite(float value,string name)
        {if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(name);}
    }
}
