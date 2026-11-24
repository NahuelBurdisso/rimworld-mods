using System;
using System.Linq;
using Gesta;
using HarmonyLib;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>The Chronicler: every beat composes a clean line; real hooks fire the right beats. Tick 2450.</summary>
    public class ChronicleHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public ChronicleHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 2450) return;
            done = true;
            try { Run(); }
            catch (Exception ex) { Kit.Check(false, "chronicle harness threw: " + ex); }
            Kit.Summary("Gesta/Chronicle");
        }

        static void ResetCooldown() => Traverse.Create(Chronicler.Instance).Field("lastLineTick").SetValue(-999999);

        static bool Clean(string s) => !s.NullOrEmpty() && !s.Contains("[") && !s.Contains("]") && !s.Contains("ERR");

        void Run()
        {
            Map map = Find.CurrentMap;
            var chron = Chronicler.Instance;
            var eng = EngagementTracker.Instance;
            IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position + new IntVec3(-8, 0, 8);
            Pawn a = NewColonist(map, root), b = NewColonist(map, root + IntVec3.East * 2);
            Pawn enemy = Kit.SpawnHostile(map, root + new IntVec3(8, 0, 0));
            enemy.equipment?.DestroyAllEquipment();

            foreach (Chronicler.Beat beat in Enum.GetValues(typeof(Chronicler.Beat)))
            {
                bool allClean = true; string sample = null;
                for (int i = 0; i < 6; i++)
                {
                    string line = chron.Compose(beat, a, beat == Chronicler.Beat.Kingslayer || beat == Chronicler.Beat.Death ? enemy : null, 3);
                    sample ??= line;
                    if (!Clean(line) || !line.Contains(a.LabelShort) && beat != Chronicler.Beat.Victory && beat != Chronicler.Beat.Kingslayer) allClean = false;
                }
                Kit.Check(allClean, $"{beat} lines resolve cleanly (e.g. \"{sample}\")");
            }

            // first colonist down in a fight speaks; the second does not
            eng.PollNow();
            Kit.Check(eng.IsActive(map), "setup: a fight is on");
            ResetCooldown();
            Kit.DownBy(a, enemy);
            Kit.Check(chron.LastBeat == Chronicler.Beat.FirstDown && chron.LastLine.Contains(a.LabelShort), $"the first colonist down gets a line ({chron.LastLine})");
            int spoken = chron.LinesSpoken;
            // cooldown: right after a line, ordinary beats wait
            Kit.Check(chron.Speak(Chronicler.Beat.Recovered, a, null, map) == null && chron.LinesSpoken == spoken, "an ordinary line inside the cooldown is skipped");
            ResetCooldown();
            Kit.DownBy(b, enemy);
            Kit.Check(chron.LinesSpoken == spoken, "the second one down in the same fight does not");
            Traverse.Create(chron).Field("lastLineTick").SetValue(Find.TickManager.TicksGame - Chronicler.DeathGapTicks - 1);
            Kit.KillBy(b, enemy);
            Kit.Check(chron.LastBeat == Chronicler.Beat.Death && chron.LastLine.Contains(b.LabelShort), $"a colonist's death always speaks ({chron.LastLine})");

            // kingslayer
            ResetCooldown();
            Pawn shooter = NewColonist(map, root + IntVec3.North * 3);
            Pawn king = Kit.SpawnHostile(map, root + new IntVec3(10, 0, 3));
            Pawn prev = king.Faction.leader;
            king.Faction.leader = king;
            Kit.KillBy(king, shooter);
            if (king.Faction.leader == king || king.Faction.leader == null) king.Faction.leader = prev;
            Kit.Check(chron.LastBeat == Chronicler.Beat.Kingslayer, $"killing their leader gets a line ({chron.LastLine})");

            // victory: the fight ends with kills on our side
            ResetCooldown();
            Pawn extra = Kit.SpawnHostile(map, root + new IntVec3(10, 0, -3));
            Kit.KillBy(extra, shooter);
            Kit.KillBy(enemy, shooter);
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned.Where(p => p.HostileTo(Faction.OfPlayer) && !p.Downed).ToList()) p.Kill(null);
            eng.PollNow();
            Kit.Check(!eng.IsActive(map) && chron.LastBeat == Chronicler.Beat.Victory, $"winning the fight gets a closing line ({chron.LastLine})");

            Kit.Check(chron.TimesDowned(a) >= 1, "it remembers how often a colonist went down");
        }

        static Pawn NewColonist(Map map, IntVec3 at)
        {
            Pawn p = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            GenSpawn.Spawn(p, at, map);
            return p;
        }
    }
}
