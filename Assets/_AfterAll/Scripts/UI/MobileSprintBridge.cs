namespace AfterAll.UI
{
    /// <summary>
    /// Static bridge so SprintButtonUI can signal PlayerMovement without a direct reference.
    /// PC: always false (PC sprint is driven by the Input Action).
    /// Mobile: toggled by SprintButtonUI tap.
    /// </summary>
    public static class MobileSprintBridge
    {
        public static bool WantsSprint { get; private set; }

        public static void SetWantsSprint(bool value) => WantsSprint = value;
    }
}
