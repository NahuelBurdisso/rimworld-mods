# Over the Body (`nahuel.overthebody`) - diseño

Idea `over-the-body` ("Sobre el cuerpo"), forja 2026-10-12 §6 (`docs/forge/research/forja-2026-10-12.md`).
Era feature del pack Gesta; ahora mod standalone (loop 90, regla 4). Sin DLC. Sin Harmony.

## Pitch

Cuando un colono cae en combate, su animal vinculado no lo abandona: corre a su
lado, se planta sobre el cuerpo mirando al enemigo y muerde a cualquiera que se
acerque. Si un compañero lo carga, lo sigue hasta la cama. Si un raider se lo
lleva, el animal va por él. Después el colono lo recuerda; si el animal muere
guardándolo, el duelo es más hondo y queda en el arte de la colonia.

## Qué ve el jugador

1. Un colono cae (downed) con enemigos cerca. Mensaje: "Rex rushes to stand over the fallen Ana."
   El animal vinculado corre (Sprint) a una celda adyacente, del lado de la amenaza más cercana.
2. Se queda quieto al lado del cuerpo mirando a la amenaza más cercana. Inspect: "standing over Ana."
3. Un hostil a <= 2.5 celdas del caído -> el animal lo ataca cuerpo a cuerpo. No lo persigue:
   si el hostil se aleja a más de 5 celdas del caído, suelta y vuelve. Un hostil a 5 celdas no lo atrae.
4. Si un compañero carga al caído: el animal lo sigue de cerca ("following Ana.") hasta que lo dejan.
   Si lo carga un hostil (secuestro): el animal persigue y ataca al secuestrador, sin correa.
5. Cuando el colono se levanta (ya no downed) y el animal guardó de verdad (>= 250 ticks en guardia o
   mordiendo): memoria "Rex stood over me" (+6, 20 días), tale "Rex standing over Ana" y mensaje positivo.
6. Si el animal muere guardando: memoria "Rex died standing over me" (-8, 30 días; suma al vanilla
   "bonded animal died" -8, 20 días), tale y mensaje negativo.

## Reglas exactas

- **Quién guarda:** animal (`IsAnimal`) de la facción del jugador, spawneado, con relación directa `Bond`
  hacia un colono (`IsColonist`, incluye esclavos seguros; nunca prisioneros) que está `Downed`, vivo,
  y en el mismo mapa (spawneado, o cargado por un pawn spawneado en ese mapa). Sin entrenamiento requerido.
- **Qué master:** si el `playerSettings.Master` del animal es uno de esos caídos, ése; si no, el más cercano.
- **Peligro:** `GenAI.EnemyIsNear(caído o su cargador, 25)` (excluye amenazas en niebla y deshabilitadas), o
  el caído es cargado por un hostil. Sin peligro y sin guardia activa -> el nodo devuelve null (vanilla:
  Rescue, wander, etc.). Con guardia activa y el caído cargado por un amigo: sigue igual (para llegar a la cama).
- **Nodo de IA:** `ThinkTreeDef OverTheBody_Guard`, `insertTag Animal_PreMain`, `insertPriority 10`
  (corre antes del resto de inserciones en ese tag; devuelve null salvo en el caso exacto).
  Va después de: acostarse/downed, mental states, amenaza melee cercana, cola de jobs, goto forzado,
  soga (RopedPawn) y deberes de lord. Prioridad del árbol en `JobGiver_StandOverMaster.TryGiveJob`:
  1. Caído cargado por hostil -> `OverTheBody_Bite` al cargador (sin correa), expiry 200.
  2. Caído cargado por amigo (y guardia activa) -> `OverTheBody_Follow` (driver propio JobDriver_StayWithCarried: se mantiene a <= 2.5 del cargador y NO termina por estar cerca, a diferencia del FollowClose vanilla que terminaba al instante y dejaba al animal sin seguir; termina cuando sueltan al caído; report "staying close to <caído>") al cargador, expiry 140, checkOverrideOnExpire. Sin registro previo -> null (no lo sigue).
  3. Hostil (pawn, no downed, no ThreatDisabled para el animal, alcanzable) a <= 2.5 del caído y el animal puede
     atacar melee (`kindDef.canMeleeAttack`) -> `OverTheBody_Bite` (JobDriver_AttackMelee + correa 5 celdas
     alrededor del caído), maxNumMeleeAttacks 3, expiry 200.
  4. Animal no adyacente (8 vecinos) al caído -> `OverTheBody_Rush` (JobDriver_Goto) a la celda adyacente libre
     (StandableBy, reservable, alcanzable Deadly), la más cercana a la amenaza; sin amenaza: la más cercana al
     animal. Sprint, expiry 120 con checkOverrideOnExpire (re-apunta si el caído se arrastra).
  4b. Rush: `IsContinuation` sólo si es la misma celda (al expirar re-apunta si el caído se arrastró o lo movieron);
     termina si el caído ya no está downed.
  5. Adyacente -> `OverTheBody_Guard` (JobDriver_Wait: auto-ataque vanilla a hostiles adyacentes;
     `neverFleeFromEnemies`), mira cada 30 ticks a la amenaza más cercana; termina solo si: el caído ya no está
     downed o no está adyacente, o aparece un hostil a <= 2.5 (re-piensa -> Bite). Expiry 250 con checkOverrideOnExpire.
- **Reacción rápida:** `GameComponent_OverTheBody` cada 60 ticks recorre `SpawnedColonyAnimals` de cada mapa; si un
  animal califica (master caído + peligro, no drafted, sin job forzado por el jugador, sin lord, sin soga, sin mental state),
  su job no es nuestro y `JobGiver_StandOverMaster.TryMakeJob(a, notify: false)` (misma decisión, sin efectos) daría un job:
  si duerme `RestUtility.WakeUp`, si no `jobs.CheckForJobOverride()`. Así nunca se lo despierta o interrumpe en vano. Nodos más altos del árbol siguen mandando (mental state, lord, soga).
- **Registro (`GuardRecord`)**: se crea la primera vez que el nodo da un job nuestro para un par (animal, caído),
  con el mensaje "rushes to stand over". Guarda startTick, lastGuardTick, ticksGuarded. Los drivers Guard y Bite
  actualizan `lastGuardTick` y suman ticks (vía el componente, cada 60 ticks si el job actual es Guard/Bite).
  Un animal tiene a lo sumo un registro (si cambia de caído, se reemplaza). `lastGuardTick` lo pone el chequeo de 60 ticks
  cuando el job actual es Guard/Bite; si el animal cae (downed) dentro de la ventana de 120 ticks queda `fellGuarding`.
- **Fin del registro** (chequeo cada 60 ticks):
  - Animal muerto: si `now - lastGuardTick <= 120` -> murió guardando: memoria `OverTheBody_DiedOverMe` al caído
    (si vive y tiene mood), tale `OverTheBody_DiedOverTale`, mensaje. Si no, nada.
  - Caído muerto o destruido: nada (el vanilla ya da su duelo).
  - Caído ya no downed: si `ticksGuarded >= 250` -> memoria `OverTheBody_StoodOverMe` (otherPawn = animal),
    tale `OverTheBody_StoodOverTale`, mensaje positivo. Si no, nada.
  - Animal ya no del jugador o sin vínculo con el caído, registro de más de 5 días, o el caído salió de todo mapa sin estar
    en caravana (secuestrado fuera del mapa): se descarta sin nada.
  - Si el caído también murió, no hay memoria ni tale de "died over me" (el vanilla ya cubre ambas muertes).
- **Stacking:** memorias con `stackLimitForSameOtherPawn 1` (refresca), `stackLimit 3`.
- **Casos borde:** animales en caravana (no spawneados) no hacen nada; mapas temporales sí (cualquier mapa);
  animales con soga o en lord no (nodos previos); mecanoides y humanoides no son animales; razas de mods: cualquier
  `IsColonist` sirve de master y cualquier `IsAnimal` de guardián; animales que no pueden pelear
  (`canMeleeAttack false`) guardan pero no muerden; el caído en una cama: guarda al lado de la cama.
  Psicópatas: reciben "stood over me"; "died over me" la anulan (como el vanilla BondedAnimalDied). Sin mood (`needs.mood == null`): sin memoria, sí tale.
- **Guardado:** `GameComponent_OverTheBody` con `List<GuardRecord>` (Deep); refs `saveDestroyedThings: true`.

## Hooks (verificados en decomp/1.6.4871)

- `Core/Defs/ThinkTreeDefs/Animal.xml:97-99` `ThinkNode_SubtreesByTag insertTag Animal_PreMain`;
  `Verse.AI/ThinkNode_SubtreesByTag.cs` ordena por `insertPriority` descendente.
- `Verse.AI/ThinkNode_JobGiver.cs:21` `protected abstract Job TryGiveJob(Pawn pawn)`.
- `Verse.AI/Pawn_JobTracker.cs:571` `public void CheckForJobOverride(float minPriority = 0f, bool ignoreQueue = true)`;
  expiry + `checkOverrideOnExpire` :166-186; `ShouldStartJobFromThinkTree` :833 (no reinicia el mismo job del mismo nodo).
- `RimWorld/RestUtility.cs:549` `WakeUp(Pawn p, bool startNewJob = true)`.
- `Verse.AI/GenAI.cs:165` `EnemyIsNear(Pawn p, float radius, out Thing threat, bool meleeOnly = false, bool requireLos = false)`
  (vía `map.attackTargetsCache.GetPotentialTargetsFor`).
- `Verse.AI/JobDriver_Wait.cs` (CheckForAutoAttack a hostiles adyacentes; `DecorateWaitToil` :75).
- `Verse.AI/JobDriver_AttackMelee.cs:7` (no sellado), `Verse.AI/JobDriver_Goto.cs:9` (reserva destino).
- `Verse.AI/JobDriver_FollowClose.cs:116` `FarEnoughAndPossibleToStartJob`; patrón `RimWorld/JobGiver_AIFollowPawn.cs`.
- `Verse/MapPawns.cs:478` `SpawnedColonyAnimals`; `Verse/Pawn.cs:882` `CarriedBy`; `Verse/Pawn.cs:518` `IsColonist`.
- `RimWorld/TaleRecorder.cs:8` `RecordTale(TaleDef, params object[])`, `Tale_DoublePawn`.
- Vanilla que esto cambia: `ThinkNode_ConditionalShouldFollowMaster` es false con el master downed, así que
  `JobGiver_AIDefendMaster` no corre (`Animal.xml:168-181`); Rescue no actúa con enemigos cerca.

## Plan de prueba (arnés `nahuel.overthebodytest`, `OverTheBodyHarness`)

Quicktest, área abierta. Colono A (master) + Husky vinculado (Bond + Master) a 8 celdas; colono B lejos (drafted);
un segundo animal NO vinculado (Husky) cerca (negativo).
1. Sin enemigos: bajar A -> 300 ticks: el husky NO toma jobs nuestros (negativo: sin peligro no hay guardia). Curar A.
2. Spawn 1 hostil a ~12 celdas, quieto (job Wait_Combat largo, para que no ataque). Bajar A.
   -> husky con Rush/Guard en <= 600 ticks; termina adyacente con `OverTheBody_Guard`; registro creado; mensaje.
   -> el animal no vinculado nunca toma nuestros jobs.
   -> el reporte de inspección nombra a A; mira hacia la amenaza.
3. Teletransportar el hostil a 5 celdas de A -> 300 ticks: el husky sigue en Guard (no lo atrae).
4. Teletransportar el hostil a 2 celdas de A -> el husky toma `OverTheBody_Bite` sobre ese hostil (<= 300 ticks).
5. Matar al hostil cercano; un hostil queda a 18 celdas; B rescata a A (job Rescue ordenado, a una cama a 6 celdas) ->
   el husky toma `OverTheBody_Follow` a B; con A en la cama y el enemigo cerca -> Guard al lado de la cama.
5b. Negativo: B carga a A (caído) sin que el husky lo estuviera guardando, enemigo cerca -> `TryMakeJob` da null y no hay registro.
6. Matar hostiles, curar A (levantar) -> memoria `OverTheBody_StoodOverMe` con otherPawn = husky, tale registrado,
   registro borrado.
7. Escenario 2: otro husky vinculado a B, bajar B con hostil cerca, esperar Guard, matar al husky -> memoria
   `OverTheBody_DiedOverMe` en B, tale `OverTheBody_DiedOverTale`, vanilla `BondedAnimalDied` también.
8. Escenario 3 (secuestro): hostil carga a C (caído) -> el animal de C toma `OverTheBody_Bite` sobre el secuestrador.

## Resultado

- `rw build` 0 warnings, `rw validate` 0 errores / 0 warnings (mod y arnés).
- gametest `--ticks 9000`: 34 PASS, 0 errores - `logs/run-20261016-230053.log` (mismo resultado en las dos corridas de compat).
- Overlap (agente rw-researcher sobre los 238 mods instalados): ningún mod hace guardia de un animal vinculado sobre su
  master caído. Allies are Helpful (3534920369) sólo humanlikes; Animals are fun (3245454244) sólo fetch/paseos;
  Alpha Animals y VEF sólo copian el `JobGiver_AIDefendMaster` vanilla en sus árboles. Ninguno parchea con Harmony
  `JobGiver_AIDefendMaster`, `ThinkNode_ConditionalShouldFollowMaster`, `JobGiver_RescueNearby` ni `JobDriver_FollowClose`.
- **Compat probada con:**
  - Vanilla Expanded Framework (`oskarpotocki.vanillafactionsexpanded.core`: transpiler en `JobDriver_Wait.CheckForAutoAttack`,
    `VEF_DraftableHook` en Animal_PreMain con prioridad 100, árboles de animales propios), Alpha Animals (`sarg.alphaanimals`:
    copias del árbol Animal con los hooks, `AA_LayExplodingEggs` en Animal_PreMain), Search and Destroy
    (`memegoddess.searchanddestroy`: nodo en el árbol Animal antes de PreMain, parche a `DetermineNextJob`):
    34 PASS, 0 errores - `logs/run-20261016-230136.log`.
  - Common Sense (`avilmask.commonsense`: parches al job tracker) y Pack Howls (`nahuel.packhowls`: perros que aúllan
    por el caído): 34 PASS, 0 errores - `logs/run-20261016-231828.log`.
  - Achtung! (`brrainz.achtung`): la corrida con Achtung + Common Sense + Pack Howls quedó parada en MAP READY sin ticks y sin
    errores en el log (`logs/run-20261016-230228.log`); no concluyente (probablemente un diálogo de Achtung pausa el
    quicktest). Achtung sólo toca jobs de colonos.
- **No probado:** guardar/cargar con una guardia activa; un caído que se arrastra (1.6 `CanCrawl`, el arnés usa anestesia);
  animales en caravana o mapas temporales; animal que cae (downed) guardando y muere después (`fellGuarding`);
  secuestro completo hasta salir del mapa (sí se probó que el animal ataca al secuestrador); varios animales vinculados
  al mismo caído; animales de mods con ThinkTree propio sin el hook Animal_PreMain. Sin Preview.png (no hay showcase).
