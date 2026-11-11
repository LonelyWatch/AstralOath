using System;
using System.IO;
using System.Reflection;
using System.Text;
using AstralOath.Editor;
using NUnit.Framework;

namespace AstralOath.Client.Tests
{
    [Platform("Win")]
    public sealed class PowerShellToolTests
    {
        private static readonly Type Settings = typeof(HotUpdateBuilder).Assembly.GetType(
            "AstralOath.Editor.HotUpdateBuildSettings", true);

        [Test]
        public void MenuScriptRunsWithProjectPowerShellWhenUnityHasNoPowerShellOnPath()
        {
            string root = (string)Settings.GetProperty("ToolRoot", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
            if (!File.Exists(Path.Combine(root, ".tools", "powershell", "pwsh.exe")))
                Assert.Ignore("Run Tools/Setup-PowerShell.ps1 before the editor process integration test.");
            string relative = ".pwsh-regression-" + Guid.NewGuid().ToString("N") + ".ps1";
            string script = Path.Combine(root, "Tools", relative);
            string previousPath = Environment.GetEnvironmentVariable("PATH");
            const string expected = "-导表 path with 'quotes’‘‚‛ & $(exit 7); ` spaces";
            try
            {
                File.WriteAllText(script, "#requires -Version 7.0\nparam([string]$Value,[switch]$Enabled)\n" +
                    "[Console]::Write($Value + '|' + $Enabled)", new UTF8Encoding(false));
                Environment.SetEnvironmentVariable("PATH", Environment.GetFolderPath(Environment.SpecialFolder.System));
                var method = Settings.GetMethod("RunScript", BindingFlags.Static | BindingFlags.NonPublic);
                string result = null;
                Assert.DoesNotThrow(() => result = (string)method.Invoke(null,
                    new object[] { relative, null, 20, new (string, string)[] { ("-Value", expected), ("-Enabled", null) } }));
                Assert.That(result, Is.EqualTo(expected + "|True"));

                File.WriteAllText(script, "#requires -Version 7.0\n[Console]::Error.Write('导表失败'); exit 37",
                    new UTF8Encoding(false));
                var failure = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
                    new object[] { relative, null, 20, Array.Empty<(string, string)>() }));
                StringAssert.Contains("（37）", failure.InnerException.Message);
                StringAssert.Contains("导表失败", failure.InnerException.Message);
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", previousPath);
                File.Delete(script);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ResolverUsesAbsolutePathsAndPrefersProjectRuntime(bool projectInstalled)
        {
            string root = Path.Combine(Path.GetTempPath(), "astraloath-pwsh-" + Guid.NewGuid().ToString("N"));
            string local = Path.Combine(root, ".tools", "powershell", "pwsh.exe");
            string external = Path.Combine(root, "Custom PowerShell 7", "pwsh.exe");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(external));
                File.WriteAllText(external, "path selection fixture");
                if (projectInstalled)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(local));
                    File.WriteAllText(local, "project selection fixture");
                }
                var method = Settings.GetMethod("ResolvePowerShell", BindingFlags.Static | BindingFlags.NonPublic);
                string result = (string)method.Invoke(null,
                    new object[] { root, "\"" + Path.GetDirectoryName(external) + "\"", null });
                Assert.That(result, Is.EqualTo(projectInstalled ? local : external));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void MissingPowerShellReportsTheProjectBootstrapCommand()
        {
            string root = Path.Combine(Path.GetTempPath(), "astraloath-missing-pwsh-" + Guid.NewGuid().ToString("N"));
            var method = Settings.GetMethod("ResolvePowerShell", BindingFlags.Static | BindingFlags.NonPublic);
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
                new object[] { root, "", null }));
            StringAssert.Contains("Setup-PowerShell.ps1", error.InnerException.Message);
        }
    }
}
