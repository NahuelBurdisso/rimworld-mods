# REVIEW - Unlikely Hero (2026-10-16)

Revisión hecha por un agente `code-reviewer` contra DESIGN.md y el decomp 1.6.4871. Hallazgos y qué se hizo:

## Bugs confirmados (corregidos)

1. **`IsActiveThreatTo` rechazaba los tiros a la cabeza.** `IsPotentialThreat` exige `Awake()`, que lee
   `capacities.CanBeAwake` (conciencia >= 0.3); en la ruta real de daño `AddHediff -> CheckForStateChange
   -> Kill` el cerebro ya está destruido cuando corre el postfix. También perdía las "death on downed" por
   inconsciencia. -> `HeroRules.WasActiveThreat`: la misma lógica sin `Awake()`, el sueño se lee de
   `curDriver.asleep`. Test nuevo P10 (bala real al cerebro) y P11 (death-on-downed real).
2. **El arnés reutilizaba un asesino en los negativos** y desde el 2.º caso todos daban "killer has
   killed before" (lo confirmó la corrida run-20261016-223206: 7 FAIL). -> un no-combatiente nuevo por caso.
   Además `PawnKindDef` "Boar" no existe: es "WildBoar".

## Riesgos (corregidos)

- Un solo try/catch para todo el momento: si fallaba un paso, el resto se perdía para siempre (el héroe
  ya está marcado). -> `Step()` con try/catch por paso.
- `TryGiveTrait` usaba `HasTrait`, que ignora rasgos suprimidos: podía dar un segundo Nerves. -> recorre
  `allTraits` por def.
- `static Pawn LastHero` retenía la partida anterior. -> `HeroMoment.ResetStatics()` en el ctor de `HeroLedger`.
- Sólo se excluían Flame y Burn por referencia: AcidBurn, ElectricalBurn (Anomaly), VacuumBurn (Odyssey)
  pasaban. -> lista `excludedDamage` en `UnlikelyHero_Config` (con MayRequire); test P3f con AcidBurn.

## Decisiones (no se cambian)

- Insectos que custodian su colmena sin duty AssaultColony no cuentan (igual que vanilla
  `IsActiveThreatTo`, `GenHostility.cs:319`). Documentado en DESIGN.
- El round-trip de save prueba `HeroRoll` (lo que guarda `HeroLedger` con `Scribe_Deep`), no una partida
  completa guardada y recargada. El reviewer verificó en `Game.cs:345,443-485` que el componente se
  reconstruye y se reasigna al cargar.

## Nits (corregidos)

- Lista estática `tmpWitnesses` compartida -> lista local.
- Mensaje con `{VICTIM_labelShort}` ("brought down boar") -> `{VICTIM_nameDef}`.
- "Steadfast" con mayúscula a mitad de frase -> `trait.Label`; pasión con `skill.label`.
- `GetSkill` de una skill "gentil" que otro mod quitó logueaba error -> se recorre `skills` y se
  compara con la lista.
- `IsMechanoid`/`IsAnomalyEntity` redundantes -> se sacó `IsMechanoid` (ya filtrado por Humanlike||Animal).
- P1 no verificaba +3 / +10 -> lo verifica ahora.
- Numeración del arnés alineada en DESIGN.md.
