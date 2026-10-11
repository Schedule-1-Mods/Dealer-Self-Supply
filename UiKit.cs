using UnityEngine;

namespace DealerSelfSupply
{
    // The look shared by both panels, after the wiki's dealer table: dark cells separated by thin
    // grid lines, white text, green only for what's assigned. (The default IMGUI skin is one flat
    // grey, so every background here is a generated 1x1 texture.)
    internal static class UiKit
    {
        public static readonly Color Green = new Color(0.55f, 0.8f, 0.2f);

        public static GUIStyle Grid, Cell, CellMine, HeadCell, Title, Name, NameGreen, NameBig, Caption, CaptionGreen, CaptionRed, CaptionGo, CaptionWarn,
            On, Off, Button, Drop, DropAssigned, Entry, EntrySelected;
        private static GUIStyle _arrowDown, _arrowUp;

        public static void EnsureStyles()
        {
            if (Grid != null) return;
            var gridLine = new Color(0.24f, 0.24f, 0.26f);
            var cellBg = new Color(0.09f, 0.09f, 0.1f, 0.97f);
            var mineBg = new Color(0.1f, 0.2f, 0.12f, 0.97f);
            var headBg = new Color(0.14f, 0.14f, 0.15f, 0.97f);

            // The grid: a line-coloured backing with 1 px between the cells inside it.
            Grid = new GUIStyle { padding = new RectOffset(1, 1, 1, 1) };
            Grid.normal.background = Solid(gridLine);

            Cell = new GUIStyle { padding = new RectOffset(8, 8, 4, 4), margin = new RectOffset(0, 1, 0, 1) };
            Cell.normal.background = Solid(cellBg);
            CellMine = new GUIStyle(Cell);
            CellMine.normal.background = Solid(mineBg);

            HeadCell = new GUIStyle(Cell) { fontStyle = FontStyle.Bold, fontSize = 13, alignment = TextAnchor.MiddleCenter };
            HeadCell.normal.background = Solid(headBg);
            HeadCell.normal.textColor = Color.white;
            Title = new GUIStyle(HeadCell) { fontSize = 22, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 10, 6, 6) };

            Name = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            Name.normal.textColor = Color.white;
            NameGreen = new GUIStyle(Name);
            NameGreen.normal.textColor = Green;
            NameBig = new GUIStyle(Name) { fontSize = 19 };
            Caption = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false, clipping = TextClipping.Clip };
            Caption.normal.textColor = new Color(0.78f, 0.78f, 0.8f);
            CaptionGreen = new GUIStyle(Caption) { fontStyle = FontStyle.Bold, fontSize = 12 };
            CaptionGreen.normal.textColor = Green;
            CaptionRed = new GUIStyle(Caption) { fontStyle = FontStyle.Bold };
            CaptionRed.normal.textColor = new Color(1f, 0.33f, 0.28f);
            CaptionGo = new GUIStyle(CaptionRed); // "On my way ..." - same size as red, so rows don't shift
            CaptionGo.normal.textColor = Green;
            CaptionWarn = new GUIStyle(CaptionRed); // "Molly already uses ... Continue?"
            CaptionWarn.normal.textColor = new Color(1f, 0.62f, 0.15f);

            // Tight lines: the default label spacing makes table rows grow past their height.
            foreach (var s in new[] { Name, NameGreen, Caption, CaptionGreen, CaptionRed, CaptionGo, CaptionWarn })
            {
                s.margin = new RectOffset(0, 0, 0, 0);
                s.padding = new RectOffset(0, 0, 1, 1);
            }

            On = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
            SetBackground(On, new Color(0.2f, 0.45f, 0.14f), new Color(0.26f, 0.55f, 0.18f));
            Off = new GUIStyle(GUI.skin.button);
            SetBackground(Off, new Color(0.2f, 0.2f, 0.22f), new Color(0.28f, 0.28f, 0.3f));
            Button = new GUIStyle(Off) { margin = new RectOffset(0, 1, 0, 1) };

            // Dropdown headings: near-black bars, text on the left, arrow drawn at the right end.
            Drop = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 13,
                padding = new RectOffset(10, 30, 0, 0), margin = new RectOffset(0, 1, 0, 1), clipping = TextClipping.Clip };
            SetBackground(Drop, new Color(0.05f, 0.05f, 0.06f), new Color(0.13f, 0.13f, 0.15f));
            DropAssigned = new GUIStyle(Drop);
            SetBackground(DropAssigned, new Color(0.11f, 0.3f, 0.13f), new Color(0.15f, 0.38f, 0.17f));

            Entry = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 13, clipping = TextClipping.Clip,
                wordWrap = false, padding = new RectOffset(10, 6, 0, 0), margin = new RectOffset(0, 1, 0, 1) };
            SetBackground(Entry, new Color(0.15f, 0.15f, 0.17f), new Color(0.24f, 0.24f, 0.27f));
            EntrySelected = new GUIStyle(Entry) { fontStyle = FontStyle.Bold };
            SetBackground(EntrySelected, new Color(0.12f, 0.36f, 0.16f), new Color(0.16f, 0.45f, 0.2f));

            _arrowDown = new GUIStyle();
            _arrowDown.normal.background = Arrow(down: true);
            _arrowUp = new GUIStyle();
            _arrowUp.normal.background = Arrow(down: false);
        }

        // A dropdown heading bar: label on the left, arrow at the right end. True when clicked.
        public static bool DropButton(string text, bool open, bool assigned, float height)
        {
            // (GUILayoutUtility.GetLastRect is stripped in this game build: reserve the rect first.)
            GUIStyle style = assigned ? DropAssigned : Drop;
            // (Same GetRect overload the portraits use, known to work.)
            Rect r = GUILayoutUtility.GetRect(10f, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            r.width -= 1f; // grid line on the right
            bool clicked = GUI.Button(r, text, style);
            GUI.Box(new Rect(r.xMax - 22f, r.y + (r.height - 8f) * 0.5f, 12f, 8f), GUIContent.none, open ? _arrowUp : _arrowDown);
            GUILayout.Space(1f); // grid line below
            return clicked;
        }

        public static Texture2D Solid(Color c)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, c);
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset; // keep it across scene loads
            return tex;
        }

        public static void SetBackground(GUIStyle style, Color normal, Color hover)
        {
            style.normal.background = Solid(normal);
            style.hover.background = Solid(hover);
            style.active.background = Solid(hover);
            style.focused.background = Solid(normal);
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Color.white;
        }

        // A copy of a (readable) picture at size x size with rounded corners, like the game's
        // app icons. radius is a fraction of the size.
        public static Texture2D RoundedCopy(Texture2D src, int size, float radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * radius;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color c = src.GetPixelBilinear((x + 0.5f) / size, (y + 0.5f) / size);
                // Distance into a rounded corner (0 outside the corner squares).
                float dx = Mathf.Max(0f, Mathf.Max(r - (x + 0.5f), (x + 0.5f) - (size - r)));
                float dy = Mathf.Max(0f, Mathf.Max(r - (y + 0.5f), (y + 0.5f) - (size - r)));
                float edge = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                c.a *= dx > 0f && dy > 0f ? edge : 1f;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        // A filled circle on a transparent square (for notification dots).
        public static Texture2D Circle(Color c, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float edge = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy)); // soft 1 px rim
                tex.SetPixel(x, y, new Color(c.r, c.g, c.b, c.a * edge));
            }
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }

        // A small white triangle, 24 x 16, pointing down (or up).
        private static Texture2D Arrow(bool down)
        {
            const int w = 24, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++)
            {
                // Texture rows count up from the bottom: a down arrow is widest at the top.
                float half = (down ? y + 1f : h - y) * (w * 0.5f) / h;
                for (int x = 0; x < w; x++)
                    tex.SetPixel(x, y, Mathf.Abs(x + 0.5f - w * 0.5f) <= half ? Color.white : Color.clear);
            }
            tex.Apply();
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return tex;
        }
    }
}
