using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    public static class AftermathUtil
    {
        /// <summary>Longitude/latitude of the map's tile, for the date strings. WorldGrid.LongLatOf(PlanetTile).</summary>
        public static Vector2 LongLat(Map map)
        {
            if (map == null || Find.WorldGrid == null) return Vector2.zero;
            return Find.WorldGrid.LongLatOf(map.Tile);
        }

        /// <summary>"5 Aprimay 5501" - GenDate.DateFullStringAt(long absTicks, Vector2 location).</summary>
        public static string Date(int gameTick, Vector2 longLat)
        {
            return GenDate.DateFullStringAt(GenDate.TickGameToAbs(gameTick), longLat);
        }

        /// <summary>Who did it: a pawn's short name, a turret's label, else "unknown hands".</summary>
        public static string NameOf(Thing t)
        {
            if (t == null) return "Aftermath_UnknownHands".Translate();
            if (t is Pawn p) return p.LabelShort;
            return t.LabelShort;
        }

        /// <summary>A hostile humanlike that is not the player's: raider, enemy faction member.</summary>
        public static bool IsHostileHumanlike(Pawn p)
        {
            return p != null && p.RaceProps.Humanlike && p.Faction != null && !p.Faction.IsPlayer
                && p.HostileTo(Faction.OfPlayer);
        }

        public static string Join(List<string> names)
        {
            return names.ToCommaList(useAnd: true);
        }
    }
}
