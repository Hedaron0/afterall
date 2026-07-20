using AfterAll.Environment;
using AfterAll.Run;
using UnityEngine;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4 spawn/despawn authority for the hunter: one instance per explore phase, spawned in the
    /// graph-farthest room when the player first leaves the elevator, removed on floor change,
    /// extraction, and death. The elevator interior stays entity-free by design.
    /// </summary>
    public class EntityDirector : MonoBehaviour
    {
        [SerializeField] private RunDirector _runDirector;
        [SerializeField] private RoomPoolSpawner _spawner;
        [SerializeField] private EntityNavGraph _navGraph;
        [SerializeField] private GameObject _hunterPrefab;
        [SerializeField, Min(0f)] private float _spawnHeightAboveFloor = 0.1f;
        [Tooltip("Depth 0 = first floor already hunted. Raise to give the first floor(s) a grace period.")]
        [SerializeField, Min(0)] private int _minDepthToSpawn;

        private GameObject _activeHunter;

        private void Awake()
        {
            if (_runDirector == null)
                _runDirector = FindAnyObjectByType<RunDirector>();
            if (_spawner == null)
                _spawner = FindAnyObjectByType<RoomPoolSpawner>();
            if (_navGraph == null)
                _navGraph = FindAnyObjectByType<EntityNavGraph>();
        }

        private void OnEnable()
        {
            if (_runDirector != null)
            {
                _runDirector.ExploreStarted += HandleExploreStarted;
                _runDirector.RunEnded += DespawnHunter;
                _runDirector.RunFailed += DespawnHunter;
                _runDirector.DepthChanged += HandleDepthChanged;
            }
        }

        private void OnDisable()
        {
            if (_runDirector != null)
            {
                _runDirector.ExploreStarted -= HandleExploreStarted;
                _runDirector.RunEnded -= DespawnHunter;
                _runDirector.RunFailed -= DespawnHunter;
                _runDirector.DepthChanged -= HandleDepthChanged;
            }
        }

        /// <summary>Floor change (GoDown / death rebuild): the old floor's hunter dies with it.</summary>
        private void HandleDepthChanged(int depth)
        {
            DespawnHunter();
        }

        private void HandleExploreStarted()
        {
            if (_activeHunter != null || _hunterPrefab == null || _navGraph == null)
                return;
            if (_runDirector.Depth < _minDepthToSpawn)
                return;

            RoomInstance spawnRoom = _navGraph.FindFarthestRoom(_spawner != null ? _spawner.CurrentElevatorRoom : null);
            if (spawnRoom == null)
                return;

            Vector3 pos = spawnRoom.GetSpawnPosition(_spawnHeightAboveFloor);
            _activeHunter = Instantiate(_hunterPrefab, pos, Quaternion.identity);
        }

        private void DespawnHunter()
        {
            if (_activeHunter != null)
            {
                Destroy(_activeHunter);
                _activeHunter = null;
            }
        }
    }
}
