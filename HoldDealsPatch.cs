using System.Reflection;
using HarmonyLib;
using Il2CppScheduleOne.Economy;

namespace DealerSelfSupply
{
    // Your orders come first: while a dealer is off doing something you sent them to do (drop
    // the cash, fetch product), the game doesn't start them walking to a deal. Its own check for
    // that runs every tick; this skips it until they're done, then deals carry on as normal.
    //
    // Applied by hand at startup rather than with an attribute: the method is looked up by name
    // when the mod loads, so a game version that renamed it costs this one nicety (one log line;
    // the trip code also puts a deal that starts anyway back on hold) and not an error on load.
    internal static class HoldDealsPatch
    {
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            MethodInfo target = typeof(Dealer).GetMethod("CheckAttendStart", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (target == null)
            {
                Core.Log.Warning("This game version has no Dealer.CheckAttendStart - orders won't hold deals back (a deal that starts anyway is paused by the trip code).");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(HoldDealsPatch).GetMethod(nameof(Prefix), BindingFlags.NonPublic | BindingFlags.Static)));
            Core.Log.Msg("Orders hold deals back: patched Dealer.CheckAttendStart.");
        }

        private static bool Prefix(Dealer __instance)
        {
            try { return !SupplyRuns.HoldsDeals(__instance); }
            catch { return true; }
        }
    }
}
