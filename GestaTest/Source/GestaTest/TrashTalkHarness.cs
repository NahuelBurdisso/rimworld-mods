using System.Collections.Generic;
using System.Linq;
using Gesta;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI.Group;

namespace GestaTest
{
    /// <summary>
    /// Trash Talk with real raids, driven through the real scan, exchange, reply and break paths.
    /// Runs after the champion harness: tick 3000 (A), 3130 (B), live stage around 3250-3500
    /// (run: --ticks 4500). Rules checked: DESIGN.md "Feature 12".
    /// </summary>
    public class TrashTalkHarness : GameComponent
    {
        const int StageA = 3000, StageB = 3130, StageC = 3290;
        bool active, done, doneA, doneB, doneC;
        int ticks, liveScanTick = -1;
        bool savedOn, savedBubbles;
        Map map;
        IntVec3 spotA, spotB, dir;
        Pawn talker, live;
        Lord raid1, raid2, staged;
        TrashTalkLordState state1;
        TauntRecord first;

        public TrashTalkHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            // a raid that sees downed colonists or loot switches to kidnapping or stealing after a quiet
            // spell (vanilla); the test raids must stay in their assault, as a harmed raid would
            int now = Find.TickManager.TicksGame;
            if (raid1 != null) raid1.lastPawnHarmTick = now;
            if (raid2 != null) raid2.lastPawnHarmTick = now;
            try
            {
                if (ticks == StageA && !doneA) { doneA = true; Begin(); RunA(); }
                else if (ticks == StageB && !doneB) { doneB = true; RunB(); }
                else if (ticks >= StageC && !doneC) RunC();
            }
            catch (System.Exception ex)
            {
                Kit.Check(false, "trash talk harness threw: " + ex);
                Finish();
            }
        }

        // ------------------------------------------------------------------ helpers

        void Begin()
        {
            map = Find.CurrentMap;
            savedOn = GestaMod.Settings.trashTalk;
            savedBubbles = GestaMod.Settings.trashTalkBubbles;
            GestaMod.Settings.trashTalk = true;
            GestaMod.Settings.trashTalkBubbles = true;
            KillHostiles();
            TrashTalk.Instance.DebugResetCooldowns();
        }

        void Finish()
        {
            TrashTalk.DebugChance = null;
            TrashTalk.DebugRoll = null;
            GestaMod.Settings.trashTalk = savedOn;
            GestaMod.Settings.trashTalkBubbles = savedBubbles;
            if (talker?.drafter != null) talker.drafter.Drafted = false;
            if (live?.drafter != null) live.drafter.Drafted = false;
            try { KillHostiles(); } catch (System.Exception) { }
            Kit.Summary("Gesta/TrashTalk");
            done = doneC = true;
        }

        void KillHostiles()
        {
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.IsPrisoner).ToList())
            {
                p.Kill(null);
                p.Corpse?.Destroy();
            }
        }

        static void KeepRunning(TimeSpeed speed)
        {
            if (Find.TickManager.Paused) Find.TickManager.CurTimeSpeed = speed == TimeSpeed.Paused ? TimeSpeed.Normal : speed;
        }

        Lord Raid(float points, RaidStrategyDef strategy)
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = Faction.OfPirates is Faction f && f.HostileTo(Faction.OfPlayer) ? f : Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false);
            parms.points = points;
            parms.raidStrategy = strategy;
            parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
            var before = new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
            TimeSpeed speed = Find.TickManager.CurTimeSpeed;
            IncidentDefOf.RaidEnemy.Worker.TryExecute(parms);
            KeepRunning(speed);
            Pawn any = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => !before.Contains(p) && p.GetLord() != null && p.HostileTo(Faction.OfPlayer));
            return any?.GetLord();
        }

        bool Open(IntVec3 c) => c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null;

        /// <summary>Two open cells 20 apart with a clear line between them and room for a raid at B.</summary>
        bool FindArena()
        {
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position;
            var dirs = new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South };
            foreach (IntVec3 a in GenRadial.RadialCellsAround(root, 40f, true))
            {
                if (!Open(a)) continue;
                foreach (IntVec3 d in dirs)
                {
                    IntVec3 b = a + d * 20;
                    if (!Open(b) || !GenSight.LineOfSight(a, b, map)) continue;
                    int room = GenRadial.RadialCellsAround(b, 3f, true).Count(c => Open(c) && GenSight.LineOfSight(a, c, map));
                    if (room < 10 || !Open(a + d * 5)) continue;
                    // the wall line at the midpoint must be buildable
                    IntVec3 m = a + d * 10;
                    IntVec3 perp = new IntVec3(d.z, 0, d.x);
                    bool wallOk = true;
                    for (int k = -3; k <= 3 && wallOk; k++) wallOk = Open(m + perp * k) && (m + perp * k).GetEdifice(map) == null;
                    if (!wallOk) continue;
                    spotA = a; spotB = b; dir = d;
                    return true;
                }
            }
            return false;
        }

        void PlaceRaid(Lord lord)
        {
            foreach (Pawn p in lord.ownedPawns.ToList())
            {
                IntVec3 c = CellFinder.StandableCellNear(spotB, map, 3f, x => Open(x) && GenSight.LineOfSight(spotA, x, map));
                if (c.IsValid) Kit.Teleport(p, c);
                p.equipment?.DestroyAllEquipment();   // a standoff: nobody shoots the test colonists dead
            }
        }

        Pawn NewColonist(IntVec3 at, int social, params TraitDef[] traits)
        {
            for (int i = 0; i < 30; i++)
            {
                Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                SkillRecord s = p.skills?.GetSkill(SkillDefOf.Social);
                if (s == null || s.TotallyDisabled || !p.DevelopmentalStage.Adult()) { Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.Discard); continue; }
                s.Level = social;
                foreach (Trait t in p.story.traits.allTraits.ToList()) p.story.traits.RemoveTrait(t);
                foreach (TraitDef d in traits) p.story.traits.GainTrait(new Trait(d));
                GenSpawn.Spawn(p, at, map);
                Kit.Teleport(p, at);
                p.drafter.Drafted = true;   // stands still
                return p;
            }
            throw new System.Exception("could not generate a colonist who can talk");
        }

        static bool HasMemory(Pawn p, ThoughtDef def) => p.needs?.mood?.thoughts?.memories?.Memories.Any(m => m.def == def) == true;

        static string Text(LogEntry e, Thing pov) => e?.ToGameStringFromPOV(pov) ?? "";

        // ------------------------------------------------------------------ stage A: first exchange, rules

        void RunA()
        {
            var tt = TrashTalk.Instance;
            Kit.Check(FindArena(), $"setup: open arena {spotA} -> {spotB}");
            raid1 = Raid(600f, RaidStrategyDefOf.ImmediateAttack);
            Kit.Check(raid1 != null && raid1.ownedPawns.Count >= 2, $"setup: a real raid ({raid1?.ownedPawns.Count ?? 0} raiders, toil {raid1?.CurLordToil?.GetType().Name})");
            if (raid1 == null) { Finish(); return; }
            PlaceRaid(raid1);
            talker = NewColonist(spotA, 20, TraitDefOf.Abrasive, TraitDefOf.Brawler);
            Kit.Check(TrashTalk.Power(talker) == 26f && TrashTalk.MoraleDamage(26f) == 80f && TrashTalk.BackfireChance(26f) == 0.05f,
                $"taunt power of a Social 20 abrasive brawler is 26 (damage {TrashTalk.MoraleDamage(TrashTalk.Power(talker))}, backfire {TrashTalk.BackfireChance(TrashTalk.Power(talker)):0.00})");
            Kit.Check(TrashTalk.BackfireChance(0f) == 0.45f && System.Math.Abs(TrashTalk.BackfireChance(8f) - 0.17f) < 0.001f, "backfire chance 45% at power 0, 17% at power 8");

            // off: nothing happens
            TrashTalk.DebugChance = 1f;
            TrashTalk.DebugRoll = 0.99f;
            GestaMod.Settings.trashTalk = false;
            Kit.Check(tt.ScanNow() == 0 && tt.WhyNotLord(raid1, Find.TickManager.TicksGame) == "feature off", "with the feature off the scan makes no exchange");
            GestaMod.Settings.trashTalk = true;

            // pair rules, with the nearest raider standing exactly at B
            Pawn r0 = raid1.ownedPawns.First(p => TrashTalk.WhyNotTarget(p, raid1) == null);
            Kit.Teleport(r0, spotB);
            Kit.Check(TrashTalk.WhyNotPair(talker, r0) == null, $"control: 20 cells in the open is a valid pair ({TrashTalk.WhyNotPair(talker, r0) ?? "ok"})");
            Kit.Teleport(r0, spotA + dir * 5);
            Kit.Check(TrashTalk.WhyNotPair(talker, r0) == "too close", $"5 cells is too close ({TrashTalk.WhyNotPair(talker, r0)})");
            IntVec3 far = map.AllCells.FirstOrDefault(c => Open(c) && (c - spotA).LengthHorizontalSquared > 48 * 48);
            if (far.IsValid && far != IntVec3.Zero)
            {
                Kit.Teleport(r0, far);
                Kit.Check(TrashTalk.WhyNotPair(talker, r0) == "too far", $"{far.DistanceTo(spotA):0} cells is too far ({TrashTalk.WhyNotPair(talker, r0)})");
            }
            Kit.Teleport(r0, spotB);
            var walls = new List<Thing>();
            IntVec3 mid = spotA + dir * 10, perp = new IntVec3(dir.z, 0, dir.x);
            for (int k = -3; k <= 3; k++) walls.Add(GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), mid + perp * k, map));
            Kit.Check(TrashTalk.WhyNotPair(talker, r0) == "no line of sight", $"a wall in between blocks it ({TrashTalk.WhyNotPair(talker, r0)})");
            foreach (Thing w in walls) if (!w.Destroyed) w.Destroy();
            Pawn brawler = Kit.SpawnHostile(map, spotA + perp);
            Kit.Teleport(brawler, (spotA + perp).Standable(map) ? spotA + perp : spotA - perp);
            Kit.Check(TrashTalk.WhyNotPair(talker, r0) == "in melee", $"a hostile next to the colonist means melee ({TrashTalk.WhyNotPair(talker, r0)})");
            brawler.Kill(null);
            brawler.Corpse?.Destroy();
            Kit.Check(TrashTalk.WhyNotPair(talker, r0) == null, "...and the pair is valid again once it is gone");
            Pawn plain = NewColonist(spotA - dir * 2, 3);
            Kit.Check(tt.WhyNotTaunter(plain, Find.TickManager.TicksGame) == "not sharp-tongued enough",
                $"Social 3 with no abrasive/aggressive trait does not qualify ({tt.WhyNotTaunter(plain, Find.TickManager.TicksGame)})");
            plain.drafter.Drafted = false;

            // the real scan
            int logBefore = Find.PlayLog.AllEntries.Count;
            int made = tt.ScanNow();
            first = tt.LastExchange;
            Kit.Check(made == 1 && first != null && first.lord == raid1, $"the scan makes one exchange with the raid ({made}, by {first?.taunter?.LabelShort} at {first?.target?.LabelShort})");
            if (first == null) { Finish(); return; }
            state1 = tt.StateFor(raid1, false);
            string text = Text(first.entry, first.taunter);
            Log.Message("[Test] (taunt log: " + text + ")");
            Kit.Check(Find.PlayLog.AllEntries.Contains(first.entry) && Find.PlayLog.AllEntries.Count > logBefore, "the taunt is in the play log");
            Kit.Check(text.Contains(first.taunter.LabelShort) && text.Contains(first.target.LabelShort), "the log entry names both the colonist and the raider");
            Kit.Check(first.entry.Concerns(first.taunter) && first.entry.Concerns(first.target), "the entry shows in both pawns' logs");
            Kit.Check(first.outcome == TauntOutcome.Hit && first.moraleBefore == 100f && first.moraleAfter == 100f - TrashTalk.MoraleDamage(TrashTalk.Power(first.taunter)),
                $"a hit lowers the raid's morale by 15 + 2.5 x power ({first.moraleBefore:0} -> {first.moraleAfter:0}, power {TrashTalk.Power(first.taunter)})");
            Kit.Check(state1 != null && state1.exchanges == 1, "the raid's exchange is counted");
            string box = TrashTalkBubbles.TextOn(first.taunter);
            Log.Message("[Test] (taunter shouts: " + box + ")");
            Kit.Check(!box.NullOrEmpty(), "the shouted words are shown above the colonist");
            Kit.Check(first.taunter.Position.GetThingList(map).OfType<MoteBubble>().Any(m => m.link1.Target.Thing == first.taunter && m.arrowTarget == first.target),
                "a vanilla interaction bubble over the colonist points at the raider");
            Kit.Check(tt.PendingReplies == 1, "the raider will answer");

            // cooldowns
            Kit.Check(tt.ScanNow() == 0, "a second scan right away does nothing");
            Kit.Check(tt.WhyNotLord(raid1, Find.TickManager.TicksGame) == "lord cooldown", "the raid is on cooldown");
            Kit.Check(tt.WhyNotTaunter(first.taunter, Find.TickManager.TicksGame) == "colonist cooldown", "the colonist is on cooldown");
            string xml = Scribe.saver.DebugOutputFor(tt);
            Kit.Check(xml != null && xml.Contains("lordStates") && xml.Contains("<lordId>" + raid1.loadID + "</lordId>") && xml.Contains("colonistLastTaunt"),
                "the raid's morale and the cooldowns are written to the save");
        }

        // ------------------------------------------------------------------ stage B: reply, break, backfire, negatives

        void RunB()
        {
            var tt = TrashTalk.Instance;
            if (first == null) { Finish(); return; }
            Kit.Check(tt.RepliesDelivered >= 1 && tt.LastReplyEntry != null && tt.LastReplyEntry.Concerns(first.target), $"{TrashTalk.ReplyDelay} ticks later the raider answers");
            string reply = Text(tt.LastReplyEntry, first.target);
            Log.Message("[Test] (reply log: " + reply + ")");
            Kit.Check(reply.Contains(first.target.LabelShort) && reply.Contains(first.taunter.LabelShort), "the reply names both");
            Log.Message("[Test] (raider shouts: " + TrashTalkBubbles.TextOn(first.target) + ")");
            Kit.Check(!TrashTalkBubbles.TextOn(first.target).NullOrEmpty(), "the reply's words are shown above the raider");

            // the break: the talker's second taunt takes morale to zero
            tt.DebugResetCooldowns();
            PlaceRaid(raid1);
            Kit.Teleport(talker, spotA);
            Pawn target = raid1.ownedPawns.Where(p => TrashTalk.WhyNotTarget(p, raid1) == null).OrderBy(p => p.Position.DistanceToSquared(spotA)).First();
            int pendingBefore = tt.PendingReplies;
            string why = tt.TryExchange(raid1, talker, target);
            TauntRecord br = tt.LastExchange;
            Kit.Check(why == null && br != first && br.outcome == TauntOutcome.Break && br.moraleAfter == 0f,
                $"morale at zero breaks the raid ({why ?? br.outcome.ToString()}, {br.moraleBefore:0} -> {br.moraleAfter:0}, toil {raid1.CurLordToil?.GetType().Name})");
            if (why != null) { Finish(); return; }
            Pawn fled = br.fled;
            Kit.Check(fled != null && fled.MentalStateDef == MentalStateDefOf.PanicFlee, $"a raider panics and flees ({fled?.LabelShort}, {fled?.MentalStateDef?.defName})");
            if (fled != null)
            {
                float maxPower = raid1.ownedPawns.Max(p => p.kindDef.combatPower);
                Kit.Check(fled != raid1.faction.leader && !fled.kindDef.isBoss && fled.kindDef.combatPower <= maxPower,
                    $"the one who runs is a weak raider ({fled.kindDef.defName}, {fled.kindDef.combatPower} of max {maxPower})");
                Kit.Check(!GenHostility.IsActiveThreatToPlayer(fled), "the fleeing raider is no longer a threat");
            }
            string brText = Text(br.entry, talker);
            Log.Message("[Test] (break log: " + brText + ")");
            Kit.Check(brText.Contains("ran") || brText.Contains("fled"), "the log tells that one of them ran");
            Kit.Check(tt.PendingReplies == pendingBefore, "nobody answers a taunt that broke them");
            tt.DebugResetCooldowns();
            Kit.Check(tt.WhyNotLord(raid1, Find.TickManager.TicksGame) == "trash talk over" && tt.TryExchange(raid1, talker, target) != null,
                "after a break that raid's trash talk is over");

            // backfire and max exchanges: a second raid against a Social 0 brawler
            KillHostiles();
            raid2 = Raid(400f, RaidStrategyDefOf.ImmediateAttack);
            Kit.Check(raid2 != null, "setup: a second raid");
            if (raid2 == null) { Finish(); return; }
            PlaceRaid(raid2);
            talker.drafter.Drafted = false;
            Kit.Teleport(talker, spotA - dir * 3);
            Pawn weak = NewColonist(spotA, 0, TraitDefOf.Brawler);
            Kit.Check(TrashTalk.Power(weak) == 2f && tt.WhyNotTaunter(weak, Find.TickManager.TicksGame) == null, "a Social 0 brawler qualifies by temper (power 2)");
            Pawn t2 = raid2.ownedPawns.First(p => TrashTalk.WhyNotTarget(p, raid2) == null);
            TrashTalk.DebugRoll = 0.99f;
            why = tt.TryExchange(raid2, weak, t2);
            float afterHit = tt.LastExchange.moraleAfter;
            Kit.Check(why == null && tt.LastExchange.outcome == TauntOutcome.Hit && afterHit == 80f, $"a weak hit still lands (100 -> {afterHit:0})");
            tt.DebugResetCooldowns();
            TrashTalk.DebugRoll = 0f;
            why = tt.TryExchange(raid2, weak, t2);
            TauntRecord bf = tt.LastExchange;
            Kit.Check(why == null && bf.outcome == TauntOutcome.Backfire, $"a bad taunt backfires ({why ?? bf.outcome.ToString()})");
            Kit.Check(bf.moraleAfter == afterHit + TrashTalk.BackfireMoraleGain, $"the raiders get bolder ({bf.moraleBefore:0} -> {bf.moraleAfter:0})");
            Kit.Check(HasMemory(weak, GestaDefOf.Gesta_TauntBackfired), "the colonist feels the sting (mood memory)");
            Kit.Check(Text(bf.entry, weak).Length > 0, "backfire log: " + Text(bf.entry, weak));
            backfireTarget = t2; backfireWeak = weak; backfireTick = GenTicks.TicksAbs;   // log timestamps are absolute ticks
            tt.DebugResetCooldowns();
            TrashTalk.DebugRoll = 0.99f;
            why = tt.TryExchange(raid2, weak, t2);
            Kit.Check(why == null && tt.StateFor(raid2, false).exchanges == 3, "a third exchange is allowed");
            tt.DebugResetCooldowns();
            why = tt.TryExchange(raid2, weak, t2);
            Kit.Check(why == "max exchanges", $"a fourth is refused ({why})");

            // never: mechanoids, animals, prisoners
            Faction mechs = Faction.OfMechanoids;
            if (mechs != null)
            {
                var pawns = new List<Pawn>();
                for (int i = 0; i < 3; i++) pawns.Add(Kit.SpawnHostile(map, spotB, PawnKindDef.Named("Mech_Scyther"), mechs));
                Lord mechLord = LordMaker.MakeNewLord(mechs, new LordJob_AssaultColony(mechs, canKidnap: false, canTimeoutOrFlee: false, sappers: false, useAvoidGridSmart: false, canSteal: false), map, pawns);
                foreach (Pawn m in pawns) Kit.Teleport(m, CellFinder.StandableCellNear(spotB, map, 3f, Open));
                tt.DebugResetCooldowns();
                int before = Find.PlayLog.AllEntries.Count;
                TauntRecord last = tt.LastExchange;
                Kit.Check(tt.WhyNotLord(mechLord, Find.TickManager.TicksGame) == "not a hostile humanlike faction", "a mechanoid raid is never taunted");
                Kit.Check(TrashTalk.WhyNotTarget(pawns[0], mechLord) == "not a humanlike adult", "a mechanoid is never a target");
                Kit.Check(tt.ScanNow() == 0 && tt.LastExchange == last, "the scan ignores the mechanoids (other raids done)");
                foreach (Pawn m in pawns) if (!m.Dead) { m.Kill(null); m.Corpse?.Destroy(); }
            }
            Pawn muffalo = PawnGenerator.GeneratePawn(PawnKindDef.Named("Muffalo"), null);
            GenSpawn.Spawn(muffalo, CellFinder.StandableCellNear(spotB, map, 3f, Open), map);
            muffalo.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Manhunter, forced: true);
            Kit.Check(TrashTalk.WhyNotTarget(muffalo, muffalo.GetLord()) != null, $"a manhunting animal is never a target ({TrashTalk.WhyNotTarget(muffalo, muffalo.GetLord())})");
            muffalo.Kill(null);
            muffalo.Corpse?.Destroy();
            Pawn prisoner = Kit.SpawnHostile(map, spotA + dir * 10);
            prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            Kit.Check(TrashTalk.WhyNotTarget(prisoner, prisoner.GetLord()) == "prisoner", $"a prisoner is never a target ({TrashTalk.WhyNotTarget(prisoner, prisoner.GetLord())})");
            prisoner.Destroy();
            weak.drafter.Drafted = false;
        }

        Pawn backfireTarget, backfireWeak;
        int backfireTick = -1;

        // ------------------------------------------------------------------ stage C: mock reply, live scan on a staging raid

        void RunC()
        {
            var tt = TrashTalk.Instance;
            int now = Find.TickManager.TicksGame;
            if (liveScanTick < 0)
            {
                // the backfire's reply mocks the colonist (it was due 120 ticks after the backfire)
                var mock = Find.PlayLog.AllEntries.OfType<PlayLogEntry_Interaction>().FirstOrDefault(e => e.Timestamp > backfireTick
                    && Traverse(e) == GestaDefOf.Gesta_TrashTalkMock && e.Concerns(backfireWeak) && e.Concerns(backfireTarget));
                Kit.Check(mock != null, "a backfired taunt gets a mocking reply: " + Text(mock, backfireTarget));
                KillHostiles();
                RaidStrategyDef stage = DefDatabase<RaidStrategyDef>.GetNamedSilentFail("StageThenAttack");
                staged = stage != null ? Raid(500f, stage) : null;
                Kit.Check(staged != null && TrashTalk.IsStandoffToil(staged.CurLordToil), $"setup: a raid staging before the attack ({staged?.CurLordToil?.GetType().Name})");
                if (staged == null) { Finish(); return; }
                if (talker?.drafter != null) talker.drafter.Drafted = false;
                live = NewColonist(spotA, 12);
                TrashTalk.DebugChance = 1f;
                TrashTalk.DebugRoll = 0.99f;
                tt.DebugResetCooldowns();
                // the next natural scan tick (TicksGame % 250 == 131)
                liveScanTick = now + ((TrashTalk.ScanOffset - now % TrashTalk.ScanInterval) + TrashTalk.ScanInterval) % TrashTalk.ScanInterval;
                if (liveScanTick - now < 2) liveScanTick += TrashTalk.ScanInterval;
                liveStartRecord = tt.LastExchange;
                return;
            }
            if (now == liveScanTick - 1)
            {
                // raiders and the colonist face off right before the game's own scan
                PlaceRaid(staged);
                Kit.Teleport(live, spotA);
                live.drafter.Drafted = true;
            }
            else if (now >= liveScanTick + 2)
            {
                TauntRecord r = tt.LastExchange;
                Kit.Check(r != liveStartRecord && r.lord == staged && r.tick == liveScanTick,
                    $"live: the game's own scan makes a raid in its {staged.CurLordToil?.GetType().Name} toil trade insults ({r?.taunter?.LabelShort} -> {r?.target?.LabelShort} at tick {r?.tick}, scan {liveScanTick})");
                Finish();
            }
        }

        TauntRecord liveStartRecord;

        static InteractionDef Traverse(PlayLogEntry_Interaction e) => e == null ? null :
            HarmonyLib.Traverse.Create(e).Field("intDef").GetValue<InteractionDef>();
    }
}
