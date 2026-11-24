# Machine Vigil - revisión (agente code-reviewer, 2026-10-16)

Revisión contra DESIGN.md y decomp/1.6.4871. Todo lo confirmado se corrigió antes del primer gametest.

| # | Severidad | Hallazgo | Estado |
|---|-----------|----------|--------|
| 1 | bug crítico | El postfix de muerte exigía `mechanitor.Dead`, pero `Pawn.Kill` remueve relaciones antes de `health.SetDead()`: nunca se registraba nada. | Corregido: `Dead \|\| health.isBeingKilled` (verificado en `Verse/Pawn.cs` Kill). |
| 2 | riesgo | `Wake` hacía `EndCurrentJob` con re-pensar inmediato, antes de que se guarde la relación Overseer: el mech elegía el wander de mech sin control. | Corregido: `startNewJob: false`. |
| 3 | riesgo | Reacción lenta a enemigos: Keep expiraba a 600 (hasta ~1200 ticks), Approach no expiraba. | Corregido: Keep 250, Approach 300 con `checkOverrideOnExpire`. |
| 4 | riesgo | Al guardar no se podaban mechs que cambiaron de facción; `byMech` quedaba con referencias viejas. | Corregido: `Vigil.PruneMechs()` (incluye facción) + `RebuildIndex()` al guardar. |
| 5 | riesgo | El anillo tardaba en seguir a un cuerpo movido. | Corregido: el chequeo de 250 ticks corta el Keep de mechs fuera del anillo. |
| 6 | nit | Celdas del anillo del otro lado de una pared; `Danger.Deadly`. | Corregido: preferencia por LOS al centro, `Danger.Some`. |
| 7 | nit | Patch del ThinkTree da error rojo si otro mod reestructura el árbol. | Corregido: `PatchOperationConditional`. |
| 8 | nit | `Instance` estático sobrevive a la partida (sólo memoria, se pisa en el constructor). | Aceptado. |
