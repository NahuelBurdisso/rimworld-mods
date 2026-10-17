# REVIEW - A Light for the Absent

Revisión de código por un agente `code-reviewer` (2026-10-17) contra DESIGN.md: correctness,
performance, saves, compat. Cada punto se verificó contra `decomp/1.6.4871` antes de corregir.

| # | Hallazgo | Veredicto | Qué se hizo |
|---|---|---|---|
| 1 | Mourning nunca mira `p.Dead`: un resucitado lejos seguía parpadeando y su familia recibía `LightWentOut` sobre alguien vivo | confirmado | `case Mourning`: si `!p.Dead` vuelve a Away (las noches ya prendidas cuentan). Test R1 |
| 2 | `!bed.Spawned` devolvía "esperar" antes de todo: una cama minificada dejaba el registro trabado para siempre (y `RecordFor != null` bloqueaba luces nuevas del pawn) | confirmado (`MinifyUtility.MakeMinified` despawnea sin destruir) | Sólo se espera si la cama está en `Find.CurrentGravship.Things`; si no, se borra el registro. Test R3 |
| 3 | `GivenAway` tomaba a un co-dueño previo no pareja (cama doble compartida, mods de cuchetas) como "cama regalada": nunca había luz y el registro se recreaba cada poll | confirmado | `AbsentRecord.coOwners` (snapshot al crear el registro, guardado por referencia); no cuentan como regalo. Test R2 |
| 4 | Mourning: `if (!room) return false` borraba el duelo si el cuarto se rompía un rato; las noches se contaban por la luz, no por el cielo (lo mismo `nightsLit` en Away) | confirmado | Mourning cuenta atardeceres con `nightLastPoll` (guardado); sin cuarto = luz apagada, el duelo sigue. Away cuenta una noche con `litTonight` (guardado). Test R4 |
| 5 | Mapas de bolsillo (undercave, laberinto) contaban como "ausente" | confirmado (`Map.IsPocketMap`, `PocketMapParent.sourceMap`) | `IsHome` sube por `PocketMapParent.sourceMap`. Sin test (no hay forma barata de crear un pocket map en el arnés) |
| 6 | La poda en `Saving` sacaba registros sin `Extinguish` | confirmado | Poda en save eliminada (el poll ya los limpia; `PostLoadInit` sigue filtrando nulls) |
| 7 | Un error en `Detect` cortaba los `Update` del mismo poll | confirmado | `try` separados |
| 8 | Latitudes extremas: una noche más corta que 20 polls nunca apagaba la luz del duelo | confirmado | Al amanecer de la 3.ª noche: si estuvo prendida, `GoOut`; si nunca prendió (sin cuarto), termina en silencio |
| 9 | Un pawn que muere a menos de 250 ticks de irse no recibe duelo | aceptado, no se corrige | El riesgo de falsos duelos (muerto en casa con cadáver destruido) pesa más; documentado en DESIGN |
| 10 | El halo (hasta 6,4 celdas en cama doble) se recortaba por el culling del `DynamicDrawManager` (`coarseBounds = OccupiedDrawRect()`, que usa `DrawSize` 1x1) | confirmado (`Verse/DynamicDrawManager.cs:277`, `GenAdj.OccupiedDrawRect`) | `AbsentLight.DrawSize => 9x9` (constante: se lee al spawnear, antes de `Init`) |
| 11 | `AllBuildingsColonistOfClass<Building_Bed>` recorre todos los edificios (paredes incluidas) cada poll | confirmado (`ListerBuildings.cs:267`) | `listerThings.ThingsInGroup(ThingRequestGroup.Bed)` + facción del jugador |
| 12 | Un esclavo con un mental break lejos deja de ser `IsColonist` (`SlaveIsSecure`) y perdía el registro | confirmado (`Pawn.cs:518`, `Pawn_GuestTracker.cs:164`) | Registros existentes usan `StillOurs` (humanlike, facción del jugador, no prisionero); `Eligible` sigue para crear |
| 13 | Hooks estáticos de test (`AutoPoll`, `NightOverride`) podían filtrarse a otra partida | riesgo real | `AbsenceTracker.FinalizeInit` los resetea |
| 14 | Docs: DESIGN decía que camas de mapas abandonados esperan (en realidad se descartan → registro borrado); About decía "roofed" (el código usa `PsychologicallyOutdoors`/`TouchesMapEdge`: un cuarto sin techo cuenta); texto de brillo decía "next time a light is lit" (se aplica en el próximo poll) | confirmado | DESIGN, About y Keyed corregidos |
| + | Sugerencia: test de guardado | hecho | S1: `Scribe.saver.DebugOutputFor` de un registro en duelo (pawn muerto y destruido) y de uno Away: las referencias se escriben |

Extra encontrado al corregir: un pawn muerto lejos que el `WorldPawnGC` descarta no se puede guardar por
referencia (`Scribe_References.CheckSaveReferenceToDestroyedThing` avisa); `Update` borra el registro si
`p.Discarded`.

No probado en juego: pocket maps (5), noches polares (8), gravship en vuelo con la cama a bordo (2),
carga real de una partida guardada (sólo la escritura, S1).

## Segunda revisión (code-reviewer, sobre el código ya corregido)

| # | Hallazgo | Veredicto | Qué se hizo |
|---|---|---|---|
| A1 | Cama doble con el ausente como único dueño: otro toma el lado libre → `GivenAway` borra el registro, y al poll siguiente el ausente (que sigue siendo dueño) recibe uno nuevo: la luz parpadea y `nightsLit` se reinicia | confirmado (`Building_Bed.OwnersForReading` = slots de `CompAssignableToPawn`) | "Regalada" sólo si el ausente ya **no** es dueño. Test A1 |
| A2 | Dos ausentes en la misma cama (pareja que se va junta) → dos `AbsentLight` apiladas: halo y glow al doble (MoteGlow aditivo, `GlowGrid` suma colores) | confirmado | Una luz por cama en el tracker (`lamps`): nivel máximo, parpadeo sólo si todos murieron. Test A2 |
| A3 | El ataúd de deathrest es `Building_Bed` con `bed_humanlike` true → podía llevar la luz | confirmado (`BuildingProperties.cs:176,194`, `ThingDef.IsBed`) | `IsHomeBed` exige `bed_countsForBedroomOrBarracks`. Test A3 |
| B1 | +3 farmeable: una sola poll prendida (salir y volver de noche) daba el thought | confirmado | `litPolls` (guardado) ≥ 10 (~1 h) para el thought. Tests P2, R5 |
| B2 | `known` no se guarda: un secuestro en los primeros 250 ticks tras cargar se pierde | confirmado | Poll inmediato en el primer tick tras cargar |
| B3 | Colonos "perdidos" (mapa neutral que se cierra) quedan Away con luz eterna | aceptado | Documentado (DESIGN). Distinguirlos de pawns en misiones/quests sin falsos positivos no es barato |
| B4 | Pawn en un contenedor a bordo de la gravship → registro Away espurio | aceptado (inocuo: sin thought, la cama viaja) | Documentado |
| B5 | Excepción persistente en `Update` → registro creado y borrado cada poll, sin log | confirmado | Set `failed` en memoria: ese pawn se ignora hasta recargar |
| B6 | Noche/día polar continuo demora el duelo | aceptado | Documentado |
| C1 | El glower se registraba dos veces al prender (spawn + `GlowColor`) | confirmado (`CompGlower.SetGlowColorInternal` sólo re-registra si `ShouldBeLitNow`) | La luz recibe el color antes de `GenSpawn.Spawn` |
| C2 | Glower con color 0 a brillo mínimo | nit inofensivo | Sin cambio |
| C3 | `Fogged` redundante en `DrawAt` | nit | Sin cambio (barato) |
| C4 | Glow en la celda de cabecera, halo en `TrueCenter` | estético | Sin cambio |
| C5 | Duelo sigue con el cadáver ya en casa | confirmado | Mourning termina en silencio si `IsHome(p)` |
| C6 | `LightWentOut` no llegaba a esclavos (`FreeColonistsSpawned`) | confirmado | `SpawnedPawnsInFaction(OfPlayer)` sin prisioneros |
| C7 | Dependencia dura de Harmony sin ningún patch | confirmado | Harmony sacado del mod y del About (el arnés lo sigue usando) |
| C8 | Quitar el mod de una partida: errores rojos únicos e inofensivos | confirmado | Dicho en el About |
| C9 | Estáticos retienen pawns hasta la próxima partida | nit | Ya se limpian en `FinalizeInit` |
