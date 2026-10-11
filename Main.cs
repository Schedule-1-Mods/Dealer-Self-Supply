using MelonLoader;

[assembly: MelonInfo(typeof(DealerSelfSupply.Core), "✽ Dealer Self-supply", "1.19.2", "Cassy")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace DealerSelfSupply
{
    // Dealers look after themselves. Talk to a hired dealer (or open a shelf) to give them a
    // supply shelf and an EARNINGS shelf: once they sell out they walk over and grab packaged
    // product, and on their way home from dealing they drop the cash off in EARNINGS.
    // Everything runs on the host; the item moves go through the game's own networked slots.
    public class Core : MelonMod
    {
        internal static MelonLogger.Instance Log;

        internal static MelonPreferences_Entry<float> CheckInterval;
        internal static MelonPreferences_Entry<int> RestockPercent;
        internal static MelonPreferences_Entry<bool> RestockWhenSoldOut;
        internal static MelonPreferences_Entry<float> SpeedMultiplier;
        internal static MelonPreferences_Entry<float> MaxTravelSeconds;
        internal static MelonPreferences_Entry<float> EmptyShelfCooldown;
        internal static MelonPreferences_Entry<bool> Notifications;
        internal static MelonPreferences_Entry<float> NotificationSeconds;

        internal static bool InMain;

        public override void OnInitializeMelon()
        {
            Log = LoggerInstance;

            string cfg = Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "DealerSelfSupply.cfg");
            // Settings saved before 1.17.8 sit under the mod's old working name - keep them.
            try
            {
                if (File.Exists(cfg))
                {
                    string text = File.ReadAllText(cfg);
                    if (text.Contains("[DealerSupplyRun]")) File.WriteAllText(cfg, text.Replace("[DealerSupplyRun]", "[DealerSelfSupply]"));
                }
            }
            catch { }

            var prefs = MelonPreferences.CreateCategory("DealerSelfSupply", "Dealer Self-supply");
            CheckInterval = prefs.CreateEntry("CheckIntervalSeconds", 20f, "How often (real seconds) dealers check whether they need a shelf run");
            RestockPercent = prefs.CreateEntry("RestockWhenEmptyPercent", 40, "With RestockWhenSoldOut off: go for more product once this percentage of slots is empty (1-100)");
            RestockWhenSoldOut = prefs.CreateEntry("RestockWhenSoldOut", true, "A dealer goes for more product only once every slot has sold out (false: use RestockWhenEmptyPercent)");
            SpeedMultiplier = prefs.CreateEntry("TravelSpeedMultiplier", 1.5f, "How much faster than normal dealers move on a shelf run");
            MaxTravelSeconds = prefs.CreateEntry("MaxTravelSeconds", 300f, "If a dealer still hasn't reached a shelf after this long, they're warped there");
            EmptyShelfCooldown = prefs.CreateEntry("EmptyShelfCooldownSeconds", 120f, "How long a dealer waits before checking again after finding nothing to do");
            Notifications = prefs.CreateEntry("ShowNotifications", true, "Pop up a notification when a dealer restocks or drops off cash");
            NotificationSeconds = prefs.CreateEntry("NotificationSeconds", 10f, "How long those pop-ups stay on screen");

            // Its own settings file, written straight away - easy to find, and there even before
            // the game has been closed once (MelonPreferences.cfg is only written on exit).
            try
            {
                prefs.SetFilePath(cfg, true, false);
                prefs.SaveToFile(false);
                Log.Msg("Settings: " + cfg);
            }
            catch (Exception e) { Log.Warning("Couldn't set up the settings file: " + e.Message); }

            MelonEvents.OnGUI.Subscribe(ShelfPanel.Draw);
            MelonEvents.OnGUI.Subscribe(DealerMenu.Draw);
            MelonEvents.OnGUI.Subscribe(PhoneApp.Draw);
            MelonEvents.OnGUI.Subscribe(PhoneApp.DrawHud);
            MelonEvents.OnGUI.Subscribe(Toasts.Draw);
            Log.Msg($"Dealer Self-supply {Info.Version} loaded (game {UnityEngine.Application.version}). Talk to a dealer or open a shelf to set up their shelves.");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            InMain = sceneName == "Main";
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
            using (new Probe("Dealer CashGrab")) CashGrab.Tick();
            using (new Probe("Dealer PhoneApp")) PhoneApp.Tick();
            using (new Probe("Dealer ShelfSigns")) ShelfSigns.Tick();
            using (new Probe("Dealer UiLayout")) UiLayout.Tick();
            using (new Probe("Dealer SupplyRuns")) SupplyRuns.Tick();
        }
    }
}
