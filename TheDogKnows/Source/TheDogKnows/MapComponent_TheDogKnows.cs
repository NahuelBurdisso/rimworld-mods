using System.Collections.Generic;
using RimWorld;
using Verse;

namespace TheDogKnows
{
    /// <summary>
    /// Every 120 ticks: list the hidden hostiles on the map; only if there is one, let each sensing colony
    /// animal that is off cooldown stare at the nearest one it can sense. Saves per-animal cooldowns.
    /// </summary>
    public class MapComponent_TheDogKnows : MapComponent
    {
        public const int ScanInterval = 120;
        public const int MessageCooldownTicks = 2500;
        private const int PurgeAbove = 64;

        /// <summary>Counters for the test harness.</summary>
        public static int Scans, ScansWithUnseen, AnimalChecks, StaresStarted;

        private Dictionary<int, int> nextAllowed = new Dictionary<int, int>();
        private int lastMessageTick = -99999;

        private readonly List<Pawn> unseen = new List<Pawn>();
        private readonly List<Pawn> animals = new List<Pawn>();
        private readonly List<int> purgeTmp = new List<int>();

        public MapComponent_TheDogKnows(Map map) : base(map) { }

        public static MapComponent_TheDogKnows For(Map map) => map?.GetComponent<MapComponent_TheDogKnows>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref nextAllowed, "nextAllowed", LookMode.Value, LookMode.Value);
            Scribe_Values.Look(ref lastMessageTick, "lastMessageTick", -99999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && nextAllowed == null) nextAllowed = new Dictionary<int, int>();
        }

        public override void MapComponentTick()
        {
            if ((Find.TickManager.TicksGame + map.uniqueID) % ScanInterval != 0) return;
            try { ScanNow(); }
            catch (System.Exception ex) { Log.ErrorOnce("[The Dog Knows] scan failed: " + ex, 0x7d0c4a1); }
        }

        /// <summary>One scan. Returns how many animals started staring.</summary>
        public int ScanNow()
        {
            Scans++;
            CollectUnseen();
            if (unseen.Count == 0) return 0;
            ScansWithUnseen++;
            int now = Find.TickManager.TicksGame;
            int started = 0;
            // copy first: SpawnedPawnsInFaction is the live registry list, and StartJob may run other mods' code
            animals.Clear();
            List<Pawn> mine = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
            for (int i = 0; i < mine.Count; i++)
                if (mine[i].IsAnimal && UnseenSenseDef.For(mine[i].def) != null) animals.Add(mine[i]);
            for (int i = 0; i < animals.Count; i++)
            {
                Pawn a = animals[i];
                if (!a.Spawned || a.Map != map) continue;
                UnseenSenseDef def = UnseenSenseDef.For(a.def);
                AnimalChecks++;
                if (nextAllowed.TryGetValue(a.thingIDNumber, out int allowedAt) && now < allowedAt) continue;
                if (!TDKUtility.CanReact(a)) continue;
                Pawn target = Nearest(a, def);
                if (target == null || !TDKUtility.StartStare(a, target)) continue;
                started++;
                StaresStarted++;
                nextAllowed[a.thingIDNumber] = now + def.stareTicks.max + def.cooldownTicks;
                if (TheDogKnowsMod.Settings.message && now - lastMessageTick >= MessageCooldownTicks)
                {
                    lastMessageTick = now;
                    TDKUtility.SendMessage(a);
                }
            }
            if (nextAllowed.Count > PurgeAbove) Purge(now);
            return started;
        }

        private void CollectUnseen()
        {
            unseen.Clear();
            // listerThings, not mapPawns.AllPawnsSpawned: the latter skips dormant pawns (mindState.Active false),
            // and a dormant hidden thing is exactly what a dog should smell. Same list CompSightstealer scans.
            List<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.Pawn);
            for (int i = 0; i < all.Count; i++)
                if (all[i] is Pawn p && TDKUtility.IsUnseen(p)) unseen.Add(p);
        }

        private Pawn Nearest(Pawn animal, UnseenSenseDef def)
        {
            Pawn best = null;
            int bestDist = int.MaxValue;
            for (int i = 0; i < unseen.Count; i++)
            {
                Pawn u = unseen[i];
                if (!TDKUtility.Senses(animal, u, def)) continue;
                int d = (u.Position - animal.Position).LengthHorizontalSquared;
                if (d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        private void Purge(int now)
        {
            purgeTmp.Clear();
            foreach (KeyValuePair<int, int> kv in nextAllowed)
                if (kv.Value <= now) purgeTmp.Add(kv.Key);
            for (int i = 0; i < purgeTmp.Count; i++) nextAllowed.Remove(purgeTmp[i]);
        }

        /// <summary>Diagnostics for the harness and dev-mode debugging.</summary>
        public string Explain(Pawn a)
        {
            CollectUnseen();
            UnseenSenseDef def = UnseenSenseDef.For(a.def);
            var sb = new System.Text.StringBuilder();
            sb.Append($"{a} at {a.Position}: def {def?.defName ?? "none"}, inFactionList {map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Contains(a)}, " +
                      $"isAnimal {a.IsAnimal}, cooldown {OnCooldown(a)}, canReact {TDKUtility.CanReact(a)}, job {a.CurJobDef?.defName ?? "none"}, unseen {unseen.Count}");
            if (def != null)
                for (int i = 0; i < unseen.Count; i++)
                {
                    Pawn u = unseen[i];
                    sb.Append($"; {u} at {u.Position} dist {u.Position.DistanceTo(a.Position):F1} los {GenSight.LineOfSight(a.Position, u.Position, map, skipFirstCell: true)} senses {TDKUtility.Senses(a, u, def)}");
                }
            return sb.ToString();
        }

        /// <summary>Test seams.</summary>
        public void ClearCooldowns(bool messageToo = true)
        {
            nextAllowed.Clear();
            if (messageToo) lastMessageTick = -99999;
        }
        public bool OnCooldown(Pawn p) => nextAllowed.TryGetValue(p.thingIDNumber, out int t) && Find.TickManager.TicksGame < t;
    }
}
