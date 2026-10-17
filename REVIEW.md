# The Pack Howls - revisión (2026-10-16)

Revisor: agente `code-reviewer` (solo lectura, verificado contra decomp 1.6.4871). Hallazgos y qué se hizo.

## Confirmados (corregidos)
1. La descripción del recuerdo fuerte usaba `{0}`, que en descripciones es el pawn que piensa (`Thought.cs:72-74`):
   ahora `{OTHERPAWN_labelShort}`. Test nuevo sobre la descripción.
2. El recuerdo fuerte sólo miraba relaciones directas de la víctima: hermanos/abuelos/nietos (implícitas) quedaban
   afuera y los ex entraban. Ahora: Spouse/Fiance/Lover/Bond directos + `FamilyByBlood`. Tests de hijo y nieto.
3. El animal vinculado del colono caído entra en Berserk/Manhunter (50%) y bloqueaba el aullido como "amenaza" hasta
   3 días, y su muerte a manos de colonos contaba como caído en combate. Ahora `AnyRealThreat` ignora pawns del
   jugador. Test con un husky en Manhunter.
4. Off-by-one en `JobDriver_Howl` (DriverTick descuenta antes de `tickAction`): 1 de cada 40 animales perdía el primer
   aullido. Corregido; el test ahora exige los 16 anillos.
5. Instigador destruido (raider muerto, su molotov/mortero) no era hostil para `HostileTo`: fallback a la facción.
6. Desactivar "wild answer" durante los 600 ticks no cancelaba la respuesta: se relee al responder.

## Especulativos tomados
- S1: se copia `SpawnedColonyAnimals` (buffer compartido) antes de arrancar jobs.
- S2: muertes no violentas durante una amenaza contaban. Ahora: con `dinfo` hace falta daño de violencia externa;
  sin `dinfo`, el culpable tiene que ser nulo, `BloodLoss` o una herida. Test con desnutrición tras heridas.
- S3: mapas de gravnave (Odyssey) cuentan como hogar: documentado en DESIGN.

## No tomados
- S4 (referencias a muertos descartadas por WorldPawnGC: bajo riesgo, se loguea warning y se limpia), S5 (memorias
  fuertes de víctimas distintas en peleas distintas pueden coexistir: intencional), S6 (Combat Extended: sin probar),
  S7 (resucitados siguen nombrados: documentado).

## Arnés
- H1: la celda esperada se toma antes de matar y se chequean los dos huskies.
- H2: anillos >= 16 (antes 8).
- H3: chequeo de la descripción.
- H4: casos nuevos de fuego amigo, sangrado, culpable no violento, fuego amigo sin pelea.
- H5: colono fuera del radio; hijo/nieto; animal en estado mental. Save/load y settings siguen sin test.
- H6: el veredicto exige la línea `[Test] DONE PackHowls`.
- Además: watchdog que despausa (el Manhunter dispara la autopausa "major threat" y la corrida se colgaba).
