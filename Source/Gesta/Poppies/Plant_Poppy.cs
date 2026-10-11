using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Gesta
{
    /// <summary>
    /// A poppy that grew where someone fell. Never eaten, never dies of age or light; it can burn
    /// or be cut. Remembers who, by whom and when.
    /// </summary>
    public class Plant_Poppy : Plant
    {
        public const float MournRadius = 4f;
        public PoppyMemory memory;

        public override float CurrentDyingDamagePerTick => 0f;
        public override bool IngestibleNow => false;

        public override string LabelNoCount =>
            memory?.victimName == null ? base.LabelNoCount : $"{base.LabelNoCount} ({memory.victimName})";

        public override void TickLong()
        {
            base.TickLong();
            if (Destroyed || memory == null || memory.mourners.Count == 0) return;
            for (int i = 0; i < memory.mourners.Count; i++)
            {
                Pawn p = memory.mourners[i];
                if (p == null || p.Dead || !p.Spawned || p.Map != Map || p.Downed) continue;
                if (!p.Position.InHorDistOf(Position, MournRadius)) continue;
                p.needs?.mood?.thoughts?.memories?.TryGainMemory(GestaDefOf.Gesta_VisitedPoppy);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            PoppyField field = Map?.GetComponent<PoppyField>();
            if (field != null && field.bloomedCount > 0) field.bloomedCount--;
            base.DeSpawn(mode);
        }

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            if (memory == null) return baseText;
            string date = GenDate.DateFullStringAt(memory.deathTicksAbs, Find.WorldGrid.LongLatOf(Map.Tile));
            string who = memory.colonist || memory.victimFaction.NullOrEmpty()
                ? memory.victimName
                : "Gesta_Poppy_VictimOfFaction".Translate(memory.victimName, memory.victimFaction).ToString();
            string line = memory.weaponLabel.NullOrEmpty()
                ? "Gesta_Poppy_Inspect".Translate(who, memory.killerName, date).ToString()
                : "Gesta_Poppy_InspectWeapon".Translate(who, memory.killerName, memory.weaponLabel, date).ToString();
            return baseText.NullOrEmpty() ? line : line + "\n" + baseText;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref memory, "gestaMemory");
        }
    }
}
