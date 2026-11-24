# Omens - progress (for resuming)

Updated 2026-10-12 ~04:30.

## Done
- Omens + OmensTest scaffolded, deployed, all 3 features implemented (DESIGN.md).
- Harness run 1: `logs/run-20261012-034530.log` -> 41 PASS, 0 FAIL, 0 errors (`--ticks 6000`).
- Showcase run 1 (`logs/run-20261012-035245.log`): crows + flock clearly readable; camp inspect text good;
  rain outline = blue blob (too big), snow not visible, campfire texture looked like a brown lump.
- Checkpoint commits: Omens a0d4684, OmensTest f9fbc4b.

## In progress (after the checkpoint, not yet run in game)
- Visual tuning: splash growthRate 0.3, 8 smaller splashes on a taller ellipse; 5 bigger snow puffs; crow drawSize 1.4;
  own ColdCamp.png (stone ring + ash, Source/Art/make_textures.py); showcase logs diagnostics per shot.
- try/catch+ErrorOnce around MapComponentTick bodies; assault detection limited to assault toils.

## Next
1. Showcase run 2 (`--showcase 150`), look at PNGs in logs/showcase/omens.
2. Harness rerun (`--ticks 6000`), then Preview.png (docs/forge/resize.ps1), final commits.
