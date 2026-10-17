# Machine Vigil (`nahuel.machinevigil`) - diseño

Idea `mech-vigil` ("La vigilia de las máquinas"), forja 2026-10-12 §4. Mod standalone.
Requiere Biotech (mecanitores). Harmony.

## Pitch

Cuando muere un mecanitor, sus mechs, ahora sin control, no deambulan por el mapa:
caminan hasta su cuerpo (o su tumba) y se quedan en anillo, quietos, mirándolo.
Los colonos que pasan cerca lo sienten. Cuando otro mecanitor toma un mech, ese
mech "despierta" con un destello y sale del anillo.

## Qué ve el jugador

1. Muere el mecanitor. Mensaje neutral: "<Name>'s mechanoids gather around <his> body." (uno por vigilia).
2. Los mechs sin control caminan (paso normal, no corren) hasta un anillo de 2-3.5
   celdas alrededor del cuerpo y se quedan quietos mirando hacia él. Inspect:
   "keeping vigil for <Name>." / "walking to <Name>'s vigil."
3. Si se lleva el cuerpo (cargado, a una tumba, a un sarcófago), el anillo lo sigue.
   Si el cuerpo desaparece (cremado), se quedan donde lo vieron por última vez.
4. Colonos despiertos a <= 6 celdas del cuerpo con línea de vista, mientras al menos
   un mech está en el anillo: memoria "<Name>'s machine vigil" (-2, 1 día), una vez por vigilia.
5. Al conectar un mech a un mecanitor nuevo: destello (FlashHollow) y vuelve a la normalidad.

## Reglas exactas

- **Quién califica:** par de la relación Overseer removida por muerte
  (`PawnRelationWorker_Overseer.Notify_PostRemovedByDeath`) donde el muerto es el no-mecanoide
  (`!RaceProps.IsMechanoid`, `Dead || health.isBeingKilled`: `Pawn.Kill` llama a esto ANTES de `SetDead`) y el otro es un mech vivo de la facción del jugador.
  Destruido sin morir (Notify_PawnDestroyed, isBeingKilled false) -> no hay vigilia.
- **Una vigilia por mecanitor** muerto; agrupa a todos sus mechs. Un mech está en una sola vigilia
  (si ya estaba en otra, se mueve a la nueva).
- **Centro:** `mechanitor.Corpse.PositionHeld/MapHeld` si el cadáver existe y está en un mapa;
  se actualiza cada 250 ticks como "último lugar conocido". Sin cadáver: último lugar conocido
  (al morir: `PositionHeld/MapHeld` del mecanitor, todavía spawneado en `RemoveRelationsOnDeath`).
  Cadáver fuera de mapa (caravana) -> sin centro -> los mechs deambulan como en vanilla.
- **Comportamiento:** nodo `MachineVigil.JobGiver_KeepVigil` insertado (PatchOperationInsert) en el
  ThinkTree `Mechanoid` justo antes del tagger `Idle` de nivel superior: después de lord, dormancia,
  energía baja y `JobGiver_AIFightEnemies` de mechs no controlados (siguen defendiéndose: el Wait
  hace auto-ataque cuerpo a cuerpo y expira cada 600 ticks para re-pensar; el daño fuerza re-think).
  Sólo actúa si `pawn.IsColonyMechRequiringMechanitor()` y el mech está registrado.
  - Si ya está en el anillo (2 <= dist <= 3.5) en su mapa y la celda está libre: job
    `MachineVigil_Keep` (JobDriver_KeepVigil : JobDriver_Wait), `overrideFacing` hacia el centro, expiry 250 (re-piensa: si hay enemigos a <= 30, pelea).
  - Si no: celda del anillo (`GenRadial.RadialCellsAround(c, 2, 3.5)`, `StandableBy`,
    `pawnDestinationReservationManager.CanReserve`, `CanReach OnCell Deadly`) eligiendo la más cercana
    al ángulo "slot" del mech (360*i/n según su índice en la vigilia) -> anillo parejo.
    Preferencia por celdas con línea de vista al centro (no del otro lado de una pared); `CanReach` con `Danger.Some`.
    Job `MachineVigil_Approach` (JobDriver_Goto, Walk, expiry 300 con checkOverrideOnExpire).
  - Si el cuerpo se mueve, el chequeo de 250 ticks corta el Keep de los mechs fuera del anillo nuevo.
  - Sin celda alcanzable -> null (deambula vanilla).
- **Despierto, no apagado:** el reloj feral vanilla sigue corriendo (1 día + MTB 10 días). Si se vuelve
  feral (facción != jugador) sale de la vigilia. Lo dejamos: es vanilla y es drama.
- **Fin por mech:** muerto/destruido, facción != jugador, recibe overseer (Postfix a
  `PawnRelationWorker_Overseer.OnRelationCreated`, que corre ANTES de agregar la relación: no usar
  `GetOverseer()` ahí) o en el chequeo de 250 ticks `GetOverseer() != null`.
- **Fin de vigilia:** sin mechs, 3 días (180000 ticks) desde la muerte, o el mecanitor deja de estar
  muerto (resurrección).
- **Despertar:** si el mech está spawneado: `FleckMaker.Static(DrawPos, map, FleckDefOf.FlashHollow, 1.5f)` y,
  si su job es de la vigilia, `EndCurrentJob(InterruptForced, startNewJob: false)` (re-pensar en el acto elegiría el wander de mech sin control, porque la relación todavía no está guardada).
- **Memoria:** `MachineVigil_WitnessedVigil` (Thought_Memory, -2, 1 día, stackLimit 1), `otherPawn` = mecanitor
  muerto. Testigos: `map.mapPawns.FreeColonistsSpawned` en el mapa del centro, despiertos, no caídos,
  `needs.mood != null`, distancia <= 6, `GenSight.LineOfSight(pos, centro)`. Una vez por colono por vigilia
  (ids guardados). Requiere >= 1 mech con `CurJobDef == MachineVigil_Keep` de esa vigilia.
- **Casos borde:** animales/prisioneros/esclavos no son mechs ni testigos (FreeColonists); mechs en caravana
  no spawneados se ignoran hasta que vuelvan a un mapa; mechs en otro mapa que el centro deambulan;
  mechs de mods con ThinkTree propio no hacen vigilia (sólo el árbol `Mechanoid`); hostiles: el giver
  sale en la primera línea (`IsColonyMech`). Mecanitor no humano (razas de mods): cualquier no-mecanoide sirve.
- **Guardado:** `GameComponent_MachineVigil` con `List<Vigil>`; `Vigil`: mechanitor (Reference,
  saveDestroyedThings - los colonos muertos nunca los descarta el WorldPawnGC: "Colonist"), mapa (Reference),
  último centro, startTick, mechs (Reference, podados antes de guardar), ids de testigos, announced.

## Hooks (verificados en decomp/1.6.4871)

- `RimWorld/PawnRelationWorker_Overseer.cs:33` `public override void Notify_PostRemovedByDeath(Pawn firstPawn, Pawn secondPawn)`
  (llamado desde `Pawn_RelationsTracker.RemoveRelationsOnDeath` :1031, dentro de `Pawn.Kill` antes del DeSpawn).
- `RimWorld/PawnRelationWorker_Overseer.cs:7` `public override void OnRelationCreated(Pawn firstPawn, Pawn secondPawn)`
  (`Pawn_RelationsTracker.AddDirectRelation` :501, antes de `directRelations.Add`).
- `MechanitorUtility.IsColonyMechRequiringMechanitor` (:133), `GetOverseer` (:91), `Pawn.IsColonyMech` (:794).
- `Verse.AI/JobDriver_Wait.cs` `overrideFacing` (:55-62), `Job.overrideFacing` (`Job.cs:147`), expiry (`Pawn_JobTracker.cs:166-186`).
- ThinkTree `Core/Defs/ThinkTreeDefs/Mechanoid.xml` (tagger Idle de nivel superior), patch envuelto en PatchOperationConditional.
- `Rot4.FromAngleFlat` (`Rot4.cs:232`), `IntVec3.AngleFlat` (0 = norte).
- `FleckDefOf.FlashHollow` (`FleckDefOf.cs:115`).

## Overlap con mods instalados (research)

Ningún mod instalado hace esto (VFE Mechanoids y VRE Android no están). Tocan sistemas cercanos:
Search and Destroy (`memegoddess.searchanddestroy`, nodo en el árbol Mechanoid dentro del tagger de órdenes
drafted, inerte salvo toggle), Performance Optimizer (`taranchuk.performanceoptimizer`, cachea `CanGoFeral`),
VEF (`oskarpotocki.vanillafactionsexpanded.core`, transpiler en `JobDriver_Wait.CheckForAutoAttack`).

## Plan de prueba (arnés MachineVigilTest)

1. Colono + `MechlinkImplant` -> `mechanitor != null`.
2. 3 `Mech_Pikeman` del jugador + `AddDirectRelation(Overseer)` -> controlados.
3. `mechanitor.Kill(null)` -> 1 vigilia con 3 mechs, todos `IsColonyMechRequiringMechanitor`.
4. ~1500 ticks -> cada mech a <= 3.5 del centro (cadáver), job `MachineVigil_Keep`, mirando al centro.
5. Colono a 4 celdas con LOS -> memoria con otherPawn = mecanitor muerto.
6. Segundo mecanitor toma mech[0] -> sale de la vigilia, deja el job; los otros 2 siguen.
7. Negativo: colono lejos (> 6 celdas) no recibe memoria; muerte de un no-mecanitor no crea vigilia;
   un mech hostil no hace vigilia.
8. Mover el cadáver -> los mechs siguen el nuevo centro.

## Resultados

(ver PROGRESS.md)

## Pruebas (2026-10-16)

- `rw build` 0 warnings, `rw validate` 0 errores / 0 warnings (mod y arnés).
- gametest `--ticks 6500`: 23 PASS, 0 errores - `logs/run-20261016-222830.log`.
- Compat probada con: Vanilla Expanded Framework (`oskarpotocki.vanillafactionsexpanded.core`, transpiler en
  `JobDriver_Wait.CheckForAutoAttack`), Performance Optimizer (`taranchuk.performanceoptimizer`, cache de `CanGoFeral`),
  Search and Destroy (`memegoddess.searchanddestroy`, nodo en el árbol Mechanoid): 23 PASS, 0 errores -
  `logs/run-20261016-222937.log`.
- No probado: guardar/cargar con una vigilia activa, fin por 3 días, mech que se vuelve feral en la vigilia,
  cuerpo enterrado en tumba/sarcófago (sí se probó cuerpo movido), caravanas, resurrección del mecanitor,
  pelea de mechs en vigilia contra un raid. Sin Preview.png (no hay showcase).
