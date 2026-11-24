using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PackHowls
{
    /// <summary>The fallen still waiting for their howl on this map.</summary>
    public class PendingHowl : IExposable
    {
        public List<Pawn> victims = new List<Pawn>();
        public List<string> names = new List<string>();
        public int firstDeathTick = -1;
        public int lastDeathTick = -1;
        public IntVec3 lastDeathCell = IntVec3.Invalid;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref victims, "victims", true, LookMode.Reference);
            Scribe_Collections.Look(ref names, "names", LookMode.Value);
            Scribe_Values.Look(ref firstDeathTick, "firstDeathTick", -1);
            Scribe_Values.Look(ref lastDeathTick, "lastDeathTick", -1);
            Scribe_Values.Look(ref lastDeathCell, "lastDeathCell", IntVec3.Invalid);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                victims ??= new List<Pawn>();
                victims.RemoveAll(v => v == null);
                names ??= new List<string>();
            }
        }
    }

    public class MapComponent_PackHowls : MapComponent
    {
        public const int CheckInterval = 250;
        public const int MinQuietTicks = 2500;
        public const int IgnoreThreatAfterTicks = 3 * GenDate.TicksPerDay;
        public const int DropAfterTicks = 6 * GenDate.TicksPerDay;
        public const int WildAnswerDelay = 600;
        public const int MaxWildAnswer = 3;

        public PendingHowl pending;
        public int wildAnswerTick = -1;
        public IntVec3 wildFaceCell = IntVec3.Invalid;

        /// <summary>Last fired howl, for the test harness (not saved).</summary>
        public List<Pawn> lastHowlers = new List<Pawn>();
        public List<Pawn> lastWildHowlers = new List<Pawn>();

        public MapComponent_PackHowls(Map map) : base(map) { }

        public static MapComponent_PackHowls For(Map map) => map?.GetComponent<MapComponent_PackHowls>();

        public void Notify_Fallen(Pawn victim, IntVec3 cell)
        {
            int now = Find.TickManager.TicksGame;
            if (pending == null)
            {
                if (!PackHowlsUtility.AnyPlayerHowler(map)) return;     // nobody here would howl
                pending = new PendingHowl { firstDeathTick = now };
            }
            if (!pending.victims.Contains(victim))
            {
                pending.victims.Add(victim);
                pending.names.Add(victim.LabelShort);
            }
            pending.lastDeathTick = now;
            pending.lastDeathCell = cell;
        }

        public override void MapComponentTick()
        {
            if (pending == null && wildAnswerTick < 0) return;
            int now = Find.TickManager.TicksGame;
            if (wildAnswerTick >= 0 && now >= wildAnswerTick)
            {
                wildAnswerTick = -1;
                if (PackHowlsMod.Settings.wildAnswer) WildAnswer();
            }
            if (pending == null || (now + map.uniqueID) % CheckInterval != 0) return;
            TryFire(now);
        }

        private void TryFire(int now)
        {
            int age = now - pending.firstDeathTick;
            if (age > DropAfterTicks || !map.IsPlayerHome) { pending = null; return; }
            if (now - pending.lastDeathTick < MinQuietTicks) return;
            if (!PackHowlsUtility.IsNight(map)) return;
            if (age < IgnoreThreatAfterTicks && PackHowlsUtility.AnyRealThreat(map)) return;

            IntVec3 face = pending.lastDeathCell;
            lastHowlers.Clear();
            var animals = new List<Pawn>(map.mapPawns.SpawnedColonyAnimals);   // shared buffer: copy before starting jobs
            for (int i = animals.Count - 1; i >= 0; i--)
            {
                Pawn a = animals[i];
                if (pending.victims.Contains(a)) continue;
                if (PackHowlsUtility.StartHowl(a, face)) lastHowlers.Add(a);
            }
            if (lastHowlers.Count == 0) return;          // try again later tonight or tomorrow

            PendingHowl fired = pending;
            pending = null;
            PackHowlsUtility.HowlsFired++;
            if (PackHowlsMod.Settings.wildAnswer)
            {
                wildAnswerTick = now + WildAnswerDelay;
                wildFaceCell = face;
            }
            if (PackHowlsMod.Settings.moodlets) GiveMemories(fired);
            if (PackHowlsMod.Settings.message)
                Messages.Message("PackHowls_Message".Translate(NameList(fired.names)), new LookTargets(lastHowlers[0]), MessageTypeDefOf.NeutralEvent);
        }

        private void WildAnswer()
        {
            lastWildHowlers.Clear();
            var wild = new List<Pawn>();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p.Faction == null && p.RaceProps.Animal && !p.Position.Fogged(map) && PackHowlsUtility.CanHowlNow(p)) wild.Add(p);
            }
            wild.Shuffle();
            for (int i = 0; i < wild.Count && lastWildHowlers.Count < MaxWildAnswer; i++)
                if (PackHowlsUtility.StartHowl(wild[i], wildFaceCell)) lastWildHowlers.Add(wild[i]);
        }

        private void GiveMemories(PendingHowl fired)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            float r2 = PackHowlsUtility.EarshotRadius * PackHowlsUtility.EarshotRadius;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn c = colonists[i];
                if (c.Dead || c.needs?.mood == null) continue;
                Pawn kin = RelatedVictim(c, fired.victims);
                if (kin != null)
                {
                    c.needs.mood.thoughts.memories.TryGainMemory(PackHowlsDefOf.PackHowls_PackHowledForMine, kin);
                    continue;
                }
                bool heard = false;
                for (int h = 0; h < lastHowlers.Count && !heard; h++)
                    heard = (lastHowlers[h].Position - c.Position).LengthHorizontalSquared <= r2;
                if (heard && fired.victims.Count > 0)
                    c.needs.mood.thoughts.memories.TryGainMemory(PackHowlsDefOf.PackHowls_PackHowled, fired.victims[0]);
            }
        }

        /// <summary>
        /// The victim this colonist was close to: family by blood (incl. implied relations such as siblings and
        /// grandparents), spouse, fiance(e), lover, or bonded animal. Exes do not count. Null if none.
        /// </summary>
        public static Pawn RelatedVictim(Pawn colonist, List<Pawn> victims)
        {
            if (colonist.relations == null) return null;
            for (int v = 0; v < victims.Count; v++)
            {
                Pawn victim = victims[v];
                if (victim == null) continue;
                if (colonist.relations.DirectRelationExists(PawnRelationDefOf.Spouse, victim)
                    || colonist.relations.DirectRelationExists(PawnRelationDefOf.Fiance, victim)
                    || colonist.relations.DirectRelationExists(PawnRelationDefOf.Lover, victim)
                    || colonist.relations.DirectRelationExists(PawnRelationDefOf.Bond, victim))
                    return victim;
            }
            for (int v = 0; v < victims.Count; v++)
            {
                Pawn victim = victims[v];
                if (victim != null && colonist.relations.FamilyByBlood.Contains(victim)) return victim;
            }
            return null;
        }

        public static string NameList(List<string> names)
        {
            if (names.Count == 0) return "?";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return "PackHowls_NamesTwo".Translate(names[0], names[1]);
            if (names.Count == 3) return "PackHowls_NamesThree".Translate(names[0], names[1], names[2]);
            return "PackHowls_NamesMany".Translate(names[0], names[1], names.Count - 2);
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref pending, "pending");
            Scribe_Values.Look(ref wildAnswerTick, "wildAnswerTick", -1);
            Scribe_Values.Look(ref wildFaceCell, "wildFaceCell", IntVec3.Invalid);
        }
    }
}
