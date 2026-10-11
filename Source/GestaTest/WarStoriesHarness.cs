using System.Linq;
using Gesta;
using HarmonyLib;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>War Stories through a real fight and real social interactions. Tick 2580.</summary>
    public class WarStoriesHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public WarStoriesHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 2580) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "war stories harness threw: " + ex); }
            Kit.Summary("Gesta/WarStories");
        }

        static void ReadyToTalk(Pawn p) => Traverse.Create(p.interactions).Field("lastInteractionTime").SetValue(-9999);

        static string Tell(Pawn teller, Pawn listener)
        {
            ReadyToTalk(teller);
            bool ok = teller.interactions.TryInteractWith(listener, GestaDefOf.Gesta_RecountsBattle);
            if (!ok) return null;
            LogEntry entry = Find.PlayLog.AllEntries.FirstOrDefault();
            return entry?.ToGameStringFromPOV(teller);
        }

        void Run()
        {
            Map map = Find.CurrentMap;
            var ws = WarStories.Instance;
            var eng = EngagementTracker.Instance;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Downed).ToList()) p.Kill(null);
            eng.PollNow();
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(4, 0, -4);
            Pawn teller = NewColonist(map, root), witness = NewColonist(map, root + IntVec3.East), outsider = NewColonist(map, root + IntVec3.West);

            Kit.Check(new InteractionWorker_WarStory().RandomSelectionWeight(teller, outsider) == 0f, "no story to tell before any fight");

            var hostiles = Enumerable.Range(0, 3).Select(i => Kit.SpawnHostile(map, root + new IntVec3(9, 0, i * 2))).ToList();
            eng.PollNow();
            Kit.Check(eng.IsActive(map), "setup: a fight is on");
            hostiles[0].TakeDamage(new DamageInfo(DamageDefOf.Cut, 2f, 2f, -1f, witness));
            foreach (Pawn h in hostiles) Kit.KillBy(h, teller);
            eng.PollNow();
            WarStories.Story story = ws.StoryOf(teller);
            Kit.Check(story != null && story.realKills == 3, $"the battle leaves a story of {story?.realKills} kills");
            Kit.Check(story != null && story.witnesses.Contains(witness.thingIDNumber) && !story.witnesses.Contains(outsider.thingIDNumber),
                "it knows who was there");
            Kit.Check(new InteractionWorker_WarStory().RandomSelectionWeight(teller, outsider) > 0f, "a veteran with a fresh story wants to tell it");

            string first = Tell(teller, witness);
            Kit.Check(first != null && first.Contains(" 3 ") && !first.Contains("GESTAKILLS"), $"the first telling is true ({first})");
            Kit.Check(witness.needs.mood.thoughts.memories.Memories.Any(m => m.def == GestaDefOf.Gesta_NotHowItHappened && m.otherPawn == teller),
                "someone who was there rolls their eyes");

            string second = Tell(teller, outsider);
            int n2 = second == null ? 0 : second.Split(' ').Select(w => int.TryParse(w, out int v) ? v : 0).Max();
            Kit.Check(n2 >= 4, $"the second telling grows ({second})");
            Kit.Check(outsider.needs.mood.thoughts.memories.Memories.Any(m => m.def == GestaDefOf.Gesta_InspiredByWarStory && m.otherPawn == teller),
                "someone who was not there is inspired");

            for (int i = 0; i < 6; i++) Tell(teller, outsider);
            Kit.Check(!ws.HasFreshStory(teller) && story.timesTold == WarStories.MaxTellings,
                $"after {WarStories.MaxTellings} tellings the story is worn out");
        }

        static Pawn NewColonist(Map map, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(p, at, map);
            return p;
        }
    }
}
