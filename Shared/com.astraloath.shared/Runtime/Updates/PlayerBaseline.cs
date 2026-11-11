using System;
using System.Runtime.Serialization;

namespace AstralOath.Shared.Updates
{
    [Serializable, DataContract]
    public sealed class PlayerBaseline
    {
        public const string ResourceName = "AstralOathPlayerBaseline";

        [DataMember(IsRequired = true)] public int formatVersion = 1;
        [DataMember(IsRequired = true)] public string baselineId;
        [DataMember(IsRequired = true)] public string platform;
        [DataMember(IsRequired = true)] public string architecture;
        [DataMember(IsRequired = true)] public string runtimeVersion;
        [DataMember(IsRequired = true)] public string appVersion;
    }
}
