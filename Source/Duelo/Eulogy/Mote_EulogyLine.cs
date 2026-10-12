using UnityEngine;
using Verse;

namespace Duelo
{
    /// <summary>
    /// One line of a eulogy, rising straight up from a grave. Drawn as GUI text (like vanilla MoteText)
    /// but larger, warm, wrapped, and outlined so it reads over grass, snow and night alike.
    /// </summary>
    public class Mote_EulogyLine : MoteThrown
    {
        public const float MaxWidth = 520f;
        public const float RiseSpeed = 0.32f;   // cells per second

        static readonly Color Warm = new Color(1f, 0.85f, 0.6f);

        public string text;

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
        }

        public override void DrawGUIOverlay()
        {
            if (text.NullOrEmpty()) return;
            float a = Mathf.Clamp01(Alpha);
            if (a <= 0.01f) return;

            Vector2 screen = Find.Camera.WorldToScreenPoint(DrawPos) / Prefs.UIScale;
            screen.y = UI.screenHeight - screen.y;

            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperCenter;
            float w = Mathf.Min(Text.CalcSize(text).x + 6f, MaxWidth);
            float h = Text.CalcHeight(text, w);
            var rect = new Rect(screen.x - w / 2f, screen.y - h, w, h);

            // a soft dark plate so the line reads over ritual light, snow or bright grass
            Widgets.DrawBoxSolid(rect.ExpandedBy(10f, 4f), new Color(0.06f, 0.04f, 0.03f, a * 0.62f));

            GUI.color = new Color(0f, 0f, 0f, a * 0.75f);
            Widgets.Label(new Rect(rect.x - 1f, rect.y, w, h), text);
            Widgets.Label(new Rect(rect.x + 1f, rect.y, w, h), text);
            Widgets.Label(new Rect(rect.x, rect.y - 1f, w, h), text);
            Widgets.Label(new Rect(rect.x, rect.y + 1f, w, h), text);
            GUI.color = new Color(Warm.r, Warm.g, Warm.b, a);
            Widgets.Label(rect, text);

            GUI.color = Color.white;
            Text.Anchor = oldAnchor;
            Text.Font = oldFont;
        }
    }
}
