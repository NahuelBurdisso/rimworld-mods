using RimWorld;
using Verse;

namespace ReactorSlag
{
    /// <summary>
    /// On every mechanoid race. When a heavy mech or a boss dies with its corpse on the map, spills a slag core
    /// under the corpse. Holds no saved state.
    /// </summary>
    public class CompReactorSlag : ThingComp
    {
        public const float MinBodySize = 2.5f;

        /// <summary>The core a dying pawn leaves: large for bosses, small for heavy mechs, null for the rest.</summary>
        public static ThingDef CoreDefFor(Pawn pawn)
        {
            if (pawn?.RaceProps == null || !pawn.RaceProps.IsMechanoid) return null;
            if (pawn.kindDef != null && pawn.kindDef.isBoss) return ReactorSlagDefOf.ReactorSlag_CoreLarge;
            return pawn.BodySize >= MinBodySize ? ReactorSlagDefOf.ReactorSlag_Core : null;
        }

        // ThingWithComps.Kill (Verse/ThingWithComps.cs:308) -> called at the end of Pawn.Kill, after the corpse
        // has been placed.
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            if (prevMap == null || !(parent is Pawn pawn)) return;
            // disassembly in a mech bay kills with forceNoDeathNotification and destroys the corpse at once
            // (RimWorld/JobDriver_DisassembleMech.cs:38-41): a dismantled mech spills nothing
            if (pawn.forceNoDeathNotification) return;
            ThingDef coreDef = CoreDefFor(pawn);
            if (coreDef == null) return;
            Corpse corpse = pawn.Corpse;
            if (corpse == null || !corpse.Spawned || corpse.Map != prevMap) return;
            try
            {
                SpillAt(coreDef, corpse.Position, prevMap);
            }
            catch (System.Exception e)
            {
                // never let a failure here break the rest of Pawn.Kill
                Log.ErrorOnce("[Reactor Slag] could not spill a core for " + pawn + ": " + e, 0x51A6C0E);
            }
        }

        public static ReactorSlagCore SpillAt(ThingDef coreDef, IntVec3 cell, Map map)
        {
            if (!cell.InBounds(map)) return null;
            var core = (ReactorSlagCore)ThingMaker.MakeThing(coreDef);
            GenSpawn.Spawn(core, cell, map);
            return core;
        }
    }
}
