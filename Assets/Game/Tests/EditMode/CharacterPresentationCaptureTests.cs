using System.IO;
using AstralOath.Client.Bootstrap;
using NUnit.Framework;
using UnityEngine;
namespace AstralOath.Tests
{
    public sealed class CharacterPresentationCaptureTests
    {
        [TestCase(LogType.Warning,"GameText missing glyph U+4E2D",1)]
        [TestCase(LogType.Warning,"An unrelated warning",0)]
        [TestCase(LogType.Log,"GameText missing glyph U+4E2D",0)]
        public void OnlyMissingGlyphWarningsFailNativeAcceptance(LogType type,string message,int expected)
        {
            var root=new GameObject("probe-log-test");
            try
            {
                var probe=root.AddComponent<CharacterLabPlayerProbe>();
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                typeof(CharacterLabPlayerProbe).GetMethod("RecordLog",flags).Invoke(probe,new object[]{message,"",type});
                var errors=(System.Collections.Generic.List<string>)typeof(CharacterLabPlayerProbe).GetField("errors",flags).GetValue(probe);
                Assert.That(errors.Count,Is.EqualTo(expected));
            }
            finally{Object.DestroyImmediate(root);}
        }
        [Test] public void RejectsBlackPixelsIncludingTransparentBlack() =>
            Assert.Throws<InvalidDataException>(()=>CharacterLabPlayerProbe.ValidateCapturePixels(new[]{new Color32(0,0,0,255),new Color32(0,0,0,0)}));
        [Test] public void RejectsSingleColorBackgroundWithoutUi() =>
            Assert.Throws<InvalidDataException>(()=>CharacterLabPlayerProbe.ValidateCapturePixels(new[]{new Color32(18,25,38,255),new Color32(18,25,38,255)}));
        [Test] public void AcceptsVisibleRgbVariation() =>
            Assert.DoesNotThrow(()=>CharacterLabPlayerProbe.ValidateCapturePixels(new[]{new Color32(18,25,38,255),new Color32(230,235,240,255)}));
    }
}
