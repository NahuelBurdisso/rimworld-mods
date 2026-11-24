# RimWorld mods

My RimWorld 1.6 mods, one folder per mod. Most are small story mods: each one adds a single
moment the colony can notice and remember (a rainbow after the rain, a dog that senses a raid,
a mechanoid standing vigil over its dead mechanitor) rather than a new system to manage.

Each mod was developed as its own repository and merged here with its history, so `git log -- <Mod>/`
shows how it was built.

## Mods

| Folder | Name | What it does | Tech |
|---|---|---|---|
| [`Aftermath`](Aftermath) | Aftermath | What a battle leaves behind, the morning after. | C# + Harmony |
| [`AfterTheRain`](AfterTheRain) | After the Rain | Sometimes, when the rain stops and the sun is low, a rainbow comes out. | C# + Harmony |
| [`ColonyTemplate`](ColonyTemplate) | Colony Template | Start every colony your way. | C# + Harmony |
| [`Duelo`](Duelo) | Pack del Duelo | Grief made visible. | C# + Harmony |
| [`Gesta`](Gesta) | Gesta | Combat that becomes legend. | C# + Harmony |
| [`LightForTheAbsent`](LightForTheAbsent) | A Light for the Absent | Someone always leaves a light on. | C# + Harmony |
| [`MachineVigil`](MachineVigil) | Machine Vigil | When a mechanitor dies, their mechanoids do not simply wander off. | C# + Harmony |
| [`OldScores`](OldScores) | Old Scores | Your enemies remember you. So do the beasts. | C# + Harmony |
| [`Omens`](Omens) | Omens | The world warns you before the battle, and remembers after it. | C# + Harmony |
| [`OverTheBody`](OverTheBody) | Over the Body | When a colonist goes down in a fight, their bonded animal does not wander off. | C# + Harmony |
| [`PackHowls`](PackHowls) | The Pack Howls | When a colonist or a bonded animal falls in battle at your home, the first quiet night after the fight your dogs and wolves stop what they are doing, sit facing the place where the fallen died, and howl. Faint rings rise into the dark. A few seconds later, any wild wolves on the map answer from afar. | C# + Harmony |
| [`QuickBuild`](QuickBuild) | Quick Build | Builds faster without cheating materials. | C# + Harmony |
| [`ReactorSlag`](ReactorSlag) | Reactor Slag | When a heavy mechanoid falls, its core spills. | C# + Harmony |
| [`StarWarsPackFixes`](StarWarsPackFixes) | Star Wars Pack Fixes | Local fixes for the Ultimate Star Wars Collection play profile: provides the patch class Alien Worlds - Tidally Locked's 1.6 build is missing, strips its 1.7-only def field, and turns the nameless ResolveIcon NullReferenceException into a warning naming the def. | C# + Harmony |
| [`TheDogKnows`](TheDogKnows) | The Dog Knows | Your dogs and cats sense what your colonists cannot see. | C# + Harmony |
| [`UnlikelyHero`](UnlikelyHero) | Unlikely Hero | Sometimes the one who saves the day is the last person anyone expected. | C# + Harmony |
| [`WindowWatchers`](WindowWatchers) | Window Watchers | The ones who can't fight still watch. | C# + Harmony |

## Test harnesses

Every C# mod has a companion `*Test` mod. It is loaded only in an isolated test profile, drives the
game for a fixed number of ticks, forces the situation the mod reacts to, asserts on the result and
quits, so each mod is verified inside the real game instead of against mocks.

| Folder | Tests |
|---|---|
| [`AftermathTest`](AftermathTest) | Aftermath |
| [`AfterTheRainTest`](AfterTheRainTest) | AfterTheRain |
| [`ColonyTemplateTest`](ColonyTemplateTest) | ColonyTemplate |
| [`DueloTest`](DueloTest) | Duelo |
| [`GestaTest`](GestaTest) | Gesta |
| [`LightForTheAbsentTest`](LightForTheAbsentTest) | LightForTheAbsent |
| [`MachineVigilTest`](MachineVigilTest) | MachineVigil |
| [`OldScoresTest`](OldScoresTest) | OldScores |
| [`OmensTest`](OmensTest) | Omens |
| [`OverTheBodyTest`](OverTheBodyTest) | OverTheBody |
| [`PackHowlsTest`](PackHowlsTest) | PackHowls |
| [`ReactorSlagTest`](ReactorSlagTest) | ReactorSlag |
| [`RimMcpGovTest`](RimMcpGovTest) | RimMcpGov |
| [`TheDogKnowsTest`](TheDogKnowsTest) | TheDogKnows |
| [`UnlikelyHeroTest`](UnlikelyHeroTest) | UnlikelyHero |
| [`WindowWatchersTest`](WindowWatchersTest) | WindowWatchers |

## Layout of a mod

```
<Mod>/
├── About/About.xml      metadata, dependencies
├── 1.6/Defs, Patches    XML defs and patches
├── 1.6/Assemblies       built DLL
├── Languages/           translations
└── Source/              C# project (Harmony patches, game components)
```

## Related

- [RimMcp](https://github.com/NahuelBurdisso/RimMcp): an MCP bridge that lets an AI assistant read and play a running game.
- [rimworld-modding-workspace](https://github.com/NahuelBurdisso/rimworld-modding-workspace): the CLI and tooling used to scaffold, build, validate and test these mods.

## Requirements

RimWorld 1.6 and [Harmony](https://github.com/pardeike/HarmonyRimWorld) for the C# mods. Copy a mod folder into
`RimWorld/Mods` to use it.
