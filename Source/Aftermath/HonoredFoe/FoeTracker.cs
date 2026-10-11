using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    /// <summary>A colonist a hostile brought down (and maybe killed).</summary>
    public class FoeVictim : IExposable
    {
        public int id;
        public string name;
        public bool killed;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref killed, "killed");
        }
    }

    /// <summary>What a living hostile has done to the colony so far.</summary>
    public class FoeDeeds : IExposable
    {
        public int foeId;
        public int lastTick;
        public List<FoeVictim> victims = new List<FoeVictim>();
        // remembered when he himself goes down: MakeDowned drops his weapon (Pawn_HealthTracker.cs:854-859)
        // before he bleeds out or is executed, and that death has no instigator
        public bool wentDown;
        public ThingDef weaponDef, weaponStuff;
        public string downedBy;

        public int Kills
        {
            get { int n = 0; foreach (FoeVictim v in victims) if (v.killed) n++; return n; }
        }

        public bool Worthy => victims.Count >= AftermathTuning.WorthyVictims || Kills >= AftermathTuning.WorthyKills;

        public void Add(Pawn victim, bool killed)
        {
            lastTick = Find.TickManager.TicksGame;
            foreach (FoeVictim v in victims)
            {
                if (v.id != victim.thingIDNumber) continue;
                v.killed |= killed;
                return;
            }
            victims.Add(new FoeVictim { id = victim.thingIDNumber, name = victim.LabelShort, killed = killed });
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref foeId, "foeId");
            Scribe_Values.Look(ref lastTick, "lastTick");
            Scribe_Collections.Look(ref victims, "victims", LookMode.Deep);
            Scribe_Values.Look(ref wentDown, "wentDown");
            Scribe_Defs.Look(ref weaponDef, "weaponDef");
            Scribe_Defs.Look(ref weaponStuff, "weaponStuff");
            Scribe_Values.Look(ref downedBy, "downedBy");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && victims == null) victims = new List<FoeVictim>();
        }
    }

    /// <summary>A worthy foe who died: what goes on his grave.</summary>
    public class FoeRecord : IExposable
    {
        public int foeId;
        public string name;
        public string factionName;
        public ThingDef weaponDef, weaponStuff, helmetDef, helmetStuff;
        public int deathTick;
        public Vector2 longLat;
        public string killerName;
        public bool buried;
        public List<FoeVictim> victims = new List<FoeVictim>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref foeId, "foeId");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Defs.Look(ref weaponDef, "weaponDef");
            Scribe_Defs.Look(ref weaponStuff, "weaponStuff");
            Scribe_Defs.Look(ref helmetDef, "helmetDef");
            Scribe_Defs.Look(ref helmetStuff, "helmetStuff");
            Scribe_Values.Look(ref deathTick, "deathTick");
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Values.Look(ref killerName, "killerName");
            Scribe_Values.Look(ref buried, "buried");
            Scribe_Collections.Look(ref victims, "victims", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && victims == null) victims = new List<FoeVictim>();
        }
    }

    /// <summary>Deeds of living hostiles and the graves-to-be of the worthy dead. Saved with the game.</summary>
    public class FoeTracker : GameComponent
    {
        List<FoeDeeds> deeds = new List<FoeDeeds>();
        List<FoeRecord> records = new List<FoeRecord>();

        public FoeTracker(Game game) { }

        public static FoeTracker Instance => Current.Game?.GetComponent<FoeTracker>();

        public IReadOnlyList<FoeRecord> Records => records;

        public FoeDeeds DeedsOf(Pawn p)
        {
            for (int i = 0; i < deeds.Count; i++) if (deeds[i].foeId == p.thingIDNumber) return deeds[i];
            return null;
        }

        public FoeRecord RecordOf(int pawnId)
        {
            for (int i = 0; i < records.Count; i++) if (records[i].foeId == pawnId) return records[i];
            return null;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref deeds, "deeds", LookMode.Deep);
            Scribe_Collections.Look(ref records, "records", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (deeds == null) deeds = new List<FoeDeeds>();
                if (records == null) records = new List<FoeRecord>();
                deeds.RemoveAll(d => d == null);
                records.RemoveAll(r => r == null);
            }
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % AftermathTuning.FoeCleanupInterval != 0) return;
            deeds.RemoveAll(d => now - d.lastTick > AftermathTuning.DeedsForgetTicks);
            if (records.Count == 0) return;
            RefreshBuried();
            records.RemoveAll(r => !r.buried && now - r.deathTick > AftermathTuning.UnburiedRecordForgetTicks);
        }

        static readonly HashSet<int> tmpInGraves = new HashSet<int>();

        /// <summary>A record is buried while some grave on a map holds his corpse; emptied graves let it expire.</summary>
        public void RefreshBuried()
        {
            tmpInGraves.Clear();
            foreach (Map m in Find.Maps)
            {
                List<Thing> graves = m.listerThings.ThingsInGroup(ThingRequestGroup.Grave);
                for (int i = 0; i < graves.Count; i++)
                {
                    Pawn dead = (graves[i] as Building_Grave)?.Corpse?.InnerPawn;
                    if (dead != null) tmpInGraves.Add(dead.thingIDNumber);
                }
            }
            foreach (FoeRecord r in records) r.buried = tmpInGraves.Contains(r.foeId);
        }

        static bool IsVictim(Pawn p) => p != null && p.IsColonist && p.RaceProps.Humanlike;

        /// <summary>MakeDowned prefix: a colonist goes down to a hostile's blow.</summary>
        public void Notify_Downed(Pawn victim, DamageInfo? dinfo)
        {
            if (!AftermathMod.Settings.honoredFoes) return;
            FoeDeeds own = DeedsOf(victim);
            if (own != null && !own.wentDown)
            {
                // the foe himself goes down: keep what he fought with and who stopped him
                own.wentDown = true;
                own.weaponDef = victim.equipment?.Primary?.def;
                own.weaponStuff = victim.equipment?.Primary?.Stuff;
                own.downedBy = dinfo?.Instigator != null ? AftermathUtil.NameOf(dinfo.Value.Instigator) : null;
            }
            if (!IsVictim(victim)) return;
            if (!(dinfo?.Instigator is Pawn foe) || !AftermathUtil.IsHostileHumanlike(foe)) return;
            Credit(foe, victim, killed: false);
        }

        /// <summary>Pawn.Kill prefix: credits a hostile who kills a colonist; snapshots a worthy foe who dies.</summary>
        public void Notify_PawnDying(Pawn p, DamageInfo? dinfo)
        {
            if (!AftermathMod.Settings.honoredFoes) return;
            if (IsVictim(p) && dinfo?.Instigator is Pawn foe && AftermathUtil.IsHostileHumanlike(foe))
                Credit(foe, p, killed: true);
            FoeDeeds d = DeedsOf(p);
            if (d == null) return;
            deeds.Remove(d);
            if (p.Faction == Faction.OfPlayer) return;   // captured and recruited since: not a foe any more
            if (d.Worthy && RecordOf(p.thingIDNumber) == null) records.Add(Snapshot(p, d, dinfo));
        }

        void Credit(Pawn foe, Pawn victim, bool killed)
        {
            FoeDeeds d = DeedsOf(foe);
            if (d == null) deeds.Add(d = new FoeDeeds { foeId = foe.thingIDNumber });
            d.Add(victim, killed);
        }

        static FoeRecord Snapshot(Pawn p, FoeDeeds d, DamageInfo? dinfo)
        {
            var rec = new FoeRecord
            {
                foeId = p.thingIDNumber,
                name = p.Name?.ToStringShort ?? p.LabelShort,
                factionName = p.Faction?.Name ?? "?",
                deathTick = Find.TickManager.TicksGame,
                longLat = AftermathUtil.LongLat(p.MapHeld),
                // the blow that downed him counts as the one that stopped him; else the killing blow
                killerName = d.downedBy ?? AftermathUtil.NameOf(dinfo?.Instigator),
                victims = d.victims,
            };
            Thing weapon = p.equipment?.Primary;
            if (weapon != null) { rec.weaponDef = weapon.def; rec.weaponStuff = weapon.Stuff; }
            else if (d.weaponDef != null) { rec.weaponDef = d.weaponDef; rec.weaponStuff = d.weaponStuff; }
            else if (p.mindState?.droppedWeapon != null) { rec.weaponDef = p.mindState.droppedWeapon.def; rec.weaponStuff = p.mindState.droppedWeapon.Stuff; }
            if (p.apparel != null)
            {
                foreach (Apparel a in p.apparel.WornApparel)
                {
                    if (a.def.apparel?.LastLayer != ApparelLayerDefOf.Overhead) continue;
                    rec.helmetDef = a.def;
                    rec.helmetStuff = a.Stuff;
                    break;
                }
            }
            return rec;
        }
    }
}
