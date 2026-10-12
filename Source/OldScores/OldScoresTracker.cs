using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace OldScores
{
    /// <summary>
    /// All saved state of the mod. The three features live in partial files
    /// (Nicknames.cs, Spared.cs, Beasts.cs). See DESIGN.md.
    /// </summary>
    public partial class OldScoresTracker : GameComponent
    {
        List<NicknameRecord> nicknames = new List<NicknameRecord>();
        List<SparedRecord> spared = new List<SparedRecord>();
        List<BeastRecord> beasts = new List<BeastRecord>();

        static OldScoresTracker instance;
        public static OldScoresTracker Instance => instance;

        public OldScoresTracker(Game game) { instance = this; }

        public IReadOnlyList<NicknameRecord> Nicknames => nicknames;
        public IReadOnlyList<SparedRecord> Spared => spared;
        public IReadOnlyList<BeastRecord> Beasts => beasts;

        /// <summary>Raid Echoes overlay toggle (play-settings icon row), saved per game like vanilla's overlays.</summary>
        public bool showEchoes;

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % OldScoresTuning.CleanupInterval != 0) return;
            CleanupNicknames();
            CleanupSpared();
            CleanupBeasts();
        }

        public void Notify_PawnKilled(Pawn victim, KillContext ctx)
        {
            Notify_Kill(victim, ctx);
            Notify_SparedDied(victim);
            Notify_BeastDied(victim, ctx);
            if (ctx.map != null) OldScoresMapComponent.For(ctx.map)?.Notify_PawnDied(victim, ctx.position);
        }

        public void Notify_PawnExitedMap(Pawn p, Map fromMap)
        {
            if (p.Spawned || p.teleporting || p.IsCaravanMember()) return;   // did not leave, is being teleported, or left with a player caravan
            Notify_SparedExitedMap(p);
            Notify_BeastExitedMap(p, fromMap);
        }

        /// <summary>Extra inspect line for nicknamed colonists, returning spared pawns and returning beasts.</summary>
        public string InspectLine(Pawn p)
        {
            if (p.RaceProps.Humanlike)
            {
                if (p.Faction == Faction.OfPlayer)
                {
                    string nicks = nicknames.Count > 0 ? NicknamesLine(p) : null;
                    return nicks == null ? null : "OldScores_Nickname_Inspect".Translate(nicks).Resolve();
                }
                SparedRecord s = spared.Count > 0 ? GetSpared(p) : null;
                if (s != null && s.stage == SparedStage.Returned && s.rescuer != null)
                    return "OldScores_Spared_Inspect".Translate(s.rescuer.LabelShort).Resolve();
                return null;
            }
            BeastRecord b = beasts.Count > 0 ? GetBeast(p) : null;
            if (b != null && b.stage == BeastStage.Returned) return BeastLabel(b);
            return null;
        }

        /// <summary>True for pawns the mod keeps alive off-map: never redress them into a new pawn.</summary>
        public bool IsHolding(Pawn p)
        {
            for (int i = 0; i < spared.Count; i++)
                if (spared[i].pawn == p && spared[i].stage != SparedStage.Captured) return true;
            for (int i = 0; i < beasts.Count; i++)
                if (beasts[i].beast == p && beasts[i].stage != BeastStage.Marked) return true;
            return false;
        }

        static void Pin(Pawn p)
        {
            if (p != null && !p.Dead && p.IsWorldPawn()) Find.WorldPawns.ForcefullyKeptPawns.Add(p);
        }

        static void Unpin(Pawn p)
        {
            if (p != null) Find.WorldPawns.ForcefullyKeptPawns.Remove(p);
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref nicknames, "nicknames", LookMode.Deep);
            Scribe_Collections.Look(ref spared, "spared", LookMode.Deep);
            Scribe_Collections.Look(ref beasts, "beasts", LookMode.Deep);
            Scribe_Values.Look(ref showEchoes, "showEchoes");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                nicknames ??= new List<NicknameRecord>();
                spared ??= new List<SparedRecord>();
                beasts ??= new List<BeastRecord>();
                // references to pawns/factions that no longer exist load as null
                nicknames.RemoveAll(r => r == null || r.faction == null);
                // a spared pawn whose captor did not survive the load stays pinned unless unpinned here
                foreach (SparedRecord r in spared)
                    if (r != null && r.pawn != null && r.rescuer == null && Find.World?.worldPawns != null) Unpin(r.pawn);
                spared.RemoveAll(r => r == null || r.pawn == null || r.rescuer == null);
                beasts.RemoveAll(r => r == null || r.beast == null);
            }
        }
    }

    /// <summary>What a death looked like while the victim was still alive (read in the Kill prefix).</summary>
    public class KillContext
    {
        public Map map;
        public IntVec3 position;
        public Pawn killer;
        public Faction victimFaction;
        public bool victimHostile;
        public bool victimDowned;
        public bool victimHumanlike;
        public bool victimWasLeader;
    }

    public class NicknameRecord : IExposable
    {
        public int colonistId;
        public string colonistName;
        public Faction faction;
        public int kills;
        public string nickname;
        public int namedTickAbs = -1;

        public bool Named => !nickname.NullOrEmpty();

        public void ExposeData()
        {
            Scribe_Values.Look(ref colonistId, "colonistId");
            Scribe_Values.Look(ref colonistName, "colonistName");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Values.Look(ref kills, "kills");
            Scribe_Values.Look(ref nickname, "nickname");
            Scribe_Values.Look(ref namedTickAbs, "namedTickAbs", -1);
        }
    }

    public enum SparedStage { Captured, Released, Away, Returned }

    public class SparedRecord : IExposable
    {
        public Pawn pawn;
        public Pawn rescuer;
        public SparedStage stage;
        public int capturedTick;
        public int leftTick = -1;
        public int notHostileSinceTick = -1;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref notHostileSinceTick, "notHostileSinceTick", -1);
            Scribe_References.Look(ref rescuer, "rescuer");
            Scribe_Values.Look(ref stage, "stage");
            Scribe_Values.Look(ref capturedTick, "capturedTick");
            Scribe_Values.Look(ref leftTick, "leftTick", -1);
        }
    }

    public enum BeastStage { Marked, Away, Returned }

    public class BeastRecord : IExposable
    {
        public Pawn beast;
        public string nickname;
        public string kindLabel;
        public List<string> victimNames = new List<string>();
        public List<int> victimIds = new List<int>();
        public int firstBloodTickAbs;
        public Vector2 firstBloodLongLat;
        public BeastStage stage;
        public int fireTick = -1;
        public int returns;
        public float damageDealt;
        public int markMapId = -1;
        public int markedTick = -1;

        public string VictimList => victimNames.ToCommaList(useAnd: true);

        public void ExposeData()
        {
            Scribe_References.Look(ref beast, "beast");
            Scribe_Values.Look(ref nickname, "nickname");
            Scribe_Values.Look(ref kindLabel, "kindLabel");
            Scribe_Collections.Look(ref victimNames, "victimNames", LookMode.Value);
            Scribe_Collections.Look(ref victimIds, "victimIds", LookMode.Value);
            Scribe_Values.Look(ref firstBloodTickAbs, "firstBloodTickAbs");
            Scribe_Values.Look(ref firstBloodLongLat, "firstBloodLongLat");
            Scribe_Values.Look(ref stage, "stage");
            Scribe_Values.Look(ref fireTick, "fireTick", -1);
            Scribe_Values.Look(ref returns, "returns");
            Scribe_Values.Look(ref damageDealt, "damageDealt");
            Scribe_Values.Look(ref markMapId, "markMapId", -1);
            Scribe_Values.Look(ref markedTick, "markedTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                victimNames ??= new List<string>();
                victimIds ??= new List<int>();
            }
        }
    }
}
