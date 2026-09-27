using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.UI;
using UnityEngine;
using UnityEngine.AI;

namespace DealerSupplyRun
{
    // Sends dealers on runs to their shelves: drop their earnings at the EARNINGS shelf, grab
    // product from the supply shelf, or both in one trip. A run takes the dealer off their
    // schedule, walks them to each shelf in turn, then hands them back to the schedule, which
    // walks them home again. Anything the game wants the dealer for (a deal, a fight, getting
    // knocked out) is a behaviour, and an active behaviour cancels the run.
    internal static class SupplyRuns
    {

        private const float ArriveDistance = 1.6f;
        private const float StopSeconds = 2.5f;

        private class Stop
        {
            public ShelfRole Role;
            public PlaceableStorageEntity Shelf;  // null for the walk home
            public bool Home;                     // walk back home and go inside
            public int DoorIndex;
            public bool Crossing;                 // a walkway handover spot: switch walkable area here
            public bool ToIsland;
            public Vector3 ShelfPoint;   // where the dealer stands to use the shelf
            public Vector3 WalkPoint;    // where they're walking: ShelfPoint, or as close as the navmesh gets
            public float Started;
            public float ActAt = -1f;    // set once they've arrived
        }

        private class Run
        {
            public Dealer Dealer;
            public readonly Queue<Stop> Stops = new Queue<Stop>();
            public Stop Current;
            public float NextRepath;
            public float OldSpeed = 1f;
            public bool OnIsland;   // walking on Greenhouse Island's walkable area (see Crossing)
        }

        private static readonly Dictionary<IntPtr, Run> _runs = new Dictionary<IntPtr, Run>();
        private static readonly Dictionary<IntPtr, Dealer> _awaitingSchedule = new Dictionary<IntPtr, Dealer>();
        private static readonly Dictionary<IntPtr, float> _cooldownUntil = new Dictionary<IntPtr, float>();
        private static readonly Dictionary<string, PlaceableStorageEntity> _shelves = new Dictionary<string, PlaceableStorageEntity>();
        private static readonly HashSet<IntPtr> _wasDealing = new HashSet<IntPtr>();
        private static readonly HashSet<IntPtr> _homeward = new HashSet<IntPtr>(); // finished dealing, cash to drop off
        private static readonly Dictionary<IntPtr, string> _lastWhy = new Dictionary<IntPtr, string>();
        private static readonly Dictionary<IntPtr, ShelfRole?> _sendWhenFree = new Dictionary<IntPtr, ShelfRole?>(); // null: both shelves
        private static float _nextCheck;
        private static float _nextStep;

        public static void Reset()
        {
            _runs.Clear();
            _awaitingSchedule.Clear();
            _cooldownUntil.Clear();
            _shelves.Clear();
            _wasDealing.Clear();
            _homeward.Clear();
            _lastWhy.Clear();
            _sendWhenFree.Clear();
            _nextCheck = Time.time + 15f; // let the save finish loading
        }

        public static bool IsHost
        {
            get { try { return InstanceFinder.IsServer; } catch { return false; } }
        }

        public static bool IsOnRun(Dealer d) => d != null && _runs.ContainsKey(d.Pointer);

        // What they're off doing right now, e.g. "Taking cash to EARNINGS"; null when not on a trip.
        public static string RunDescription(Dealer d)
        {
            if (d == null || !_runs.TryGetValue(d.Pointer, out Run run) || run.Current == null) return null;
            // At a walkway's handover spot, describe where they're headed after it.
            Stop stop = run.Current.Crossing ? run.Stops.FirstOrDefault() : run.Current;
            if (stop == null) return "Walking back from the island";
            if (stop.Home) return "Walking back home";
            if (stop.Shelf == null) return null;
            string shelf = ShelfLabel(stop.Shelf);
            return $"On {Speech.Her(d)} way to \"{shelf}\"" + (stop.Role == ShelfRole.Earnings ? " with cash" : "");
        }

        // A dealer who isn't off to a shelf: out dealing with what they've got, or sold out.
        public static string IdleDescription(Dealer d)
        {
            int items = ProductCarried(d);
            return items == 0 ? "Sold out, needs product" : $"Dealing, {items} items on {Speech.Him(d)}";
        }

        // How many packs of product the dealer has on them (baggies, jars, bricks).
        public static int ProductCarried(Dealer d)
        {
            var slots = d.Inventory?.ItemSlots;
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var item = slots[i]?.ItemInstance;
                if (item != null && item.TryCast<ProductItemInstance>() != null) n += item.Quantity;
            }
            return n;
        }

        // The cash sitting on a shelf.
        public static float CashOn(PlaceableStorageEntity shelf)
        {
            var slots = shelf?.StorageEntity?.ItemSlots;
            if (slots == null) return 0f;
            float total = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                var cash = slots[i]?.ItemInstance?.TryCast<CashInstance>();
                if (cash != null) total += cash.Balance;
            }
            return total;
        }

        public static IEnumerable<Dealer> HiredDealers()
        {
            var all = Dealer.AllPlayerDealers;
            if (all == null) yield break;
            for (int i = 0; i < all.Count; i++)
            {
                Dealer d = all[i];
                if (d != null && d.IsRecruited) yield return d;
            }
        }

        public static string GuidOf(PlaceableStorageEntity shelf) => shelf.GUID.ToString();

        // Every placed storage in the loaded save, by GUID. Shelves keep the names you give them
        // in game (e.g. PRODUCTS, EARNINGS) - the mod never renames them.
        public static Dictionary<string, PlaceableStorageEntity> RefreshShelves()
        {
            _shelves.Clear();
            foreach (var s in UnityEngine.Object.FindObjectsOfType<PlaceableStorageEntity>())
            {
                if (s != null && !s.isGhost) _shelves[GuidOf(s)] = s;
            }
            return _shelves;
        }

        public static PlaceableStorageEntity ShelfFor(Dealer d, ShelfRole role)
        {
            foreach (string guid in Assignments.ShelvesFor(role, d.ID))
            {
                if (_shelves.TryGetValue(guid, out var s) && s != null) return s;
            }
            return null;
        }

        public static void Tick()
        {
            if (!IsHost) return;

            if (Time.time >= _nextStep)
            {
                _nextStep = Time.time + 0.25f;
                StepRuns();
                RestoreSchedules();
                try { WatchDeals(); }
                catch (Exception e) { Core.Log.Warning("Deal watch failed: " + e.Message); }
            }

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + Mathf.Max(2f, Core.CheckInterval.Value);
                try
                {
                    RefreshShelves();
                    Speech.ScrubTexts();
                    CheckDealers();
                }
                catch (Exception e) { Core.Log.Warning("Dealer check failed: " + e.Message); }
            }
        }

        // Sold out? Go get more.
        private static void CheckDealers()
        {
            foreach (Dealer d in HiredDealers())
            {
                if (IsOnRun(d)) continue;
                if (_cooldownUntil.TryGetValue(d.Pointer, out float until) && Time.time < until) continue;
                if (ShelfFor(d, ShelfRole.Supply) != null && NeedsRestock(d)) LogWhy(d, "sold out", TryStart(d, false));
            }
        }

        // Says once in the log why a dealer who should go can't (and again if the reason changes).
        private static void LogWhy(Dealer d, string need, string result)
        {
            if (result == null || result == "on the way")
            {
                _lastWhy.Remove(d.Pointer);
                return;
            }
            if (_lastWhy.TryGetValue(d.Pointer, out string last) && last == result) return;
            _lastWhy[d.Pointer] = result;
            Core.Log.Msg($"{d.FirstName} is {need} but can't go yet: {result}.");
        }

        // Deals are timed by the game: the dealer walks out, hands over, and once the contract is
        // done the schedule walks them home. That's when they swing by EARNINGS with the cash.
        private static void WatchDeals()
        {
            foreach (Dealer d in HiredDealers())
            {
                IntPtr key = d.Pointer;
                bool dealing = d.currentContract != null;
                if (dealing) _wasDealing.Add(key);
                else if (_wasDealing.Remove(key) && d.Cash >= 1f && ShelfFor(d, ShelfRole.Earnings) != null) _homeward.Add(key);

                // Heading home (free again, no new deal lined up): make the drop-off.
                if (_homeward.Contains(key) && !IsOnRun(d) && BusyReason(d) == null) LogWhy(d, "carrying cash", TryStart(d, false));

                // Sent from the talk blocks: off they go once you've finished talking.
                if (_sendWhenFree.TryGetValue(key, out ShelfRole? role) && !IsOnRun(d) && BusyReason(d) == null)
                {
                    _sendWhenFree.Remove(key);
                    string result = TryStart(d, true, role);
                    Speech.Text(d, result == "on the way"
                        ? "Heading over to " + RunDescription(d)?.Replace($"On {Speech.Her(d)} way to ", "") + " now."
                        : result + ".");
                }
            }
        }

        private static bool NeedsRestock(Dealer d)
        {
            var slots = d.Inventory?.ItemSlots;
            if (slots == null || slots.Count == 0) return false;
            int empty = 0, total = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == null || slots[i].IsLocked || slots[i].IsAddLocked) continue;
                total++;
                if (slots[i].ItemInstance == null) empty++;
            }
            if (total == 0) return false;
            if (Core.RestockWhenSoldOut.Value) return empty == total;
            int needed = Mathf.Max(1, Mathf.CeilToInt(total * Mathf.Clamp(Core.RestockPercent.Value, 1, 100) / 100f));
            return empty >= needed;
        }

        // The daily routine is itself a behaviour (ScheduleBehaviour, the lowest priority): a
        // dealer following it is free. Anything else active - a deal, a chat, a fight - is busy.
        public static string BusyBehaviour(Dealer d)
        {
            var active = d.Behaviour?.activeBehaviour;
            if (active == null || active.TryCast<ScheduleBehaviour>() != null) return null;
            return string.IsNullOrEmpty(active.Name) ? "busy" : active.Name;
        }

        // Why a dealer can't go right now, or null if they can.
        public static string BusyReason(Dealer d)
        {
            if (DealerMenu.IsTalkingTo(d)) return "talking to you";
            if (!d.IsConscious) return "knocked out";
            if (d.currentContract != null) return "on a deal";
            if (d.IsInVehicle) return "driving";
            var beh = d.Behaviour;
            if (beh == null || beh.ScheduleManager == null) return "unavailable";
            string busy = BusyBehaviour(d);
            if (busy != null) return "busy (" + busy + ")";
            if (!beh.ScheduleManager.ScheduleEnabled) return "off their routine";
            return null;
        }

        // Starts a run if the dealer is free and has something to do: cash to drop off (on the
        // way home from dealing) and/or product to pick up (once they've sold out). Sent by hand
        // (manual), they do whatever they can - or, with only, just that one shelf. Returns a short
        // status: "on the way", or why not.
        public static string TryStart(Dealer d, bool manual, ShelfRole? only = null)
        {
            if (IsOnRun(d)) return "already on the way";
            string busy = BusyReason(d);
            if (busy != null) return busy;

            var run = new Run { Dealer = d };
            PlaceableStorageEntity earnings = only == ShelfRole.Supply ? null : ShelfFor(d, ShelfRole.Earnings);
            PlaceableStorageEntity supply = only == ShelfRole.Earnings ? null : ShelfFor(d, ShelfRole.Supply);
            if (earnings != null && d.Cash >= 1f && (manual || _homeward.Contains(d.Pointer)))
                run.Stops.Enqueue(new Stop { Role = ShelfRole.Earnings, Shelf = earnings });
            if (supply != null && (manual || NeedsRestock(d)) && CanTakeAny(d, supply))
                run.Stops.Enqueue(new Stop { Role = ShelfRole.Supply, Shelf = supply });

            if (run.Stops.Count == 0)
            {
                if (only != ShelfRole.Supply) _homeward.Remove(d.Pointer);
                if (!manual) _cooldownUntil[d.Pointer] = Time.time + Core.EmptyShelfCooldown.Value;
                if (earnings == null && supply == null) return "No shelf set";
                // Sent for nothing: they head back home.
                if (manual) GoHome(d);
                if (supply != null && !CanTakeAny(d, supply))
                {
                    Core.Log.Msg($"{d.FirstName} can't take anything from the {ShelfLabel(supply)} ({WhyCantTake(d, supply)}). On it: {ShelfContents(supply)}");
                    return WhyCantTake(d, supply) + ", I'll go back home";
                }
                return "No cash to grab, I'll go back home";
            }

            Begin(run);
            return "on the way";
        }

        // Walks the dealer back to their home building and in through the door. False when
        // they're already inside (or have no home).
        public static bool GoHome(Dealer d)
        {
            if (d == null || IsOnRun(d) || d.isInBuilding || d.Home == null) return false;
            var run = new Run { Dealer = d };
            run.Stops.Enqueue(new Stop { Home = true });
            Begin(run);
            return true;
        }

        // Takes the dealer off their routine and sets them walking to the first stop.
        private static void Begin(Run run)
        {
            Dealer d = run.Dealer;
            d.Behaviour.ScheduleManager.DisableSchedule();
            if (d.isInBuilding) d.ExitBuilding("");
            var move = d.Movement;
            try { run.OldSpeed = move.MoveSpeedMultiplier; move.MoveSpeedMultiplier = run.OldSpeed * Mathf.Max(0.5f, Core.SpeedMultiplier.Value); }
            catch { }

            _runs[d.Pointer] = run;
            NextStop(run);
            string where = string.Join(" and then ", run.Stops.Prepend(run.Current).Select(StopLabel));
            Core.Log.Msg($"{d.FirstName} is heading to {where}.");
        }

        private static string StopLabel(Stop s) => s.Home ? "home" : s.Crossing ? "the walkway" : "the " + ShelfLabel(s.Shelf);

        // The shelf's name in quotes, the way dealers mention it: "Dealer Earnings Shelf".
        public static string Quoted(PlaceableStorageEntity shelf) => "\"" + ShelfLabel(shelf) + "\"";

        // The name you gave the shelf in game (its saved name), else what the game calls it.
        public static string ShelfLabel(PlaceableStorageEntity shelf)
        {
            string name = null;
            try { name = shelf.Configuration?.Name?.Value; } catch { }
            if (string.IsNullOrEmpty(name) && shelf.StorageEntity != null) name = shelf.StorageEntity.StorageEntityName;
            return string.IsNullOrEmpty(name) ? shelf.Name : name;
        }

        // Sets the dealer walking to the next stop; false if there are none left.
        private static bool NextStop(Run run)
        {
            if (run.Stops.Count == 0)
            {
                // Done - but not while still out on the island: back over a walkway first.
                if (!run.OnIsland || !Crossings.TryBest(run.Dealer.Movement.FootPosition, HomeOrHere(run.Dealer), out Vector3 exit))
                {
                    run.Current = null;
                    return false;
                }
                run.Stops.Enqueue(new Stop { Crossing = true, ToIsland = false, ShelfPoint = exit });
            }
            Stop stop = run.Current = run.Stops.Dequeue();
            var move = run.Dealer.Movement;
            if (!stop.Crossing)
                stop.ShelfPoint = stop.Home ? HomeDoorPoint(run.Dealer, out stop.DoorIndex) : StandPoint(run.Dealer, stop.Shelf);

            // On the other side of a walkway (Greenhouse Island's shelves, or home from there)?
            // Walk to the handover spot first, then on from there.
            if (!stop.Crossing && Crossings.IsIsland(stop.ShelfPoint) != run.OnIsland
                && Crossings.TryBest(move.FootPosition, stop.ShelfPoint, out Vector3 spot))
            {
                var rest = run.Stops.ToList();
                run.Stops.Clear();
                run.Stops.Enqueue(stop);
                foreach (var s in rest) run.Stops.Enqueue(s);
                stop = run.Current = new Stop { Crossing = true, ToIsland = !run.OnIsland, ShelfPoint = spot };
            }
            stop.WalkPoint = stop.ShelfPoint;
            if (!move.CanGetTo(stop.ShelfPoint, ArriveDistance) && move.GetClosestReachablePoint(stop.ShelfPoint, out Vector3 closest))
                stop.WalkPoint = closest;
            stop.Started = Time.time;
            move.SetDestination(stop.WalkPoint);
            run.NextRepath = Time.time + 3f;
            return true;
        }

        private static Vector3 HomeOrHere(Dealer d)
        {
            try { if (d.Home != null) return d.Home.transform.position; } catch { }
            return d.Movement.FootPosition;
        }

        // Outside the nearest usable door of the dealer's home.
        private static Vector3 HomeDoorPoint(Dealer d, out int doorIndex)
        {
            doorIndex = 0;
            var home = d.Home;
            var door = home.GetClosestDoor(d.Movement.FootPosition, true) ?? (home.Doors.Length > 0 ? home.Doors[0] : null);
            if (door == null) return home.transform.position;
            for (int i = 0; i < home.Doors.Length; i++)
                if (home.Doors[i] != null && home.Doors[i].Pointer == door.Pointer) doorIndex = i;
            Vector3 p = door.AccessPoint != null ? door.AccessPoint.position : door.transform.position;
            return NavMesh.SamplePosition(p, out NavMeshHit hit, 2f, NavMesh.AllAreas) ? hit.position : p;
        }

        private static Vector3 StandPoint(Dealer d, PlaceableStorageEntity shelf)
        {
            Vector3 fallback = shelf.transform.position;
            try
            {
                var points = shelf.AccessPoints;
                if (points != null)
                {
                    Transform first = null;
                    for (int i = 0; i < points.Length; i++)
                    {
                        Transform p = points[i];
                        if (p == null) continue;
                        if (first == null) first = p;
                        if (d.Movement.CanGetTo(p.position, ArriveDistance)) return p.position;
                    }
                    if (first != null) fallback = first.position;
                }
            }
            catch { }
            return NavMesh.SamplePosition(fallback, out NavMeshHit hit, 3f, NavMesh.AllAreas) ? hit.position : fallback;
        }

        private static void StepRuns()
        {
            if (_runs.Count == 0) return;
            foreach (var key in _runs.Keys.ToList())
            {
                Run run = _runs[key];
                try
                {
                    if (!StepRun(run)) _runs.Remove(key);
                }
                catch (Exception e)
                {
                    Core.Log.Warning("Supply run failed: " + e.Message);
                    _runs.Remove(key);
                    EndRun(run);
                }
            }
        }

        // Advances one run; false once it's over.
        private static bool StepRun(Run run)
        {
            Dealer d = run.Dealer;
            if (d == null) return false;

            var beh = d.Behaviour;
            if (!d.IsConscious || d.currentContract != null || BusyBehaviour(d) != null)
            {
                Core.Log.Msg($"{d.FirstName}'s shelf run was interrupted.");
                EndRun(run);
                return false;
            }

            Stop stop = run.Current;
            // Shelf taken off them mid-trip (or given to someone else): don't use it.
            if (stop != null && !stop.Home && !stop.Crossing && stop.Shelf != null && (ShelfFor(d, stop.Role)?.Pointer ?? IntPtr.Zero) != stop.Shelf.Pointer)
                stop = run.Current = null;
            if (stop == null || (!stop.Home && !stop.Crossing && stop.Shelf == null))
            {
                // Shelf picked up or destroyed mid-trip: skip to the next one.
                if (NextStop(run)) return true;
                EndRun(run);
                return false;
            }

            // Something switched the schedule back on - it would steer them home, so take it back.
            if (beh.ScheduleManager.ScheduleEnabled) beh.ScheduleManager.DisableSchedule();

            var move = d.Movement;
            if (stop.ActAt < 0f)
            {
                if (Flat(move.FootPosition, stop.WalkPoint) <= ArriveDistance)
                {
                    // Got as close as walking allows (e.g. a property the navmesh doesn't reach): step in.
                    if (Flat(stop.WalkPoint, stop.ShelfPoint) > ArriveDistance) move.Warp(stop.ShelfPoint);
                    Arrive(run.Dealer, stop);
                }
                else if (Time.time - stop.Started > Core.MaxTravelSeconds.Value)
                {
                    move.Warp(stop.ShelfPoint);
                    Arrive(run.Dealer, stop);
                }
                else if (!move.IsMoving && Time.time >= run.NextRepath)
                {
                    move.SetDestination(stop.WalkPoint);
                    run.NextRepath = Time.time + 3f;
                }
                return true;
            }

            if (Time.time < stop.ActAt) return true;

            if (stop.Crossing)
            {
                Crossings.Cross(d, stop.ShelfPoint, stop.ToIsland);
                run.OnIsland = stop.ToIsland;
            }
            else if (stop.Home)
            {
                // In through the door (networked, like the game's own schedule does it).
                try { d.EnterBuilding(null, d.Home.GUID.ToString(), stop.DoorIndex); }
                catch (Exception e) { Core.Log.Warning($"{d.FirstName} couldn't go inside: {e.Message}"); }
            }
            else if (stop.Role == ShelfRole.Earnings)
            {
                _homeward.Remove(d.Pointer); // done for this trip, whether or not it fit
                DepositCash(d, stop.Shelf);
            }
            else if (TakeProductAndReport(d, stop.Shelf) == 0 && run.Stops.Count == 0)
            {
                // Came for product and the shelf was bare: back home.
                run.Stops.Enqueue(new Stop { Home = true });
            }

            if (NextStop(run)) return true;
            EndRun(run);
            return false;
        }

        private static void Arrive(Dealer d, Stop stop)
        {
            var move = d.Movement;
            move.Stop();
            if (stop.Home || stop.Crossing)
            {
                stop.ActAt = Time.time + (stop.Crossing ? 0f : 0.3f); // straight in / straight on
                return;
            }
            Vector3 face = stop.Shelf.transform.position - move.FootPosition;
            face.y = 0f;
            if (face.sqrMagnitude > 0.01f) move.FaceDirection(face.normalized);
            try { d.SetAnimationTrigger_Networked(null, "GrabItem"); } catch { }
            stop.ActAt = Time.time + StopSeconds;
        }

        private static void EndRun(Run run)
        {
            Dealer d = run.Dealer;
            if (d == null) return;
            try { d.Movement.MoveSpeedMultiplier = run.OldSpeed; } catch { }
            // Cut short out on the island (a deal came up): back to the game's walkable area.
            if (run.OnIsland) Crossings.Cross(d, d.Movement.FootPosition, false, warp: false);
            _awaitingSchedule[d.Pointer] = d;
            RestoreSchedules();
        }

        // Hand dealers back to their schedule - but not while a behaviour (a deal, a fight) is in
        // charge; that behaviour gives the schedule back itself, or we do once it's done.
        private static void RestoreSchedules()
        {
            if (_awaitingSchedule.Count == 0) return;
            foreach (var key in _awaitingSchedule.Keys.ToList())
            {
                Dealer d = _awaitingSchedule[key];
                try
                {
                    if (d == null) { _awaitingSchedule.Remove(key); continue; }
                    // If something else took over (a deal), its end resumes the routine by itself.
                    if (BusyBehaviour(d) == null && !d.Behaviour.ScheduleManager.ScheduleEnabled)
                        d.Behaviour.ScheduleManager.EnableSchedule();
                    _awaitingSchedule.Remove(key);
                }
                catch
                {
                    _awaitingSchedule.Remove(key);
                }
            }
        }

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // --- Earnings ---

        // Puts all the dealer's cash on the shelf: onto an existing cash stack if there is one
        // (cash stacks have no practical limit), otherwise into an empty slot.
        private static void DepositCash(Dealer d, PlaceableStorageEntity shelf)
        {
            float amount = d.Cash;
            var slots = shelf.StorageEntity?.ItemSlots;
            var money = NetworkSingleton<MoneyManager>.Instance;
            if (amount < 1f || slots == null || money == null) return;

            ItemSlot target = null;
            float existing = 0f;
            for (int i = 0; i < slots.Count && target == null; i++)
            {
                ItemSlot s = slots[i];
                var cash = s?.ItemInstance?.TryCast<CashInstance>();
                if (cash != null && !s.IsLocked && !s.IsAddLocked) { target = s; existing = cash.Balance; }
            }
            for (int i = 0; i < slots.Count && target == null; i++)
            {
                ItemSlot s = slots[i];
                if (s != null && s.ItemInstance == null && !s.IsLocked && !s.IsAddLocked) target = s;
            }
            if (target == null)
            {
                Speech.Text(d, "No room for the cash on " + Quoted(shelf) + ", it's full.");
                return;
            }

            target.SetStoredItem(money.GetCashInstance(existing + amount));
            d.ChangeCash(-amount);
            Core.Log.Msg($"{d.FirstName} dropped off {MoneyManager.FormatAmount(amount)} in {ShelfLabel(shelf)}.");
            Speech.Text(d, $"Dropped off {MoneyManager.FormatAmount(amount)} in {Quoted(shelf)}.", red: false);
        }

        // --- Product ---

        private static bool IsPackagedProduct(ItemInstance item)
        {
            var product = item?.TryCast<ProductItemInstance>();
            return product != null && product.AppliedPackaging != null;
        }

        private static bool CanTakeAny(Dealer d, PlaceableStorageEntity shelf)
        {
            var from = shelf.StorageEntity?.ItemSlots;
            if (from == null || d.Inventory == null) return false;
            for (int i = 0; i < from.Count; i++)
            {
                ItemSlot s = from[i];
                if (s == null || s.IsRemovalLocked || !IsPackagedProduct(s.ItemInstance)) continue;
                if (d.Inventory.GetCapacityForItem(s.ItemInstance) > 0) return true;
            }
            return false;
        }

        // Returns how many items they took.
        private static int TakeProductAndReport(Dealer d, PlaceableStorageEntity shelf)
        {
            int taken = TakeProduct(d, shelf);
            if (taken > 0)
            {
                Core.Log.Msg($"{d.FirstName} grabbed {taken} item{(taken == 1 ? "" : "s")} from the {ShelfLabel(shelf)}.");
                Speech.Text(d, $"Grabbed {taken} items from {Quoted(shelf)}. Back to work.", red: false);
            }
            else
            {
                _cooldownUntil[d.Pointer] = Time.time + Core.EmptyShelfCooldown.Value;
                Core.Log.Msg($"{d.FirstName} took nothing from the {ShelfLabel(shelf)}: {WhyCantTake(d, shelf)}. On it: {ShelfContents(shelf)}");
                Speech.Text(d, WhyCantTake(d, shelf) + ", I'll go back home.");
            }
            return taken;
        }

        // Moves as much packaged product as the dealer can carry from the shelf into their
        // inventory, shelf order first. Returns how many items moved.
        private static int TakeProduct(Dealer d, PlaceableStorageEntity shelf)
        {
            var from = shelf.StorageEntity?.ItemSlots;
            var to = d.Inventory?.ItemSlots;
            if (from == null || to == null) return 0;

            int moved = 0;
            for (int i = 0; i < from.Count; i++)
            {
                ItemSlot src = from[i];
                if (src == null || src.IsRemovalLocked || !IsPackagedProduct(src.ItemInstance)) continue;

                // Top up matching stacks first, then fill empty slots.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int j = 0; j < to.Count; j++)
                    {
                        ItemInstance item = src.ItemInstance;
                        if (item == null || item.Quantity <= 0) break;
                        ItemSlot dst = to[j];
                        if (dst == null || dst.IsLocked || dst.IsAddLocked) continue;

                        int n;
                        if (pass == 0)
                        {
                            if (dst.ItemInstance == null || !dst.ItemInstance.CanStackWith(item, false)) continue;
                            n = Math.Min(item.StackLimit - dst.Quantity, item.Quantity);
                            if (n <= 0) continue;
                            dst.ChangeQuantity(n);
                        }
                        else
                        {
                            if (dst.ItemInstance != null) continue;
                            n = Math.Min(item.StackLimit, item.Quantity);
                            if (n <= 0) continue;
                            dst.SetStoredItem(item.GetCopy(n));
                        }
                        src.ChangeQuantity(-n);
                        moved += n;
                    }
                }
            }
            return moved;
        }

        // "Send to shelf now" while talking to them: they go as soon as the chat ends.
        public static void SendWhenFree(Dealer d, ShelfRole role) => _sendWhenFree[d.Pointer] = role;
        public static bool IsWaitingToGo(Dealer d, ShelfRole role) =>
            d != null && _sendWhenFree.TryGetValue(d.Pointer, out ShelfRole? r) && r == role;

        // "Send them to their shelves now" from the shelf panel: off they go, or - if they're on a
        // deal or otherwise busy - they come once they're free. Returns what to show in their row.
        // only: just the cash drop-off, or just the product pick-up (null: both). They text you
        // what they're doing.
        public static string SendNow(Dealer d, ShelfRole? only = null)
        {
            if (IsOnRun(d)) return RunDescription(d);
            if (BusyReason(d) != null)
            {
                _sendWhenFree[d.Pointer] = only;
                string waiting = WaitingDescription(d);
                Speech.Text(d, BusyReason(d) == "on a deal" ? "On a deal right now, I'll head over after." : "Busy right now, I'll head over after.");
                return waiting;
            }
            string result = TryStart(d, true, only);
            if (result == "on the way")
            {
                Speech.Text(d, "On my way to " + RunDescription(d)?.Replace($"On {Speech.Her(d)} way to ", "") + ".");
                return RunDescription(d);
            }
            Speech.Text(d, result + ".");
            return result;
        }

        // For a dealer who's been sent but is still busy: why they haven't left yet.
        public static string WaitingDescription(Dealer d)
        {
            if (d == null || !_sendWhenFree.ContainsKey(d.Pointer)) return null;
            string why = BusyReason(d);
            string she = Speech.She(d).ToLowerInvariant();
            if (why == null) return $"On {Speech.Her(d)} way";
            if (why == "on a deal") return $"On a deal, {she}'ll come after";
            if (why == "talking to you") return $"Talking to you, {she}'ll go after";
            return $"Busy, {she}'ll come after";
        }

        // For the log: every item on a shelf, e.g. "ogkush x20 (WeedInstance, packaged: baggie)".
        public static string ShelfContents(PlaceableStorageEntity shelf)
        {
            var slots = shelf?.StorageEntity?.ItemSlots;
            if (slots == null) return "(no slots)";
            var parts = new List<string>();
            for (int i = 0; i < slots.Count; i++)
            {
                var item = slots[i]?.ItemInstance;
                if (item == null) continue;
                string type = "?";
                try { type = item.GetIl2CppType().Name; } catch { }
                var product = item.TryCast<ProductItemInstance>();
                string pack = product == null ? "not a product" : product.AppliedPackaging != null ? "packaged: " + product.PackagingID : "unpackaged";
                parts.Add($"{item.ID} x{item.Quantity} ({type}, {pack}{(slots[i].IsRemovalLocked ? ", locked" : "")})");
            }
            return parts.Count == 0 ? "nothing" : string.Join("; ", parts);
        }

        // Why a dealer can't take anything from a shelf right now (for texts and the panels).
        public static string WhyCantTake(Dealer d, PlaceableStorageEntity shelf)
        {
            var from = shelf?.StorageEntity?.ItemSlots;
            if (from == null) return "Can't reach " + Quoted(shelf);
            bool anyProduct = false, anyPackaged = false;
            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i]?.ItemInstance;
                if (item == null || item.TryCast<ProductItemInstance>() == null) continue;
                anyProduct = true;
                if (IsPackagedProduct(item)) anyPackaged = true;
            }
            if (!anyProduct) return "Nothing to grab on " + Quoted(shelf);
            if (!anyPackaged) return "Only unpackaged product on " + Quoted(shelf);
            return "Pockets full, no room to carry more";
        }

        // Cash goes to EARNINGS as soon as the dealer is free (e.g. once you finish talking).
        public static void QueueDropOff(Dealer d)
        {
            if (d != null && d.Cash >= 1f) _homeward.Add(d.Pointer);
        }
    }
}
