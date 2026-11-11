using System;
using System.Security.Cryptography;
using System.Text;
using YooAsset.Editor;

namespace AstralOath.Editor
{
    public sealed class CharacterLabPackRule : IPackRule
    {
        public PackRuleResult GetPackRuleResult(PackRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/').ToLowerInvariant();
            // Preserve directory batching, while banks remain independently loadable files.
            // Hash the group, not individual non-bank assets or collector metadata.
            string group = path.EndsWith(".bank.bytes", StringComparison.Ordinal)
                ? "file:" + path : "directory:" + path.Substring(0, path.LastIndexOf('/'));
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(group)))
                .Replace("-", "").ToLowerInvariant().Substring(0, 32);
            // 43 characters including extension; 52 with Unity's .manifest sidecar.
            return new PackRuleResult("lab_" + hash, "bundle");
        }
    }
}
