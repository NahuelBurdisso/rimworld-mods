# Window Watchers - diseño

Idea `window-watchers` (ledger 2026-10-13, combate r4, puntaje 48). packageId `nahuel.windowwatchers`.

## Pitch

Durante un raid en el mapa de hogar, los que no pelean (chicos, viejos, enfermos/convalecientes que
caminan, colonos con violencia deshabilitada) y tienen a un ser querido peleando buscan un lugar
techado y seguro desde donde verlo, y se quedan mirándolo con una burbuja de preocupación con flecha
hacia él. Si lo hieren la burbuja tiembla y se pone roja (pensamiento chico). Si cae, uno mayor. Si
vuelve entero cuando termina la amenaza, alivio y un abrazo.

## Qué ve el jugador

- Raid activo. La nena de 8 años deja de jugar, va a la puerta de la casa que da al patio donde su
  madre está draftada, la puerta queda entreabierta y la nena se queda en el umbral de adentro,
  mirando, con una burbuja de pensamiento (icono de ojo con lágrima) y una flecha hacia la madre.
  Reporte del job: "watching Ana from cover".
- La madre recibe un tiro: la burbuja tiembla ~2.5 s y se tiñe de rojo; la nena recibe
  "Watched Ana get hurt" (-2, medio día).
- La madre cae: burbuja roja temblando unos segundos, "Watched Ana go down" (-5, 2 días) o
  "Watched Ana die" (-6, 4 días, encima del duelo vanilla). La nena deja de mirar.
- Termina el raid y la madre está en pie: "Ana made it back" (+4, 1.5 días) y la nena va a
  abrazarla (interacción "embrace", línea en el log social, la madre recibe "Lia hugged me after the
  fight" +2, 1 día).

## Reglas exactas

### Amenaza activa
- Solo mapas `map.IsPlayerHome`.
- Escaneo cada 250 ticks por mapa (MapComponent, con offset por hash del mapa).
- `GenHostility.AnyHostileActiveThreatToPlayer(map)` (no cuenta dormidos ni empañados por niebla).
- "Amenaza terminada" = 2 escaneos seguidos sin amenaza (~500 ticks), para no disparar el alivio en
  un parpadeo.
- Sin amenaza no se hace nada más que el alivio/abrazo pendientes: costo ~0 en paz.

### Quién mira (watcher)
Todas estas:
- `IsColonist` (incluye esclavos seguros), humanlike, spawneado en el mapa, no muerto/caído/draftado,
  sin estado mental, no peleando (`IsFighting`), no prendido fuego, sin Lord (rituales, caravanas).
- Puede moverse (`CapableOf(Moving)`) y ver (`CapableOf(Sight)`); no bebé.
- Sin rasgo Psicópata y no `Inhumanized` (Anomaly): no les importa.
- Es no-combatiente: niño (`DevelopmentalStage.Child`), o viejo (edad biológica >= 0.8 x
  `RaceProps.lifeExpectancy`, 64 en humanos; razas de mods usan su propia expectativa), o violencia
  deshabilitada (`WorkTagIsDisabled(Violent)`), o enfermo/convaleciente
  (`HealthAIUtility.ShouldSeekMedicalRest` o `hediffSet.AnyHediffMakesSickThought`).

Y en el momento de darle el job (no interfiere con prioridades):
- Comida: no si `food.CurCategory >= Hungry`.
- Descanso: no si `rest.CurCategory >= Tired`, ni si el horario dice Sleep.
- Médico: no si `ShouldSeekMedicalRestUrgent` (necesita atención/cirugía ya), ni si
  `ShouldSeekMedicalRest` y ya está en cama o pelea una enfermedad inmunizable (la cama da inmunidad).
- Doctores: no si tiene Doctor activo y en el mapa hay un colono/animal de la colonia que
  `ShouldBeTendedNowByPlayer` o caído fuera de cama (los bebés, siempre "caídos", no cuentan).
- Huir del fuego/explosiones/enemigos cercanos: lo hace el árbol constante vanilla
  (`JobGiver_FleePotentialExplosion`, `JobGiver_ConfigurableHostilityResponse`), que corre antes que
  el principal y nuestro job no tiene `neverFleeFromEnemies`. Trabajo de emergencia (bomberos,
  rescates de emergencia) va antes que nuestro nodo en el árbol.

### A quién mira (loved one)
Colono de la facción del jugador, humanlike, spawneado en el mismo mapa, no caído, y peleando:
`Drafted || IsFighting()` (AttackMelee, AttackStatic, Wait_Combat, ManTurret). Puntaje del vínculo
desde el watcher:
- pareja (`LovePartnerRelationExists`: esposo/a, amante, prometido/a): 200
- padre/madre o hijo/a (`DirectRelationExists(Parent|Child)`): 150
- amigo: opinión del watcher >= 40: puntaje = opinión
Se elige el puntaje más alto; empate -> el más cercano. Sin nadie con puntaje -> no mira.

### Desde dónde (celda segura)
Búsqueda por regiones (BFS) desde la región del watcher, hasta 48 regiones, entrando solo a regiones
de puerta o de cuartos interiores (no toca borde de mapa, no usa temperatura exterior, no es
doorway), respetando `TraverseParms.For(pawn)`. Si el watcher ya está afuera, el BFS puede cruzar
regiones exteriores cuyo centro no tenga hostiles a < 14 (para llegar a cubierto); si está adentro,
nunca sale. Regiones a más de 36 del ser querido se saltean; como mucho 40 candidatas. Una celda candidata:
- está en un cuarto interior, techada, sin niebla, `Standable`, en el área permitida del pawn,
  reservable (`pawnDestinationReservationManager.CanReserve`), no la eligió otro watcher en este escaneo;
- a <= 36 celdas del ser querido;
- tiene línea de vista al ser querido, de dos formas:
  - A (directa): LOS como la vanilla, salvo que toda puerta bloquea (aunque esté abierta un instante)
    excepto las HoldOpen; ventanas de mods con fillPercent < 1 dejan ver;
  - B (puerta entreabierta): la celda es adyacente (cardinal) a una puerta del jugador que el pawn puede
    abrir y que no está prohibida para pasar (no en mapas con vacío, Odyssey), y hay LOS tratando esa
    puerta como abierta. Mientras mira, el watcher mantiene esa puerta
    abierta (`StartManualOpenBy` cada 60 ticks; se cierra sola 110 ticks después de que deja de mirar).
- es segura: ningún hostil activo a < 14 celdas de la celda, ni a < 24 de la puerta (en B), y ningún
  hostil activo a < 24 celdas con LOS a la celda (para la vista de los hostiles las puertas abiertas sí dejan ver).
- Si no hay celda, no se vuelve a buscar para ese watcher+objetivo por 500 ticks.
Se elige la candidata más cercana al watcher (distancia en línea recta); se prefiere A sobre B a
igual distancia. Al final `CanReach(cell, OnCell, Danger.Some)`. Si no hay celda: no hace nada.

### Mientras mira (job WW_Watch, A = ser querido, B = celda, C = puerta o nada)
- Camina a la celda y se queda ahí mirando al ser querido (rotación hacia él), con la burbuja
  mantenida (mote propio `WW_Mote_Worry`, nube de pensamiento + icono + flecha vanilla de MoteBubble).
- `expiryInterval` 250 + `checkOverrideOnExpire`: cada 250 ticks se re-evalúa el árbol, así hambre,
  sueño o trabajo urgente lo sacan. `IsContinuation` = mismo ser querido y misma celda, así no se
  reinicia.
- Cada 30 ticks (en la celda):
  1. ser querido muerto -> "Watched X die", burbuja roja final, termina.
  2. caído -> "Watched X go down", burbuja roja final, termina.
  3. despawneado/otro mapa -> termina.
  4. amenaza terminada o ya no pelea -> termina (Succeeded).
  5. bajó su `SummaryHealthPercent` -> la burbuja tiembla y se pone roja 150 ticks; "Watched X get
     hurt" (como mucho una vez cada 600 ticks por watcher).
  6. perdió la LOS, se alejó > 36, la puerta dejó de ser usable (prohibida), o hay un hostil a < 14 de
     la celda / < 24 de la puerta -> termina. El giver y el escaneo también chequean ese peligro, así
     no se re-entrega el mismo job.
- Mientras camina hacia la celda, cada 60 ticks: peligro en la celda/puerta -> termina.
- El watcher queda registrado (MapComponent, se guarda) como "vio a X en esta amenaza".

### Cuándo empieza a mirar
- El escaneo arma un plan (ser querido + celda) por watcher. El JobGiver lo usa cuando el árbol de
  pensamiento corre (al terminar un job). Además, si el job actual es casual (tag Idle, MiscWork,
  Fieldwork, ChangingApparel, UnloadingOwnInventory, TakeForInventoryStock, o joy/aprendizaje), no
  forzado por el jugador, `casualInterruptible`, no es de Doctor, Firefighter ni Childcare y no carga a nadie,
  el escaneo llama `CheckForJobOverride()` para que vaya ya. No interrumpe dormir, comer, ser
  atendido, rescatar, ni órdenes del jugador.
- Si ya está mirando, el escaneo no le cambia la celda (sin ir y venir).

### Al terminar la amenaza
Para cada registro (watcher vio a X) sin caída: si X está vivo, en pie y en el mapa y el watcher
también: "X made it back" (+4, 1.5 días) y queda un abrazo pendiente 2500 ticks. El JobGiver (sin
amenaza) da `WW_Reunite`: va hasta tocar a X y hace la interacción `WW_Embrace` (si
`CanInteractNowWith`; espera hasta 300 ticks), X recibe "Y hugged me after the fight" (+2, 1 día).
Los mismos chequeos de comida/sueño/doctor aplican; sin capacidad de hablar no hay abrazo. Los
registros se limpian; si empieza otra amenaza antes del abrazo, se descarta.

### Stacking
Memorias con otherPawn: `stackLimitForSameOtherPawn` 1 (re-ganar refresca), `stackLimit` 3.
Psicópata e Inhumanized excluidos por la regla de watcher (y `nullifyingTraits` Psychopath por las dudas).

### Casos borde
- Animales, mecanoides, prisioneros, huéspedes (Hospitality), visitantes: no son `IsColonist` -> no miran.
  Ser querido tiene que ser de la facción del jugador.
- Caravanas / mapas que no son hogar: nada.
- Razas de mods: solo humanlike; edad de viejo según su lifeExpectancy.
- Combat Extended: no se toca combate; `IsFighting` y `Drafted` siguen valiendo.
- Save/load: el MapComponent guarda los registros (watcher, objetivo, vio caer, abrazo pendiente).
  Los planes no se guardan (se recalculan en 250 ticks). Agregar a una partida existente: seguro.
  Sacarlo: el JobDef desaparece de los jobs guardados (error vanilla de job faltante una vez).
- Temperatura: una puerta entreabierta iguala temperatura como cualquier puerta abierta.
- Puerta entreabierta y raiders: el watcher se va si un hostil se acerca a < 14 de la celda/puerta;
  la puerta se cierra sola 110 ticks después.

## Hooks (verificados en decomp 1.6.4871)

- ThinkTreeDef propio con `insertTag` `Humanlike_PreMain` (Data/Core/Defs/ThinkTreeDefs/Humanlike.xml:358,
  después de emergencias, lord duties, traits; antes de `MainColonistBehaviorCore`, donde están
  comida/sueño/joy/trabajo). Sin Harmony, sin PatchOperation sobre el árbol: no choca con mods que
  parchean nodos.
- `GenHostility.AnyHostileActiveThreatToPlayer` (RimWorld/GenHostility.cs:227), `IsActiveThreatTo` (:311).
- `PawnUtility.IsFighting` (RimWorld/PawnUtility.cs:1065).
- `GenSight.LineOfSight(start, end, map)` (Verse/GenSight.cs:122) - reimplementado en `WatchSight`
  con la misma recta (versión rect, :70) para tratar una puerta como abierta. `CanBeSeenOver`
  (Verse/GenGrid.cs:237).
- `MoteBubble.SetupMoteBubble` + `arrowTarget` (RimWorld/MoteBubble.cs), `MoteAttachLink.UpdateTarget`
  para el temblor (Verse/MoteAttachLink.cs:35), `Mote.Maintain` (Verse/Mote.cs:227).
- `Building_Door.StartManualOpenBy` -> `DoorOpen(110)` (RimWorld/Building_Door.cs:516).
- `Pawn_JobTracker.CheckForJobOverride` (Verse.AI/Pawn_JobTracker.cs:571) y expiración con
  `checkOverrideOnExpire` (:166-185).
- `Pawn_InteractionsTracker.TryInteractWith` / `CanInteractNowWith` (RimWorld/Pawn_InteractionsTracker.cs:152,176).
- `RegionTraverser.BreadthFirstTraverse` (Verse/RegionTraverser.cs:215), `Room.UsesOutdoorTemperature`,
  `TouchesMapEdge`, `IsDoorway` (Verse/Room.cs).

## Contenido
- C#: MapComponent_WindowWatchers (escaneo, registros, alivio), WatcherUtility (reglas),
  WatchSight (LOS), CellFinder propio, JobGiver_WatchLovedOne, JobDriver_WatchLovedOne,
  JobDriver_Reunite, MoteBubble_Worry, DefOf.
- XML: ThinkTreeDef, 2 JobDefs, 5 ThoughtDefs, InteractionDef + RulePack, 2 ThingDefs de mote.
- Texturas: icono de preocupación (ojo con lágrima) y de abrazo (dos corazones), generadas.
- Sin DLC requerido. Sin Harmony patches (Harmony queda como dependencia del scaffold: no la usa;
  se saca la dependencia).

## Compat
Ver al final ("compat probada con").

## Plan de prueba (arnés WindowWatchersTest, quicktest)
Setup: casa de prueba construida por el arnés (cuarto 7x7 de paredes con techo y una puerta en el
lado este) en un área abierta; Madre M (draftada) afuera al este a ~10 celdas de la puerta; hija C
(niña, o adulto con violencia deshabilitada si no hay Biotech) adentro, relación Parent; colono N sin
vínculo con M (negativo); raider R lejos (40 celdas, quieto, desarmado) para que haya amenaza.
PASS:
1. Sin amenaza: el giver no da job a C (negativo).
2. Con amenaza: C recibe WW_Watch con objetivo M, llega a una celda techada interior adyacente a la
   puerta, la puerta queda abierta, y la burbuja WW_Mote_Worry existe con arrowTarget M.
3. N (sin vínculo) no recibe WW_Watch (negativo).
4. Adulto no-combatiente sin necesidad de categoría (colono sano, adulto, violento) no mira (negativo).
5. Hambre: con comida en Hungry el giver no devuelve job (negativo, no interfiere).
6. M recibe daño -> C gana WW_WatchedHurt y la burbuja está temblando.
7. Sin LOS (puerta del jugador bloqueada/pared) -> no hay celda -> no mira (negativo).
8. Raider a < 14 de la celda -> C deja de mirar.
9. M cae (anestesia) -> C gana WW_WatchedFall.
10. Segunda pareja: M2 draftada, hijo C2 mira; se mata/aleja el raider -> amenaza terminada -> C2 gana
    WW_Relief, recibe WW_Reunite, y M2 gana WW_Embraced (abrazo ocurrido).
11. M muerto mientras C2 mira -> WW_WatchedDie (si alcanza el tiempo; si no, se reporta no probado).

## Resultado de pruebas (2026-10-17)
- Arnés `WindowWatchersTest` (quicktest, 18000 ticks): **33 PASS, 0 FAIL, 0 errores** -
  logs/run-20261017-003759.log (build final). Incluye: C y E arrancan AFUERA de la casa y entran a
  mirar; puerta entreabierta; burbuja con flecha; temblor rojo al herir; salida por enemigo cerca y
  vuelta; sin LOS no mira; caída, muerte, alivio y abrazo; negativos (sin vínculo, adulto sano, hambre,
  sin alivio para quien vio caer).
- Si el mapa no tiene un área despejada de 20x20, el arnés limpia una a mano (lo loguea).
- Cambio después de la revisión: un pawn parado en la celda ya no la bloquea (solo la reserva de
  destino); E tardaba ~1700-2000 ticks por el caché "sin celda", ahora ~660.

## Compat probada con
- Common Sense (`avilmask.commonsense`) + Hospitality (`orion.hospitality`) + Dubs Bad Hygiene
  (`dubwise.dubsbadhygiene`) + Living Bonds (`bsandyh.livingbonds`, inserta en Humanlike_PreMain por
  xpath) juntos: 33 PASS, 0 errores (logs/run-20261017-003643.log, build anterior a quitar el chequeo
  de ocupante; el cambio no toca el árbol).
- Achtung! (`brrainz.achtung`): carga sin errores ni warnings, pero el quicktest queda en MAP READY
  (diálogo de Achtung pausa el juego, ya conocido) - el arnés no corrió. No se insistió.
- No probado: VEF (`Reloading_AmmoReload_Colony` en PreMain; ambos son ThinkTreeDefs por insertTag,
  componen por insertPriority), Simple Sidearms, LTO Colony Groups, Search and Destroy (solo draftados),
  Combat Extended (no instalado).
- `rw validate` contra el perfil de prueba tal como quedó (233 mods, casi toda la lista de Nahuel,
  dejado por otra corrida): 0 errores, 0 warnings (solo XML/defs, sin correr el juego).
- Preview.png: omitido (no hay captura de showcase).
