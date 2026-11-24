using System.Collections.Generic;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>
    /// The shouted words in a dark box above the speaker's head. Vanilla's text mote is Tiny white
    /// text with no background, unreadable over grass at normal zoom, so these are drawn in OnGUI.
    /// Real time, frozen while paused, current map only. Not saved.
    /// </summary>
    public static class TrashTalkBubbles
    {
        public const float Lifetime = 5f, FadeTime = 0.6f, MaxWidth = 260f, Pad = 6f;
        static readonly Color ColonistBorder = new Color(0.95f, 0.78f, 0.35f);
        static readonly Color RaiderBorder = new Color(0.9f, 0.3f, 0.25f);
        static readonly Color Back = new Color(0.07f, 0.07f, 0.07f, 0.85f);

        class Bubble
        {
            public Pawn pawn;
            public Map map;
            public Vector3 lastPos;
            public string text;
            public bool colonist;
            public float age;
        }

        static readonly List<Bubble> bubbles = new List<Bubble>();

        public static int Count => bubbles.Count;

        public static string TextOn(Pawn p)
        {
            for (int i = 0; i < bubbles.Count; i++) if (bubbles[i].pawn == p) return bubbles[i].text;
            return null;
        }

        public static void Add(Pawn p, string text, bool colonist)
        {
            bubbles.RemoveAll(b => b.pawn == p);
            bubbles.Add(new Bubble { pawn = p, map = p.Map, lastPos = p.DrawPos, text = text, colonist = colonist });
        }

        public static void Clear() => bubbles.Clear();

        public static void Update()
        {
            if (bubbles.Count == 0 || Find.TickManager == null || Find.TickManager.Paused) return;
            float dt = Time.deltaTime;
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble b = bubbles[i];
                b.age += dt;
                if (b.age >= Lifetime || b.map == null || b.map.Disposed) bubbles.RemoveAt(i);
            }
        }

        public static void OnGUI()
        {
            if (bubbles.Count == 0 || Event.current.type != EventType.Repaint || WorldRendererUtility.WorldSelected) return;
            Map current = Find.CurrentMap;
            if (current == null) return;
            GameFont oldFont = Text.Font;
            TextAnchor oldAnchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleCenter;
            for (int i = 0; i < bubbles.Count; i++)
            {
                Bubble b = bubbles[i];
                if (b.map != current) continue;
                if (b.pawn != null && b.pawn.Spawned && b.pawn.Map == current) b.lastPos = b.pawn.DrawPos;
                Draw(b);
            }
            Text.Font = oldFont;
            Text.Anchor = oldAnchor;
            GUI.color = Color.white;
        }

        static void Draw(Bubble b)
        {
            float alpha = b.age > Lifetime - FadeTime ? Mathf.Clamp01((Lifetime - b.age) / FadeTime) : 1f;
            // anchor above the head (the vanilla interaction icon sits at the shoulder)
            Vector3 anchor = new Vector3(b.lastPos.x, 0f, b.lastPos.z + 0.85f);
            Vector2 screen = Find.Camera.WorldToScreenPoint(anchor) / Prefs.UIScale;
            screen.y = UI.screenHeight - screen.y;
            float width = Mathf.Min(Text.CalcSize(b.text).x, MaxWidth);
            float height = Text.CalcHeight(b.text, width);
            var box = new Rect(screen.x - width / 2f - Pad, screen.y - height - 2f * Pad - 6f, width + 2f * Pad, height + 2f * Pad);
            Color back = Back;
            back.a *= alpha;
            Widgets.DrawBoxSolid(box, back);
            Color border = b.colonist ? ColonistBorder : RaiderBorder;
            border.a = alpha;
            GUI.color = border;
            Widgets.DrawBox(box, 1);
            // a short tail pointing down at the speaker
            Widgets.DrawBoxSolid(new Rect(screen.x - 1f, box.yMax, 2f, 6f), border);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Widgets.Label(new Rect(box.x + Pad, box.y + Pad, width, height), b.text);
            GUI.color = Color.white;
        }
    }
}
