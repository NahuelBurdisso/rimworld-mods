using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Grammar;

namespace OldScores
{
    /// <summary>Feature 3 — The Beast That Came Back. See DESIGN.md.</summary>
    public partial class OldScoresTracker
    {
        public static readonly Color GrudgeRed = new Color(1f, 0.32f, 0.28f);
        static readonly List<Hediff> tmpHediffs = new List<Hediff>();

        public BeastRecord GetBeast(Pawn p)
        {
            for (int i = 0; i < beasts.Count; i++)
                if (beasts[i].beast == p) return beasts[i];
            return null;
        }

        public void Notify_ColonistDamaged(Pawn victim, DamageInfo dinfo, float totalDamageDealt)
        {
            if (!OldScoresMod.Settings.beasts) return;
            if (!(dinfo.Instigator is Pawn beast) || beast == victim || beast.Dead || !beast.RaceProps.Animal || beast.Faction != null) return;
            if (!victim.IsColonist) return;
            if (totalDamageDealt < OldScoresTuning.BeastMinDamage && !victim.Downed && !victim.Dead) return;

            BeastRecord rec = GetBeast(beast);
            if (rec == null)
            {
                rec = new BeastRecord
                {
                    beast = beast,
                    stage = BeastStage.Marked,
                    kindLabel = beast.KindLabel,
                    firstBloodTickAbs = Find.TickManager.TicksAbs,
                    firstBloodLongLat = beast.MapHeld != null ? Find.WorldGrid.LongLatOf(beast.MapHeld.Tile) : Vector2.zero,
                };
                beasts.Add(rec);
            }
            if (!rec.victimIds.Contains(victim.thingIDNumber))
            {
                rec.victimIds.Add(victim.thingIDNumber);
                rec.victimNames.Add(victim.LabelShort);
            }
        }

        void Notify_BeastExitedMap(Pawn beast, Map fromMap)
        {
            BeastRecord rec = GetBeast(beast);
            if (rec == null) return;
            if (!OldScoresMod.Settings.beasts)
            {
                beasts.Remove(rec);
                Unpin(beast);
                return;
            }
            if (beast.Dead || beast.Spawned || !beast.IsWorldPawn()) return;
            if (rec.stage == BeastStage.Marked)
            {
                if (beast.Name == null)
                {
                    rec.nickname = GenerateBeastName(beast, rec);
                    beast.Name = new NameSingle(rec.nickname);
                }
                else rec.nickname = beast.Name.ToStringShort;
                MakeInjuriesPermanent(beast);
            }
            rec.stage = BeastStage.Away;
            Pin(beast);
            QueueReturn(rec, fromMap, OldScoresTuning.BeastReturnDelay.RandomInRange);
            Log.Message($"[Old Scores] {rec.nickname} ({rec.kindLabel}) escaped; returns in {(rec.fireTick - Find.TickManager.TicksGame) / (float)GenDate.TicksPerDay:0.#} days");
        }

        public static void MakeInjuriesPermanent(Pawn p)
        {
            tmpHediffs.Clear();
            tmpHediffs.AddRange(p.health.hediffSet.hediffs);
            foreach (Hediff h in tmpHediffs)
            {
                if (!(h is Hediff_Injury)) continue;
                HediffComp_GetsPermanent comp = h.TryGetComp<HediffComp_GetsPermanent>();
                if (comp != null) comp.IsPermanent = true;
            }
            tmpHediffs.Clear();
        }

        public static string GenerateBeastName(Pawn beast, BeastRecord rec)
        {
            var request = new GrammarRequest();
            request.Includes.Add(OldScoresDefOf.OldScores_Namer_Beast);
            Dictionary<string, string> constants = request.Constants;
            request.Rules.AddRange(GrammarUtility.RulesForPawn("ANIMAL", beast, constants, addRelationInfoSymbol: false, addTags: false));
            string victim = rec.victimNames.Count > 0 ? rec.victimNames[0] : "OldScores_Beast_SomeoneFallback".Translate().ToString();
            request.Rules.Add(new Rule_String("VICTIM_name", victim));
            constants["hasVictim"] = rec.victimNames.Count > 0 ? "true" : "false";
            string result = null;
            try { result = GrammarResolver.Resolve("r_name", request, "OldScores beast name"); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] beast name grammar failed: " + ex, 0x0D5C02); }
            if (result.NullOrEmpty() || result.Contains("[") || result.Contains("Error")) result = "OldScores_Beast_NameFallback".Translate();
            return result.Trim();
        }

        void QueueReturn(BeastRecord rec, Map preferred, int delay)
        {
            rec.fireTick = Find.TickManager.TicksGame + delay;
            Map target = preferred != null && preferred.IsPlayerHome ? preferred : Find.AnyPlayerHomeMap;
            if (target == null) return;   // re-queued by the cleanup pass once there is a home again
            var parms = new IncidentParms { target = target, forced = true };
            Find.Storyteller.incidentQueue.Add(OldScoresDefOf.OldScores_BeastReturns, rec.fireTick, parms, GenDate.TicksPerDay);
        }

        /// <summary>The Away beast whose return is due soonest (fireTick reached), or null.</summary>
        public BeastRecord NextDueBeast()
        {
            int now = Find.TickManager.TicksGame;
            BeastRecord best = null;
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord r = beasts[i];
                Pawn b = r.beast;
                if (r.stage != BeastStage.Away || r.fireTick > now || b == null || b.Dead || b.Destroyed || b.Spawned || !b.IsWorldPawn()) continue;
                if (best == null || r.fireTick < best.fireTick) best = r;
            }
            return best;
        }

        /// <summary>For tests and debugging: make this beast's return due now.</summary>
        public void ForceBeastDue(Pawn beast)
        {
            BeastRecord rec = GetBeast(beast);
            if (rec != null && rec.stage == BeastStage.Away) rec.fireTick = Find.TickManager.TicksGame;
        }

        /// <summary>Spawns the beast at the cell, angrier and tougher. Called by IncidentWorker_BeastReturns.</summary>
        public Pawn BringBack(BeastRecord rec, Map map, IntVec3 cell)
        {
            Pawn beast = rec.beast;
            Rot4 rot = Rot4.FromAngleFlat((map.Center - cell).AngleFlat);
            GenSpawn.Spawn(beast, cell, map, rot);
            if (!beast.health.hediffSet.HasHediff(OldScoresDefOf.OldScores_Veteran))
                beast.health.AddHediff(OldScoresDefOf.OldScores_Veteran);
            beast.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent, forced: true, forceWake: true);
            beast.mindState.exitMapAfterTick = Find.TickManager.TicksGame + OldScoresTuning.BeastStayTicks.RandomInRange;
            rec.stage = BeastStage.Returned;
            rec.fireTick = -1;
            Log.Message($"[Old Scores] {beast.LabelShort} came back to {map}");
            return beast;
        }

        void Notify_BeastDied(Pawn beast, KillContext ctx)
        {
            BeastRecord rec = GetBeast(beast);
            if (rec == null) return;
            beasts.Remove(rec);
            Unpin(beast);
            if (rec.stage != BeastStage.Returned || !OldScoresMod.Settings.beasts || ctx?.map == null) return;

            Pawn killer = ctx.killer;
            Thing trophy = ThingMaker.MakeThing(OldScoresDefOf.OldScores_BeastTrophy);
            CompBeastTrophy comp = trophy.TryGetComp<CompBeastTrophy>();
            if (comp != null)
            {
                comp.beastName = rec.nickname ?? beast.LabelShort;
                comp.kindLabel = rec.kindLabel ?? beast.KindLabel;
                comp.victims = rec.VictimList;
                comp.firstBloodTickAbs = rec.firstBloodTickAbs;
                comp.longLat = rec.firstBloodLongLat;
                comp.killerName = killer?.LabelShort;
                comp.killTickAbs = Find.TickManager.TicksAbs;
            }
            GenPlace.TryPlaceThing(trophy, ctx.position, ctx.map, ThingPlaceMode.Near);
            if (killer != null && !killer.Dead)
                killer.needs?.mood?.thoughts?.memories?.TryGainMemory(OldScoresDefOf.OldScores_SettledOldScore);
            Messages.Message("OldScores_Beast_KilledMessage".Translate(comp?.beastName ?? beast.LabelShort, killer?.LabelShort ?? "?"),
                trophy, MessageTypeDefOf.PositiveEvent);
            Log.Message($"[Old Scores] {rec.nickname} killed by {killer?.LabelShort ?? "nobody"}; trophy dropped");
        }

        public string BeastLabel(BeastRecord rec) => "OldScores_Beast_Label".Translate(rec.VictimList);

        void CleanupBeasts()
        {
            int now = Find.TickManager.TicksGame;
            int queued = 0, away = 0;
            foreach (object o in Find.Storyteller.incidentQueue)
                if (o is QueuedIncident qi && qi.FiringIncident?.def == OldScoresDefOf.OldScores_BeastReturns) queued++;
            for (int i = beasts.Count - 1; i >= 0; i--)
            {
                BeastRecord rec = beasts[i];
                Pawn b = rec.beast;
                if (b == null || b.Dead || b.Discarded || b.Faction != null)
                {
                    beasts.RemoveAt(i);
                    Unpin(b);
                    continue;
                }
                if (rec.stage == BeastStage.Away) away++;
            }
            if (queued >= away) return;
            // an Away beast whose return could not fire (no home map, retries exhausted): try again in a day
            for (int i = 0; i < beasts.Count && queued < away; i++)
            {
                BeastRecord rec = beasts[i];
                if (rec.stage != BeastStage.Away || now <= rec.fireTick + 2 * GenDate.TicksPerDay) continue;
                QueueReturn(rec, null, GenDate.TicksPerDay);
                queued++;
            }
        }
    }

    public class IncidentWorker_BeastReturns : IncidentWorker
    {
        protected override bool CanFireNowSub(IncidentParms parms)
        {
            if (!OldScoresMod.Settings.beasts || !(parms.target is Map map) || !map.IsPlayerHome) return false;
            if (OldScoresTracker.Instance?.NextDueBeast() == null) return false;
            return RCellFinder.TryFindRandomPawnEntryCell(out _, map, CellFinder.EdgeRoadChance_Animal);
        }

        protected override bool TryExecuteWorker(IncidentParms parms)
        {
            if (!OldScoresMod.Settings.beasts || !(parms.target is Map map)) return false;
            OldScoresTracker tracker = OldScoresTracker.Instance;
            BeastRecord rec = tracker?.NextDueBeast();
            if (rec == null) return false;
            IntVec3 cell = parms.spawnCenter;
            if ((!cell.IsValid || !cell.InBounds(map)) && !RCellFinder.TryFindRandomPawnEntryCell(out cell, map, CellFinder.EdgeRoadChance_Animal))
                return false;
            Pawn beast = tracker.BringBack(rec, map, cell);
            SendStandardLetter("OldScores_Beast_LetterLabel".Translate(beast.LabelShort),
                "OldScores_Beast_LetterText".Translate(beast.LabelShort, rec.kindLabel, rec.VictimList),
                LetterDefOf.ThreatSmall, parms, beast);
            return true;
        }
    }

    public class CompProperties_BeastTrophy : CompProperties
    {
        public CompProperties_BeastTrophy() { compClass = typeof(CompBeastTrophy); }
    }

    /// <summary>The history of the beast, written on the trophy.</summary>
    public class CompBeastTrophy : ThingComp
    {
        public string beastName, kindLabel, victims, killerName;
        public int firstBloodTickAbs, killTickAbs;
        public Vector2 longLat;

        public override string TransformLabel(string label) =>
            beastName.NullOrEmpty() ? label : label + " (" + beastName + ")";

        public override bool AllowStackWith(Thing other) => false;

        public string History()
        {
            if (beastName.NullOrEmpty()) return null;
            string first = GenDate.DateFullStringAt(firstBloodTickAbs, longLat);
            string killed = GenDate.DateFullStringAt(killTickAbs, longLat);
            return killerName.NullOrEmpty()
                ? "OldScores_Trophy_HistoryNoKiller".Translate(beastName, kindLabel, victims, first, killed)
                : "OldScores_Trophy_History".Translate(beastName, kindLabel, victims, first, killerName, killed);
        }

        public override string CompInspectStringExtra() => History();

        public override string GetDescriptionPart() => History();

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref beastName, "beastName");
            Scribe_Values.Look(ref kindLabel, "kindLabel");
            Scribe_Values.Look(ref victims, "victims");
            Scribe_Values.Look(ref killerName, "killerName");
            Scribe_Values.Look(ref firstBloodTickAbs, "firstBloodTickAbs");
            Scribe_Values.Look(ref killTickAbs, "killTickAbs");
            Scribe_Values.Look(ref longLat, "longLat");
        }
    }
}
