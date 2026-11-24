using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace Aftermath
{
    [StaticConstructorOnStartup]
    public static class StandardGraphics
    {
        public static readonly Texture2D ClothTex = ContentFinder<Texture2D>.Get("Things/Building/Aftermath/CapturedStandardCloth");

        // centre of the cloth relative to the texture centre, in texture fractions (x right, z up); see make_textures.py
        public static readonly Vector2 ClothCenter = new Vector2(11f / 128f, 10f / 128f);
        public const float IconSizeFraction = 0.40f;   // of drawSize.x

        // Printer_Plane lifts the top vertices of every plane by 0.01 (~0.27 AltInc): the icon must sit
        // clearly above the cloth's tilted plane or the depth test hides it (the 2026-10-11 bug).
        public const float ClothLift = 0.2f, IconLift = 0.7f;   // in Altitudes.AltInc

        public static Material ClothMat(Color c) => MaterialPool.MatFrom(ClothTex, ShaderDatabase.Cutout, c);

        /// <summary>The faction's icon, else its settlement texture, else null (never BadTex).</summary>
        public static Texture2D Emblem(FactionDef def)
        {
            if (def == null) return null;
            Texture2D t = def.FactionIcon;
            if (t != null && t != BaseContent.BadTex) return t;
            if (def.settlementTexturePath.NullOrEmpty()) return null;
            t = def.SettlementTexture;
            return t != null && t != BaseContent.BadTex ? t : null;
        }

        public static Material IconMat(Texture2D icon, Color clothColor)
        {
            // dark icon on a light cloth, light icon on a dark one
            float lum = 0.299f * clothColor.r + 0.587f * clothColor.g + 0.114f * clothColor.b;
            Color tint = lum > 0.55f ? new Color(0.12f, 0.10f, 0.08f) : new Color(0.96f, 0.93f, 0.86f);
            return MaterialPool.MatFrom(icon, ShaderDatabase.Cutout, tint);
        }
    }

    /// <summary>
    /// The standard a raid's leader dropped. Remembers the faction, the leader, who brought him down and
    /// the colonists who fought that day; prints the faction-coloured cloth and icon over its pole; gives
    /// pride to colonists who see it (vanilla PawnObserver, RimWorld/PawnObserver.cs).
    /// </summary>
    public class Building_CapturedStandard : Building, IObservedThoughtGiver
    {
        public Faction sourceFaction;
        public FactionDef factionDef;
        public string factionName;
        public string leaderName;
        public string killerName;
        public Color clothColor = Color.white;
        public int tick;
        public Vector2 longLat;
        public List<int> veterans = new List<int>();

        public void Init(Pawn leader, Thing by, List<int> veteranIds, Map map)
        {
            sourceFaction = leader.Faction;
            factionDef = sourceFaction?.def;
            factionName = sourceFaction?.Name ?? "?";
            clothColor = sourceFaction != null ? sourceFaction.Color : Color.white;
            leaderName = AftermathUtil.FoeName(leader);
            killerName = AftermathUtil.NameOf(by);
            tick = Find.TickManager.TicksGame;
            longLat = AftermathUtil.LongLat(map);
            veterans = new List<int>(veteranIds);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref sourceFaction, "sourceFaction");
            Scribe_Defs.Look(ref factionDef, "factionDef");
            Scribe_Values.Look(ref factionName, "factionName");
            Scribe_Values.Look(ref leaderName, "leaderName");
            Scribe_Values.Look(ref killerName, "killerName");
            Scribe_Values.Look(ref clothColor, "clothColor", Color.white);
            Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref longLat, "longLat");
            Scribe_Collections.Look(ref veterans, "veterans", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && veterans == null) veterans = new List<int>();
        }

        public override string LabelNoCount =>
            factionName.NullOrEmpty() ? base.LabelNoCount : "Aftermath_StandardLabel".Translate(base.LabelNoCount, factionName).ToString();

        /// <summary>What is printed on the cloth: the faction icon (or settlement texture); null = plain cloth.</summary>
        public Texture2D EmblemTexture => StandardGraphics.Emblem(factionDef);

        public override void Print(SectionLayer layer)
        {
            base.Print(layer);
            Vector2 size = def.graphicData.drawSize;
            Vector3 center = DrawPos + def.graphicData.drawOffset;
            center.y += Altitudes.AltInc * StandardGraphics.ClothLift;
            Printer_Plane.PrintPlane(layer, center, size, StandardGraphics.ClothMat(clothColor));
            Texture2D icon = EmblemTexture;
            if (icon == null) return;
            Vector3 at = center + new Vector3(StandardGraphics.ClothCenter.x * size.x,
                Altitudes.AltInc * (StandardGraphics.IconLift - StandardGraphics.ClothLift), StandardGraphics.ClothCenter.y * size.y);
            float s = StandardGraphics.IconSizeFraction * size.x;
            Printer_Plane.PrintPlane(layer, at, new Vector2(s, s), StandardGraphics.IconMat(icon, clothColor));
        }

        public Thought_Memory GiveObservedThought(Pawn observer)
        {
            if (!Spawned || !AftermathMod.Settings.trophyStandards || !observer.IsFreeNonSlaveColonist) return null;
            ThoughtDef def = veterans.Contains(observer.thingIDNumber)
                ? AftermathDefOf.Aftermath_TrophyStandardVeteran
                : AftermathDefOf.Aftermath_TrophyStandard;
            var thought = (Thought_MemoryObservation)ThoughtMaker.MakeThought(def);
            thought.Target = this;
            return thought;
        }

        public HistoryEventDef GiveObservedHistoryEvent(Pawn observer) => null;

        public override string GetInspectString()
        {
            var sb = new StringBuilder(base.GetInspectString());
            void Line(string s) { if (sb.Length > 0) sb.AppendLine(); sb.Append(s); }
            Line("Aftermath_StandardOf".Translate(factionName));
            Line("Aftermath_StandardTakenFrom".Translate(leaderName, AftermathUtil.Date(tick, longLat)));
            Line("Aftermath_StandardBroughtDownBy".Translate(killerName));
            if (veterans.Count > 0) Line("Aftermath_StandardVeterans".Translate(veterans.Count));
            return sb.ToString();
        }
    }
}
