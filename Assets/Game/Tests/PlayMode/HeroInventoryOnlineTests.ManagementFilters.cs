#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AstralOath.Client.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AstralOath.Client.Tests
{
    public sealed partial class HeroInventoryOnlineTests
    {
        private IEnumerator CheckManagementFilters(BindingMap browser)
        {
            yield return null;
            IEnumerator OpenStatFilter(int index)
            {
                ClickVisibleCenter(browser, "manageStatFilter" + index);
                yield return null;
                Assert.That(browser.Require<GameObject>("manageStatMenu").activeInHierarchy, Is.True);
            }

            int Count() => VisibleRows(browser, "items").Length;
            Assert.That(Count(), Is.EqualTo(2));
            yield return OpenStatFilter(1);
            yield return Capture("equipment-manager-filter-menu",1478,720);
            ClickVisibleCenter(browser, "manageStatOption_speed");
            yield return null;
            Assert.That(Count(), Is.EqualTo(1));
            Assert.That(browser.Require<GameObject>("manageStatMenu").activeSelf, Is.False);
            yield return OpenStatFilter(2);
            Assert.That(browser.Require<GameButton>("manageStatOption_speed").interactable, Is.False);
            ClickVisibleCenter(browser, "manageStatOption_health");
            yield return null;
            Assert.That(Count(), Is.Zero);
            Assert.That(browser.Require<GameText>("status").text, Does.Contain("没有符合条件"));
            yield return OpenStatFilter(2);
            ClickVisibleCenter(browser, "manageStatOption_all");
            yield return null;
            Assert.That(Count(), Is.EqualTo(1));
            yield return OpenStatFilter(1);
            ClickVisibleCenter(browser, "manageStatOption_all");
            Assert.That(Count(), Is.EqualTo(2));
            yield return OpenStatFilter(0);
            ClickVisibleCenter(browser, "manageStatOption_attackPct");
            Assert.That(Count(), Is.Zero, "Flat attack and attack % are distinct.");
            yield return OpenStatFilter(0);
            ClickVisibleCenter(browser, "manageStatOption_all");
            Assert.That(Count(), Is.EqualTo(2));
            yield return OpenStatFilter(3);
            // The source menu straddles the canvas centre. Click the visible left backdrop, not the menu behind its centre.
            Canvas.ForceUpdateCanvases();
            var backdrop = browser.Require<GameButton>("manageStatBackdrop");
            var backdropRect = (RectTransform)backdrop.transform;
            var point = new PointerEventData(EventSystem.current) {
                position = RectTransformUtility.WorldToScreenPoint(null,backdropRect.TransformPoint(
                    new Vector2(backdropRect.rect.xMin + backdropRect.rect.width * .25f,backdropRect.rect.center.y)))
            };
            var hits = new List<RaycastResult>();
            backdrop.GetComponentInParent<GraphicRaycaster>().Raycast(point,hits);
            Assert.That(ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits.First().gameObject),Is.EqualTo(backdrop.gameObject));
            ExecuteEvents.ExecuteHierarchy(hits.First().gameObject,point,ExecuteEvents.pointerClickHandler);
            Assert.That(browser.Require<GameObject>("manageStatMenu").activeSelf, Is.False);
            Assert.That(Count(), Is.EqualTo(2));
        }
    }
}
#endif
