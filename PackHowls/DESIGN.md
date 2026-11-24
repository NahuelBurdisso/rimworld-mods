# The Pack Howls (`nahuel.packhowls`) - diseño

Idea `the-pack-howls` (forja 2026-10-12 §3, puntaje 55). Originalmente iba al pack Duelo; va como mod propio
(Harmony + Core, DLC opcionales).

## Pitch

Cuando un colono, o un animal vinculado, cae en combate en el mapa de hogar, la primera noche tranquila los perros y
lobos de la colonia dejan lo que hacen, se sientan mirando hacia donde cayó y aúllan, con anillos tenues que suben. Si
hay lobos salvajes en el mapa, contestan desde lejos unos segundos después. Los colonos que lo oyen reciben "the pack
howled for X" (+3); la familia, pareja o dueño del caído, la versión fuerte (+5). Un aullido por pelea, no por muerte.

## Qué ve el jugador

- Mensaje (no carta): "The pack howls for Rex." / "The pack howls for Anna and Rex." / "... and 2 others."
- Cada perro/lobo de la colonia elegible: job "howling for the fallen." durante 7 s (420 ticks), quieto, de cara a la
  celda donde murió la última víctima (si coincide con su celda o no es válida: norte). Dos aullidos (tick ~0-40 con
  desfase aleatorio, y +210), anillos blancos tenues que suben desde la cabeza cada 35 ticks.
- 600 ticks después del primer aullido, hasta 3 aulladores salvajes (sin facción) del mapa hacen lo mismo.
- Memoria en el panel de ánimo.

## Reglas exactas

**Víctima califica** (prefijo/postfijo de `Pawn.Kill`, sólo si de verdad murió):
- `IsColonist` (incluye esclavos seguros, como vanilla), o animal (`RaceProps.Animal`) de la facción del jugador con al
  menos un vínculo `Bond`.
- `MapHeld` es hogar del jugador (`map.IsPlayerHome`). Caravanas, mapas temporales y mundo: no.
- **En combate**, cualquiera de:
  - el instigador es hostil al jugador (`HostileTo(Faction.OfPlayer)`, incluye manhunters), o ya está destruido
    (raider muerto: su molotov/mortero) y su facción es hostil;
  - hay `dinfo` sin instigador hostil (fuego amigo) con daño de violencia externa (`ExternalViolenceFor`) y una amenaza
    real activa;
  - no hay `dinfo` (sangrado) y el culpable es nulo, `BloodLoss` o una herida, y (amenaza real activa, o
    `mindState.lastHarmTick` en las últimas 12 h = 30000 ticks: sólo lo setea violencia externa).
  - "Amenaza real" = `GenHostility.IsActiveThreatTo` sobre `attackTargetsCache.TargetsHostileToFaction(player)`,
    **ignorando pawns de la facción del jugador** (el animal vinculado del colono caído entra en Berserk/Manhunter
    25%+25% al morir su humano, `Pawn_RelationsTracker.cs:1105-1129`; sin este filtro postergaba el aullido hasta 3 días
    y su muerte a manos de colonos contaba como caído en combate).
- Ejecuciones, eutanasia, vejez, enfermedad, hambre, infección: no (culpable no violento, o sin amenaza ni daño reciente).

**Pendiente** (`MapComponent_PackHowls`, uno por mapa): lista de víctimas (refs, guardadas aunque destruidas) + nombres,
`firstDeathTick`, `lastDeathTick`, `lastDeathCell`. Muertes nuevas se suman al pendiente no disparado del mapa. Si ya
disparó, la próxima muerte arma uno nuevo. Al morir, si el mapa no tiene ningún animal aullador del jugador vivo, no se
encola (nadie va a aullar).

**Disparo** (chequeo cada 250 ticks, sólo con pendiente):
- sin amenaza real (ver arriba);
- noche local: `GenLocalDate.HourOfDay(map)` >= 21 o < 4;
- al menos 2500 ticks desde la última muerte;
- la condición de amenaza se ignora pasados 3 días desde la primera muerte (mapa con amenaza permanente: dispara igual);
- al menos un aullador del jugador elegible. Si a los 6 días no disparó: se descarta en silencio.

**Aulladores**: animal cuya raza está en `PackHowls_Howlers.races`, o cuya `soundCall` de alguna etapa de vida está en
`PackHowls_Howlers.callSounds` (Pawn_Dog_Call, Pawn_DogSmall_Call, Pawn_Warg_Call: cubre Husky, Labrador, Yorkshire,
lobos (incl. Wolf_Great de Odyssey), warg, bog hound y los cánidos de Alpha Animals que reusan esos sonidos), menos
`excludedRaces`. Elegible: spawneado, vivo, no `Downed`, no `InMentalState`, no `Drafted`, sin `Lord`, no atado
(`roping.IsRoped`), no en llamas, no `Starving`, no `ShouldSeekMedicalRest`, sin cosa cargada, y su job actual es nulo,
`casualInterruptible` o `LayDown`/`LayDownAwake` (los dormidos se despiertan: es el punto del aullido). Mapa con niebla:
salvajes en celdas con niebla no contestan.
- De la colonia: `Faction == OfPlayer`. Todos los elegibles aúllan.
- Salvajes: `Faction == null`, no manhunter (`InMentalState` ya los excluye); hasta 3, al azar.

**Memorias** (al disparar, `TryGainMemory(def, otherPawn)`):
- Receptores: colonos libres (`FreeColonistsSpawned`) del mapa con `needs.mood`.
- Fuerte (`PackHowls_PackHowledForMine`, +5, 2 días): cónyuge, prometido/a, pareja o vínculo (relación directa), o
  familia de sangre (`FamilyByBlood`, incluye relaciones implícitas: hermanos, abuelos, nietos, tíos). Exes no.
  `otherPawn` = esa víctima. Distancia: cualquiera en el mapa.
- Normal (`PackHowls_PackHowled`, +3, 1 día): el resto a <= 45 celdas de algún aullador. Dormidos incluidos: a la noche
  casi todos duermen; lo oyen igual (decisión: la spec decía "despiertos", pero a la hora del aullido eso lo deja casi
  sin efecto). `otherPawn` = primera víctima.
- `stackLimit 1`, psicópatas excluidos (`nullifyingTraits`).

**Casos borde**: mapas de Odyssey con motor de gravnave cuentan como hogar (`Map.IsPlayerHome`), a propósito. Prisioneros/visitantes no son víctimas ni receptores. Mecanoides: no víctimas. Razas de mods humanlike:
`IsColonist` las cubre. Un muerto que resucita (Anomaly) antes del aullido igual figura (no se revisa). Save/load: el
pendiente y el aullido en curso (tick de respuesta salvaje) se guardan; los jobs se guardan solos. Combat Extended: no se
toca daño; sólo leemos el instigador.

## Hooks (verificados en decomp 1.6.4871)

- `Pawn.Kill(DamageInfo? dinfo, Hediff exactCulprit = null)` `Verse/Pawn.cs:2096` - Prefix (mapa, clasificación) + Postfix
  (`__instance.Dead`). Duelo también lo parchea (Prefix/Postfix, sin `return false`): compatibles.
- `GenHostility.AnyHostileActiveThreatToPlayer(Map, bool, bool)` `RimWorld/GenHostility.cs:227`; `HostileTo(Thing, Faction)` :118.
- `Pawn_MindState.lastHarmTick` `Verse.AI/Pawn_MindState.cs:89`, seteado en `Notify_DamageTaken` sólo con `ExternalViolenceFor` (:659).
- `GenLocalDate.HourOfDay(Map)` `RimWorld/GenLocalDate.cs:16`.
- `Pawn_JobTracker.StartJob(Job, JobCondition, ...)` `Verse.AI/Pawn_JobTracker.cs:252`, `JobMaker.MakeJob(JobDef, LocalTargetInfo)` `Verse/JobMaker.cs:23`.
- `JobDriver.DriverTick` corre cada tick y descuenta `ticksLeftThisToil` (`Verse.AI/JobDriver.cs:342-402`) -> toil `Delay` + `tickAction`.
- Sonido: SoundDef propia con `AudioGrain_Folder` `Pawn/Animal/Dog/Dog_Call` (`ContentFinder.GetAllInFolder`, busca en
  todos los mods y en el juego), pitch 0.62~0.72; `SoundInfo.InMap` + `pitchFactor` por tamaño corporal.
- Fleck: `FleckCreationData` + `map.flecks.CreateFleck` (`FleckSystemThrown`, `velocityAngle 0` = hacia arriba en
  pantalla), textura propia `Things/PackHowls/HowlRing` (anillo blanco suave). No se usa `WaterRipple`: su
  `Graphic_FleckSplash` dibuja en la capa de profundidad de agua.
- `MemoryThoughtHandler.TryGainMemory(ThoughtDef, Pawn otherPawn, Precept)` `RimWorld/MemoryThoughtHandler.cs:129`.

## Settings

`wildAnswer` (true), `moodlets` (true), `message` (true).

## Plan de prueba (arnés `PackHowlsTest`, armado por `rwtools-ticks`)

Seams públicos: `PackHowlsUtility.HourOverride` (-1 = real) y `MapComponent_PackHowls.pending.lastDeathTick`.
Todos los pawns de prueba se generan (sin relaciones aleatorias, sin psicópatas).
1. Lista de aulladores: Husky/Wolf_Arctic/Yorkshire sí, Cat no.
2. Con un raider vivo: colono muerto por el raider -> 1 pendiente; perro vinculado -> mismo pendiente (2, con nombres);
   animal sin vínculo -> no; fuego amigo (colono mata colono) durante la pelea -> 3.
3. Raider muerto -> no suma; sin amenaza. Colono herido por el raider que muere después con `Kill(null)` (sangrado) -> 4;
   herido que muere de desnutrición -> no; fuego amigo sin pelea -> no; colono sin daño con `Kill(null)` -> no.
   `lastDeathCell` = celda del último.
4. Hora 12, quietud cumplida -> no dispara en 300 ticks; husky2 dormido.
5. Hora 23 + husky3 en Manhunter (amenaza vanilla, no "real") -> dispara en <= 250 ticks: husky1 y husky2 (despierto) con
   job `PackHowls_Howl`, gato no, lobo no, husky3 no; mensaje con nombres; memorias: cónyuge fuerte (label y descripción
   nombran al caído, no al lector), hijo y nieto (relación implícita) fuertes, otro colono normal, colono a > 75 celdas
   nada.
6. +60: ambos huskies miran a la celda de la última muerte, quietos.
7. +370: >= 4 aullidos, >= 16 anillos (2 x 8).
8. +630: el lobo salvaje contesta; los huskies volvieron a su IA.
9. +500 más: no se repite.

## Resultado

- Arnés: 43 PASS, 0 errores (logs/run-20261016-225252.log).
- Compat probada con: Ragdoll Physics (tk421storm.Ragdoll, también parchea `Pawn.Kill`), Alpha Animals (+ VFE Core),
  Vanilla Events Expanded, Duelo (nahuel.duelo, también parchea `Pawn.Kill`): 43 PASS, 0 errores
  (logs/run-20261016-225606.log). Alpha Animals: AA_DuskProwler, AA_Frostling, AA_Nightling, AA_OcularNightling,
  AA_RipperHound y AA_Skiphound quedan como aulladores por su voz de perro/warg.
- Overlap con mods instalados: ninguno. Lo más cercano: "midnight howler" de 1-2-3 Personalities (sólo una variante de
  personalidad de animal) y los aullidos de Sightstealer (Anomaly, horror).
- No probado: save/load con pendiente o aullido en curso, toggles de settings, varios mapas, Combat Extended, cómo
  suena realmente el aullido (pitch bajo del Dog_Call: no se escuchó), cómo se ve el anillo (sin showcase/captura).
