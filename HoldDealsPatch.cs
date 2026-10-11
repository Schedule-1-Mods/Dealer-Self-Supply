using HarmonyLib;
using Il2CppScheduleOne.Economy;

namespace DealerSelfSupply
{
    // Your orders come first: while a dealer is off doing something you sent them to do (drop
    // the cash, fetch product), the game doesn't start them walking to a deal. Its own check for
    // that runs every tick; this skips it until they're done, then deals carry on as normal.
    [HarmonyPatch(typeof(Dealer), nameof(Dealer.CheckAttendStart))]
    internal static class HoldDealsPatch
    {
        private static bool Prefix(Dealer __instance)
        {
            try { return !SupplyRuns.HoldsDeals(__instance); }
            catch { return true; }
        }
    }
}
