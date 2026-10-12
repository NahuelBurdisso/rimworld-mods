# Omens - progress (for resuming)

Updated 2026-10-12 ~03:58.

## Done
- Scaffolded Omens + OmensTest (git repos, deployed junctions). All 3 features implemented (DESIGN.md).
- Harness test run 1: `logs/run-20261012-034530.log` -> 41 PASS, 0 FAIL, 0 errors (`--ticks 6000`).
- Showcase run 1: `logs/run-20261012-035245.log`, PNGs in `logs/showcase/omens/` (being reviewed).

## In progress
- Reviewing showcase PNGs; small post-test changes NOT yet rebuilt/retested: try/catch + ErrorOnce around the
  three MapComponentTick bodies; assault detection restricted to assault toils (StagingWatcher.IsAssaultToil).

## Next
1. Rebuild both, rerun harness: `.venv/Scripts/python.exe docs/forge/gametest.py --who omens --mods omens omenstest --ticks 6000`.
2. Iterate showcase if visuals unclear (`--showcase 150`, flag file `mods/OmensTest/showcase.flag`).
3. About/Preview.png via docs/forge/resize.ps1; commit both repos locally (no commit yet).
