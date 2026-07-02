using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Connection point on a room. Parent of room root — flat Y rotation only.
    /// </summary>
    public class RoomSocket : MonoBehaviour
    {
        private const float OrthogonalStepDegrees = 90f;
        [SerializeField] private WallGapController _wall;
        [SerializeField] private float _width = 1.3f;

        public WallGapController Wall => _wall;
        public float Width => _width;
        public bool IsConnected { get; set; }

        public void Bind(WallGapController wall, float width)
        {
            _wall = wall;
            _width = width;
        }

        public void AlignAt(Vector3 worldCenter, Vector3 outward, float width)
        {
            _width = width;
            gameObject.SetActive(true);
            transform.position = worldCenter;
            transform.rotation = YawOnlyRotation(outward, quantizeOrthogonal: true);
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
    }
}
