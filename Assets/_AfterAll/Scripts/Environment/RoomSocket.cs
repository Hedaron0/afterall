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
