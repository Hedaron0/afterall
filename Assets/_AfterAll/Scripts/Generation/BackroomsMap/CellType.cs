namespace AfterAll.Generation.BackroomsMap
{
    public enum CellType
    {
        Wall,
        Floor,
        Room,
        Exit,
        Pillar,
        DoorFrame
    }

    public static class CellTypeExtensions
    {
        public static bool IsWalkable(this CellType cell) =>
            cell is CellType.Floor or CellType.Room or CellType.Exit or CellType.DoorFrame;
    }
}
