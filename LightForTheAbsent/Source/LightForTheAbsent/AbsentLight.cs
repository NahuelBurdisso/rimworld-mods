using RimWorld;
using UnityEngine;
using Verse;

namespace LightForTheAbsent
{
    /// <summary>
    /// The light left on: a vanilla CompGlower (real, candle-strength light; only re-registered when the tracker
    /// changes its level, at most once per poll) plus a soft amber halo drawn over the bed every frame while on
    /// screen (RealtimeOnly). The halo is drawing only - no gameplay effect - and does the smooth parts: the slow
    /// fade between polls, the candle breathing and the drowning flicker after a death. Never saved.
    /// </summary>
    public class AbsentLight : ThingWithComps
    {
        public static readonly Color HaloColor = new Color(0.9f, 0.55f, 0.2f);
        /// <summary>Glow-grid strength while mourning (the flicker itself is only drawn).</summary>
        public const float MourningGlow = 0.6f;

        const int StepsPerUnit = 20;
        const float MaxIntensity = 2f;
        static readonly Material[] mats = new Material[Mathf.RoundToInt(MaxIntensity * StepsPerUnit) + 1];
        static Texture2D haloTex;

        Vector3 center;
        bool centerSet;
        public float haloSize = 4.4f;

        float levelFrom = 1f, levelTo = 1f;
        int levelTick = -99999;
        public bool flicker;
        float appliedGlow = -1f;
        CompGlower glower;

        public CompGlower Glower => glower ??= GetComp<CompGlower>();

        /// <summary>Culling bounds for the dynamic drawer (read at spawn, before Init): as big as the largest halo
        /// (a 3-wide bed, 8.4 cells) so the halo does not pop out when the bed itself leaves the screen.</summary>
        public override Vector2 DrawSize => new Vector2(9f, 9f);

        /// <summary>0..1: where the fade is right now, interpolated between the last two polls.</summary>
        public float CurrentLevel
        {
            get
            {
                if (Find.TickManager == null) return levelTo;
                float t = Mathf.Clamp01((Find.TickManager.TicksGame - levelTick) / (float)AbsenceTracker.PollInterval);
                return Mathf.Lerp(levelFrom, levelTo, t);
            }
        }

        public float TargetLevel => levelTo;
        public float AppliedGlow => appliedGlow;
        public Vector3 HaloCenter => center;

        public void Init(Building_Bed bed)
        {
            center = bed.TrueCenter();
            haloSize = 2f * bed.def.size.x + 2.4f;   // 4.4 cells for a single bed, 6.4 for a double
            centerSet = true;
        }

        /// <summary>Set by the tracker on every poll.</summary>
        public void SetLevel(float level, bool flickering)
        {
            levelFrom = levelTick < 0 ? level : CurrentLevel;
            levelTo = Mathf.Clamp01(level);
            levelTick = Find.TickManager.TicksGame;
            flicker = flickering;

            // the real light, quantized so a fade re-registers the glower at most ~20 times
            float g = levelTo * (flickering ? MourningGlow : 1f) * LightForTheAbsentMod.Settings.Brightness;
            g = Mathf.Round(g * StepsPerUnit) / StepsPerUnit;
            // also before spawning: unspawned, the setter only stores the colour, so the glower registers once
            if (!Mathf.Approximately(g, appliedGlow) && Glower != null)
            {
                appliedGlow = g;
                Glower.GlowColor = Glower.Props.glowColor * g;
            }
        }

        /// <summary>A soft disc, brightest in the middle, made once on the main thread.</summary>
        static Texture2D HaloTex
        {
            get
            {
                if (haloTex != null) return haloTex;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "LightForTheAbsentHalo", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = d >= 1f ? 0f : Mathf.Pow(1f - Mathf.SmoothStep(0f, 1f, d), 1.8f);
                        px[y * N + x] = new Color(v, v, v, v);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                haloTex = tex;
                return haloTex;
            }
        }

        /// <summary>The cached additive material for an intensity (null at zero). Public for the test.</summary>
        public static Material HaloMaterial(float intensity)
        {
            int step = Mathf.Clamp(Mathf.RoundToInt(intensity * StepsPerUnit), 0, mats.Length - 1);
            if (step == 0) return null;
            if (mats[step] == null)
            {
                float f = (float)step / StepsPerUnit;
                mats[step] = MaterialPool.MatFrom(HaloTex, ShaderDatabase.MoteGlow, new Color(HaloColor.r * f, HaloColor.g * f, HaloColor.b * f, 1f));
            }
            return mats[step];
        }

        /// <summary>0..1 multiplier: a candle breathing, or one drowning in its own wax after a death.</summary>
        public float FlickerFactor(float realTime)
        {
            float phase = (thingIDNumber % 97) * 0.37f;
            if (!flicker)
                return 1f + 0.06f * Mathf.Sin(realTime * 2.1f + phase) * Mathf.Sin(realTime * 1.3f + 1.1f);
            float n = Mathf.PerlinNoise(realTime * 2.6f + phase, 0.37f);
            return n < 0.3f ? 0.22f : Mathf.Lerp(0.5f, 1f, (n - 0.3f) / 0.7f);   // brief gutters, mostly a weak, unsteady glow
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (Map == null || Position.Fogged(Map)) return;
            float t = Time.realtimeSinceStartup;
            Material mat = HaloMaterial(CurrentLevel * FlickerFactor(t) * LightForTheAbsentMod.Settings.Brightness);
            if (mat == null) return;
            Vector3 pos = centerSet ? center : drawLoc;
            // above the lighting overlay: the lamp's own light is not darkened by the night around it
            pos.y = AltitudeLayer.LightingOverlay.AltitudeFor() + Altitudes.AltInc * 0.5f;
            float s = haloSize * (flicker ? 1f : 1f + 0.03f * Mathf.Sin(t * 2.4f + thingIDNumber % 31));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s)), mat, 0);
        }
    }
}
