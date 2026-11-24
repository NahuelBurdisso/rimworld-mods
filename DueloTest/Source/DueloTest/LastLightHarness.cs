using System.Collections.Generic;
using System.Linq;
using Duelo;
using RimWorld;
using Verse;

namespace DueloTest
{
    /// <summary>
    /// Armed only under `rw launch --ticks N`. Marries two colonists, kills one, and checks the
    /// last light picks the strongest bond, delivers the farewell thought and cleans itself up.
    /// Also checks a non-player death releases no light.
    /// </summary>
    public class LastLightHarness : GameComponent
    {
        bool active, done;
        int ticks;
        Pawn deceased, expected;
        Mote_LastLight light;
        int arrivedAt = -1;

        public LastLightHarness(Game game) { }

        public override void FinalizeInit()
        {
            active = GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _);
        }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            if (ticks == 60) Start();
            else if (ticks > 60 && ticks % 30 == 0) Poll();
        }

        void Start()
        {
            Map map = Find.CurrentMap;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned
                .Where(p => p.story?.traits?.HasTrait(TraitDefOf.Psychopath) != true).ToList();
            if (colonists.Count < 2) { Check(false, "need 2 non-psychopath colonists, found " + colonists.Count); done = true; return; }

            deceased = colonists[0];
            Pawn spouse = colonists[1];
            if (!deceased.relations.DirectRelationExists(PawnRelationDefOf.Spouse, spouse))
                deceased.relations.AddDirectRelation(PawnRelationDefOf.Spouse, spouse);

            expected = LastLightUtility.FindStrongestBond(deceased, map, out int score);
            Check(expected != null, $"a bond target exists for {deceased.LabelShort} (best score {score})");

            int before = LastLightUtility.EmittedCount;
            deceased.Kill(null, null);
            Check(deceased.Dead, "victim died");

            Check(LastLightUtility.EmittedCount == before + 1, $"one light released (before {before}, after {LastLightUtility.EmittedCount})");
            light = LastLightUtility.LastEmitted;
            Check(light != null && light.Spawned && light.deceased == deceased, "the light is spawned and belongs to the victim");
            Check(light != null && light.target == expected,
                $"light targets the strongest bond ({expected?.LabelShort}), got {light?.target?.LabelShort ?? "none"}");

            // A death outside the player faction must not release a light.
            Pawn stranger = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, null);
            GenSpawn.Spawn(stranger, CellFinder.RandomClosewalkCellNear(map.Center, map, 8), map);
            int beforeStranger = LastLightUtility.EmittedCount;
            stranger.Kill(null, null);
            Check(LastLightUtility.EmittedCount == beforeStranger, "no light for a non-player death");
        }

        void Poll()
        {
            Map map = Find.CurrentMap;
            if (arrivedAt < 0 && expected != null &&
                expected.needs.mood.thoughts.memories.Memories.Any(m => m.def == DueloDefOf.Duelo_LastLightFarewell && m.otherPawn == deceased))
            {
                arrivedAt = ticks;
                Check(true, $"{expected.LabelShort} got the farewell thought after {(ticks - 60) / 60f:F1}s");
            }
            if (arrivedAt >= 0 && light != null && light.Destroyed)
            {
                Check(true, "light cleaned itself up after fading");
                done = true;
                return;
            }
            if (ticks > 60 + 1800)
            {
                Check(arrivedAt >= 0, "farewell thought arrived within 30s");
                Check(light == null || light.Destroyed, "light destroyed within 30s");
                done = true;
            }
        }

        static void Check(bool ok, string what)
        {
            if (ok) Log.Message("[Test] PASS " + what);
            else Log.Error("[Test] FAIL " + what);
        }
    }
}
