using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using UnityEngine;

namespace DealerSupplyRun
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
        private const float X = 20f, Top = 40f, Width = 640f, Gap = 10f;
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
                return;
            }
            if (_shelf == null || _shelf.Pointer != shelf.Pointer)
            {
                _shelf = shelf;
                SupplyRuns.RefreshShelves();
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

            float ownerH = _users.Count == 0 ? 0f
                : WindowChrome + HeadH + 1f + _users.Count * (OwnerRowH + 1f) + (_shelfCash > 0f ? HeadH + 1f : 0f) + 4f;
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
                float x = X / scale, y = Top / scale;
                UiLayout.Window("shelf", 739149, new Rect(x, y, Width, nameH), _nameFn, "Shelf name (shown on its sign)");
                y += nameH + Gap;
                if (ownerH > 0f)
                {
                    UiLayout.Window("shelf", 739150, new Rect(x, y, Width, ownerH), _ownerFn, "Whose shelf");
                    y += ownerH + Gap;
                }
                UiLayout.Window("shelf", 739151, new Rect(x, y, Width, listH), _dealersFn, "Dealers using this shelf");
                y += listH + Gap;
                if (actionsH > 0f)
                    UiLayout.Window("shelf", 739152, new Rect(x, y, Width, actionsH), _actionsFn, "");
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

        // Block 1: everyone who uses this shelf - face | name + what it's for | what they carry.
        private static void DrawOwner()
        {
            GUILayout.BeginVertical(_grid);
            GUILayout.BeginHorizontal();
            GUILayout.Label("", _headCell, GUILayout.Width(FaceW), GUILayout.Height(HeadH));
            GUILayout.Label("Assigned to", _headCell, GUILayout.Width(NameW), GUILayout.Height(HeadH));
            GUILayout.Label("On them now", _headCell, GUILayout.Height(HeadH));
            GUILayout.EndHorizontal();

            foreach (Dealer d in _users)
            {
                bool supply = Assignments.IsAssigned(ShelfRole.Supply, _guid, d.ID);
                bool earnings = Assignments.IsAssigned(ShelfRole.Earnings, _guid, d.ID);

                GUILayout.BeginHorizontal();
                FaceCell(d, OwnerRowH, 34f);

                GUILayout.BeginVertical(_cellMine, GUILayout.Width(NameW), GUILayout.Height(OwnerRowH));
                GUILayout.FlexibleSpace();
                Fixed(d.FirstName + " " + d.LastName, _nameGreen, NameW);
                Fixed(supply && earnings ? "Product + earnings" : supply ? "Takes product" : "Drops earnings", _caption, NameW);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

                GUILayout.BeginVertical(_cell, GUILayout.Height(OwnerRowH));
                GUILayout.FlexibleSpace();
                GUILayout.Label(SupplyRuns.ProductCarried(d) + " items of product", _caption);
                GUILayout.Label(MoneyManager.FormatAmount(d.Cash) + " cash", _caption);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
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

                GUILayout.BeginVertical(supply || earnings ? _cellMine : _cell, GUILayout.Width(NameW), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                Fixed(d.FirstName + " " + d.LastName, supply || earnings ? _nameGreen : _name, NameW);
                string activity = Activity(d, out bool alert);
                Fixed(activity, alert ? UiKit.CaptionRed : _caption, NameW);
                Fixed(ProductShelfLine(d), _caption, NameW);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

                if (ToggleCell(supply, "Product")) Toggle(ShelfRole.Supply, d);
                if (ToggleCell(earnings, "Earnings")) Toggle(ShelfRole.Earnings, d);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        // Block 3: send everyone who uses this shelf off to their shelves.
        private static void DrawActions()
        {
            if (GUILayout.Button("Send them to their shelves now", GUILayout.Height(28f)))
            {
                foreach (Dealer d in _dealers)
                {
                    if (Assignments.IsAssigned(ShelfRole.Supply, _guid, d.ID) || Assignments.IsAssigned(ShelfRole.Earnings, _guid, d.ID))
                        SupplyRuns.SendNow(d); // they say where they're off to (shown in their row)
                }
            }
            GUILayout.Label("Only packaged product is taken.", _caption);
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

        private static void Toggle(ShelfRole role, Dealer d)
        {
            Assignments.Toggle(role, _guid, d.ID, SupplyRuns.RefreshShelves().Keys);
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
