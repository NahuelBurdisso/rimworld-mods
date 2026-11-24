using System.Collections.Generic;
using System.Linq;
using Gesta;
using HarmonyLib;
using UnityEngine;
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
                    for (int i = 0; i < 8; i++) mechs.Add(SpawnVisible(map, root, kind, mechFaction));
                    // the colony's strength depends on the random colonists: add enemies until clearly outmatched
                    for (int i = 0; i < 24 && TideWatcher.Ratio(map) < TideWatcher.LosingRatio * 1.5f; i++)
                        mechs.Add(SpawnVisible(map, root, kind, mechFaction));
                    EngagementTracker.Instance.PollNow();
                    float ratio = TideWatcher.Ratio(map);
                    tide.Evaluate();
                    tide.Evaluate();
                    Kit.Check(!tide.Losing, $"outmatched but nobody down yet is not losing (ratio {ratio:0.0})");
                    Pawn fallen = map.mapPawns.FreeColonistsSpawned.First(CombatUtil.IsFighter);
                    Kit.Down(fallen);
                    tide.Evaluate();
                    tide.Evaluate();
                    Kit.Check(tide.Losing, $"outmatched and bleeding counts as losing (ratio {TideWatcher.Ratio(map):0.0})");
                    break;
                case 1950:
                    Kit.Check(tide.losingIntensity > 0.1f, $"the cold sky eases in ({tide.losingIntensity:0.00})");
                    SkyTarget withTide = Sky(), withoutTide;
                    GestaMod.Settings.turningTide = false; withoutTide = Sky(); GestaMod.Settings.turningTide = true;
                    Kit.Check(withTide.colors.saturation < withoutTide.colors.saturation - 0.1f,
                        $"the sky loses colour ({withoutTide.colors.saturation:0.00} -> {withTide.colors.saturation:0.00})");
                    Kit.Check(Mathf.Approximately(withTide.glow, withoutTide.glow), $"light level is untouched (glow {withTide.glow:0.000})");
                    foreach (Pawn m in mechs) if (!m.Dead) m.Kill(null);
                    EngagementTracker.Instance.PollNow();
                    tide.Evaluate();
                    Kit.Check(tide.Gold && tide.hostilesFelled >= mechs.Count / 2, $"bringing the enemy down after losing opens the gold sky ({tide.hostilesFelled} felled)");
                    break;
                case 2150:
                    float f = tide.TintFactor(out bool gold);
                    SkyTarget goldSky = Sky();
                    GestaMod.Settings.turningTide = false; SkyTarget plain = Sky(); GestaMod.Settings.turningTide = true;
                    Kit.Check(gold && f > 0.5f && goldSky.colors.saturation > plain.colors.saturation + 0.1f && goldSky.colors.sky.b < plain.colors.sky.b,
                        $"the gold sky is strong and warm (tint {f:0.00}, saturation {plain.colors.saturation:0.00} -> {goldSky.colors.saturation:0.00})");
                    Kit.Check(Mathf.Approximately(goldSky.glow, plain.glow), "gold does not brighten the light level");
                    Kit.Check(tide.losingIntensity == 0f, "the cold is gone");
                    Finish();
                    break;
            }
        }

        /// <summary>A hostile on an unfogged, standable cell 12-25 cells away (fogged pawns are no threat).</summary>
        static Pawn SpawnVisible(Map map, IntVec3 root, PawnKindDef kind, Faction f)
        {
            IntVec3 cell = IntVec3.Invalid;
            for (int i = 0; i < 60 && !cell.IsValid; i++)
            {
                IntVec3 c = root + new IntVec3(Rand.RangeInclusive(-25, 25), 0, Rand.RangeInclusive(-25, 25));
                if (c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.DistanceTo(root) >= 12f) cell = c;
            }
            Pawn p = Kit.SpawnHostile(map, cell.IsValid ? cell : root + new IntVec3(12, 0, 0), kind, f);
            if (cell.IsValid) Kit.Teleport(p, cell);
            return p;
        }

        SkyTarget Sky() => Traverse.Create(map.skyManager).Method("CurrentSkyTarget").GetValue<SkyTarget>();

        void Finish()
        {
            foreach (Pawn m in mechs) if (!m.Dead && !m.Destroyed) m.Kill(null);
            Kit.Summary("Gesta/Tide");
            done = true;
        }
    }
}
