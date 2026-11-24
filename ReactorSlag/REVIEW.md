# Reactor Slag - revisión

Revisión estática de un agente `code-reviewer` (2026-10-16), contra DESIGN.md y decomp/1.6.4871. Sin críticos.

## Confirmado y corregido

1. **Desarmar un mech pesado en el mech bay derramaba un núcleo** (importante). `JobDriver_DisassembleMech.cs:38-41`
   mata con `forceNoDeathNotification = true` y destruye el cadáver enseguida. Fix: `Notify_Killed` sale si
   `pawn.forceNoDeathNotification`. Test: "a disassembled centurion spilled no core".
2. **La lava enfriada mataba árboles** (importante): `TerrainGrid.SetTerrain` -> `DoTerrainChangedEffects` destruye
   plantas cuyo `fertilityMin` supera la fertilidad 0 de CooledLava (Gauranlen y anima incluidos). Fix:
   `CanScarGround` rechaza celdas con cualquier planta árbol o cosechable (recorre toda la lista de la celda). Test:
   roble en la cruz del núcleo grande sigue vivo y sin lava; además apareció en una corrida un arbusto cosechable en
   la cruz -> la aserción ahora exige lava exactamente en las celdas elegibles.
3. **El "reload" del arnés no probaba el camino real** (importante, arnés): ahora el comp queda como tras cargar
   (`glowOnInt = true`, sin override de radio), se re-spawnea con `respawningAfterLoad`, se recalcula el glow grid y
   se mide luz a 7 celdas (> 0 solo con radio 9) y que el glower esté en `GlowGrid.litGlowers`.
4. Menores aplicados: conteos exactos de filth (9/9, 21/21, medidos contra `FilthMaker.CanMakeFilth`); `litGlowers`
   en vez de `Glows` para "luz apagada"; radio esperado numérico (~4.6) en vez de la misma fórmula; chequeo de que el
   núcleo caliente esté en `CurrentViewRect`; negativos de zona de cultivo y `groundScar = false`; comentario engañoso
   en el try/catch; `RandomPoolCell` ahora usa el disco (GenRadial) y no el cuadrado; se quitó la referencia a
   0Harmony del csproj (el mod no usa Harmony).

## Bug propio encontrado por el arnés

- `spawnTick < 0` como "sin setear": en las primeras horas de una partida (o al envejecer un núcleo en el test)
  `TicksGame - 1 día` es negativo y el calor quedaba en 1 para siempre. Ahora el centinela es `int.MinValue`.

## No aplicado

- Muerte dentro de un contenedor / `prevMap == null`: verificado en decomp por el revisor (el cadáver no está
  spawneado -> no hay núcleo), sin test propio.
- `drawHalo = false`: solo corta el `DrawAt`; sin test (no hay forma de afirmar que no se dibujó).
- Roca natural áspera puede volverse CooledLava: es reversible con "quitar piso" (CooledLava es `layerable` y guarda
  el terreno debajo). Se deja así.
