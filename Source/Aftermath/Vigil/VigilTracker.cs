using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Aftermath
{
    /// <summary>
    /// Lights a faint amber glow on the bed of anyone fighting for their life; fades it when the danger
    /// passes, cuts it the moment the patient dies (Pawn.Kill prefix). Nothing here is saved: the glow
    /// def is isSaveable=false and the state is rebuilt by the next poll after a load.
    /// </summary>
    public class VigilTracker : MapComponent
    {
        class Vigil
        {
            public Thing glower;
            public Pawn patient;
            public int fadeStep;   // 0 = full; counts up to VigilFadeSteps once the danger has passed
        }

        readonly Dictionary<Building_Bed, Vigil> vigils = new Dictionary<Building_Bed, Vigil>();
        readonly List<Building_Bed> tmpBeds = new List<Building_Bed>();
        readonly HashSet<Building_Bed> tmpSeen = new HashSet<Building_Bed>();
        readonly List<Pawn> tmpPawns = new List<Pawn>();

        public VigilTracker(Map map) : base(map) { }

        public static VigilTracker For(Map map) => map?.GetComponent<VigilTracker>();

        public int Count => vigils.Count;

        public Thing GlowerAt(Building_Bed bed) => vigils.TryGetValue(bed, out Vigil v) ? v.glower : null;

        public int FadeStepAt(Building_Bed bed) => vigils.TryGetValue(bed, out Vigil v) ? v.fadeStep : -1;

        public override void MapComponentTick()
        {
            int interval = AftermathTuning.VigilPollInterval;
            if ((Find.TickManager.TicksGame + map.uniqueID * 7) % interval != 0) return;
            Poll();
        }

        public void Poll()
        {
            tmpSeen.Clear();
            if (AftermathMod.Settings.vigilLamps)
            {
                // AllHumanlikeSpawned returns a shared list (Verse/MapPawns.cs:187): copy it
                tmpPawns.Clear();
                tmpPawns.AddRange(map.mapPawns.AllHumanlikeSpawned);
                for (int i = 0; i < tmpPawns.Count; i++)
                {
                    Pawn p = tmpPawns[i];
                    if (p.Dead) continue;
                    Building_Bed bed = p.CurrentBed();
                    if (bed == null || !Watched(bed) || tmpSeen.Contains(bed)) continue;
                    bool danger = InDanger(p);
                    vigils.TryGetValue(bed, out Vigil v);
                    if (danger)
                    {
                        tmpSeen.Add(bed);
                        if (v == null || v.glower == null || v.glower.Destroyed || v.patient != p) { Remove(bed); Light(bed, p); }
                        else if (v.fadeStep > 0) { v.fadeStep = 0; SetColor(v); }
                    }
                    else if (v != null && v.patient == p && v.glower != null && !v.glower.Destroyed)
                    {
                        tmpSeen.Add(bed);
                        v.fadeStep++;
                        if (v.fadeStep >= AftermathTuning.VigilFadeSteps) { tmpSeen.Remove(bed); }
                        else SetColor(v);
                    }
                }
            }
            // everything not confirmed this poll goes out: patient left the bed, faded out, bed gone, setting off
            tmpBeds.Clear();
            foreach (Building_Bed bed in vigils.Keys) if (!tmpSeen.Contains(bed)) tmpBeds.Add(bed);
            foreach (Building_Bed bed in tmpBeds) Remove(bed);
        }

        static bool Watched(Building_Bed bed)
        {
            BuildingProperties b = bed.def.building;
            return bed.Spawned && bed.Faction == Faction.OfPlayer && b != null && b.bed_humanlike && b.bed_canBeMedical;
        }

        /// <summary>Bleeding out within hours, a lethal condition near its end and winning, or a life-threatening stage.</summary>
        public static bool InDanger(Pawn p)
        {
            if (p.Dead || p.health?.hediffSet == null) return false;
            if (HealthUtility.TicksUntilDeathDueToBloodLoss(p) < AftermathTuning.BleedOutDangerTicks) return true;
            List<Hediff> hediffs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hediffs.Count; i++)
            {
                Hediff h = hediffs[i];
                if (!h.Visible) continue;
                if (h.IsCurrentlyLifeThreatening) return true;
                if (!h.IsLethal || h.FullyImmune()) continue;
                if (h.Severity / h.def.lethalSeverity < AftermathTuning.LethalSeverityDanger) continue;
                bool immunizable = h is HediffWithComps hc && hc.TryGetComp<HediffComp_Immunizable>() != null;
                if (immunizable && p.health.immunity.GetImmunity(h.def) >= h.Severity) continue;   // winning the race
                return true;
            }
            return false;
        }

        void Light(Building_Bed bed, Pawn p)
        {
            Thing glower = GenSpawn.Spawn(AftermathDefOf.Aftermath_VigilGlow, bed.Position, map);
            var v = new Vigil { glower = glower, patient = p };
            vigils[bed] = v;
            SetColor(v);
        }

        static void SetColor(Vigil v)
        {
            CompGlower g = v.glower.TryGetComp<CompGlower>();
            if (g == null) return;
            ColorInt c = AftermathTuning.VigilColor;
            float f = 1f - (float)v.fadeStep / AftermathTuning.VigilFadeSteps;
            // the setter deregisters and re-registers the light (Verse/CompGlower.cs:357)
            g.GlowColor = new ColorInt((int)(c.r * f), (int)(c.g * f), (int)(c.b * f), 0);
        }

        void Remove(Building_Bed bed)
        {
            if (!vigils.TryGetValue(bed, out Vigil v)) return;
            vigils.Remove(bed);
            if (v.glower != null && !v.glower.Destroyed) v.glower.Destroy(DestroyMode.Vanish);
        }

        /// <summary>Pawn.Kill prefix: the patient is dying in this bed - the light goes out now, no fade.</summary>
        public static void Notify_PawnDying(Pawn p)
        {
            if (!p.Spawned) return;
            Building_Bed bed = p.CurrentBed();
            if (bed == null) return;
            VigilTracker t = For(p.Map);
            if (t == null || !t.vigils.TryGetValue(bed, out Vigil v) || v.patient != p) return;
            t.Remove(bed);
        }

        public override void MapRemoved()
        {
            vigils.Clear();
        }
    }
}
