namespace AfterAll.Environment
{
    public enum FluorescentLightTier
    {
        Off = 0,
        EmissionOnly = 1,
        /// <summary>Point light only (beyond spotLightMaxDistance).</summary>
        CorridorPartial = 2,
        /// <summary>Spot + Point (within spotLightMaxDistance).</summary>
        Full = 3,
    }
}
