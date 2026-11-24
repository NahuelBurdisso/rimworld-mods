using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    [StaticConstructorOnStartup]
    public static class BracedDoorGraphics
    {
        public static readonly Material PlankMat = MaterialPool.MatFrom("Things/Aftermath/BracedPlank", ShaderDatabase.Cutout);
    }

    /// <summary>
    /// Injected into every door def (CompInjector). Records breaches of player doors by hostiles,
    /// draws one crossed plank per breach on the door standing on that cell, lists them when inspected.
    /// </summary>
    public class CompBracedDoor : ThingComp
    {
        BreachTracker tracker;
        int seenVersion = -1;
        int planks;          // visible planks for the current records (cached per tracker version)
        bool cellPending;    // a battered breach here waits for the door to be repaired

        // plank layout: angle and offset along z (world units), one entry per plank
        static readonly float[] Angles = { 32f, -32f, 14f, -14f };
        static readonly float[] Offsets = { 0.1f, 0.1f, -0.22f, -0.22f };

        bool OurDoor => parent.Faction == Faction.OfPlayer;

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            tracker = BreachTracker.For(parent.Map);
            seenVersion = -1;
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            tracker = null;
        }

        /// <summary>Thing.Kill -> Destroy(KillFinalize) -> here (ThingWithComps.Kill, Verse/ThingWithComps.cs:308).</summary>
        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            if (!AftermathMod.Settings.bracedDoors || prevMap == null || !OurDoor) return;
            Thing by = dinfo?.Instigator;
            string attacker;
            if (by != null)
            {
                // our own people never count, not even berserk or rebelling (GenHostility.cs:128-145)
                if (by.Faction == Faction.OfPlayer || !by.HostileTo(Faction.OfPlayer)) return;
                attacker = AttackerLabel(by);
            }
            else
            {
                if (!GenHostility.AnyHostileActiveThreatToPlayer(prevMap)) return;
                attacker = "Aftermath_Raiders".Translate();
            }
            BreachTracker.For(prevMap)?.Register(parent.Position, BreachKind.Broken, attacker);
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            if (!AftermathMod.Settings.bracedDoors || !parent.Spawned || parent.Destroyed || !OurDoor) return;
            if (parent.HitPoints <= 0 || parent.HitPoints > parent.MaxHitPoints * AftermathTuning.BatteredHitPointsFraction) return;
            Thing by = dinfo.Instigator;
            if (by == null || by.Faction == Faction.OfPlayer || !by.HostileTo(Faction.OfPlayer)) return;
            BreachTracker.For(parent.Map)?.Register(parent.Position, BreachKind.Battered, AttackerLabel(by));
        }

        /// <summary>Latches repairs done off-screen too (PostDraw only runs for doors in view). Free while nothing is pending.</summary>
        public override void CompTickInterval(int delta)
        {
            if (tracker == null || tracker.pendingRepairs == 0 || !parent.IsHashIntervalTick(250, delta)) return;
            VisiblePlanks();
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            // the player chose to take this door down: the cell forgets
            if (mode == DestroyMode.Deconstruct) BreachTracker.For(previousMap)?.Clear(parent.Position);
        }

        static string AttackerLabel(Thing by)
        {
            Faction f = by.Faction;
            if (f != null && !f.Name.NullOrEmpty()) return f.Name;
            return by is Pawn p ? p.KindLabel : by.LabelShort;
        }

        /// <summary>How many planks this door shows now (latches repairs). An int compare when nothing changed.</summary>
        public int VisiblePlanks()
        {
            if (tracker == null || tracker.Count == 0 || !AftermathMod.Settings.bracedDoors) return 0;
            Refresh();
            if (cellPending)
            {
                tracker.CheckRepaired(parent.Position, parent);   // checks hit points, bumps the version on a repair
                Refresh();
            }
            return planks;
        }

        void Refresh()
        {
            if (seenVersion == tracker.version) return;
            seenVersion = tracker.version;
            planks = 0;
            cellPending = false;
            List<BreachRecord> here = tracker.At(parent.Position);
            for (int i = 0; i < here.Count; i++)
            {
                if (here[i].ShowsPlank) planks++;
                else cellPending = true;
            }
            if (planks > AftermathTuning.MaxPlanksDrawn) planks = AftermathTuning.MaxPlanksDrawn;
            if (planks > Angles.Length) planks = Angles.Length;
        }

        /// <summary>The breaches of this door's cell, oldest first (empty when unspawned).</summary>
        public List<BreachRecord> Records => tracker != null ? tracker.At(parent.Position) : new List<BreachRecord>();

        public override void PostDraw()
        {
            int n = VisiblePlanks();
            if (n == 0 || (parent is Building_Door door && door.Open)) return;
            Vector3 center = parent.DrawPos;
            center.y = AltitudeLayer.DoorMoveable.AltitudeFor() + Altitudes.AltInc * 0.3f;
            // laid out along the door's own axis, like Building_Door.DrawMovers rotates the leaves
            Quaternion rot = parent.Rotation.AsQuat;
            var scale = new Vector3(1.12f * parent.def.size.x, 1f, 0.26f);
            for (int i = 0; i < n; i++)
            {
                Vector3 pos = center + rot * new Vector3(0f, 0f, Offsets[i]) + new Vector3(0f, Altitudes.AltInc * 0.02f * i, 0f);
                Matrix4x4 m = Matrix4x4.TRS(pos, rot * Quaternion.AngleAxis(Angles[i], Vector3.up), scale);
                Graphics.DrawMesh(MeshPool.plane10, m, BracedDoorGraphics.PlankMat, 0);
            }
        }

        public override string CompInspectStringExtra()
        {
            if (!AftermathMod.Settings.bracedDoors || tracker == null) return null;
            VisiblePlanks();
            List<BreachRecord> here = tracker.At(parent.Position);
            if (here.Count == 0) return null;
            var sb = new StringBuilder();
            sb.Append("Aftermath_Braced".Translate(here.Count));
            for (int i = 0; i < here.Count; i++)
            {
                BreachRecord r = here[i];
                sb.AppendLine().Append("  - ");
                string key = r.kind == BreachKind.Broken ? "Aftermath_BreachBroken" : "Aftermath_BreachBattered";
                sb.Append(key.Translate(AftermathUtil.Date(r.tick, r.longLat), r.attacker));
                if (!r.ShowsPlank) sb.Append(" ").Append("Aftermath_BreachNotRepaired".Translate());
            }
            return sb.ToString();
        }
    }
}
