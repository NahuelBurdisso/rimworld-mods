using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using OldScores;
using RimWorld;
using Verse;
using Verse.AI;

namespace OldScoresTest
{
    /// <summary>Feature 1 — Enemy Nickname, through real kills and the real dread check.</summary>
    public class NicknameHarness : GameComponent
    {
        bool active, done;
        int ticks, phase;
        Map map;
        Faction pirates;
        IntVec3 origin;
        Pawn ana, fleer, periodicRaider;
        Thing wall;
        int periodicSpawnTick;
        readonly List<Pawn> spawned = new List<Pawn>();

        public NicknameHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks < TestUtil.NicknamesStart) return;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "nickname harness threw: " + ex); Finish(); }
        }

        OldScoresTracker T => OldScoresTracker.Instance;

        void Step()
        {
            if (phase == 0)
            {
                phase = 1;
                map = Find.CurrentMap;
                // a hostile humanlike faction nobody has a nickname from yet (the Gesta harness kills
                // leaders earlier in the same game, which can already have named one of its colonists)
                pirates = TestUtil.HostileFaction(f => !T.Nicknames.Any(r => r.faction == f && r.Named));
                Kit.Check(pirates != null, $"setup: a hostile humanlike faction to fight ({pirates?.Name ?? "none"})");
                if (pirates == null) { Finish(); return; }
                if (T.Nicknames.Any(r => r.faction == pirates && r.Named))
                    Log.Message($"[Test] (note: someone already has a nickname from {pirates.Name}; dread checks only look at who caused it)");
                Pawn anyColonist = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
                origin = TestUtil.PrepareStrip(map, anyColonist?.Position ?? map.Center, 8, 22, 2);
                Kit.Check(origin.IsValid, $"setup: prepared a clear test strip at {origin}");
                if (!origin.IsValid) { Finish(); return; }
                KillsAndNames();
                Dread();
                return;
            }
            if (phase == 1 && ticks >= TestUtil.NicknamesStart + 90)
            {
                phase = 2;
                JobDef job = fleer.CurJobDef;
                Kit.Check(fleer.MentalStateDef == OldScoresDefOf.OldScores_Dread && job == JobDefOf.Flee,
                    $"the frightened raider runs away (think tree wired: state {fleer.MentalStateDef?.defName}, job {job?.defName})");
                Kit.Check(fleer.Position.DistanceTo(ana.Position) > 8f, $"...and is farther from her than before ({fleer.Position.DistanceTo(ana.Position):0.0} cells)");
                // periodic path: a fresh raider close to her, the map component's 250-tick check must catch him
                IntVec3 cell = CellFinder.StandableCellNear(ana.Position + new IntVec3(6, 0, 0), map, 3f,
                    c => c.DistanceTo(ana.Position) >= 4f && GenSight.LineOfSight(ana.Position, c, map));
                periodicRaider = TestUtil.SpawnAt(map, cell, pirates);
                periodicRaider.equipment?.DestroyAllEquipment();
                spawned.Add(periodicRaider);
                periodicSpawnTick = ticks;
                return;
            }
            if (phase == 2)
            {
                if (periodicRaider.MentalStateDef == OldScoresDefOf.OldScores_Dread)
                {
                    Pawn cause = periodicRaider.MentalState.causedByPawn;
                    Kit.Check(cause != null && cause.Faction == Faction.OfPlayer,
                        $"the map component's 250-tick check frightens raiders by itself ({ticks - periodicSpawnTick} ticks, caused by {cause?.LabelShort ?? "nobody"}{(cause == ana ? "" : ", another nicknamed colonist")})");
                    Finish();
                }
                else if (ticks - periodicSpawnTick > OldScoresTuning.DreadInterval + 30)
                {
                    Kit.Check(false, $"the map component's 250-tick check frightens raiders by itself (state {periodicRaider.MentalStateDef?.defName ?? "none"}, dist {periodicRaider.Position.DistanceTo(ana.Position):0.0})");
                    Finish();
                }
            }
        }

        void KillsAndNames()
        {
            ana = TestUtil.NewColonist(map, origin);
            TestUtil.Teleport(ana, origin);
            ana.equipment?.DestroyAllEquipment();   // unarmed: the namer must still produce something
            ana.drafter.Drafted = true;             // stand still for the dread checks

            KillHostiles(ana, 4, pirates);
            NicknameRecord rec = T.GetNickname(ana, pirates);
            Kit.Check(rec != null && rec.kills == 4 && !rec.Named, $"four kills: counted ({rec?.kills}) but no nickname yet");
            KillHostiles(ana, 1, pirates);
            rec = T.GetNickname(ana, pirates);
            Kit.Check(rec != null && rec.Named, $"fifth kill: the pirates give her a nickname ({rec?.nickname})");
            Kit.Check(rec != null && rec.nickname.StartsWith("the ") && !rec.nickname.Contains("[") && rec.nickname.Length > 6,
                "the nickname is a resolved 'the ...' phrase");
            Kit.Check(TestUtil.LetterAbout(ana.LabelShort, pirates), "a letter with relatedFaction = the pirates announces it");
            string direct = T.InspectLine(ana);
            Kit.Check(rec != null && direct != null && direct.Contains(rec.nickname), $"the mod builds her inspect line ({direct ?? "null"})");
            Patches inspectPatches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Pawn), nameof(Pawn.GetInspectString)));
            bool patched = inspectPatches != null && inspectPatches.Postfixes.Any(x => x.owner == "nahuel.oldscores");
            Kit.Check(patched, $"Pawn.GetInspectString carries the Old Scores postfix (owners: {(inspectPatches == null ? "none" : string.Join(",", inspectPatches.Owners))})");
            string inspect = ana.GetInspectString() ?? "";
            Kit.Check(rec != null && inspect.Contains(rec.nickname),
                $"her inspect pane shows the nickname (inspect: {inspect.Replace("\n", " | ")})");
            Kit.Check(direct == null || !inspect.Contains("\n\n" + direct), "...on its own line, with no blank line before it");
            KillHostiles(ana, 2, pirates);
            Kit.Check(T.GetNickname(ana, pirates).nickname == rec.nickname, "more kills do not rename her");

            // names for several personalities, to show the rule pack resolves everywhere (log only)
            var samples = new List<string>();
            for (int i = 0; i < 6; i++) samples.Add(OldScoresTracker.GenerateNickname(ana));
            Kit.Check(samples.All(s => s.StartsWith("the ") && !s.Contains("[")), "sample nicknames: " + string.Join(" / ", samples));

            // downed raiders do not count
            Pawn ben = TestUtil.NewColonist(map, origin);
            for (int i = 0; i < 5; i++)
            {
                Pawn h = Kit.SpawnHostile(map, origin + new IntVec3(15, 0, 0), null, pirates);
                spawned.Add(h);
                Kit.Down(h);
                Kit.KillBy(h, ben);
            }
            NicknameRecord benRec = T.GetNickname(ben, pirates);
            Kit.Check(benRec == null || (benRec.kills == 0 && !benRec.Named), "finishing off five downed raiders earns nothing");

            // hidden factions (here the hostile ancients) never nickname anyone
            Faction ancients = Faction.OfAncientsHostile;
            if (ancients != null && ancients.Hidden)
            {
                Pawn sleeper = Kit.SpawnHostile(map, origin + new IntVec3(15, 0, 0), null, ancients);
                spawned.Add(sleeper);
                bool hostile = sleeper.HostileTo(Faction.OfPlayer);
                Kit.KillBy(sleeper, ben);
                Kit.Check(hostile && T.GetNickname(ben, ancients) == null, $"killing a hostile pawn of a hidden faction ({ancients.Name}) counts for nothing (hostile {hostile})");
            }
            else Log.Message("[Test] (skipped: no hidden hostile ancients faction)");

            // the real leader names her at once
            Pawn cal = TestUtil.NewColonist(map, origin);
            Pawn king = Kit.SpawnHostile(map, origin + new IntVec3(15, 0, 0), null, pirates);
            spawned.Add(king);
            Pawn oldLeader = pirates.leader;
            pirates.leader = king;
            Kit.KillBy(king, cal);
            if (pirates.leader == king || pirates.leader == null) pirates.leader = oldLeader;
            NicknameRecord calRec = T.GetNickname(cal, pirates);
            Kit.Check(calRec != null && calRec.Named && calRec.kills == 1, $"killing the faction's leader names her with one kill ({calRec?.nickname})");

            // toggle off
            OldScoresMod.Settings.nicknames = false;
            Pawn dan = TestUtil.NewColonist(map, origin);
            KillHostiles(dan, 5, pirates);
            OldScoresMod.Settings.nicknames = true;
            Kit.Check(T.GetNickname(dan, pirates) == null, "with the feature off, five kills give nothing");
            // move the extra colonists out of the dread strip
            foreach (Pawn p in new[] { ben, cal, dan })
                TestUtil.Teleport(p, CellFinder.StandableCellNear(origin + new IntVec3(0, 0, 12), map, 8f));
        }

        void Dread()
        {
            float oldChance = OldScoresTuning.DreadChance;
            OldScoresTuning.DreadChance = 1f;
            try
            {
                fleer = SpawnUnarmed(origin + new IntVec3(8, 0, 0), pirates);
                Pawn far = SpawnUnarmed(origin + new IntVec3(20, 0, 0), pirates);
                Pawn downed = SpawnUnarmed(origin + new IntVec3(5, 0, 1), pirates);
                Kit.Down(downed);
                Pawn hidden = SpawnUnarmed(origin + new IntVec3(-6, 0, 0), pirates);
                wall = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), origin + new IntVec3(-3, 0, 0), map);
                Faction other = TestUtil.OtherHostileFaction(pirates);
                Pawn stranger = other != null ? SpawnUnarmed(origin + new IntVec3(6, 0, -1), other) : null;
                Kit.Check(!GenSight.LineOfSight(origin, hidden.Position, map), "setup: the wall blocks her line of sight");

                // only her share of the check: other colonists in this game may already have a nickname
                // from the same faction (the Gesta harness kills its leaders), and could frighten him first
                string before = OldScoresTracker.WhyNoDread(fleer, ana, pirates);
                int started = T.DreadFrom(ana);

                Kit.Check(fleer.MentalStateDef == OldScoresDefOf.OldScores_Dread && fleer.MentalState.causedByPawn == ana,
                    $"a raider 8 cells away in plain sight falls into dread of her (eligible before: {before ?? "yes"}; started {started}; now {fleer.MentalStateDef?.defName ?? "no mental state"}, caused by {fleer.MentalState?.causedByPawn?.LabelShort ?? "-"})");
                Kit.Check(!DreadOf(far, ana), "a raider 20 cells away does not");
                Kit.Check(!DreadOf(downed, ana), "a downed raider does not");
                Kit.Check(!DreadOf(hidden, ana), "a raider behind a wall does not");
                if (stranger != null) Kit.Check(!DreadOf(stranger, ana), $"a raider of another hostile faction ({other.Name}) does not");
                else Log.Message("[Test] (skipped: no second hostile humanlike faction for the stranger case)");
                // the others would only start a fistfight with her: gone now
                TestUtil.KillAll(new[] { far, downed, hidden, stranger });
            }
            finally { OldScoresTuning.DreadChance = oldChance; }
            // the periodic check (phase 2) runs at the real 15%: force it again for that one
            OldScoresTuning.DreadChance = 1f;
        }

        static bool DreadOf(Pawn raider, Pawn colonist) =>
            raider.MentalStateDef == OldScoresDefOf.OldScores_Dread && raider.MentalState.causedByPawn == colonist;

        Pawn SpawnUnarmed(IntVec3 cell, Faction f)
        {
            Pawn p = TestUtil.SpawnAt(map, cell, f);
            p.equipment?.DestroyAllEquipment();
            spawned.Add(p);
            return p;
        }

        void KillHostiles(Pawn killer, int n, Faction f)
        {
            for (int i = 0; i < n; i++)
            {
                Pawn h = Kit.SpawnHostile(map, origin + new IntVec3(15, 0, 0), null, f);
                spawned.Add(h);
                Kit.KillBy(h, killer);
            }
        }

        void Finish()
        {
            OldScoresTuning.DreadChance = 0.15f;
            OldScoresMod.Settings.nicknames = true;
            if (ana?.drafter != null && ana.Spawned) ana.drafter.Drafted = false;
            TestUtil.KillAll(spawned);
            if (wall != null && !wall.Destroyed) wall.Destroy();
            Kit.Summary("OldScores/Nicknames");
            TestUtil.MarkFinished("Nicknames");
            done = true;
        }
    }
}
