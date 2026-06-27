using System.Collections.Generic;

namespace AfterAll.Generation.BackroomsMap
{
    public enum VentKind
    {
        Short,
        Long,
        Unrelated
    }

    public enum VentConnectionType
    {
        DeadEnd,
        WallMid,
        Multi
    }

    public sealed class VentSpec
    {
        public List<(int x, int y)> Path = new();
        public VentKind Kind;
        public VentConnectionType ConnType;
        public bool HasLadder;
    }
}
