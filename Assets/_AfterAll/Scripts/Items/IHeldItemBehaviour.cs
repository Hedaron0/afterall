using AfterAll.Inventories;
using UnityEngine;

namespace AfterAll.Items
{
    public interface IHeldItemBehaviour
    {
        void OnEquipped(Inventory inventory, Camera camera, ItemDefinition item);
        void OnUnequipped();
    }
}
