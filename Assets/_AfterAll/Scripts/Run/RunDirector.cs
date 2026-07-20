using System;
using System.Collections;
using AfterAll.Environment;
using AfterAll.Items.Loot;
using AfterAll.Meta;
using AfterAll.Player;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// Run-loop authority: elevator floor → explore → DOWN (deeper floor) or UP (extract, run ends).
    /// Extract banks EchoPocket + BulkyCarrier + whatever's resting in ElevatorStashVolume to
    /// MetaProgress; death clears all three unbanked.
    /// </summary>
    public enum RunState
    {
        InElevator,
        Exploring
    }

    public class RunDirector : MonoBehaviour
    {
        [SerializeField] private RoomPoolSpawner _spawner;
        [SerializeField] private PlayerMovement _playerMovement;
        [SerializeField] private EchoPocket _echoPocket;
        [SerializeField] private BulkyCarrier _bulkyCarrier;

        [Header("Loot")]
        [Tooltip("Every EchoDefinition asset in the game. Forces them to load so EchoDefinition.TryGetFor can resolve value/size class — see EchoDefinition.RegisterAll.")]
        [SerializeField] private EchoDefinition[] _knownEchoDefinitions;

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
        /// <summary>Fired when the player first leaves the elevator on a floor (S4: hunter spawn).</summary>
        public event Action ExploreStarted;

        private void Awake()
        {
            EchoDefinition.RegisterAll(_knownEchoDefinitions);

            if (_spawner == null)
                _spawner = GetComponent<RoomPoolSpawner>();

            if (_playerMovement == null)
                _playerMovement = FindAnyObjectByType<PlayerMovement>();

            if (_echoPocket == null)
                _echoPocket = FindAnyObjectByType<EchoPocket>();

            if (_bulkyCarrier == null)
                _bulkyCarrier = FindAnyObjectByType<BulkyCarrier>();
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
            ExploreStarted?.Invoke();
        }

        /// <summary>Descend: current floor's progress is discarded, a new deeper floor is generated.</summary>
        public void GoDown()
        {
            if (_transitionRoutine != null)
                return;

            _transitionRoutine = StartCoroutine(TransitionRoutine(descending: true));
        }

        /// <summary>Extract: run ends successfully, EchoPocket contents are banked to MetaProgress.</summary>
        public void GoUp()
        {
            if (_transitionRoutine != null)
                return;

            _transitionRoutine = StartCoroutine(TransitionRoutine(descending: false));
        }

        /// <summary>Lets the button press anim/SFX/door-close play before the floor rebuild destroys everything outside the (persistent) cabin.</summary>
        private IEnumerator TransitionRoutine(bool descending)
        {
            // The cabin itself is never destroyed and never moves (RoomPoolSpawner re-aligns the
            // REST of the level onto it, not the other way around) — only the door needs to
            // close, sealing the player inside real, solid ground while the new floor assembles
            // around it. No movement freeze needed anymore now that free-fall-through-the-void
            // can't happen (that was the old pre-persistent-cabin risk this used to guard against).
            CloseCurrentElevatorDoor();

            if (_transitionDelaySeconds > 0f)
                yield return new WaitForSeconds(_transitionDelaySeconds);

            if (descending)
            {
                Depth++;
                SpawnFloor();
                // Door reopens in HandleFloorReady once the new floor finishes building —
                // SpawnFloor's Build() runs asynchronously across several frames.
            }
            else
            {
                int banked = 0;
                if (_echoPocket != null)
                    banked += _echoPocket.Bank(); // abstract pocket value — no physical object to sweep

                // Releases the held object in place (gravity restored) but its value is NOT added
                // here: the stash sweep below counts every physical Loot item in the cabin,
                // including this one, so adding Bank()'s return too would double-count it.
                _bulkyCarrier?.Bank();

                ElevatorStashVolume stash = GetCurrentElevatorStashVolume();
                if (stash != null)
                    banked += stash.CollectAndDestroyAll();

                MetaProgress.AddBanked(banked);

                RunEnded?.Invoke();
                // Interim economy (no shop yet, Core Design §7): extract just starts a fresh
                // depth-0 run behind the already-closed door, same sequencing GoDown uses — so
                // extract no longer leaves the player standing in the stale floor with nothing
                // visibly happening. Player can move freely inside the cabin the whole time.
                BeginRun();
            }

            _transitionRoutine = null;
        }

        private void FreezePlayer()
        {
            if (_playerMovement != null)
                _playerMovement.enabled = false;
        }

        private void UnfreezePlayer()
        {
            if (_playerMovement != null)
                _playerMovement.enabled = true;
        }

        private void CloseCurrentElevatorDoor()
        {
            RoomInstance elevator = _spawner != null ? _spawner.CurrentElevatorRoom : null;
            if (elevator == null)
                return;

            ElevatorDoorSeal seal = elevator.GetComponentInChildren<ElevatorDoorSeal>(true);
            seal?.Close();
        }

        /// <summary>ElevatorStashVolume lives on the per-floor elevator instance, so it must be
        /// re-resolved each time rather than cached — ClearLevelRoot destroys/recreates it on every
        /// floor change. Public so UI (running value display) can read CurrentValue live.</summary>
        public ElevatorStashVolume GetCurrentElevatorStashVolume()
        {
            RoomInstance elevator = _spawner != null ? _spawner.CurrentElevatorRoom : null;
            return elevator != null ? elevator.GetComponentInChildren<ElevatorStashVolume>(true) : null;
        }

        private void HandleFloorReady(RoomInstance elevatorRoom)
        {
            UnfreezePlayer();

            if (elevatorRoom == null)
                return;

            ElevatorDoorSeal seal = elevatorRoom.GetComponentInChildren<ElevatorDoorSeal>(true);
            seal?.Open();
        }

        /// <summary>Player died: run resets fully. Carried + stash loot is lost — meta progression
        /// persists elsewhere. The player wakes up back in the (persistent) cabin at depth 0 with a
        /// freshly generated floor behind the closed door.</summary>
        public void OnPlayerDied()
        {
            _echoPocket?.Clear();
            _bulkyCarrier?.Clear();
            GetCurrentElevatorStashVolume()?.ClearOnDeath();
            RunFailed?.Invoke();
            ResetRunState();

            TeleportPlayerToCabin();
            FreezePlayer();
            CloseCurrentElevatorDoor();
            BeginRun();
            // Unfrozen (and door reopened) by HandleFloorReady once the depth-0 floor lands.
        }

        private void TeleportPlayerToCabin()
        {
            RoomInstance cabin = _spawner != null ? _spawner.CurrentElevatorRoom : null;
            if (cabin == null || _playerMovement == null)
                return;

            Vector3 spawnPos = cabin.GetSpawnPosition(1.0f);
            Transform playerTransform = _playerMovement.transform;
            CharacterController controller = _playerMovement.GetComponent<CharacterController>();
            if (controller != null)
                controller.enabled = false;
            playerTransform.position = spawnPos;
            if (controller != null)
                controller.enabled = true;
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
