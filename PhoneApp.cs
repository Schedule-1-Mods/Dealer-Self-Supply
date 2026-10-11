using System.Reflection;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI.Phone;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DealerSelfSupply
{
    // A "Dealer Self-supply" app on the phone's home screen. Tapping it opens an overview of every
    // dealer - face, product shelf, earnings shelf, what they're doing, what they're carrying -
    // with one button that sends them all to their shelves. Host only, like the rest.
    internal static class PhoneApp
    {
        private const string Title = "Dealer Self-supply";
        private const float Width = 1180f;
        private const float RowH = 56f, HeadH = 26f, FaceW = 60f, NameW = 380f, ShelfW = 250f;
        private const float MessageSeconds = 180f; // how long a dealer's latest message shows in their row

        private static GameObject _icon;
        private static float _nextTry;
        private static bool _open;
        private static GUI.WindowFunction _fn;

        public static void Reset()
        {
            _icon = null;
            _shownBadge = -1;
            _open = false;
        }

        private static bool PhoneOpen()
        {
            try { return PlayerSingleton<Phone>.Instance != null && PlayerSingleton<Phone>.Instance.IsOpen; }
            catch { return false; }
        }

        // Adds the icon once the phone's home screen exists (and again after a scene change).
        public static void Tick()
        {
            if (_open && !PhoneOpen()) _open = false; // phone put away
            UpdateBadge();
            if (Time.time < _nextTry || !SupplyRuns.IsHost) return;
            _nextTry = Time.time + 2f;
            if (_icon != null) { KeepIconPlaced(); return; }
            try { CreateIcon(); }
            catch (Exception e) { Core.Log.Warning("Couldn't add the phone app: " + e.Message); _nextTry = Time.time + 30f; }
        }

        // Where the home screen's app icons live. Phone mods built on S1API move them into a
        // scrolling list; the icon has to go there too, or it sits in the old, hidden grid.
        private static Transform IconParent(HomeScreen home)
        {
            try
            {
                var scroll = home.transform.Find("AppIconsScrollView/Viewport/AppIcons");
                if (scroll != null) return scroll;
            }
            catch { }
            return home.appIconContainer;
        }

        // Keeps the icon in the right list if another mod rearranges the home screen after us.
        private static void KeepIconPlaced()
        {
            try
            {
                var home = PlayerSingleton<HomeScreen>.Instance;
                if (home == null || _icon == null) return;
                var parent = IconParent(home);
                if (parent != null && _icon.transform.parent != parent)
                {
                    _icon.transform.SetParent(parent, false);
                    Core.Log.Msg("Moved the Dealer Self-supply app icon into the phone's app list.");
                }
            }
            catch { }
        }

        private static void CreateIcon()
        {
            var home = PlayerSingleton<HomeScreen>.Instance;
            if (home == null || home.appIconPrefab == null || home.appIconContainer == null) return;

            _icon = UnityEngine.Object.Instantiate(home.appIconPrefab, IconParent(home));
            _icon.name = "DealerSupplyApp";
            var label = _icon.transform.Find("Label")?.GetComponent<Text>();
            if (label != null) label.text = Title;

            var image = _icon.transform.Find("Mask")?.GetChild(0)?.GetComponent<Image>();
            if (image != null)
            {
                Sprite sprite = LoadIcon() ?? DealersIconSprite(home);
                if (sprite != null) image.sprite = sprite;
            }

            var button = _icon.GetComponent<Button>();
            if (button != null)
            {
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener((UnityAction)(Action)(() => { _open = true; Speech.Unread = 0; }));
                try { home.appIcons.Add(button); } catch { }
            }
            Core.Log.Msg("Added the Dealer Self-supply app to the phone.");
        }

        // The red number on the icon: dealer texts since you last opened the app (hidden at 0).
        private static int _shownBadge = -1;

        private static void UpdateBadge()
        {
            if (_icon == null) return;
            if (_open) Speech.Unread = 0;
            if (Speech.Unread == _shownBadge) return;
            _shownBadge = Speech.Unread;
            try
            {
                var badge = _icon.transform.Find("Notifications");
                if (badge == null) return;
                badge.gameObject.SetActive(Speech.Unread > 0);
                var text = badge.Find("Text")?.GetComponent<Text>();
                if (text != null) text.text = Speech.Unread > 99 ? "99+" : Speech.Unread.ToString();
            }
            catch { }
        }

        // --- Always on screen: the app's icon at the top left, next to the game's Messages icon,
        // with a red dot while there are red messages you haven't seen in the app. ---

        private const float HudX = 36f, HudY = 10f, HudSize = 24f; // fallback if the game's icon can't be found
        private static GUIStyle _hudIcon, _hudDot;
        private static bool _hudTried;
        private static readonly Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3> _corners =
            new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);

        public static void DrawHud()
        {
            // Only while a dealer has said something you haven't seen in the app yet.
            if (!Core.InMain || !SupplyRuns.IsHost || _icon == null || Speech.Unread == 0) return;
            if (!_hudTried)
            {
                _hudTried = true;
                var sprite = LoadIcon();
                if (sprite != null)
                {
                    _hudIcon = new GUIStyle();
                    // Rounded corners, like the game's Messages icon.
                    _hudIcon.normal.background = UiKit.RoundedCopy(sprite.texture, 128, 0.24f);
                }
                _hudDot = new GUIStyle();
                _hudDot.normal.background = UiKit.Circle(new Color(0.92f, 0.2f, 0.2f), 32);
            }
            if (_hudIcon == null) return;
            if (GamePopupsShowing()) return; // don't cover the game's pop-ups (e.g. "Wages paid")

            Rect r = HudRect();
            GUI.Box(r, GUIContent.none, _hudIcon);
            float dot = r.height * 0.42f;
            GUI.Box(new Rect(r.xMax - dot * 0.6f, r.y - dot * 0.4f, dot, dot), GUIContent.none, _hudDot);
        }

        // The game's own pop-ups stack down the top-left corner, right where this icon sits.
        private static bool GamePopupsShowing()
        {
            try
            {
                var entries = Singleton<Il2CppScheduleOne.UI.NotificationsManager>.Instance?.entries;
                return entries != null && entries.Count > 0;
            }
            catch { return false; }
        }

        // To the right of the game's own Messages icon (top left): same size and height, with a
        // clear gap between them. Always that spot, so it never lands on the Messages icon.
        private static Rect HudRect()
        {
            try
            {
                var hud = Singleton<Il2CppScheduleOne.UI.HUD>.Instance;
                var prompt = hud?.UnreadMessagesPrompt;
                if (prompt != null)
                {
                    prompt.GetWorldCorners(_corners);
                    Camera cam = hud.canvas != null && hud.canvas.renderMode != RenderMode.ScreenSpaceOverlay ? hud.canvas.worldCamera : null;
                    Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, _corners[0]); // bottom left
                    Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]); // top right
                    float size = b.y - a.y;
                    if (size > 4f)
                    {
                        return new Rect(b.x + size * 0.7f, Screen.height - b.y, size, size);
                    }
                }
            }
            catch { }
            return new Rect(HudX, HudY, HudSize, HudSize);
        }

        // The app icon shipped in the mod (Assets/app_icon.png).
        private static Sprite LoadIcon()
        {
            try
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DealerSelfSupply.app_icon.png");
                if (stream == null) return null;
                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2);
                if (!ImageConversion.LoadImage(tex, bytes)) return null;
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch { return null; }
        }

        // Without a shipped icon, borrow the game's Dealers app picture.
        private static Sprite DealersIconSprite(HomeScreen home)
        {
            var parent = home.appIconContainer;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.Find("Label")?.GetComponent<Text>()?.text != "Dealers") continue;
                return child.Find("Mask")?.GetChild(0)?.GetComponent<Image>()?.sprite;
            }
            return null;
        }

        // --- The app window ---

        public static void Draw()
        {
            if (!_open || !Core.InMain || !SupplyRuns.IsHost || !PhoneOpen()) return;
            UiKit.EnsureStyles();
            _fn ??= (GUI.WindowFunction)(Action<int>)(_ => { DrawWindow(); UiLayout.Drag(); });

            var dealers = SupplyRuns.HiredDealers().ToList();
            float h = 30f + 70f + HeadH + 2f + Math.Max(1, dealers.Count) * (RowH + 1f) + 90f;
            float scale = Mathf.Min(1f, (Screen.height - 40f) / h, (Screen.width - 40f) / Width);
            Matrix4x4 previous = GUI.matrix;
            if (scale < 1f) GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            try
            {
                float x = (Screen.width / scale - Width) * 0.5f, y = (Screen.height / scale - h) * 0.5f;
                UiLayout.Window("app", 740400, new Rect(x, y, Width, h), _fn, "");
            }
            finally { GUI.matrix = previous; }
        }

        private static void DrawWindow()
        {
            SupplyRuns.RefreshShelves();
            var dealers = SupplyRuns.HiredDealers().ToList();

            // Heading.
            GUILayout.BeginVertical(UiKit.Grid);
            GUILayout.BeginVertical(UiKit.Cell, GUILayout.Height(60f));
            GUILayout.Label("Dealer Self-supply", UiKit.NameBig);
            GUILayout.Label("Who's assigned to which shelf, and what they're up to", UiKit.Caption);
            GUILayout.EndVertical();
            GUILayout.EndVertical();
            GUILayout.Space(8f);

            // The table.
            GUILayout.BeginVertical(UiKit.Grid);
            GUILayout.BeginHorizontal();
            Head("", FaceW); Head("Dealer", NameW); Head("Product shelf", ShelfW); Head("Earnings shelf", ShelfW);
            GUILayout.Label("On them", UiKit.HeadCell, GUILayout.Height(HeadH));
            GUILayout.EndHorizontal();

            if (dealers.Count == 0)
                GUILayout.Label("You haven't hired any dealers yet.", UiKit.HeadCell, GUILayout.Height(RowH));

            foreach (Dealer d in dealers)
            {
                var supply = SupplyRuns.ShelfFor(d, ShelfRole.Supply);
                var earnings = SupplyRuns.ShelfFor(d, ShelfRole.Earnings);
                bool any = supply != null && earnings != null; // green only when both shelves are set

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(UiKit.Cell, GUILayout.Width(FaceW), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal(); GUILayout.FlexibleSpace(); Portraits.Draw(d, 44f); GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

                GUILayout.BeginVertical(any ? UiKit.CellMine : UiKit.Cell, GUILayout.Width(NameW), GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                Fixed(d.FirstName + " " + d.LastName, UiKit.Name, NameW);
                string activity = Activity(d, out bool alert);
                Fixed(activity, Speech.OnTheWay(activity) ? UiKit.CaptionGo : alert ? UiKit.CaptionRed : UiKit.Caption, NameW);
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();

                ShelfCell(supply);
                ShelfCell(earnings, showCash: true);

                GUILayout.BeginVertical(UiKit.Cell, GUILayout.Height(RowH));
                GUILayout.FlexibleSpace();
                GUILayout.Label(SupplyRuns.ProductCarried(d) + " items", UiKit.Caption);
                GUILayout.Label(MoneyManager.FormatAmount(d.Cash) + " cash", d.Cash > 0f ? UiKit.CaptionGo : UiKit.CaptionRed); // green with cash, red at $0
                GUILayout.FlexibleSpace();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(8f);

            // Actions.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Put cash away", UiKit.On, GUILayout.Height(34f))) SendAll(dealers, ShelfRole.Earnings);
            if (GUILayout.Button("Collect product", UiKit.On, GUILayout.Height(34f))) SendAll(dealers, ShelfRole.Supply);
            if (GUILayout.Button("Both: cash, then product", UiKit.On, GUILayout.Height(34f))) SendAll(dealers, null);
            if (GUILayout.Button("Close", UiKit.Off, GUILayout.Width(120f), GUILayout.Height(34f))) _open = false;
            GUILayout.EndHorizontal();
            GUILayout.Label("Set a dealer's shelves by talking to them (E), or from a shelf's panel.", UiKit.Caption);
        }

        // Everyone with a shelf for that job (only: null = both) heads off - or once they're free.
        private static void SendAll(List<Dealer> dealers, ShelfRole? only)
        {
            foreach (Dealer d in dealers)
            {
                bool hasSupply = SupplyRuns.ShelfFor(d, ShelfRole.Supply) != null;
                bool hasEarnings = SupplyRuns.ShelfFor(d, ShelfRole.Earnings) != null;
                if (only == ShelfRole.Supply ? !hasSupply : only == ShelfRole.Earnings ? !hasEarnings : !hasSupply && !hasEarnings) continue;
                SupplyRuns.SendNow(d, only); // they say where they're off to (shown in their row)
            }
        }

        private static void Head(string text, float width) =>
            GUILayout.Label(text, UiKit.HeadCell, GUILayout.Width(width), GUILayout.Height(HeadH));

        private static void Fixed(string text, GUIStyle style, float cellWidth) =>
            GUILayout.Label(text, style, GUILayout.Width(cellWidth - 16f));

        private static void ShelfCell(PlaceableStorageEntity shelf, bool showCash = false)
        {
            GUILayout.BeginVertical(shelf != null ? UiKit.CellMine : UiKit.Cell, GUILayout.Width(ShelfW), GUILayout.Height(RowH));
            GUILayout.FlexibleSpace();
            if (shelf == null)
            {
                Fixed("Unassigned", UiKit.Caption, ShelfW);
            }
            else
            {
                Fixed(SupplyRuns.ShelfLabel(shelf), UiKit.NameGreen, ShelfW);
                string place = PlaceOf(shelf);
                if (showCash) place = MoneyManager.FormatAmount(SupplyRuns.CashOn(shelf)) + " on it" + (place.Length > 0 ? " - " + place : "");
                Fixed(place, UiKit.Caption, ShelfW);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
        }

        private static string PlaceOf(PlaceableStorageEntity shelf)
        {
            try { return shelf.ParentProperty?.PropertyName ?? ""; }
            catch { return ""; }
        }

        // Same as the shelf panel: a trip / waiting / send-now message (red), else what they carry.
        private static string Activity(Dealer d, out bool alert)
        {
            // Their latest message ("Nothing to grab on the ..., I'll go back home.") for a few
            // minutes, then what they're doing now.
            var said = Speech.LatestFor(d, MessageSeconds);
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
    }
}
