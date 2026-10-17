using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using UnlikelyHero;
using Verse;

namespace UnlikelyHeroTest
{
    /// <summary>
    /// Visual check, armed by mods/UnlikelyHeroTest/showcase.flag (never under --ticks). Turns a colonist
    /// into a non-combatant, has them kill a raider next to them at normal speed and captures the column of
    /// light twice into D:/rimworld/logs/showcase/unlikelyhero.
    /// </summary>
    public class HeroShowcase : GameComponent
    {
        public const string ShotDir = "D:/rimworld/logs/showcase/unlikelyhero";
        bool active, started;
        int ticks, killTick = -1;
        bool shot1, shot2;
        string pending;
        int frames;

        public HeroShowcase(Game game) { }

        public override void FinalizeInit()
        {
            if (Kit.HarnessArmed) return;
            ModContentPack me = LoadedModManager.RunningModsListForReading.FirstOrDefault(m => m.PackageId == "nahuel.unlikelyherotest");
            active = me != null && File.Exists(Path.Combine(me.RootDir, "showcase.flag"));
            if (active) Directory.CreateDirectory(ShotDir);
        }

        public override void GameComponentTick()
        {
            if (!active || pending != null) return;
            ticks++;
            if (!started && ticks == 180) Begin();
            if (killTick < 0) return;
            int since = ticks - killTick;
            if (!shot1 && since == 25) { shot1 = true; Shoot("1-beam"); }
            else if (!shot2 && since == 90) { shot2 = true; Shoot("2-fading"); }
        }

        public override void GameComponentUpdate()
        {
            if (pending == null) return;
            frames++;
            if (frames == 10)
            {
                string path = Path.Combine(ShotDir, $"unlikelyhero-{pending}.png");
                ScreenCapture.CaptureScreenshot(path);
                Log.Message($"[Showcase] captured {path}");
            }
            else if (frames >= 40)
            {
                pending = null;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            }
        }

        void Shoot(string name)
        {
            Find.TickManager.Pause();
            pending = name;
            frames = 0;
        }

        void Begin()
        {
            started = true;
            Map map = Find.CurrentMap;
            Pawn hero = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (hero == null) { Log.Error("[Showcase] no colonist"); return; }
            foreach (Trait t in hero.story.traits.allTraits.ToList())
                if (HeroDefOf.UnlikelyHero_Config.violentTraits.Contains(t.def)) hero.story.traits.RemoveTrait(t);
            hero.skills.GetSkill(SkillDefOf.Shooting).Level = 2;
            hero.skills.GetSkill(SkillDefOf.Melee).Level = 2;
            hero.jobs.StopAll();

            IntVec3 cell = CellFinder.StandableCellNear(hero.Position + new IntVec3(2, 0, 0), map, 3f);
            Pawn raider = PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, Faction.OfPirates ?? Find.FactionManager.RandomEnemyFaction(allowNonHumanlike: false));
            GenSpawn.Spawn(raider, cell.IsValid ? cell : hero.Position, map);

            Find.CameraDriver.SetRootPosAndSize(hero.DrawPos + new Vector3(0f, 0f, 1.5f), 11f);
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
            Find.Selector.ClearSelection();
            Kit.KillBy(raider, hero, DamageDefOf.Bullet);
            killTick = ticks;
            Log.Message($"[Showcase] {hero.LabelShort} killed {raider.LabelShort}; moments {HeroMoment.Triggered}, rejection {HeroRules.LastRejection ?? "none"}; hour {GenLocalDate.HourOfDay(map)}");
        }
    }
}
