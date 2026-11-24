using System;
using System.Collections.Generic;
using System.Diagnostics;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace LightForTheAbsent
{
    /// <summary>
    /// Polls every 250 ticks: who owns a bed on a home map but is away (caravan, pods, shuttle, quest map,
    /// kidnapped), and drives each absence's light through Away -> Returning or Away -> Mourning. No Harmony:
    /// a free colonist who leaves keeps the bed (Verse/Pawn.cs ExitMap), and the cases that clear it
    /// (kidnap, gravship landing) are covered by the beds remembered while the pawn was home.
    /// </summary>
    public class AbsenceTracker : GameComponent
    {
        public const int PollInterval = 250;
        public const int PollOffset = 17;
        public const int FadePolls = 24;          // 6000 ticks, ~2.4 in-game hours
        public const int MourningNights = 3;
        public const int FinalNightPolls = 20;    // 5000 ticks into the last night
        public const float DuskSunGlow = 0.5f;
        public const int MinLitPollsForThought = 10;   // 2500 ticks, ~1 in-game hour of light while away

        // test hooks
        public static bool? NightOverride;
        public static bool AutoPoll = true;
        public static int Polls, ReturnThoughts, SleptLightlyGiven, LightsGoneOut, GriefGiven;
        public static double LastPollMs;

        List<AbsentRecord> records = new List<AbsentRecord>();
        /// <summary>Bed of each eligible pawn last seen home. In memory only: rebuilt by the first poll.</summary>
        readonly Dictionary<Pawn, Building_Bed> known = new Dictionary<Pawn, Building_Bed>();
        /// <summary>One lamp per bed, shared by every absence on it. Never saved: lamps are re-lit by the next poll.</summary>
        readonly Dictionary<Building_Bed, AbsentLight> lamps = new Dictionary<Building_Bed, AbsentLight>();
        /// <summary>Pawns whose update threw: left alone until the game is reloaded, so a broken record is not
        /// re-created and dropped on every poll with the error logged only once.</summary>
        readonly HashSet<Pawn> failed = new HashSet<Pawn>();
        bool seeded;

        static readonly HashSet<Pawn> seenOwners = new HashSet<Pawn>();
        static readonly List<Pawn> tmpPawns = new List<Pawn>();
        static readonly Dictionary<Building_Bed, (float level, bool flicker)> tmpWant = new Dictionary<Building_Bed, (float, bool)>();
        static readonly List<Building_Bed> tmpBeds = new List<Building_Bed>();
        static readonly Stopwatch watch = new Stopwatch();

        static PawnRelationDef[] griefRelations;
        static PawnRelationDef[] GriefRelations => griefRelations ??= new[]
        {
            PawnRelationDefOf.Spouse, PawnRelationDefOf.Lover, PawnRelationDefOf.Fiance,
            PawnRelationDefOf.Parent, PawnRelationDefOf.Child, PawnRelationDefOf.Sibling, PawnRelationDefOf.HalfSibling
        };

        public AbsenceTracker(Game game) { }

        /// <summary>Static test hooks must never leak from one game into the next.</summary>
        public override void FinalizeInit()
        {
            NightOverride = null;
            AutoPoll = true;
            seenOwners.Clear();
            tmpPawns.Clear();
            tmpWant.Clear();
            tmpBeds.Clear();
        }

        public static AbsenceTracker Instance => Current.Game?.GetComponent<AbsenceTracker>();

        public List<AbsentRecord> RecordsForReading => records;

        public AbsentRecord RecordFor(Pawn p)
        {
            for (int i = 0; i < records.Count; i++)
                if (records[i].pawn == p) return records[i];
            return null;
        }

        public override void GameComponentTick()
        {
            if (!AutoPoll) return;
            // the first tick after a load polls at once: `known` is not saved, and a kidnapper already carrying
            // a colonist off the map must find the bed remembered before he leaves
            if (!seeded || Find.TickManager.TicksGame % PollInterval == PollOffset)
            {
                seeded = true;
                Poll();
            }
        }

        public void Poll()
        {
            watch.Restart();
            try { Detect(); }
            catch (Exception ex) { Log.ErrorOnce("[Light for the Absent] detection failed: " + ex, 0x4C4642); }
            try
            {
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    AbsentRecord r = records[i];
                    r.wantOn = false;
                    bool keep;
                    try { keep = Update(r); }
                    catch (Exception ex)
                    {
                        Log.ErrorOnce("[Light for the Absent] update failed for " + r + ": " + ex, 0x4C4641);
                        if (r.pawn != null) failed.Add(r.pawn);
                        keep = false;
                    }
                    if (!keep)
                    {
                        r.Extinguish();
                        records.RemoveAt(i);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.ErrorOnce("[Light for the Absent] poll failed: " + ex, 0x4C4640);
            }
            try { ApplyLamps(); }
            catch (Exception ex) { Log.ErrorOnce("[Light for the Absent] lamps failed: " + ex, 0x4C4643); }
            watch.Stop();
            LastPollMs = watch.Elapsed.TotalMilliseconds;
            Polls++;
        }

        // ---------------------------------------------------------------- detection

        void Detect()
        {
            seenOwners.Clear();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                if (!map.IsPlayerHome) continue;
                // the Bed group holds beds only; AllBuildingsColonistOfClass would walk every wall of the colony
                List<Thing> beds = map.listerThings.ThingsInGroup(ThingRequestGroup.Bed);
                for (int b = 0; b < beds.Count; b++)
                {
                    if (!(beds[b] is Building_Bed bed) || bed.Faction != Faction.OfPlayer || !IsHomeBed(bed)) continue;
                    List<Pawn> owners = bed.OwnersForReading;
                    for (int i = 0; i < owners.Count; i++)
                    {
                        Pawn p = owners[i];
                        if (p == null || p.Dead || !Eligible(p) || failed.Contains(p)) continue;
                        seenOwners.Add(p);
                        if (IsHome(p))
                            known[p] = bed;
                        else if (!InGravship(p) && RecordFor(p) == null)
                            records.Add(new AbsentRecord(p, bed));
                    }
                }
            }

            // pawns who were home with a bed and lost it without coming back: kidnapped, or a gravship landed
            tmpPawns.Clear();
            foreach (KeyValuePair<Pawn, Building_Bed> kv in known)
                if (!seenOwners.Contains(kv.Key)) tmpPawns.Add(kv.Key);
            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn p = tmpPawns[i];
                Building_Bed bed = known[p];
                known.Remove(p);
                if (p.Dead || p.Destroyed || !Eligible(p) || IsHome(p) || InGravship(p) || failed.Contains(p)) continue;
                if (bed == null || bed.Destroyed || RecordFor(p) != null) continue;
                records.Add(new AbsentRecord(p, bed));
            }
        }

        public static bool IsHomeBed(Building_Bed bed) =>
            bed.Spawned && bed.def.building != null && bed.def.building.bed_humanlike && bed.def.building.bed_countsForBedroomOrBarracks
            && !bed.Medical && !bed.ForPrisoners;   // countsForBedroomOrBarracks: not a deathrest casket

        /// <summary>A colonist whose room would wait for them. Not for dead pawns.</summary>
        public static bool Eligible(Pawn p) =>
            p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer && p.IsColonist && !p.IsQuestLodger() && !p.IsMutant && p.needs?.mood != null;

        /// <summary>Still a colonist whose absence we keep following. Looser than <see cref="Eligible"/>: a slave having
        /// a mental break far away is not "secure" (IsColonist false) but is still ours.</summary>
        public static bool StillOurs(Pawn p) =>
            p.RaceProps.Humanlike && p.Faction == Faction.OfPlayer && !p.IsPrisoner && !p.IsQuestLodger() && !p.IsMutant && p.needs?.mood != null;

        /// <summary>On a home map (spawned, or held in a casket / transporter there). A pocket map (undercave,
        /// labyrinth) counts as the map it hangs from: someone down in the caves is not away.</summary>
        public static bool IsHome(Pawn p)
        {
            Map m = p.MapHeld;
            for (int i = 0; i < 8 && m != null && m.IsPocketMap; i++)
                m = m.PocketMapParent?.sourceMap;
            return m != null && m.IsPlayerHome;
        }

        /// <summary>Aboard the gravship in flight: travelling with their own bed, not away from it.</summary>
        public static bool InGravship(Pawn p)
        {
            Gravship g = Find.CurrentGravship;
            if (g == null) return false;
            foreach (Pawn x in g.Pawns)
                if (x == p) return true;
            return false;
        }

        public static bool IsNight(Map map) => NightOverride ?? GenCelestial.CurCelestialSunGlow(map) < DuskSunGlow;

        public static bool HasRoom(Building_Bed bed)
        {
            Room room = bed.GetRoom();
            return room != null && !room.PsychologicallyOutdoors && !room.TouchesMapEdge;
        }

        /// <summary>Someone other than the absent pawn and their partner now owns the bed: the room was given away.</summary>
        static bool GivenAway(AbsentRecord r)
        {
            List<Pawn> owners = r.bed.OwnersForReading;
            if (owners.Contains(r.pawn)) return false;   // still theirs: someone took the free side of a double bed
            for (int i = 0; i < owners.Count; i++)
            {
                Pawn o = owners[i];
                if (o == null || o == r.pawn || r.coOwners.Contains(o)) continue;   // shared the bed already (double bed, bunks)
                if (!LovePartnerRelationUtility.LovePartnerRelationExists(o, r.pawn)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- per absence

        /// <summary>False = forget this record (its light goes out).</summary>
        bool Update(AbsentRecord r)
        {
            Pawn p = r.pawn;
            Building_Bed bed = r.bed;
            if (p == null || p.Discarded || bed == null || bed.Destroyed) return false;   // a discarded pawn can no longer be saved by reference
            if (!bed.Spawned)
            {
                // flying with the gravship: wait for it to land. Anything else (minified, its map discarded): the room is gone
                if (!BedInGravship(bed)) return false;
                r.SetLight(false, 0f, false);
                return true;
            }
            if (GivenAway(r)) return false;
            bool night = IsNight(bed.Map);
            bool room = HasRoom(bed);

            switch (r.state)
            {
                case AbsenceState.Away:
                    if (p.Dead)
                    {
                        if (IsHome(p)) return false;   // brought home and died there: vanilla's grief only
                        r.state = AbsenceState.Mourning;
                        r.mourningNight = 0;
                        r.mourningLitPolls = 0;
                        r.nightLastPoll = false;
                        return UpdateMourning(r, night, room);
                    }
                    if (!StillOurs(p) || InGravship(p)) return false;
                    if (IsHome(p)) return Return(r, night && room);
                    {
                        bool lit = night && room;
                        if (lit) r.litPolls++;
                        if (!night) r.litTonight = false;
                        if (lit && !r.litTonight)
                        {
                            r.nightsLit++;   // once per night, even if the room is lost and found again in between
                            r.litTonight = true;
                        }
                        r.SetLight(lit, 1f, false);
                        SleepLightly(r);
                    }
                    return true;

                case AbsenceState.Returning:
                    if (p.Dead || !StillOurs(p)) return false;
                    if (!IsHome(p))
                    {
                        // gone again before the light was out: start over
                        r.state = AbsenceState.Away;
                        r.nightsLit = 0;
                        r.litPolls = 0;
                        r.litTonight = false;
                        return Update(r);
                    }
                    if (!night || !room || --r.fadePolls <= 0) return false;
                    r.SetLight(true, (float)r.fadePolls / FadePolls, false);
                    return true;

                case AbsenceState.Mourning:
                    if (!p.Dead)
                    {
                        // resurrected far away: no longer mourned, just away again (the nights already lit still count)
                        r.state = AbsenceState.Away;
                        r.litTonight = r.LightOn;
                        return Update(r);
                    }
                    if (IsHome(p)) return false;   // the body was brought home: vanilla's grief takes it from here
                    return UpdateMourning(r, night, room);
            }
            return false;
        }

        static bool BedInGravship(Building_Bed bed)
        {
            Gravship g = Find.CurrentGravship;
            if (g == null) return false;
            foreach (Thing t in g.Things)
                if (t == bed) return true;
            return false;
        }

        bool Return(AbsentRecord r, bool litNow)
        {
            Pawn p = r.pawn;
            if (r.nightsLit >= 1 && r.litPolls >= MinLitPollsForThought && LightForTheAbsentMod.Settings.moodlets && p.needs?.mood != null)
            {
                p.needs.mood.thoughts.memories.TryGainMemory(LightDefOf.LightForTheAbsent_LeftALightOn);
                ReturnThoughts++;
            }
            if (!litNow || !r.LightOn) return false;
            r.state = AbsenceState.Returning;
            r.fadePolls = FadePolls;
            r.SetLight(true, 1f, false);
            return true;
        }

        /// <summary>Nights are counted from the sky, not from the light: losing the room for a while only dims it.</summary>
        bool UpdateMourning(AbsentRecord r, bool night, bool room)
        {
            if (night && !r.nightLastPoll)
            {
                r.mourningNight++;
                r.mourningLitPolls = 0;
            }
            else if (!night && r.nightLastPoll && r.mourningNight >= MourningNights)
            {
                // dawn after the last night came before the two hours were up (short nights near the poles)
                r.nightLastPoll = false;
                if (r.mourningLitPolls > 0) GoOut(r);
                return false;   // never lit on the last night (no room): it ends quietly
            }
            r.nightLastPoll = night;
            bool lit = night && room;
            if (lit)
            {
                r.mourningLitPolls++;
                if (r.mourningNight >= MourningNights && r.mourningLitPolls >= FinalNightPolls)
                {
                    GoOut(r);
                    return false;
                }
            }
            r.SetLight(lit, 1f, true);
            return true;
        }

        void SleepLightly(AbsentRecord r)
        {
            if (!LightForTheAbsentMod.Settings.moodlets) return;
            List<Pawn> owners = r.bed.OwnersForReading;
            for (int i = 0; i < owners.Count; i++)
            {
                Pawn o = owners[i];
                if (o == null || o == r.pawn || !o.Spawned || o.needs?.mood == null) continue;
                if (!LovePartnerRelationUtility.LovePartnerRelationExists(o, r.pawn)) continue;
                if (o.CurrentBed() != r.bed || o.jobs?.curDriver == null || !o.jobs.curDriver.asleep) continue;
                o.needs.mood.thoughts.memories.TryGainMemory(LightDefOf.LightForTheAbsent_SleptLightly, r.pawn);
                SleptLightlyGiven++;
            }
        }

        void GoOut(AbsentRecord r)
        {
            Pawn dead = r.pawn;
            Building_Bed bed = r.bed;
            Map map = bed.Map;
            LightsGoneOut++;
            if (LightForTheAbsentMod.Settings.message)
                Messages.Message("LightForTheAbsent_LightWentOutMessage".Translate(dead.Named("PAWN")), new LookTargets(bed), MessageTypeDefOf.NeutralEvent);
            if (!LightForTheAbsentMod.Settings.moodlets) return;
            tmpPawns.Clear();
            tmpPawns.AddRange(map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer));   // colonists and slaves
            for (int i = 0; i < tmpPawns.Count; i++)
            {
                Pawn c = tmpPawns[i];
                if (c == dead || c.Dead || !c.RaceProps.Humanlike || c.IsPrisoner || c.needs?.mood == null) continue;
                if (!IsCloseFamily(c, dead)) continue;
                c.needs.mood.thoughts.memories.TryGainMemory(LightDefOf.LightForTheAbsent_LightWentOut, dead);
                GriefGiven++;
            }
            tmpPawns.Clear();
        }

        /// <summary>Partner, parent, child or sibling of the dead.</summary>
        public static bool IsCloseFamily(Pawn living, Pawn dead)
        {
            PawnRelationDef[] defs = GriefRelations;
            for (int i = 0; i < defs.Length; i++)
                if (defs[i] != null && defs[i].Worker.InRelation(dead, living)) return true;
            return false;
        }

        // ---------------------------------------------------------------- lamps

        /// <summary>One lamp per bed: as bright as the brightest absence on it, flickering only if everyone it burns
        /// for is dead. A new lamp gets its colour before it is spawned, so its glower registers once.</summary>
        void ApplyLamps()
        {
            tmpWant.Clear();
            for (int i = 0; i < records.Count; i++)
            {
                AbsentRecord r = records[i];
                if (!r.wantOn || r.bed == null || !r.bed.Spawned) continue;
                tmpWant[r.bed] = tmpWant.TryGetValue(r.bed, out var w)
                    ? (Math.Max(w.level, r.wantLevel), w.flicker && r.wantFlicker)
                    : (r.wantLevel, r.wantFlicker);
            }

            tmpBeds.Clear();
            foreach (KeyValuePair<Building_Bed, AbsentLight> kv in lamps)
                if (!tmpWant.ContainsKey(kv.Key)) tmpBeds.Add(kv.Key);
            for (int i = 0; i < tmpBeds.Count; i++)
            {
                PutOut(lamps[tmpBeds[i]]);
                lamps.Remove(tmpBeds[i]);
            }
            tmpBeds.Clear();

            foreach (KeyValuePair<Building_Bed, (float level, bool flicker)> kv in tmpWant)
            {
                Building_Bed bed = kv.Key;
                lamps.TryGetValue(bed, out AbsentLight lamp);
                if (lamp != null && (lamp.Destroyed || !lamp.Spawned || lamp.Map != bed.Map || lamp.Position != bed.Position))
                {
                    PutOut(lamp);   // e.g. the bed flew away on a gravship: ethereal things stay behind
                    lamp = null;
                }
                if (lamp == null)
                {
                    lamp = (AbsentLight)ThingMaker.MakeThing(LightDefOf.LightForTheAbsent_Light);
                    lamp.Init(bed);
                    lamp.SetLevel(kv.Value.level, kv.Value.flicker);
                    GenSpawn.Spawn(lamp, bed.Position, bed.Map);
                    lamps[bed] = lamp;
                }
                else lamp.SetLevel(kv.Value.level, kv.Value.flicker);
            }
            tmpWant.Clear();

            for (int i = 0; i < records.Count; i++)
            {
                AbsentRecord r = records[i];
                r.light = r.wantOn && r.bed != null && lamps.TryGetValue(r.bed, out AbsentLight l) ? l : null;
            }
        }

        static void PutOut(AbsentLight lamp)
        {
            if (lamp != null && !lamp.Destroyed) lamp.Destroy();
        }

        // ---------------------------------------------------------------- save

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<AbsentRecord>();
                records.RemoveAll(r => r == null || r.pawn == null || r.bed == null);
            }
        }

        /// <summary>Test helper: forget everything and put every light out.</summary>
        public void ResetForTest()
        {
            for (int i = 0; i < records.Count; i++) records[i].Extinguish();
            records.Clear();
            known.Clear();
            failed.Clear();
            foreach (AbsentLight lamp in lamps.Values) PutOut(lamp);
            lamps.Clear();
        }
    }
}
