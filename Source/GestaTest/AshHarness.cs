using System.Linq;
using Gesta;
using RimWorld;
using Verse;

namespace GestaTest
{
    /// <summary>Ash Silhouettes through real kills. Runs at tick 1600, after the poppies harness.</summary>
    public class AshHarness : GameComponent
    {
        bool active, done;
        int ticks;

        public AshHarness(Game game) { }

        public override void FinalizeInit() { active = Kit.HarnessArmed; }

        public override void GameComponentTick()
        {
            if (!active || done || ++ticks != 1600) return;
            done = true;
            try { Run(); }
            catch (System.Exception ex) { Kit.Check(false, "ash harness threw: " + ex); }
            Kit.Summary("Gesta/Ash");
        }

        static AshSilhouette MarkAt(Map map, IntVec3 c) => c.GetThingList(map).OfType<AshSilhouette>().FirstOrDefault();

        void Run()
        {
            Map map = Find.CurrentMap;
            Pawn witness = map.mapPawns.FreeColonistsSpawned.First(p => !p.Downed);
            IntVec3 root = witness.Position;

            Pawn h1 = Kit.SpawnHostile(map, root + new IntVec3(10, 0, 4));
            IntVec3 c1 = h1.Position;
            Kit.KillBy(h1, witness, DamageDefOf.Bomb);
            AshSilhouette m1 = MarkAt(map, c1);
            Kit.Check(m1 != null, "a death by explosion leaves a silhouette on the floor");
            Kit.Check(m1 != null && m1.png != null && m1.png.Length > 300, $"the silhouette holds a real snapshot ({m1?.png?.Length ?? 0} bytes)");
            Kit.Check(m1 != null && m1.Material != null, "its texture decodes into a material");
            Kit.Check(m1 != null && m1.GetInspectString().Contains(m1.pawnName) && !m1.byFire, $"inspect text names them ({m1?.GetInspectString()})");

            Pawn h2 = Kit.SpawnHostile(map, root + new IntVec3(-10, 0, 4));
            IntVec3 c2 = h2.Position;
            Kit.KillBy(h2, witness, DamageDefOf.Bullet);
            Kit.Check(MarkAt(map, c2) == null, "a bullet leaves no silhouette");

            Pawn h3 = Kit.SpawnHostile(map, root + new IntVec3(0, 0, -10));
            IntVec3 c3 = h3.Position;
            h3.TryAttachFire(0.5f, null);
            Kit.Check(h3.IsBurning(), "setup: pawn is on fire");
            Kit.KillBy(h3, witness, DamageDefOf.Cut);
            AshSilhouette m3 = MarkAt(map, c3);
            Kit.Check(m3 != null && m3.byFire, "dying while on fire leaves a burned silhouette");
            foreach (Thing f in c3.GetThingList(map).OfType<Fire>().ToList()) f.Destroy();

            if (m1 != null)
            {
                map.terrainGrid.SetTerrain(c1, TerrainDefOf.Concrete);
                m1.TickLong();
                Kit.Check(m1.Destroyed, "rebuilding the floor removes the silhouette");
            }
        }
    }
}
