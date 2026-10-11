using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.UI;
using Il2CppTMPro;
using UnityEngine;

namespace DealerSelfSupply
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
            public List<TextMeshPro> Texts; // one per board (both sides of the shelf)
            public string Label;
            public string Who; // the dealers whose faces are on it
        }

        // Everyone using the shelf, for product or earnings, in hiring order.
        private static List<Dealer> Users(string guid, List<Dealer> dealers) =>
            dealers.Where(d => Assignments.IsAssigned(ShelfRole.Supply, guid, d.ID) || Assignments.IsAssigned(ShelfRole.Earnings, guid, d.ID)).ToList();

        private static readonly Dictionary<IntPtr, Sign> _signs = new Dictionary<IntPtr, Sign>();
        private static TMP_FontAsset _font;
        private static float _next;

        public static void Reset()
        {
            foreach (var s in _signs.Values)
                if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
            _signs.Clear();
            _font = null;
            _faceMats.Clear(); // mugshot textures can go with the scene
        }

        public static void Tick()
        {
            if (!Core.InMain || Time.time < _next) return;
            _next = Time.time + 2f;
            try
            {
                var keep = new HashSet<IntPtr>();
                var dealers = SupplyRuns.HiredDealers().ToList();
                foreach (var pair in SupplyRuns.RefreshShelves())
                {
                    var shelf = pair.Value;
                    if (shelf == null) continue;
                    if (!Assignments.HasAny(ShelfRole.Supply, pair.Key) && !Assignments.HasAny(ShelfRole.Earnings, pair.Key)) continue;
                    keep.Add(shelf.Pointer);

                    string label = SupplyRuns.ShelfLabel(shelf); // no quotes on the sign itself
                    List<Dealer> users = Users(pair.Key, dealers);
                    string who = string.Join(",", users.Select(u => u.ID));
                    if (_signs.TryGetValue(shelf.Pointer, out Sign sign) && sign.Go != null)
                    {
                        if (sign.Who == who)
                        {
                            if (sign.Label != label) { foreach (var tx in sign.Texts) tx.text = label; sign.Label = label; }
                            continue;
                        }
                        UnityEngine.Object.Destroy(sign.Go); // someone joined or left: new faces, new layout
                    }
                    sign = Make(shelf, label, users);
                    if (sign != null) { sign.Who = who; _signs[shelf.Pointer] = sign; }
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

        private static Sign Make(PlaceableStorageEntity shelf, string label, List<Dealer> users)
        {
            // The shelf's size in its own frame (so it follows the shelf's rotation), from its
            // visible parts - not counting any sign already on it.
            // Measured from each mesh's own bounds, not the world-aligned renderer bounds: on a
            // rotated grid (Greenhouse Island's) those balloon out and throw the sign off the side.
            var t = shelf.transform;
            Bounds local = default;
            bool any = false;
            foreach (var mf in shelf.GetComponentsInChildren<MeshFilter>())
            {
                if (mf == null || mf.sharedMesh == null || mf.gameObject.name.StartsWith("DealerShelfSign")) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled) continue;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x, (i & 2) == 0 ? mb.min.y : mb.max.y, (i & 4) == 0 ? mb.min.z : mb.max.z);
                    var p = t.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                    else local.Encapsulate(p);
                }
            }
            if (!any) return null;

            // Which way it faces. Where the game marks the spot workers stand to use it (its access
            // point), that's the front - right for safes and lockers, which are as deep as they are
            // wide. Otherwise it's a rack: wide and shallow, so the front is a long face. A board
            // goes on the front and the back, so the name reads from either side.
            bool wideX = local.size.x >= local.size.z;
            try
            {
                var points = shelf.AccessPoints;
                if (points != null && points.Length > 0 && points[0] != null)
                {
                    var ap = t.InverseTransformPoint(points[0].position) - local.center;
                    if (Mathf.Abs(ap.x) > 0.05f || Mathf.Abs(ap.z) > 0.05f) wideX = Mathf.Abs(ap.z) >= Mathf.Abs(ap.x);
                }
            }
            catch { }
            Vector3 frontLocal = wideX ? Vector3.forward : Vector3.right;
            float wide = wideX ? local.size.x : local.size.z;
            float halfDepth = (wideX ? local.size.z : local.size.x) * 0.5f;

            // Small: a label, not a banner - sized to the face so it fits a safe as well as a
            // big rack (about 60% of the width, up to 70 cm, and a fifth as tall).
            float boardW = Mathf.Clamp(wide * 0.6f, 0.12f, 0.7f);
            float boardH = Mathf.Clamp(boardW * 0.2f, 0.035f, 0.09f);
            const float thick = 0.01f;

            // The faces of the dealers using it, in a row at the left of the name, so you can tell
            // whose shelf it is at a glance. The board grows to make room (up to nearly the full
            // width of the face it sits on); with a crowd, the faces shrink instead.
            var faces = users.Select(Portraits.TextureFor).Where(tx => tx != null).ToList();
            float face = boardH * 0.8f, gap = boardH * 0.1f, pad = boardH * 0.1f;
            float textW = boardW * 0.9f;
            if (faces.Count > 0)
            {
                float facesW = faces.Count * face + (faces.Count - 1) * gap + pad;
                float newW = Mathf.Min(boardW + facesW, Mathf.Max(boardW, wide * 0.95f));
                float room = newW * 0.6f; // faces get at most 60% of the board
                if (facesW > room)
                {
                    float k = room / facesW;
                    face *= k; gap *= k; facesW = room;
                }
                boardW = newW;
                textW = (boardW - facesW - pad) * 0.92f;
            }
            float facesRight = faces.Count > 0 ? -boardW * 0.5f + pad + faces.Count * face + (faces.Count - 1) * gap : -boardW * 0.5f;
            float textX = faces.Count > 0 ? (facesRight + boardW * 0.5f) * 0.5f : 0f;

            var root = new GameObject("DealerShelfSign");
            root.transform.SetParent(t, false);
            var texts = new List<TextMeshPro>();
            foreach (var side in new[] { frontLocal, -frontLocal })
            {
                // Each board: just under the top rail, flush on that face, facing out. Unscaled
                // holder, so the text isn't stretched by the board.
                var holder = new GameObject("DealerShelfSignSide");
                holder.transform.SetParent(root.transform, false);
                holder.transform.localPosition = new Vector3(local.center.x, local.max.y - boardH * 0.5f - 0.02f, local.center.z)
                                                 + side * (halfDepth + thick * 0.5f + 0.005f);
                holder.transform.localRotation = Quaternion.LookRotation(-side); // forward points into the shelf
                SnapToSurface(holder.transform, shelf.transform, thick);

                // The white board.
                var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
                board.name = "DealerShelfSignBoard";
                var col = board.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.Destroy(col); // just a picture - nothing to bump into
                board.transform.SetParent(holder.transform, false);
                board.transform.localScale = new Vector3(boardW, boardH, thick);
                var mat = BoardMaterial();
                if (mat != null) board.GetComponent<MeshRenderer>().sharedMaterial = mat;

                // Black text on its outside face.
                var textGo = new GameObject("DealerShelfSignText");
                textGo.transform.SetParent(holder.transform, false);
                textGo.transform.localPosition = new Vector3(textX, 0f, -(thick * 0.5f + 0.002f));
                var text = textGo.AddComponent<TextMeshPro>();
                var font = Font();
                if (font != null) text.font = font;
                text.text = label;
                text.alignment = TextAlignmentOptions.Center;
                text.enableWordWrapping = false;
                text.enableAutoSizing = true;
                text.fontSizeMin = 0.05f;
                text.fontSizeMax = 1.2f;
                text.rectTransform.sizeDelta = new Vector2(textW, boardH * 0.75f);
                text.color = Color.black;
                text.fontStyle = FontStyles.Bold;
                texts.Add(text);

                for (int i = 0; i < faces.Count; i++)
                {
                    var pic = GameObject.CreatePrimitive(PrimitiveType.Quad); // faces out, like the text
                    pic.name = "DealerShelfSignFace";
                    var pc = pic.GetComponent<Collider>();
                    if (pc != null) UnityEngine.Object.Destroy(pc);
                    pic.transform.SetParent(holder.transform, false);
                    pic.transform.localPosition = new Vector3(-boardW * 0.5f + pad + face * 0.5f + i * (face + gap), 0f, -(thick * 0.5f + 0.001f));
                    pic.transform.localScale = new Vector3(face, face, 1f);
                    var fm = FaceMaterial(faces[i]);
                    if (fm != null) pic.GetComponent<MeshRenderer>().sharedMaterial = fm;
                }
            }
            return new Sign { Go = root, Texts = texts, Label = label };
        }

        // The outline includes handles and hinges sticking out (a safe's door handle), which
        // would leave the board floating off the face. Cast a short ray back at the object and sit
        // the board flush on the solid surface it hits - if it hits one of this object's own
        // colliders; otherwise keep the outline position.
        private static void SnapToSurface(Transform board, Transform shelf, float thick)
        {
            try
            {
                Vector3 outward = -board.forward;
                Vector3 origin = board.position + outward * 0.6f;
                RaycastHit best = default;
                float bestDist = float.MaxValue;
                foreach (var hit in Physics.RaycastAll(origin, -outward, 1.5f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider == null || !hit.collider.transform.IsChildOf(shelf)) continue;
                    if (hit.collider.transform.IsChildOf(board.parent)) continue; // our own board
                    if (hit.distance < bestDist) { bestDist = hit.distance; best = hit; }
                }
                if (bestDist < float.MaxValue)
                    board.position = best.point + outward * (thick * 0.5f + 0.002f);
            }
            catch { }
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

        // A dealer's face, unlit so it reads like a printed photo in any light.
        private static readonly Dictionary<IntPtr, Material> _faceMats = new Dictionary<IntPtr, Material>();
        private static Material FaceMaterial(Texture2D tex)
        {
            if (_faceMats.TryGetValue(tex.Pointer, out Material m) && m != null) return m;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) return null;
            m = new Material(shader) { name = "DealerShelfSignFace" };
            var pic = OnWhite(tex);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", pic);
            m.mainTexture = pic;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            m.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _faceMats[tex.Pointer] = m;
            return m;
        }

        // The face pictures have see-through backgrounds, which an opaque material draws black.
        // Paint the picture onto white (the board's colour) so the background blends into the board.
        private static Texture2D OnWhite(Texture2D src)
        {
            try
            {
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    Color32 c = px[i];
                    int a = c.a;
                    px[i] = new Color32((byte)((c.r * a + 255 * (255 - a)) / 255), (byte)((c.g * a + 255 * (255 - a)) / 255),
                                        (byte)((c.b * a + 255 * (255 - a)) / 255), 255);
                }
                var tex = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = src.name + " (on white)" };
                tex.SetPixels32(px);
                tex.Apply(true, true); // mipmaps, then free the CPU copy
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return tex;
            }
            catch (Exception e)
            {
                Core.Log.Warning("Couldn't put a dealer's face on white: " + e.Message);
                return src;
            }
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
