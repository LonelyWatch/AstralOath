using AstralOath.Client.Common;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    public sealed class EquipmentItemCardBinder
    {
        private readonly EquipmentItemCard view;
        private readonly NotificationBadgeBinder badge;
        public GameButton Select => view.Select;

        public EquipmentItemCardBinder(BindingMap instance)
        {
            view = new EquipmentItemCard(instance);
            badge = new NotificationBadgeBinder(view.Badge);
        }

        public void Set(Sprite icon, Sprite gradeFrame, Sprite setIcon, Sprite statIcon,
            Sprite wearerIcon, int level, int enhancement, string stat)
        {
            SetImage(view.Icon, icon);
            SetImage(view.GradeFrame, gradeFrame);
            SetImage(view.SetIcon, setIcon);
            SetImage(view.WearerIcon, wearerIcon);
            SetImage(view.StatIcon, statIcon);
            view.StatIcon.gameObject.SetActive(statIcon);
            view.Level.text = level > 0 ? level.ToString(CultureInfo.InvariantCulture) : string.Empty;
            view.Stat.text = stat ?? string.Empty;
            badge.Set(false, enhancement);
        }

        public void SetSelected(bool selected)
        {
            view.SelectedPlate.enabled = selected;
            view.Highlight.enabled = selected;
        }

        private static void SetImage(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.enabled = sprite;
        }
    }
}
