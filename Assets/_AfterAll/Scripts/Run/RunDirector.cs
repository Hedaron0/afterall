using System;
using System.Collections;
using AfterAll.Environment;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// M1 run-loop skeleton: elevator floor → explore → DOWN (deeper floor) or UP (extract, run ends).
    /// No entities/loot/stash yet — extract and death paths are stubbed for the systems that will own them.
    /// </summary>
    public enum RunState
    {
        InElevator,
        Exploring
    }

    public class RunDirector : MonoBehaviour
    {
        [SerializeField] private RoomPoolSpawner _spawner;

        [Header("Floor Budget")]
        [SerializeField, Min(8)] private int _baseRoomCount = 20;
        [SerializeField, Min(0)] private int _roomCountPerDepth = 4;
        [SerializeField, Min(8)] private int _maxRoomCount = 60;

        [Header("Seed")]
        [SerializeField] private bool _useFixedRunSeed;
        [SerializeField] private int _fixedRunSeed = 12345;

        [Header("Elevator Transition")]
        [Tooltip("Delay between the button press (anim + SFX + door close) and the floor rebuild, so the press is visible before ClearLevelRoot tears the elevator down.")]
        [SerializeField, Min(0f)] private float _transitionDelaySeconds = 1.0f;

        private System.Random _seedRng;
        private Coroutine _transitionRoutine;

        public int Depth { get; private set; }
        public RunState State { get; private set; } = RunState.InElevator;

        public event Action<int> DepthChanged;
        public event Action RunEnded;
        public event Action RunFailed;

        private void Awake()
        {
            if (_spawner == null)
                _spawner = GetComponent<RoomPoolSpawner>();
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

        private void Start() => BeginRun();

        public void BeginRun()
        {
            _seedRng = new System.Random(_useFixedRunSeed ? _fixedRunSeed : System.Environment.TickCount ^ Guid.NewGuid().GetHashCode());
            Depth = 0;
            SpawnFloor();
        }

        /// <summary>Player left the elevator room and is exploring the floor.</summary>
        public void OnExploreStarted()
        {
            if (State == RunState.Exploring)
                return;

            State = RunState.Exploring;
        }

        /// <summary>Descend: current floor's progress is discarded, a new deeper floor is generated.</summary>
        public void GoDown()
        {
            if (_transitionRoutine != null)
                return;

            _transitionRoutine = StartCoroutine(TransitionRoutine(descending: true));
        }

        /// <summary>Extract: run ends successfully. Carried loot is kept — stash/inventory hookup lands with the loot system.</summary>
        public void GoUp()
        {
            if (_transitionRoutine != null)
                return;

            _transitionRoutine = StartCoroutine(TransitionRoutine(descending: false));
        }

        /// <summary>Lets the button press anim/SFX/door-close play before the floor rebuild destroys the elevator.</summary>
        private IEnumerator TransitionRoutine(bool descending)
        {
            CloseCurrentElevatorDoor();

            if (_transitionDelaySeconds > 0f)
                yield return new WaitForSeconds(_transitionDelaySeconds);

            if (descending)
            {
                Depth++;
                SpawnFloor();
            }
            else
            {
                RunEnded?.Invoke();
                ResetRunState();
            }

            _transitionRoutine = null;
        }

        private void CloseCurrentElevatorDoor()
        {
            RoomInstance elevator = _spawner != null ? _spawner.CurrentElevatorRoom : null;
            if (elevator == null)
                return;

            ElevatorDoorSeal seal = elevator.GetComponentInChildren<ElevatorDoorSeal>(true);
            seal?.Close();
        }

        private void HandleFloorReady(RoomInstance elevatorRoom)
        {
            if (elevatorRoom == null)
                return;

            ElevatorDoorSeal seal = elevatorRoom.GetComponentInChildren<ElevatorDoorSeal>(true);
            seal?.Open();
        }

        /// <summary>Player died: run resets fully. Carried + stash loot is lost — meta progression persists elsewhere.</summary>
        public void OnPlayerDied()
        {
            RunFailed?.Invoke();
            ResetRunState();
        }

        private void SpawnFloor()
        {
            State = RunState.InElevator;
            int roomCount = Mathf.Min(_maxRoomCount, _baseRoomCount + Depth * _roomCountPerDepth);
            int seed = _seedRng.Next();
            _spawner.BeginNewFloor(seed, roomCount);
            DepthChanged?.Invoke(Depth);
        }

        private void ResetRunState()
        {
            Depth = 0;
            State = RunState.InElevator;
        }
    }
}
