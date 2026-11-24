using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Aftermath
{
    /// <summary>Credit rules of The Old Sentinel (DESIGN.md, Feature 6). Called from the shared Harmony patches.</summary>
    public static class SentinelUtility
    {
        static bool On => AftermathMod.Settings.oldSentinels;

        /// <summary>
        /// The player turret a blow belongs to. Unmanned: the instigator is the turret
        /// (Verb_LaunchProjectile.cs:91-97). Manned: the instigator is the gunner and the "weapon" is the
        /// turret's own def (equipmentSource = caster), so it is the gunner's lastMannedThing of that def.
        /// </summary>
        public static CompOldSentinel Resolve(DamageInfo dinfo, out Pawn gunner)
        {
            gunner = null;
            Thing inst = dinfo.Instigator;
            if (inst == null) return null;
            if (inst is Pawn p)
            {
                ThingDef w = dinfo.Weapon;
                if (w == null || w.category != ThingCategory.Building || !w.HasComp(typeof(CompOldSentinel))) return null;
                Thing manned = p.mindState?.lastMannedThing;
                if (manned == null || manned.def != w || manned.Destroyed) return null;
                CompOldSentinel mc = Eligible(manned.TryGetComp<CompOldSentinel>());
                if (mc != null) gunner = p;
                return mc;
            }
            if (inst is Building_Turret) return Eligible(inst.TryGetComp<CompOldSentinel>());
            return null;
        }

        static CompOldSentinel Eligible(CompOldSentinel c) => c != null && c.parent.Faction == Faction.OfPlayer ? c : null;

        /// <summary>An enemy worth counting: hostile to the player now, not the player's own, not his prisoner.</summary>
        public static bool IsFoe(Pawn v)
        {
            if (v == null || v.Faction == Faction.OfPlayer) return false;
            if (v.HostFaction == Faction.OfPlayer) return false;
            return v.HostileTo(Faction.OfPlayer);
        }

        /// <summary>MakeDowned prefix / Pawn.Kill prefix (victim not yet downed or dead).</summary>
        public static void Notify_Takedown(Pawn victim, DamageInfo? dinfo)
        {
            if (!On || dinfo == null || victim.Dead || victim.Downed || !IsFoe(victim)) return;
            CompOldSentinel c = Resolve(dinfo.Value, out Pawn gunner);
            if (c == null) return;
            c.CreditTakedown(victim);
            if (gunner != null && gunner.IsFreeColonist) c.AddComrade(gunner);
        }

        /// <summary>Pawn.PostApplyDamage postfix: engagements, gunners, colonists fighting beside a turret.</summary>
        public static void Notify_Damage(Pawn victim, DamageInfo dinfo)
        {
            if (!On || dinfo.Instigator == null || !IsFoe(victim)) return;
            CompOldSentinel c = Resolve(dinfo, out Pawn gunner);
            if (c != null)
            {
                c.Engage();
                if (gunner != null && gunner.IsFreeColonist) c.AddComrade(gunner);
                return;
            }
            if (!(dinfo.Instigator is Pawn attacker) || !attacker.IsFreeColonist || !attacker.Spawned) return;
            SentinelTracker t = SentinelTracker.For(attacker.Map);
            if (t == null || t.sentinels.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            float r2 = AftermathTuning.SentinelComradeRadius * AftermathTuning.SentinelComradeRadius;
            for (int i = 0; i < t.sentinels.Count; i++)
            {
                CompOldSentinel s = t.sentinels[i];
                if (!s.RecentlyEngaged(now) || s.parent.Faction != Faction.OfPlayer) continue;
                if ((s.parent.Position - attacker.Position).LengthHorizontalSquared > r2) continue;
                s.AddComrade(attacker);
            }
        }

        /// <summary>Name validator: no other sentinel on any map carries it.</summary>
        public static bool NameFree(string name)
        {
            if (Find.Maps == null) return true;
            foreach (Map m in Find.Maps)
            {
                SentinelTracker t = SentinelTracker.For(m);
                if (t == null) continue;
                foreach (CompOldSentinel s in t.sentinels)
                    if (s.Named && string.Equals(s.SentinelName, name, System.StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        /// <summary>Who destroyed it, for the letter and the remains: a faction name, else a short label, else null.</summary>
        public static string Destroyer(DamageInfo? dinfo)
        {
            Thing inst = dinfo?.Instigator;
            if (inst == null) return null;
            if (inst.Faction != null && !inst.Faction.IsPlayer && !inst.Faction.Name.NullOrEmpty()) return inst.Faction.Name;
            return AftermathUtil.NameOf(inst);
        }

        static readonly List<Pawn> tmpPawns = new List<Pawn>();

        /// <summary>Notify_Killed of a named turret: remains on its cell, mourning, a letter.</summary>
        public static void Fall(CompOldSentinel c, Map map, DamageInfo? dinfo)
        {
            if (!On || !c.Named || map == null) return;
            SentinelRecord r = c.rec;
            Thing turret = c.parent;
            string by = Destroyer(dinfo);

            var remains = (SentinelRemains)ThingMaker.MakeThing(AftermathDefOf.Aftermath_SentinelRemains);
            remains.Init(turret.def, r, by, map);
            if (!GenPlace.TryPlaceThing(remains, turret.Position, map, ThingPlaceMode.Near))
            {
                remains.Destroy();
                remains = null;
            }

            tmpPawns.Clear();
            tmpPawns.AddRange(map.mapPawns.FreeColonistsSpawned);
            foreach (Pawn p in tmpPawns)
            {
                if (p.needs?.mood?.thoughts?.memories == null) continue;
                ThoughtDef def = r.HasComrade(p.thingIDNumber) ? AftermathDefOf.Aftermath_SentinelFellComrade : AftermathDefOf.Aftermath_SentinelFell;
                var thought = (Thought_SentinelFell)ThoughtMaker.MakeThought(def);
                thought.sentinelName = r.name;
                p.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
            tmpPawns.Clear();

            TaggedString text = by.NullOrEmpty()
                ? "Aftermath_SentinelFellText".Translate(r.name, turret.def.label, r.battles, r.takedowns)
                : "Aftermath_SentinelFellTextBy".Translate(r.name, turret.def.label, r.battles, r.takedowns, by);
            Find.LetterStack.ReceiveLetter("Aftermath_SentinelFellLabel".Translate(r.name), text,
                LetterDefOf.NegativeEvent, remains != null ? new LookTargets(remains) : new LookTargets(turret.Position, map));
        }
    }
}
