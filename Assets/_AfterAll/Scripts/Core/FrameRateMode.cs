namespace AfterAll.Core
{
    /// <summary>
    /// Frame-rate presets. Settings UI can bind dropdowns to these values.
    /// </summary>
    public enum FrameRateMode
    {
        /// <summary>Match the device's highest reported display refresh rate.</summary>
        Unlimited = 0,
        Cap30 = 30,
        Cap60 = 60,
        Cap90 = 90,
        Cap120 = 120,
        Custom = -1,
    }
}
