// Forge test kit (docs/forge/kit/ForgeTestKit.cs), adapted for the RimMcp governor harness.
// Every call below was checked against decomp/1.6.4871.
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimMcpGovTest
{
    public static class Kit
    {
        public static int Passed, Failed;

        /// <summary>Armed only by `rw launch --ticks N` (arg is -rwtools-ticks=N).</summary>
        public static bool HarnessArmed => GenCommandLine.TryGetCommandLineArg("rwtools-ticks", out _);

        /// <summary>RIMMCP_HARNESS=steward skips the governor fights (25 of the 30 minutes).</summary>
        public static bool StewardOnly => System.Environment.GetEnvironmentVariable("RIMMCP_HARNESS") == "steward";

        public static void Check(bool ok, string what)
        {
            if (ok) { Passed++; Log.Message("[Test] PASS " + what); }
            else { Failed++; Log.Error("[Test] FAIL " + what); }
        }

        public static void Info(string what) => Log.Message("[Test] INFO " + what);

        /// <summary>Verse/HealthUtility.cs:245.</summary>
        public static void Down(Pawn p) => HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);

        public static void Teleport(Pawn p, IntVec3 cell)
        {
            p.Position = cell;
            p.Notify_Teleported();
            p.jobs?.StopAll();
        }

        public static void Summary(string mod)
        {
            if (Failed == 0) Log.Message($"[Test] DONE {mod}: {Passed} passed");
            else Log.Error($"[Test] DONE {mod}: {Passed} passed, {Failed} FAILED");
        }
    }
}
