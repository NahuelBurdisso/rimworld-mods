# Reactor Slag (`nahuel.reactorslag`)

Idea `reactor-slag` ("Escoria del reactor", forja 2026-10-12 §5, puntaje 54). Antes iba al pack Aftermath;
ahora es un mod propio. Sin Harmony, sin dependencias. Odyssey opcional.

## Pitch

Cuando cae un mecanoide pesado o un jefe, su núcleo se derrama: un charco de escoria al rojo que alumbra el campo
de batalla de noche, humea, y se enfría durante 3 días de naranja a rojo a negro. Cuando se enfría queda una
cicatriz de escoria (suciedad que se puede limpiar; en el campo queda para siempre) y, con Odyssey, el suelo natural
del centro se vuelve lava enfriada.

## Qué ve el jugador

- Al morir el mech: debajo del cadáver aparece un disco chamuscado con ceniza oscura (3x3, 5x5 para jefes) y un
  halo naranja que respira alrededor del casco, humo y alguna chispa. Alumbra como una lámpara (radio 6, jefes 9).
- Con las horas el halo y la luz se achican y viran a rojo y a rojo oscuro.
- A los 3 días el núcleo se apaga: queda "reactor slag scar" (filth) en las celdas del charco y, con Odyssey, lava
  enfriada en el centro (1 celda; jefes: cruz de 5).

## Reglas exactas

- **Quién califica:** `pawn.RaceProps.IsMechanoid` y (`pawn.kindDef.isBoss` o `pawn.BodySize >= 2.5`).
  Vanilla: Centipede (todas, 3.0), Tunneler 3.5, Centurion 3.6 -> chico; War Queen, Diabolus, Apocriton
  (`isBoss`) -> grande. Lancer, Scyther, Pikeman, Termite (1.6), mechs ligeros: nada. Mechs de mods: por tamaño.
- **Cualquier facción** (hostil, del jugador, sin facción): es el reactor, no la lealtad.
- **Desarme** en el mech bay (`forceNoDeathNotification`, `JobDriver_DisassembleMech.cs:38-41`): nada.
- **Dónde:** solo si el cadáver quedó spawneado en el mapa donde murió (`Notify_Killed(prevMap)`, `pawn.Corpse.Spawned`
  y `Corpse.Map == prevMap`). Muerte en caravana, en contenedor, sin cadáver, o `prevMap == null` -> nada.
  El núcleo se pone en la celda del cadáver; mover/enterrar el cadáver después no lo mueve.
- **Núcleo** (`ReactorSlag_Core` / `ReactorSlag_CoreLarge`, `ThingWithComps` Ethereal, no seleccionable, no lo dañan
  explosiones, no bloquea nada): `spawnTick` guardado. Calor `h = 1 - edad / (coolingDays * 60000)`, `coolingDays`
  setting (default 3, rango 0.5-10).
- **Luz:** `CompGlower` vanilla. Radio `glowRadius * lerp(0.3, 1, h)`; color rampa (255,150,60) -> (220,70,25) ->
  (110,25,10). Se recalcula cada TickRare (250 ticks) pero se re-registra solo cuando cambia el escalón (24 escalones
  en toda la vida: ~1 re-registro cada 3 horas de juego). Al cargar partida el radio se reaplica antes de
  `base.SpawnSetup` (el comp vanilla no guarda el radio).
- **Halo:** realtime, textura radial generada en código, `ShaderDatabase.MoteGlow`, en `AltitudeLayer.MoteLow`
  (debajo de ítems, cadáveres y pawns: sobre `LightingOverlay` lavaba el casco a blanco, visto en el showcase),
  tamaño `haloSize` (6 / 8.5) `* lerp(0.6, 1, h)`, intensidad `0.95 h^0.7` cuantizada en 32 materiales cacheados,
  pulso suave +-5%. Setting `drawHalo` (default on).
- **Humo/chispas:** en TickRare, si `h > 0.35` con prob. `0.5h` un `FleckMaker.ThrowSmoke` en una celda al azar del
  charco; si `h > 0.7` con prob. 0.25 `ThrowMicroSparks`. Solo si la celda no tiene niebla y `ShouldSpawnMotesAt`.
- **Charco impreso:** en el map mesh (`Print`): un disco chamuscado generado en código (alpha 0.6 -> 0, borde
  irregular, diámetro `2*poolRadius+1`) y 12 / 26 manchas de `Things/Filth/Ash` teñidas oscuras, sembradas por
  `thingIDNumber` (no cambian entre recargas). `RubbleSlag` se probó y se leía como rayitas negras (showcase).
- **Enfriado (h <= 0):** `FilthMaker.TryMakeFilth(c, map, ReactorSlag_Scar, 1, shouldPropagate: false)` en cada
  celda del disco del charco (radio 1.5 chico / 2.5 grande: 9 / 21 celdas). La filth respeta las reglas vanilla
  (no en agua, no en paredes, no en vacío). `ReactorSlag_Scar` (textura `Things/Filth/Ash` oscura, tamaño 1.7): no
  desaparece sola (`disappearsInDays` 0), la lluvia no la lava, se limpia (trabajo 120), Beauty -8, Cleanliness -4. Con Odyssey y setting `groundScar` (default
  on): `CooledLava` en el centro (chico) o centro + 4 adyacentes cardinales (grande) solo si la celda es terreno
  natural (`terrain.natural`), no agua/hielo/inundación, sin piso construido encima (`UnderTerrainAt == null`), sin
  `FoundationAt`, sin edificio, fuera de zonas de cultivo, sin árboles ni plantas cosechables (CooledLava tiene
  fertilidad 0 y `SetTerrain` las destruiría), y no ya `CooledLava`. CooledLava es `layerable`: el terreno original
  queda debajo y se recupera con "quitar piso". Después el núcleo se destruye
  (`Vanish`).
- **Stacking:** cada muerte es independiente; varios charcos pueden superponerse (la filth se engrosa).
- **Casos borde:** animales, humanos, mutantes de Anomaly: nunca (no son `IsMechanoid`). Mechs fabricados por el
  jugador cuentan igual. Gravship en vuelo: el núcleo es una Thing normal del mapa. Mapa destruido: se va con él.
- **Inyección:** `[StaticConstructorOnStartup]` agrega `CompProperties_ReactorSlag` a todo `ThingDef` con
  `race.IsMechanoid` (vanilla y de mods), sin duplicar. Sin datos guardados en el comp: seguro de agregar o quitar a
  mitad de partida (al quitarlo, los núcleos existentes se pierden con el aviso vanilla de def faltante).

## Hooks (verificados en decomp/1.6.4871)

- `public virtual void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)` (`Verse/ThingComp.cs:202`), llamado por
  `ThingWithComps.Kill` (`Verse/ThingWithComps.cs:308-331`) desde el final de `Pawn.Kill` (`Verse/Pawn.cs:2096`,
  `if (!Destroyed) base.Kill(...)` ~:2259), después de crear y colocar el cadáver.
- `RaceProperties.IsMechanoid` (`Verse/RaceProperties.cs:337`), `PawnKindDef.isBoss` (`Verse/PawnKindDef.cs:67`),
  `Pawn.BodySize` (`Verse/Pawn.cs:1249`).
- `CompGlower.GlowRadius` setter solo guarda; `GlowColor` setter (`SetGlowColorInternal`, `Verse/CompGlower.cs:357`)
  des-registra y re-registra si está prendido; `GlowGrid.RegisterGlower` toma una foto de radio+color
  (`Verse/GlowGrid.cs:283`). `PostExposeData` guarda el color, no el radio (:173-177).
- `SectionLayer_Things.Regenerate` (`Verse/SectionLayer_Things.cs:55`) no filtra por categoría: una Ethereal con
  `MapMeshAndRealTime` se imprime (riesgo de la ficha resuelto). Ethereal ignorada por explosiones
  (`Verse/DamageWorker.cs:255,302`) y por el inspector de celda.
- `GenSpawn.SpawningWipes` (`Verse/GenSpawn.cs`): un Ethereal Standable no borra el cadáver ni ítems.
- `FilthMaker.TryMakeFilth(IntVec3, Map, ThingDef, int, FilthSourceFlags, bool shouldPropagate)`
  (`RimWorld/FilthMaker.cs:55`); filth con `disappearAfterTicks == 0` no desaparece
  (`RimWorld/SteadyEnvironmentEffects.cs:128`).
- `TerrainDefOf.CooledLava` `[MayRequireOdyssey]` (`RimWorld/TerrainDefOf.cs:90`), `TerrainGrid.SetTerrain`
  (`Verse/TerrainGrid.cs:193`), `UnderTerrainAt`/`FoundationAt` (:131,141), `TerrainDef.natural` (:153).
- `FleckMaker.ThrowSmoke(Vector3, Map, float)` (:189), `ThrowMicroSparks(Vector3, Map)` (:235),
  `GenView.ShouldSpawnMotesAt(IntVec3, Map, bool)` (:16).

## Overlap con mods instalados

Ninguno de los 238 deja escoria/charco al morir un mech (`rw find slag/scorch/molten --partial --all`, grep de
`OnDeath|deathAction` en Defs de Workshop: solo explosiones de drones de VFE Pirates). Tocan muerte/cadáveres/filth:
Keep Burning, Corpse! (transpiler + prefix/postfix en `Pawn.Kill`), VEF (`VanillaExpandedFramework_Pawn_Kill_Patch`),
Death Acidifiest (hediff comp al morir), Blood and Stains (sangrado/filth), Better Explosions (DamageDefs).

## Plan de prueba (arnés `nahuel.reactorslagtest`, `--ticks 2500`)

1. Centipede Blaster hostil muerto con Bomb -> exactamente 1 `ReactorSlag_Core` en la celda del cadáver, el glower
   prendido con radio = 6 (calor 1).
2. Apocriton -> 1 `ReactorSlag_CoreLarge`.
3. Lancer hostil y un pirata humano muertos -> ningún núcleo (negativos).
4. Centurion del jugador muerto -> núcleo chico (cualquier facción).
5. Envejecer 1 día (`spawnTick -= 60000`, `TickRare`) -> radio menor y color más rojo (g/r bajó).
6. "Carga": borrar por reflexión el override de radio del CompGlower, DeSpawn + Spawn con
   `respawningAfterLoad: true` -> radio reaplicado al valor del calor.
7. Enfriar (`spawnTick` = hace 3 días + 1) -> núcleo destruido, glower des-registrado, `ReactorSlag_Scar` en >= 5
   celdas del charco, `CooledLava` en el centro (Odyssey activo en el perfil).
8. Negativo de terreno: centipede sobre piso de madera construido -> al enfriar, el piso sigue (`WoodPlankFloor`).
9. Grande enfriado -> CooledLava en el centro y en las 4 cardinales naturales.
10. Cámara saltando al charco para que `Print`/`DrawAt` corran (errores irían al log).

## Resultado

- Arnés: 31 PASS, 0 errores - `logs/run-20261017-000402.log` (solo el mod y el arnés).
- Compat probada con: Vanilla Expanded Framework (`oskarpotocki.vanillafactionsexpanded.core`, patch en `Pawn.Kill`),
  Keep Burning, Corpse! (`tixiv.keepburningcorpse`, transpiler + prefix/postfix en `Pawn.Kill`), Blood and Stains
  (`zal.bloodandstains`), Death Acidifiest (`petetimessix.deathacidifiest`), Better Explosions (`nephlite.advexplosions`),
  todos juntos: 31 PASS, 0 errores, TICKS DONE - `logs/run-20261017-003319.log`. Una corrida anterior idéntica
  (`logs/run-20261017-000939.log`) pasó los 31 PASS pero el juego no llegó a 2500 ticks (timeout de 900 s, sin
  ninguna línea en el log después del DONE); no se repitió.
- Showcase: `logs/showcase/reactorslag/slag-*.png` (caliente de día/noche, medio frío, frío de día/noche);
  `About/Preview.png` sale de la noche caliente.
- No probado: guardar/cargar real (emulado con respawn `respawningAfterLoad` y estado del comp como tras cargar),
  muerte dentro de contenedor / caravana (revisado en decomp), `drawHalo = false`, la ventana de settings, mechs de
  mods con cuerpo >= 2.5 (solo por tamaño), Combat Extended.
