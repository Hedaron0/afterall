namespace AfterAll.Items
{
    /// <summary>
    /// Anything on the player that can accept a picked-up item (hotbar, ammo pool, health, etc.).
    /// </summary>
    public interface IItemReceiver
    {
        bool CanReceive(ItemDefinition item);
        bool TryReceive(ItemDefinition item, int amount = 1);
    }
}
