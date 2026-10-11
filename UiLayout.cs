using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace DealerSelfSupply
{
    // Lets you drag the mod's panels anywhere on screen. Each group of windows (the shelf panel,
    // the talk-to-dealer blocks, the phone app) keeps one offset from where it normally sits, so
    // its windows move together. Offsets are saved on this PC (UserData), so every player keeps
    // their own layout.
    internal static class UiLayout
    {
        private static Dictionary<string, float[]> _offsets;
        private static bool _dirty;

        private static string FilePath => Path.Combine(MelonEnvironment.UserDataDirectory, "DealerSelfSupply_Layout.json");

        private static Dictionary<string, float[]> Offsets
        {
            get
            {
                if (_offsets != null) return _offsets;
                _offsets = new Dictionary<string, float[]>();
                try
                {
                    if (File.Exists(FilePath))
                        _offsets = JsonSerializer.Deserialize<Dictionary<string, float[]>>(File.ReadAllText(FilePath)) ?? _offsets;
                }
                catch (Exception e) { MelonLogger.Warning("[Dealer Self-supply] Couldn't read the panel layout: " + e.Message); }
                return _offsets;
            }
        }

        public static Vector2 Offset(string group) =>
            Offsets.TryGetValue(group, out var o) && o.Length == 2 ? new Vector2(o[0], o[1]) : Vector2.zero;

        // GUI.Window at its normal rect plus the group's offset. Dragging it (the window function
        // calls GUI.DragWindow) moves the whole group.
        public static void Window(string group, int id, Rect rect, GUI.WindowFunction fn, string title)
        {
            Vector2 o = Offset(group);
            // Never dragged (or saved) off screen: at least 80 px of the window stays visible.
            float scale = GUI.matrix.m00 > 0f ? GUI.matrix.m00 : 1f;
            float sw = Screen.width / scale, sh = Screen.height / scale;
            o.x = Mathf.Clamp(o.x, 80f - rect.xMax, sw - 80f - rect.x);
            o.y = Mathf.Clamp(o.y, -rect.y, sh - 40f - rect.y);
            var r = new Rect(rect.x + o.x, rect.y + o.y, rect.width, rect.height);
            Rect moved = GUI.Window(id, r, fn, title);
            if (moved.x != r.x || moved.y != r.y)
            {
                Vector2 n = o + new Vector2(moved.x - r.x, moved.y - r.y);
                Offsets[group] = new[] { n.x, n.y };
                _dirty = true;
            }
        }

        // Every window function ends with this: drag it by any spot that isn't a button.
        public static void Drag()
        {
            try { GUI.DragWindow(); } catch { }
        }

        // Saved once you let go of the mouse.
        public static void Tick()
        {
            if (!_dirty || Input.GetMouseButton(0)) return;
            _dirty = false;
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Offsets)); }
            catch (Exception e) { MelonLogger.Warning("[Dealer Self-supply] Couldn't save the panel layout: " + e.Message); }
        }
    }
}
