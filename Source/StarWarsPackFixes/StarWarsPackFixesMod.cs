using System;
using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace StarWarsPackFixes
{
    public class StarWarsPackFixesMod : Mod
    {
        public StarWarsPackFixesMod(ModContentPack content) : base(content)
        {
            // Runs while mods load, before patches and defs - the stub type below is
            // already resolvable when Alien Worlds' Patches/Settings.xml is parsed.
            new Harmony(content.PackageId).PatchAll();
            Log.Message("[Star Wars Pack Fixes] loaded");
        }
    }

    /// <summary>
    /// BuildableDef.ResolveIcon throws a NullReferenceException when a def's graphic
    /// returns no material (missing texture/shader in some Workshop mod). Vanilla then
    /// aborts the rest of that def's icon setup and logs a red error without naming the
    /// def. Swallow it and say which def and mod it was.
    /// </summary>
    [HarmonyPatch(typeof(BuildableDef), "ResolveIcon")]
    public static class SWPF_BuildableDef_ResolveIcon
    {
        static readonly HashSet<string> Reported = new HashSet<string>();

        static Exception Finalizer(Exception __exception, BuildableDef __instance)
        {
            if (__exception == null)
                return null;
            if (Reported.Add(__instance.defName))
                Log.Warning($"[Star Wars Pack Fixes] {__instance.defName} " +
                            $"({__instance.modContentPack?.Name ?? "unknown mod"}) has no material " +
                            "to build an icon from; icon left blank.");
            return null;
        }
    }
}

namespace AlienWorlds.TidallyLocked
{
    /// <summary>
    /// Alien Worlds - Tidally Locked ships Patches/Settings.xml (shared by 1.6 and 1.7)
    /// using this class, but only its 1.7 assembly defines it. Its default setting
    /// (realisticMode = true) applies <c>match</c>, which the XML leaves empty - so the
    /// 1.6 equivalent is a no-op. Name must match the XML's Class attribute exactly.
    /// </summary>
    public class PatchOperationRealisticMode : PatchOperation
    {
        private PatchOperation match = null;
#pragma warning disable CS0414 // filled from XML, intentionally unused (see summary)
        private PatchOperation nomatch = null;
#pragma warning restore CS0414

        protected override bool ApplyWorker(System.Xml.XmlDocument xml)
        {
            return match == null || match.Apply(xml);
        }
    }
}
