using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Gesta
{
    /// <summary>What a poppy remembers about the one who fell where it grows.</summary>
    public class PoppyMemory : IExposable
    {
        public IntVec3 cell;
        public bool colonist;
        public string victimName;
        public string victimFaction;
        public string killerName;
        public string weaponLabel;
        public int deathTicksAbs;
        public int recordedTick;
        public List<Pawn> mourners = new List<Pawn>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref cell, "cell");
            Scribe_Values.Look(ref colonist, "colonist");
            Scribe_Values.Look(ref victimName, "victimName");
            Scribe_Values.Look(ref victimFaction, "victimFaction");
            Scribe_Values.Look(ref killerName, "killerName");
            Scribe_Values.Look(ref weaponLabel, "weaponLabel");
            Scribe_Values.Look(ref deathTicksAbs, "deathTicksAbs");
            Scribe_Values.Look(ref recordedTick, "recordedTick");
            Scribe_Collections.Look(ref mourners, "mourners", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                mourners ??= new List<Pawn>();
                mourners.RemoveAll(p => p == null);
            }
        }
    }

    /// <summary>
    /// Battlefield Poppies: remembers combat deaths on open fertile ground and, after a few days in
    /// growing season, makes a poppy bloom there. See DESIGN.md "Feature 3".
    /// </summary>
    public class PoppyField : MapComponent
    {
        public const float MinFertility = 0.5f;
        public const int MinAgeTicks = GenDate.TicksPerDay * 3;
        public const int GiveUpTicks = GenDate.TicksPerDay * 60;
        public const int MaxPerMap = 80;
        const int CheckInterval = 2500;

        List<PoppyMemory> pending = new List<PoppyMemory>();
        public int bloomedCount;   // saved; together with pending this caps a map

        public IReadOnlyList<PoppyMemory> Pending => pending;

        public PoppyField(Map map) : base(map) { }

        public void Notify_Killed(Pawn victim, KillContext ctx)
        {
            if (!GestaMod.Settings.poppies) return;
            if (victim.RaceProps == null || !victim.RaceProps.Humanlike || ctx.killer == null) return;
            IntVec3 c = ctx.position;
            if (!c.InBounds(map) || c.Roofed(map) || map.fertilityGrid.FertilityAt(c) < MinFertility) return;
            if (pending.Count + bloomedCount >= MaxPerMap || NearExisting(c)) return;

            var mem = new PoppyMemory
            {
                cell = c,
                colonist = victim.Faction == Faction.OfPlayer,
                victimName = victim.LabelShort,
                victimFaction = victim.Faction?.Name,
                killerName = ctx.killer.LabelShort,
                weaponLabel = ctx.killer.equipment?.Primary?.def.label,
                deathTicksAbs = GenTicks.TicksAbs,
                recordedTick = Find.TickManager.TicksGame,
            };
            if (mem.colonist)
            {
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p == victim || p.Dead || p.relations == null) continue;
                    if (p.relations.OpinionOf(victim) >= 20 || p.relations.DirectRelationExists(PawnRelationDefOf.Spouse, victim)
                        || PawnRelationUtility.GetMostImportantRelation(p, victim) != null)
                        mem.mourners.Add(p);
                }
            }
            pending.Add(mem);
        }

        bool NearExisting(IntVec3 c)
        {
            foreach (PoppyMemory m in pending)
                if (m.cell.InHorDistOf(c, 1.5f)) return true;
            foreach (IntVec3 n in GenRadial.RadialCellsAround(c, 1.5f, useCenter: true))
                if (n.InBounds(map) && n.GetPlant(map) is Plant_Poppy) return true;
            return false;
        }

        public override void MapComponentTick()
        {
            if (pending.Count == 0 || Find.TickManager.TicksGame % CheckInterval != 0) return;
            TryBloomAll(force: false);
        }

        /// <param name="force">Tests: ignore the minimum age.</param>
        public void TryBloomAll(bool force)
        {
            int now = Find.TickManager.TicksGame;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PoppyMemory m = pending[i];
                int age = now - m.recordedTick;
                if (!force && age < MinAgeTicks) continue;
                ThingDef def = m.colonist ? GestaDefOf.Gesta_PoppyRed : GestaDefOf.Gesta_PoppyWhite;
                if (!force && !PlantUtility.GrowthSeasonNow(m.cell, map, def))
                {
                    if (age > GiveUpTicks) pending.RemoveAt(i);
                    continue;
                }
                IntVec3 cell = FindBloomCell(m.cell, def);
                if (!cell.IsValid)
                {
                    if (age > GiveUpTicks) pending.RemoveAt(i);
                    continue;
                }
                Bloom(m, def, cell);
                pending.RemoveAt(i);
            }
        }

        IntVec3 FindBloomCell(IntVec3 origin, ThingDef def)
        {
            foreach (IntVec3 c in GenRadial.RadialCellsAround(origin, 2f, useCenter: true))
                if (CanBloomAt(c, def)) return c;
            return IntVec3.Invalid;
        }

        bool CanBloomAt(IntVec3 c, ThingDef def)
        {
            if (!c.InBounds(map) || c.Roofed(map) || map.fertilityGrid.FertilityAt(c) < MinFertility) return false;
            if (c.GetEdifice(map) != null || c.GetZone(map) is Zone_Growing) return false;
            Plant existing = c.GetPlant(map);
            if (existing != null && (existing.def.plant.IsTree || existing is Plant_Poppy || existing.def.plant.sowTags.Count > 0 && existing.sown))
                return false;
            List<Thing> things = c.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
                if (things[i].def.category == ThingCategory.Building || things[i].def.passability == Traversability.Impassable) return false;
            return true;
        }

        void Bloom(PoppyMemory m, ThingDef def, IntVec3 cell)
        {
            cell.GetPlant(map)?.Destroy();
            var poppy = (Plant_Poppy)ThingMaker.MakeThing(def);
            poppy.memory = m;
            poppy.Growth = 0.35f;
            GenSpawn.Spawn(poppy, cell, map);
            bloomedCount++;
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref pending, "pending", LookMode.Deep);
            Scribe_Values.Look(ref bloomedCount, "bloomedCount");
            if (Scribe.mode == LoadSaveMode.PostLoadInit) pending ??= new List<PoppyMemory>();
        }
    }
}
