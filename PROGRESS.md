# Old Scores — progress (for resuming)

## Done
- Feature 4 Raid Echoes: committed (OldScores 3d99772, OldScoresTest 1cda239). Last test run
  `logs/run-20261012-032844.log`: Nicknames 24, Spared 48, Beasts 78, Echoes 126 passed (cumulative), 0 errors.
  Showcase `logs/showcase/oldscores/4..7-*.png` checked. Awaiting code-reviewer findings from the coordinator.

## In progress
- Feature 5 "The Beast Remembers" (beast-memory). Spec: docs/forge/research/forja-2026-10-12.md §8.
  DESIGN.md section written; code written (Source/OldScores/BeastMemory.cs, Defs/OldScores_BeastMemory.xml,
  keyed, settings, tuning) and builds clean. Next: BeastMemoryHarness + showcase. Hooks (hooks verified: Pawn.PostApplyDamage postfix (existing),
  private Pawn_HealthTracker.MakeDowned(DamageInfo? dinfo, Hediff hediff) :837, Pawn_CallTracker.DoCall(bool) :95,
  MoteMaker.MakeInteractionBubble(Pawn, Pawn, ThingDef, Texture2D, Color?) :181, ThingDefOf.Mote_Speech,
  symbol "Things/Mote/SpeechSymbols/Insult", DamageDef.ExternalViolenceFor(Thing) :179).

## Raid Echoes review fixes: DONE
- committed OldScores c9f9e85 (source), OldScoresTest 2218c3a. Run logs/run-20261012-041806.log: Echoes 137 cumulative, 0 FAIL.

## Beast Memory status
- growl/grudge tests pass; fear tests failed because vanilla death-on-downed chance killed the raider;
  fixed with health.overrideDeathOnDownedChance = 0 in the harness. Re-running.

## Next
- implement (Source/OldScores/BeastMemory.cs), harness BeastMemoryHarness (after Echoes, ~6200+; run --ticks 9000),
  showcase of the red growl bubble, self-review, commit both repos.

## How to test
`.venv/Scripts/python.exe docs/forge/gametest.py --who oldscores --mods oldscores oldscorestest --ticks 8000`
(showcase: `--showcase 300`, needs mods/OldScoresTest/showcase.flag, PNGs in logs/showcase/oldscores/).

## 2026-10-16 (retomado)
- Build roto: el mod Harmony movió 0Harmony.dll de Current/ a 1.6/Assemblies; ambos csproj ahora toman el que exista.
- gametest --ticks 9000: logs/run-20261016-220348.log -> Nicknames 24, Spared 48, Beasts 78, Echoes 137,
  BeastMemory 167 (acumulados), 0 FAIL, 0 errores.
- Spared FAIL viejo (run-20261012-104300, "second pirate raid executed"): vanilla rechazó el raid (aleatorio);
  Raid() del arnés ahora reintenta hasta 3 veces con parms nuevos y loguea el motivo.
- Showcase OK: logs/showcase/oldscores/8-growl-bubble.png, 9-fear-of-the-beast.png (mirados).
- Review (code-reviewer): docs/forge/reviews/oldscores-beast-memory.md; 15 de 17 corregidos, tests nuevos.
- Final: logs/run-20261016-223033.log -> 24/48/79/138/173 acumulados (BeastMemory 35), 0 FAIL, 0 errores.
- Compat: ver DESIGN.md "Compat probada con" (Grudges! pinea al lobo: FAIL de unpin ajeno).
- Feature 5 TERMINADA y commiteada.
