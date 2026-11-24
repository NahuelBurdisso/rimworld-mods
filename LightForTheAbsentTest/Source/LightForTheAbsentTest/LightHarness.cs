using System;
using System.Collections.Generic;
using System.Linq;
using LightForTheAbsent;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LightForTheAbsentTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Builds four roofed rooms and one bed in the open, makes seven
    /// colonists with controlled relations and drives the real paths: caravans leaving and entering the map
    /// (CaravanExitMapUtility / CaravanEnterMapUtility), a kidnapping (KidnappedPawnsTracker.Kidnap), a death
    /// in a caravan (Pawn.Kill), a partner asleep through a LayDown job. Day and night are forced through
    /// AbsenceTracker.NightOverride and the tracker is polled by hand (automatic polling paused).
    /// </summary>
    public class LightHarness : GameComponent
    {
        bool active, finished;
        int ticks, phase, phaseTick;
        Map map;
        AbsenceTracker tracker;
        IntVec3 origin;
        Building_Bed bedAB, bedC, bedD, bedG;
        Pawn A, B, C, D, E, F, G;
        Caravan caravanA;

        public LightHarness(Game game) { }

        public override void FinalizeInit() => active = Kit.HarnessArmed;

        public override void GameComponentTick()
        {
            if (!active || finished) return;
            ticks++;
            try
            {
                if (phase == 0 && ticks == 60) { Setup(); Next(); }
                else if (phase == 1) WaitForSleep();
                else if (phase == 2 && ticks - phaseTick >= 40) { CheckGlowGrid(); Rest(); StartAuto(); }
                else if (phase == 3 && ticks - phaseTick >= 600) { CheckAuto(); Finish(); }
            }
            catch (Exception ex)
            {
                Kit.Check(false, "harness threw: " + ex);
                Finish();
            }
        }

        void Next() { phase++; phaseTick = ticks; }

        void Finish()
        {
            finished = true;
            AbsenceTracker.NightOverride = null;
            AbsenceTracker.AutoPoll = true;
            Kit.Summary("Light for the Absent");
        }

        // ---------------------------------------------------------------- setup

        void Setup()
        {
            map = Find.CurrentMap;
            tracker = AbsenceTracker.Instance;
            Kit.Check(tracker != null, "AbsenceTracker game component exists");
            AbsenceTracker.AutoPoll = false;
            AbsenceTracker.NightOverride = true;
            Kit.Check(LightDefOf.LightForTheAbsent_Light.category == ThingCategory.Ethereal && !LightDefOf.LightForTheAbsent_Light.selectable
                      && !LightDefOf.LightForTheAbsent_Light.isSaveable && LightDefOf.LightForTheAbsent_Light.drawerType == DrawerType.RealtimeOnly
                      && LightDefOf.LightForTheAbsent_Light.HasComp(typeof(CompGlower)),
                "light def: ethereal, not selectable, not saved, drawn realtime, has a CompGlower");

            if (!FindArea()) { Kit.Check(false, "found a clear 30x6 area for the rooms"); return; }
            bedAB = MakeRoom(origin, doubleBed: true, walls: true);
            bedC = MakeRoom(origin + new IntVec3(7, 0, 0), false, true);
            bedG = MakeRoom(origin + new IntVec3(14, 0, 0), false, true);
            bedD = MakeRoom(origin + new IntVec3(22, 0, 0), false, walls: false);

            A = MakeColonist("A", Gender.Male);
            B = MakeColonist("B", Gender.Female);
            C = MakeColonist("C", null);
            D = MakeColonist("D", null);
            E = MakeColonist("E", null);
            F = MakeColonist("F", null);
            G = MakeColonist("G", null);
            A.relations.AddDirectRelation(PawnRelationDefOf.Spouse, B);
            A.relations.AddDirectRelation(PawnRelationDefOf.Parent, E);   // E is A's parent
            A.ownership.ClaimBedIfNonMedical(bedAB);
            B.ownership.ClaimBedIfNonMedical(bedAB);
            C.ownership.ClaimBedIfNonMedical(bedC);
            D.ownership.ClaimBedIfNonMedical(bedD);
            G.ownership.ClaimBedIfNonMedical(bedG);

            Kit.Check(bedAB.OwnersForReading.Contains(A) && bedAB.OwnersForReading.Contains(B) && bedC.OwnersForReading.Contains(C)
                      && bedD.OwnersForReading.Contains(D) && bedG.OwnersForReading.Contains(G), "setup: beds claimed (A+B double, C, D, G)");
            Kit.Check(AbsenceTracker.HasRoom(bedAB) && AbsenceTracker.HasRoom(bedC) && AbsenceTracker.HasRoom(bedG) && !AbsenceTracker.HasRoom(bedD),
                "setup: A/B, C and G beds are in roofed rooms, D's bed is in the open");
            Kit.Check(LovePartnerRelationUtility.LovePartnerRelationExists(A, B) && AbsenceTracker.IsCloseFamily(E, A)
                      && !AbsenceTracker.IsCloseFamily(C, A) && !LovePartnerRelationUtility.LovePartnerRelationExists(C, A),
                "setup: B is A's spouse, E is A's parent, C is nobody to A");

            // P0: everyone home -> nothing
            tracker.ResetForTest();
            tracker.Poll();
            Kit.Check(tracker.RecordsForReading.Count == 0 && LightsOnMap() == 0, "P0 everyone home at night: no absence, no light");

            // B goes to bed (tired, so she really sleeps)
            B.needs.rest.CurLevel = 0.05f;
            Kit.Teleport(B, bedAB.Position);
            B.jobs.StartJob(JobMaker.MakeJob(JobDefOf.LayDown, bedAB), JobCondition.InterruptForced);
        }

        void WaitForSleep()
        {
            bool asleep = B.CurrentBed() == bedAB && B.jobs.curDriver != null && B.jobs.curDriver.asleep;
            if (!asleep && ticks - phaseTick < 1500) return;
            Kit.Check(asleep, $"setup: B is asleep in the double bed after {ticks - phaseTick} ticks (job {B.CurJobDef?.defName})");

            // negative: partner asleep while A is home
            tracker.Poll();
            Kit.Check(Memory(B, LightDefOf.LightForTheAbsent_SleptLightly) == null, "P3- B asleep while A is home: no 'slept lightly'");

            // ---- P1: A leaves in a caravan at night
            caravanA = Leave(A);
            Kit.Check(caravanA != null && A.IsCaravanMember() && !A.Spawned && A.MapHeld == null, "P1 A left the map in a caravan");
            Kit.Check(bedAB.OwnersForReading.Contains(A), "P1 vanilla: A still owns the double bed while away");
            tracker.Poll();
            AbsentRecord ra = tracker.RecordFor(A);
            Kit.Check(ra != null && ra.state == AbsenceState.Away && ra.bed == bedAB, $"P1 A has an Away record on the double bed ({ra})");
            AbsentLight la = LightOn(bedAB);
            Kit.Check(la != null && ra?.light == la, "P1 a light stands on A's bed at night");
            Kit.Check(la != null && la.Glower != null && la.Glower.Glows && la.AppliedGlow > 0.9f, $"P1 its CompGlower is lit (glow {la?.AppliedGlow:0.00})");
            Kit.Check(la != null && !la.def.selectable && AbsentLight.HaloMaterial(la.CurrentLevel) != null && Mathf.Approximately(la.HaloCenter.x, bedAB.TrueCenter().x),
                "P1 the halo has a material and is centred on the bed");
            Kit.Check(ra != null && ra.nightsLit == 1, $"P1 first night counted ({ra?.nightsLit})");
            Kit.Check(LightOn(bedC) == null && LightOn(bedG) == null && tracker.RecordFor(B) == null && tracker.RecordFor(C) == null,
                "P1 nobody else away: no other record, no other light");

            // ---- P3: B, asleep in the shared bed, sleeps lightly; C (not A's partner) is not affected
            Thought_Memory sl = Memory(B, LightDefOf.LightForTheAbsent_SleptLightly);
            Kit.Check(sl != null && sl.otherPawn == A && sl.MoodOffset() < 0f, $"P3 B asleep while A is away: '{sl?.LabelCap}' ({sl?.MoodOffset():+0;-0}) about A");
            Kit.Check(sl != null && sl.Description.Contains(A.LabelShort), "P3 'slept lightly' description names A");
            Kit.Check(Memory(C, LightDefOf.LightForTheAbsent_SleptLightly) == null && Memory(E, LightDefOf.LightForTheAbsent_SleptLightly) == null,
                "P3- C and E (not sharing A's bed) do not sleep lightly");

            Next();   // the glow grid is recomputed on the next frames: checked in phase 2
        }

        void CheckGlowGrid()
        {
            float atBed = map.glowGrid.GroundGlowAt(bedAB.Position);
            IntVec3 far = bedAB.Position + new IntVec3(0, 0, -2);
            float twoAway = far.InBounds(map) ? map.glowGrid.GroundGlowAt(far) : 0f;
            Kit.Check(atBed > 0.3f && atBed <= 0.5f, $"P1 real light: the bed cell is lit like a candle (glow {atBed:0.00}, Lit, not Overlit)");
            Kit.Check(twoAway < 0.3f, $"P1 two cells from the bed it is dark again (glow {twoAway:0.00})");
        }

        void Rest()
        {
            AbsentRecord ra = tracker.RecordFor(A);

            // ---- P2: day puts it out, the next dusk lights it again and counts a second night
            AbsenceTracker.NightOverride = false;
            tracker.Poll();
            Kit.Check(LightOn(bedAB) == null && ra.light == null && tracker.RecordFor(A) == ra && ra.nightsLit == 1, "P2 day: the light is out, A is still away (1 night)");
            AbsenceTracker.NightOverride = true;
            tracker.Poll();
            Kit.Check(LightOn(bedAB) != null && ra.nightsLit == 2, $"P2 next dusk: lit again, second night counted ({ra.nightsLit})");
            tracker.Poll();
            Kit.Check(ra.nightsLit == 2 && OnMap(LightDefOf.LightForTheAbsent_Light).Count == 1, "P2 polls during the same night count nothing and keep a single light");
            for (int i = 0; i < 8; i++) tracker.Poll();   // the light burns on for a while (the memory needs ~1 h of it)
            Kit.Check(ra.nightsLit == 2 && ra.litPolls >= AbsenceTracker.MinLitPollsForThought, $"P2 the light burned {ra.litPolls} polls while A was away");

            // ---- P4: A comes home at night: thought, slow fade, gone
            CaravanEnterMapUtility.Enter(caravanA, map, CaravanEnterMode.Edge);
            Kit.Check(A.Spawned && A.Map == map, "P4 A entered the home map with the caravan");
            tracker.Poll();
            Kit.Check(ra.state == AbsenceState.Returning && LightOn(bedAB) != null, $"P4 A home at night: the light is fading, not gone ({ra})");
            Thought_Memory left = Memory(A, LightDefOf.LightForTheAbsent_LeftALightOn);
            Kit.Check(left != null && left.MoodOffset() > 0f, $"P4 A got '{left?.LabelCap}' ({left?.MoodOffset():+0;-0})");
            tracker.Poll();
            AbsentLight fading = LightOn(bedAB);
            Kit.Check(fading != null && fading.TargetLevel < 1f && fading.TargetLevel > 0.9f, $"P4 one poll later it is dimmer ({fading?.TargetLevel:0.00})");
            for (int i = 0; i < 10; i++) tracker.Poll();
            Kit.Check(LightOn(bedAB) != null && LightOn(bedAB).AppliedGlow < 0.6f, $"P4 halfway: the real light is dimmer too (glow {LightOn(bedAB)?.AppliedGlow:0.00})");
            for (int i = 0; i < 14; i++) tracker.Poll();
            Kit.Check(LightOn(bedAB) == null && tracker.RecordFor(A) == null, "P4 after the fade (24 polls) the light and the record are gone");
            Kit.Check(Memory(B, LightDefOf.LightForTheAbsent_LeftALightOn) == null, "P4- B (who stayed) gets no 'left a light on'");

            // ---- P5: a trip by day never lights anything and gives no thought
            AbsenceTracker.NightOverride = false;
            Caravan cc = Leave(C);
            tracker.Poll();
            AbsentRecord rc = tracker.RecordFor(C);
            Kit.Check(rc != null && LightOn(bedC) == null, "P5 C away by day: record, no light");
            CaravanEnterMapUtility.Enter(cc, map, CaravanEnterMode.Edge);
            tracker.Poll();
            Kit.Check(tracker.RecordFor(C) == null && Memory(C, LightDefOf.LightForTheAbsent_LeftALightOn) == null && LightOn(bedC) == null,
                "P5- C back by day after no night away: no thought, record closed");

            // ---- P6: a bed in the open never gets a light
            AbsenceTracker.NightOverride = true;
            Caravan cd = Leave(D);
            tracker.Poll();
            Kit.Check(tracker.RecordFor(D) != null && LightOn(bedD) == null, "P6- D's bed is in the open: away at night, but no light");
            CaravanEnterMapUtility.Enter(cd, map, CaravanEnterMode.Edge);
            tracker.Poll();
            Kit.Check(tracker.RecordFor(D) == null && Memory(D, LightDefOf.LightForTheAbsent_LeftALightOn) == null, "P6- D back: no light was ever on, no thought");

            // ---- P7: the room is given away
            cc = Leave(C);
            tracker.Poll();
            Kit.Check(LightOn(bedC) != null, "P7 C away at night: light on");
            F.ownership.ClaimBedIfNonMedical(bedC);
            tracker.Poll();
            Kit.Check(tracker.RecordFor(C) == null && LightOn(bedC) == null, "P7 F (not C's partner) claimed the bed: the light and the record are gone");
            CaravanEnterMapUtility.Enter(cc, map, CaravanEnterMode.Edge);
            tracker.Poll();
            Kit.Check(Memory(C, LightDefOf.LightForTheAbsent_LeftALightOn) == null, "P7- C back after the room was given away: no thought");

            // ---- P8: kidnapped (vanilla clears the bed; the tracker remembers it)
            tracker.Poll();   // G home: remembered
            Pawn raider = Kit.SpawnHostile(map, G.Position);
            raider.Faction.kidnapped.Kidnap(G, raider);
            Kit.Check(!G.Spawned && !bedG.OwnersForReading.Contains(G) && PawnUtility.IsKidnappedPawn(G), "P8 G kidnapped: vanilla removed G from the bed");
            tracker.Poll();
            AbsentRecord rg = tracker.RecordFor(G);
            Kit.Check(rg != null && rg.bed == bedG && LightOn(bedG) != null, "P8 a light burns for the kidnapped G on the remembered bed");
            raider.Destroy();

            // ---- P9: A leaves again and dies far away
            Pawn companion = MakeColonist("Companion", null);   // keeps the caravan alive after A's death
            caravanA = Leave(A, companion);
            tracker.Poll();
            ra = tracker.RecordFor(A);
            Kit.Check(ra != null && ra.state == AbsenceState.Away && LightOn(bedAB) != null, "P9 A away again at night: light on");
            A.Kill(null);
            Kit.Check(A.Dead && A.MapHeld == null && !bedAB.OwnersForReading.Contains(A), "P9 A died in the caravan (vanilla removed A from the bed)");
            int outBefore = AbsenceTracker.LightsGoneOut;
            tracker.Poll();
            Kit.Check(ra.state == AbsenceState.Mourning && ra.mourningNight == 1 && LightOn(bedAB) != null && LightOn(bedAB).flicker,
                $"P9 the light now flickers (mourning, night {ra.mourningNight})");
            Kit.Check(LightOn(bedAB) != null && LightOn(bedAB).FlickerFactor(3.3f) <= 1f && Enumerable.Range(0, 2000).Any(i => LightOn(bedAB).FlickerFactor(i * 0.11f) < 0.3f),
                "P9 the flicker drops out now and then");
            Kit.Check(LightOn(bedAB).AppliedGlow > 0.5f && LightOn(bedAB).AppliedGlow < 0.7f, $"P9 the real light is dimmed while mourning (glow {LightOn(bedAB).AppliedGlow:0.00})");
            AbsenceTracker.NightOverride = false;
            tracker.Poll();
            Kit.Check(LightOn(bedAB) == null && tracker.RecordFor(A) == ra, "P9 day: out, still mourning");
            AbsenceTracker.NightOverride = true;
            tracker.Poll();
            AbsenceTracker.NightOverride = false;
            tracker.Poll();
            AbsenceTracker.NightOverride = true;
            tracker.Poll();
            Kit.Check(ra.mourningNight == 3 && LightOn(bedAB) != null, $"P9 third night: still flickering (night {ra.mourningNight})");
            for (int i = 0; i < AbsenceTracker.FinalNightPolls - 2; i++) tracker.Poll();
            Kit.Check(LightOn(bedAB) != null && Memory(B, LightDefOf.LightForTheAbsent_LightWentOut) == null, "P9 just before the end the light still burns, no grief yet");
            tracker.Poll();
            Kit.Check(LightOn(bedAB) == null && tracker.RecordFor(A) == null && AbsenceTracker.LightsGoneOut == outBefore + 1,
                "P9 two hours into the third night the light goes out for good");
            Thought_Memory wb = Memory(B, LightDefOf.LightForTheAbsent_LightWentOut);
            Thought_Memory we = Memory(E, LightDefOf.LightForTheAbsent_LightWentOut);
            Kit.Check(wb != null && wb.otherPawn == A && wb.MoodOffset() < 0f && wb.LabelCap.ToString().Contains(A.LabelShort),
                $"P9 spouse B: '{wb?.LabelCap}' ({wb?.MoodOffset():+0;-0})");
            Kit.Check(we != null && we.otherPawn == A, $"P9 parent E: '{we?.LabelCap}'");
            Kit.Check(Memory(C, LightDefOf.LightForTheAbsent_LightWentOut) == null && Memory(F, LightDefOf.LightForTheAbsent_LightWentOut) == null,
                "P9- C and F (not family) feel nothing from the light");
            Kit.Check(Messages_Contains(A.LabelShort), "P9 message: the light in A's room has gone out");
            tracker.Poll();
            Kit.Check(tracker.RecordFor(A) == null, "P9 dead A is not picked up again");

            // ---- P10: cost
            double worst = 0;
            for (int i = 0; i < 20; i++) { tracker.Poll(); worst = Math.Max(worst, AbsenceTracker.LastPollMs); }
            Kit.Check(worst < 1.0, $"P10 a poll costs {worst:0.000} ms (worst of 20, {tracker.RecordsForReading.Count} records)");

            Review();

            // G's absence is left to the game's own polls (phase 3)
        }

        /// <summary>Cases added after the code review: resurrection, saving a dead pawn, losing the room while
        /// mourning, a double bed shared with someone who is not a partner, a bed that gets minified.</summary>
        void Review()
        {
            AbsenceTracker.NightOverride = true;

            // ---- R1: F (owner of C's old bed since P7) leaves and dies far away
            Pawn companion2 = MakeColonist("Companion2", null);
            Leave(F, companion2);
            tracker.Poll();
            AbsentRecord rf = tracker.RecordFor(F);
            Kit.Check(rf != null && rf.bed == bedC && LightOn(bedC) != null, "R1 F away at night: light on F's bed");
            F.Kill(null);
            tracker.Poll();
            Kit.Check(rf != null && rf.state == AbsenceState.Mourning && LightOn(bedC) != null && LightOn(bedC).flicker, $"R1 F died far away: mourning ({rf})");

            // ---- S1: the record of a dead pawn saves both references
            string xml = Scribe.saver.DebugOutputFor(rf);
            Kit.Check(F.Destroyed && xml.Contains(F.GetUniqueLoadID()) && xml.Contains(bedC.GetUniqueLoadID()) && xml.Contains("Mourning"),
                $"S1 saving a mourning record keeps the dead pawn ({F.GetUniqueLoadID()}, destroyed {F.Destroyed}) and the bed");
            AbsentRecord rgSave = tracker.RecordFor(G);
            string xmlG = rgSave != null ? Scribe.saver.DebugOutputFor(rgSave) : "";
            Kit.Check(xmlG.Contains(G.GetUniqueLoadID()) && xmlG.Contains(bedG.GetUniqueLoadID()) && xmlG.Contains("<nightsLit>"),
                "S1 saving an away record keeps pawn, bed and counters");

            // ---- R4: the room is broken open while mourning: the light goes dark but the mourning goes on
            IntVec3 wallCell = origin + new IntVec3(12, 0, 2);   // east wall of C's room
            Building wall = wallCell.GetEdifice(map);
            Kit.Check(wall != null && wall.def == ThingDefOf.Wall, "R4 setup: found the east wall of the room");
            wall?.Destroy();
            int nightBefore = rf.mourningNight;
            tracker.Poll();
            Kit.Check(!AbsenceTracker.HasRoom(bedC) && tracker.RecordFor(F) == rf && rf.state == AbsenceState.Mourning && LightOn(bedC) == null,
                "R4 room broken open: no light, still mourning (not forgotten)");
            Thing newWall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
            newWall.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(newWall, wallCell, map);
            tracker.Poll();
            Kit.Check(AbsenceTracker.HasRoom(bedC) && LightOn(bedC) != null && LightOn(bedC).flicker && rf.mourningNight == nightBefore,
                $"R4 wall rebuilt the same night: flickering again, still night {rf.mourningNight}");

            // ---- R1: brought back to life far away: away again, steady light, nobody grieves
            int griefBefore = AbsenceTracker.GriefGiven, outBefore = AbsenceTracker.LightsGoneOut;
            bool back = !F.Discarded && ResurrectionUtility.TryResurrect(F);
            Kit.Check(back && !F.Dead && !F.Spawned, $"R1 F resurrected far away (discarded {F.Discarded})");
            tracker.Poll();
            Kit.Check(tracker.RecordFor(F) == rf && rf.state == AbsenceState.Away && LightOn(bedC) != null && !LightOn(bedC).flicker,
                $"R1 resurrected: away again, steady light ({rf})");
            for (int night = 0; night < 3; night++)
            {
                AbsenceTracker.NightOverride = false; tracker.Poll();
                AbsenceTracker.NightOverride = true;
                for (int i = 0; i < AbsenceTracker.FinalNightPolls + 2; i++) tracker.Poll();
            }
            Kit.Check(AbsenceTracker.LightsGoneOut == outBefore && AbsenceTracker.GriefGiven == griefBefore && tracker.RecordFor(F) == rf && rf.nightsLit >= 3,
                $"R1- three more nights: the light of a living F never goes out, no grief ({rf.nightsLit} nights)");

            // ---- R5: out and straight back at night: the light was on for one poll, no memory (not farmable)
            Caravan cb = Leave(B);
            tracker.Poll();
            AbsentRecord rq = tracker.RecordFor(B);
            Kit.Check(rq != null && LightOn(bedAB) != null, "R5 B steps out at night: light on");
            CaravanEnterMapUtility.Enter(cb, map, CaravanEnterMode.Edge);
            tracker.Poll();
            Kit.Check(Memory(B, LightDefOf.LightForTheAbsent_LeftALightOn) == null && rq != null && rq.litPolls < AbsenceTracker.MinLitPollsForThought,
                $"R5- B back after {rq?.litPolls} lit poll(s): no 'left a light on'");
            AbsenceTracker.NightOverride = false;
            tracker.Poll();
            AbsenceTracker.NightOverride = true;
            Kit.Check(tracker.RecordFor(B) == null && LightOn(bedAB) == null, "R5 day: the fading light is out, record closed");

            // ---- A3: a deathrest casket is not anyone's room
            if (ModsConfig.BiotechActive && ThingDefOf.DeathrestCasket != null)
            {
                var casket = (Building_Bed)ThingMaker.MakeThing(ThingDefOf.DeathrestCasket, ThingDefOf.DeathrestCasket.MadeFromStuff ? ThingDefOf.Steel : null);
                casket.SetFaction(Faction.OfPlayer);
                GenSpawn.Spawn(casket, CellFinder.StandableCellNear(origin + new IntVec3(12, 0, -3), map, 10f), map);
                Kit.Check(!AbsenceTracker.IsHomeBed(casket) && AbsenceTracker.IsHomeBed(bedAB), "A3 a deathrest casket never carries the light (a real bed does)");
                casket.Destroy();
            }

            // ---- R2: a double bed shared with someone who is not a partner
            C.ownership.ClaimBedIfNonMedical(bedAB);
            Kit.Check(bedAB.OwnersForReading.Contains(B) && bedAB.OwnersForReading.Contains(C) && !LovePartnerRelationUtility.LovePartnerRelationExists(B, C),
                "R2 setup: B and C (not partners) share the double bed");
            Leave(B);
            tracker.Poll();
            AbsentRecord rb = tracker.RecordFor(B);
            tracker.Poll();
            tracker.Poll();
            Kit.Check(rb != null && tracker.RecordFor(B) == rb && rb.coOwners.Contains(C) && LightOn(bedAB) != null && rb.nightsLit == 1,
                $"R2 B away: C was already sharing the bed, so it is not 'given away' - light stays, one record ({rb})");

            // ---- A1: the free side of the double bed goes to someone else: B still owns her side, the light stays
            C.ownership.UnclaimBed();
            D.ownership.ClaimBedIfNonMedical(bedAB);
            tracker.Poll();
            tracker.Poll();
            Kit.Check(bedAB.OwnersForReading.Contains(B) && bedAB.OwnersForReading.Contains(D) && tracker.RecordFor(B) == rb && LightOn(bedAB) != null && rb.nightsLit == 1,
                $"A1 D took the free side while B is away: same record, light on, no re-count ({rb})");

            // ---- A2: both owners away: one lamp for the bed, not two stacked
            Leave(D);
            tracker.Poll();
            AbsentRecord rd = tracker.RecordFor(D);
            int lampsOnBed = bedAB.Position.GetThingList(map).Count(t => t is AbsentLight && !t.Destroyed);
            Kit.Check(rd != null && rd.bed == bedAB && rb.light != null && rd.light == rb.light && lampsOnBed == 1,
                $"A2 B and D both away from the double bed: one shared lamp ({lampsOnBed} on the bed)");

            // ---- R3: the bed is minified (moved): the absence is forgotten
            MinifiedThing mini = bedAB.MakeMinified();
            Kit.Check(mini != null && !bedAB.Spawned && !bedAB.Destroyed, "R3 setup: the double bed was minified");
            tracker.Poll();
            Kit.Check(tracker.RecordFor(B) == null && tracker.RecordFor(D) == null && rb?.light == null,
                "R3 bed minified: B's and D's records and the lamp are gone");
            Kit.Check(LightsOnMap() == (LightOn(bedC) != null ? 1 : 0) + (LightOn(bedG) != null ? 1 : 0), $"R3 no stray lights left ({LightsOnMap()})");
            mini.Destroy();
        }

        int pollsBefore;

        /// <summary>Back to real time: the game's own GameComponentTick must poll every 250 ticks without errors.</summary>
        void StartAuto()
        {
            AbsenceTracker.NightOverride = null;
            AbsenceTracker.AutoPoll = true;
            pollsBefore = AbsenceTracker.Polls;
            Next();
        }

        void CheckAuto()
        {
            int n = AbsenceTracker.Polls - pollsBefore;
            Kit.Check(n >= 2 && n <= 3, $"automatic polling: {n} polls in 600 ticks (every 250)");
            Kit.Check(tracker.RecordFor(G) != null, "the real polls keep kidnapped G's absence (remembered bed)");
        }

        // ---------------------------------------------------------------- helpers

        Caravan Leave(params Pawn[] pawns)
        {
            foreach (Pawn p in pawns) p.jobs?.StopAll();
            return CaravanExitMapUtility.ExitMapAndCreateCaravan(pawns.ToList(), Faction.OfPlayer, map.Tile, map.Tile, PlanetTile.Invalid, sendMessage: false);
        }

        AbsentLight LightOn(Building_Bed bed)
        {
            foreach (Thing t in bed.Position.GetThingList(map))
                if (t is AbsentLight l && !l.Destroyed) return l;
            return null;
        }

        int LightsOnMap() => OnMap(LightDefOf.LightForTheAbsent_Light).Count;

        List<Thing> OnMap(ThingDef def) => map.listerThings.ThingsOfDef(def).Where(t => !t.Destroyed).ToList();

        static Thought_Memory Memory(Pawn p, ThoughtDef def) =>
            p.needs?.mood?.thoughts?.memories?.Memories.FirstOrDefault(m => m.def == def);

        static bool Messages_Contains(string text)
        {
            var live = (List<Message>)typeof(Messages).GetField("liveMessages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)?.GetValue(null);
            return live != null && live.Any(m => m.text.Contains(text) && m.text.Contains("gone out"));
        }

        Building_Bed MakeRoom(IntVec3 o, bool doubleBed, bool walls)
        {
            var outer = new CellRect(o.x, o.z, 6, 6);
            if (walls)
            {
                foreach (IntVec3 c in outer.EdgeCells)
                {
                    Thing wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
                    wall.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(wall, c, map);
                }
                foreach (IntVec3 c in outer) map.roofGrid.SetRoof(c, RoofDefOf.RoofConstructed);
            }
            ThingDef def = doubleBed ? DefDatabase<ThingDef>.GetNamed("DoubleBed") : ThingDefOf.Bed;
            var bed = (Building_Bed)ThingMaker.MakeThing(def, ThingDefOf.WoodLog);
            bed.SetFaction(Faction.OfPlayer);
            GenSpawn.Spawn(bed, o + new IntVec3(2, 0, 3), map, Rot4.South);
            return bed;
        }

        Pawn MakeColonist(string tag, Gender? gender)
        {
            Pawn p = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, canGeneratePawnRelations: false, forcedXenotype: XenotypeDefOf.Baseliner,
                developmentalStages: DevelopmentalStage.Adult, fixedGender: gender));
            foreach (Trait t in p.story.traits.allTraits.ToList()) p.story.traits.RemoveTrait(t);
            IntVec3 cell = CellFinder.StandableCellNear(origin + new IntVec3(12, 0, -3), map, 10f);
            GenSpawn.Spawn(p, cell.IsValid ? cell : origin, map);
            Log.Message($"[Test] made {tag}: {p.LabelShort} ({p.gender})");
            return p;
        }

        /// <summary>A clear 30x12 patch near the centre (rooms plus a strip south for the pawns). Strict first; then
        /// chunks, items and filth are allowed and cleared (quicktest maps are random: forests, marshes, rocks).</summary>
        bool FindArea()
        {
            float radius = Mathf.Min(GenRadial.MaxRadialPatternRadius - 1f, 90f);
            for (int pass = 0; pass < 2; pass++)
                foreach (IntVec3 cell in GenRadial.RadialCellsAround(map.Center, radius, true))
                {
                    var r = new CellRect(cell.x, cell.z - 5, 30, 12);
                    if (!r.InBounds(map)) continue;
                    bool ok = true;
                    foreach (IntVec3 x in r)
                        if (!x.Walkable(map) || x.GetTerrain(map).IsWater || x.GetEdifice(map) != null || x.Fogged(map) || x.Roofed(map)
                            || x.GetFirstPawn(map) != null || (pass == 0 && x.GetFirstItem(map) != null))
                        { ok = false; break; }
                    if (!ok) continue;
                    foreach (IntVec3 x in r)
                        foreach (Thing t in x.GetThingList(map).ToList())
                            if (t is Plant || t.def.category == ThingCategory.Item || t.def.category == ThingCategory.Filth) t.Destroy();
                    origin = cell;
                    Log.Message($"[Test] rooms at {origin} (pass {pass})");
                    return true;
                }
            return false;
        }
    }
}
