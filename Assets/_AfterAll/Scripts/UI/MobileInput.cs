namespace AfterAll.UI
{
    /// <summary>Shared mobile input gate — kept in sync with MobileHUD simulate toggle.</summary>
    public static class MobileInput
    {
        public const float MoveZoneWidth       = 0.42f;
        public const float ActionStripHeight   = 0.22f;
        public const float TapDeadzonePixels   = 22f;
        public const float MaxTapDuration      = 0.28f;

        private static bool _simulateInEditor;

        public static void SetSimulateInEditor(bool simulate) => _simulateInEditor = simulate;

        public static bool IsActive
        {
            get
            {
#if UNITY_EDITOR
                return _simulateInEditor;
#elif UNITY_ANDROID || UNITY_IOS
                return true;
#else
                return false;
#endif
            }
        }
    }
}
