using Il2CppScheduleOne.Economy;
using UnityEngine;

namespace DealerSelfSupply
{
    // Dealer messages on screen: a stack of cards near the top left (clear of the screen edge
    // and the game's own icons), each with the dealer's face, name and what they said - red for
    // status messages. Drawn by the mod itself rather than the game's corner pop-up, which is a
    // fixed width and cut them off.
    internal static class Toasts
    {
        // Laid out for 1080p and scaled with the screen height.
        private const float Left = 60f, Top = 56f, Width = 540f, CardH = 54f, Gap = 6f;
        private const int MaxCards = 7;

        private sealed class Card
        {
            public Dealer Dealer;
            public string Name, Text;
            public bool Red;
            public float Until;
        }

        private static readonly List<Card> _cards = new List<Card>(); // newest first
        private static GUIStyle _bg, _name, _text, _textRed, _textGo;

        public static void Reset() => _cards.Clear();

        public static void Show(Dealer d, string text, bool red)
        {
            if (!Core.Notifications.Value) return;
            _cards.Insert(0, new Card
            {
                Dealer = d,
                Name = d.FirstName + " " + d.LastName,
                Text = text,
                Red = red,
                Until = Time.time + Mathf.Max(2f, Core.NotificationSeconds.Value),
            });
            if (_cards.Count > MaxCards) _cards.RemoveAt(_cards.Count - 1);
        }

        public static void Draw()
        {
            if (!Core.InMain || _cards.Count == 0) return;
            _cards.RemoveAll(c => Time.time > c.Until);
            if (_cards.Count == 0) return;
            EnsureStyles();

            float s = Screen.height / 1080f;
            Matrix4x4 previous = GUI.matrix;
            if (!Mathf.Approximately(s, 1f)) GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
            try
            {
                float y = Top;
                foreach (var c in _cards)
                {
                    // Fade out over the last half second.
                    float alpha = Mathf.Clamp01((c.Until - Time.time) / 0.5f);
                    Color old = GUI.color;
                    GUI.color = new Color(1f, 1f, 1f, alpha);

                    GUI.Box(new Rect(Left, y, Width, CardH), GUIContent.none, _bg);
                    if (c.Dealer != null) Portraits.DrawAt(new Rect(Left + 6f, y + 6f, CardH - 12f, CardH - 12f), c.Dealer);
                    GUI.Label(new Rect(Left + CardH + 2f, y + 6f, Width - CardH - 10f, 20f), c.Name, _name);
                    GUI.Label(new Rect(Left + CardH + 2f, y + 27f, Width - CardH - 10f, 22f), c.Text, Speech.OnTheWay(c.Text) ? _textGo : c.Red ? _textRed : _text);

                    GUI.color = old;
                    y += CardH + Gap;
                }
            }
            finally { GUI.matrix = previous; }
        }

        private static void EnsureStyles()
        {
            if (_bg != null) return;
            UiKit.EnsureStyles();
            _bg = new GUIStyle();
            _bg.normal.background = UiKit.Solid(new Color(0.07f, 0.07f, 0.08f, 0.88f));
            _name = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            _name.normal.textColor = Color.white;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 12, clipping = TextClipping.Clip };
            _text.normal.textColor = new Color(0.9f, 0.9f, 0.92f);
            _textRed = new GUIStyle(_text) { fontStyle = FontStyle.Bold };
            _textRed.normal.textColor = new Color(1f, 0.36f, 0.3f);
            _textGo = new GUIStyle(_textRed); // "On my way ..." in green, so it stands out
            _textGo.normal.textColor = UiKit.Green;
        }
    }
}
