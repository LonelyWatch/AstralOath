using System;
namespace AstralOath.Client.CharacterPresentation
{
    // All visual tracks consume the same delta; sound is only valid from a fresh 1x start.
    public sealed class PresentationClock
    {
        public double Time { get; private set; }
        public double Speed { get; private set; } = 1;
        public bool AudioAllowed { get; private set; } = true;
        public bool IsPlaying { get; private set; }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public double Advance(double delta)
        {
            RequireFiniteNonnegative(delta, nameof(delta));
            double step = IsPlaying ? delta * Speed : 0;
            Time += step;
            return step;
        }
        public void SetSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < .1 || speed > 4)
                throw new ArgumentOutOfRangeException(nameof(speed));
            if (speed != Speed) AudioAllowed = false;
            Speed = speed;
        }
        public void ResetWithAudio() { Time = 0; Speed = 1; AudioAllowed = true; IsPlaying = false; }
        public void Seek(double time)
        {
            RequireFiniteNonnegative(time, nameof(time));
            Time = time; AudioAllowed = false; IsPlaying = false;
        }
        public void Step(double delta) { RequireFiniteNonnegative(delta, nameof(delta)); Seek(Time + delta); }
        private static void RequireFiniteNonnegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }
    }
}
