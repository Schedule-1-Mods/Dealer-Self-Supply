using MelonLoader;
using MelonLoader.Preferences;

[assembly: MelonInfo(typeof(DealerSelfSupply.Core), "✽ Dealer Self-supply", "1.21.0", "Cassy")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace DealerSelfSupply
{
    // Dealers look after themselves. Talk to a hired dealer (or open a shelf) to give them a
    // supply shelf and an EARNINGS shelf: once they sell out they walk over and grab product,
    // and on their way home from dealing they drop the cash off in EARNINGS.
    // Everything runs on the host; the item moves go through the game's own networked slots.
    public class Core : MelonMod
    {
        internal static MelonLogger.Instance Log;

        internal static MelonPreferences_Entry<float> CheckInterval;
        internal static MelonPreferences_Entry<int> RestockPercent;
        internal static MelonPreferences_Entry<bool> RestockWhenSoldOut;
        internal static MelonPreferences_Entry<bool> RestockAtNight;
        internal static MelonPreferences_Entry<float> SpeedMultiplier;
        internal static MelonPreferences_Entry<float> MaxTravelSeconds;
        internal static MelonPreferences_Entry<float> EmptyShelfCooldown;
        internal static MelonPreferences_Entry<bool> Notifications;
        internal static MelonPreferences_Entry<float> NotificationSeconds;
        internal static MelonPreferences_Entry<bool> LogSlowFrames;

        internal static bool InMain;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;

            string cfg = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "DealerSelfSupply.cfg");
            string[] keys =
            {
                "CheckIntervalSeconds", "RestockWhenEmptyPercent", "RestockWhenSoldOut", "RestockAtNight", "TravelSpeedMultiplier",
                "MaxTravelSeconds", "EmptyShelfCooldownSeconds", "ShowNotifications", "NotificationSeconds", "LogSlowFrames",
            };
            // Before MelonLoader reads it: one table, current settings only (it ignores a file with a table twice).
            string repaired = SettingsFile.Repair(cfg, keys);
            if (repaired != null) Log.Warning(repaired);

            var prefs = MelonPreferences.CreateCategory("DealerSelfSupply", "Dealer Self-supply");
            // Limits keep a hand-edited file from producing nonsense (e.g. a 0 second travel limit would warp every trip).
            CheckInterval = prefs.CreateEntry("CheckIntervalSeconds", 20f, "How often (real seconds) dealers check whether they need a shelf run", validator: new ValueRange<float>(2f, 600f));
            RestockPercent = prefs.CreateEntry("RestockWhenEmptyPercent", 40, "With RestockWhenSoldOut off: go for more product once this percentage of slots is empty (1-100)", validator: new ValueRange<int>(1, 100));
            RestockWhenSoldOut = prefs.CreateEntry("RestockWhenSoldOut", true, "A dealer goes for more product only once every slot has sold out (false: use RestockWhenEmptyPercent)");
            RestockAtNight = prefs.CreateEntry("RestockAtNight", true, "Dealers who are in bed (inside their home at night) also go for product when sold out (false: they sleep on)");
            SpeedMultiplier = prefs.CreateEntry("TravelSpeedMultiplier", 1.5f, "How much faster than normal dealers move on a shelf run", validator: new ValueRange<float>(0.5f, 4f));
            MaxTravelSeconds = prefs.CreateEntry("MaxTravelSeconds", 300f, "If a dealer still hasn't reached a shelf after this long, they're warped there", validator: new ValueRange<float>(30f, 1800f));
            EmptyShelfCooldown = prefs.CreateEntry("EmptyShelfCooldownSeconds", 120f, "How long a dealer waits before checking again after finding nothing to do", validator: new ValueRange<float>(0f, 3600f));
            Notifications = prefs.CreateEntry("ShowNotifications", true, "Pop up a notification when a dealer restocks or drops off cash");
            NotificationSeconds = prefs.CreateEntry("NotificationSeconds", 10f, "How long those pop-ups stay on screen", validator: new ValueRange<float>(2f, 60f));
            LogSlowFrames = prefs.CreateEntry("LogSlowFrames", false, "Write a line to the log whenever one of the mod's jobs takes over 8 ms (for tracking down stutters)");

            // Its own settings file, written straight away - easy to find, and there even before
            // the game has been closed once (MelonPreferences.cfg is only written on exit).
            try
            {
                prefs.SetFilePath(cfg, true, false);
                prefs.SaveToFile(false);
                Log.Msg("Settings: " + cfg);
            }
            catch (Exception e) { Log.Warning("Couldn't set up the settings file: " + e.Message); }

            // Every on-screen panel and every per-frame job runs through Safe: a failure stays in that one.
            MelonEvents.OnGUI.Subscribe(() => Safe.Run("Shelf panel", ShelfPanel.Draw));
            MelonEvents.OnGUI.Subscribe(() => Safe.Run("Talk panel", DealerMenu.Draw));
            MelonEvents.OnGUI.Subscribe(() => Safe.Run("Phone app", PhoneApp.Draw));
            MelonEvents.OnGUI.Subscribe(() => Safe.Run("Phone icon", PhoneApp.DrawHud));
            MelonEvents.OnGUI.Subscribe(() => Safe.Run("Messages", Toasts.Draw));

            Log.Msg($"Dealer Self-supply {Info.Version} loaded (game {UnityEngine.Application.version}). Talk to a dealer or open a shelf to set up their shelves.");
            Safe.Run("Game version check", () => Log.Msg("[Compat] " + Compat.Describe()));
            Safe.Run("Deal hold patch", () => HoldDealsPatch.Apply(HarmonyInstance));
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            InMain = sceneName == "Main";
            Safe.Reset();
            Compat.Reset();
            Crossings.Reset();
            SupplyRuns.Reset();
            DealerMenu.Reset();
            Portraits.Reset();
            PhoneApp.Reset();
            Speech.Reset();
            Toasts.Reset();
            ShelfSigns.Reset();
        }

        public override void OnUpdate()
        {
            if (!InMain) return;
            Safe.Run("Cash grab", _cashGrab);
            Safe.Run("Phone app tick", _phone);
            Safe.Run("Shelf signs", _signs);
            Safe.Run("Panel layout", _layout);
            Safe.Run("Dealer jobs", _runs);
        }

        // (Delegates made once: a new one per frame would be garbage every frame.)
        private static readonly Action _cashGrab = () => { using (new Probe("Dealer CashGrab")) CashGrab.Tick(); };
        private static readonly Action _phone = () => { using (new Probe("Dealer PhoneApp")) PhoneApp.Tick(); };
        private static readonly Action _signs = () => { using (new Probe("Dealer ShelfSigns")) ShelfSigns.Tick(); };
        private static readonly Action _layout = () => { using (new Probe("Dealer UiLayout")) UiLayout.Tick(); };
        private static readonly Action _runs = () => { using (new Probe("Dealer SupplyRuns")) SupplyRuns.Tick(); };
    }
}
