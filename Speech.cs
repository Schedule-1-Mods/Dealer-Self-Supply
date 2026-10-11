using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using UnityEngine;

namespace DealerSelfSupply
{
    // How dealers are talked about (she / he) and what they tell you. Their messages pop up in
    // the corner with their face, and are kept in the Dealer Self-supply app's message list. The "red"
    // ones - where they're off to, on a deal, can't go - light up the app's red dot. Nothing goes
    // to the phone's Messages app (and earlier versions' texts there are cleared out).
    internal static class Speech
    {
        private static readonly HashSet<string> Women = new HashSet<string> { "jane_lucero", "molly_presley" };

        public static bool IsWoman(Dealer d) => d != null && Women.Contains(d.ID);
        public static string She(Dealer d) => IsWoman(d) ? "She" : "He";   // "She'll go..."
        public static string Her(Dealer d) => IsWoman(d) ? "her" : "his";   // "on her way"
        public static string Him(Dealer d) => IsWoman(d) ? "her" : "him";   // "40 items on her"

        public sealed class Entry
        {
            public Dealer Dealer;
            public string Text;
            public bool Red;
            public string When;
            public float At; // Time.time
        }

        private const int MaxLog = 40;
        private static readonly List<Entry> _log = new List<Entry>();
        public static IReadOnlyList<Entry> Log => _log; // newest first

        // Red messages since the Dealer Self-supply app was last opened (its red dot).
        public static int Unread { get; set; }

        public static void Reset()
        {
            _log.Clear();
            Unread = 0;
        }

        // red: a status message (on the way, on a deal, can't go...) - counts towards the red dot.
        public static void Text(Dealer d, string message, bool red = true)
        {
            if (d == null) return;
            _log.Insert(0, new Entry { Dealer = d, Text = message, Red = red, When = GameClock(), At = Time.time });
            if (_log.Count > MaxLog) _log.RemoveAt(_log.Count - 1);
            if (red) Unread++;
            Toasts.Show(d, message, red);
        }

        // "On my way to ..." (or "On her way to ...", "Already on my way, boss.") - shown in green
        // so a dealer who's actually going stands out from the "$0, I'll go back home" ones.
        public static bool OnTheWay(string text) =>
            text != null && (text.StartsWith("On my way") || text.StartsWith("Already on my way")
                             || text.StartsWith("On her way") || text.StartsWith("On his way"));

        // A dealer's latest message, if it's recent (seconds).
        public static Entry LatestFor(Dealer d, float maxAge)
        {
            if (d == null) return null;
            var e = _log.FirstOrDefault(x => x.Dealer != null && x.Dealer.Pointer == d.Pointer);
            return e != null && Time.time - e.At <= maxAge ? e : null;
        }

        private static string GameClock()
        {
            try
            {
                int t = NetworkSingleton<TimeManager>.Instance.CurrentTime; // e.g. 1630
                int h = t / 100, m = t % 100;
                return $"{(h % 12 == 0 ? 12 : h % 12)}:{m:00} {(h < 12 ? "AM" : "PM")}";
            }
            catch { return ""; }
        }

    }
}
