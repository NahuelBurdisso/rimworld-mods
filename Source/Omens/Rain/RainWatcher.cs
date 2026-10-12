using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Omens
{
    /// <summary>
    /// Rain Reveals the Unseen. While rain or snow falls on the current map, hostile pawns hidden from the
    /// player (Revenant, Sightstealer...) standing under open sky get a flickering ring of splashes or white
    /// dust. Flecks only: nothing becomes selectable or targetable. No save data.
    /// </summary>
    public class RainWatcher : MapComponent
    {
        readonly List<Pawn> hidden = new List<Pawn>();
        int lastScanTick = -99999;

        public RainWatcher(Map map) : base(map) { }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID) % OmensTuning.RainPulseInterval != 0) return;
            try { Pulse(); }
            catch (System.Exception ex) { Log.ErrorOnce("[Omens] RainWatcher: " + ex, 0x0e3a3); }
        }

        /// <summary>Is any rain or snow falling hard enough to show?</summary>
        public bool Falling(out float rain, out float snow)
        {
            rain = map.weatherManager.RainRate;
            snow = map.weatherManager.SnowRate;
            return rain >= OmensTuning.MinFallRate || snow >= OmensTuning.MinFallRate;
        }

        /// <summary>The hidden hostiles this pulse would outline (before the roof/fog test).</summary>
        public static bool Qualifies(Pawn p) =>
            p != null && p.Spawned && !p.Dead && p.IsHiddenFromPlayer() && p.HostileTo(Faction.OfPlayer);

        /// <summary>A pawn's cell lets the weather reach it and the player see it.</summary>
        public static bool Exposed(Pawn p) => !p.Position.Roofed(p.Map) && !p.Position.Fogged(p.Map);

        /// <summary>
        /// One pulse: returns how many flecks were made (the harness calls it directly; the tick calls it every
        /// 30 ticks). <paramref name="forceScan"/> rescans the map instead of waiting for the 240-tick rescan.
        /// </summary>
        public int Pulse(bool forceScan = false)
        {
            if (!OmensMod.Settings.rainReveals || !ModsConfig.AnomalyActive) return 0;
            if (map != Find.CurrentMap) return 0;
            if (!Falling(out float rain, out float snow)) return 0;
            int now = Find.TickManager.TicksGame;
            if (forceScan || now - lastScanTick >= OmensTuning.RainScanInterval)
            {
                lastScanTick = now;
                Rescan();
            }
            if (hidden.Count == 0) return 0;
            int made = 0;
            // snow weathers also carry rainRate 1 (they put out fires): snow wins
            bool snowing = snow >= OmensTuning.MinFallRate;
            for (int i = hidden.Count - 1; i >= 0; i--)
            {
                Pawn p = hidden[i];
                if (!Qualifies(p) || p.Map != map)
                {
                    hidden.RemoveAt(i);
                    continue;
                }
                if (!Exposed(p)) continue;
                made += snowing ? Dust(p, snow) : Splash(p, rain);
            }
            return made;
        }

        void Rescan()
        {
            hidden.Clear();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
                if (Qualifies(all[i])) hidden.Add(all[i]);
        }

        static Vector2 Radii(Pawn p)
        {
            float s = Mathf.Clamp(Mathf.Sqrt(p.BodySize), 0.7f, 1.5f);
            return new Vector2(0.40f * s, 0.60f * s);
        }

        static Vector3 OnRing(Pawn p, Vector2 r)
        {
            float a = Rand.Range(0f, Mathf.PI * 2f);
            float k = Rand.Range(0.85f, 1.1f);
            return p.DrawPos + new Vector3(Mathf.Cos(a) * r.x * k, 0f, 0.1f + Mathf.Sin(a) * r.y * k);
        }

        int Splash(Pawn p, float rate)
        {
            Vector2 r = Radii(p);
            int made = 0;
            for (int i = 0; i < OmensTuning.SplashesPerPulse; i++)
            {
                if (!Rand.Chance(rate)) continue;
                FleckCreationData d = FleckMaker.GetDataStatic(OnRing(p, r), map, OmensDefOf.Omens_RainSplash, Rand.Range(0.3f, 0.42f));
                d.rotation = Rand.Range(0f, 360f);
                map.flecks.CreateFleck(d);
                made++;
            }
            OmensStats.RainSplashes += made;
            return made;
        }

        int Dust(Pawn p, float rate)
        {
            Vector2 r = Radii(p);
            int made = 0;
            for (int i = 0; i < OmensTuning.PuffsPerPulse; i++)
            {
                if (!Rand.Chance(rate)) continue;
                FleckCreationData d = FleckMaker.GetDataStatic(OnRing(p, r), map, OmensDefOf.Omens_SnowDust, Rand.Range(0.55f, 0.8f));
                d.rotationRate = Rand.Range(-40f, 40f);
                d.velocityAngle = Rand.Range(0f, 360f);
                d.velocitySpeed = Rand.Range(0.05f, 0.2f);
                d.instanceColor = new Color(1f, 1f, 1f, 0.9f);
                map.flecks.CreateFleck(d);
                made++;
            }
            OmensStats.SnowPuffs += made;
            return made;
        }
    }
}
