#if UNITY_EDITOR
using System;
using System.Reflection;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AstralOath.Client.Tests
{
    public sealed class InventoryFeedbackAudioTests
    {
        [TestCase("InventoryOpen", "UiInventoryOpen")]
        [TestCase("HeroPageOpen", "UiHeroPageOpen")]
        [TestCase("PageBack", "UiBack")]
        [TestCase("QuickMenuOpen", "UiQuickMenuOpen")]
        [TestCase("QuickMenuClose", "UiQuickMenuClose")]
        [TestCase("EquipWeapon", "UiEquipWeapon")]
        [TestCase("EquipHelmet", "UiEquipHelmet")]
        [TestCase("EquipArmor", "UiEquipArmor")]
        [TestCase("EquipNecklace", "UiEquipNecklace")]
        [TestCase("EquipRing", "UiEquipRing")]
        [TestCase("EquipBoots", "UiEquipBoots")]
        [TestCase("EquipArtifact", "UiEquipArtifact")]
        [TestCase("EquipRemove", "UiEquipRemove")]
        [TestCase("ItemLock", "UiItemLock")]
        [TestCase("Normal", "UiConfirm")]
        [TestCase("Back", "UiCancel")]
        [TestCase("RosterScroll", "UiRosterScroll")]
        public void HomeCanvasRoutesRecordedCue(string role, string asset)
        {
            Assert.That(Enum.TryParse(role, out GameUiFeedback.Sound sound), Is.True, role);
            var scene = EditorSceneManager.OpenScene("Assets/Game/Modules/Home/Scenes/GameHome.unity", OpenSceneMode.Additive);
            try
            {
                GameUiFeedback feedback = null;
                foreach (var root in scene.GetRootGameObjects())
                    if (root.TryGetComponent(out feedback)) break;
                Assert.That(feedback, Is.Not.Null);
                var resolve = typeof(GameUiFeedback).GetMethod("Resolve", BindingFlags.Instance | BindingFlags.NonPublic);
                var clip = (AudioClip)resolve.Invoke(feedback, new object[] { sound });
                Assert.That(clip, Is.Not.Null, role);
                Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo("Assets/Game/Common/Res/Sounds/" + asset + ".wav"));
                var samples = new float[clip.samples * clip.channels];
                Assert.That(clip.GetData(samples, 0), Is.True, asset);
                Assert.That(Array.Exists(samples, x => Mathf.Abs(x) > .01f), Is.True, asset);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
#endif
