# The Dog Knows (`nahuel.thedogknows`) - diseño

Idea `the-dog-knows` (forja 2026-10-12 r3, puntaje 50; ranking 1 de `docs/forge/loop90/reserva-vs-instalados.md`).
Mod propio, C# sin parches Harmony (la dependencia de Harmony queda del scaffold, `PatchAll()` no encuentra nada).
Requiere Anomaly (`ludeon.rimworld.anomaly`).

## Pitch

Los perros y gatos de la colonia sienten lo que los colonos no ven. Cuando un revenant, un sightstealer o cualquier
hostil invisible pasa cerca, el animal deja lo que hace, se queda duro mirando hacia ese lugar vacío, gruñe bajo y
se le eriza el lomo. Al seleccionarlo dice "Staring at something you can't see." Es una pista, no una revelación:
la entidad sigue invisible, no se vuelve seleccionable ni targeteable.

## Qué ve el jugador

- El animal se para (job `TDK_StareAtUnseen`, 300-420 ticks = 5-7 s) y gira la cabeza siguiendo a la entidad
  (sólo 4 direcciones: `Rot4`, la pista es gruesa). Tiembla un poco (jitter 0.03 cada 12 ticks: lomo erizado).
- Encima del animal, un icono propio (`Things/Mote/TDK_Hackles`: "!" con púas) que dura ~3 s.
- Un gruñido grave: el `soundAngry` de su etapa de vida con pitch ×0.72 (perros) / ×0.85 (gatos), al empezar y a
  la mitad. Sonido en la posición del animal, nunca en la de la entidad.
- Inspect: "Staring at something you can't see." (reportString del JobDef).
- Mensaje (setting, on por defecto): "Rex is staring at something you can't see." con lookTarget = el animal,
  `MessageTypeDefOf.CautionInput`, como mucho uno cada 2500 ticks por mapa.

## Reglas exactas

**Entidad invisible ("unseen")** - `TDKUtility.IsUnseen(Pawn)`:
- Spawned, no muerta, celda sin niebla (gotcha 20d: la niebla es información).
- Tiene un `HediffComp_Invisibility` con `!PsychologicallyVisible`. Se recorre `hediffSet.hediffs` a mano en vez de
  `IsPsychologicallyInvisible()`: Melee Animation (`co.uk.epicguru.meleeanimation`) parchea ese método con un prefix
  que devuelve `true` para pawns en animación de duelo (los perros mirarían a los raiders en duelo), y Performance
  Optimizer lo cachea. Cubre `HoraxianInvisibility` (Revenant, Sightstealer: ocultos al jugador) y cualquier
  hediff con la comp (psycast `PsychicInvisibility`, hediffs de mods como `VFED_Invisibility`).
- `HostileTo(Faction.OfPlayer)`: un colono propio con psycast de invisibilidad no dispara nada.
- Entidades en plataforma de contención, quemándose, aturdidas, caídas: vanilla las fuerza visibles
  (`ForcedVisible`) => no califican.

**Animal sensible** - `UnseenSenseDef` (Def propio, extensible):
- `races` (lista de ThingDef, admite `<li MayRequire="...">`), `radius`, `requireLineOfSight`, `stareTicks`
  (IntRange), `cooldownTicks`, `growlPitch`, `growlVolume`, `growlSound` (opcional; si no, `soundAngry` de la etapa
  de vida, si no `soundCall`). Varias defs se combinan; si una raza está en dos, gana la de mayor radio.
  Otros mods agregan razas con un `UnseenSenseDef` propio o un patch `PatchOperationAdd` sobre `races`.
- `TDK_Canines`: Husky, LabradorRetriever, YorkshireTerrier; con Alpha Animals AA_Skiphound, AA_RayHound.
  Radio 11, LOS, 300~420 ticks, cooldown 2500, pitch 0.72.
- `TDK_Felines`: Cat; con Alpha Animals AA_PhoenixOwlcat. Radio 9, LOS, 300~420, cooldown 2500, pitch 0.85.

**Elegible para reaccionar** (`TDKUtility.CanReact`): animal (`RaceProps.Animal`) de `Faction.OfPlayer`, raza con
sense def, spawned, vivo, no `Downed`, despierto (`Awake()`, como vanilla con los colonos), no `InMentalState`, no
`Drafted`, sin `Lord` (caravanas en formación, rituales), no atado (`roping.IsRoped`), no en llamas, no `Starving`,
no `ShouldSeekMedicalRest`, sin cosa cargada, no escoltando a su amo (`ShouldFollowMaster`, como vanilla
`CanCasuallyInteractNow`), cooldown cumplido, y job actual: nulo, o `LayDown`/`LayDownAwake` (despierto), o
`casualInterruptible`; nunca `playerForced`, nunca Flee/FleeAndCower/FleeAndCowerShort, nunca una espera forzada
(`PawnUtility.ForceWait`: Wait/Wait_MaintainPosture/LayDown/LayDownAwake con `expiryInterval > 0`, o sea ordeñe,
esquila, doma, atención médica). Ya mirando: no se reinicia. Aullando de Pack Howls: ese job no es
casualInterruptible, no se interrumpe (al revés sí: un aullido puede cortar una mirada, aceptado).

**Detección**: entidad a distancia horizontal <= `radius` y, si `requireLineOfSight`,
`GenSight.LineOfSight(animal, entidad, skipFirstCell: true)` (paredes cortan). El animal elige la más cercana.

**Escaneo** (`MapComponent_TheDogKnows.MapComponentTick`): cada 120 ticks (`(TicksGame + map.uniqueID) % 120`).
1. Lista de unseen recorriendo `map.listerThings.ThingsInGroup(Pawn)` (como `CompSightstealer`; `AllPawnsSpawned` omite pawns dormidos con `mindState.Active == false`, y una entidad dormida oculta también se huele). Vacía => termina.
2. Si hay: copia de `SpawnedPawnsInFaction(Faction.OfPlayer)` a una lista propia, sólo animales con sense def, y evalúa cada uno.
Costo sin entidades: un loop de pawns con su lista de hediffs cada 2 s de juego.

**Mirada** (`JobDriver_StareAtUnseen`): un toil Delay de `stareTicks`, `handlingFacing`. Cada tick mira a la entidad
(`rotationTracker.Face(entity.DrawPos)`). La entidad va en un campo del driver (Scribe_References), el job **no
tiene targets**: vanilla dibuja una línea a targetA y otros mods podrían listar targets. Se entrega con un campo
estático `Pending` durante `StartJob` (tomado en `Notify_Starting`). Al guardar, si la entidad ya no está spawneada
se descarta. Termina antes (Succeeded, chequeo cada 30 ticks) si la entidad deja de ser unseen, muere, se
despawnea, cambia de mapa, se aleja más de `radius × 1.5` o (con `requireLineOfSight`) se pierde la línea de vista.
Cooldown del animal: `ahora + stareTicks.max + cooldownTicks` (se usa el máximo, no la duración sorteada: más simple
y sin diferencia práctica), en un diccionario `thingIDNumber -> tick` guardado en el MapComponent (se purga lo
vencido cuando pasa de 64 entradas). Gruñido: `growlSound`, si no `soundAngry`/`soundCall` de la etapa actual, si no
de la etapa más vieja que tenga (cachorros y gatitos vanilla no traen sonido propio).

**Lo que NO hace**: no llama `BecomeVisible`, no toca la entidad, no cambia su detección vanilla (vanilla excluye
animales en `CompSightstealer.PawnCanDetect`, `CompSightstealer.cs:93-96`; `CompRevenant.CheckIfHeard` sólo colonos).
No pisa Omens "Rain Reveals the Unseen" (esa sí dibuja el contorno con lluvia). El animal no ataca ni huye.

**Casos borde**: prisioneros/visitantes no son animales de la colonia. Animales de otras facciones o salvajes: no.
Mecanoides: no son animales. Mapas pocket (laberinto, undercave) y mapas temporales: el MapComponent vive en cada mapa.
Save/load: cooldowns y entidad del job se guardan. Sin Anomaly el mod no carga (dependencia), pero el código no usa
defs de Anomaly.

## Hooks (verificados en decomp 1.6.4871)

- `Verse/HediffComp_Invisibility.cs`: `public bool PsychologicallyVisible` (get). `Props.visibleToPlayer`.
- `RimWorld/InvisibilityUtility.cs:8,22` (referencia; no se usa por Melee Animation).
- `Verse/MapPawns.cs:334` `IReadOnlyList<Pawn> AllPawnsSpawned`; `SpawnedPawnsInFaction(Faction)`.
- `Verse/Pawn_RotationTracker.cs:105` `Face(Vector3)`.
- `RimWorld/MoteMaker.cs:92` `MakeAttachedOverlay(Thing, ThingDef, Vector3, float, float)`.
- `Verse/Pawn_DrawTracker.cs:12` `private JitterHandler jitterer` -> `AccessTools.FieldRefAccess`; `JitterHandler.AddOffset(float dist, float dir)` (:48).
- `Verse/LifeStageAge.cs` `soundAngry`, `soundCall`; `Verse.Sound/SoundInfo.cs` `pitchFactor`, `volumeFactor`.
- `Verse/GenSight.cs:20` `LineOfSight(IntVec3, IntVec3, Map, bool skipFirstCell, ...)`.
- `Verse.AI/Pawn_JobTracker.cs:252` `StartJob(Job, JobCondition, ...)`.

## Plan de prueba (arnés `nahuel.thedogknowstest`)

Cada escenario: spawnea animal(es) y entidad, `comp.ScanNow()`, afirma, limpia (destroy), resetea cooldowns.
1. Revenant a 6 celdas con LOS de un Husky => Husky en `TDK_StareAtUnseen`; el revenant sigue invisible y oculto al
   jugador (no revelado); el report contiene "something you can't see"; se creó el mote; sonó el gruñido (contador);
   mensaje enviado (contador).
2. Durante la mirada, la rotación del Husky apunta a la entidad (`Rot4.FromAngleFlat`).
3. Cooldown: terminado el job, otro scan no reinicia; con el cooldown vencido (forzado) sí.
4. La entidad se vuelve visible (`BecomeVisible(true)`) => el job termina en <= 60 ticks.
5. Sightstealer => un Cat reacciona.
6. Raider humano con `HoraxianInvisibility` agregado => reacciona (cualquier hediff de invisibilidad).
7. Negativos: entidad fuera de radio; pared en medio (LOS); raider visible normal; colono propio invisible; animal
   no listado (Muffalo); Husky salvaje; Husky dormido; Husky con job no interrumpible (playerForced).
8. Sin entidades invisibles: el scan no evalúa animales (contador `AnimalChecks` sin cambios).
9. Lista extensible: la sense def resuelve Husky/Cat y no Muffalo; radio de Cat = 9.

Compat probada con: Vanilla Expanded Framework (`OskarPotocki.VanillaFactionsExpanded.Core`), Alpha Animals
(`sarg.alphaanimals`: sus 3 razas resuelven por MayRequire) y Omens (`nahuel.omens`) cargados juntos: 52 PASS, 0 errores
(`logs/run-20261016-234233.log`). Sin ellos: 51 PASS, 0 errores (`logs/run-20261016-233834.log`).
No probado: save/load a mitad de una mirada; Melee Animation y Performance Optimizer (se esquivan por diseño: no se
llama `IsPsychologicallyInvisible`); Preview.png (sin showcase).
