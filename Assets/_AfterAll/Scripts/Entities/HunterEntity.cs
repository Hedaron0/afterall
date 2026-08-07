using System.Collections.Generic;
using AfterAll.Environment;
using AfterAll.Player;
using AfterAll.Run;
using UnityEngine;
using UnityEngine.AI;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4 entity v1: sound-driven unkillable hunter. Patrol (random rooms) → Investigate (last
    /// heard noise) → Chase (line of sight) → kill on touch → RunDirector.OnPlayerDied.
    /// Moves via NavMeshAgent on the floor's runtime-baked NavMesh (RoomPoolSpawner bakes it fresh
    /// per floor, hidden behind the elevator door-close transition) — real obstacle avoidance
    /// around pillars/decor, no more custom straight-line-plus-slide walking.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class HunterEntity : MonoBehaviour
    {
        private enum State
        {
            Patrol,
            Investigate,
            Chase
        }

        [Header("Refs")]
        [SerializeField] private EntityNavGraph _navGraph;
        [SerializeField] private RunDirector _runDirector;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float _patrolSpeed = 2f;
        [SerializeField, Min(0.1f)] private float _chaseSpeed = 4.2f;
        [SerializeField, Min(0.05f)] private float _waypointReachDistance = 0.5f;
        [SerializeField, Min(0.5f)] private float _turnSpeedDeg = 360f;
        [Tooltip("How far to search for a walkable NavMesh point when a target (room center, " +
                 "noise position, player position) isn't exactly on the mesh.")]
        [SerializeField, Min(0.1f)] private float _navSampleRadius = 3f;

        [Header("Senses")]
        [SerializeField, Min(1f)] private float _sightRange = 14f;
        [SerializeField, Range(30f, 180f)] private float _sightFovDegrees = 110f;
        [SerializeField, Min(0f)] private float _eyeHeight = 1.6f;
        [Tooltip("Multiplier on incoming noise loudnessRadius — >1 hears farther, <1 is deafer.")]
        [SerializeField, Min(0f)] private float _hearingMultiplier = 1f;

        [Header("Behavior")]
        [SerializeField, Min(0f)] private float _investigateLingerSeconds = 3f;
        [SerializeField, Min(0.1f)] private float _chaseRepathInterval = 0.4f;
        [SerializeField, Min(0f)] private float _loseSightGraceSeconds = 1.5f;
        [SerializeField, Min(0.2f)] private float _killDistance = 1.1f;

        [Header("Blackout (opt-in atmosphere)")]
        [Tooltip("Darken nearby fluorescent panels while chasing, restore them when the chase " +
                 "ends. Off by default — enable per design decision, not tied to performance work.")]
        [SerializeField] private bool _blackoutOnChase = false;
        [SerializeField, Min(1f)] private float _blackoutRadius = 12f;

        private readonly List<FluorescentLight> _blackedOutPanels = new();
        private NavMeshAgent _agent;
        private State _state = State.Patrol;
        private PlayerMovement _player;
        private Vector3 _investigateTarget;
        private float _lingerUntil;
        private float _nextRepathAt;
        private float _lastSeenPlayerAt = float.NegativeInfinity;

        private void Awake()
        {
            if (_navGraph == null)
                _navGraph = FindAnyObjectByType<EntityNavGraph>();
            if (_runDirector == null)
                _runDirector = FindAnyObjectByType<RunDirector>();
            _player = FindAnyObjectByType<PlayerMovement>();

            _agent = GetComponent<NavMeshAgent>();
            _agent.stoppingDistance = _waypointReachDistance;
            _agent.angularSpeed = _turnSpeedDeg;
        }

        private void OnEnable()
        {
            NoiseEvents.NoiseReported += HandleNoise;
            _state = State.Patrol;
        }

        private void OnDisable()
        {
            NoiseEvents.NoiseReported -= HandleNoise;
            RestoreBlackedOutPanels();
        }

        private void Update()
        {
            if (_player == null || _navGraph == null || !_navGraph.IsReady || !_agent.isOnNavMesh)
                return;

            // The cabin is safe ground by design — the hunter never acts while the player is
            // in the elevator (EntityDirector also deactivates it between floors).
            if (_runDirector != null && _runDirector.State == RunState.InElevator)
                return;

            bool seesPlayer = CanSeePlayer();
            if (seesPlayer)
                _lastSeenPlayerAt = Time.time;

            switch (_state)
            {
                case State.Patrol:
                    if (seesPlayer) { EnterChase(); break; }
                    TickPatrol();
                    break;
                case State.Investigate:
                    if (seesPlayer) { EnterChase(); break; }
                    TickInvestigate();
                    break;
                case State.Chase:
                    TickChase(seesPlayer);
                    break;
            }
        }

        private void HandleNoise(Vector3 pos, float loudnessRadius)
        {
            if (_state == State.Chase)
                return;
            if (_runDirector != null && _runDirector.State == RunState.InElevator)
                return;

            float heardRange = loudnessRadius * _hearingMultiplier;
            if ((pos - transform.position).sqrMagnitude > heardRange * heardRange)
                return;

            _investigateTarget = pos;
            _state = State.Investigate;
            _lingerUntil = 0f;
            RepathTo(pos);
        }

        private void TickPatrol()
        {
            _agent.speed = _patrolSpeed;
            if (IsTraveling())
                return;

            // Arrived (or no path yet): wander to a random room center.
            RoomInstance target = PickPatrolRoom();
            if (target != null)
                RepathTo(target.GetSpawnPosition(0f));
        }

        private void TickInvestigate()
        {
            _agent.speed = _patrolSpeed;
            if (IsTraveling())
                return;

            // Arrived: linger scanning, then go back to patrol.
            if (_lingerUntil <= 0f)
                _lingerUntil = Time.time + _investigateLingerSeconds;
            else if (Time.time >= _lingerUntil)
                _state = State.Patrol;
        }

        private void TickChase(bool seesPlayer)
        {
            _agent.speed = _chaseSpeed;

            if (!seesPlayer && Time.time - _lastSeenPlayerAt > _loseSightGraceSeconds)
            {
                // Lost them: investigate the last known position.
                _investigateTarget = _player.transform.position;
                _state = State.Investigate;
                _lingerUntil = 0f;
                RepathTo(_investigateTarget);
                if (_blackoutOnChase)
                    RestoreBlackedOutPanels();
                return;
            }

            if (Time.time >= _nextRepathAt)
            {
                _nextRepathAt = Time.time + _chaseRepathInterval;
                RepathTo(_player.transform.position);
            }

            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude <= _killDistance * _killDistance)
                KillPlayer();
        }

        private void EnterChase()
        {
            _state = State.Chase;
            _nextRepathAt = 0f;
            if (_blackoutOnChase)
                BlackOutNearbyPanels();
        }

        private void KillPlayer()
        {
            _runDirector?.OnPlayerDied();
        }

        /// <summary>Drives every FluorescentLight within <see cref="_blackoutRadius"/> of the
        /// hunter dark for a "lights die as it arrives" beat. Restored on losing the chase or on
        /// disable — never left permanently dark.</summary>
        private void BlackOutNearbyPanels()
        {
            _blackedOutPanels.Clear();
            foreach (FluorescentLight panel in FindObjectsByType<FluorescentLight>(FindObjectsSortMode.None))
            {
                if (panel == null)
                    continue;
                if ((panel.WorldPosition - transform.position).sqrMagnitude > _blackoutRadius * _blackoutRadius)
                    continue;

                panel.SetLit(false);
                _blackedOutPanels.Add(panel);
            }
        }

        private void RestoreBlackedOutPanels()
        {
            foreach (FluorescentLight panel in _blackedOutPanels)
            {
                if (panel != null)
                    panel.SetLit(true);
            }

            _blackedOutPanels.Clear();
        }

        /// <summary>True while the agent still has ground to cover toward its current destination.</summary>
        private bool IsTraveling()
        {
            if (_agent.pathPending)
                return true;

            return _agent.remainingDistance > _agent.stoppingDistance;
        }

        /// <summary>Snaps worldPos onto the nearest walkable NavMesh point before targeting it —
        /// room centers, noise positions and the player's feet are rarely exactly on the mesh.</summary>
        private void RepathTo(Vector3 worldPos)
        {
            if (NavMesh.SamplePosition(worldPos, out NavMeshHit hit, _navSampleRadius, NavMesh.AllAreas))
                _agent.SetDestination(hit.position);
        }

        private RoomInstance PickPatrolRoom()
        {
            System.Collections.Generic.IReadOnlyList<RoomInstance> rooms = _navGraph.Rooms;
            if (rooms.Count == 0)
                return null;

            // Bias away from the player: 3 random candidates, keep the farthest.
            RoomInstance best = null;
            float bestSqr = -1f;
            for (int i = 0; i < 3; i++)
            {
                RoomInstance candidate = rooms[Random.Range(0, rooms.Count)];
                if (candidate == null)
                    continue;
                if (candidate.IsPointBlockedForEntities(candidate.GetSpawnPosition(0f)))
                    continue;

                float sqr = (candidate.GetApproximateCenter() - _player.transform.position).sqrMagnitude;
                if (sqr > bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        private bool CanSeePlayer()
        {
            Vector3 eye = transform.position + Vector3.up * _eyeHeight;
            Vector3 playerHead = _player.transform.position + Vector3.up * 1.5f;
            Vector3 to = playerHead - eye;

            if (to.sqrMagnitude > _sightRange * _sightRange)
                return false;

            Vector3 flatTo = new Vector3(to.x, 0f, to.z);
            if (flatTo.sqrMagnitude > 0.001f)
            {
                float angle = Vector3.Angle(transform.forward, flatTo);
                if (angle > _sightFovDegrees * 0.5f)
                    return false;
            }

            // Anything solid between eye and head blocks sight (own collider excluded by origin).
            if (Physics.Raycast(eye, to.normalized, out RaycastHit hit, to.magnitude,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return hit.transform == _player.transform || hit.transform.IsChildOf(_player.transform);
            }

            return true;
        }
    }
}
