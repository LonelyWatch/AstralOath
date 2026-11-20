using System.Globalization;
namespace AstralOath.Client.CharacterPresentation
{
    public static class PresentationScalar
    {
        public static bool TryRead(string input,out float value)
        {
            bool parsed=float.TryParse(input,NumberStyles.Float,CultureInfo.InvariantCulture,out value);
            return parsed&&!float.IsNaN(value)&&!float.IsInfinity(value);
        }

        public static bool TryReadScaleAxes(string input, out float x, out float y)
        {
            x = y = 0f;
            if (string.IsNullOrEmpty(input) || input.IndexOf(',') < 0) return false;
            string[] axes = input.Split(',');
            return axes.Length == 2 && TryRead(axes[0], out x) && TryRead(axes[1], out y) && x != 0f && y != 0f;
        }
    }
}
