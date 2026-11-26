using System.Collections.Generic;
using AstralOath.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AstralOath.Editor
{
    public static partial class ArenaUiBuilder
    {
        private static void BuildArtifactEnhancement(RectTransform root, List<(string Key, Object Target)> bindings)
        {
            var page = Area(root, "Artifact enhancement", 0, 0, 1, 1);
            var fields = new List<(string Key, Object Target)>();
            void Link(string key, Object target) => fields.Add((key, target));
            Sprite ArtFor(string key) => InventoryArt("ArtifactEnhancement/" + key);
            var gold = new Color32(214, 173, 110, 255);
            var orange = new Color32(255, 135, 27, 255);
            GameText Label(string key, RectTransform parent, float x, float y, float w, float h, float size,
                Color color, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool wrap = false)
            {
                var label = Text(parent, key, null, size, x, y, w, h, color, alignment, wrap);
                Link(key, label);
                return label;
            }

            Paint(page, InventoryArt("base_unit_hero"), new Color32(120, 120, 120, 255), true);
            var header = Area(page, "Enhancement top bar", 0, 0, 1, .09f);
            Paint(header, null, Black(.64f), true);
            Link("back", SharedHeaderBack(header));
            Text(header, "Title", ArtifactInventoryPrefix + "upgradeTitle", 33, .043f, 0, .25f, 1);
            DrawNativeTopBarIcons(page, dimmed: true);
            Picture(header, "Gold icon", ArtFor("token_gold"), .606f, .14f, .035f, .72f, true);
            Label("balance", header, .642f, .08f, .14f, .82f, 26, Color.white);

            var left = Area(page, "Artifact stats and skill", .09f, .121f, .229f, .84f);
            Label("grade", left, 0, .011f, 1, .064f, 27, gold);
            Label("name", left, 0, .068f, 1, .088f, 42, Color.white);
            for (int i = 0; i < 3; ++i)
            {
                float y = .151f + .062f * i;
                string key = i == 0 ? "attack" : i == 1 ? "health" : "cap";
                if (i < 2) Picture(left, key + " icon", InventoryArt("Equipment/cm_icon_stat_" + (i == 0 ? "att" : "max_hp")),
                    0, y, .068f, .06f, true);
                Text(left, key + " label", i < 2 ? InventoryPrefix + "stat." + key : ArtifactInventoryPrefix + "maxEnhance",
                    35, i < 2 ? .08f : 0, y, .46f, .06f);
                Label(key, left, .44f, y, .22f, .06f, 40, Color.white, TextAlignmentOptions.MidlineRight);
                Label(key + "Gain", left, .67f, y, .33f, .06f, 36, orange, TextAlignmentOptions.MidlineRight);
            }
            Paint(Area(left, "Skill rule", 0, .35f, 1, .015f),BagArt("_bar_2"),new Color32(146,109,62,178),sliced:true);
            Text(left, "Skill heading", ArtifactInventoryPrefix + "skill_heading", 30, .08f, .371f, .88f, .059f, gold);
            Picture(left, "Skill icon", RosterArt("cm_icon_pararti"), 0, .37f, .08f, .064f, true);
            Paint(Area(left, "Skill divider", 0, .444f, 1, .012f),BagArt("_bar_2"),new Color32(146,109,62,153),sliced:true);
            Label("rank", left, 0, .459f, 1, .06f, 34, orange);
            Label("skill", left, 0, .533f, 1, .18f, 29, Color.white, TextAlignmentOptions.TopLeft, true);
            Label("nextRank", left, 0, .76f, 1, .059f, 34, orange);
            Label("nextSkill", left, 0, .83f, 1, .167f, 29, Color.white, TextAlignmentOptions.TopLeft, true);
            NativeSeparator("Left separator",.335f);
            NativeSeparator("Right separator",.677f);
            void NativeSeparator(string name,float x)
            {
                var rail=Area(page,name,x,.118f,0,.846f);
                for(int i=0;i<2;i++)
                {
                    var half=Area(rail,i==0?"Upper ornament":"Lower ornament",.5f,i==0?.25f:.75f,0,0);
                    half.sizeDelta=new Vector2(306f,17f)*BagDesignScale;
                    half.localEulerAngles=new Vector3(0,0,i==0?-90:90);
                    Paint(half,ArtifactRestoreArt("_bar_4"),new Color32(146,109,62,178),sliced:true)
                        .pixelsPerUnitMultiplier=1f/BagDesignScale;
                }
            }

            var dial = Area(page, "Native rune dial", .49f, .349f, 0, 0);
            Image Rune(string key, float width, float height, float dx = 0f, float dy = 0f, byte alpha = 255)
            {
                var pivot = Area(dial, key + " pivot", .5f, .5f, 0, 0);
                var layer = Area(pivot, key, .5f, .5f, 0, 0);
                const float scale = .75f * BagDesignScale;
                layer.anchoredPosition = new Vector2(dx, dy) * scale;
                layer.sizeDelta = new Vector2(width, height) * scale;
                var image = Paint(layer, ArtFor(key), new Color32(255, 255, 255, alpha));
                Link(key, pivot);
                return image;
            }
            Rune("upgrade_bg", 799, 723, 0, 1.47f, 149);
            Rune("panta_bg", 538, 537, 0, 0, 154);
            Rune("star_01", 387, 96, 16.21f, 229.89f, 138);
            Rune("star_02", 195, 272, -123.25f, 78.53f, 138);
            Rune("star_03", 133, 211, 146.66f, 71.44f, 138);
            Rune("stone_b", 221, 221);
            Rune("panta_in", 236, 216, 0, 0, 111);
            Rune("stone", 319, 317);
            Link("glow", Rune("stone_glow", 333, 321, 1.27f, 4.89f, 0));
            Link("portrait", Picture(page, "Enhanced artifact", null, .441f, .258f, .098f, .183f, true));
            Label("level", page, .494f, .259f, .052f, .04f, 24, orange, TextAlignmentOptions.Center);
            Link("success", Rune("flash", 400, 400, 0, 0, 0));

            var center = Area(page, "Enhancement materials", .376f, .55f, .228f, .419f);
            Text(center, "Limit break", ArtifactInventoryPrefix + "breaksTitle", 21, 0, 0, 1, .092f, Mist, TextAlignmentOptions.Center);
            for (int i = 0; i < 5; i++)
            {
                float x = .255f + i * .09f;
                Picture(center, "Limit slot " + i, SummonArt("arti_gauge_bg"), x, .087f, .101f, .045f, true);
                Link("break" + i, Picture(center, "Limit " + i, BagArt("arti_gauge_bar"), x, .087f, .101f, .045f, true));
            }
            Paint(Area(center, "Experience rail", .06f, .19f, .88f, .033f), ArtifactRestoreArt("_progress_bg"), Color.white, sliced:true);
            var fill = Area(center, "Experience", .06f, .19f, .88f, .033f);
            fill.pivot = new Vector2(0, .5f);
            Paint(fill, ArtifactRestoreArt("_progress_bar"), new Color32(146,109,62,255));
            Link("fill", fill);
            Label("exp", center, 0, .145f, 1, .117f, 22, Color.white, TextAlignmentOptions.Center);
            Label("gain", center, .80f, .145f, .20f, .115f, 23, orange, TextAlignmentOptions.Center);
            var tray = InventoryScroll(center, "Selected materials", 0, .287f, 1, .457f);
            Link("tray", tray);
            for (int i = 0; i < 20; i++)
            {
                var tile = Area(tray.content, "Material " + i, i % 5 * .201f, (i / 5) * .25f, .194f, .243f);
                Link("remove" + i, Button(tile, BagArt("cm_item_slot_arti"), Color.white));
                Link("material" + i, Picture(tile, "Material icon", null, 0, 0, 1, 1, true));
            }
            var submitButton = InventoryAction(center, "Enhance", ArtifactInventoryPrefix + "enhance", 29,
                0, .776f, 1, .217f, out _, "GreenCost");
            Link("submit", submitButton);
            Link("cost", submitButton.GetComponent<BindingMap>().Require<GameText>("cost"));
            Label("status", page, .32f, .81f, .36f, .06f, 23, gold, TextAlignmentOptions.Center);
            var sort = Area(page, "Material sort", .72f, .118f, .095f, .062f);
            Link("sort", Button(sort, BagArt("_btn_small_black"), Color.white));
            Label("sortLabel", sort, 0, 0, 1, 1, 23, Mist, TextAlignmentOptions.Center);
            Label("capacity", page, .82f, .118f, .105f, .062f, 24, Mist, TextAlignmentOptions.Center);
            Link("candidates", InventoryScroll(page, "Materials", .689f, .19f, .248f, .808f));
            var materialArt = Area(page, "Material artwork", 0, 0, 0, 0);
            foreach (var pair in new[] { ("epicCharm", 3), ("greaterCharm", 2), ("lesserCharm", 1) })
            {
                Link(pair.Item1, Picture(materialArt, pair.Item1, ArtFor("icon_stone_art" + pair.Item2), 0, 0, 1, 1));
                Link(pair.Item1 + "Banner", Picture(materialArt, pair.Item1 + " banner",
                    Load<Sprite>(InventoryTextures + "ArtifactEnhancement/stone_art" + pair.Item2 + "_l.jpg"), 0, 0, 1, 1));
            }
            Link("bottle", Picture(materialArt, "Bottle", ArtFor("icon_bottle_01"), 0, 0, 1, 1));
            materialArt.gameObject.SetActive(false);
            Bind(page, fields.ToArray());
            bindings.Add(("artifactEnhancement", page.GetComponent<BindingMap>()));
            page.gameObject.SetActive(false);
        }
    }
}
