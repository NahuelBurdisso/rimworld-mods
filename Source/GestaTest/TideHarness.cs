using System.Collections.Generic;
using System.Linq;
using Gesta;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>Turning Tide: losing → cold sky, winning after losing → gold sky. Runs from tick 1800.</summary>
    public class TideHarness : GameComponent
    {
        bool active, done;
        int ticks;
        Map map;
        TideWatcher tide;
        readonly List<Pawn> mechs = new List<Pawn>();

        public TideHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done) return;
            ticks++;
            try { Step(); }
            catch (System.Exception ex) { Kit.Check(false, "tide harness threw: " + ex); Finish(); }
        }

        void Step()
        {
            switch (ticks)
            {
                case 1800:
                    map = Find.CurrentMap;
                    tide = map.GetComponent<TideWatcher>();
                    EngagementTracker.Instance.PollNow();
                    Kit.Check(!EngagementTracker.Instance.IsActive(map) && TideWatcher.Ratio(map) < 0.01f, "setup: quiet map, no hostile strength");
                    IntVec3 root = map.mapPawns.FreeColonistsSpawned.First().Position;
                    Faction mechFaction = Faction.OfMechanoids ?? Faction.OfPirates;
                    PawnKindDef kind = Faction.OfMechanoids != null ? PawnKindDef.Named("Mech_CentipedeGunner") : PawnKindDefOf.PirateBoss;
                    for (int i = 0; i < 8; i++) mechs.Add(Kit.SpawnHostile(map, root + new IntVec3(35, 0, i * 2 - 8), kind, mechFaction));
                    EngagementTracker.Instance.PollNow();
                    float ratio = TideWatcher.Ratio(map);
                    tide.Evaluate();
                    tide.Evaluate();
                    Kit.Check(tide.Losing, $"a much stronger enemy counts as losing (ratio {ratio:0.0})");
                    break;
                case 1950:
                    Kit.Check(tide.losingIntensity > 0.1f, $"the cold sky eases in ({tide.losingIntensity:0.00})");
                    Kit.Check(tide.SkyEvent != null && map.weatherManager.eventHandler.LiveEventsListForReading.Contains(tide.SkyEvent), "a sky event is live");
                    Kit.Check(tide.SkyEvent != null && tide.SkyEvent.SkyTargetLerpFactor > 0f && tide.SkyEvent.SkyTarget.colors.saturation < 1f,
                        "it pulls the sky toward grey");
                    foreach (Pawn m in mechs) if (!m.Dead) m.Kill(null);
                    EngagementTracker.Instance.PollNow();
                    tide.Evaluate();
                    Kit.Check(tide.Gold, "winning a fight you were losing opens the gold sky");
                    break;
                case 2150:
                    Kit.Check(tide.SkyEvent != null && tide.SkyEvent.SkyTargetLerpFactor > 0.3f && tide.SkyEvent.SkyTarget.colors.saturation > 1f,
                        $"the gold sky is strong and warm (lerp {tide.SkyEvent?.SkyTargetLerpFactor:0.00})");
                    Kit.Check(tide.losingIntensity == 0f, "the cold is gone");
                    Finish();
                    break;
            }
        }

        void Finish()
        {
            foreach (Pawn m in mechs) if (!m.Dead && !m.Destroyed) m.Kill(null);
            Kit.Summary("Gesta/Tide");
            done = true;
        }
    }
}
