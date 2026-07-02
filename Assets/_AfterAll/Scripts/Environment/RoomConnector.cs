using System.Collections.Generic;
using UnityEngine;

namespace AfterAll.Environment
{
    public class RoomConnector : MonoBehaviour
    {
        private const float FloorOverlapInset = 0.12f;
        private const float InteriorInsetPerSide = 0.2f;
        private const float ParentPenetrationOverlapArea = 1.25f;

        public struct ConnectionStats
        {
            public int noCompatibleSocket;
            public int gapMismatch;
            public int overlapRejected;
            public int fallbackUsed;
            public int offsetRetried;
            public int offsetSolvedOverlap;
            public int offsetSearchExhausted;
        }

        [SerializeField] private Transform _levelRoot;
        [SerializeField] private bool _offsetSearchEnabled = true;
        [SerializeField, Min(1)] private int _offsetSamplesPerWall = 5;
        private int _placementSeed;
        private int _connectionAttemptSerial;
        private ConnectionStats _stats;

        public Transform LevelRoot => _levelRoot != null ? _levelRoot : transform;

        public void ResetStats()
        {
            _stats = default;
            _connectionAttemptSerial = 0;
        }

        public ConnectionStats GetStats() => _stats;
        public void ConfigureOffsetSearch(bool enabled, int samplesPerWall, int placementSeed)
        {
            _offsetSearchEnabled = enabled;
            _offsetSamplesPerWall = Mathf.Max(1, samplesPerWall);
            _placementSeed = placementSeed;
        }

        public RoomInstance Connect(
            RoomInstance parent,
            WallGapController parentWall,
            GameObject roomPrefab,
            bool spawnDoor = false,
            bool spawnFrame = false)
        {
            if (parent == null || parentWall == null || roomPrefab == null)
                return null;

            if (parent.IsWallConnected(parentWall))
                return null;

            int attemptId = _connectionAttemptSerial++;
            System.Random rng = CreatePlacementRng(parentWall, attemptId);
            float parentOffset = WallGapController.GetRandomGapOffset(parentWall, rng);
            parent.OpenWall(parentWall, parentOffset, false);

            if (!parentWall.TryGetSocket(out RoomSocket parentSocket))
            {
                RollbackParentWall(parentWall);
                Debug.LogError($"[RoomConnector] Parent {parentWall.name} has no socket.");
                return null;
            }

            GameObject go = Instantiate(roomPrefab, LevelRoot);
            go.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            RoomInstance child = EnsureRoomInstance(go);
            if (!TryPickChildWall(
                    child,
                    parent,
                    parentWall,
                    parentSocket,
                    attemptId,
                    spawnFrame,
                    out WallGapController childWall))
            {
                Destroy(go);
                RollbackParentWall(parentWall);
                Debug.LogWarning($"[RoomConnector] No wall on {roomPrefab.name} fits {parentWall.name}.");
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

        public static bool RoomsOverlapForPlacement(
            RoomInstance candidate,
            RoomInstance other,
            bool isDirectParentConnection,
            RoomSocket parentSocket,
            RoomSocket childSocket)
        {
            if (candidate == null || other == null || candidate == other)
                return false;

            if (isDirectParentConnection && parentSocket != null)
                return PenetratesIntoRoom(candidate, other, parentSocket, childSocket);

            Bounds candidateFloor = candidate.GetFloorFootprintBounds();
            Bounds otherFloor = other.GetFloorFootprintBounds();
            return FootprintsOverlapXZ(candidateFloor, otherFloor, FloorOverlapInset);
        }

        private static bool PenetratesIntoRoom(
            RoomInstance candidate,
            RoomInstance parentRoom,
            RoomSocket parentSocket,
            RoomSocket childSocket)
        {
            Bounds candidateFloor = candidate.GetFloorFootprintBounds();
            Bounds parentFloor = parentRoom.GetFloorFootprintBounds();
            Bounds parentInterior = parentRoom.GetInteriorFootprintBounds(InteriorInsetPerSide);
            Bounds candidateInterior = candidate.GetInteriorFootprintBounds(InteriorInsetPerSide);

            Vector3 childCenter = candidateFloor.center;

            float interiorOverlap = ComputeXZOverlapArea(candidateInterior, parentInterior);
            if (interiorOverlap > ParentPenetrationOverlapArea)
                return true;

            Vector3 door = parentSocket.transform.position;
            Vector3 outward = parentSocket.transform.forward;
            outward.y = 0f;
            if (outward.sqrMagnitude < 0.0001f)
                return FootprintsOverlapXZ(candidateFloor, parentFloor, FloorOverlapInset);

            outward.Normalize();
            float outwardDistance = Vector3.Dot(childCenter - door, outward);
            if (outwardDistance < -0.35f)
                return true;

            if (outwardDistance < 0.15f && interiorOverlap > 0.2f)
                return true;

            if (childSocket != null)
            {
                float facing = Vector3.Dot(childSocket.transform.forward, outward);
                if (facing > 0.2f)
                    return true;
            }

            return false;
        }

        private static bool FootprintsOverlapXZ(Bounds a, Bounds b, float insetMargin)
        {
            float aMinX = a.min.x + insetMargin;
            float aMaxX = a.max.x - insetMargin;
            float aMinZ = a.min.z + insetMargin;
            float aMaxZ = a.max.z - insetMargin;
            float bMinX = b.min.x + insetMargin;
            float bMaxX = b.max.x - insetMargin;
            float bMinZ = b.min.z + insetMargin;
            float bMaxZ = b.max.z - insetMargin;

            if (aMaxX <= aMinX || aMaxZ <= aMinZ || bMaxX <= bMinX || bMaxZ <= bMinZ)
                return false;

            return aMinX < bMaxX && aMaxX > bMinX && aMinZ < bMaxZ && aMaxZ > bMinZ;
        }

        private static void RollbackParentWall(WallGapController parentWall)
        {
            if (parentWall == null)
                return;

            parentWall.ConfigureOpening(false, false, 0f);
        }

        private System.Random CreatePlacementRng(WallGapController wall, int attemptId)
        {
            int salt = wall != null ? wall.name.GetHashCode() : 0;
            return new System.Random(_placementSeed ^ salt ^ (attemptId * 397));
        }

        private bool TryPickChildWall(
            RoomInstance childRoom,
            RoomInstance parentRoom,
            WallGapController parentWall,
            RoomSocket parentSocket,
            int attemptId,
            bool spawnFrame,
            out WallGapController selectedWall)
        {
            selectedWall = null;
            float best = float.MinValue;
            float selectedParentOffset = 0f;
            float selectedChildOffset = 0f;
            System.Random rng = CreatePlacementRng(parentWall, attemptId);
            bool parentHasContract = parentSocket != null && parentSocket.HasValidContract;
            bool anyCompatibleByContract = false;
            bool usedFallback = false;
            bool failedGap = false;
            bool failedOverlap = false;
            bool solvedOverlap = false;
            int retryAttempts = 0;
            List<float> parentOffsets = new();
            List<float> childOffsets = new();
            // Cache once per placement attempt — do not call GetComponentsInChildren inside offset loops.
            RoomInstance[] placedRooms = LevelRoot.GetComponentsInChildren<RoomInstance>();

            if (_offsetSearchEnabled)
                WallGapController.GetOffsetSamples(parentWall, _offsetSamplesPerWall, rng, parentOffsets);
            else
                parentOffsets.Add(WallGapController.GetRandomGapOffset(parentWall, rng));

            foreach (WallGapController wall in childRoom.Walls)
            {
                bool wallHadOverlap = false;
                bool wallFoundAfterOverlap = false;

                System.Random childRng = CreatePlacementRng(
                    wall,
                    attemptId ^ childRoom.name.GetHashCode() ^ (wall != null ? wall.name.GetHashCode() : 0));

                if (_offsetSearchEnabled)
                    WallGapController.GetOffsetSamples(wall, _offsetSamplesPerWall, childRng, childOffsets);
                else
                {
                    childOffsets.Clear();
                    childOffsets.Add(WallGapController.GetRandomGapOffset(wall, childRng));
                }

                for (int pi = 0; pi < parentOffsets.Count; pi++)
                for (int ci = 0; ci < childOffsets.Count; ci++)
                {
                    if (pi > 0 || ci > 0)
                        retryAttempts++;

                    childRoom.SealAllWalls();
                    parentRoom.OpenWall(parentWall, parentOffsets[pi], false);
                    childRoom.OpenWall(wall, childOffsets[ci], false);

                    if (!parentWall.TryGetSocket(out RoomSocket parentProbeSocket) || !wall.TryGetSocket(out RoomSocket probeSocket))
                        continue;

                    bool probeHasContract = probeSocket.HasValidContract;
                    bool contractComparable = parentHasContract && probeHasContract;
                    bool compatibleByContract = contractComparable &&
                        RoomSocket.AreDirectionsOpposite(parentProbeSocket.Direction, probeSocket.Direction);

                    if (contractComparable && !compatibleByContract)
                        continue;

                    if (compatibleByContract)
                        anyCompatibleByContract = true;
                    else if (!contractComparable)
                        usedFallback = true;

                    RoomSocket.SnapRoom(childRoom, probeSocket, parentProbeSocket);

                    float gap = Vector3.Distance(probeSocket.transform.position, parentProbeSocket.transform.position);
                    if (gap > 0.1f)
                    {
                        failedGap = true;
                        continue;
                    }

                    if (HasInvalidOverlap(childRoom, parentRoom, parentProbeSocket, probeSocket, placedRooms))
                    {
                        failedOverlap = true;
                        wallHadOverlap = true;
                        continue;
                    }

                    wallFoundAfterOverlap |= wallHadOverlap && (pi > 0 || ci > 0);
                    float score = RoomSocket.FaceScore(probeSocket, parentProbeSocket);
                    bool isTie = Mathf.Abs(score - best) < 0.01f;
                    if (score > best || (isTie && rng.NextDouble() < 0.5))
                    {
                        best = score;
                        selectedWall = wall;
                        selectedParentOffset = parentOffsets[pi];
                        selectedChildOffset = childOffsets[ci];
                    }
                }

                solvedOverlap |= wallFoundAfterOverlap;
            }

            if (selectedWall == null)
            {
                if (!anyCompatibleByContract)
                    _stats.noCompatibleSocket++;
                if (failedGap)
                    _stats.gapMismatch++;
                if (failedOverlap)
                    _stats.overlapRejected++;
                if (usedFallback)
                    _stats.fallbackUsed++;
                if (retryAttempts > 0)
                    _stats.offsetSearchExhausted++;

                return false;
            }

            childRoom.SealAllWalls();
            bool spawnParentFrame = spawnDoor || spawnFrame;
            parentRoom.OpenWall(parentWall, selectedParentOffset, spawnParentFrame);
            childRoom.OpenWall(selectedWall, selectedChildOffset, false);
            if (!parentWall.TryGetSocket(out RoomSocket selectedParentSocket) || !selectedWall.TryGetSocket(out RoomSocket selectedSocket))
            {
                _stats.noCompatibleSocket++;
                return false;
            }

            RoomSocket.SnapRoom(childRoom, selectedSocket, selectedParentSocket);
            float finalGap = Vector3.Distance(selectedSocket.transform.position, selectedParentSocket.transform.position);
            if (finalGap > 0.1f)
            {
                _stats.gapMismatch++;
                return false;
            }

            if (HasInvalidOverlap(childRoom, parentRoom, selectedParentSocket, selectedSocket, placedRooms))
            {
                _stats.overlapRejected++;
                return false;
            }

            if (usedFallback)
                _stats.fallbackUsed++;
            if (retryAttempts > 0)
                _stats.offsetRetried += retryAttempts;
            if (solvedOverlap)
                _stats.offsetSolvedOverlap++;

            return true;
        }

        private bool HasInvalidOverlap(
            RoomInstance candidate,
            RoomInstance parentRoom,
            RoomSocket parentSocket,
            RoomSocket childSocket,
            RoomInstance[] placedRooms)
        {
            foreach (RoomInstance existing in placedRooms)
            {
                if (existing == null || existing == candidate)
                    continue;

                bool isParent = existing == parentRoom;
                if (RoomsOverlapForPlacement(candidate, existing, isParent, parentSocket, childSocket))
                    return true;
            }

            return false;
        }

        public static float ComputeXZOverlapArea(Bounds a, Bounds b)
        {
            float minX = Mathf.Max(a.min.x, b.min.x);
            float maxX = Mathf.Min(a.max.x, b.max.x);
            float minZ = Mathf.Max(a.min.z, b.min.z);
            float maxZ = Mathf.Min(a.max.z, b.max.z);

            if (minX >= maxX || minZ >= maxZ)
                return 0f;

            return (maxX - minX) * (maxZ - minZ);
        }

        private static RoomInstance EnsureRoomInstance(GameObject go)
        {
            RoomInstance r = go.GetComponent<RoomInstance>() ?? go.AddComponent<RoomInstance>();
            r.CacheWalls();
            return r;
        }
    }
}
