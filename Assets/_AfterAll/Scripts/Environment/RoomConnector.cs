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

            if (!TryPickChildWall(roomPrefab, parent, parentSocket, out string childWallName))
            {
                Debug.LogError($"[RoomConnector] No wall on {roomPrefab.name} fits {parentWall.name}.");
                return null;
            }

            GameObject go = Instantiate(roomPrefab, LevelRoot);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            RoomInstance child = EnsureRoomInstance(go);
            WallGapController childWall = child.GetWall(childWallName);
            child.SealAllWalls();
            child.OpenWall(childWall, spawnFrames);

            if (!childWall.TryGetSocket(out RoomSocket childSocket))
            {
                Destroy(go);
                Debug.LogError($"[RoomConnector] Child {childWallName} has no socket.");
                return null;
            }

            RoomSocket.SnapRoom(child, childSocket, parentSocket);

            float gap = Vector3.Distance(childSocket.transform.position, parentSocket.transform.position);
            if (gap > 0.1f)
            {
                Destroy(go);
                Debug.LogError($"[RoomConnector] Snap failed — gap {gap:F2}m on {childWallName}.");
                return null;
            }

            parent.MarkWallConnected(parentWall, child);
            child.MarkWallConnected(childWall, parent);
            parentSocket.IsConnected = true;
            childSocket.IsConnected = true;

            Debug.Log($"[RoomConnector] {parent.name}/{parentWall.name} -> {child.name}/{childWallName}");
            return child;
        }

        private bool TryPickChildWall(
            GameObject prefab,
            RoomInstance parentRoom,
            RoomSocket parentSocket,
            out string wallName)
        {
            wallName = null;
            float best = float.MinValue;

            GameObject probe = Instantiate(prefab, LevelRoot);
            probe.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            RoomInstance probeRoom = EnsureRoomInstance(probe);

            foreach (WallGapController wall in probeRoom.Walls)
            {
                probeRoom.SealAllWalls();
                probeRoom.OpenWall(wall, false);

                if (!wall.TryGetSocket(out RoomSocket probeSocket))
                    continue;

                RoomSocket.SnapRoom(probeRoom, probeSocket, parentSocket);

                float score = RoomSocket.FaceScore(probeSocket, parentSocket);
                if (BoundsOverlap(probeRoom, parentRoom))
                    score -= 5f;

                if (score > best)
                {
                    best = score;
                    wallName = wall.name;
                }
            }

            Destroy(probe);
            return wallName != null;
        }

        private static bool BoundsOverlap(RoomInstance a, RoomInstance b)
        {
            Bounds ba = a.GetWorldBounds();
            Bounds bb = b.GetWorldBounds();
            ba.Expand(-0.3f);
            bb.Expand(-0.3f);
            return ba.Intersects(bb);
        }

        private static RoomInstance EnsureRoomInstance(GameObject go)
        {
            RoomInstance r = go.GetComponent<RoomInstance>() ?? go.AddComponent<RoomInstance>();
            r.CacheWalls();
            return r;
        }
    }
}
