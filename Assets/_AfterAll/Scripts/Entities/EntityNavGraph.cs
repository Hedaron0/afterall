using System.Collections.Generic;
using AfterAll.Environment;
using UnityEngine;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4 room bookkeeping: tracks the current floor's rooms for high-level entity decisions
    /// (spawn anchor, patrol-target sampling). Actual movement runs on the runtime-baked NavMesh
    /// (RoomPoolSpawner bakes it per floor) via NavMeshAgent, not this graph — revised from the
    /// original room-graph-waypoint pathing (Core Design §9a 2026-07-18 tech-risk note, option
    /// (b)) after real obstacles inside rooms broke the straight-line-between-doors assumption.
    /// Rebuilt from LevelRoot each floor via RoomPoolSpawner.FloorReady.
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

        /// <summary>Room with the highest graph depth (farthest by connection count from the hub),
        /// skipping <paramref name="exclude"/> and any room <paramref name="isValid"/> rejects
        /// (e.g. a sealed compartment at the spawn point). Spawn anchor for the hunter.</summary>
        public RoomInstance FindFarthestRoom(RoomInstance exclude, System.Func<RoomInstance, bool> isValid = null)
        {
            RoomInstance best = null;
            foreach (RoomInstance room in _rooms)
            {
                if (room == null || room == exclude)
                    continue;
                if (isValid != null && !isValid(room))
                    continue;
                if (best == null || room.GraphDepth > best.GraphDepth)
                    best = room;
            }

            return best;
        }
    }
}
