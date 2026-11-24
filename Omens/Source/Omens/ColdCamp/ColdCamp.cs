using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Omens
{
    /// <summary>One raid that waited at a camp.</summary>
    public class CampEntry : IExposable
    {
        public int id;
        public string factionName;
        public int startTick, waitTicks, came, returned;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref startTick, "startTick");
            Scribe_Values.Look(ref waitTicks, "waitTicks");
            Scribe_Values.Look(ref came, "came");
            Scribe_Values.Look(ref returned, "returned");
        }

        public static string TheName(string name)
        {
            if (name.NullOrEmpty()) return "Omens_ColdCamp_Unknown".Translate();
            return name.StartsWith("the ", System.StringComparison.OrdinalIgnoreCase) ? name.CapitalizeFirst() : "Omens_ColdCamp_The".Translate(name).ToString();
        }

        public string Text(Map map)
        {
            string date = map != null && map.Tile.Valid
                ? GenDate.DateFullStringAt(GenDate.TickGameToAbs(startTick), Find.WorldGrid.LongLatOf(map.Tile))
                : "";
            return "Omens_ColdCamp_Entry".Translate(TheName(factionName), waitTicks.ToStringTicksToPeriod(allowSeconds: false), date, came, returned);
        }
    }

    /// <summary>The dead campfire a staged raid leaves behind. Holds the story of every raid that waited here.</summary>
    public class Building_ColdCamp : Building
    {
        List<CampEntry> entries = new List<CampEntry>();
        int nextEntryId = 1;

        public List<CampEntry> Entries => entries;

        public int AddEntry(StageRecord rec)
        {
            var e = new CampEntry
            {
                id = nextEntryId++,
                factionName = rec.factionName,
                startTick = rec.startTick,
                waitTicks = rec.WaitTicks,
                came = rec.Came,
                returned = rec.returned,
            };
            entries.Add(e);
            OmensStats.CampEntries++;
            return e.id;
        }

        public void UpdateEntry(int id, int came, int returned)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].id == id)
                {
                    entries[i].came = came;
                    entries[i].returned = returned;
                }
        }

        public override string GetInspectString()
        {
            var sb = new StringBuilder();
            string b = base.GetInspectString();
            if (!b.NullOrEmpty()) sb.Append(b);
            int shown = OmensTuning.CampEntriesShown;
            int first = Mathf.Max(0, entries.Count - shown);
            if (first > 0)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("Omens_ColdCamp_Earlier".Translate(first));
            }
            for (int i = first; i < entries.Count; i++)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(entries[i].Text(MapHeld));
            }
            return sb.ToString();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref entries, "omensEntries", LookMode.Deep);
            Scribe_Values.Look(ref nextEntryId, "omensNextEntryId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) entries ??= new List<CampEntry>();
        }
    }

    public static class ColdCamp
    {
        /// <summary>A stage ended: add the raid to a camp within 5 cells, or leave a new one at the stage point.</summary>
        public static void Notify_StageEnded(Map map, StageRecord rec)
        {
            if (!OmensMod.Settings.coldCamp || !rec.cell.IsValid || !rec.cell.InBounds(map)) return;
            if (rec.WaitTicks < OmensTuning.MinWaitTicks) return;
            Building_ColdCamp camp = CampNear(map, rec.cell);
            if (camp == null)
            {
                if (!TryFindCell(map, rec.cell, out IntVec3 cell)) return;
                camp = (Building_ColdCamp)GenSpawn.Spawn(ThingMaker.MakeThing(OmensDefOf.Omens_ColdCamp), cell, map);
                Ash(map, cell);
                OmensStats.CampsSpawned++;
            }
            rec.camp = camp;
            rec.campEntryId = camp.AddEntry(rec);
        }

        public static Building_ColdCamp CampNear(Map map, IntVec3 c)
        {
            List<Thing> camps = map.listerThings.ThingsOfDef(OmensDefOf.Omens_ColdCamp);
            Building_ColdCamp best = null;
            float bestD = OmensTuning.CampMergeRadius * OmensTuning.CampMergeRadius + 0.01f;
            for (int i = 0; i < camps.Count; i++)
            {
                float d = (camps[i].Position - c).LengthHorizontalSquared;
                if (d <= bestD && camps[i] is Building_ColdCamp b) { best = b; bestD = d; }
            }
            return best;
        }

        static bool Good(IntVec3 c, Map map) =>
            c.InBounds(map) && c.Standable(map) && c.GetEdifice(map) == null && c.GetFirstBuilding(map) == null
            && !c.GetTerrain(map).IsWater;

        public static bool TryFindCell(Map map, IntVec3 root, out IntVec3 cell)
        {
            if (Good(root, map)) { cell = root; return true; }
            // nearest first, then anything in the square
            foreach (IntVec3 c in GenRadial.RadialCellsAround(root, OmensTuning.CampPlaceRadius, false))
                if (Good(c, map)) { cell = c; return true; }
            return CellFinder.TryFindRandomCellNear(root, map, OmensTuning.CampPlaceRadius, c => Good(c, map), out cell);
        }

        static void Ash(Map map, IntVec3 center)
        {
            int made = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(center, 1.5f, true))
            {
                if (made >= 4) break;
                if (!c.InBounds(map) || !c.Standable(map) || c.GetTerrain(map).IsWater || !Rand.Chance(0.7f)) continue;
                if (FilthMaker.TryMakeFilth(c, map, ThingDefOf.Filth_Ash, 1)) made++;
            }
        }
    }
}
