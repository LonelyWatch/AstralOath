using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AstralOath.Client.Tests
{
    public sealed class MailHotUpdateConfigurationTests
    {
        [Serializable] private sealed class Configuration
        {
            public string[] assemblies;
            public string entryAssembly, entryType;
        }

        [Test]
        public void MailIsLoadedOnceBeforeTheUnchangedFoundationEntry()
        {
            var config = JsonUtility.FromJson<Configuration>(File.ReadAllText("Config/HotUpdate/assemblies.json"));
            Assert.That(config.entryAssembly, Is.EqualTo("AstralOath.Client.FoundationDemo"));
            Assert.That(config.entryType, Is.EqualTo("AstralOath.Client.FoundationDemo.FoundationEntry"));
            Assert.That(config.assemblies.Count(name => name == "AstralOath.Client.Mail"), Is.EqualTo(1));
            Assert.That(Array.IndexOf(config.assemblies, "AstralOath.Client.Mail"),
                Is.LessThan(Array.IndexOf(config.assemblies, config.entryAssembly)));
        }
    }
}
