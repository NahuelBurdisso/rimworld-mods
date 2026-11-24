using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace OldScores
{
    /// <summary>
    /// Feature 5 — The Beast Remembers. (a) a colony animal remembers the man who hurt it and growls at him
    /// when he is near again; (b) a raider downed by a colony animal fears it once he is a prisoner. See DESIGN.md.
    /// </summary>
    public partial class OldScoresTracker
    {
        List<GrudgeRecord> grudges = new List<GrudgeRecord>();
        List<FearRecord> fears = new List<FearRecord>();

        public IReadOnlyList<GrudgeRecord> Grudges => grudges;
        public IReadOnlyList<FearRecord> Fears => fears;
        public bool AnyBeastMemory => grudges.Count > 0 || fears.Count > 0;

        static Texture2D GrowlSymbol => BeastMemoryTextures.Growl;
        public static readonly Color GrowlRed = new Color(1f, 0.18f, 0.12f);

        static bool IsColonyAnimal(Pawn p) => p != null && p.RaceProps.Animal && p.Faction == Faction.OfPlayer;

        // --- (a) recording ------------------------------------------------------------------------

        /// <summary>Pawn.PostApplyDamage postfix: a colony animal hurt by a man who is not one of ours.</summary>
        public void Notify_AnimalHurt(Pawn victim, DamageInfo dinfo, float totalDamageDealt)
        {
            if (!OldScoresMod.Settings.beastMemory || totalDamageDealt <= 0f || victim.Dead || !IsColonyAnimal(victim)) return;
            // a man: humanlike, alive (a grenade can land after its thrower died), not one of ours, not a shambler
            if (!(dinfo.Instigator is Pawn man) || man == victim || man.Dead || !man.RaceProps.Humanlike
                || man.Faction == Faction.OfPlayer || man.IsMutant) return;
            if (dinfo.Def == null || !dinfo.Def.ExternalViolenceFor(victim)) return;
            int now = Find.TickManager.TicksGame;
            GrudgeRecord rec = GetGrudge(victim.thingIDNumber, man.thingIDNumber);
            if (rec != null) { rec.createdTick = now; rec.humanName = man.LabelShort; return; }
            // at most GrudgesPerAnimal per animal: the oldest goes
            int count = 0, oldest = -1;
            for (int i = 0; i < grudges.Count; i++)
            {
                if (grudges[i].animalId != victim.thingIDNumber) continue;
                count++;
                if (oldest < 0 || grudges[i].createdTick < grudges[oldest].createdTick) oldest = i;
            }
            if (count >= OldScoresTuning.GrudgesPerAnimal) grudges.RemoveAt(oldest);
            if (grudges.Count >= OldScoresTuning.MaxGrudges) grudges.RemoveAt(OldestGrudge());
            grudges.Add(new GrudgeRecord
            {
                animalId = victim.thingIDNumber,
                animalName = victim.LabelShort,
                humanId = man.thingIDNumber,
                humanName = man.LabelShort,
                createdTick = now,
            });
        }

        /// <summary>Index of the grudge with the smallest createdTick (a refreshed grudge is not the oldest).</summary>
        int OldestGrudge()
        {
            int oldest = 0;
            for (int i = 1; i < grudges.Count; i++)
                if (grudges[i].createdTick < grudges[oldest].createdTick) oldest = i;
            return oldest;
        }

        public GrudgeRecord GetGrudge(int animalId, int humanId)
        {
            for (int i = 0; i < grudges.Count; i++)
                if (grudges[i].animalId == animalId && grudges[i].humanId == humanId) return grudges[i];
            return null;
        }

        public FearRecord GetFear(int prisonerId)
        {
            for (int i = 0; i < fears.Count; i++)
                if (fears[i].prisonerId == prisonerId) return fears[i];
            return null;
        }

        // --- (b) recording ------------------------------------------------------------------------

        /// <summary>Pawn_HealthTracker.MakeDowned postfix: a hostile man brought down by a colony animal.</summary>
        public void Notify_Downed(Pawn man, DamageInfo? dinfo)
        {
            if (!OldScoresMod.Settings.beastMemory || man == null || man.Dead || !man.RaceProps.Humanlike) return;
            if (man.Faction == Faction.OfPlayer || !man.HostileTo(Faction.OfPlayer)) return;
            // alive: a boomalope's death blast downs him ticks after its Kill postfix cleared its records
            if (!(dinfo?.Instigator is Pawn animal) || animal.Dead || !IsColonyAnimal(animal)) return;
            FearRecord rec = GetFear(man.thingIDNumber);
            if (rec == null)
            {
                if (fears.Count >= OldScoresTuning.MaxFears) fears.RemoveAt(0);
                rec = new FearRecord { prisonerId = man.thingIDNumber };
                fears.Add(rec);
            }
            rec.prisonerName = man.LabelShort;
            rec.animalId = animal.thingIDNumber;
            rec.animalName = animal.LabelShort;
        }

        // --- the 60-tick pass (from OldScoresMapComponent) ----------------------------------------

        readonly Dictionary<int, Pawn> idLookup = new Dictionary<int, Pawn>();
        readonly HashSet<int> growledThisPass = new HashSet<int>();
        readonly Dictionary<int, Pawn> cleanupLookup = new Dictionary<int, Pawn>();

        public void BeastMemoryCheck(Map map)
        {
            if (!OldScoresMod.Settings.beastMemory || map == null) return;
            // id -> spawned pawn on this map, only for the ids we care about
            idLookup.Clear();
            IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < all.Count; i++)
            {
                Pawn p = all[i];
                if (p.RaceProps.Humanlike || p.RaceProps.Animal) idLookup[p.thingIDNumber] = p;
            }
            int now = Find.TickManager.TicksGame;
            growledThisPass.Clear();
            for (int i = 0; i < grudges.Count; i++)
            {
                GrudgeRecord g = grudges[i];
                if (now - g.lastGrowlTick < OldScoresTuning.GrowlCooldown && g.lastGrowlTick >= 0) continue;
                // one growl per animal per pass: a second bubble would replace the first, and three angry calls at once is noise
                if (growledThisPass.Contains(g.animalId)) continue;
                if (!idLookup.TryGetValue(g.animalId, out Pawn animal) || !idLookup.TryGetValue(g.humanId, out Pawn man)) continue;
                if (!IsColonyAnimal(animal) || animal.Dead || animal.Downed || !animal.Awake() || animal.InMentalState) continue;
                if (man.Dead || !Near(animal, man)) continue;
                g.lastGrowlTick = now;   // before the side effects: a throwing modded pawn must not retry every pass
                growledThisPass.Add(g.animalId);
                Growl(animal, man);
            }
            int today = now / GenDate.TicksPerDay;
            for (int i = 0; i < fears.Count; i++)
            {
                FearRecord f = fears[i];
                if (f.lastFearDay == today) continue;
                if (!idLookup.TryGetValue(f.prisonerId, out Pawn man) || !idLookup.TryGetValue(f.animalId, out Pawn animal)) continue;
                if (man.Dead || !man.IsPrisonerOfColony || man.needs?.mood == null || !man.Awake()) continue;   // he has to see it
                if (!IsColonyAnimal(animal) || animal.Dead || animal.Downed || !Near(animal, man)) continue;
                f.lastFearDay = today;
                man.needs.mood.thoughts.memories.TryGainMemory(OldScoresDefOf.OldScores_FearOfBeast, animal);
                if (man.guest != null) man.guest.resistance = Mathf.Max(0f, man.guest.resistance - OldScoresTuning.FearResistanceLoss);
            }
            idLookup.Clear();
        }

        /// <summary>The last growl bubble made (not saved; for tests). Cleared when a game starts or loads
        /// (OldScoresTracker constructor), so it never keeps an old map alive.</summary>
        public static MoteBubble LastGrowlBubble;

        public static bool Near(Pawn animal, Pawn man)
        {
            float r = OldScoresTuning.GrowlRadius;
            if (animal.Position.DistanceToSquared(man.Position) > r * r) return false;
            // the same indoor room (a prison cell, a barn) or plain sight; outdoors is one big "room", so sight decides there
            Room room = animal.GetRoom();
            if (room != null && !room.PsychologicallyOutdoors && room == man.GetRoom()) return true;
            return GenSight.LineOfSight(animal.Position, man.Position, animal.Map);
        }

        static void Growl(Pawn animal, Pawn man)
        {
            animal.caller?.DoCall(forceAggressive: true);
            LastGrowlBubble = MoteMaker.MakeInteractionBubble(animal, man, ThingDefOf.Mote_Speech, GrowlSymbol, GrowlRed);
        }

        // --- forgetting ---------------------------------------------------------------------------

        void Notify_BeastMemoryDied(Pawn victim)
        {
            if (grudges.Count == 0 && fears.Count == 0) return;
            int id = victim.thingIDNumber;
            grudges.RemoveAll(g => g.animalId == id || g.humanId == id);
            fears.RemoveAll(f => f.animalId == id || f.prisonerId == id);
        }

        void CleanupBeastMemory()
        {
            if (grudges.Count == 0 && fears.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            Dictionary<int, Pawn> found = cleanupLookup;
            found.Clear();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                IReadOnlyList<Pawn> all = maps[m].mapPawns.AllPawnsSpawned;
                for (int i = 0; i < all.Count; i++) found[all[i].thingIDNumber] = all[i];
            }
            grudges.RemoveAll(g =>
            {
                if (found.TryGetValue(g.animalId, out Pawn animal) && !IsColonyAnimal(animal)) return true;   // sold, released, gone wild
                if (!found.TryGetValue(g.humanId, out Pawn man)) return false;
                if (!man.IsFreeColonist) { g.colonistSinceTick = -1; return false; }   // free: a slave is not one of us
                if (g.colonistSinceTick == -1) g.colonistSinceTick = now;   // -1: not one of us (ticks can be negative only in tests)
                return now - g.colonistSinceTick >= OldScoresTuning.GrudgeForgiveTicks;   // one of us for 30 days
            });
            fears.RemoveAll(f =>
            {
                if (found.TryGetValue(f.animalId, out Pawn animal) && !IsColonyAnimal(animal)) return true;
                return found.TryGetValue(f.prisonerId, out Pawn man) && (man.IsColonist || (!man.IsPrisonerOfColony && !man.HostileTo(Faction.OfPlayer)));
            });
            found.Clear();
        }

        /// <summary>Inspect line for a colony animal that remembers someone.</summary>
        string GrudgeLine(Pawn animal)
        {
            if (!OldScoresMod.Settings.beastMemory || grudges.Count == 0 || !IsColonyAnimal(animal)) return null;
            List<string> names = null;
            for (int i = 0; i < grudges.Count; i++)
                if (grudges[i].animalId == animal.thingIDNumber) (names ??= new List<string>()).Add(grudges[i].humanName);
            return names == null ? null : "OldScores_Grudge_Inspect".Translate(names.ToCommaList(useAnd: true)).Resolve();
        }

        /// <summary>Test support.</summary>
        public void ClearBeastMemory() { grudges.Clear(); fears.Clear(); }

        /// <summary>Test support: runs the 2500-tick forgetting pass now.</summary>
        public void CleanupBeastMemoryNow() => CleanupBeastMemory();
    }

    /// <summary>Loaded on the main thread at startup (ContentFinder is not thread-safe; some mods tick off-thread).</summary>
    [StaticConstructorOnStartup]
    public static class BeastMemoryTextures
    {
        public static readonly Texture2D Growl = ContentFinder<Texture2D>.Get("Things/Mote/SpeechSymbols/Insult");
    }

    public class GrudgeRecord : IExposable
    {
        public int animalId;
        public string animalName;
        public int humanId;
        public string humanName;
        public int createdTick;
        public int lastGrowlTick = -1;
        public int colonistSinceTick = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref animalId, "animalId");
            Scribe_Values.Look(ref animalName, "animalName");
            Scribe_Values.Look(ref humanId, "humanId");
            Scribe_Values.Look(ref humanName, "humanName");
            Scribe_Values.Look(ref createdTick, "createdTick");
            Scribe_Values.Look(ref lastGrowlTick, "lastGrowlTick", -1);
            Scribe_Values.Look(ref colonistSinceTick, "colonistSinceTick", -1);
        }
    }

    public class FearRecord : IExposable
    {
        public int prisonerId;
        public string prisonerName;
        public int animalId;
        public string animalName;
        public int lastFearDay = -1;

        public void ExposeData()
        {
            Scribe_Values.Look(ref prisonerId, "prisonerId");
            Scribe_Values.Look(ref prisonerName, "prisonerName");
            Scribe_Values.Look(ref animalId, "animalId");
            Scribe_Values.Look(ref animalName, "animalName");
            Scribe_Values.Look(ref lastFearDay, "lastFearDay", -1);
        }
    }

    /// <summary>private Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff hediff) - Verse/Pawn_HealthTracker.cs:837.</summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_MakeDowned
    {
        // Postfixes also run when another mod's prefix skips the original, and on vanilla's "already downed"
        // error path: only a pawn that was standing before and is down after counts.
        public static void Prefix(Pawn ___pawn, out bool __state) { __state = ___pawn != null && !___pawn.Downed; }

        public static void Postfix(Pawn ___pawn, DamageInfo? dinfo, bool __state)
        {
            OldScoresTracker tracker = OldScoresTracker.Instance;
            if (tracker == null || dinfo == null || !__state || ___pawn == null || !___pawn.Downed) return;
            try { tracker.Notify_Downed(___pawn, dinfo); }
            catch (System.Exception ex) { Log.ErrorOnce("[Old Scores] downed handling failed: " + ex, 0x0D5C14); }
        }
    }
}
