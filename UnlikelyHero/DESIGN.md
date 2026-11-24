# Unlikely Hero (`nahuel.unlikelyhero`)

Idea `unlikely-hero` del ledger (combate r4, 2026-10-13). Mod standalone, C# + Harmony, sin DLC.

## Pitch

La primera vez que alguien que nunca peleó (el cocinero, la médica, la artista, el que cuida
los animales, un pibe) mata a un enemigo de verdad en un ataque, la colonia lo ve: una columna
de luz cálida breve lo envuelve, aparece un texto flotante, él piensa "I didn't know I had it in
me", los que lo vieron con sus propios ojos lo recuerdan, y los artistas pueden contarlo.
Una sola vez por colono en toda la partida. A veces le queda algo para siempre.

## Qué ve el jugador

1. Columna de luz ámbar (fleck propio, 2,6 s) y un resplandor a los pies del héroe.
2. Texto flotante cálido "An unlikely hero!" sobre el héroe.
3. Mensaje positivo: "<Héroe>, who never fought, has killed <víctima>..." (con look target).
4. Pensamiento del héroe `UnlikelyHero_FoundOut` "didn't know I had it in me": +6 de ánimo, 6 días.
5. Testigos: `UnlikelyHero_SawStandUp` (Thought_MemorySocial) "saw {0} stand up": +3 de ánimo y
   +10 de opinión sobre el héroe, 6 días, una por héroe.
6. Tale `UnlikelyHero_Tale` (Tale_DoublePawn HERO/FOE, PermanentHistorical) para arte.
7. Con chance baja (setting, 15 %): bonus permanente (ver Reglas 6). El mensaje lo nombra.

## Reglas exactas

Hook: **Postfix en `Verse.Pawn.DoKillSideEffects(DamageInfo? dinfo, Hediff exactCulprit, bool spawned)`**
(privado, `decomp/1.6.4871/Assembly-CSharp/Verse/Pawn.cs:2012`). Es el único método que llama a
`RecordsUtility.Notify_PawnKilled(this, killer)` (`Pawn.cs:2028`, único caller en todo el assembly),
justo antes, con el mismo `dinfo?.Instigator as Pawn`. Se eligió el caller y no
`RecordsUtility.Notify_PawnKilled(Pawn killed, Pawn killer)` porque éste no recibe el `DamageInfo`
y lo necesitamos para excluir fuego, explosiones y ejecuciones. En el postfix: la víctima todavía
está spawneada, no muerta (`health.SetDead` viene después, `Pawn.cs:2151`), su lord sigue asignada
(`lord.Notify_PawnLost` después) y los records del asesino ya se incrementaron.

1. **Asesino (héroe)** — todo debe cumplirse:
   - `dinfo.Instigator is Pawn`, humanlike, `IsColonist` (incluye esclavos; excluye quest lodgers),
     con `story`, `skills` y `needs.mood`. No mutante (ghoul). Spawneado en el mismo mapa que la víctima.
   - No está en ningún estado mental (berserk, asesino, etc.) ni caído.
   - No está en la lista de héroes de la partida (una sola vez por pawn, guardado en el save).
   - Ningún rasgo de violencia (lista en `UnlikelyHero_Config`: Bloodlust, Brawler,
     ShootingAccuracy (careful shooter y trigger-happy), Psychopath).
   - **No combatiente**, por A o B:
     - A: Shooting <= `maxCombatSkill` (4) **y** Melee <= 4.
     - B: "trabajo principal" pacífico: su skill más alto (empates cuentan) es Cooking, Medicine,
       Artistic o Animals (lista en el config), **y** Shooting y Melee <= `crafterCombatCap` (8)
       y ambos menores que ese skill. Se usa el skill y no las prioridades de trabajo porque con
       prioridades manuales apagadas todas valen 3 y no dicen nada.
   - **Primera baja real** (setting `requireFirstKill`, on): antes de esta muerte tenía 0 en
     KillsHumanlikes y 0 en KillsMechanoids (records vanilla; KillsAnimals no cuenta porque incluye caza).
     Así un mod agregado a mitad de partida no premia al cocinero que ya mató raiders.
2. **Víctima** — todo debe cumplirse:
   - Humanlike o animal (`RaceProps.Humanlike || RaceProps.Animal`). No mecanoide, no entidad
     de Anomaly, no subhumano, no mutante (shambler/ghoul).
   - Facción != jugador (excluye animales domesticados propios, mecanoides propios, colonos en
     berserk, esclavos rebeldes).
   - No prisionera, no caída (`Downed`: rematar a un caído no cuenta; las muertes "death on downed"
     de vanilla sí cuentan porque `Kill` corre antes de `MakeDowned`, `Pawn_HealthTracker.cs:515/524`),
     no en cama.
   - Amenaza activa: `HeroRules.WasActiveThreat` = `GenHostility.IsActiveThreatTo` (`GenHostility.cs:311`)
     sin su chequeo `Awake()`: `Awake` lee `capacities.CanBeAwake` y un tiro a la cabeza ya dejó la
     conciencia en 0 cuando corre `Kill` (lo encontró la review; P10 lo prueba). El sueño se lee del job
     (`curDriver.asleep`). Hostil, no niebla, no `ThreatDisabled`, no prisionera, no huyendo en pánico,
     no dormida/latente. Cubre raiders, manhunters (también venganza de caza) y depredadores cazando
     colonos. Como vanilla, los insectos que custodian su colmena (sin duty AssaultColony) no cuentan.
3. **Daño**: `dinfo.Def` con `ExternalViolenceFor(victim)`, **no** `isExplosive` (granadas,
   morteros, IEDs: "un héroe improbable es cara a cara"), **no** ExecutionCut, y **no** fuego ni
   quemaduras: lista `excludedDamage` del config (Flame, Burn, AcidBurn, ElectricalBurn [Anomaly],
   VacuumBurn [Odyssey]; el fuego arrastra su instigador por minutos). Torretas y trampas no cuentan solas (su instigador no es Pawn).
4. **Efecto**: se marca al héroe primero (aunque algo falle después no se repite), después:
   thought del héroe, memorias de testigos, tale, visual + texto, mensaje, tirada de bonus.
   Todo en try/catch con `Log.ErrorOnce` (estamos dentro de `Pawn.Kill`).
5. **Testigos**: pawns humanlike de la facción del jugador en el mismo mapa con `needs.mood`,
   distintos del héroe, que pasan `ThoughtUtility.Witnessed(witness, hero)` (vanilla: despierto,
   no ciego, <= 12 celdas, línea de vista). Un caído que lo ve también cuenta (la mejor historia).
6. **Bonus** (chance `bonusChance` 0,15; 0 lo apaga):
   - Primero: rasgo **Nerves +1 (steadfast)** si no tiene Nerves en ningún grado, ningún rasgo
     suyo choca con Nerves (`TraitDef.ConflictsWith`) y tiene menos de 4 rasgos. `GainTrait`.
   - Si no se puede: **pasión menor** en el skill con que mató (Shooting si `dinfo.Def.isRanged`,
     si no Melee), sólo si su pasión ahí es None, el skill no está deshabilitado y ningún rasgo
     tiene `ConflictsWithPassion` con ese skill.
   - Si tampoco: nada (se loguea).
7. **Gesta Feats** (`ModsConfig.IsActive("nahuel.gesta")`, setting `quietWithGesta`, on):
   Gesta ya usa la columna de luz dorada + texto + tale para sus feats. Con Gesta cargado,
   Unlikely Hero no tira columna, texto ni tale (no se confunde con un feat ni lo duplica si la
   misma baja es también un feat de Gesta); sí da el pensamiento, las memorias de testigos, el
   mensaje y la tirada de bonus (Gesta no tiene equivalente). Con el setting en off, todo.
8. **Rocket's Medals** (`rockets.medals`): medallas que otorga el jugador por ritual. No parchea
   kills ni da pensamientos por bajas: no hay solapamiento. No hace falta integración.
9. **Casos borde**:
   - Caravanas / mapas temporales: funciona en cualquier mapa (héroe y víctima en el mismo).
   - Razas de mods: se exige humanlike con skills/story/mood; sin eso, no califica (no hay crash).
   - Niños (Biotech): califican (skills bajos); la tale permite niños.
   - Combat Extended: el instigador y DamageDef siguen viniendo en el DamageInfo; balas CE son
     `isRanged`. Explosivos de CE son `isExplosive`. No probado con CE instalado.
   - Prisioneros fugándose: `IsPrisoner` => no cuenta.

## Settings (ModSettings)

`bonusChance` (0..1, 0,15), `maxCombatSkill` (0..10, 4), `crafterCombatCap` (0..20, 8),
`requireFirstKill` (on), `quietWithGesta` (on), `showMessage` (on).

## Hooks verificados (decomp 1.6.4871)

- `Verse/Pawn.cs:2012` `private void DoKillSideEffects(DamageInfo? dinfo, Hediff exactCulprit, bool spawned)`
- `RimWorld/RecordsUtility.cs:8` `Notify_PawnKilled(Pawn killed, Pawn killer)` (sólo como referencia del momento)
- `RimWorld/ThoughtUtility.cs:368` `Witnessed(Pawn p, Pawn victim)`
- `RimWorld/GenHostility.cs:311` `IsActiveThreatTo(IAttackTarget, Faction, bool, bool)`
- `RimWorld/TraitSet.cs:258` `GainTrait(Trait, bool)`, `RimWorld/TraitDef.cs:101` `ConflictsWith(TraitDef)`, `:129` `ConflictsWithPassion`
- `RimWorld/MoteMaker.cs:48` `ThrowText(Vector3, Map, string, Color, float)`
- `RimWorld/FleckMaker.cs:8` `GetDataStatic(Vector3, Map, FleckDef, float)`
- `RimWorld/TaleRecorder.cs:8` `RecordTale(TaleDef, params object[])`
- `RimWorld/MemoryThoughtHandler.cs:129` `TryGainMemory(ThoughtDef, Pawn, Precept)`

## Plan de prueba (arnés `nahuel.unlikelyherotest`, `Source/UnlikelyHeroTest/HeroHarness.cs`, armado por `rwtools-ticks`)

Colonos generados con `PawnGenerator` (facción jugador, adultos baseliner, sin rasgos, skills en 3)
para controlar skills/rasgos; cada caso negativo usa un asesino nuevo (cada baja sube los records).
- P1 humano: cocinero (Shooting 2, Melee 2) mata a un pirata -> momento, ledger, pensamiento propio,
  testigo con LOS recibe "saw X stand up" (+3 ánimo, +10 opinión, label y descripción con el nombre),
  el de atrás de una pared no, el héroe no es testigo de sí mismo, tale + descripción de arte,
  visual (contador), bonus a chance 1 -> steadfast.
- P2 una sola vez por pawn.
- P3 negativos: caído (a), prisionero (b), ExecutionCut (c), Flame (d), Bomb (e), AcidBurn (f),
  animal propio (g), extraño no hostil (h), asesino en berserk (i).
- P4 combatiente (Shooting 12). P5 bajas previas (KillsHumanlikes 1). P6 rasgo Brawler.
- P7 animal hostil: médico nervioso mata a un jabalí manhunter cuerpo a cuerpo -> sí; el bonus no
  agrega steadfast (choca con Nervous) y da pasión menor en Melee.
- P8 regla B: Cooking 12 / Shooting 6 / Melee 5 -> sí; Shooting 9 (> 8) -> no; mejor skill
  Construction -> no.
- P10 daño real: bala al cerebro con `TakeDamage` (no `Kill` pelado) -> sí.
- P11 death-on-downed real (`overrideDeathOnDownedChance = 1` + golpes) -> sí.
- P9 save: el `HeroRoll` se serializa y se vuelve a leer con Scribe (archivo en la carpeta de datos
  del perfil de prueba).
- Corrida con Gesta cargado: modo silencioso (sin visual ni tale, sí pensamiento y memorias).
- Showcase (`showcase.flag`): capturas de la columna de luz en `logs/showcase/unlikelyhero/`.

## Compat probada con

- **Gesta** (`nahuel.gesta`, propio): mismo arnés con Gesta cargado -> modo silencioso activo, sin
  columna/texto/tale, sí pensamiento, memorias y bonus. 56 PASS, 0 errores (logs/run-20261016-225218.log).
- **Trauma and Integrity** (`ferny.TraumaAndIntegrity`, Workshop): parchea `Pawn_RecordsTracker.Increment`
  (suma trauma por KillsHumanlikes), mismo sistema de records que leemos. Misma corrida: 0 errores.
- **Rocket's Medals** (`rockets.medals`): no parchea kills ni records (sólo apparel/ritual); no se corrió.
- Sin compat: Combat Extended (no instalado).

## Resultado

- Arnés: 57 PASS, 0 errores (logs/run-20261016-230018.log).
- Showcase: logs/showcase/unlikelyhero/unlikelyhero-1-beam.png, -2-fading.png; Preview.png sale del recorte del 1.
- No probado: ventana de settings (UI), mensaje en pantalla verificado sólo en la captura, una partida
  real guardada y recargada (se prueba el round-trip del `HeroRoll`), caravanas, razas de mods, niños,
  raids reales con IA (las muertes se generan con `Kill`/`TakeDamage` desde el arnés).
