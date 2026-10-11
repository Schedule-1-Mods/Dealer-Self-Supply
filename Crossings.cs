using Il2CppScheduleOne.Economy;
using UnityEngine;
using UnityEngine.AI;

namespace DealerSelfSupply
{
    // Walking onto Greenhouse Island (the Greenhouse Island mod) instead of being warped there.
    //
    // The island has its own walkable area (navmesh area 29), over the platform and down every
    // walkway. It doesn't join up with the game's - separate navmeshes never do, and navmesh
    // links are out because the game treats every link as a ladder. But at each walkway's foot on
    // the quay the two overlap, and Greenhouse Island marks that spot ("NavPortal_29"). A dealer
    // walks there on the game's navmesh, switches their area mask to the island's on the spot,
    // and walks on down the walkway - and the same the other way. No visible jump.
    internal static class Crossings
    {
        private const string MarkerName = "NavPortal_29";
        private const int IslandMask = 1 << 29;
        private const int MainMask = ~IslandMask;

        private static readonly List<Transform> _spots = new List<Transform>();
        private static readonly Dictionary<IntPtr, int> _landMasks = new Dictionary<IntPtr, int>();
        private static float _foundAt = -999f;

        private static List<Vector3> Spots()
        {
            if (Time.time - _foundAt > 30f || _spots.Exists(t => t == null))
            {
                _spots.Clear();
                _foundAt = Time.time;
                try
                {
                    var root = GameObject.Find("PelicanKey"); // Greenhouse Island's root object
                    if (root != null)
                        foreach (var t in root.GetComponentsInChildren<Transform>(true))
                            if (t.name == MarkerName) _spots.Add(t);
                }
                catch { }
            }
            return _spots.Where(t => t != null).Select(t => t.position).ToList();
        }

        // A point only the island's walkable area reaches (a shelf in the dome, the walkway).
        public static bool IsIsland(Vector3 p)
        {
            if (Spots().Count == 0) return false;
            return NavMesh.SamplePosition(p, out _, 2f, IslandMask) && !NavMesh.SamplePosition(p, out _, 2f, MainMask);
        }

        // The handover spot that makes for the shortest walk from -> spot -> to - out of the ones
        // the dealer can actually walk to from here (reachable), if any are. onIsland: which
        // side they're on now; the spot is snapped onto that side's walkable area, so the game
        // accepts it as a destination.
        public static bool TryBest(Vector3 from, Vector3 to, bool onIsland, Func<Vector3, bool> reachable, out Vector3 spot)
        {
            spot = default;
            int mask = onIsland ? IslandMask : MainMask;
            float best = float.MaxValue, bestAny = float.MaxValue;
            Vector3 any = default;
            foreach (var raw in Spots())
            {
                Vector3 s = NavMesh.SamplePosition(raw, out NavMeshHit hit, 2f, mask) ? hit.position : raw;
                float d = Vector3.Distance(from, s) + Vector3.Distance(s, to);
                if (d < bestAny) { bestAny = d; any = s; }
                bool ok;
                try { ok = reachable == null || reachable(s); } catch { ok = true; }
                if (ok && d < best) { best = d; spot = s; }
            }
            if (best == float.MaxValue && bestAny < float.MaxValue) spot = any; // none reachable: nearest, the stuck-check takes it from there
            return bestAny < float.MaxValue;
        }

        // Switch the dealer onto the island's walkable area (or back), standing where they are.
        public static void Cross(Dealer d, Vector3 at, bool toIsland, bool warp = true)
        {
            try
            {
                var agent = d.Movement.Agent;
                // Back on land they get exactly the areas the game gave them, not "everything".
                if (toIsland && (agent.areaMask & IslandMask) == 0) _landMasks[d.Pointer] = agent.areaMask;
                int mask = toIsland ? IslandMask : (_landMasks.TryGetValue(d.Pointer, out int land) ? land : MainMask);
                agent.areaMask = mask;
                if (warp && NavMesh.SamplePosition(at, out NavMeshHit hit, 2f, mask))
                    Compat.Warp(d.Movement, hit.position); // same spot - re-attaches them to that navmesh
                Core.Log.Msg($"{d.FirstName} is walking {(toIsland ? "onto" : "off")} Greenhouse Island.");
            }
            catch (Exception e)
            {
                Core.Log.Warning($"{d.FirstName} couldn't cross onto the walkway: {e.Message}");
            }
        }
    }
}
