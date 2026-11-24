using System.Linq;
using Omens;
using RimWorld;
using Verse;

namespace OmensTest
{
    /// <summary>
    /// Rain Reveals the Unseen (DESIGN.md test plan 1-9). A real Revenant (Faction.OfEntities) is teleported to an
    /// open, a roofed and a fogged cell under clear weather, rain and snow; RainWatcher.Pulse() is called directly
    /// after each change, plus one passive window where only the 30-tick pulse runs.
    /// </summary>
    public class RainHarness : HarnessBase
    {
        public RainHarness(Game game) { }

        protected override int StartTick => 300;
        protected override string Name => "Rain";

        RainWatcher rw;
        Pawn revenant, friend;
        IntVec3 open, roofed, fogged, friendCell;
        int statsAtPassive;

        protected override string Facts() =>
            $"revenant {revenant?.Position} spawned {revenant?.Spawned} hidden {revenant?.IsHiddenFromPlayer()} weather {map?.weatherManager.curWeather} splashes {OmensStats.RainSplashes} puffs {OmensStats.SnowPuffs}";

        void Weather(string defName)
        {
            WeatherDef w = DefDatabase<WeatherDef>.GetNamed(defName);
            map.weatherManager.curWeather = w;
            map.weatherManager.lastWeather = w;
            map.weatherManager.curWeatherAge = 5000;
        }

        int PulseAt(Pawn p, IntVec3 c)
        {
            if (p.Position != c) TestUtil.Teleport(p, c);
            return rw.Pulse(forceScan: true);
        }

        static bool OpenCell(IntVec3 c, Map m) =>
            c.InBounds(m) && c.Standable(m) && !c.Roofed(m) && !c.Fogged(m) && c.GetFirstPawn(m) == null && !c.GetTerrain(m).IsWater;

        protected override void Step()
        {
            switch (phase)
            {
                case 0:
                    if (!ModsConfig.AnomalyActive)
                    {
                        Log.Message("[Test] Rain: Anomaly not active - nothing to reveal, harness skipped");
                        Finish();
                        return;
                    }
                    rw = map.GetComponent<RainWatcher>();
                    Kit.Check(rw != null, "Rain: RainWatcher map component exists");
                    OmensMod.Settings.rainReveals = true;
                    IntVec3 anchor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault()?.Position ?? map.Center;
                    // three open cells in a row, away from the colonists
                    open = CellFinder.RandomClosewalkCellNear(anchor + new IntVec3(12, 0, 0), map, 10, c => OpenCell(c, map)
                        && OpenCell(c + IntVec3.East, map) && OpenCell(c + IntVec3.East * 2, map) && OpenCell(c + IntVec3.North, map));
                    roofed = open + IntVec3.East;
                    fogged = open + IntVec3.East * 2;
                    friendCell = open + IntVec3.North;
                    map.roofGrid.SetRoof(roofed, RoofDefOf.RoofConstructed);
                    map.fogGrid.Refog(CellRect.SingleCell(fogged));
                    revenant = PawnGenerator.GeneratePawn(PawnKindDefOf.Revenant, Faction.OfEntities);
                    GenSpawn.Spawn(revenant, open, map);
                    Log.Message($"[Test] Rain: open {open}, roofed {roofed} ({roofed.Roofed(map)}), fogged {fogged} ({fogged.Fogged(map)})");
                    Next();
                    break;
                case 1:
                    if (InPhase < 2) return;
                    Kit.Check(revenant.IsHiddenFromPlayer() && revenant.HostileTo(Faction.OfPlayer), "Rain: the revenant is hidden from the player and hostile (precondition)");
                    Weather("Clear");
                    Kit.Check(PulseAt(revenant, open) == 0, "Rain: clear weather -> no splashes");
                    Weather("Rain");
                    int n = PulseAt(revenant, open);
                    Kit.Check(n >= 3, $"Rain: rain, open sky -> {n} splashes around the revenant (>= 3)");
                    statsAtPassive = OmensStats.RainSplashes;
                    Next();
                    break;
                case 2:
                    // passive: only MapComponentTick pulses (every 30 ticks); keep the revenant on its open cell
                    if (revenant.Position != open) TestUtil.Teleport(revenant, open);
                    if (InPhase < 95) return;
                    int passive = OmensStats.RainSplashes - statsAtPassive;
                    Kit.Check(passive >= 3, $"Rain: the 30-tick pulse alone made {passive} splashes in 95 ticks");
                    Kit.Check(PulseAt(revenant, roofed) == 0, "Rain: under a roof -> no splashes");
                    Kit.Check(PulseAt(revenant, fogged) == 0, "Rain: in fog -> no splashes");
                    Weather("SnowGentle");
                    int s0 = OmensStats.RainSplashes;
                    int puffs = PulseAt(revenant, open);
                    Kit.Check(puffs >= 1 && OmensStats.RainSplashes == s0, $"Rain: snow -> {puffs} white puffs and no rain splashes");
                    Weather("Rain");
                    OmensMod.Settings.rainReveals = false;
                    Kit.Check(PulseAt(revenant, open) == 0, "Rain: setting off -> nothing");
                    OmensMod.Settings.rainReveals = true;
                    Next();
                    break;
                case 3:
                    // a friendly pawn hidden the same way: never outlined
                    HediffDef horax = DefDatabase<HediffDef>.GetNamedSilentFail("HoraxianInvisibility");
                    Faction ally = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
                        !f.IsPlayer && !f.Hidden && !f.defeated && f.def.humanlikeFaction && !f.HostileTo(Faction.OfPlayer));
                    friend = PawnGenerator.GeneratePawn(ally?.def.basicMemberKind ?? PawnKindDefOf.Villager, ally);
                    GenSpawn.Spawn(friend, friendCell, map);
                    friend.health.AddHediff(horax);
                    TestUtil.Teleport(revenant, roofed);      // the revenant contributes nothing from under the roof
                    Next();
                    break;
                case 4:
                    if (InPhase < 2) return;
                    Kit.Check(friend.IsHiddenFromPlayer() && !friend.HostileTo(Faction.OfPlayer),
                        $"Rain: the {friend.Faction?.Name ?? "factionless"} pawn is hidden from the player and not hostile (precondition)");
                    Kit.Check(PulseAt(friend, friendCell) == 0, "Rain: friendly hidden pawn in rain, open sky -> no splashes");
                    Kit.Check(PulseAt(revenant, open) >= 1, "Rain: the revenant back in the open splashes again (control)");
                    Kit.Check(revenant.IsHiddenFromPlayer(), "Rain: after all this the revenant is still hidden (nothing revealed)");
                    Finish();
                    break;
            }
        }

        protected override void Cleanup()
        {
            if (revenant != null && !revenant.Destroyed) revenant.Destroy();
            if (friend != null && !friend.Destroyed) friend.Destroy();
            if (map != null)
            {
                Weather("Clear");
                if (roofed.IsValid) map.roofGrid.SetRoof(roofed, null);
                if (fogged.IsValid) map.fogGrid.Unfog(fogged);
            }
        }
    }
}
