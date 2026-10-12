using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// The vigil light: a vanilla CompGlower (the actual light level) plus a soft amber halo drawn around
    /// the bed, so the vigil reads under a roof by day too. RealtimeOnly: DrawAt runs every frame while the
    /// thing is on screen (Verse/DynamicDrawManager.cs). Purely visual; never saved (isSaveable false).
    /// </summary>
    public class VigilGlow : ThingWithComps
    {
        public static readonly Color HaloColor = new Color(0.36f, 0.21f, 0.06f);

        static Texture2D haloTex;

        /// <summary>
        /// A soft ring of light, made once on the main thread: 35% at the centre (the bed and the patient are
        /// lit, not washed out), brightest a little out from the bed, fading to nothing at the edge.
        /// </summary>
        public static Texture2D HaloTex
        {
            get
            {
                if (haloTex != null) return haloTex;
                const int N = 128;
                var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { name = "AftermathVigilHalo", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float v = (0.35f + 0.65f * Mathf.SmoothStep(0f, 1f, d / 0.42f)) * (1f - Mathf.SmoothStep(0f, 1f, (d - 0.42f) / 0.58f));
                        if (d >= 1f) v = 0f;
                        px[y * N + x] = new Color(v, v, v, v);
                    }
                tex.SetPixels(px);
                tex.Apply(false, true);
                haloTex = tex;
                return haloTex;
            }
        }
        const int Steps = 20;   // cached material per intensity step

        static readonly Material[] mats = new Material[Steps + 1];

        /// <summary>Size of the halo in cells (set by the tracker from the bed).</summary>
        public float haloSize = 4.2f;
        /// <summary>1 = full; drops with the fade (set by the tracker).</summary>
        public float intensity = 1f;
        public Vector3 haloCenter;
        bool centerSet;

        public static float HaloSizeFor(Building_Bed bed) => 2.4f * bed.def.size.x + 1.8f;

        public void SetBed(Building_Bed bed)
        {
            if (bed == null) return;
            haloSize = HaloSizeFor(bed);
            haloCenter = bed.TrueCenter();
            centerSet = true;
        }

        /// <summary>The halo material for the current intensity (null when fully faded). Exposed for the test.</summary>
        public Material HaloMaterial
        {
            get
            {
                int step = Mathf.Clamp(Mathf.RoundToInt(intensity * Steps), 0, Steps);
                if (step == 0) return null;
                if (mats[step] == null)
                {
                    float f = (float)step / Steps;
                    mats[step] = MaterialPool.MatFrom(HaloTex, ShaderDatabase.MoteGlow,
                        new Color(HaloColor.r * f, HaloColor.g * f, HaloColor.b * f, 1f));
                }
                return mats[step];
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (!AftermathMod.Settings.vigilLamps) return;
            Material mat = HaloMaterial;
            if (mat == null) return;
            Vector3 pos = centerSet ? haloCenter : drawLoc;
            // above the lighting overlay: a lamp's light is not darkened by the roof or the night around it
            pos.y = AltitudeLayer.LightingOverlay.AltitudeFor() + Altitudes.AltInc * 0.5f;
            // a slow candle flicker: the halo breathes by +-4%
            float t = Time.realtimeSinceStartup * 2.4f + (thingIDNumber % 97) * 0.37f;
            float s = haloSize * (1f + 0.04f * Mathf.Sin(t) * Mathf.Sin(t * 0.63f + 1.1f));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, 1f, s)), mat, 0);
        }
    }
}
