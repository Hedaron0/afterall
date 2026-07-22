using System.Collections.Generic;
using AfterAll.Player;
using UnityEngine;

namespace AfterAll.Environment
{
    /// <summary>
    /// Disables renderers and sleeps Rigidbodies on rooms more than <see cref="_visibleHopRadius"/>
    /// connection-hops from the player's current room. A cheap stand-in for occlusion culling,
    /// which can't be baked for runtime-generated floors (Editor-only). Colliders, NavMesh and
    /// RoomInstance/WallGapController state are untouched, so hunter pathing/logic (which reads
    /// room graph state, never renderer state) is unaffected.
    /// Collects its own room set from RoomConnector.LevelRoot on RoomPoolSpawner.FloorReady —
    /// deliberately does NOT read EntityNavGraph.Rooms, since FloorReady has multiple subscribers
    /// (this, RunDirector, EntityNavGraph) and Unity gives no ordering guarantee between them;
    /// depending on EntityNavGraph having already rebuilt this frame was a real bug (its cache
    /// came up empty half the time depending on subscription order).
    /// </summary>
    public class RoomVisibilityCuller : MonoBehaviour
    {
        [SerializeField] private RoomPoolSpawner _spawner;
        [SerializeField] private RoomConnector _connector;
        [SerializeField] private Transform _player;
        [SerializeField, Min(0)] private int _visibleHopRadius = 2;
        [SerializeField, Min(0.05f)] private float _pollIntervalSeconds = 0.2f;

        private readonly Dictionary<RoomInstance, MeshRenderer[]> _renderersByRoom = new();
        private readonly Dictionary<RoomInstance, Rigidbody[]> _rigidbodiesByRoom = new();
        private readonly HashSet<RoomInstance> _visibleRooms = new();
        private readonly Queue<RoomInstance> _bfsQueue = new();

        private RoomInstance _currentRoom;
        private float _pollTimer;

        private void Awake()
        {
            if (_spawner == null)
                _spawner = FindAnyObjectByType<RoomPoolSpawner>();
            if (_connector == null)
                _connector = GetComponent<RoomConnector>() ?? FindAnyObjectByType<RoomConnector>();
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
            RebuildRendererCache(elevatorRoom);
            _currentRoom = null; // force a re-evaluation against the new floor
            _pollTimer = 0f;
        }

        private void Update()
        {
            if (_renderersByRoom.Count == 0)
                return;

            Transform player = ResolvePlayer();
            if (player == null)
                return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f)
                return;
            _pollTimer = _pollIntervalSeconds;

            RoomInstance room = FindRoomContaining(player.position);
            if (room == null || room == _currentRoom)
                return;

            _currentRoom = room;
            ApplyVisibility(room);
        }

        private Transform ResolvePlayer()
        {
            if (_player != null)
                return _player;

            PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null)
                _player = movement.transform;

            return _player;
        }

        private RoomInstance FindRoomContaining(Vector3 worldPos)
        {
            foreach (RoomInstance room in _renderersByRoom.Keys)
            {
                if (room != null && room.ContainsPointXZ(worldPos))
                    return room;
            }

            return null;
        }

        /// <summary>Mirrors EntityNavGraph.Rebuild's room collection (LevelRoot children + the
        /// persistent elevator cabin, which lives outside LevelRoot) so this stays correct
        /// independent of whether EntityNavGraph has rebuilt itself yet this frame.</summary>
        private void RebuildRendererCache(RoomInstance elevatorRoom)
        {
            _renderersByRoom.Clear();
            _rigidbodiesByRoom.Clear();
            _visibleRooms.Clear();

            Transform root = _connector != null ? _connector.LevelRoot : null;
            if (root != null)
            {
                foreach (RoomInstance room in root.GetComponentsInChildren<RoomInstance>(true))
                {
                    _renderersByRoom[room] = room.GetComponentsInChildren<MeshRenderer>(true);
                    _rigidbodiesByRoom[room] = room.GetComponentsInChildren<Rigidbody>(true);
                }
            }

            if (elevatorRoom != null && !_renderersByRoom.ContainsKey(elevatorRoom))
            {
                _renderersByRoom[elevatorRoom] = elevatorRoom.GetComponentsInChildren<MeshRenderer>(true);
                _rigidbodiesByRoom[elevatorRoom] = elevatorRoom.GetComponentsInChildren<Rigidbody>(true);
            }

            // Every room's renderers start enabled by default on instantiation — seed the
            // "currently visible" set to match that reality so the first ApplyVisibility call
            // correctly detects true->false transitions instead of assuming nothing changed.
            _visibleRooms.UnionWith(_renderersByRoom.Keys);
        }

        private void ApplyVisibility(RoomInstance center)
        {
            HashSet<RoomInstance> newVisible = CollectRoomsWithinHops(center, _visibleHopRadius);

            foreach (RoomInstance room in _renderersByRoom.Keys)
            {
                bool wasVisible = _visibleRooms.Contains(room);
                bool nowVisible = newVisible.Contains(room);
                if (wasVisible == nowVisible)
                    continue;

                SetRoomRenderersEnabled(room, nowVisible);
                SetRoomRigidbodiesAsleep(room, !nowVisible);
            }

            _visibleRooms.Clear();
            _visibleRooms.UnionWith(newVisible);
        }

        private HashSet<RoomInstance> CollectRoomsWithinHops(RoomInstance start, int maxHops)
        {
            var visited = new Dictionary<RoomInstance, int> { [start] = 0 };
            _bfsQueue.Clear();
            _bfsQueue.Enqueue(start);

            while (_bfsQueue.Count > 0)
            {
                RoomInstance current = _bfsQueue.Dequeue();
                int depth = visited[current];
                if (depth >= maxHops)
                    continue;

                foreach (RoomInstance neighbor in current.ConnectedRooms)
                {
                    if (neighbor != null && !visited.ContainsKey(neighbor))
                    {
                        visited[neighbor] = depth + 1;
                        _bfsQueue.Enqueue(neighbor);
                    }
                }
            }

            return new HashSet<RoomInstance>(visited.Keys);
        }

        private void SetRoomRenderersEnabled(RoomInstance room, bool enabled)
        {
            if (!_renderersByRoom.TryGetValue(room, out MeshRenderer[] renderers))
                return;

            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }

        /// <summary>Sleeping physics-driven props in hop-culled rooms skips their solver cost
        /// entirely (not just rendering) — any external impulse (player, hunter) still wakes a
        /// body normally, this only stops idle bodies simulating while off-screen.</summary>
        private void SetRoomRigidbodiesAsleep(RoomInstance room, bool asleep)
        {
            if (!_rigidbodiesByRoom.TryGetValue(room, out Rigidbody[] bodies))
                return;

            foreach (Rigidbody rb in bodies)
            {
                if (rb == null)
                    continue;

                if (asleep)
                    rb.Sleep();
                else
                    rb.WakeUp();
            }
        }
    }
}
