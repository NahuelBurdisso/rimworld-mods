using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    public static class EulogyUtility
    {
        public const float MournerRadius = 10f;
        public const int MournerMinOpinion = 10;

        /// <summary>The stored eulogy, or one written now (cause of death unknown) if the death was never seen.</summary>
        public static EulogyRecord GetOrCompose(Pawn deceased)
        {
            GameComponent_DueloMemory mem = GameComponent_DueloMemory.Instance;
            EulogyRecord rec = mem?.Find(deceased.ThingID);
            if (rec != null && rec.lines != null && rec.lines.Count > 0) return rec;
            rec = EulogyComposer.ComposeRecord(deceased, null, null, causeKnown: false);
            mem?.Store(rec);
            return rec;
        }

        public static bool Cares(Pawn living, Pawn deceased)
        {
            if (living.relations == null) return false;
            foreach (PawnRelationDef rel in living.GetRelations(deceased))
            {
                if (rel.familyByBloodRelation || rel == PawnRelationDefOf.Spouse || rel == PawnRelationDefOf.Fiance
                    || rel == PawnRelationDefOf.Lover || rel == PawnRelationDefOf.Bond)
                    return true;
            }
            return living.RaceProps.Humanlike && EulogyComposer.SafeOpinion(living, deceased) >= MournerMinOpinion;
        }

        /// <summary>Free colonists near the grave, awake and on their feet, who cared about the deceased.</summary>
        public static int CountMourners(Building_Grave grave, Pawn deceased)
        {
            int n = 0;
            float r2 = MournerRadius * MournerRadius;
            List<Pawn> pawns = grave.Map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (p == deceased || p.Dead || p.Downed || !p.Awake()) continue;
                if (p.Position.DistanceToSquared(grave.Position) > r2) continue;
                if (Cares(p, deceased)) n++;
            }
            return n;
        }

        /// <summary>
        /// True when Ideology is active and the colony owes this pawn a funeral (an active obligation of a
        /// funeral ritual targets the pawn or its corpse): the funeral will read the eulogy, not the burial.
        /// </summary>
        public static bool FuneralPending(Pawn deceased)
        {
            if (!ModsConfig.IdeologyActive || PreceptDefOf.Funeral == null) return false;
            FactionIdeosTracker ideos = Faction.OfPlayer?.ideos;
            if (ideos == null) return false;
            foreach (Ideo ideo in ideos.AllIdeos)
            {
                List<Precept> precepts = ideo.PreceptsListForReading;
                for (int i = 0; i < precepts.Count; i++)
                {
                    if (!(precepts[i] is Precept_Ritual ritual) || !IsFuneral(ritual) || ritual.activeObligations == null) continue;
                    foreach (RitualObligation o in ritual.activeObligations)
                    {
                        Thing t = o.targetA.Thing;
                        if (t == deceased || (t is Corpse c && c.InnerPawn == deceased)) return true;
                    }
                }
            }
            return false;
        }

        public static bool IsFuneral(Precept_Ritual ritual)
        {
            if (ritual == null) return false;
            return (PreceptDefOf.Funeral != null && ritual.def == PreceptDefOf.Funeral) || ritual.behavior is RitualBehaviorWorker_Funeral;
        }

        public static void ThrowEmbers(Map map, Vector3 center, int count, float spread)
        {
            for (int i = 0; i < count; i++)
            {
                Vector3 loc = center + new Vector3(Rand.Range(-spread, spread), 0f, Rand.Range(-spread * 0.8f, spread * 0.8f));
                if (!loc.ToIntVec3().InBounds(map)) continue;
                FleckCreationData data = FleckMaker.GetDataStatic(loc, map, DueloDefOf.Duelo_Fleck_Ember, Rand.Range(0.6f, 1.25f));
                data.velocityAngle = Rand.Range(-28f, 28f);   // 0 = up the screen
                data.velocitySpeed = Rand.Range(0.35f, 0.9f);
                data.rotationRate = Rand.Range(-60f, 60f);
                map.flecks.CreateFleck(data);
            }
        }
    }
}
