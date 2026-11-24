# REVIEW - After the Rain

Revisión por agente `code-reviewer` (2026-10-17) contra DESIGN.md y decomp 1.6.4871. Estado de cada hallazgo:

| # | Sev. | Hallazgo | Estado |
|---|------|----------|--------|
| 1 | alta | `MaxGlow = 0.95` cerraba la ventana: `GenCelestial.SunPosition` acerca el sol al cenit (19° + hasta 17°) antes del clamp, el brillo da 1.0 de ~7 a ~17 h. | **Confirmado** por el arnés (tabla por hora: 8-16 h = 1.00). Sacado el tope; la ventana es por hora (`DayPercent` 0.25-0.42 y 0.58-0.77) + brillo >= 0.2. Test nuevo con el sol real del mapa hora por hora. |
| 2 | media | `side` desde el vector de sombra se invierte con brillo <= 0.6 (rama "luna" de `GetLightSourceInfo`). | **Confirmado** en decomp. Ahora `SideFor(DayPercent)`: mañana izquierda, tarde derecha. Test. |
| 3 | media | Tests/showcase no cubrían el camino real (sol fijo, showcase iniciaba directo). | **Corregido**: el showcase hace `Log.Error` si el watcher no da `Started`; dos arcos reales (6:18 y 17:24). Tabla de sol real en el arnés. |
| 4 | media | Quitar el mod con un arco activo: GameCondition con def null -> NRE por frame / por tick. | **Confirmado** (no se arregla desde código). About.xml ahora dice que se quite cuando "Rainbow" no esté en la lista de condiciones y que habrá un error rojo inofensivo una vez. |
| 5 | baja | `FreeColonistsSpawned` es una lista cacheada que se rellena en cada llamada. | **Corregido**: copia a buffer estático antes de iterar. |
| 6 | baja | Sin corte por sol alto durante el arco. | **Corregido**: fundido temprano si `DayPercent` sale de la ventana + 0.04 (~1 h). Test de `InGraceWindow`. |
| 7 | baja | Con `endingEarly` seguía dando memorias. | **Corregido**: no se cuentan testigos mientras se va por lluvia/luz. |
| 8 | baja | Oscuridad de otros mods no suprime. | **Corregido**: cualquier `GameCondition_NoSunlight` activa (mapa o mundo) suprime. |
| 9 | baja | Motivo de inspiración ambiguo ("their"). | **Corregido**: "[PAWN_nameDef] looked up at the rainbow after the rain, and something stirred." |
| 10 | baja | Arnés sin guardas de DLC (Odyssey/Anomaly) y test de inspiración sin salida si no hay inspiración disponible. | **Corregido**: guardas `ModsConfig.*Active`; SKIP con warning si ninguno tiene inspiración disponible. |
| 11 | baja | Dependencia de Harmony sin patches. | **Corregido**: sacada (About sin modDependencies, sin `PatchAll`). |

Verificado sin problemas por el revisor: estabilidad de `transitionStart` (curWeatherAge forceSave, mismo orden de tick),
`SingleMap` en `Init`, esclavos incluidos, Scribe de `seen`, supresión mira el mundo, defs/MayRequire, clasificación
de climas, textura POT con bordes alpha 0, draw sin allocs, altitud debajo de LightingOverlay/FogOfWar.
