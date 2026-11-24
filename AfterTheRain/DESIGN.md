# After the Rain - DESIGN

packageId `nahuel.aftertherain`, prefijo `AfterTheRain_`. RimWorld 1.6, sin DLC requerido.
Idea `arcoiris` del ledger (test-run-clima, puntaje 50; reserva-vs-instalados: puesto 7).

## Pitch

A veces, cuando para de llover y el sol está bajo (mañana o tarde), sale un arco iris
pastel muy tenue sobre el mapa durante 1-2 horas de juego. Aparece y se desvanece suave.
Los colonos que están afuera y lo ven se sienten un poco mejor (+2), y con suerte uno se
inspira. Es una recompensa rara al clima feo.

## Qué ve el jugador

- Un arco ancho, pastel (rojo afuera, violeta adentro), alpha final ~0.10-0.18, dibujado en
  screen-space sobre el mapa (como los overlays de lluvia), con un segundo arco muy débil
  por fuera con colores invertidos y un leve aclarado del cielo adentro del arco primario.
- El arco aparece del lado opuesto al sol: a la mañana (sol al este) a la izquierda/oeste,
  a la tarde a la derecha (`SideFor(DayPercent)`; el vector de sombra vanilla no sirve: con
  brillo <= 0.6 usa la rama de la luna y se invierte justo al amanecer/atardecer).
- En la lista de condiciones (arriba a la derecha): "Rainbow", con descripción.
- Mensaje opcional (verde, PositiveEvent): "A rainbow over <colonia>."
- Pensamiento "saw a rainbow" +2, medio día.
- Si alguien se inspira: la carta vanilla de inspiración con el motivo
  "The rainbow after the rain lifted their spirits."
- Los colonos con tiempo libre tienden más a mirar el cielo (skygaze x3) mientras dura.

## Reglas exactas

### Disparo (RainbowWatcher : MapComponent)
Chequeo cada 250 ticks (`(TicksGame + map.uniqueID) % 250 == 0`). Sin estado de clima
propio: lee `WeatherManager.lastWeather/curWeather/curWeatherAge` (campos públicos).

Hay "transición de lluvia a despejado" si:
1. `lastWeather` es lluvia: `rainRate > 0`, `snowRate <= 0` (las nevadas vanilla tienen
   rainRate 1), `sandRate <= 0`, sin rayos (ningún eventMaker con eventClass derivado de
   `WeatherEvent_LightningFlash`, que es padre de LightningStrike), sin niebla (ningún
   overlayClass cuyo nombre contenga "Fog") y no está en `excludedRain` (BloodRain).
2. `curWeather` es despejado: rain/snow/sand = 0, sin rayos, sin niebla, `maxGlow >= 1`
   (deja afuera Overcast de Odyssey), cielo de día claro (`skyColorsDay.sky.grayscale >= 0.9`),
   no está en `excludedClear`; o está en `extraClear`.
3. `curWeatherAge` en [2000, 7500]: desde la mitad del fundido vanilla (4000 ticks) hasta
   3 horas de juego después del cambio.
4. Esa transición no se tiró todavía: `transitionStart = TicksGame - curWeatherAge` distinto
   de `rolledTransitionStart` (guardado).

Luego, en orden (cada uno devuelve un motivo, `LastResult`, para tests/debug):
- `Disabled` si el setting `enabled` es false.
- `AlreadyActive` si ya hay un arco en el mapa.
- `NoSky`: mapa subterráneo (`generatorDef.isUnderground`), pocket map, capa espacial
  (`Tile.LayerDef.isSpace`), bioma en vacío o `disableSkyLighting`, o
  `GameConditionManager.IsAlwaysDarkOutside`.
- `Suppressed`: condición activa de `suppressedBy` (Eclipse, VolcanicWinter, ToxicFallout,
  NoxiousHaze, UnnaturalDarkness, BloodRain, GrayPall; los de DLC con MayRequire).
- `OutOfWindow`: ventana de sol. `DayPercent` en [0.25, 0.42] (6:00-10:05) o
  [0.58, 0.77] (13:55-18:29) **y** brillo solar `CurCelestialSunGlow` >= 0.2 (hay luz).
  El brillo vanilla NO mide la elevación: `GenCelestial.SunPosition` acerca el sol al cenit
  (19° + hasta 17° según latitud) antes del clamp, y da 1.0 de ~8 a ~16 h en el mapa de
  prueba (tabla en el log del arnés). Por eso "sol bajo-medio" = la hora. No se tira: la
  transición sigue pendiente y puede salir más tarde dentro de las 3 h (lluvia que para a
  las 12 -> arco desde las 13:55 si sigue despejado).
- `NoWitness`: ningún colono libre en el mapa (no se tira, sigue pendiente).
- Se tira una sola vez por transición: `rolledTransitionStart = transitionStart`.
  `Rand.Chance(chance)` (setting, default 0.35). `RollFailed` o `Started`.

Started: `GameConditionMaker.MakeCondition(AfterTheRain_Rainbow, Rand.Range(2500, 5000))`
(1-2 h), registrado en `map.gameConditionManager`. Mensaje si `showMessage`.

### El arco (GameCondition_Rainbow)
- Fundido: `TransitionTicks = 600`; fuerza = `GameConditionUtility.LerpInOutValue`.
- Cada 250 ticks: si el clima deja de ser despejado, el brillo baja de 0.1, la hora sale
  de la ventana + 0.04 (~1 h de gracia), o aparece una condición de `suppressedBy` o
  cualquier `GameCondition_NoSunlight`, se acorta a `TicksLeft = TransitionTicks` (fundido
  de salida, una vez) y ya no se cuentan testigos nuevos.
- Testigos (cada 250 ticks, solo con fuerza >= 0.3): `FreeColonistsSpawned` (incluye
  esclavos; no prisioneros, invitados, animales ni mecanoides), con `needs.mood`, despiertos,
  no ciegos (`PawnUtility.IsBiologicallyOrArtificiallyBlind`, como ThoughtWorker_Aurora),
  celda sin techo, no vistos antes en este arco (`seen`, guardado).
  - Memoria `AfterTheRain_SawRainbow` (+2, 0.5 días, stackLimit 1).
  - Inspiración: como mucho UNA por arco. Para cada testigo nuevo no inspirado,
    `Rand.Chance(inspirationChance)` (default 0.05) -> `GetRandomAvailableInspirationDef()`
    y `TryStartInspiration` (vanilla chequea ya-inspirado, hediffs que bloquean y
    `InspirationCanOccur`; vanilla no tiene cooldown aparte). Si no hay ninguna disponible
    no se gasta el cupo.
- Skygaze: `SkyGazeChanceFactor` 3, `SkyGazeJoyGainFactor` 1.5.
- Sin endMessage ni carta: se va solo.
- Un solo arco por mapa a la vez. Mapas sin colonos no lo generan. Caravanas: nada.
- Mods con razas: solo cuenta que tengan mood y no sean ciegos; no se asume humano.

### Dibujo
- `GameConditionDraw(map)` (vanilla lo llama por frame solo para el mapa visible,
  Map.cs:1178). Un quad `MeshPool.plane10` con matriz propia en screen-space (como
  `SkyOverlay.DrawScreenOverlay`), a altitud `AltitudeLayer.Weather + 0.04` (encima de
  la lluvia, debajo de la iluminación y la niebla de guerra).
- Dos materiales propios creados en `[StaticConstructorOnStartup]`:
  `ShaderDatabase.Transparent` (cuerpo pastel) y `ShaderDatabase.MoteGlow` (brillo aditivo
  suave), ambos con `Textures/AfterTheRain/Rainbow.png` generado por
  `tools/make_rainbow.py` (en el repo del mod). Sin shaders en AssetBundle.
- Testigos: la lista `FreeColonistsSpawned` se copia a un buffer estático antes de iterar.
- Alpha = fuerza * `opacity` (setting, 0.25-2, default 1) * 0.12 (transparente) / 0.045 (glow).
- Geometría: ancho del quad = 1.15 x ancho de pantalla, alto = ancho/2, centro del arco
  1.24 x semialto por debajo del centro de cámara; desplazado `side * 0.2 * anchoPantalla`
  hacia el lado antisolar (`side` = signo del vector de sombra al empezar, guardado).
  Cero allocs por frame.

### Settings (ModSettings)
enabled (true), chance (0.35), inspirationChance (0.05), opacity (1.0), showMessage (true).

## Hooks (verificados en decomp/1.6.4871)
- `RimWorld/WeatherManager.cs`: `lastWeather`, `curWeather`, `curWeatherAge` públicos;
  `TransitionTo` (:138) pone curWeatherAge = 0; `TransitionTicks = 4000`.
- `Verse/WeatherDef.cs`: rainRate, snowRate, sandRate, maxGlow, eventMakers, overlayClasses,
  skyColorsDay.
- `RimWorld/GameCondition.cs`: GameConditionTick, GameConditionDraw, TransitionTicks,
  TicksLeft (set), SkyGazeChanceFactor/JoyGainFactor, ExposeData.
- `RimWorld/GameConditionMaker.cs:16` MakeCondition(def, duration);
  `GameConditionManager.RegisterCondition` (:167).
- `RimWorld/GameConditionUtility.cs` LerpInOutValue.
- `RimWorld/GenCelestial.cs`: CurCelestialSunGlow (:59), GetLightSourceInfo(Shadow) (:80).
- `Verse/SkyOverlay.cs:29` (patrón de quad en screen-space).
- `RimWorld/InspirationHandler.cs:68,134`.
- `RimWorld/JoyGiver_Skygaze.cs:10` usa AggregateSkyGazeChanceFactor.
- Sin Harmony: no se parchea nada (la dependencia se sacó después de la revisión).

## Compat
- Ningún mod instalado hace arco iris (`rw find rainbow`, grep de About/Defs).
- Tocan clima/cielo: VEF (patch a `WeatherManager.CurWeatherPerceived` y algo de SkyManager),
  VEE (patch a `WeatherDecider.CurrentWeatherCommonality`), Gesta Turning Tide (postfix a
  `SkyManager.CurrentSkyTarget`), Omens Rain Reveals (lee RainRate). Ninguno toca los campos
  que leo ni GameConditionDraw. WeatherDefs de mods: VQE Ancients/Deadlife (mapas de quest
  subterráneos) - quedan fuera por NoSky/clear-check.
- compat probada con: Vanilla Expanded Framework + Vanilla Events Expanded, Gesta (Turning Tide), Omens (Rain Reveals): 54 PASS, 0 errores del mod (logs/run-20261017-003110.log).

## Plan de prueba (arnés AfterTheRainTest, `--ticks 2500`)
1. Clasificación de clima: Rain/TorrentialRain/ToxRain = lluvia; RainyThunderstorm, FoggyRain,
   SnowGentle, BloodRain, Clear = no lluvia. Clear/Windy = despejado; Fog, Overcast, Rain,
   DryThunderstorm = no despejado.
2. Ventana de sol: (0.35,0.6) y (0.65,0.6) sí; (0.5,0.9) mediodía no; (0.1,0) noche no;
   (0.45,1)/(0.55,1) sol alto no; (0.3,0.1) amanecer oscuro no; gracia de 1 h; lado
   mañana/tarde; con el sol REAL del mapa, hora por hora, abren las dos ventanas.
3. Mapa de inicio tiene cielo.
4. Camino real: TransitionTo(Rain)->TransitionTo(Clear), edad 2500, Evaluate(0.35,0.6,1)
   -> Started, condición activa, duración en [2500,5000], segunda llamada no crea otra.
5. Negativos: tormenta eléctrica->Clear, FoggyRain->Clear, Rain->Fog: NotTransition;
   mediodía: OutOfWindow y luego sí sale (pendiente); chance 0: RollFailed y después
   AlreadyRolled con chance 1; edad > 7500: NotTransition; Eclipse: Suppressed.
6. Testigos: colono reclutado afuera (drafteado) recibe la memoria; otro bajo techo no.
7. Inspiración con inspirationChance 1: exactamente un testigo inspirado.
8. Si vuelve a llover, TicksLeft <= TransitionTicks.
9. El tick real llama a Evaluate (LastEvaluateTick avanza) y el dibujo corre (DrawCount > 0)
   sin errores.
Showcase (`showcase.flag`): mañana despejada, capturas antes/fundido/lleno/zoom out/salida.
