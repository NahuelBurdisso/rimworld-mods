# A Light for the Absent (`nahuel.lightfortheabsent`)

Idea `luz-para-el-ausente` del ledger (2026-10-11). Mod standalone, C# **sin Harmony** (ningún patch,
todo por polling; la dependencia del scaffold se sacó en la revisión), sin DLC.

## Pitch

Mientras un colono está lejos (caravana, cápsulas, lanzadera, mapa de misión, secuestrado), en su
cuarto se enciende al anochecer una luz ámbar tenue que alguien dejó prendida. De día se apaga. Cuando
vuelve de noche, la luz se apaga despacio y él piensa "they left a light on for me". Si muere lejos,
la luz parpadea unas noches y se apaga para siempre; su pareja y su familia en el mapa lo sienten.
Mientras está fuera, su pareja que comparte la cama duerme liviano.

## Qué ve el jugador

1. De noche, en el cuarto del ausente: un halo ámbar suave sobre su cama (respira apenas, como una
   vela) y luz real de vela: sólo la celda de la cama queda "Lit". De día no hay nada.
2. Vuelve de noche: el halo se apaga en ~2,4 h de juego (fade continuo). Thought
   `LightForTheAbsent_LeftALightOn` "they left a light on for me" +3, 2 días, si la luz estuvo
   prendida al menos ~1 h de juego (10 polls, `MinLitPollsForThought`) mientras estaba fuera: salir y
   volver enseguida de noche no da el thought. Si vuelve de día: la luz ya estaba apagada,
   el registro se cierra (y el thought igual, si hubo noches).
3. Muere lejos: desde esa noche, el halo parpadea de forma irregular (vela que se ahoga). A la
   3.ª noche, tras ~2 h encendida, se apaga para siempre: mensaje "The light in X's room has gone
   out." y la pareja / familia directa en el mapa recibe `LightForTheAbsent_LightWentOut`
   "the light for {0} went out" −3, 8 días (una por muerto). Es un matiz sobre el thought vanilla
   de muerte (que sigue igual), no un reemplazo.
4. Pareja que comparte la cama, dormida en ella mientras el otro está fuera: `LightForTheAbsent_SleptLightly`
   "slept lightly" −2, medio día (se refresca mientras siga, stack 1).

## Decisión: la luz cuenta (es luz real, de vela)

La luz es un `CompGlower` vanilla registrado en el glow grid, así que es luz de verdad: el sistema
de iluminación del juego es el que la dibuja sobre paredes y muebles. Se eligió fuerza de vela
(color (60,36,12) × brillo, radio 2,5): con la matemática de `GlowGrid` (`Verse/GlowGrid.cs`
`GroundGlowAt`: max canal/255 × 3,6, tope 0,5 para luz artificial) la celda de la cama queda
"Lit" (≈0,5) y a una celda ya es "Dark" (≈0,18). Consecuencias aceptadas y
documentadas: un ausente no está en el cuarto, así que casi nadie la usa; no llega a Overlit;
no hace crecer plantas (luz artificial tope 0,5 < 0,51 de las plantas); no gasta energía.
La visibilidad la da el halo (dibujo puro, `MoteGlow` aditivo, sin efecto en gameplay) — el
mismo enfoque que el Vigil Lamp de Aftermath, con colores y tamaño propios.
El fade y el parpadeo se hacen en el halo por frame (barato); el glow grid se actualiza sólo en
cada poll (escala el color en pasos), así el costo de recalcular luz es mínimo.

## Reglas exactas

**Tracker:** `AbsenceTracker : GameComponent` (guardado), poll cada **250 ticks**
(`TicksGame % 250 == 17`), y uno inmediato en el primer tick tras cargar (`known` no se guarda: así un
secuestrador que ya carga a un colono no se lo lleva sin que la cama quede recordada). Sin Harmony.

**Elegible (el ausente):** humanlike, `Faction == OfPlayer`, `IsColonist` (incluye esclavos
"seguros"; excluye prisioneros), no quest lodger (`QuestUtility.IsQuestLodger`), no mutante,
con `needs.mood`. Animales, mecanoides, huéspedes (Hospitality: facción ajena) nunca.

**Cama:** `Building_Bed` del jugador en un mapa hogar (`Map.IsPlayerHome`), `def.building.bed_humanlike`,
`bed_countsForBedroomOrBarracks` (excluye el ataúd de deathrest, que también es `Building_Bed`), no médica, `ForColonists` o `ForSlaves`(no prisioneros). Se leen sus dueños con
`bed.OwnersForReading` (**no** `ownership.OwnedBed`: Sensible Bed Ownership, activo en la lista
de Nahuel, reemplaza ese getter y un pawn puede tener camas en varios mapas).

**En casa / ausente:** un pawn está en casa si `MapHeld` es un mapa hogar (incluye criosueño y
transportadores en el mapa: `MapHeld` los resuelve). Está ausente si vive y `MapHeld` es null
(caravana, cápsulas en vuelo, secuestrado, world pawn) o un mapa no-hogar (misión, asentamiento
enemigo). Los pawns a bordo de la gravship en vuelo (`Find.CurrentGravship.Pawns`) **no** están
ausentes: viajan con su cama.

**Detección (cada poll, por mapa hogar):**
1. Recorre las camas (`listerThings.ThingsInGroup(ThingRequestGroup.Bed)` del jugador; no
   `AllBuildingsColonistOfClass`, que recorre también todas las paredes). Para cada
   dueño elegible: si está en casa → `known[pawn] = bed` (memoria, no guardada) y si tenía un
   registro Away → **Return**. Si está ausente y no tiene registro → nuevo registro **Away**.
2. Para cada `known` sin registro que ya no aparece como dueño (secuestro y aterrizaje de gravship
   le borran la cama, research §1/§3): si vive y está ausente → registro Away con la cama recordada.
   Muertos / no elegibles → se olvidan.

**Registro (`AbsentRecord`, guardado):** pawn (`saveDestroyedThings: true`), cama, estado
(Away / Returning / Mourning), `nightsLit`, `litTonight`, `nightLastPoll`, `fadePolls`, `mourningNight`,
`mourningLitPolls`, `coOwners` (quién más era dueño de la cama al empezar la ausencia: cama doble
compartida, cuchetas; por referencia). La luz (`LightForTheAbsent_Light`) **no se guarda** (`isSaveable false`): el
poll la vuelve a crear. **Una luz por cama** (`lamps`, en el tracker): cada registro pide luz
(nivel, parpadeo) y al final del poll la cama recibe el nivel más alto y parpadea sólo si todos sus
ausentes están muertos (una pareja que se va junta no apila dos halos ni dos glowers). La luz nueva
recibe el color antes de spawnear: el glower se registra una sola vez.

**Noche:** `GenCelestial.CurCelestialSunGlow(map) < 0.5` (sol astronómico: tormentas y eclipses
no la prenden; polar/estaciones correctos). Hook de test: `AbsenceTracker.NightOverride`.

**Cuarto:** `bed.GetRoom()` no null, no `PsychologicallyOutdoors`, no `TouchesMapEdge`. Barracas
cuentan (la luz está en su cama). Sin cuarto (cama a la intemperie) → registro sí, luz no.

**Estado Away:**
- Cama destruida, o pawn `Discarded` → se borra el registro. Cama no spawneada: si está a bordo de la
  gravship en vuelo (`Find.CurrentGravship.Things`) → sin luz, espera; si no (minificada) → se borra.
- Cama "regalada": el ausente ya no es dueño y algún dueño actual no es su pareja
  (`LovePartnerRelationExists`) ni estaba en `coOwners` → se borra el registro (sin thought). Si el
  ausente sigue siendo dueño (alguien tomó el lado libre de una cama doble), la luz sigue.
- Pawn que ya no es nuestro (`StillOurs`: humanlike, facción del jugador, no prisionero; más laxo que
  `Eligible` para que un esclavo con un mental break lejos no pierda la luz) → se borra.
- Pawn muerto → **Mourning** (`mourningNight = 1` si es de noche, si no 0).
- Si no: `lit = noche && cuarto`. La primera vez que prende en una noche → `nightsLit++`
  (`litTonight`, que se limpia de día: perder y recuperar el cuarto la misma noche no cuenta doble).
  Cada poll prendida suma `litPolls` (para el thought).
- Pareja que duerme liviano: para cada dueño de la cama que es pareja del ausente, en casa,
  `CurrentBed() == bed` y `curDriver.asleep` → `SleptLightly` con otherPawn = ausente.

**Return** (en casa de nuevo): si `nightsLit >= 1` → thought `LeftALightOn`. Si la luz está
prendida → **Returning** con `fadePolls = 24` (6000 ticks ≈ 2,4 h); si no, registro borrado.
**Returning:** cada poll `fadePolls--`; intensidad = fadePolls/24 (el halo interpola por tick
entre polls). Al llegar a 0, o de día, la luz se apaga y el registro se borra. Si se vuelve a ir
→ Away de nuevo con `nightsLit = 0`.

**Mourning** (murió lejos): luz sólo de noche y con cuarto. Cada atardecer (`nightLastPoll`, del cielo,
no de la luz) `mourningNight++`. Sin cuarto un rato → luz apagada, el duelo sigue. Resucitado lejos
(`!p.Dead`) → vuelve a Away. Cadáver traído a casa (`IsHome`) → termina en silencio (el duelo vanilla
sigue). La luz parpadea (halo con caídas irregulares; glow grid a 60%). En la
noche 3, tras **20 polls** prendida (5000 ticks ≈ 2 h), o al amanecer de esa noche si fue más corta
(polos) y llegó a prender → **se apaga para siempre** (si nunca prendió en la 3.ª noche, termina en
silencio): mensaje (setting), y a cada colono o esclavo humanlike
spawneado en el mapa de la cama (`SpawnedPawnsInFaction(OfPlayer)`, sin prisioneros), con `needs.mood`, que es Spouse / Lover / Fiance / Parent / Child /
Sibling / HalfSibling del muerto → `LightWentOut` (otherPawn = muerto). Registro borrado.
Cama destruida o regalada durante el duelo → se borra sin memoria.

**Settings:** `moodlets` (on), `message` (on), `brightness` 0,25–2 (1). Settings off → la luz sigue.

**En casa:** un mapa de bolsillo (undercave, laberinto) cuenta como el mapa del que cuelga
(`PocketMapParent.sourceMap`).

**Casos borde:** reconfiguración de mapas (gravship): la luz es Ethereal y no viaja; si
`light.Map != bed.Map` o la posición cambió, se destruye y se recrea. Mapa hogar abandonado: sus
cosas se descartan (cama `Destroyed`) → registro borrado. Un pawn que muere a menos de 250 ticks de
irse (antes del primer poll) no tiene registro → sin duelo de luz (aceptado: evita falsos duelos).
Hooks estáticos de test se resetean en `FinalizeInit`. Si `Update` tira una excepción, el pawn queda
ignorado hasta recargar (no se recrea el registro cada poll). Aceptados y documentados (REVIEW.md):
colonos "perdidos" en un mapa neutral que se cierra quedan Away para siempre (luz eterna); noche o día
polar continuo demoran el duelo hasta que vuelva a salir el sol; pawns en un contenedor a bordo de la
gravship generan un registro Away que no hace nada. Mod agregado a
mitad de partida: los ausentes que siguen siendo dueños de su cama se detectan en el primer poll
(no los secuestrados de antes).

## Hooks verificados (decomp 1.6.4871, ver research)

- `Pawn.ExitMap` (`Verse/Pawn.cs:2513`): colono libre que se va en caravana / cápsulas / lanzadera
  **conserva la cama** (`UnclaimAll` sólo si `flag5`, liberados / huéspedes).
- Muerte (`Pawn.Destroy` `Pawn.cs:2361`), secuestro (`PreKidnapped` `Pawn.cs:2660`) y aterrizaje de
  gravship (`CompAssignableToPawn.PostSwapMap` L222) **borran la cama** → el registro guarda la suya.
- `Thing.MapHeld` (`Verse/Thing.cs:245`), `Map.IsPlayerHome` (`Verse/Map.cs:354`),
  `Find.CurrentGravship` (`Verse/Find.cs:186`) + `Gravship.Pawns` (`RimWorld.Planet/Gravship.cs:128`).
- `CompGlower.GlowColor` setter re-registra (`Verse/CompGlower.cs:357`); `GlowGrid.GroundGlowAt`.
- `GenCelestial.CurCelestialSunGlow(Map)` (`RimWorld/GenCelestial.cs:59`).
- `RestUtility.CurrentBed` (`RimWorld/RestUtility.cs:493`), `JobDriver.asleep`.
- Thought_Memory con otherPawn formatea `{0}` y `{OTHERPAWN_*}` y guarda otherPawn destruido
  (`RimWorld/Thought_Memory.cs:73,113`).
- `GenSpawn.Spawn` de Ethereal sobre la cama no borra nada (`GenSpawn.SpawningWipes`).

## Costo

Poll cada 250 ticks: recorre camas de mapas hogar (decenas) + registros (pocos). Sin LINQ en el
poll salvo el enumerador de `AllBuildingsColonistOfClass`. El halo dibuja sólo en pantalla
(`RealtimeOnly`), materiales cacheados. Test mide el poll.

## Plan de prueba (arnés `LightForTheAbsentTest`, `rwtools-ticks`)

Setup: cuartos techados con camas (doble para A+B pareja, simple para C, simple a la intemperie
para D, simple para G), polling automático pausado, noche forzada con `NightOverride`.
- P1 A sale en caravana (`CaravanExitMapUtility.ExitMapAndCreateCaravan`) de noche → registro Away,
  luz en su cama, `Glows`, glow grid > 0 en la celda (sin cielo), no seleccionable, no guardable; B y C sin luz.
- P2 día → luz destruida, registro sigue, `nightsLit == 1`; otra noche → 2.
- P3 B (esposa) dormida en la cama doble → `SleptLightly` sobre A; C (no pareja) dormido → nada.
- P4 A vuelve (`CaravanEnterMapUtility.Enter`) de noche → Returning, thought `LeftALightOn`;
  fade baja la intensidad; tras 24 polls luz y registro desaparecen.
- P5 C sale y vuelve de día → nunca luz, sin thought (negativo).
- P6 D (cama a la intemperie) sale de noche → registro, sin luz (negativo).
- P7 Cama regalada: C sale de noche, luz; F (no pareja) reclama la cama → registro y luz fuera.
- P8 Secuestro: G (conocido) secuestrado → registro Away con la cama recordada, luz.
- P9 A sale otra vez y muere en la caravana → Mourning, parpadeo; 3 noches; en la 3.ª, tras 20 polls:
  luz fuera para siempre, B (esposa) y E (padre/madre) reciben `LightWentOut`; C no (negativo); mensaje.
- P10 costo del poll < 1 ms.
- Revisión (REVIEW.md): R1 resucitado lejos → Away, sin duelo; S1 guardado (DebugOutputFor) de un
  registro en duelo y uno Away; R4 cuarto roto en duelo → sin luz, duelo sigue; R2 cama doble con
  co-dueño no pareja → luz; R3 cama minificada → registro fuera. Segunda revisión: R5 salida corta de
  noche → sin thought; A3 ataúd de deathrest no es cama; A1 alguien toma el lado libre → la luz sigue;
  A2 dos ausentes en la misma cama → una sola luz.
- Sin errores del mod en el log.

## Compat

Compat probada con: Sensible Bed Ownership (sensiblebedownership.1trickPwnyta), Hospitality (Orion.Hospitality),
Dubs Bad Hygiene (Dubwise.DubsBadHygiene): 57 PASS, 0 errores (logs/run-20261016-234620.log); tras las
dos revisiones: 79 PASS, 0 FAIL, 0 errores (logs/run-20261017-003149.log). Solo: 79 PASS (logs/run-20261017-003040.log).
