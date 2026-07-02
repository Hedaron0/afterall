using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AfterAll.Environment
{
    public class RoomConnector : MonoBehaviour
    {
        [SerializeField] private Transform _levelRoot;

        public Transform LevelRoot => _levelRoot != null ? _levelRoot : transform;

        public RoomInstance Connect(
            RoomInstance parent,
            WallGapController parentWall,
            GameObject roomPrefab,
            bool spawnFrames = false)
        {
            if (parent == null || parentWall == null || roomPrefab == null)
                return null;

            if (parent.IsWallConnected(parentWall))
                return null;

            parent.OpenWall(parentWall, spawnFrames);

            if (!parentWall.TryGetSocket(out RoomSocket parentSocket))
            {
                Debug.LogError($"[RoomConnector] Parent {parentWall.name} has no socket.");
                return null;
            }

            GameObject go = Instantiate(roomPrefab, LevelRoot);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            RoomInstance child = EnsureRoomInstance(go);
            if (!TryPickChildWall(child, parent, parentSocket, out WallGapController childWall))
            {
                Destroy(go);
                Debug.LogError($"[RoomConnector] No wall on {roomPrefab.name} fits {parentWall.name}.");
                return null;
            }

            parent.MarkWallConnected(parentWall, child);
            child.MarkWallConnected(childWall, parent);
            parentSocket.IsConnected = true;
            if (childWall.TryGetSocket(out RoomSocket childSocket))
                childSocket.IsConnected = true;

            Debug.Log($"[RoomConnector] {parent.name}/{parentWall.name} -> {child.name}/{childWall.name}");
            return child;
        }

        private bool TryPickChildWall(
            RoomInstance childRoom,
            RoomInstance parentRoom,
            RoomSocket parentSocket,
            out WallGapController selectedWall)
        {
            selectedWall = null;
            float best = float.MinValue;

            foreach (WallGapController wall in childRoom.Walls)
            {
                childRoom.SealAllWalls();
                childRoom.OpenWall(wall, false);

                if (!wall.TryGetSocket(out RoomSocket probeSocket))
                    continue;

                RoomSocket.SnapRoom(childRoom, probeSocket, parentSocket);

                float gap = Vector3.Distance(probeSocket.transform.position, parentSocket.transform.position);
                if (gap > 0.1f)
                    continue;

                float score = RoomSocket.FaceScore(probeSocket, parentSocket);
                if (OverlapsAnyPlacedRoom(childRoom, parentRoom))
                    score -= 5f;

                if (score > best)
                {
                    best = score;
                    selectedWall = wall;
                }
            }

            if (selectedWall == null)
                return false;

            childRoom.SealAllWalls();
            childRoom.OpenWall(selectedWall, false);
            if (!selectedWall.TryGetSocket(out RoomSocket selectedSocket))
                return false;

            RoomSocket.SnapRoom(childRoom, selectedSocket, parentSocket);
            float finalGap = Vector3.Distance(selectedSocket.transform.position, parentSocket.transform.position);
            if (finalGap > 0.1f || OverlapsAnyPlacedRoom(childRoom, parentRoom))
                return false;

            return true;
        }

        private static bool BoundsOverlap(RoomInstance a, RoomInstance b)
        {
            Bounds ba = a.GetWorldBounds();
            Bounds bb = b.GetWorldBounds();
            ba.Expand(-0.3f);
            bb.Expand(-0.3f);
            return ba.Intersects(bb);
        }

        private bool OverlapsAnyPlacedRoom(RoomInstance candidate, RoomInstance connectedParent)
        {
            foreach (RoomInstance existing in LevelRoot.GetComponentsInChildren<RoomInstance>())
            {
                if (existing == null || existing == candidate || existing == connectedParent)
                    continue;

                if (BoundsOverlap(candidate, existing))
                    return true;
            }

            return false;
        }

        private static RoomInstance EnsureRoomInstance(GameObject go)
        {
            RoomInstance r = go.GetComponent<RoomInstance>() ?? go.AddComponent<RoomInstance>();
            r.CacheWalls();
            return r;
        }
    }
}
