using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>Screen vignette + caption for the last stand. Drawn under vanilla UI (GameComponentOnGUI).</summary>
    public static class LastStandOverlay
    {
        const float CaptionFadeIn = 0.6f, CaptionHold = 3.2f, CaptionFadeOut = 1.2f;
        static Texture2D vignette;

        static Texture2D Vignette
        {
            get
            {
                if (vignette == null) vignette = MakeVignette(128);
                return vignette;
            }
        }

        public static void Draw(Pawn hero, string caption, float captionStart)
        {
            bool stillHeroic = hero.health.hediffSet.HasHediff(GestaDefOf.Gesta_LastStand);
            if (GestaMod.Settings.lastStandVignette && stillHeroic)
            {
                // heartbeat: two quick beats per ~1.1 s, stronger as health drops
                float t = Time.realtimeSinceStartup % 1.1f;
                float beat = Mathf.Max(Pulse(t, 0f), 0.6f * Pulse(t, 0.22f));
                float hurt = 1f - hero.health.summaryHealth.SummaryHealthPercent;
                float alpha = Mathf.Clamp01(0.5f + 0.3f * hurt + 0.2f * beat);
                GUI.color = new Color(0.35f, 0f, 0f, alpha);
                GUI.DrawTexture(new Rect(0f, 0f, UI.screenWidth, UI.screenHeight), Vignette, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }

            float age = Time.realtimeSinceStartup - captionStart;
            if (caption != null && age >= 0f && age < CaptionFadeIn + CaptionHold + CaptionFadeOut)
            {
                float a = age < CaptionFadeIn ? age / CaptionFadeIn
                        : age < CaptionFadeIn + CaptionHold ? 1f
                        : 1f - (age - CaptionFadeIn - CaptionHold) / CaptionFadeOut;
                Text.Font = GameFont.Medium;
                Text.Anchor = TextAnchor.MiddleCenter;
                var rect = new Rect(0f, UI.screenHeight * 0.16f, UI.screenWidth, 40f);
                GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
                Widgets.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), caption);
                GUI.color = new Color(LastStandTracker.Gold.r, LastStandTracker.Gold.g, LastStandTracker.Gold.b, a);
                Widgets.Label(rect, caption);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Text.Font = GameFont.Small;
            }
        }

        static float Pulse(float t, float at)
        {
            float d = (t - at) / 0.12f;
            return Mathf.Exp(-d * d);
        }

        /// <summary>Transparent centre, opaque edges (smoothstep on distance from the centre).</summary>
        static Texture2D MakeVignette(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float half = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half, dy = (y - half) / half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41421f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.95f, d));
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }

    /// <summary>Gold map label for the pawn making the last stand. PawnNameColorUtility.PawnNameColorOf(Pawn pawn).</summary>
    [HarmonyPatch(typeof(PawnNameColorUtility), nameof(PawnNameColorUtility.PawnNameColorOf))]
    public static class Patch_PawnNameColorOf
    {
        public static void Postfix(Pawn pawn, ref Color __result)
        {
            if (pawn == null || pawn != LastStandTracker.ActiveHero || pawn.MentalStateDef != null) return;
            if (pawn.health.hediffSet.HasHediff(GestaDefOf.Gesta_LastStand)) __result = LastStandTracker.Gold;
        }
    }

    /// <summary>When the last stand wears off, the body collects the debt.</summary>
    public class HediffCompProperties_LastStandCrash : HediffCompProperties
    {
        public float restAfter = 0.15f;
        public HediffCompProperties_LastStandCrash() { compClass = typeof(HediffComp_LastStandCrash); }
    }

    public class HediffComp_LastStandCrash : HediffComp
    {
        HediffCompProperties_LastStandCrash Props => (HediffCompProperties_LastStandCrash)props;

        public override void CompPostPostRemoved()
        {
            Need_Rest rest = Pawn?.needs?.rest;
            if (rest != null && !Pawn.Dead && rest.CurLevel > Props.restAfter) rest.CurLevel = Props.restAfter;
        }
    }
}
