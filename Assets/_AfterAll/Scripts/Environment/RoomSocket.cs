using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Connection point on a room. Parent of room root — flat Y rotation only.
    /// </summary>
    public class RoomSocket : MonoBehaviour
    {
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
            transform.rotation = Quaternion.LookRotation(outward.normalized, Vector3.up);
        }

        public static void SnapRoom(RoomInstance childRoom, RoomSocket childSocket, RoomSocket parentSocket)
        {
            Transform root = childRoom.transform;
            Transform childT = childSocket.transform;
            Transform parentT = parentSocket.transform;

            // Capture pivot BEFORE changing room rotation (childT.position moves with the room).
            Vector3 pivot = childT.position;
            Quaternion targetRot = parentT.rotation * Quaternion.Euler(0f, 180f, 0f);
            Quaternion delta = targetRot * Quaternion.Inverse(childT.rotation);

            root.rotation = delta * root.rotation;
            root.position = delta * (root.position - pivot) + parentT.position;
        }

        public static float FaceScore(RoomSocket child, RoomSocket parent) =>
            Vector3.Dot(child.transform.forward, -parent.transform.forward);
    }
}
