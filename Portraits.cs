using System.Reflection;
using Il2CppScheduleOne.Economy;
using UnityEngine;

namespace DealerSupplyRun
{
    // Draws a dealer's face in the IMGUI panels. GUI.DrawTexture is stripped in this game build,
    // so the picture goes in as a box background (stretched to fit). Where the face comes from,
    // first that works:
    //   1. a picture shipped in the mod (Assets/Faces/<dealer id>.png, embedded in the DLL);
    //   2. the game's own mugshot - cut out of its sprite atlas onto a texture of its own;
    //   3. the dealer's initials.
    internal static class Portraits
    {
        private static readonly Dictionary<string, Texture2D> _faces = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, GUIStyle> _styles = new Dictionary<string, GUIStyle>();
        private static GUIStyle _initials;

        public static void Draw(Dealer d, float size) =>
            DrawAt(GUILayoutUtility.GetRect(size, size, GUILayout.Width(size), GUILayout.Height(size)), d);

        // At a fixed spot on screen (outside a layout).
        public static void DrawAt(Rect r, Dealer d)
        {
            GUIStyle style = StyleFor(d);
            if (style != null)
            {
                GUI.Box(r, GUIContent.none, style);
                return;
            }
            _initials ??= new GUIStyle(GUI.skin.box) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            string initials = (d.FirstName.Length > 0 ? d.FirstName.Substring(0, 1) : "") + (d.LastName.Length > 0 ? d.LastName.Substring(0, 1) : "");
            GUI.Box(r, initials, _initials);
        }

        private static GUIStyle StyleFor(Dealer d)
        {
            string id = d.ID;
            if (_styles.TryGetValue(id, out GUIStyle cached)) return cached;

            Texture2D tex = LoadShipped(id) ?? CutMugshot(d);
            GUIStyle style = null;
            if (tex != null)
            {
                style = new GUIStyle();
                style.normal.background = tex;
            }
            _styles[id] = style; // null too, so we don't retry every frame
            return style;
        }

        private static Texture2D LoadShipped(string dealerId)
        {
            if (_faces.TryGetValue(dealerId, out Texture2D t)) return t;
            Texture2D tex = null;
            try
            {
                using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DealerSupplyRun.Faces." + dealerId + ".png");
                if (stream != null)
                {
                    byte[] bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    tex = new Texture2D(2, 2);
                    if (!ImageConversion.LoadImage(tex, bytes)) tex = null;
                    else tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Couldn't load the face for {dealerId}: {e.Message}");
                tex = null;
            }
            _faces[dealerId] = tex;
            return tex;
        }

        // The game's mugshot sprite usually lives in a shared atlas - drawn as a background it
        // would show the whole atlas. Copy just its part onto a texture of its own on the GPU.
        private static Texture2D CutMugshot(Dealer d)
        {
            try
            {
                Sprite sprite = d.MugshotSprite;
                Texture2D atlas = sprite != null ? sprite.texture : null;
                if (atlas == null) return null;
                Rect sr = sprite.textureRect;
                int w = Mathf.Max(1, Mathf.RoundToInt(sr.width)), h = Mathf.Max(1, Mathf.RoundToInt(sr.height));
                if (w == atlas.width && h == atlas.height) return atlas;

                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(atlas, rt, new Vector2(sr.width / atlas.width, sr.height / atlas.height), new Vector2(sr.x / atlas.width, sr.y / atlas.height));
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                return tex;
            }
            catch (Exception e)
            {
                Core.Log.Warning($"Couldn't copy {d.FirstName}'s mugshot: {e.Message}");
                return null;
            }
        }

        // Scene change: the game's textures may be gone; the shipped faces are kept.
        public static void Reset() => _styles.Clear();
    }
}
