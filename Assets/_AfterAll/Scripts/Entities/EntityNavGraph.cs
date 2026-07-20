using System.Collections.Generic;
using AfterAll.Environment;
using UnityEngine;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4 navigation: BFS over the live room graph (RoomInstance.ConnectedRooms) with doorway
    /// sockets as waypoints. No NavMesh — rooms are rectangular greyboxes, so inside a room the
    /// entity walks straight lines between doors (decision: Core Design §9a 2026-07-18 tech-risk
    /// note, option (b)). Rebuilt from LevelRoot each floor via RoomPoolSpawner.FloorReady.
    /// </summary>
    public class EntityNavGraph : MonoBehaviour
    {
        [SerializeField] private RoomPoolSpawner _spawner;

        private readonly List<RoomInstance> _rooms = new();

        public IReadOnlyList<RoomInstance> Rooms => _rooms;
        public bool IsReady => _rooms.Count > 0;

        private void Awake()
        {
            if (_spawner == null)
                _spawner = FindAnyObjectByType<RoomPoolSpawner>();
        }

        private void OnEnable()
        {
            if (_spawner != null)
                _spawner.FloorReady += HandleFloorReady;
        }

        private void OnDisable()
        {
            if (_spawner != null)
                _spawner.FloorReady -= HandleFloorReady;
        }

        private void HandleFloorReady(RoomInstance elevatorRoom)
        {
            Rebuild(elevatorRoom);
        }

        /// <summary>Collects the current floor's rooms (LevelRoot children + the persistent
        /// elevator cabin, which lives outside LevelRoot but is part of the graph).</summary>
        public void Rebuild(RoomInstance elevatorRoom)
        {
            _rooms.Clear();

            Transform root = _spawner != null ? _spawner.GetComponent<RoomConnector>()?.LevelRoot : null;
            if (root != null)
                _rooms.AddRange(root.GetComponentsInChildren<RoomInstance>());

            if (elevatorRoom != null && !_rooms.Contains(elevatorRoom))
                _rooms.Add(elevatorRoom);
        }

        /// <summary>Room whose footprint contains the point, else nearest room center. Null when
        /// the graph is empty.</summary>
        public RoomInstance FindRoomAt(Vector3 worldPos)
        {
            RoomInstance nearest = null;
            float nearestSqr = float.PositiveInfinity;

            foreach (RoomInstance room in _rooms)
            {
                if (room == null)
                    continue;

                if (room.ContainsPointXZ(worldPos))
                    return room;

                float sqr = (room.GetApproximateCenter() - worldPos).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = room;
                }
            }

            return nearest;
        }

        /// <summary>Room with the highest graph depth (farthest by connection count from the hub),
        /// skipping <paramref name="exclude"/>. Spawn anchor for the hunter.</summary>
        public RoomInstance FindFarthestRoom(RoomInstance exclude)
        {
            RoomInstance best = null;
            foreach (RoomInstance room in _rooms)
            {
                if (room == null || room == exclude)
                    continue;
                if (best == null || room.GraphDepth > best.GraphDepth)
                    best = room;
            }

            return best;
        }

        /// <summary>
        /// Waypoint path from a world position to a target world position: doorway sockets for
        /// every room transition, then the target itself. False when no route exists.
        /// </summary>
        public bool TryGetPath(Vector3 fromWorld, Vector3 toWorld, List<Vector3> resultWaypoints)
        {
            resultWaypoints.Clear();

            RoomInstance fromRoom = FindRoomAt(fromWorld);
            RoomInstance toRoom = FindRoomAt(toWorld);
            if (fromRoom == null || toRoom == null)
                return false;

            if (fromRoom == toRoom)
            {
                resultWaypoints.Add(toWorld);
                return true;
            }

            // BFS over ConnectedRooms.
            var cameFrom = new Dictionary<RoomInstance, RoomInstance> { [fromRoom] = fromRoom };
            var queue = new Queue<RoomInstance>();
            queue.Enqueue(fromRoom);
            bool found = false;

            while (queue.Count > 0 && !found)
            {
                RoomInstance current = queue.Dequeue();
                foreach (RoomInstance next in current.ConnectedRooms)
                {
                    if (next == null || cameFrom.ContainsKey(next))
                        continue;

                    cameFrom[next] = current;
                    if (next == toRoom)
                    {
                        found = true;
                        break;
                    }

                    queue.Enqueue(next);
                }
            }

            if (!found)
                return false;

            // Walk back to build the room chain, then emit doorway waypoints forward.
            var chain = new List<RoomInstance>();
            for (RoomInstance r = toRoom; r != fromRoom; r = cameFrom[r])
                chain.Add(r);
            chain.Add(fromRoom);
            chain.Reverse();

            for (int i = 0; i < chain.Count - 1; i++)
            {
                if (chain[i].TryGetDoorwayTo(chain[i + 1], out Vector3 door))
                    resultWaypoints.Add(door);
            }

            resultWaypoints.Add(toWorld);
            return true;
        }
    }
}
