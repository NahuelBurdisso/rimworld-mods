using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScores
{
    /// <summary>
    /// Feature 4 — Raid Echoes: the overlay. Everything here runs only for the current map, only while the
    /// feature and the play-settings toggle are on and the map has echoes. See DESIGN.md.
    /// </summary>
    public partial class OldScoresMapComponent
    {
        sealed class EchoVisual
        {
            public RaidEcho echo;
            public int color;
            public int alphaStep;
            public Vector3[] pts;          // one per echo.points
            public Vector3[] chevrons;     // tip, wing, wing
            public IntVec3[] chevronCells;
            public Vector3[] crosses;      // 4 per death
            public Vector3[] rings;        // RingSides + 1 per breach
        }

        sealed class EchoLabel
        {
            public IntVec3 cell;      // what must be in view and unfogged
            public IntVec3 anchor;    // where the label is drawn
            public string text;
            public Color color;
        }

        const int RingSides = 12;
        /// <summary>The dark under-stroke sits a hair below the line so the two never z-fight.</summary>
        static readonly Vector3 UnderOffset = new Vector3(0f, -0.01f, 0f);
        const float BaseLineWidth = 0.4f, BaseUnderWidth = 0.75f, ChevronSpacing = 12f, ChevronSize = 0.75f,
                    CrossSize = 0.38f, RingRadius = 1.4f;

        readonly List<EchoVisual> visuals = new List<EchoVisual>();
        readonly List<EchoLabel> labels = new List<EchoLabel>();
        bool echoesDirty = true;

        void MarkEchoesDirty() => echoesDirty = true;

        static bool OverlayOn => OldScoresMod.Settings.echoes && OldScoresTracker.Instance != null && OldScoresTracker.Instance.showEchoes;

        public override void MapComponentDraw()
        {
            if (echoes.Count == 0 || !OverlayOn) return;
            EnsureEchoCache();
            CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
            FogGrid fog = map.fogGrid;
            int budget = OldScoresTuning.EchoMaxDrawsPerFrame;
            // zoomed out the lines would shrink to a pixel: widen them with the camera (no change at normal zoom)
            float zoom = Mathf.Max(1f, Find.CameraDriver.RootSize / 18f);
            float LineWidth = BaseLineWidth * zoom, UnderWidth = BaseUnderWidth * zoom;
            // newest first: when the cap is reached it is the oldest, faintest echoes that are left out
            for (int v = visuals.Count - 1; v >= 0 && budget > 0; v--)
            {
                EchoVisual vis = visuals[v];
                RaidEcho e = vis.echo;
                Material mat = EchoMaterials.Line(vis.color, vis.alphaStep);
                Material under = EchoMaterials.Under(vis.alphaStep);
                for (int lane = 0; lane < e.laneStarts.Count && budget >= 2; lane++)
                {
                    int end = e.LaneEnd(lane);
                    for (int j = e.laneStarts[lane] + 1; j < end && budget >= 2; j++)
                    {
                        IntVec3 a = e.points[j - 1], b = e.points[j];
                        if (!SegmentInView(a, b, view) || (fog.IsFogged(a) && fog.IsFogged(b))) continue;
                        GenDraw.DrawLineBetween(vis.pts[j - 1] + UnderOffset, vis.pts[j] + UnderOffset, under, UnderWidth);
                        GenDraw.DrawLineBetween(vis.pts[j - 1], vis.pts[j], mat, LineWidth);
                        budget -= 2;
                    }
                }
                for (int c = 0; c < vis.chevronCells.Length && budget >= 2; c++)
                {
                    IntVec3 cell = vis.chevronCells[c];
                    if (!view.Contains(cell) || fog.IsFogged(cell)) continue;
                    GenDraw.DrawLineBetween(vis.chevrons[3 * c], vis.chevrons[3 * c + 1], mat, LineWidth);
                    GenDraw.DrawLineBetween(vis.chevrons[3 * c], vis.chevrons[3 * c + 2], mat, LineWidth);
                    budget -= 2;
                }
                for (int d = 0; d < e.deaths.Count && budget >= 2; d++)
                {
                    IntVec3 cell = e.deaths[d];
                    if (!view.Contains(cell) || fog.IsFogged(cell)) continue;
                    GenDraw.DrawLineBetween(vis.crosses[4 * d], vis.crosses[4 * d + 1], mat, LineWidth);
                    GenDraw.DrawLineBetween(vis.crosses[4 * d + 2], vis.crosses[4 * d + 3], mat, LineWidth);
                    budget -= 2;
                }
                for (int r = 0; r < e.breaches.Count && budget >= RingSides; r++)
                {
                    IntVec3 cell = e.breaches[r];
                    if (!view.Contains(cell) || fog.IsFogged(cell)) continue;
                    int o = r * (RingSides + 1);
                    for (int s = 0; s < RingSides; s++) GenDraw.DrawLineBetween(vis.rings[o + s], vis.rings[o + s + 1], mat, LineWidth);
                    budget -= RingSides;
                }
            }
            EchoStats.Lines += OldScoresTuning.EchoMaxDrawsPerFrame - budget;
        }

        void DrawEchoLabels()
        {
            if (echoes.Count == 0 || !OverlayOn) return;
            EnsureEchoCache();
            CellRect view = Find.CameraDriver.CurrentViewRect;
            FogGrid fog = map.fogGrid;
            for (int i = 0; i < labels.Count; i++)
            {
                EchoLabel l = labels[i];
                if (!view.Contains(l.cell) || fog.IsFogged(l.cell)) continue;
                GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(l.anchor), l.text, l.color);
                EchoStats.Labels++;
            }
        }

        static bool SegmentInView(IntVec3 a, IntVec3 b, CellRect view)
        {
            int minX = a.x < b.x ? a.x : b.x, maxX = a.x < b.x ? b.x : a.x;
            int minZ = a.z < b.z ? a.z : b.z, maxZ = a.z < b.z ? b.z : a.z;
            return maxX >= view.minX && minX <= view.maxX && maxZ >= view.minZ && minZ <= view.maxZ;
        }

        // --- cache --------------------------------------------------------------------------------

        void EnsureEchoCache()
        {
            if (!echoesDirty) return;
            echoesDirty = false;
            visuals.Clear();
            labels.Clear();
            if (echoes.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            float y = AltitudeLayer.MetaOverlays.AltitudeFor();
            var factionOrder = new List<int>();
            for (int i = 0; i < echoes.Count; i++)
            {
                RaidEcho e = echoes[i];
                int color = factionOrder.IndexOf(e.factionId);
                if (color < 0) { factionOrder.Add(e.factionId); color = factionOrder.Count - 1; }
                visuals.Add(BuildVisual(e, color % EchoMaterials.Palette.Length, now, y));
            }
            BuildLabels(factionOrder);
        }

        /// <summary>Drawing alpha of an echo in tenths: 0.85 when new, fading to 0.3 at EchoFadeTicks.</summary>
        public static int AlphaStep(RaidEcho e, int now)
        {
            float age = Mathf.Clamp01((now - e.endTick) / (float)OldScoresTuning.EchoFadeTicks);
            float alpha = Mathf.Lerp(OldScoresTuning.EchoMaxAlpha, OldScoresTuning.EchoMinAlpha, age);
            return Mathf.Clamp(Mathf.RoundToInt(alpha * 10f), 1, 10);
        }

        static Vector3 World(IntVec3 c, float y)
        {
            Vector3 v = c.ToVector3Shifted();
            v.y = y;
            return v;
        }

        EchoVisual BuildVisual(RaidEcho e, int color, int now, float y)
        {
            var vis = new EchoVisual { echo = e, color = color, alphaStep = AlphaStep(e, now), pts = new Vector3[e.points.Count] };
            for (int i = 0; i < e.points.Count; i++) vis.pts[i] = World(e.points[i], y);

            var chev = new List<Vector3>();
            var chevCells = new List<IntVec3>();
            for (int lane = 0; lane < e.laneStarts.Count; lane++)
            {
                int start = e.laneStarts[lane], end = e.LaneEnd(lane);
                float next = ChevronSpacing;
                float walked = 0f;
                for (int j = start + 1; j < end; j++)
                {
                    Vector3 a = vis.pts[j - 1], b = vis.pts[j];
                    float len = (b - a).MagnitudeHorizontal();
                    if (len < 0.01f) continue;
                    Vector3 dir = (b - a) / len;
                    while (next < walked + len)
                    {
                        // none in the last cell of a segment: it would sit on the next corner
                        if (next - walked <= len - 1f) AddChevron(chev, chevCells, a + dir * (next - walked), dir);
                        next += ChevronSpacing;
                    }
                    walked += len;
                    if (j == end - 1) AddChevron(chev, chevCells, b, dir);   // arrowhead where the lane ends
                }
            }
            vis.chevrons = chev.ToArray();
            vis.chevronCells = chevCells.ToArray();

            vis.crosses = new Vector3[e.deaths.Count * 4];
            for (int d = 0; d < e.deaths.Count; d++)
            {
                Vector3 c = World(e.deaths[d], y);
                vis.crosses[4 * d] = c + new Vector3(-CrossSize, 0f, -CrossSize);
                vis.crosses[4 * d + 1] = c + new Vector3(CrossSize, 0f, CrossSize);
                vis.crosses[4 * d + 2] = c + new Vector3(-CrossSize, 0f, CrossSize);
                vis.crosses[4 * d + 3] = c + new Vector3(CrossSize, 0f, -CrossSize);
            }

            vis.rings = new Vector3[e.breaches.Count * (RingSides + 1)];
            for (int r = 0; r < e.breaches.Count; r++)
            {
                Vector3 c = World(e.breaches[r], y);
                for (int s = 0; s <= RingSides; s++)
                {
                    float ang = s * Mathf.PI * 2f / RingSides;
                    vis.rings[r * (RingSides + 1) + s] = c + new Vector3(Mathf.Cos(ang) * RingRadius, 0f, Mathf.Sin(ang) * RingRadius);
                }
            }
            return vis;
        }

        static void AddChevron(List<Vector3> chev, List<IntVec3> cells, Vector3 tip, Vector3 dir)
        {
            Vector3 w1 = Quaternion.AngleAxis(35f, Vector3.up) * dir;
            Vector3 w2 = Quaternion.AngleAxis(-35f, Vector3.up) * dir;
            chev.Add(tip);
            chev.Add(tip - w1 * ChevronSize);
            chev.Add(tip - w2 * ChevronSize);
            cells.Add(tip.ToIntVec3());
        }

        void BuildLabels(List<int> factionOrder)
        {
            List<Faction> factions = Find.FactionManager?.AllFactionsListForReading;
            float mergeSq = OldScoresTuning.EchoBreachMergeDist * OldScoresTuning.EchoBreachMergeDist;
            for (int f = 0; f < factionOrder.Count; f++)
            {
                int id = factionOrder[f];
                RaidEcho newest = null;
                int count = 0, newestIndex = -1;
                var clusterCells = new List<IntVec3>();
                var clusterCounts = new List<int>();
                var clusterTicks = new List<int>();
                for (int i = 0; i < echoes.Count; i++)
                {
                    RaidEcho e = echoes[i];
                    if (e.factionId != id) continue;
                    count++;
                    if (newest == null || e.endTick >= newest.endTick) { newest = e; newestIndex = i; }
                    for (int b = 0; b < e.breaches.Count; b++)
                    {
                        int k = 0;
                        while (k < clusterCells.Count && (clusterCells[k] - e.breaches[b]).LengthHorizontalSquared > mergeSq) k++;
                        if (k == clusterCells.Count) { clusterCells.Add(e.breaches[b]); clusterCounts.Add(0); clusterTicks.Add(0); }
                        clusterCounts[k]++;
                        if (e.endTick > clusterTicks[k]) clusterTicks[k] = e.endTick;
                    }
                }
                if (newest == null) continue;
                string name = null;
                if (factions != null)
                    for (int i = 0; i < factions.Count && name == null; i++) if (factions[i].loadID == id) name = factions[i].Name;
                if (name.NullOrEmpty()) name = newest.factionName ?? "?";
                string raids = count == 1 ? "OldScores_Echo_OneRaid".Translate().Resolve() : "OldScores_Echo_Raids".Translate(count).Resolve();
                EchoVisual nv = visuals[newestIndex];
                // the line colour lifted towards white: tiny text on the grey label background needs the contrast
                Color color = Color.Lerp(EchoMaterials.Palette[nv.color], Color.white, 0.45f);
                color.a = Mathf.Max(0.8f, nv.alphaStep / 10f + 0.1f);

                if (clusterCells.Count > 0)
                {
                    int main = 0;
                    for (int k = 1; k < clusterCells.Count; k++)
                        if (clusterCounts[k] > clusterCounts[main] || (clusterCounts[k] == clusterCounts[main] && clusterTicks[k] > clusterTicks[main])) main = k;
                    for (int k = 0; k < clusterCells.Count; k++)
                    {
                        string text = k == main
                            ? "OldScores_Echo_LabelBreach".Translate(name, raids).Resolve()
                            : "OldScores_Echo_LabelBreachOther".Translate(name).Resolve();
                        labels.Add(new EchoLabel { cell = clusterCells[k], anchor = Below(clusterCells[k], 2), text = text, color = color });
                    }
                }
                else
                {
                    IntVec3 at = newest.laneStarts.Count > 0 ? newest.points[newest.LaneEnd(0) - 1]
                        : newest.deaths.Count > 0 ? newest.deaths[0] : newest.arrival;
                    if (!at.IsValid || !at.InBounds(map)) continue;
                    labels.Add(new EchoLabel { cell = at, anchor = Above(at, 2), text = "OldScores_Echo_Label".Translate(name, raids).Resolve(), color = color });
                }
            }
        }

        IntVec3 Below(IntVec3 c, int cells) => new IntVec3(c.x, 0, Mathf.Max(0, c.z - cells));
        IntVec3 Above(IntVec3 c, int cells) => new IntVec3(c.x, 0, Mathf.Min(map.Size.z - 1, c.z + cells));

        /// <summary>Test support: the labels the overlay would draw on this map, as "text @ cell".</summary>
        public List<string> EchoLabelsForTest()
        {
            EnsureEchoCache();
            var list = new List<string>();
            for (int i = 0; i < labels.Count; i++) list.Add(labels[i].text + " @ " + labels[i].cell);
            return list;
        }
    }

    /// <summary>Line and label draw counters (cumulative), so a test can tell whether the overlay drew anything.</summary>
    public static class EchoStats
    {
        public static int Lines, Labels;
    }

    /// <summary>Materials for the echo lines: palette colour × 10 alpha steps, plus a dark under-stroke per step.</summary>
    public static class EchoMaterials
    {
        public static readonly Color[] Palette =
        {
            new Color(1f, 0.52f, 0.44f),    // pale red
            new Color(1f, 0.82f, 0.4f),     // amber
            new Color(0.8f, 0.64f, 1f),     // violet
            new Color(0.48f, 0.93f, 0.86f), // teal
            new Color(0.96f, 0.94f, 0.86f), // bone
        };

        static readonly Dictionary<int, Material> cache = new Dictionary<int, Material>();

        public static Material Line(int color, int alphaStep) => Get(color, alphaStep, false);
        public static Material Under(int alphaStep) => Get(0, alphaStep, true);

        static Material Get(int color, int alphaStep, bool under)
        {
            int key = (under ? 100 : color) * 16 + alphaStep;
            if (cache.TryGetValue(key, out Material m)) return m;
            Color c = under ? new Color(0.05f, 0.04f, 0.04f, alphaStep * 0.07f) : Palette[color];
            if (!under) c.a = alphaStep / 10f;
            m = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, c);
            cache[key] = m;
            return m;
        }
    }

    [StaticConstructorOnStartup]
    public static class EchoTextures
    {
        public static readonly Texture2D Toggle = ContentFinder<Texture2D>.Get("UI/OldScores/EchoesToggle");
    }

    /// <summary>
    /// PlaySettings.DoPlaySettingsGlobalControls(WidgetRow row, bool worldView) - RimWorld/PlaySettings.cs:108.
    /// One more toggle in the bottom-right icon row, map view only.
    /// </summary>
    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    public static class Patch_PlaySettingsEchoes
    {
        static string tooltip;

        public static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || row == null || !OldScoresMod.Settings.echoes) return;
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null || Find.CurrentMap == null) return;
            try
            {
                tooltip ??= "OldScores_Echo_ToggleTip".Translate().Resolve();
                row.ToggleableIcon(ref tracker.showEchoes, EchoTextures.Toggle, tooltip, SoundDefOf.Mouseover_ButtonToggle);
            }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] echo toggle failed: " + ex, 0x0D5C15); }
        }
    }

    /// <summary>
    /// IncidentWorker_Raid.TryGenerateRaidInfo(IncidentParms parms, out List&lt;Pawn&gt; pawns, bool debugTest)
    /// - RimWorld/IncidentWorker_Raid.cs:66. Every raid worker goes through it; the pawns have arrived
    /// (walk-in) or are in their drop pods.
    /// </summary>
    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    public static class Patch_RaidGenerated
    {
        public static void Postfix(IncidentParms parms, ref List<Pawn> pawns, bool debugTest, bool __result)
        {
            if (!__result || debugTest) return;
            try { OldScoresMapComponent.Notify_RaidGenerated(parms, pawns); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] raid echo handling failed: " + ex, 0x0D5C13); }
        }
    }
}
