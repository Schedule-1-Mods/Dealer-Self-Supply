using System.Reflection;
using System.Text.Json;
using MelonLoader;

namespace DealerSupplyRun
{
    internal enum ShelfRole { Supply, Earnings }

    // Which dealers use which shelf, per role: shelf GUID -> dealer IDs. Shelf GUIDs are random
    // per placed object, so one file safely holds every save's assignments side by side - only
    // the shelves that exist in the loaded save ever match.
    internal static class Assignments
    {
        private class Data
        {
            public Dictionary<string, List<string>> Supply { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, List<string>> Earnings { get; set; } = new Dictionary<string, List<string>>();
        }

        private static Data _data;

        private static string FilePath =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "DealerSupplyRun_Shelves.json");

        private static Data Loaded
        {
            get
            {
                if (_data != null) return _data;
                _data = new Data();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        try { _data = JsonSerializer.Deserialize<Data>(json) ?? _data; }
                        catch (JsonException)
                        {
                            // 1.0.0 files were just the supply map.
                            _data.Supply = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json) ?? _data.Supply;
                        }
                        _data.Supply ??= new Dictionary<string, List<string>>();
                        _data.Earnings ??= new Dictionary<string, List<string>>();
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.Warning("[DealerSupplyRun] Couldn't read shelf assignments: " + e.Message);
                }
                return _data;
            }
        }

        private static Dictionary<string, List<string>> Map(ShelfRole role) =>
            role == ShelfRole.Supply ? Loaded.Supply : Loaded.Earnings;

        public static bool IsAssigned(ShelfRole role, string shelfGuid, string dealerId) =>
            Map(role).TryGetValue(shelfGuid, out var ids) && ids.Contains(dealerId);

        public static IReadOnlyList<string> DealersOn(ShelfRole role, string shelfGuid) =>
            Map(role).TryGetValue(shelfGuid, out var ids) ? ids : (IReadOnlyList<string>)Array.Empty<string>();

        public static bool HasAny(ShelfRole role, string shelfGuid) =>
            Map(role).TryGetValue(shelfGuid, out var ids) && ids.Count > 0;

        public static IEnumerable<string> ShelvesFor(ShelfRole role, string dealerId) =>
            Map(role).Where(p => p.Value.Contains(dealerId)).Select(p => p.Key);

        // A dealer has one shelf per role per save: this replaces their shelf among the shelves
        // in the loaded save (liveShelves), leaving other saves' assignments alone.
        public static void Assign(ShelfRole role, string shelfGuid, string dealerId, ICollection<string> liveShelves)
        {
            RemoveFrom(role, dealerId, liveShelves);
            var map = Map(role);
            if (!map.TryGetValue(shelfGuid, out var list)) map[shelfGuid] = list = new List<string>();
            // Several dealers can share a shelf, for product or earnings.
            list.Add(dealerId);
            // One shelf can't be both a dealer's product shelf and their earnings shelf.
            RemoveFrom(role == ShelfRole.Supply ? ShelfRole.Earnings : ShelfRole.Supply, dealerId, new[] { shelfGuid });
            Save();
        }

        public static void Clear(ShelfRole role, string dealerId, ICollection<string> liveShelves)
        {
            RemoveFrom(role, dealerId, liveShelves);
            Save();
        }

        public static void Toggle(ShelfRole role, string shelfGuid, string dealerId, ICollection<string> liveShelves)
        {
            if (IsAssigned(role, shelfGuid, dealerId)) Clear(role, dealerId, liveShelves);
            else Assign(role, shelfGuid, dealerId, liveShelves);
        }

        private static void RemoveFrom(ShelfRole role, string dealerId, ICollection<string> liveShelves)
        {
            var map = Map(role);
            foreach (string guid in liveShelves)
            {
                if (map.TryGetValue(guid, out var ids) && ids.Remove(dealerId) && ids.Count == 0)
                    map.Remove(guid);
            }
        }

        private static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Loaded, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                MelonLogger.Warning("[DealerSupplyRun] Couldn't save shelf assignments: " + e.Message);
            }
        }
    }
}
