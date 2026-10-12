using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Duelo
{
    public class CompProperties_DueloGrave : CompProperties
    {
        public CompProperties_DueloGrave() { compClass = typeof(CompDueloGrave); }
    }

    /// <summary>
    /// Added to every grave def at startup (GraveCompInjector). Keeps the eulogy spoken at this grave,
    /// and grows the moss of the beloved: recomputed every ~2500 ticks from the living's love.
    /// </summary>
    public class CompDueloGrave : ThingComp
    {
        public string eulogyFor;            // ThingID of the pawn the eulogy was spoken for
        public List<string> eulogyLines;
        public bool spokenAtFuneral;
        public int spokenTick = -1;

        public float mossLevel;              // saved; what is drawn
        public float mossTarget = -1f;       // recomputed, not saved (-1 = not computed since spawn/load)
        public float lastLove;
        int rareTicks;
        internal bool registered;            // in MapComponent_Duelo.graves (drawn)

        public Building_Grave Grave => parent as Building_Grave;

        public Pawn Buried => Grave?.Corpse?.InnerPawn;

        /// <summary>True once a eulogy has been spoken here for the pawn lying in this grave now.</summary>
        public bool HasEulogyForCurrent
        {
            get
            {
                if (eulogyLines == null || eulogyLines.Count == 0 || eulogyFor == null) return false;
                Pawn b = Buried;
                return b != null && b.ThingID == eulogyFor;
            }
        }

        public bool SpokenFor(Pawn p) => p != null && eulogyFor == p.ThingID && eulogyLines != null && eulogyLines.Count > 0;

        public void SetEulogy(Pawn p, List<string> lines, bool atFuneral)
        {
            eulogyFor = p.ThingID;
            eulogyLines = new List<string>(lines);
            spokenAtFuneral = atFuneral;
            spokenTick = Find.TickManager.TicksGame;
        }

        public string FullText
        {
            get
            {
                if (eulogyLines == null) return string.Empty;
                var sb = new StringBuilder();
                for (int i = 0; i < eulogyLines.Count; i++)
                {
                    if (i > 0) sb.Append("\n\n");
                    sb.Append(eulogyLines[i]);
                }
                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- moss

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            rareTicks = Mathf.Abs(parent.thingIDNumber) % MossUtility.RecomputeEveryRareTicks;   // stagger graves
            MapComponent_Duelo.For(parent.Map)?.RegisterGrave(this);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            MapComponent_Duelo.For(map)?.DeregisterGrave(this);
        }

        public override void CompTickRare()
        {
            if (!registered && parent.Spawned) MapComponent_Duelo.For(parent.Map)?.RegisterGrave(this);
            if (!DueloMod.Settings.moss) return;
            if (mossTarget >= 0f && ++rareTicks % MossUtility.RecomputeEveryRareTicks != 0) return;
            RecomputeMoss();
        }

        /// <summary>
        /// Re-reads the living's love and moves the moss one step toward it. The first call after spawn or
        /// load only sets the target, so saving and loading never speeds the moss up.
        /// </summary>
        public void RecomputeMoss()
        {
            bool step = mossTarget >= 0f;
            lastLove = MossUtility.LoveFor(Buried);
            mossTarget = MossUtility.TargetForLove(lastLove);
            if (step) mossLevel = Mathf.MoveTowards(mossLevel, mossTarget, MossUtility.StepPerRecompute);
        }

        public string MossLabel
        {
            get
            {
                if (mossLevel < 0.05f) return null;
                if (mossLevel < 0.25f) return "Duelo_MossFaint".Translate();
                if (mossLevel < 0.5f) return "Duelo_MossGrowing".Translate();
                if (mossLevel < 0.8f) return "Duelo_MossLush".Translate();
                return "Duelo_MossRadiant".Translate();
            }
        }

        public override string CompInspectStringExtra()
        {
            var sb = new StringBuilder();
            if (DueloMod.Settings.moss && MossLabel != null)
                sb.Append("Duelo_MossInspect".Translate(MossLabel).Resolve());
            if (DueloMod.Settings.eulogy && HasEulogyForCurrent)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append("Duelo_EulogyInspect".Translate(eulogyLines[0]).Resolve());
            }
            return sb.Length > 0 ? sb.ToString() : null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!DueloMod.Settings.eulogy || !HasEulogyForCurrent) yield break;
            string name = Buried.LabelShortCap;
            yield return new Command_Action
            {
                defaultLabel = "Duelo_ReadEulogy".Translate(),
                defaultDesc = "Duelo_ReadEulogyDesc".Translate(name),
                icon = DueloTex.ReadEulogy,
                action = () => Find.WindowStack.Add(new Dialog_MessageBox(FullText, title: "Duelo_EulogyTitle".Translate(name))),
            };
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref eulogyFor, "dueloEulogyFor");
            Scribe_Collections.Look(ref eulogyLines, "dueloEulogyLines", LookMode.Value);
            Scribe_Values.Look(ref spokenAtFuneral, "dueloSpokenAtFuneral");
            Scribe_Values.Look(ref spokenTick, "dueloSpokenTick", -1);
            Scribe_Values.Look(ref mossLevel, "dueloMossLevel", 0f);
        }
    }
}
