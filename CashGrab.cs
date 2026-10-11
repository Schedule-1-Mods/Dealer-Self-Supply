using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using UnityEngine;

namespace DealerSelfSupply
{
    // Shift-clicking cash in an open storage (your EARNINGS shelf) puts the whole stack into your
    // cash in one go. The game's own shift-click only moves up to $1,000 a click. If this runs
    // first the slot is emptied and the game has nothing left to move; if the game ran first,
    // this takes what it left behind.
    internal static class CashGrab
    {
        public static void Tick()
        {
            if (!Input.GetMouseButtonDown(0) || !(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))) return;
            try
            {
                var menu = Singleton<StorageMenu>.Instance;
                if (menu == null || !menu.IsOpen || menu.OpenedStorageEntity == null) return;
                ItemSlot slot = Singleton<ItemUIManager>.Instance?.HoveredSlot?.assignedSlot;
                var cash = slot?.ItemInstance?.TryCast<CashInstance>();
                if (cash == null || slot.IsLocked || slot.IsRemovalLocked) return;

                // Only the storage's own slots, not your inventory's.
                var slots = menu.OpenedStorageEntity.ItemSlots;
                bool inStorage = false;
                for (int i = 0; i < slots.Count && !inStorage; i++) inStorage = slots[i].Pointer == slot.Pointer;
                if (!inStorage) return;

                float amount = cash.Balance;
                if (amount <= 0f) return;
                slot.ClearStoredInstance();
                NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(amount, true, true);
            }
            catch (Exception e)
            {
                Core.Log.Warning("Cash grab failed: " + e.Message);
            }
        }
    }
}
