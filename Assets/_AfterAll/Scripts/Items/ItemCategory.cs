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

        /// <summary>Carried loot (e.g. Echoes) — never occupies a hotbar slot, routes to a carry
        /// receiver instead (elevator stash IItemReceiver lands in S3).</summary>
        Loot,
    }
}
