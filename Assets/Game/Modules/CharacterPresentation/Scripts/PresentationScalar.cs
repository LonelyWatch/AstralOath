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
    }
}
