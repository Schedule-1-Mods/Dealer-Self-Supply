using System.Linq;
using System.Reflection;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.AI;

namespace DealerSelfSupply
{
    // Game calls whose form differs between game versions. The mod is built once and has to run
    // on all of them (0.4.6, 0.4.7, ...), and a call to something the running game doesn't have
    // fails the whole method that makes it - so everything that changed is looked up BY NAME at
    // runtime, once, and used through here. Nothing in this file refers to a changed member
    // directly, so it loads on every version; what's missing falls back to Unity's own calls.
    //
    //                       0.4.6                          0.4.7
    //   walking agent       NPCMovement.Agent              (moved into a private field) -> the NavMeshAgent component
    //   has a destination   NPCMovement.HasDestination     _hasDestination
    //   paused / resume     IsPaused, ResumeMovement()     a zero-speed entry on the speed controller; the game un-pauses itself
    //   speed multiplier    NPCMovement.MoveSpeedMultiplier   SpeedController.SetSpeedMultiplier(v)
    //   warp                Warp(Vector3)                  Warp(Vector3, Quaternion)
    internal static class Compat
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        // --- the walking agent: it's a Unity component, the same in every version ---

        private static readonly Dictionary<IntPtr, NavMeshAgent> _agents = new Dictionary<IntPtr, NavMeshAgent>();

        public static NavMeshAgent AgentOf(Dealer d)
        {
            if (d == null) return null;
            if (_agents.TryGetValue(d.Pointer, out NavMeshAgent known) && known != null) return known;
            NavMeshAgent found = null;
            try { found = d.Movement != null ? d.Movement.GetComponent<NavMeshAgent>() : null; } catch { }
            if (found == null) { try { found = d.GetComponentInChildren<NavMeshAgent>(true); } catch { } }
            if (found != null) _agents[d.Pointer] = found;
            return found;
        }

        // --- members that moved: found by name, once (MovementBinding) ---

        private static MovementBinding _binding;
        private static MovementBinding B => _binding ??= new MovementBinding(typeof(NPCMovement));

        // One line for the log: which form of each call this game version has.
        public static string Describe() => B.Report;

        public static bool HasDestination(Dealer d)
        {
            try { bool? has = B.HasDestination(d.Movement); if (has.HasValue) return has.Value; } catch { }
            var agent = AgentOf(d);
            return agent != null && (agent.hasPath || agent.pathPending);
        }

        public static bool IsPaused(Dealer d)
        {
            try { bool? paused = B.IsPaused(d.Movement); if (paused.HasValue) return paused.Value; } catch { }
            var agent = AgentOf(d);
            return agent != null && agent.isOnNavMesh && agent.isStopped;
        }

        // A paused walker never sets off: lift it (the game's own call where there is one).
        public static void Resume(Dealer d)
        {
            try { if (B.Resume(d.Movement)) return; } catch { }
            var agent = AgentOf(d);
            if (agent != null && agent.isOnNavMesh) agent.isStopped = false;
        }

        public static float GetSpeed(Dealer d, float fallback = 1f)
        {
            try { float? v = B.GetSpeed(d.Movement); if (v.HasValue) return v.Value; } catch { }
            return fallback;
        }

        public static void SetSpeed(Dealer d, float value)
        {
            try { B.SetSpeed(d.Movement, value); } catch { }
        }

        // --- warp: Warp(Vector3) on 0.4.6, Warp(Vector3, Quaternion) on 0.4.7 ---

        private static MethodInfo _warp;

        public static void Warp(NPCMovement movement, Vector3 position)
        {
            if (movement == null) return;
            if (_warp == null)
                _warp = typeof(NPCMovement).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "Warp")
                    .Select(m => (m, p: m.GetParameters()))
                    .Where(x => x.p.Length >= 1 && x.p[0].ParameterType == typeof(Vector3))
                    .OrderBy(x => x.p.Length)
                    .Select(x => x.m)
                    .FirstOrDefault();
            if (_warp == null)
            {
                // Last resort: put them there directly.
                movement.transform.position = position;
                return;
            }
            var ps = _warp.GetParameters();
            var args = new object[ps.Length];
            args[0] = position;
            for (int i = 1; i < ps.Length; i++)
                args[i] = ps[i].ParameterType == typeof(Quaternion) ? movement.transform.rotation
                        : ps[i].HasDefaultValue ? ps[i].DefaultValue
                        : ps[i].ParameterType.IsValueType ? System.Activator.CreateInstance(ps[i].ParameterType) : null;
            _warp.Invoke(movement, args);
        }

        public static void Reset() => _agents.Clear();
    }
}
