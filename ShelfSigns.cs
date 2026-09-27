using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;

namespace DealerSupplyRun
{
    // A white name board on the front of every shelf a dealer uses (black text, e.g. Dealer
    // Earnings Shelf), so you can tell them apart at a glance. It follows the shelf's in-game
    // name (rename it from the shelf panel), and goes when no dealer uses the shelf any more.
    // Signs are local (just drawn, not saved or networked).
    internal static class ShelfSigns
    {
        private sealed class Sign
        {
            public GameObject Go;
            public TextMeshPro Text;
            public string Label;
        }

        private static readonly Dictionary<IntPtr, Sign> _signs = new Dictionary<IntPtr, Sign>();
        private static TMP_FontAsset _font;
        private static float _next;

        public static void Reset()
        {
            foreach (var s in _signs.Values)
                if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
            _signs.Clear();
            _font = null;
        }

        public static void Tick()
        {
            if (!Core.InMain || Time.time < _next) return;
            _next = Time.time + 2f;
            try
            {
                var keep = new HashSet<IntPtr>();
                foreach (var pair in SupplyRuns.RefreshShelves())
                {
                    var shelf = pair.Value;
                    if (shelf == null) continue;
                    if (!Assignments.HasAny(ShelfRole.Supply, pair.Key) && !Assignments.HasAny(ShelfRole.Earnings, pair.Key)) continue;
                    keep.Add(shelf.Pointer);

                    string label = SupplyRuns.ShelfLabel(shelf); // no quotes on the sign itself
                    if (_signs.TryGetValue(shelf.Pointer, out Sign sign) && sign.Go != null)
                    {
                        if (sign.Label != label) { sign.Text.text = label; sign.Label = label; }
                        continue;
                    }
                    sign = Make(shelf, label);
                    if (sign != null) _signs[shelf.Pointer] = sign;
                }

                foreach (var key in _signs.Keys.ToList())
                {
                    if (keep.Contains(key)) continue;
                    if (_signs[key].Go != null) UnityEngine.Object.Destroy(_signs[key].Go);
                    _signs.Remove(key);
                }
            }
            catch (Exception e)
            {
                Core.Log.Warning("Shelf signs failed: " + e.Message);
                _next = Time.time + 30f;
            }
        }

        private static Sign Make(PlaceableStorageEntity shelf, string label)
        {
            // The shelf's size in its own frame (so it follows the shelf's rotation), from its
            // visible parts - not counting any sign already on it.
            var t = shelf.transform;
            var renderers = shelf.GetComponentsInChildren<Renderer>();
            Bounds local = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null || r.gameObject.name.StartsWith("DealerShelfSign") || r.transform.parent?.name == "DealerShelfSign") continue;
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? wb.min.x : wb.max.x, (i & 2) == 0 ? wb.min.y : wb.max.y, (i & 4) == 0 ? wb.min.z : wb.max.z);
                    var p = t.InverseTransformPoint(corner);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) return null;

            // A rack is wide and shallow: the front is across its short side, on whichever face
            // dealers walk up to (its access point), else local +Z/+X.
            bool wideX = local.size.x >= local.size.z;
            Vector3 frontLocal = wideX ? Vector3.forward : Vector3.right;
            try
            {
                var points = shelf.AccessPoints;
                if (points != null && points.Length > 0 && points[0] != null)
                {
                    var ap = t.InverseTransformPoint(points[0].position) - local.center;
                    if ((wideX ? ap.z : ap.x) < 0f) frontLocal = -frontLocal;
                }
            }
            catch { }
            float wide = wideX ? local.size.x : local.size.z;
            float halfDepth = (wideX ? local.size.z : local.size.x) * 0.5f;

            float boardW = Mathf.Clamp(wide * 0.75f, 0.4f, 1.4f);
            const float boardH = 0.2f, thick = 0.015f;

            // Root: on the front face, just under the top rail, facing out. Unscaled, so the text
            // isn't stretched by the board.
            var root = new GameObject("DealerShelfSign");
            root.transform.SetParent(t, false);
            root.transform.localPosition = new Vector3(local.center.x, local.max.y - boardH * 0.5f - 0.03f, local.center.z)
                                           + frontLocal * (halfDepth + thick * 0.5f + 0.005f);
            root.transform.localRotation = Quaternion.LookRotation(-frontLocal); // forward points into the shelf

            // The white board.
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = "DealerShelfSignBoard";
            var col = board.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col); // just a picture - nothing to bump into
            board.transform.SetParent(root.transform, false);
            board.transform.localScale = new Vector3(boardW, boardH, thick);
            var mat = BoardMaterial();
            if (mat != null) board.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Black text on the outside face.
            var textGo = new GameObject("DealerShelfSignText");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0f, -(thick * 0.5f + 0.002f));
            var text = textGo.AddComponent<TextMeshPro>();
            var font = Font();
            if (font != null) text.font = font;
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.05f;
            text.fontSizeMax = 1.2f;
            text.rectTransform.sizeDelta = new Vector2(boardW * 0.9f, boardH * 0.75f);
            text.color = Color.black;
            text.fontStyle = FontStyles.Bold;
            return new Sign { Go = root, Text = text, Label = label };
        }

        // Plain white, in the game's own lit shader (so it's lit like everything else).
        private static Material _boardMat;
        private static Material BoardMaterial()
        {
            if (_boardMat != null) return _boardMat;
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                foreach (var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())
                {
                    var m = r.sharedMaterial;
                    if (m != null && m.shader != null && m.shader.name.Contains("Lit")) { lit = m.shader; break; }
                }
            }
            if (lit == null) return null;
            _boardMat = new Material(lit) { name = "DealerShelfSignBoard" };
            _boardMat.color = Color.white;
            if (_boardMat.HasProperty("_BaseColor")) _boardMat.SetColor("_BaseColor", Color.white);
            if (_boardMat.HasProperty("_Smoothness")) _boardMat.SetFloat("_Smoothness", 0.2f);
            _boardMat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _boardMat;
        }

        // The game's own UI font (from the storage menu's title), so signs match the game.
        private static TMP_FontAsset Font()
        {
            if (_font != null) return _font;
            try { _font = Singleton<StorageMenu>.Instance?.TitleLabel?.font; } catch { }
            if (_font == null)
            {
                try { _font = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>()?.font; } catch { }
            }
            return _font;
        }
    }
}
