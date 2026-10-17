# Over the Body - revisión

Revisión de un agente `code-reviewer` (solo lectura) contra DESIGN.md, más lo que encontraron las corridas del arnés.

## Revisor

| # | Hallazgo | Severidad | Estado |
|---|----------|-----------|--------|
| 1 | El componente hacía `WakeUp`/`CheckForJobOverride` aunque el nodo fuera a devolver null (cargado por amigo sin registro, GuardCell inválida, secuestrador inalcanzable, animal sin melee): despertaba/interrumpía al animal cada 60 ticks | bug | Corregido: la decisión vive en `JobGiver_StandOverMaster.TryMakeJob(pawn, notify)` sin efectos con `notify:false`; el componente sólo molesta al animal si daría un job nuestro. PASS nuevo en el arnés |
| 2 | `{0}` en la descripción de los ThoughtDefs resuelve al colono (PAWN), no al animal | bug | Corregido: `{OTHERPAWN_labelShort}` |
| 3 | Registro vivo con el caído secuestrado fuera del mapa: podía dar "stood over me" de un rescate fallido | riesgo | Corregido: `MapHeld == null` y sin caravana -> se descarta |
| 4 | Rush no re-apuntaba al expirar (IsContinuation true de base) | riesgo | Corregido: `IsContinuation` sólo si es la misma celda; end condition si el caído ya no está downed |
| 5 | WakeAnimals no excluía drafted (VEF), jobs playerForced ni animales en lord | riesgo | Corregido (y `CanGuard` excluye drafted) |
| 6 | DESIGN decía que psicópatas reciben "died over me" pero el XML lo anula | inconsistencia | DESIGN corregido (igual que vanilla BondedAnimalDied) |
| n | Report de FollowClose nombraba al cargador | nit | JobDef propio `OverTheBody_Follow` ("staying close to <caído>") |
| n | `Notify_GuardTick` con un AddEndCondition como hook | nit | Quitado; ver abajo cómo se reemplazó |
| n | About decía "within 2 cells" (código 2.5), loadAfter de Core sobrante | nit | Corregido |
| n | DiedGuarding exige master vivo | nit | Documentado en DESIGN |

## Encontrado por el arnés

- **FollowClose vanilla terminaba al instante** cuando el animal ya estaba al lado del cargador quieto
  (`NearDestinationOrNotMoving`), así que a veces el animal no seguía al rescatista (PASS intermitente).
  Reemplazado por `JobDriver_StayWithCarried`: se mantiene a <= 2.5 del cargador, no termina por estar cerca, termina
  cuando sueltan al caído.
- **"Murió guardando" no contaba si moría en los primeros 60 ticks** de la guardia (lastGuardTick sólo lo ponía el
  chequeo periódico). Ahora `Notify_Guarding` (cada vez que el nodo entrega un job nuestro) y el chequeo periódico
  (cualquier job nuestro, incluido Rush) actualizan `lastGuardTick`. El arnés mata al animal apenas toma la guardia.
- **Mirada:** las primeras ticks del job miraban al caído hasta el primer chequeo de 30 ticks; ahora busca la amenaza
  en la primera tick.
- Geometría del arnés: el husky ya adyacente se quedaba del lado opuesto a la amenaza (comportamiento de diseño:
  "si ya está al lado, se planta"); el arnés lo aleja antes de bajar al colono.
