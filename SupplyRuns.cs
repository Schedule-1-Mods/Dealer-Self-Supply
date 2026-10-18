using Il2CppFishNet;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Product.Packaging;
using Il2CppScheduleOne.UI;
using UnityEngine;
using UnityEngine.AI;

namespace DealerSelfSupply
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
            public bool Door;                     // a building's doorway: step through to StepTo here
            public Vector3 StepTo;
            public Stop For;                      // a door stop: the place it was made for
            public bool DoorTried;                // a door has been tried for this stop (once is enough)
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
            public bool Priority;   // you sent them: deals wait until this is done
            public Vector3 ProgressPos;  // where they were when last seen making progress
            public float ProgressAt;
            public int Unsticks;
            public float RefusedSince; // when the game last dropped their walk order (0: it hasn't)
            public bool Held;          // paused while you talk to them
            public bool Inside;        // stepped into a building through its door (see Door)
            public Stop EnteredFor;    // the place they went in for - never a reason to come back out (see DoorRules)
            public int DoorCrossings;  // doorway crossings this trip (capped)
            public Vector3 InsidePoint, OutsidePoint; // that doorway: its inside and outside ends
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
        private const float StepInterval = 0.25f; // how often trips are moved along

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
            _everSeen.Clear();
            _missingSince.Clear();
            _warnedMissing.Clear();
            _shelvesAt = -999f;
            _nextCheck = Time.time + 15f; // let the save finish loading
        }

        // The host (or you, playing alone): dealers walk and items move here, so everything runs here.
        public static bool IsHost
        {
            get
            {
#if DEVTEST
                if (ForcedJoiner) return false;
#endif
                try { return InstanceFinder.IsServer; } catch { return false; }
            }
        }

        // Someone who joined another player's game. Dealers are the host's to run, so a joiner gets a
        // read-only overview (the phone app) built from what the game itself shares with them.
        public static bool IsJoiner
        {
            get
            {
#if DEVTEST
                if (ForcedJoiner) return true;
#endif
                try { return InstanceFinder.IsClient && !InstanceFinder.IsServer; } catch { return false; }
            }
        }

        // Host or joiner: gets the phone app (the joiner's one is read-only).
        public static bool HasApp => IsHost || IsJoiner;

#if DEVTEST
        // Test only: a file in UserData makes this player act as a joiner, to see that view in single player.
        private static bool ForcedJoiner =>
            File.Exists(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "DealerSelfSupply_forcejoiner.txt"));
#endif

        public static bool IsOnRun(Dealer d) => d != null && _runs.ContainsKey(d.Pointer);

        // What they're off doing right now, e.g. "Taking cash to EARNINGS"; null when not on a trip.
        public static string RunDescription(Dealer d)
        {
            if (d == null || !_runs.TryGetValue(d.Pointer, out Run run) || run.Current == null) return null;
            // At a walkway's handover spot or a doorway, describe where they're headed after it.
            Stop stop = run.Current.Crossing || run.Current.Door ? run.Stops.FirstOrDefault() : run.Current;
            if (stop == null) return run.Current.Door ? "Walking back out" : "Walking back from the island";
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
        // Scanning the scene for storage costs a noticeable slice of a frame, so it's done at
        // most every 5 seconds (the phone app used to do it several times a frame). force: a
        // menu just opened or you changed something - look now.
        private static float _shelvesAt = -999f;
        private static readonly HashSet<string> _everSeen = new HashSet<string>(); // shelves met in this save, this session

        public static Dictionary<string, PlaceableStorageEntity> RefreshShelves(bool force = false)
        {
            if (!force && Time.unscaledTime - _shelvesAt < 5f) return _shelves;
            _shelvesAt = Time.unscaledTime;
            // A shelf that's still alive stays on the list even if the scan doesn't find it (the scan
            // only sees active objects, and a hidden building's shelves aren't): only shelves that
            // have been destroyed - picked up - go.
            foreach (string key in _shelves.Keys.ToList())
            {
                var old = _shelves[key];
                if (old == null || old.isGhost) _shelves.Remove(key);
            }
            foreach (var s in UnityEngine.Object.FindObjectsOfType<PlaceableStorageEntity>())
            {
                if (s == null || s.isGhost) continue;
                string guid = GuidOf(s);
                _shelves[guid] = s;
                _everSeen.Add(guid);
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

            // Each job on its own: if the game has changed something one of them needs, the others
            // keep going, and the log gets one line about it instead of one per frame (see Safe).
            if (Time.time >= _nextStep)
            {
                _nextStep = Time.time + StepInterval;
                Safe.Run("Dealer trips", _stepRuns);
                Safe.Run("Dealer schedules", _restoreSchedules);
                Safe.Run("Dealer deals", _watchDeals);
            }

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + Mathf.Max(2f, Core.CheckInterval.Value);
                Safe.Run("Shelf scan", _scan);
                Safe.Run("Restock check", _checkDealers);
            }
        }

        private static readonly Action _stepRuns = StepRuns, _restoreSchedules = RestoreSchedules, _watchDeals = WatchDeals,
                                       _checkDealers = CheckDealers, _scan = () => RefreshShelves();

        // Sold out? Go get more.
        private static readonly HashSet<string> _warnedMissing = new HashSet<string>();
        private static readonly Dictionary<string, float> _missingSince = new Dictionary<string, float>();

        private static bool IsNight()
        {
            try { return NetworkSingleton<Il2CppScheduleOne.GameTime.TimeManager>.Instance.IsNight; }
            catch { return false; }
        }

        private static void CheckDealers()
        {
            foreach (Dealer d in HiredDealers())
            {
                // A shelf this dealer used in this save has been picked up (not seen for two minutes):
                // say so once, instead of quietly never going. A shelf from another save isn't "gone" -
                // all saves share one assignments file - so only shelves met this session count.
                foreach (var role in new[] { ShelfRole.Supply, ShelfRole.Earnings })
                {
                    string key = d.ID + "|" + role;
                    bool lost = Assignments.ShelvesFor(role, d.ID).Any(g => _everSeen.Contains(g)) && ShelfFor(d, role) == null;
                    if (lost)
                    {
                        if (!_missingSince.TryGetValue(key, out float since)) _missingSince[key] = since = Time.time;
                        if (Time.time - since > 120f && _warnedMissing.Add(key))
                        {
                            string job = role == ShelfRole.Supply ? "product" : "earnings";
                            Core.Log.Msg($"{d.FirstName}'s {job} shelf isn't there any more (picked up?) - give them a new one.");
                            Speech.Text(d, $"My {job} shelf is gone. Can you give me a new one?");
                        }
                    }
                    else { _missingSince.Remove(key); _warnedMissing.Remove(key); }
                }

                if (IsOnRun(d)) continue;
                if (_cooldownUntil.TryGetValue(d.Pointer, out float until) && Time.time < until) continue;
                // Optional: leave dealers who are in bed (inside their home at night) alone.
                if (!Core.RestockAtNight.Value && d.isInBuilding && IsNight()) continue;
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
                if (_sendWhenFree.TryGetValue(key, out ShelfRole? role) && !IsOnRun(d) && BusyReason(d, ignoreDeals: true) == null)
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
        public static string BusyReason(Dealer d) => BusyReason(d, false);

        // ignoreDeals: your orders come before deals - a dealer on a deal still goes when you
        // send them (the deal waits, see HoldsDeals).
        public static string BusyReason(Dealer d, bool ignoreDeals)
        {
            if (DealerMenu.IsTalkingTo(d)) return "talking to you";
            if (!d.IsConscious) return "knocked out";
            if (!ignoreDeals && d.currentContract != null) return "on a deal";
            if (d.IsInVehicle) return "driving";
            var beh = d.Behaviour;
            if (beh == null || beh.ScheduleManager == null) return "unavailable";
            if (ignoreDeals && IsDealing(d)) return null;
            string busy = BusyBehaviour(d);
            if (busy != null) return "busy (" + busy + ")";
            return null;
        }

        // In a conversation with you: the dialogue screen is up, or the game's dialogue behaviour is in charge.
        private static bool Talking(Dealer d) => DealerMenu.IsTalkingTo(d) || IsDialogue(d);

        private static bool IsDialogue(Dealer d)
        {
            try { return d.Behaviour?.activeBehaviour?.TryCast<GenericDialogueBehaviour>() != null; }
            catch { return false; }
        }

        // Walking to / waiting at a deal (the game's DealerAttendDealBehaviour).
        private static bool IsDealing(Dealer d)
        {
            try { return d.Behaviour?.activeBehaviour?.TryCast<DealerAttendDealBehaviour>() != null; }
            catch { return false; }
        }

        // True while a dealer is doing something you sent them to do: their deals wait until
        // they're done (checked by the patch on Dealer.CheckAttendStart).
        public static bool HoldsDeals(Dealer d) =>
            d != null && _runs.TryGetValue(d.Pointer, out Run run) && run.Priority;

        // Takes them off a deal they're walking to (it picks up again when they're done).
        private static void PauseDeal(Dealer d)
        {
            try
            {
                var attend = d._attendDealBehaviour;
                if (attend != null && (attend.Enabled || attend.Active)) attend.Disable_Server();
            }
            catch (Exception e) { Core.Log.Warning($"Couldn't hold {d.FirstName}'s deal: {e.Message}"); }
        }

        // Starts a run if the dealer is free and has something to do. On their own they drop off
        // cash on the way home from dealing and restock once sold out. Sent by you (manual) they
        // go straight away - even mid-deal - and every cash drop tops them up with product too.
        // only: just the cash (plus the top-up), or just the product. Returns "on the way" or why not.
        public static string TryStart(Dealer d, bool manual, ShelfRole? only = null)
        {
            if (IsOnRun(d)) return "already on the way";
            string busy = BusyReason(d, ignoreDeals: manual);
            if (busy != null) return busy;

            var run = new Run { Dealer = d, Priority = manual };
            PlaceableStorageEntity earnings = only == ShelfRole.Supply ? null : ShelfFor(d, ShelfRole.Earnings);
            PlaceableStorageEntity supply = ShelfFor(d, ShelfRole.Supply);
            bool cashStop = earnings != null && d.Cash >= 1f && (manual || _homeward.Contains(d.Pointer));
            if (cashStop)
                run.Stops.Enqueue(new Stop { Role = ShelfRole.Earnings, Shelf = earnings });
            // Product: when sent for it, when sold out, or topping up on any cash drop.
            bool wantProduct = only != ShelfRole.Earnings ? (manual || NeedsRestock(d) || cashStop) : cashStop;
            if (supply != null && wantProduct && CanTakeAny(d, supply))
                run.Stops.Enqueue(new Stop { Role = ShelfRole.Supply, Shelf = supply });

            if (run.Stops.Count == 0)
            {
                if (only != ShelfRole.Supply) _homeward.Remove(d.Pointer);
                if (!manual) _cooldownUntil[d.Pointer] = Time.time + Core.EmptyShelfCooldown.Value;

                // Why not, in their words: "I have $0 and "X" is empty".
                var why = new List<string>();
                if (only != ShelfRole.Supply && earnings != null && d.Cash < 1f) why.Add("I have $0");
                if (only != ShelfRole.Earnings && supply != null && !CanTakeAny(d, supply))
                {
                    Core.Log.Msg($"{d.FirstName} can't take anything from the {ShelfLabel(supply)} ({WhyCantTake(d, supply)}). On it: {ShelfContents(supply)}");
                    string reason = WhyCantTake(d, supply);
                    why.Add(why.Count > 0 && !reason.StartsWith("\"") ? char.ToLower(reason[0]) + reason.Substring(1) : reason);
                }
                if (why.Count == 0)
                    return earnings == null && supply == null ? "No shelf set" : "Nothing to do";
                if (manual && d.currentContract == null && !IsDealing(d)) GoHome(d); // sent for nothing: back home (unless mid-deal)
                return string.Join(" and ", why) + ", I'll go back home";
            }

            if (manual) PauseDeal(d);
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
            try { run.OldSpeed = Compat.GetSpeed(d); Compat.SetSpeed(d, run.OldSpeed * Mathf.Max(0.5f, Core.SpeedMultiplier.Value)); }
            catch { }

            _runs[d.Pointer] = run;
            NextStop(run);
            string where = string.Join(" and then ", run.Stops.Prepend(run.Current).Select(StopLabel));
            Core.Log.Msg($"{d.FirstName} is heading to {where}.");
        }

        private static string StopLabel(Stop s) => s.Home ? "home" : s.Crossing ? "the walkway" : s.Door ? "the door" : "the " + ShelfLabel(s.Shelf);

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
            if (run.Stops.Count == 0 && run.Inside)
            {
                // Done - but not while still inside a building the game can't walk them out of.
                run.Stops.Enqueue(ExitDoor(run));
            }
            if (run.Stops.Count == 0)
            {
                // Done - but not while still out on the island: back over a walkway first.
                if (!run.OnIsland || !Crossings.TryBest(run.Dealer.Movement.FootPosition, HomeOrHere(run.Dealer), true, v => run.Dealer.Movement.CanGetTo(v, 2f), out Vector3 exit))
                {
                    run.Current = null;
                    return false;
                }
                run.Stops.Enqueue(new Stop { Crossing = true, ToIsland = false, ShelfPoint = exit });
            }
            Stop stop = run.Current = run.Stops.Dequeue();
            var move = run.Dealer.Movement;
            if (!stop.Crossing && !stop.Door)
                stop.ShelfPoint = stop.Home ? HomeDoorPoint(run.Dealer, out stop.DoorIndex) : StandPoint(run.Dealer, stop.Shelf);

            // Inside a building and the next place is out of it? Back out through the door first - but
            // never for the place they came in for (see DoorRules: that loop was in-and-out for ever).
            if (!stop.Door && DoorRules.ShouldExit(run.Inside, stop == run.EnteredFor, ReachableForDoors(move, stop.ShelfPoint), run.DoorCrossings))
            {
                PushFront(run, stop);
                stop = run.Current = ExitDoor(run);
            }

            // On the other side of a walkway (Greenhouse Island's shelves, or home from there)?
            // Walk to the handover spot first, then on from there.
            else if (!stop.Crossing && !stop.Door && Crossings.IsIsland(stop.ShelfPoint) != run.OnIsland
                && Crossings.TryBest(move.FootPosition, stop.ShelfPoint, run.OnIsland, v => move.CanGetTo(v, 2f), out Vector3 spot))
            {
                PushFront(run, stop);
                stop = run.Current = new Stop { Crossing = true, ToIsland = !run.OnIsland, ShelfPoint = spot };
            }
            // A shelf inside a house whose inside the game doesn't join up with the street: walk to
            // the front door, step in, and walk on to the shelf.
            else if (!stop.Crossing && !stop.Door && !stop.Home && stop.Shelf != null
                     && DoorRules.ShouldEnter(run.Inside, ReachableForDoors(move, stop.ShelfPoint), stop.DoorTried, run.DoorCrossings)
                     && TryEnterDoor(run, stop, out Stop door))
            {
                stop.DoorTried = true;
                door.For = stop;
                PushFront(run, stop);
                stop = run.Current = door;
            }
            stop.WalkPoint = stop.ShelfPoint;
            if (!move.CanGetTo(stop.ShelfPoint, ArriveDistance) && move.GetClosestReachablePoint(stop.ShelfPoint, out Vector3 closest))
                stop.WalkPoint = closest;
            stop.Started = Time.time;
            run.ProgressAt = 0f; // fresh stuck-watch for this leg
            run.Unsticks = 0;
            run.RefusedSince = 0f;
            Unpause(run.Dealer);
            move.SetDestination(stop.WalkPoint);
            run.NextRepath = Time.time + 3f;
            return true;
        }

        // "Can the game walk them there?" - what the door rules are asked. (DEVTEST builds only: a file in
        // UserData makes the answer always "no", which is what a shelf whose standing spot is too far from
        // the walkable floor looks like - the case that once sent a dealer in and out of a door for ever.)
        private static bool ReachableForDoors(NPCMovement move, Vector3 point)
        {
#if DEVTEST
            if (File.Exists(Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "DealerSelfSupply_forceunreachable.txt"))) return false;
#endif
            return move.CanGetTo(point, ArriveDistance);
        }

        private static void PushFront(Run run, Stop stop)
        {
            var rest = run.Stops.ToList();
            run.Stops.Clear();
            run.Stops.Enqueue(stop);
            foreach (var s in rest) run.Stops.Enqueue(s);
        }

        // The way into the building a shelf is in: its inside point (where the game puts people who
        // arrive inside), if the shelf can be walked to from there, and the nearest spot outside
        // the dealer can walk to - the front door. A door stop walks there and steps through.
        private static bool TryEnterDoor(Run run, Stop stop, out Stop door)
        {
            door = null;
            try
            {
                var prop = stop.Shelf.ParentProperty;
                if (prop == null) return false;
                Transform inPoint = prop.InteriorSpawnPoint;
                if (inPoint == null && prop.EmployeeIdlePoints != null && prop.EmployeeIdlePoints.Length > 0) inPoint = prop.EmployeeIdlePoints[0];
                if (inPoint == null) return false;

                var move = run.Dealer.Movement;
                var agent = Compat.AgentOf(run.Dealer);
                if (agent == null) return false;
                if (!NavMesh.SamplePosition(inPoint.position, out NavMeshHit inHit, 2f, agent.areaMask)) return false;
                Vector3 inside = inHit.position;

                // The shelf has to be reachable from in there, or stepping in won't help.
                var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                var path = new NavMeshPath();
                if (!NavMesh.SamplePosition(stop.ShelfPoint, out NavMeshHit shelfHit, 2f, agent.areaMask)) return false;
                if (!NavMesh.CalculatePath(inside, shelfHit.position, filter, path) || path.corners.Length == 0
                    || Vector3.Distance(path.corners[path.corners.Length - 1], stop.ShelfPoint) > ArriveDistance + 0.5f) return false;

                // The doorstep: as close to the inside point as the dealer can walk from the street.
                if (!move.GetClosestReachablePoint(inside, out Vector3 outside) || Flat(outside, inside) > 15f) return false;

                door = new Stop { Door = true, ShelfPoint = outside, StepTo = inside };
                Core.Log.Msg($"{run.Dealer.FirstName} will go in through the door of {prop.PropertyName} to reach the {ShelfLabel(stop.Shelf)}.");
                return true;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Couldn't find the way into the building with the {ShelfLabel(stop.Shelf)}: {e.Message}");
                return false;
            }
        }

        // Back out the way they came in.
        private static Stop ExitDoor(Run run) => new Stop { Door = true, ShelfPoint = run.InsidePoint, StepTo = run.OutsidePoint };

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
                    try { EndRun(run); } catch (Exception e2) { Core.Log.Warning("Couldn't hand " + run.Dealer?.FirstName + " back to their routine cleanly: " + e2.Message); }
                }
            }
        }

        // Advances one run; false once it's over.
        private static bool StepRun(Run run)
        {
            Dealer d = run.Dealer;
            if (d == null) return false;

            var beh = d.Behaviour;
            // Sent by you: a deal doesn't stop them - it waits (and is put back on hold if it
            // tries to start anyway).
            if (run.Priority && IsDealing(d)) PauseDeal(d);
            bool dealInTheWay = !run.Priority && (d.currentContract != null || IsDealing(d));
            // Talking to them is not an interruption: the trip waits (see below) and carries on afterwards.
            bool talking = Talking(d);
            if (!d.IsConscious || dealInTheWay || (BusyBehaviour(d) != null && !IsDealing(d) && !talking))
            {
                Core.Log.Msg($"{d.FirstName}'s shelf run was interrupted.");
                EndRun(run);
                return false;
            }

            Stop stop = run.Current;
            // Shelf taken off them mid-trip (or given to someone else): don't use it.
            if (stop != null && !stop.Home && !stop.Crossing && !stop.Door && stop.Shelf != null && (ShelfFor(d, stop.Role)?.Pointer ?? IntPtr.Zero) != stop.Shelf.Pointer)
                stop = run.Current = null;
            if (stop == null || (!stop.Home && !stop.Crossing && !stop.Door && stop.Shelf == null))
            {
                // Shelf picked up or destroyed mid-trip: skip to the next one.
                if (NextStop(run)) return true;
                EndRun(run);
                return false;
            }

            // Something switched the schedule back on - it would steer them home, so take it back.
            if (beh.ScheduleManager.ScheduleEnabled) beh.ScheduleManager.DisableSchedule();

            var move = d.Movement;

            // You're talking to them: the game stops them where they stand (its dialogue behaviour takes
            // them over). The trip waits - no stuck checks, no moving them on - and once you're done they
            // set off again from there, to the same place.
            if (talking)
            {
                run.Held = true;
                run.ProgressAt = Time.time;
                run.RefusedSince = 0f;
                stop.Started += StepInterval;                     // the travel time limit waits too
                if (stop.ActAt >= 0f) stop.ActAt += StepInterval; // ...as does taking things off the shelf
                return true;
            }
            if (run.Held)
            {
                run.Held = false;
                if (stop.ActAt < 0f)
                {
                    Unpause(d);
                    move.SetDestination(stop.WalkPoint);
                    run.NextRepath = Time.time + 3f;
                }
            }

            if (stop.ActAt < 0f)
            {
                if (Flat(move.FootPosition, stop.WalkPoint) <= ArriveDistance)
                {
                    // Got as close as walking allows (e.g. a property the navmesh doesn't reach): step in.
                    if (Flat(stop.WalkPoint, stop.ShelfPoint) > ArriveDistance) Compat.Warp(move, stop.ShelfPoint);
                    Arrive(run.Dealer, stop);
                }
                else if (Time.time - stop.Started > Core.MaxTravelSeconds.Value)
                {
                    Compat.Warp(move, stop.ShelfPoint);
                    Arrive(run.Dealer, stop);
                }
                else if (WalkRefused(run, stop))
                {
                    return true; // handled
                }
                else if (Stuck(run, stop))
                {
                    return true; // handled
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
            else if (stop.Door)
            {
                // Through the doorway: the game has no walkable way across, so step over it.
                Compat.Warp(d.Movement, stop.StepTo);
                run.DoorCrossings++;
                if (!run.Inside) { run.Inside = true; run.InsidePoint = stop.StepTo; run.OutsidePoint = stop.ShelfPoint; run.EnteredFor = stop.For; }
                else { run.Inside = false; run.EnteredFor = null; }
                Core.Log.Msg($"{d.FirstName} went {(run.Inside ? "in" : "out")} through the door (crossing {run.DoorCrossings}).");
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

        // The game drops a walk order it can't path (a shelf inside a building its navmesh doesn't
        // reach, a dealer standing on a scrap of navmesh, another mod pausing NPCs): no
        // destination, not moving. Don't stand there for the full stuck timer - un-pause them and
        // ask again every 2 seconds, and after 8 seconds of refusals step them over to the shelf.
        private const float RefusedSeconds = 8f;

        private static bool WalkRefused(Run run, Stop stop)
        {
            Dealer d = run.Dealer;
            var move = d.Movement;
            if (move.IsMoving || Compat.HasDestination(d))
            {
                run.RefusedSince = 0f;
                return false;
            }
            if (run.RefusedSince <= 0f) run.RefusedSince = Time.time;
            if (Time.time - run.RefusedSince >= RefusedSeconds)
            {
                Core.Log.Msg($"{d.FirstName} can't find a way to {StopLabel(stop)} (paused={Compat.IsPaused(d)}, " +
                             $"on navmesh={(Compat.AgentOf(d) != null && Compat.AgentOf(d).isOnNavMesh)}, doing={d.Behaviour?.activeBehaviour?.Name ?? "nothing"}) - stepping over there.");
                run.RefusedSince = 0f;
                if (d.isInBuilding) d.ExitBuilding("");
                Compat.Warp(move, stop.ShelfPoint);
                Arrive(d, stop);
                return true;
            }
            if (Time.time >= run.NextRepath)
            {
                Unpause(d);
                move.SetDestination(stop.WalkPoint);
                run.NextRepath = Time.time + 2f;
            }
            return true;
        }

        // Movement left paused (by a conversation that's over, or another mod) - a paused dealer
        // never sets off. Left alone while you're still talking to them.
        private static void Unpause(Dealer d)
        {
            try
            {
                if (!Talking(d) && Compat.IsPaused(d)) Compat.Resume(d);
            }
            catch { }
        }

        // Not getting anywhere? (Still inside, a path that won't start, a doorway...) After 20
        // seconds without moving a metre: step them outside and send them again. Another 20: put
        // them at the next point of the trip. Either way the log says what state they were in.
        private const float StuckSeconds = 20f;

        private static bool Stuck(Run run, Stop stop)
        {
            Dealer d = run.Dealer;
            var move = d.Movement;
            Vector3 here = move.FootPosition;
            if (run.ProgressAt <= 0f || Flat(here, run.ProgressPos) > 1f)
            {
                run.ProgressPos = here;
                run.ProgressAt = Time.time;
                return false;
            }
            if (Time.time - run.ProgressAt < StuckSeconds) return false;

            string state = $"inside={d.isInBuilding}, moving={move.IsMoving}, destination={Compat.HasDestination(d)}, paused={Compat.IsPaused(d)}, " +
                           $"agent={(Compat.AgentOf(d) != null && Compat.AgentOf(d).enabled)}, on navmesh={(Compat.AgentOf(d) != null && Compat.AgentOf(d).isOnNavMesh)}, " +
                           $"doing={d.Behaviour?.activeBehaviour?.Name ?? "nothing"}";
            run.ProgressAt = Time.time;
            run.Unsticks++;
            if (run.Unsticks == 1)
            {
                Core.Log.Msg($"{d.FirstName} hasn't moved in {StuckSeconds:0}s on the way to {StopLabel(stop)} ({state}) - nudging them.");
                if (d.isInBuilding) d.ExitBuilding("");
                move.Stop();
                Unpause(d);
                move.SetDestination(stop.WalkPoint);
                run.NextRepath = Time.time + 3f;
            }
            else
            {
                Core.Log.Msg($"{d.FirstName} is still stuck on the way to {StopLabel(stop)} ({state}) - moving them on.");
                if (d.isInBuilding) d.ExitBuilding("");
                Compat.Warp(move, stop.WalkPoint);
                run.Unsticks = 0;
            }
            return true;
        }

        private static void Arrive(Dealer d, Stop stop)
        {
            var move = d.Movement;
            move.Stop();
            if (stop.Home || stop.Crossing || stop.Door)
            {
                stop.ActAt = Time.time + (stop.Home ? 0.3f : 0f); // straight in / straight on
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
            Compat.SetSpeed(d, run.OldSpeed);
            // Cut short inside a building the game can't walk them out of: put them on the doorstep.
            if (run.Inside) { try { Compat.Warp(d.Movement, run.OutsidePoint); } catch { } run.Inside = false; }
            // Cut short out on the island (a deal came up): back to the game's walkable area.
            if (run.OnIsland) Crossings.Cross(d, d.Movement.FootPosition, false, warp: false);
            _awaitingSchedule[d.Pointer] = d;
            try { RestoreSchedules(); } catch (Exception e) { Core.Log.Warning("Couldn't restore a dealer's routine yet: " + e.Message); }
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

        private static int EmptySlots(Il2CppSystem.Collections.Generic.List<ItemSlot> slots)
        {
            int n = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].ItemInstance == null && !slots[i].IsLocked && !slots[i].IsAddLocked) n++;
            return n;
        }

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
                if (s == null || s.IsRemovalLocked || !IsTakeable(s.ItemInstance)) continue;
                var shape = AsCarried(s.ItemInstance); // loose product counts as the baggies it becomes
                if (shape != null && d.Inventory.GetCapacityForItem(shape) > 0) return true;
            }
            return false;
        }

        // Returns how many items they took.
        private static int TakeProductAndReport(Dealer d, PlaceableStorageEntity shelf)
        {
            int taken = TakeProduct(d, shelf, out int bagged);
            if (taken > 0)
            {
                // Every slot filled? Then they're maxed out; otherwise the shelf ran out first.
                var slots = d.Inventory?.ItemSlots;
                bool full = slots != null && EmptySlots(slots) == 0;
                Core.Log.Msg($"{d.FirstName} grabbed {taken} item{(taken == 1 ? "" : "s")} from the {ShelfLabel(shelf)}"
                             + (bagged > 0 ? $" ({bagged} loose, bagged)" : "") + (full ? " - inventory full." : " - that was all of it."));
                Speech.Text(d, full
                    ? "Inventory full, starting to deal."
                    : $"Took everything on {Quoted(shelf)} ({taken} items), starting to deal.");
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
        private static int TakeProduct(Dealer d, PlaceableStorageEntity shelf) => TakeProduct(d, shelf, out _);

        // Everything they can carry off the shelf, shelf order first: baggies, jars and bricks as
        // they are, and loose product bagged up on the way (dealers can only sell packaged
        // product) - no baggies needed on the shelf.
        // Returns how many items moved; bagged: how many of those were loose.
        private static int TakeProduct(Dealer d, PlaceableStorageEntity shelf, out int bagged)
        {
            bagged = 0;
            var from = shelf.StorageEntity?.ItemSlots;
            var to = d.Inventory?.ItemSlots;
            if (from == null || to == null) return 0;

            int moved = 0;
            for (int i = 0; i < from.Count; i++)
            {
                ItemSlot src = from[i];
                if (src == null || src.IsRemovalLocked || !IsTakeable(src.ItemInstance)) continue;
                bool loose = !IsPackagedProduct(src.ItemInstance);

                // Top up matching stacks first, then fill empty slots.
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int j = 0; j < to.Count; j++)
                    {
                        ItemInstance item = src.ItemInstance;
                        if (item == null || item.Quantity <= 0) break;
                        ItemInstance shape = AsCarried(item); // what it becomes in their pocket
                        if (shape == null) break;
                        ItemSlot dst = to[j];
                        if (dst == null || dst.IsLocked || dst.IsAddLocked) continue;

                        int n;
                        if (pass == 0)
                        {
                            if (dst.ItemInstance == null || !dst.ItemInstance.CanStackWith(shape, false)) continue;
                            n = Math.Min(shape.StackLimit - dst.Quantity, item.Quantity);
                            if (n <= 0) continue;
                            dst.ChangeQuantity(n);
                        }
                        else
                        {
                            if (dst.ItemInstance != null) continue;
                            n = Math.Min(shape.StackLimit, item.Quantity);
                            if (n <= 0) continue;
                            dst.SetStoredItem(shape.GetCopy(n));
                        }
                        src.ChangeQuantity(-n);
                        moved += n;
                        if (loose) bagged += n;
                    }
                }
            }
            return moved;
        }

        // The game's baggie packaging (for bagging loose product).
        private static PackagingDefinition _baggie;
        private static PackagingDefinition Baggie()
        {
            if (_baggie != null) return _baggie;
            try { _baggie = Il2CppScheduleOne.Registry.GetItem("baggie")?.TryCast<PackagingDefinition>(); } catch { }
            return _baggie;
        }

        // Any product: packaged as it is, or loose (bagged when taken).
        private static bool IsTakeable(ItemInstance item) =>
            IsPackagedProduct(item) || (item?.TryCast<ProductItemInstance>() != null && Baggie() != null);

        // The item as it goes into the dealer's pocket: packaged product unchanged, loose
        // product as baggies.
        private static ItemInstance AsCarried(ItemInstance item)
        {
            if (IsPackagedProduct(item)) return item;
            var product = item?.TryCast<ProductItemInstance>();
            var baggie = Baggie();
            if (product == null || baggie == null) return null;
            var bag = product.GetCopy(1).TryCast<ProductItemInstance>();
            if (bag == null) return null;
            bag.SetPackaging(baggie);
            return bag;
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
            // Already off on a trip of their own (e.g. dropping cash after a deal)? It's your
            // order now: a deal can't cut it short any more.
            if (_runs.TryGetValue(d.Pointer, out Run already))
            {
                if (!already.Priority)
                {
                    already.Priority = true;
                    PauseDeal(d);
                    Core.Log.Msg($"{d.FirstName} was already heading to the shelves - it's the boss's order now, deals wait.");
                    Speech.Text(d, "Already on my way, boss.");
                }
                return RunDescription(d);
            }
            if (BusyReason(d, ignoreDeals: true) != null)
            {
                _sendWhenFree[d.Pointer] = only;
                string waiting = WaitingDescription(d);
                string why = BusyReason(d, ignoreDeals: true);
                Core.Log.Msg($"{d.FirstName} was sent to their shelves but is busy: {why}. Going once free.");
                Speech.Text(d, why == "on a deal" ? "On a deal right now, I'll head over after."
                             : why == "talking to you" ? "I'll head over once we're done talking."
                             : why == "knocked out" ? "I'm hurt, I'll head over once I'm back up."
                             : $"Busy right now ({why}), I'll head over after.");
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
            string why = BusyReason(d, ignoreDeals: true);
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
            bool anyItem = false, anyProduct = false, anyPackaged = false;
            for (int i = 0; i < from.Count; i++)
            {
                var item = from[i]?.ItemInstance;
                if (item == null) continue;
                anyItem = true;
                if (item.TryCast<ProductItemInstance>() == null) continue;
                anyProduct = true;
                if (IsTakeable(item)) anyPackaged = true; // loose product counts: they bag it
            }
            if (!anyItem) return Quoted(shelf) + " is empty";
            if (!anyProduct) return "No product on " + Quoted(shelf);
            if (!anyPackaged) return "Couldn't bag the loose product on " + Quoted(shelf);
            return "Pockets full, no room to carry more";
        }

        // Cash goes to EARNINGS as soon as the dealer is free (e.g. once you finish talking).
        public static void QueueDropOff(Dealer d)
        {
            if (d != null && d.Cash >= 1f) _homeward.Add(d.Pointer);
        }
    }
}
