namespace AfterAll.Items
{
    /// <summary>
    /// What kind of item this is — drives pickup routing and UI behaviour.
    /// Only Hotbar / KeyItem use the 3-slot hotbar today; others hook in via IItemReceiver later.
    /// </summary>
    public enum ItemCategory
    {
        Hotbar,
        KeyItem,
        Consumable,
        Ammo,
    }
}
