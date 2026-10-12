# Aftermath — progress (overnight run 2026-10-12)

Task: 3 visual fixes (standard emblem, vigil glow indoors, unnamed foe grave name) + new feature
The Old Sentinel (DESIGN.md, Feature 6). Harness in mods/AftermathTest.

## Done (uncommitted until the final commit)
- DESIGN.md: fixes documented in Features 2/3/4, Feature 6 written in full.
- Emblem: icon printed with Cutout (Transparent never showed in the map mesh) and lifted above the
  cloth; VERIFIED visible in logs/showcase/aftermath/2-trophy-standard.png (run 03:32).
- Vigil: `VigilGlow` draws a procedural amber halo above the lighting overlay; VERIFIED visible
  in 4-vigil-lamp.png (run 03:32); then toned down (color 0.36/0.21/0.06, size 2.4w+1.8) — recheck.
- Foe name fallback "an unnamed <kind>" (AftermathUtil.FoeName) + "Here lies X, a worthy foe." key.
- Old Sentinel: Source/Aftermath/Sentinel/*, 1.6/Defs/Aftermath_Sentinel.xml, settings toggle,
  keys, injector (Building_Turret defs), patches (MakeDowned/Kill/PostApplyDamage).
- Harness: SentinelHarness.cs, emblem/halo/unnamed checks, start ticks relative (TestUtil.Offset:
  300 alone, 4600 with gesta/oldscores tests), offerings stayer test hardened (drafts blockers).
- Showcase: scenes 6-old-sentinel, 7-sentinel-remains (both verified readable at 03:17 run).

## Last test result
- logs/run-20261012-034617.log (`--ticks 3000`, aftermath+aftermathtest): 113 PASS, 1 FAIL
  (real-fire rat ran out of range; fixed by penning it with fences — needs rerun).

## Next
1. Re-run `gametest.py --who aftermath --mods aftermath aftermathtest --ticks 3000`: expect 0 FAIL.
2. Showcase `--showcase 260`, Read crops of 2, 4, 6, 7.
3. Commit in mods/Aftermath and mods/AftermathTest (Co-Authored-By line), report to coordinator.
