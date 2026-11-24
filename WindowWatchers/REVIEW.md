# Window Watchers - revisión

Revisión de un agente `code-reviewer` (solo lectura, contra DESIGN.md y decomp 1.6.4871), 2026-10-16.
Correcto según la revisión: expiración + IsContinuation sin churn, reserva de destino, wrapper
ConditionalColonist (guests de Hospitality corren Humanlike_PreMain en su duty Relax), detección de
herida por SummaryHealthPercent (solo heridas), mantenimiento del mote.

| # | Hallazgo | Estado |
|---|----------|--------|
| 1 | Salida por peligro: el job terminaba y el giver lo volvía a dar en la misma celda (loop) | Confirmado también en el juego (compat run 1 FAIL "C stops watching when an enemy comes close"). Arreglado: giver y escaneo chequean `ThreatTooClose`; el escaneo termina jobs inseguros |
| 2 | Abría puertas prohibidas por el jugador | Arreglado: `DoorUsable` (`IsForbiddenToPass`, facción, vacío de Odyssey); CheckSpot termina si la puerta deja de ser usable |
| 3 | Sacaba de la cama a convalecientes / enfermos (LayDown re-piensa cada 211 ticks) | Arreglado: no mira si `ShouldSeekMedicalRest` y está en cama o tiene enfermedad inmunizable |
| 4 | Bebés en el piso = "pacientes esperando" siempre | Arreglado: `LifeStageUtility.AlwaysDowned` excluido |
| 5 | LOS a través de una puerta que otro abrió de casualidad | Arreglado (y visto en el juego, run 2 FAIL del spot): las puertas bloquean salvo la propia entreabierta o HoldOpen; la vista de los hostiles sí cuenta puertas abiertas |
| 6 | No re-chequeaba peligro mientras camina | Arreglado: toil de ir chequea cada 60 ticks |
| 7 | Registros de una amenaza pasaban a la siguiente | Arreglado: al empezar una amenaza nueva se limpian |
| 8 | Interrumpía Childcare (biberón/amamantar) | Arreglado: Childcare excluido |
| 9 | Costo de la búsqueda | Arreglado en parte: regiones fuera de 36 celdas se saltean, CanReserve después de la LOS, caché "sin celda" 500 ticks por watcher+objetivo, componente cacheado por mapa (dict estático) |
| 10 | Puerta abierta deja pasar enemigos | Mitigado: radio de la puerta 24 (antes 14), chequeo cada 30 ticks |
| 11 | Tras cargar partida perdía el job antes del primer escaneo | Arreglado: si ya mira, el giver usa el job actual como plan |
| 12 | Un pawn pasando por la celda cortaba el job | Arreglado: no se chequea si ya está mirando |
| 13 | Watcher que no puede hablar esperaba el abrazo en vano | Arreglado: sin Talking no hay abrazo (sí alivio) |
| 14 | Reunión consumida en el giver | Arreglado: se consume en `Notify_Starting` del driver |
| 15 | Defensores no draftados: IsFighting parpadea | No arreglado (sospechado). El job termina Succeeded y se re-planea en el próximo escaneo; costo bajo |
| 16 | Pawns afuera casi nunca encontraban celda | Confirmado en el juego (run 4 FAIL "E starts watching M2"). Arreglado: si arranca afuera, el BFS puede cruzar regiones exteriores sin hostil a < 14 de su centro |
| 17 | Burbuja desaparecía de golpe | Arreglado: `fadeOutUnmaintained` |
