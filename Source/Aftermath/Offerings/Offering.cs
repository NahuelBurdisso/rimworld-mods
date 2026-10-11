using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Aftermath
{
    public enum OfferingState { Active, Done, Shrine }

    /// <summary>
    /// A small gift one visitor left by a wounded colonist's bed. One per (visitor, patient); repeat visits
    /// count up. When the patient is back on his feet it thanks everyone at once (a mood memory) and the
    /// gifts are taken home; if he dies they stay a few days as a little shrine.
    /// </summary>
    public class Offering : ThingWithComps
    {
        public Pawn patient;
        public string patientName;
        public int visitorId = -1;
        public string visitorName;
        public string relation;   // visitor's bond to the patient ("lover", "brother", "friend") or empty
        public int visits = 1;
        public int tick;
        public Vector2 longLat;
        public OfferingState state;
        public int stateTick;

        public void Init(Pawn patient, Pawn visitor, string relation, Map map)
        {
            this.patient = patient;
            patientName = patient.LabelShort;
            visitorId = visitor.thingIDNumber;
            visitorName = visitor.LabelShort;
            this.relation = relation ?? "";
            tick = Find.TickManager.TicksGame;
            longLat = AftermathUtil.LongLat(map);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref patient, "patient");
            Scribe_Values.Look(ref patientName, "patientName");
            Scribe_Values.Look(ref visitorId, "visitorId", -1);
            Scribe_Values.Look(ref visitorName, "visitorName");
            Scribe_Values.Look(ref relation, "relation");
            Scribe_Values.Look(ref visits, "visits", 1);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Values.Look(ref state, "state", OfferingState.Active);
            Scribe_Values.Look(ref stateTick, "stateTick");
        }

        public void SetState(OfferingState s)
        {
            state = s;
            stateTick = Find.TickManager.TicksGame;
        }

        public override string LabelNoCount =>
            patientName.NullOrEmpty() ? base.LabelNoCount : "Aftermath_OfferingLabel".Translate(base.LabelNoCount, patientName).ToString();

        public override void TickRare()
        {
            base.TickRare();
            if (!Spawned) return;
            int now = Find.TickManager.TicksGame;
            switch (state)
            {
                case OfferingState.Active:
                    if (patient == null || patient.Discarded) SetState(OfferingState.Done);
                    else if (patient.Dead) OfferingUtility.Died(patient, Map);
                    else if (!patient.Spawned || patient.Map != Map) SetState(OfferingState.Done);
                    else if (!patient.InBed() && !HealthAIUtility.ShouldSeekMedicalRest(patient)) OfferingUtility.Recovered(patient, Map);
                    break;
                case OfferingState.Done:
                    if (now - stateTick > AftermathTuning.DoneOfferingVanishTicks) Destroy(DestroyMode.Vanish);
                    break;
                case OfferingState.Shrine:
                    if (now - stateTick > AftermathTuning.ShrineVanishTicks) Destroy(DestroyMode.Vanish);
                    break;
            }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            void Line(string s) { if (sb.Length > 0) sb.AppendLine(); sb.Append(s); }
            if (state == OfferingState.Shrine) Line("Aftermath_OfferingShrine".Translate(patientName));
            string date = AftermathUtil.Date(tick, longLat);
            Line(relation.NullOrEmpty()
                ? "Aftermath_OfferingLeftBy".Translate(patientName, visitorName, date)
                : "Aftermath_OfferingLeftByRelation".Translate(patientName, visitorName, relation, date));
            if (visits > 1) Line("Aftermath_OfferingVisits".Translate(visits));
            return sb.ToString();
        }
    }

    public static class OfferingUtility
    {
        static readonly List<Offering> tmp = new List<Offering>();

        /// <summary>JobDriver.Cleanup postfix for JobDriver_VisitSickPawn: a finished visit may leave a gift.</summary>
        public static void Notify_VisitEnded(JobDriver driver, JobCondition condition)
        {
            if (!AftermathMod.Settings.bedsideOfferings) return;
            Pawn visitor = driver.pawn;
            Job job = driver.job;
            if (visitor == null || job == null || visitor.Dead || !visitor.Spawned || !visitor.RaceProps.Humanlike) return;
            if (!(job.targetA.Thing is Pawn patient) || patient == visitor || patient.Dead || !patient.Spawned
                || patient.Map != visitor.Map || !patient.IsColonist) return;
            bool counts = condition == JobCondition.Succeeded
                || (condition != JobCondition.Errored && driver.OnLastToil
                    && job.def.joyDuration - driver.ticksLeftThisToil >= AftermathTuning.MinVisitTicks);
            if (!counts) return;
            Building_Bed bed = patient.CurrentBed();
            if (bed == null) return;
            LeaveGift(visitor, patient, bed);
        }

        /// <summary>The gift this visitor leaves (or adds a visit to), or null if he leaves nothing.</summary>
        public static Offering LeaveGift(Pawn visitor, Pawn patient, Building_Bed bed)
        {
            Map map = bed.Map;
            bool lover = LovePartnerRelationUtility.LovePartnerRelationExists(visitor, patient);
            PawnRelationDef rel = patient.GetMostImportantRelation(visitor);   // what the visitor is to the patient
            bool family = rel != null && rel.familyByBloodRelation;
            int opinion = visitor.relations?.OpinionOf(patient) ?? 0;
            if (!lover && !family && opinion < AftermathTuning.MinOpinionForGift) return null;   // disliked: nothing

            Offering existing = FindGift(patient, visitor.thingIDNumber, map);
            if (existing != null)
            {
                existing.visits++;
                return existing;
            }
            ThingDef def;
            string label;
            if (lover) { def = AftermathDefOf.Aftermath_Offering_Flowers; label = rel?.GetGenderSpecificLabel(visitor) ?? "Aftermath_Lover".Translate(); }
            else if (family) { def = AftermathDefOf.Aftermath_Offering_Letter; label = rel.GetGenderSpecificLabel(visitor); }
            else if (opinion >= AftermathTuning.FriendOpinion) { def = AftermathDefOf.Aftermath_Offering_Figure; label = "Aftermath_Friend".Translate(); }
            else { def = AftermathDefOf.Aftermath_Offering_Candle; label = ""; }

            IntVec3 cell = FreeCellNear(bed);
            if (!cell.IsValid) return null;
            var o = (Offering)ThingMaker.MakeThing(def);
            o.Init(patient, visitor, label, map);
            GenSpawn.Spawn(o, cell, map);
            return o;
        }

        /// <summary>The visitor's active gift for this patient on the map, if any.</summary>
        public static Offering FindGift(Pawn patient, int visitorId, Map map)
        {
            foreach (Offering o in OfferingsFor(patient, map))
                if (o.visitorId == visitorId && o.state == OfferingState.Active) return o;
            return null;
        }

        /// <summary>Every offering for this patient on the map (shared temp list - copy before modifying the map).</summary>
        public static List<Offering> OfferingsFor(Pawn patient, Map map)
        {
            tmp.Clear();
            Collect(map, AftermathDefOf.Aftermath_Offering_Flowers, patient);
            Collect(map, AftermathDefOf.Aftermath_Offering_Letter, patient);
            Collect(map, AftermathDefOf.Aftermath_Offering_Figure, patient);
            Collect(map, AftermathDefOf.Aftermath_Offering_Candle, patient);
            return tmp;
        }

        static void Collect(Map map, ThingDef def, Pawn patient)
        {
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
                if (things[i] is Offering o && o.patient == patient) tmp.Add(o);
        }

        static IntVec3 FreeCellNear(Building_Bed bed)
        {
            Map map = bed.Map;
            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(bed))
            {
                if (!c.InBounds(map) || !c.Standable(map) || c.Fogged(map)) continue;
                if (c.GetFirstItem(map) != null || c.GetDoor(map) != null || c.GetFirstThing<Building_Bed>(map) != null) continue;
                return c;
            }
            return IntVec3.Invalid;
        }

        /// <summary>The patient is up and well: one memory by the number of givers; all his gifts are done.</summary>
        public static int Recovered(Pawn patient, Map map)
        {
            var active = new List<Offering>();
            foreach (Offering o in OfferingsFor(patient, map)) if (o.state == OfferingState.Active) active.Add(o);
            var givers = new HashSet<int>();
            foreach (Offering o in active) { givers.Add(o.visitorId); o.SetState(OfferingState.Done); }
            if (givers.Count > 0 && patient.needs?.mood != null)
            {
                int stage = StageFor(givers.Count);
                patient.needs.mood.thoughts.memories.TryGainMemory(ThoughtMaker.MakeThought(AftermathDefOf.Aftermath_CameToSeeMe, stage));
            }
            return givers.Count;
        }

        public static int StageFor(int givers) => givers >= 4 ? 2 : givers >= 2 ? 1 : 0;

        /// <summary>The patient died: his gifts stay a few days as a shrine.</summary>
        public static void Died(Pawn patient, Map map)
        {
            var all = new List<Offering>(OfferingsFor(patient, map));
            foreach (Offering o in all) if (o.state == OfferingState.Active) o.SetState(OfferingState.Shrine);
        }
    }
}
