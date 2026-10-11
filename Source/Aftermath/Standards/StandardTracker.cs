using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Aftermath
{
    /// <summary>What the mod knows about one hostile raid lord on a home map.</summary>
    public class LordRecord : IExposable
    {
        public int lordId = -1;
        public int leaderId = -1;
        public bool dropped;
        public List<int> veterans = new List<int>();
        public Building_CapturedStandard standard;   // once dropped, veterans keep flowing into it

        public void AddVeteran(int id)
        {
            if (!veterans.Contains(id)) veterans.Add(id);
            if (standard != null && !standard.Destroyed && !standard.veterans.Contains(id)) standard.veterans.Add(id);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref lordId, "lordId", -1);
            Scribe_Values.Look(ref leaderId, "leaderId", -1);
            Scribe_Values.Look(ref dropped, "dropped");
            Scribe_Collections.Look(ref veterans, "veterans", LookMode.Value);
            Scribe_References.Look(ref standard, "standard");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && veterans == null) veterans = new List<int>();
        }
    }

    /// <summary>
    /// Names each hostile humanlike raid's leader and drops the faction's standard when he falls.
    /// Every StandardScanInterval ticks it records new raid lords and forgets lords that are gone.
    /// </summary>
    public class StandardTracker : MapComponent
    {
        List<LordRecord> records = new List<LordRecord>();

        public StandardTracker(Map map) : base(map) { }

        public static StandardTracker For(Map map) => map?.GetComponent<StandardTracker>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "lords", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (records == null) records = new List<LordRecord>();
                records.RemoveAll(r => r == null);
            }
        }

        public override void MapComponentTick()
        {
            int interval = AftermathTuning.StandardScanInterval;
            if ((Find.TickManager.TicksGame + map.uniqueID) % interval != 0) return;
            Scan();
        }

        public void Scan()
        {
            List<Lord> lords = map.lordManager.lords;
            records.RemoveAll(r => !LordAlive(r.lordId));   // pruned even while the feature is off
            if (!AftermathMod.Settings.trophyStandards || !map.IsPlayerHome) return;
            for (int i = 0; i < lords.Count; i++)
            {
                Lord lord = lords[i];
                if (Get(lord) == null && Eligible(lord) && HumanlikeMembers(lord) >= AftermathTuning.MinRaidMembers)
                    Create(lord);
            }
        }

        bool LordAlive(int id)
        {
            List<Lord> lords = map.lordManager.lords;
            for (int i = 0; i < lords.Count; i++) if (lords[i].loadID == id) return true;
            return false;
        }

        public LordRecord Get(Lord lord)
        {
            for (int i = 0; i < records.Count; i++) if (records[i].lordId == lord.loadID) return records[i];
            return null;
        }

        /// <summary>The leader the tracker has named for this lord (null if none yet or he is gone).</summary>
        public Pawn LeaderOf(Lord lord)
        {
            LordRecord rec = Get(lord);
            if (rec == null) return null;
            foreach (Pawn p in lord.ownedPawns) if (p.thingIDNumber == rec.leaderId) return p;
            return null;
        }

        public static bool Eligible(Lord lord)
        {
            Faction f = lord?.faction;
            // a prison break makes a lord of the prisoners' own hostile faction (PrisonBreakUtility.cs:324): not a raid
            return f != null && !f.IsPlayer && f.def.humanlikeFaction && f.HostileTo(Faction.OfPlayer)
                && !(lord.LordJob is LordJob_PrisonBreak);
        }

        static int HumanlikeMembers(Lord lord)
        {
            int n = 0;
            foreach (Pawn p in lord.ownedPawns) if (p.RaceProps.Humanlike && !p.Dead) n++;
            return n;
        }

        LordRecord Create(Lord lord)
        {
            Pawn leader = ChooseLeader(lord);
            if (leader == null) return null;
            var rec = new LordRecord { lordId = lord.loadID, leaderId = leader.thingIDNumber };
            records.Add(rec);
            return rec;
        }

        /// <summary>The faction's real leader if he is in the lord, else the strongest humanlike (combatPower).</summary>
        static Pawn ChooseLeader(Lord lord)
        {
            Pawn real = lord.faction?.leader;
            Pawn best = null;
            foreach (Pawn p in lord.ownedPawns)
            {
                if (p == null || p.Dead || !p.RaceProps.Humanlike) continue;
                if (p == real) return p;
                if (best == null) { best = p; continue; }
                float a = p.kindDef?.combatPower ?? 0f, b = best.kindDef?.combatPower ?? 0f;
                if (a > b || (a == b && p.thingIDNumber < best.thingIDNumber)) best = p;
            }
            return best;
        }

        LordRecord GetOrCreate(Lord lord)
        {
            LordRecord rec = Get(lord);
            if (rec != null) return rec;
            if (!map.IsPlayerHome || !Eligible(lord) || HumanlikeMembers(lord) < AftermathTuning.MinRaidMembers) return null;
            return Create(lord);
        }

        // ------------------------------------------------------------------ events (from Patches)

        /// <summary>A pawn is about to be downed or killed (prefixes). Drops the standard if he leads his raid.</summary>
        public static void Notify_Falling(Pawn p, DamageInfo? dinfo)
        {
            if (!AftermathMod.Settings.trophyStandards || !p.RaceProps.Humanlike) return;
            Lord lord = p.GetLord();
            if (lord == null || !Eligible(lord)) return;
            Map m = p.MapHeld;
            if (m == null || !m.IsPlayerHome) return;
            StandardTracker tracker = For(m);
            LordRecord rec = tracker?.GetOrCreate(lord);
            if (rec == null || rec.dropped || rec.leaderId != p.thingIDNumber) return;
            rec.dropped = true;
            Thing by = dinfo?.Instigator;
            if (by is Pawn killer && killer.IsColonist) rec.AddVeteran(killer.thingIDNumber);
            rec.standard = tracker.Drop(p, lord, by, rec);
        }

        /// <summary>Colonist hurt a raider or a raider hurt a colonist: the colonist is a veteran of that raid.</summary>
        public static void Notify_Damage(Pawn victim, Pawn attacker)
        {
            if (!AftermathMod.Settings.trophyStandards) return;
            Pawn colonist, raider;
            if (attacker.IsColonist && !victim.IsColonist) { colonist = attacker; raider = victim; }
            else if (victim.IsColonist && !attacker.IsColonist) { colonist = victim; raider = attacker; }
            else return;
            Lord lord = raider.GetLord();
            if (lord == null || !Eligible(lord)) return;
            LordRecord rec = For(raider.MapHeld)?.GetOrCreate(lord);
            rec?.AddVeteran(colonist.thingIDNumber);
        }

        Building_CapturedStandard Drop(Pawn leader, Lord lord, Thing by, LordRecord rec)
        {
            var standard = (Building_CapturedStandard)ThingMaker.MakeThing(AftermathDefOf.Aftermath_CapturedStandard);
            standard.Init(leader, by, rec.veterans, map);
            MinifiedThing mini = standard.MakeMinified();
            if (mini == null) return null;
            if (!GenPlace.TryPlaceThing(mini, leader.PositionHeld, map, ThingPlaceMode.Near))
            {
                mini.Destroy();
                return null;
            }
            Messages.Message("Aftermath_StandardTaken".Translate(standard.factionName, standard.leaderName, standard.killerName),
                mini, MessageTypeDefOf.PositiveEvent);
            return standard;
        }
    }
}
