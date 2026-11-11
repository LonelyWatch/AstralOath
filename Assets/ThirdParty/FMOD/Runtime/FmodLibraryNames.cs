// Project bridge to the combined Core/Studio libraries shipped by FMOD for Unity 2.03.15.
namespace FMOD
{
    public partial class VERSION
    {
#if UNITY_2021_3_OR_NEWER
#if UNITY_EDITOR
        public const string dll = "fmodstudioL";
#else
        public const string dll = "fmodstudio" + suffix;
#endif
#endif
    }
}
namespace FMOD.Studio
{
    public partial class STUDIO_VERSION
    {
#if UNITY_2021_3_OR_NEWER
        public const string dll = FMOD.VERSION.dll;
#endif
    }
}
