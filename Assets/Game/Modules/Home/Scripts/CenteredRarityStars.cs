using UnityEngine;
using UnityEngine.UI;

namespace AstralOath.Client.Home
{
    internal sealed class CenteredRarityStars
    {
        private readonly Image[] stars;
        private readonly float center, pitch, width;

        // Stars use proportional anchors. A portrait can share their parent or be their parent.
        internal CenteredRarityStars(Image[] stars, RectTransform siblingPortrait = null)
        {
            this.stars = stars;
            var first = stars[0].rectTransform;
            width = first.anchorMax.x - first.anchorMin.x;
            pitch = stars[1].rectTransform.anchorMin.x - first.anchorMin.x;
            center = siblingPortrait ? (siblingPortrait.anchorMin.x + siblingPortrait.anchorMax.x) * .5f : .5f;
        }

        internal void Show(int count)
        {
            count = Mathf.Clamp(count, 0, stars.Length);
            float left = center - (width + (count - 1) * pitch) * .5f;
            for (int i = 0; i < stars.Length; ++i)
            {
                stars[i].enabled = i < count;
                if (i >= count) continue;
                var rect = stars[i].rectTransform;
                float x = left + i * pitch;
                rect.anchorMin = new Vector2(x, rect.anchorMin.y);
                rect.anchorMax = new Vector2(x + width, rect.anchorMax.y);
            }
        }
    }
}
