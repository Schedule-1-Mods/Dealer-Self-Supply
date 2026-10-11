using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace DealerSelfSupply
{
    // Beside the storage menu while a placed shelf is open, three separate blocks down the left,
    // laid out like the wiki's dealer table (dark cells, grid lines, green names):
    //   1. whose shelf it is - the product dealer's face and name in their own cells, and the
    //      faces of everyone who drops their earnings here (only when someone uses it);
    //   2. a table of every dealer: face | name + what they're up to | Product | Earnings;
    //   3. "Send them to their shelves now".
    // Host only - the runs happen on the host.
    internal static class ShelfPanel
    {
        private const float Right = 28f, Top = 24f, Width = 640f, Gap = 10f; // default: top right of the screen
        private const float WindowChrome = 32f;              // title bar + padding
        private const float RowH = 58f, HeadH = 26f, OwnerRowH = 44f; // table rows (plus 1 px grid line each)
        private const float FaceW = 60f, NameW = 380f, ToggleW = 88f;

        private static PlaceableStorageEntity _shelf;
        private static string _guid;
        private static List<Dealer> _dealers = new List<Dealer>();
        private static List<Dealer> _users = new List<Dealer>(); // dealers assigned to this shelf, for either job
        private static float _shelfCash;

        private static GUI.WindowFunction _nameFn, _ownerFn, _dealersFn, _actionsFn;
        private static GUIStyle _grid, _cell, _cellMine, _headCell, _cashCell, _name, _nameGreen, _caption, _on, _off;

        public static PlaceableStorageEntity OpenShelf()
        {
            if (!Core.InMain || !SupplyRuns.IsHost) return null;
            try
            {
                var menu = Singleton<StorageMenu>.Instance;
                if (menu == null || !menu.IsOpen || menu.OpenedStorageEntity == null) return null;
                return menu.OpenedStorageEntity.GetComponentInParent<PlaceableStorageEntity>();
            }
            catch
            {
                return null;
            }
        }

        public static void Draw()
        {
            PlaceableStorageEntity shelf = OpenShelf();
            if (shelf == null)
            {
                _shelf = null;
                _confirm = null;
                return;
            }
            if (_shelf == null || _shelf.Pointer != shelf.Pointer)
            {
                _shelf = shelf;
                _confirm = null;
                SupplyRuns.RefreshShelves(force: true);
            }

            EnsureStyles();
            _guid = SupplyRuns.GuidOf(shelf);
            _dealers = SupplyRuns.HiredDealers().ToList();
            _users = _dealers.Where(d => Assignments.IsAssigned(ShelfRole.Supply, _guid, d.ID) || Assignments.IsAssigned(ShelfRole.Earnings, _guid, d.ID)).ToList();
            _shelfCash = SupplyRuns.CashOn(shelf);

            _nameFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawName(); UiLayout.Drag(); });
            _ownerFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawOwner(); UiLayout.Drag(); });
            _dealersFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawDealers(); UiLayout.Drag(); });
            _actionsFn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawActions(); UiLayout.Drag(); });

            int cardRows = (_users.Count + CardsPerRow - 1) / CardsPerRow;
            float ownerH = _users.Count == 0 ? 0f
                : WindowChrome + cardRows * (CardH + 1f) + (_shelfCash > 0f ? HeadH + 1f : 0f) + 4f;
            float listH = WindowChrome + (_dealers.Count == 0 ? 24f : HeadH + 2f + _dealers.Count * (RowH + 1f));
            float actionsH = _dealers.Count > 0 ? WindowChrome + 52f : 0f;
            float nameH = WindowChrome + 34f;
            float total = nameH + Gap + (ownerH > 0f ? ownerH + Gap : 0f) + listH + (actionsH > 0f ? Gap + actionsH : 0f);

            // Too tall for the screen (lots of dealers, small window)? Shrink the whole panel to fit
            // rather than cut anyone off. Windows keep the scale they're declared with.
            float scale = Mathf.Min(1f, (Screen.height - Top - 16f) / total);
            Matrix4x4 previous = GUI.matrix;
            if (scale < 1f) GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float x = Screen.width / scale - Width - Right, y = Top / scale;
                UiLayout.Window("shelfPanel", 739149, new Rect(x, y, Width, nameH), _nameFn, "Shelf name (shown on its sign)");
                y += nameH + Gap;
                if (ownerH > 0f)
                {
                    UiLayout.Window("shelfPanel", 739150, new Rect(x, y, Width, ownerH), _ownerFn, "Whose shelf");
                    y += ownerH + Gap;
                }
                UiLayout.Window("shelfPanel", 739151, new Rect(x, y, Width, listH), _dealersFn, "Dealers using this shelf");
                y += listH + Gap;
                if (actionsH > 0f)
                    UiLayout.Window("shelfPanel", 739152, new Rect(x, y, Width, actionsH), _actionsFn, "");
            }
            finally
            {
                GUI.matrix = previous;
            }
        }

        // Block 0: the shelf's name - click it to type a new one, Enter (or Rename) to save it,
        // Esc to cancel. Saved through the game's own rename, so it sticks with the save.
        // (A hand-drawn text box: Unity's own text field is stripped in this game build.)
        private static string _nameEdit;   // null when not editing
        private static IntPtr _nameEditShelf;

        private static void DrawName()
        {
            if (_nameEdit != null && _nameEditShelf != _shelf.Pointer) _nameEdit = null; // another shelf opened
            bool editing = _nameEdit != null;
            string current = SupplyRuns.ShelfLabel(_shelf);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(editing ? _nameEdit + "|" : current, editing ? UiKit.DropAssigned : UiKit.Drop, GUILayout.Height(30f)))
            {
                if (!editing) { _nameEdit = ""; _nameEditShelf = _shelf.Pointer; } // start fresh: type the new name
            }
            if (GUILayout.Button(editing ? "Save" : "Rename", UiKit.On, GUILayout.Width(110f), GUILayout.Height(30f)))
            {
                if (editing) Rename(_nameEdit);
                else { _nameEdit = ""; _nameEditShelf = _shelf.Pointer; }
            }
            GUILayout.EndHorizontal();

            if (!editing) return;
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Rename(_nameEdit);
            else if (e.keyCode == KeyCode.Escape) _nameEdit = null;
            else if (e.keyCode == KeyCode.Backspace) { if (_nameEdit.Length > 0) _nameEdit = _nameEdit.Substring(0, _nameEdit.Length - 1); }
            else if (e.character != '\0' && !char.IsControl(e.character) && _nameEdit.Length < 40) _nameEdit += e.character;
            else return;
            e.Use();
        }

        private static void Rename(string name)
        {
            _nameEdit = null;
            name = name?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                _shelf.Configuration.Name.SetValue(name, true);
                if (_shelf.StorageEntity != null) _shelf.StorageEntity.StorageEntityName = name;
                try { Singleton<StorageMenu>.Instance.TitleLabel.text = name; } catch { }
                Core.Log.Msg($"Renamed the shelf to \"{name}\".");
            }
            catch (Exception ex)
            {
                Core.Log.Warning("Couldn't rename the shelf: " + ex.Message);
            }
        }

        // Block 1: everyone who uses this shelf, side by side - a card each with their face, name
        // and what they're carrying (product, and cash: green, or red at $0). Six to a row.
        private const int CardsPerRow = 6;
        private const float CardH = 108f;
        private static GUIStyle _card, _cardName, _cardCaption, _cardGo, _cardRed;

        private static void DrawOwner()
        {
            if (_cardName == null)
            {
                _card = new GUIStyle(_cell) { padding = new RectOffset(3, 3, 4, 4) };
                _cardName = new GUIStyle(_name) { fontSize = 12, alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
                _cardCaption = new GUIStyle(_caption) { alignment = TextAnchor.MiddleCenter };
                _cardGo = new GUIStyle(UiKit.CaptionGo) { alignment = TextAnchor.MiddleCenter };
                _cardRed = new GUIStyle(UiKit.CaptionRed) { alignment = TextAnchor.MiddleCenter };
            }
            // Cards share the row evenly (the window's inner width, less a pixel of grid line each).
            float cardW = Mathf.Floor((Width - 22f) / CardsPerRow) - 1f;

            GUILayout.BeginVertical(_grid);
            for (int i = 0; i < _users.Count; i += CardsPerRow)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < Mathf.Min(i + CardsPerRow, _users.Count); j++)
                {
                    Dealer d = _users[j];
                    GUILayout.BeginVertical(_card, GUILayout.Width(cardW), GUILayout.Height(CardH));
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); Portraits.Draw(d, 44f); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                    GUILayout.Space(3f);
                    GUILayout.Label(d.FirstName + " " + d.LastName, _cardName, GUILayout.Width(cardW - 6f));
                    GUILayout.Label(SupplyRuns.ProductCarried(d) + " items", _cardCaption, GUILayout.Width(cardW - 6f));
                    GUILayout.Label(MoneyManager.FormatAmount(d.Cash), d.Cash > 0f ? _cardGo : _cardRed, GUILayout.Width(cardW - 6f));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndVertical();
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (_shelfCash > 0f)
                GUILayout.Label("Cash on this shelf:  " + MoneyManager.FormatAmount(_shelfCash), _cashCell, GUILayout.Height(HeadH));
            GUILayout.EndVertical();
        }

        // Block 2: the table.
        private static void DrawDealers()
        {
            if (_dealers.Count == 0)
            {
                GUILayout.Label("You haven't hired any dealers yet.");
                return;
            }

            GUILayout.BeginVertical(_grid);

            GUILayout.BeginHorizontal();
            GUILayout.Label("", _headCell, GUILayout.Width(FaceW), GUILayout.Height(HeadH));
            GUILayout.Label("Dealer", _headCell, GUILayout.Width(NameW), GUILayout.Height(HeadH));
            GUILayout.Label("Product", _headCell, GUILayout.Width(ToggleW), GUILayout.Height(HeadH));
            GUILayout.Label("Earnings", _headCell, GUILayout.Width(ToggleW), GUILayout.Height(HeadH));
            GUILayout.EndHorizontal();

            foreach (Dealer d in _dealers)
            {
                bool supply = Assignments.IsAssigned(ShelfRole.Supply, _guid, d.ID);
                bool earnings = Assignments.IsAssigned(ShelfRole.Earnings, _guid, d.ID);

                GUILayout.BeginHorizontal();
                FaceCell(d, RowH, 44f);

                bool asking = _confirm != null && _confirm.Dealer == d.Pointer;

                GUILayout.BeginVertical(supply || earnings ? _cellMine : _cell, GUILayout.Width(NameW), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                Fixed(d.FirstName + " " + d.LastName, _name, NameW);
                if (asking)
                {
                    // Orange: this dealer already has a shelf for that - moving them is a choice.
                    Fixed(_confirm.Line1, UiKit.CaptionWarn, NameW);
                    Fixed(_confirm.Line2, UiKit.CaptionWarn, NameW);
                }
                else
                {
                    string activity = Activity(d, out bool alert);
                    Fixed(activity, Speech.OnTheWay(activity) ? UiKit.CaptionGo : alert ? UiKit.CaptionRed : _caption, NameW);
                    Fixed(ProductShelfLine(d), _caption, NameW);
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

                if (asking)
                {
                    if (ButtonCell("Continue", _on)) { var c = _confirm; _confirm = null; Toggle(c.Role, d); }
                    if (ButtonCell("Cancel", _off)) _confirm = null;
                }
                else
                {
                    if (ToggleCell(supply, "Product")) AskOrToggle(ShelfRole.Supply, d, supply);
                    if (ToggleCell(earnings, "Earnings")) AskOrToggle(ShelfRole.Earnings, d, earnings);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        // Block 3: send everyone who uses this shelf off to their shelves.
        private static void DrawActions()
        {
            if (GUILayout.Button("Send them to their shelves now", GUILayout.Height(28f)))
            {
                // Each dealer for what this shelf is to them: their earnings shelf means "put the
                // cash away" (plus a product top-up), their product shelf means "fetch product".
                foreach (Dealer d in _dealers)
                {
                    bool supply = Assignments.IsAssigned(ShelfRole.Supply, _guid, d.ID);
                    bool earnings = Assignments.IsAssigned(ShelfRole.Earnings, _guid, d.ID);
                    if (!supply && !earnings) continue;
                    SupplyRuns.SendNow(d, supply && earnings ? (ShelfRole?)null : supply ? ShelfRole.Supply : ShelfRole.Earnings);
                }
            }
            GUILayout.Label("Loose product is bagged up when they take it.", _caption);
        }

        // --- Cells ---

        // A label held to its column's width: long text is cut off inside the cell instead of
        // widening the column and pushing the buttons off the side.
        private static void Fixed(string text, GUIStyle style, float cellWidth) =>
            GUILayout.Label(text, style, GUILayout.Width(cellWidth - 16f)); // minus the cell's padding

        private static void FaceCell(Dealer d, float height, float faceSize)
        {
            GUILayout.BeginVertical(_cell, GUILayout.Width(FaceW), GUILayout.Height(height));
            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Portraits.Draw(d, Mathf.Min(faceSize, FaceW - 8f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        // A cell holding one toggle button; true when clicked.
        private static bool ToggleCell(bool on, string label)
        {
            GUILayout.BeginVertical(_cell, GUILayout.Width(ToggleW), GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            bool clicked = GUILayout.Button(on ? "Yes" : "No", on ? _on : _off, GUILayout.Height(26f));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            return clicked;
        }

        private static bool ButtonCell(string label, GUIStyle style)
        {
            GUILayout.BeginVertical(_cell, GUILayout.Width(ToggleW), GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            bool clicked = GUILayout.Button(label, style, GUILayout.Height(26f));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            return clicked;
        }

        // A "Yes" that would take a dealer off a shelf they already use (their product shelf
        // elsewhere, or this one for the other job) asks first, in orange, with Continue / Cancel.
        private sealed class Confirm
        {
            public IntPtr Dealer;
            public ShelfRole Role;
            public string Line1, Line2;
        }

        private static Confirm _confirm;

        private static void AskOrToggle(ShelfRole role, Dealer d, bool isOn)
        {
            _confirm = null;
            if (!isOn)
            {
                string job = role == ShelfRole.Supply ? "product" : "earnings";
                string otherJob = role == ShelfRole.Supply ? "earnings" : "product";
                string here = SupplyRuns.Quoted(_shelf);
                var current = SupplyRuns.ShelfFor(d, role);
                string line1 = null;
                if (current != null && current.Pointer != _shelf.Pointer)
                    line1 = $"{d.FirstName} already uses {SupplyRuns.Quoted(current)} for {job}.";
                else if (Assignments.IsAssigned(role == ShelfRole.Supply ? ShelfRole.Earnings : ShelfRole.Supply, _guid, d.ID))
                    line1 = $"{d.FirstName} already uses {here} for {otherJob}.";
                if (line1 != null)
                {
                    _confirm = new Confirm
                    {
                        Dealer = d.Pointer,
                        Role = role,
                        Line1 = line1,
                        Line2 = $"Use {here} for {Speech.Her(d)} {job} instead?",
                    };
                    return;
                }
            }
            Toggle(role, d);
        }

        private static void Toggle(ShelfRole role, Dealer d)
        {
            Assignments.Toggle(role, _guid, d.ID, SupplyRuns.RefreshShelves(force: true).Keys);
            bool on = Assignments.IsAssigned(role, _guid, d.ID);
            Core.Log.Msg($"{d.FirstName} {(on ? "now uses" : "no longer uses")} {SupplyRuns.ShelfLabel(_shelf)} for {(role == ShelfRole.Supply ? "product" : "earnings")}.");
            if (role == ShelfRole.Earnings && Assignments.IsAssigned(role, _guid, d.ID)) SupplyRuns.QueueDropOff(d);
        }

        // What they're up to: the trip they're on (e.g. "Taking cash to EARNINGS"), why "send now"
        // didn't work, or otherwise what they're carrying.
        // alert: a trip / waiting / send-now message, shown in red so it stands out.
        private static string Activity(Dealer d, out bool alert)
        {
            var said = Speech.LatestFor(d, 180f); // their latest message, for a few minutes
            if (said != null && !SupplyRuns.IsOnRun(d))
            {
                alert = said.Red;
                return said.Text;
            }
            alert = true;
            string trip = SupplyRuns.RunDescription(d) ?? SupplyRuns.WaitingDescription(d);
            if (trip != null) return trip;
            alert = false;
            return SupplyRuns.IdleDescription(d);
        }

        // Which shelf they get product from - spelled out when they don't have one.
        private static string ProductShelfLine(Dealer d)
        {
            var shelf = SupplyRuns.ShelfFor(d, ShelfRole.Supply);
            if (shelf == null) return "Product shelf: unassigned";
            return "Product shelf: " + (shelf.Pointer == _shelf.Pointer ? "this one" : SupplyRuns.ShelfLabel(shelf));
        }

        private static void EnsureStyles()
        {
            UiKit.EnsureStyles();
            if (_grid != null) return;
            _grid = UiKit.Grid; _cell = UiKit.Cell; _cellMine = UiKit.CellMine; _headCell = UiKit.HeadCell;
            _name = UiKit.Name; _nameGreen = UiKit.NameGreen; _caption = UiKit.Caption; _on = UiKit.On; _off = UiKit.Off;
            _cashCell = new GUIStyle(UiKit.HeadCell) { alignment = TextAnchor.MiddleLeft };
            _cashCell.normal.textColor = UiKit.Green;
        }
    }
}
