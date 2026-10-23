using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace DealerSelfSupply
{
    // While you're talking to a hired dealer (look at them, press E), two blocks sit in the
    // top-left corner: Product and Earnings. Each has two dropdowns - the place (a property you
    // own), then the shelf/locker there by name with how much is on it. The game frees the mouse
    // during conversations, so they're clickable.
    internal static class DealerMenu
    {
        private const float BlockWidth = 470f;
        private const float Margin = 16f, Gap = 10f;
        private const float Chrome = 30f;          // window title bar + padding
        private const float HeadingH = 88f;
        private const float TitleH = 38f, InfoH = 25f, DropH = 29f, EntryH = 27f, ButtonH = 27f;
        private const int ShelfPageSize = 16;      // two columns of eight

        private class Choice
        {
            public PlaceableStorageEntity Shelf;
            public string Guid;
            public string Place;
            public string Name;   // shelf name, numbered if the place has more than one with it
            public bool Empty;
        }

        private class BlockState
        {
            public bool PlaceOpen, ShelfOpen;
            public string Place;
            public int Page;
        }

        private static IntPtr _dealer;
        private static readonly Dictionary<ShelfRole, BlockState> _state = new Dictionary<ShelfRole, BlockState>();
        private static List<Choice> _choices = new List<Choice>();
        private static List<string> _places = new List<string>();
        private static Dealer _drawing;
        private static GUI.WindowFunction _headingFn, _supplyFn, _earningsFn;

        public static void Reset()
        {
            _dealer = IntPtr.Zero;
            _choices.Clear();
            _places.Clear();
        }

        public static bool IsTalkingTo(Dealer d) => d != null && _dealer != IntPtr.Zero && d.Pointer == _dealer;

        private static Dealer TalkingTo()
        {
            if (!Core.InMain || !SupplyRuns.IsHost) return null;
            try
            {
                var handler = Singleton<DialogueCanvas>.Instance?.currentHandler;
                var dealer = handler?.NPC?.TryCast<Dealer>();
                return dealer != null && dealer.IsRecruited ? dealer : null;
            }
            catch
            {
                return null;
            }
        }

        public static void Draw()
        {
            Dealer d = TalkingTo();
            if (d == null)
            {
                _dealer = IntPtr.Zero;
                return;
            }
            if (d.Pointer != _dealer)
            {
                // New conversation: fresh shelf list, dropdowns closed, each on the dealer's current place.
                _dealer = d.Pointer;
                RebuildChoices();
                _state.Clear();
                foreach (ShelfRole role in new[] { ShelfRole.Supply, ShelfRole.Earnings })
                {
                    var current = SupplyRuns.ShelfFor(d, role);
                    _state[role] = new BlockState { Place = current != null ? PlaceOf(current) : _places.FirstOrDefault() };
                }
            }

            // Hidden by the setting: the conversation still counts as "talking" above (their trip waits), just no menu.
            if (!Core.ShowTalkMenu.Value) return;

            UiKit.EnsureStyles();
            _drawing = d;
            _headingFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawHeading(); UiLayout.Drag(); });
            _supplyFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawBlock(ShelfRole.Supply); UiLayout.Drag(); });
            _earningsFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawBlock(ShelfRole.Earnings); UiLayout.Drag(); });

            UiLayout.Window("talk", 740300, new Rect(Margin, Margin, BlockWidth * 2f + Gap, HeadingH), _headingFn, "");
            float y = Margin + HeadingH + Gap;
            UiLayout.Window("talk", 740301, new Rect(Margin, y, BlockWidth, Height(ShelfRole.Supply)), _supplyFn, "");
            UiLayout.Window("talk", 740302, new Rect(Margin + BlockWidth + Gap, y, BlockWidth, Height(ShelfRole.Earnings)), _earningsFn, "");
        }

        // Heading across both blocks: the mod's name, and whose shelves these are.
        private static void DrawHeading()
        {
            Dealer d = _drawing;
            if (d == null) return;
            GUILayout.BeginHorizontal(UiKit.Grid);
            GUILayout.BeginVertical(UiKit.Cell, GUILayout.Width(62f), GUILayout.Height(52f));
            GUILayout.FlexibleSpace();
            Portraits.Draw(d, 44f);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.BeginVertical(UiKit.Cell, GUILayout.Height(52f));
            GUILayout.FlexibleSpace();
            GUILayout.Label("Dealer Self-supply", UiKit.NameBig);
            GUILayout.Label("Shelves for " + d.FirstName + " " + d.LastName, UiKit.Caption);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private static float Height(ShelfRole role)
        {
            BlockState s = _state[role];
            float h = Chrome + TitleH + InfoH + 2 * DropH + InfoH + 4f; // title, status, two dropdowns, assigned line
            if (s.PlaceOpen) h += Math.Max(1, _places.Count) * EntryH;
            if (s.ShelfOpen)
            {
                int count = ShelvesIn(s.Place).Count;
                int onPage = Math.Min(ShelfPageSize, Math.Max(1, count - s.Page * ShelfPageSize));
                h += (onPage + 1) / 2 * EntryH;
                if (count > ShelfPageSize) h += ButtonH;
            }
            if (_drawing != null && SupplyRuns.ShelfFor(_drawing, role) != null) h += 2 * ButtonH; // stop + send
            if (SupplyRuns.IsWaitingToGo(_drawing, role)) h += InfoH;
            return h;
        }

        private static void DrawBlock(ShelfRole role)
        {
            Dealer d = _drawing;
            if (d == null) return;
            BlockState s = _state[role];
            PlaceableStorageEntity current = SupplyRuns.ShelfFor(d, role);
            Choice currentChoice = current != null ? _choices.FirstOrDefault(c => c.Shelf.Pointer == current.Pointer) : null;

            GUILayout.BeginVertical(UiKit.Grid);
            GUILayout.Label(role == ShelfRole.Supply ? "Product" : "Earnings", UiKit.Title, GUILayout.Height(TitleH - 1f));
            GUILayout.BeginVertical(UiKit.Cell, GUILayout.Height(InfoH - 1f));
            GUILayout.Label(role == ShelfRole.Supply ? ProductStatus(d) : EarningsStatus(d), SupplyRuns.RunDescription(d) != null ? UiKit.CaptionGo : UiKit.Caption);
            GUILayout.EndVertical();

            // Place dropdown.
            if (UiKit.DropButton("PLACE        " + (s.Place ?? "none"), s.PlaceOpen, false, DropH - 1f))
            {
                s.PlaceOpen = !s.PlaceOpen;
                s.ShelfOpen = false;
            }
            if (s.PlaceOpen)
            {
                if (_places.Count == 0) GUILayout.Label("You haven't placed any shelves or lockers.", UiKit.Caption);
                foreach (string place in _places)
                {
                    int n = ShelvesIn(place).Count;
                    if (GUILayout.Button(place + "   (" + n + ")", place == s.Place ? UiKit.EntrySelected : UiKit.Entry, GUILayout.Height(EntryH - 1f)))
                    {
                        s.Place = place;
                        s.Page = 0;
                        s.PlaceOpen = false;
                        s.ShelfOpen = true; // straight on to picking the shelf
                    }
                }
            }

            // Shelf dropdown for the chosen place - green once the dealer's shelf is the one shown.
            List<Choice> shelves = ShelvesIn(s.Place);
            bool showingAssigned = currentChoice != null && currentChoice.Place == s.Place;
            if (UiKit.DropButton("SHELF        " + (showingAssigned ? currentChoice.Name : "Choose a shelf"), s.ShelfOpen, showingAssigned, DropH - 1f))
            {
                s.ShelfOpen = !s.ShelfOpen;
                s.PlaceOpen = false;
                if (s.ShelfOpen) RebuildChoices(); // fresh contents
            }
            if (s.ShelfOpen)
            {
                int pages = Math.Max(1, (shelves.Count + ShelfPageSize - 1) / ShelfPageSize);
                s.Page = Mathf.Clamp(s.Page, 0, pages - 1);
                var page = shelves.Skip(s.Page * ShelfPageSize).Take(ShelfPageSize).ToList();
                if (page.Count == 0) GUILayout.Label("No shelves or lockers here.", UiKit.Caption);
                float half = (BlockWidth - 32f) / 2f;
                for (int i = 0; i < page.Count; i += 2)
                {
                    GUILayout.BeginHorizontal();
                    for (int j = i; j < Math.Min(i + 2, page.Count); j++)
                    {
                        Choice c = page[j];
                        bool selected = currentChoice == c;
                        string owner = role == ShelfRole.Supply ? OwnerOf(c.Guid, d.ID) : null;
                        string text = c.Name + (owner != null ? "  (" + owner + ")" : "") + (c.Empty ? "  (empty)" : "");
                        if (GUILayout.Button(text, selected ? UiKit.EntrySelected : UiKit.Entry, GUILayout.Width(half), GUILayout.Height(EntryH - 1f)))
                        {
                            Select(d, role, c);
                            s.ShelfOpen = false;
                        }
                    }
                    GUILayout.EndHorizontal();
                }
                if (pages > 1)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("< Prev", UiKit.Button, GUILayout.Height(ButtonH - 1f)) && s.Page > 0) s.Page--;
                    GUILayout.Label((s.Page + 1) + " / " + pages, UiKit.HeadCell, GUILayout.Width(60f), GUILayout.Height(ButtonH - 1f));
                    if (GUILayout.Button("Next >", UiKit.Button, GUILayout.Height(ButtonH - 1f)) && s.Page < pages - 1) s.Page++;
                    GUILayout.EndHorizontal();
                }
            }

            // Assigned or not - in green when it is.
            GUILayout.BeginVertical(current != null ? UiKit.CellMine : UiKit.Cell, GUILayout.Height(InfoH - 1f));
            if (current != null)
            {
                string where = currentChoice != null ? currentChoice.Name + " (" + currentChoice.Place + ")" : SupplyRuns.ShelfLabel(current);
                GUILayout.Label("Assigned to " + where, UiKit.CaptionGreen);
            }
            else
            {
                GUILayout.Label("Not assigned to a shelf", UiKit.Caption);
            }
            GUILayout.EndVertical();

            bool stop = false;
            if (current != null)
            {
                stop = GUILayout.Button("Stop using a shelf", UiKit.Button, GUILayout.Height(ButtonH - 1f));
                // You're talking to them, so they set off once the chat ends.
                string name = currentChoice != null ? currentChoice.Name : SupplyRuns.ShelfLabel(current);
                if (GUILayout.Button("Send to " + name + " now", UiKit.Button, GUILayout.Height(ButtonH - 1f)))
                    SupplyRuns.SendWhenFree(d, role);
                if (SupplyRuns.IsWaitingToGo(d, role))
                {
                    GUILayout.BeginVertical(UiKit.Cell, GUILayout.Height(InfoH - 1f));
                    GUILayout.Label(Speech.She(d) + "'ll go when you finish talking.", UiKit.CaptionRed);
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndVertical();
            if (stop) Select(d, role, null);
        }

        private static void Select(Dealer d, ShelfRole role, Choice c)
        {
            var live = SupplyRuns.RefreshShelves(force: true).Keys;
            if (c == null) Assignments.Clear(role, d.ID, live);
            else Assignments.Assign(role, c.Guid, d.ID, live);
            Core.Log.Msg($"{d.FirstName}'s {(role == ShelfRole.Supply ? "product" : "earnings")} shelf: {(c == null ? "none" : c.Name + " at " + c.Place)}.");
            RebuildChoices();

            string job = role == ShelfRole.Supply ? "product" : "earnings";
            if (c == null)
            {
                Speech.Text(d, role == ShelfRole.Supply ? "Okay, I'll wait for you to bring me product." : "Okay, I'll hang on to the cash.", red: false);
            }
            else
            {
                Speech.Text(d, (role == ShelfRole.Supply ? "Got it, I'll grab my product from " : "Got it, I'll drop my cash in ") + "\"" + c.Name + "\" at " + c.Place + ".", red: false);
                if (role == ShelfRole.Earnings) SupplyRuns.QueueDropOff(d);
            }
        }

        // --- The shelf list ---

        private static List<Choice> ShelvesIn(string place) =>
            place == null ? new List<Choice>() : _choices.Where(c => c.Place == place).ToList();

        private static void RebuildChoices()
        {
            var shelves = SupplyRuns.RefreshShelves(force: true)
                .Select(p => new Choice { Shelf = p.Value, Guid = p.Key, Place = PlaceOf(p.Value) })
                .OrderBy(c => c.Place)
                .ThenBy(c => SupplyRuns.ShelfLabel(c.Shelf), NaturalOrder.Instance)
                .ThenBy(c => c.Shelf.transform.position.x)
                .ThenBy(c => c.Shelf.transform.position.z)
                .ToList();

            // Number shelves that share a name in the same place: "Storage Rack #2".
            foreach (var group in shelves.GroupBy(c => c.Place + "|" + SupplyRuns.ShelfLabel(c.Shelf)))
            {
                int n = 0, count = group.Count();
                foreach (Choice c in group)
                {
                    c.Name = SupplyRuns.ShelfLabel(c.Shelf) + (count > 1 ? " #" + ++n : "");
                    c.Empty = IsEmpty(c.Shelf);
                }
            }
            _choices = shelves;
            _places = shelves.Select(c => c.Place).Distinct().ToList();
        }

        private static string PlaceOf(PlaceableStorageEntity shelf)
        {
            try
            {
                string name = shelf.ParentProperty?.PropertyName;
                return string.IsNullOrEmpty(name) ? "Elsewhere" : name;
            }
            catch { return "Elsewhere"; }
        }

        // First names of the other dealers who get their product from this shelf, e.g. "Benji, Wei".
        private static string OwnerOf(string guid, string exceptId)
        {
            var names = Assignments.DealersOn(ShelfRole.Supply, guid)
                .Where(id => id != exceptId)
                .Select(id => SupplyRuns.HiredDealers().FirstOrDefault(x => x.ID == id)?.FirstName)
                .Where(n => n != null)
                .ToList();
            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        private static bool IsEmpty(PlaceableStorageEntity shelf)
        {
            var slots = shelf.StorageEntity?.ItemSlots;
            if (slots == null) return false;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i]?.ItemInstance != null) return false;
            return true;
        }

        private static string ProductStatus(Dealer d)
        {
            var slots = d.Inventory?.ItemSlots;
            if (slots == null) return "";
            int empty = 0;
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null && slots[i].ItemInstance == null) empty++;
            string state = SupplyRuns.RunDescription(d) ?? (empty == slots.Count ? "sold out" : null);
            return $"Carrying {SupplyRuns.ProductCarried(d)} items ({empty} of {slots.Count} slots empty)" + (state != null ? " - " + state : "");
        }

        // "Holding $120 - $23,332 on EARNINGS": what they carry, and what's waiting on their shelf.
        private static string EarningsStatus(Dealer d)
        {
            string text = "Holding " + MoneyManager.FormatAmount(d.Cash);
            var shelf = SupplyRuns.ShelfFor(d, ShelfRole.Earnings);
            if (shelf != null) text += "  -  " + MoneyManager.FormatAmount(SupplyRuns.CashOn(shelf)) + " on " + SupplyRuns.ShelfLabel(shelf);
            return text;
        }

        // Numbered shelves come out in order.
        private class NaturalOrder : IComparer<string>
        {
            public static readonly NaturalOrder Instance = new NaturalOrder();

            // Runs of digits compare as numbers wherever they are in the name: "Rack 2" before "Rack 10".
            public int Compare(string a, string b)
            {
                a ??= ""; b ??= "";
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int si = i, sj = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        string x = a.Substring(si, i - si).TrimStart('0'), y = b.Substring(sj, j - sj).TrimStart('0');
                        if (x.Length != y.Length) return x.Length.CompareTo(y.Length);
                        int c = string.CompareOrdinal(x, y);
                        if (c != 0) return c;
                    }
                    else
                    {
                        int c = char.ToUpperInvariant(a[i]).CompareTo(char.ToUpperInvariant(b[j]));
                        if (c != 0) return c;
                        i++; j++;
                    }
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }
        }
    }
}
