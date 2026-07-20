using System.Collections.Generic;
using AfterAll.Environment;
using AfterAll.Player;
using AfterAll.Run;
using UnityEngine;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4 entity v1: sound-driven unkillable hunter. Patrol (random rooms) → Investigate (last
    /// heard noise) → Chase (line of sight) → kill on touch → RunDirector.OnPlayerDied.
    /// Moves on the EntityNavGraph (doorway waypoints, straight lines inside rooms); Y is locked
    /// to the flat, height-aligned floors. Greybox-friendly: any capsule works as the body.
    /// </summary>
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

        private readonly List<Vector3> _path = new();
        private State _state = State.Patrol;
        private PlayerMovement _player;
        private int _pathIndex;
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
        }

        private void OnEnable()
        {
            NoiseEvents.NoiseReported += HandleNoise;
            _state = State.Patrol;
            _path.Clear();
        }

        private void OnDisable()
        {
            NoiseEvents.NoiseReported -= HandleNoise;
        }

        private void Update()
        {
            if (_player == null || _navGraph == null || !_navGraph.IsReady)
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
            if (MoveAlongPath(_patrolSpeed))
                return;

            // Path finished (or none): wander to a random room center.
            RoomInstance target = PickPatrolRoom();
            if (target != null)
                RepathTo(target.GetSpawnPosition(0f));
        }

        private void TickInvestigate()
        {
            if (MoveAlongPath(_patrolSpeed))
                return;

            // Arrived: linger scanning, then go back to patrol.
            if (_lingerUntil <= 0f)
                _lingerUntil = Time.time + _investigateLingerSeconds;
            else if (Time.time >= _lingerUntil)
                _state = State.Patrol;
        }

        private void TickChase(bool seesPlayer)
        {
            if (!seesPlayer && Time.time - _lastSeenPlayerAt > _loseSightGraceSeconds)
            {
                // Lost them: investigate the last known position.
                _investigateTarget = _player.transform.position;
                _state = State.Investigate;
                _lingerUntil = 0f;
                RepathTo(_investigateTarget);
                return;
            }

            if (Time.time >= _nextRepathAt)
            {
                _nextRepathAt = Time.time + _chaseRepathInterval;
                RepathTo(_player.transform.position);
            }

            MoveAlongPath(_chaseSpeed);

            Vector3 toPlayer = _player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude <= _killDistance * _killDistance)
                KillPlayer();
        }

        private void EnterChase()
        {
            _state = State.Chase;
            _nextRepathAt = 0f;
        }

        private void KillPlayer()
        {
            _runDirector?.OnPlayerDied();
        }

        /// <summary>True while still moving; false when the path is exhausted.</summary>
        private bool MoveAlongPath(float speed)
        {
            if (_pathIndex >= _path.Count)
                return false;

            Vector3 target = _path[_pathIndex];
            Vector3 to = target - transform.position;
            to.y = 0f;

            if (to.magnitude <= _waypointReachDistance)
            {
                _pathIndex++;
                return _pathIndex < _path.Count;
            }

            Vector3 dir = to.normalized;
            transform.position += dir * (speed * Time.deltaTime);
            Quaternion look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, _turnSpeedDeg * Time.deltaTime);
            return true;
        }

        private void RepathTo(Vector3 worldPos)
        {
            if (_navGraph.TryGetPath(transform.position, worldPos, _path))
                _pathIndex = 0;
            else
                _path.Clear();
        }

        private RoomInstance PickPatrolRoom()
        {
            IReadOnlyList<RoomInstance> rooms = _navGraph.Rooms;
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
