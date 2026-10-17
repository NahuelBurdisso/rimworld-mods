# Revisión - The Dog Knows

Revisor: agente `code-reviewer` (2026-10-16), contra DESIGN.md y decomp 1.6.4871. Sin críticos.

| # | Hallazgo | Estado |
|---|----------|--------|
| 1 | La entidad viajaba en `job.targetB`; vanilla sólo dibuja targetA, pero otro mod que liste targets la vería. | **Corregido**: el job no tiene targets; la entidad pasa al driver (`Pending` durante `StartJob`, tomado en `Notify_Starting`) y se guarda con `Scribe_References`. Test: "the job has no targets". |
| 2 | `CanReact` cortaba Follow (escoltar al amo), Flee y las esperas forzadas (`PawnUtility.ForceWait`: ordeñe, esquila, doma, atención médica), todas `casualInterruptible` por defecto. | **Corregido**: `ShouldFollowMaster` (como `CanCasuallyInteractNow`), Flee/FleeAndCower/FleeAndCowerShort excluidos, Wait/Wait_MaintainPosture/LayDown/LayDownAwake con `expiryInterval > 0` excluidos. Tests: huida y ForceWait. |
| 3 | Sin prueba de niebla ni save/load. | **Niebla agregada** (raider oculto en celda con niebla => no cuenta). Save/load: **no probado** (anotado). |
| 4 | Cachorros y gatitos sin sonido (sólo AnimalAdult tiene soundAngry/soundCall). | **Corregido**: `GrowlSoundFor` cae a la etapa más vieja con sonido. Test con un cachorro de husky. |
| 5 | `FieldRefAccess` en inicializador estático: si falla, `TypeInitializationException` tumba `TDKUtility` entero. | **Corregido**: holder perezoso con try/catch; `Shiver` no hace nada si es null. |
| 6 | Iteraba la lista viva `SpawnedPawnsInFaction`. | **Corregido**: copia a lista reutilizable filtrando animales con sense def. |
| 7 | `UnseenSenseDef.For` cada tick en el driver. | **Corregido**: cacheada en campo `[Unsaved]`. |
| 8 | Cooldown usa `stareTicks.max`; `StillSensing` corta también por LOS. | Documentado en DESIGN.md (decisión). |
| 9 | `duration - staring` en initAction era código muerto (initAction no corre al cargar). | **Corregido** y comentado. |
| 10 | Hostiles con `PsychicInvisibility` (visibles para el jugador) también disparan. | Decisión de diseño, se mantiene (documentado). |
| 11 | Anomaly como dependencia dura sin necesidad de código. | Se mantiene: el pedido lo exige y el contenido es para entidades de Anomaly. |
| 12 | Pack Howls puede cortar una mirada (al revés no). | Aceptado, documentado. |
| 13 | Guardar referencia a entidad despawneada no destruida puede dar warning. | **Corregido**: al guardar, si no está spawneada se descarta (la mirada termina en el próximo chequeo). |

Hallazgo propio en la primera corrida del arnés: `mapPawns.AllPawnsSpawned` no registra pawns con
`mindState.Active == false` (`MapPawns.RegisterPawn`, `MapPawns.cs:855`), así que las entidades dormidas no
se veían. **Corregido**: el escaneo usa `listerThings.ThingsInGroup(ThingRequestGroup.Pawn)`, la misma lista que
`CompSightstealer.CheckDetected`.
