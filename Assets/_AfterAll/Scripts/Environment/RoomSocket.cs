using UnityEngine;

namespace AfterAll.Environment
{
    public enum SocketDirection
    {
        Unknown = 0,
        North = 1,
        East = 2,
        South = 3,
        West = 4
    }

    /// <summary>
    /// Connection point on a room. Parent of room root — flat Y rotation only.
    /// </summary>
    public class RoomSocket : MonoBehaviour
    {
        private const float OrthogonalStepDegrees = 90f;
        [SerializeField] private WallGapController _wall;
        [SerializeField] private float _width = 1.3f;
        [SerializeField] private SocketDirection _direction = SocketDirection.Unknown;
        [SerializeField] private int _wallIndex = -1;
        [SerializeField] private string _socketTag = "Default";
        [SerializeField] private string _sizeClass = "M";

        public WallGapController Wall => _wall;
        public float Width => _width;
        public SocketDirection Direction => _direction;
        public int WallIndex => _wallIndex;
        public string SocketTag => _socketTag;
        public string SizeClass => _sizeClass;
        public bool IsConnected { get; set; }
        public bool HasValidContract => _direction != SocketDirection.Unknown;

        public void Bind(WallGapController wall, float width)
        {
            _wall = wall;
            _width = width;
        }

        public void SetContract(SocketDirection direction, string socketTag, string sizeClass)
        {
            _direction = direction;
            _socketTag = string.IsNullOrWhiteSpace(socketTag) ? "Default" : socketTag;
            _sizeClass = string.IsNullOrWhiteSpace(sizeClass) ? "M" : sizeClass;
        }

        public void SetWallIndex(int index) => _wallIndex = index;

        public string DebugContractLabel() => $"{_direction}:{_socketTag}:{_sizeClass}";

        public static bool AreDirectionsOpposite(SocketDirection a, SocketDirection b)
        {
            if (a == SocketDirection.Unknown || b == SocketDirection.Unknown)
                return false;

            return a switch
            {
                SocketDirection.North => b == SocketDirection.South,
                SocketDirection.East => b == SocketDirection.West,
                SocketDirection.South => b == SocketDirection.North,
                SocketDirection.West => b == SocketDirection.East,
                _ => false
            };
        }

        public void AlignAt(Vector3 worldCenter, Vector3 outward, float width)
        {
            _width = width;
            gameObject.SetActive(true);
            transform.position = worldCenter;
            transform.rotation = YawOnlyRotation(outward, quantizeOrthogonal: true);
            _direction = DirectionFromForward(transform.forward);
        }

        public static void SnapRoom(RoomInstance childRoom, RoomSocket childSocket, RoomSocket parentSocket)
        {
            Transform root = childRoom.transform;
            Transform childT = childSocket.transform;
            Transform parentT = parentSocket.transform;

            // Capture pivot BEFORE changing room rotation (childT.position moves with the room).
            Vector3 pivot = childT.position;
            Quaternion targetRot = YawOnlyRotation(parentT.forward, quantizeOrthogonal: true) * Quaternion.Euler(0f, 180f, 0f);
            Quaternion childRot = YawOnlyRotation(childT.forward, quantizeOrthogonal: true);
            Quaternion delta = targetRot * Quaternion.Inverse(childRot);

            root.rotation = delta * root.rotation;
            root.position = delta * (root.position - pivot) + parentT.position;

            // Landing the two sockets on the same point puts the two rooms' wall slabs in the SAME
            // volume, because a socket sits on its wall's mid-plane (measured: the slab spans
            // -0.125..+0.125 around it). That leaves the parent's inner face exactly coplanar with the
            // child's OUTER face — one lit, one baked against the void and therefore black — so which
            // one draws is decided per pixel by depth precision and the wall flickers black as the
            // camera moves. Anything sitting inside that shared volume (a door frame is spawned right
            // in it) fights the same way.
            //
            // Backing the child off by the two half-thicknesses puts the slabs side by side instead,
            // which is also what the planner already assumes: RoomFootprint's AABB edge sits on the
            // OUTER wall face, so plan space has the rooms meeting outer face to outer face. Without
            // this the built floor sits one wall thickness tighter than the plan it came from, and
            // every connected pair overlaps by that much.
            float separation = HalfThicknessAlongNormal(parentSocket) + HalfThicknessAlongNormal(childSocket);
            if (separation > 0.0001f)
                root.position += parentT.forward * separation;
        }

        /// <summary>
        /// Half the wall's depth measured along its own outward normal, or 0 when the wall has no
        /// measurable geometry (portal-style OpenEnd walls, whose pieces are hidden).
        /// </summary>
        private static float HalfThicknessAlongNormal(RoomSocket socket)
        {
            if (socket == null || socket.Wall == null)
                return 0f;

            Vector3 normal = FlattenDirection(socket.transform.forward);
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            foreach (Renderer renderer in socket.Wall.GetComponentsInChildren<Renderer>(true))
            {
                Bounds b = renderer.bounds;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(x, y, z));
                    float t = Vector3.Dot(corner - socket.transform.position, normal);
                    min = Mathf.Min(min, t);
                    max = Mathf.Max(max, t);
                }
            }

            if (float.IsInfinity(min) || float.IsInfinity(max))
                return 0f;

            // Only the part standing between the socket plane and the neighbour matters — that is the
            // half that would otherwise reach into the room being attached.
            return Mathf.Max(0f, max);
        }

        public static float FaceScore(RoomSocket child, RoomSocket parent) =>
            Vector3.Dot(FlattenDirection(child.transform.forward), -FlattenDirection(parent.transform.forward));

        public static SocketDirection DirectionFromForward(Vector3 forward)
        {
            Vector3 snapped = QuantizeToOrthogonal(FlattenDirection(forward));
            float yaw = Mathf.Atan2(snapped.x, snapped.z) * Mathf.Rad2Deg;
            int cardinal = Mathf.RoundToInt(yaw / OrthogonalStepDegrees);
            int wrapped = ((cardinal % 4) + 4) % 4;

            return wrapped switch
            {
                0 => SocketDirection.North,
                1 => SocketDirection.East,
                2 => SocketDirection.South,
                3 => SocketDirection.West,
                _ => SocketDirection.Unknown
            };
        }

        private static Quaternion YawOnlyRotation(Vector3 direction, bool quantizeOrthogonal)
        {
            Vector3 flat = FlattenDirection(direction);
            if (quantizeOrthogonal)
                flat = QuantizeToOrthogonal(flat);

            return Quaternion.LookRotation(flat, Vector3.up);
        }

        private static Vector3 FlattenDirection(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f)
                return Vector3.forward;

            return flat.normalized;
        }

        private static Vector3 QuantizeToOrthogonal(Vector3 direction)
        {
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float snappedYaw = Mathf.Round(yaw / OrthogonalStepDegrees) * OrthogonalStepDegrees;
            return Quaternion.Euler(0f, snappedYaw, 0f) * Vector3.forward;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_direction == SocketDirection.Unknown)
                _direction = DirectionFromForward(transform.forward);

            if (string.IsNullOrWhiteSpace(_socketTag))
                _socketTag = "Default";
            if (string.IsNullOrWhiteSpace(_sizeClass))
                _sizeClass = "M";
        }
#endif
    }
}
