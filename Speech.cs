using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Messaging;
using UnityEngine;

namespace DealerSupplyRun
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
            _scrubbed = false;
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

        // --- Clearing out the texts 1.9.0 sent to the Messages app ---

        private static bool _scrubbed;
        private static readonly string[] OurTexts =
        {
            "On my way to the ", "Heading over to the ", "On a deal right now, I'll head over after.",
            "Busy right now, I'll head over after.", "Dropped off $", "Grabbed ", "No room for the cash",
            "Can't go: ", "Got it, I'll grab my product from the ", "Got it, I'll drop my cash in the ",
            "Okay, I'll wait for you to bring me product.", "Okay, I'll hang on to the cash.",
            "Pockets full", "Nothing to grab on the ", "Only unpackaged product on the ", "No cash to grab",
            "No shelf set", "Can't reach the ",
        };

        private static bool IsOurs(string text) =>
            text != null && OurTexts.Any(t => text.StartsWith(t) || text.Contains(" items from the ") && text.EndsWith("Back to work."));

        public static void ScrubTexts()
        {
            if (_scrubbed) return;
            _scrubbed = true;
            int removed = 0;
            foreach (Dealer d in SupplyRuns.HiredDealers())
            {
                try
                {
                    var convo = d.MSGConversation;
                    if (convo == null) continue;
                    bool changed = false;

                    var history = convo.messageHistory;
                    for (int i = history.Count - 1; i >= 0; i--)
                    {
                        var m = history[i];
                        if (m == null || m.sender != Message.ESenderType.Other || !IsOurs(m.text)) continue;
                        history.RemoveAt(i);
                        removed++;
                        changed = true;
                    }

                    var bubbles = convo.bubbles;
                    for (int i = bubbles.Count - 1; i >= 0; i--)
                    {
                        var b = bubbles[i];
                        if (b == null || !IsOurs(b.text)) continue;
                        bubbles.RemoveAt(i);
                        UnityEngine.Object.Destroy(b.gameObject);
                        changed = true;
                    }

                    if (changed)
                    {
                        convo.RefreshPreviewText();
                        convo.SetRead(true); // no unread dot left behind by our texts
                    }
                }
                catch (Exception e)
                {
                    Core.Log.Warning($"Couldn't clear old texts from {d.FirstName}: {e.Message}");
                }
            }
            if (removed > 0) Core.Log.Msg($"Cleared {removed} old dealer text(s) from the Messages app.");
        }
    }
}
