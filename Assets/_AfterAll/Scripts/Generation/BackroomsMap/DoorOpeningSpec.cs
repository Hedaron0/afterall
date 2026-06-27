namespace AfterAll.Generation.BackroomsMap
{
    /// <summary>
    /// Walkable wall gap with a door. Facing is the direction you step through from the door cell.
    /// </summary>
    public readonly struct DoorOpeningSpec
    {
        public readonly int X;
        public readonly int Y;
        public readonly CardinalDir Facing;

        public DoorOpeningSpec(int x, int y, CardinalDir facing)
        {
            X = x;
            Y = y;
            Facing = facing;
        }
    }
}
