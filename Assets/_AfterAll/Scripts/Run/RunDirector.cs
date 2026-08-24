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
        [SerializeField] private PlayerLook _playerLook;
        [SerializeField] private EchoPocket _echoPocket;
        [SerializeField] private BulkyCarrier _bulkyCarrier;

        [Header("Loot")]
        [Tooltip("Every EchoDefinition asset in the game. Forces them to load so EchoDefinition.TryGetFor can resolve value/size class — see EchoDefinition.RegisterAll.")]
        [SerializeField] private EchoDefinition[] _knownEchoDefinitions;

        [Header("Run Goal")]
        [Tooltip("Extract from at least this depth, with at least Target Banked Echoes of value, and the run counts as COMPLETED instead of a plain extract. Core Design 6: the White Door sits at depth 6.")]
        [SerializeField, Min(1)] private int _targetDepth = 6;
        [Tooltip("Value that single extraction must be worth to complete the run. Extracting with less is still a successful run, it just does not finish the goal.")]
        [SerializeField, Min(0)] private int _targetBankedEchoes = 500;

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

        // Set while a run summary is on screen: the next floor keeps building behind the closed
        // door, but the player stays frozen in the cabin until they acknowledge it.
        private bool _awaitingSummaryAck;
        private RoomInstance _pendingElevatorRoom;

        public int Depth { get; private set; }
        public RunState State { get; private set; } = RunState.InElevator;

        public int TargetDepth => _targetDepth;
        public int TargetBankedEchoes => _targetBankedEchoes;

        public event Action<int> DepthChanged;
        public event Action RunEnded;
        public event Action RunFailed;
        /// <summary>Fired when the player first leaves the elevator on a floor (S4: hunter spawn).</summary>
        public event Action ExploreStarted;
        /// <summary>
        /// Fired on death AND on extract, with everything the summary screen needs. Having a
        /// subscriber is what makes the run pause for acknowledgement — with no UI wired, the run
        /// loop keeps its old uninterrupted behavior instead of soft-locking the player.
        /// </summary>
        public event Action<RunSummary> RunConcluded;

        private void Awake()
        {
            EchoDefinition.RegisterAll(_knownEchoDefinitions);

            if (_spawner == null)
                _spawner = GetComponent<RoomPoolSpawner>();

            if (_playerMovement == null)
                _playerMovement = FindAnyObjectByType<PlayerMovement>();

            if (_playerLook == null)
                _playerLook = FindAnyObjectByType<PlayerLook>();

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

        /// <summary>
        /// Keeps State honest about where the player actually is. ElevatorExitTrigger only ever
        /// fired one way, so after the first step onto a floor the run stayed Exploring for the
        /// rest of that floor even once the player walked back into the cabin. Two things silently
        /// broke as a result: ElevatorPanel gates on InElevator, so both buttons went dead for the
        /// remainder of the floor, and HunterEntity's "the cabin is safe ground" rule stopped
        /// applying. ElevatorStashVolume.PlayerInside is a fresh physics sweep every frame, so it
        /// is the ground truth to sync against rather than another one-shot trigger.
        /// </summary>
        private void Update()
        {
            if (State != RunState.Exploring)
                return;

            ElevatorStashVolume stash = GetCurrentElevatorStashVolume();
            if (stash != null && stash.PlayerInside)
                State = RunState.InElevator;
        }

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
            if (_transitionRoutine != null || _awaitingSummaryAck)
                return;

            _transitionRoutine = StartCoroutine(TransitionRoutine(descending: true));
        }

        /// <summary>Extract: run ends successfully, EchoPocket contents are banked to MetaProgress.</summary>
        public void GoUp()
        {
            if (_transitionRoutine != null || _awaitingSummaryAck)
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

                bool completed = Depth >= _targetDepth && banked >= _targetBankedEchoes;
                RunOutcome outcome = completed ? RunOutcome.Completed : RunOutcome.Extracted;

                RunEnded?.Invoke();
                // Interim economy (no shop yet, Core Design §7): extract just starts a fresh
                // depth-0 run behind the already-closed door, same sequencing GoDown uses — so
                // extract no longer leaves the player standing in the stale floor with nothing
                // visibly happening. ConcludeRun holds them in the cabin until the summary is
                // dismissed, then hands them the new floor.
                ConcludeRun(new RunSummary(outcome, Depth, banked, MetaProgress.BankedEchoes,
                                           _targetDepth, _targetBankedEchoes));
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

        /// <summary>Look is toggled separately from movement so the summary screen can hand the
        /// mouse back: PlayerLook re-locks and hides the cursor every Update while it is enabled, so
        /// nothing on a UI panel is clickable on desktop until it is switched off.
        ///
        /// Switching it off is NOT enough on its own — PlayerLook.OnDisable calls UpdateCursorState
        /// too, which re-locks the cursor on the way out. So the unlock has to be applied after the
        /// component is down, or the summary screen appears with an invisible, captured cursor and
        /// its button cannot be clicked. Re-enabling restores the lock through PlayerLook.OnEnable.</summary>
        private void SetLookEnabled(bool value)
        {
            if (_playerLook != null && _playerLook.enabled != value)
                _playerLook.enabled = value;

            if (!value)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
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
            _pendingElevatorRoom = elevatorRoom;

            // Floor is built, but the run summary still owns the screen — keep the player frozen
            // and the door shut until they dismiss it.
            if (_awaitingSummaryAck)
                return;

            ReleasePlayerIntoFloor(elevatorRoom);
        }

        private void ReleasePlayerIntoFloor(RoomInstance elevatorRoom)
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
            int depthReached = Depth; // ResetRunState zeroes this — capture it for the summary first.

            _echoPocket?.Clear();
            _bulkyCarrier?.Clear();
            GetCurrentElevatorStashVolume()?.ClearOnDeath();
            RunFailed?.Invoke();
            ResetRunState();

            TeleportPlayerToCabin();
            FreezePlayer();
            CloseCurrentElevatorDoor();
            ConcludeRun(new RunSummary(RunOutcome.Died, depthReached, 0, MetaProgress.BankedEchoes,
                                       _targetDepth, _targetBankedEchoes));
            // Unfrozen (and door reopened) once the summary is acknowledged and the depth-0 floor
            // has landed — whichever happens last.
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

        /// <summary>Publishes the finished run and starts the next one behind the closed door. The
        /// player is only held in place if something is actually listening to show the summary.</summary>
        private void ConcludeRun(RunSummary summary)
        {
            _awaitingSummaryAck = RunConcluded != null;
            _pendingElevatorRoom = null;

            if (_awaitingSummaryAck)
            {
                FreezePlayer();
                SetLookEnabled(false);
            }

            RunConcluded?.Invoke(summary);
            BeginRun();
        }

        /// <summary>Called by the summary screen's continue button. Hands the player the floor that
        /// has been building behind the door — or just unfreezes them if it has not landed yet, in
        /// which case HandleFloorReady opens the door when it does.</summary>
        public void AcknowledgeRunSummary()
        {
            if (!_awaitingSummaryAck)
                return;

            _awaitingSummaryAck = false;
            SetLookEnabled(true);
            ReleasePlayerIntoFloor(_pendingElevatorRoom);
        }

        private void SpawnFloor()
        {
            State = RunState.InElevator;
            int roomCount = Mathf.Min(_maxRoomCount, _baseRoomCount + Depth * _roomCountPerDepth);
            // Normally our own seed, but a Push → Play from Layout Top View gets to override the first
            // floor so you actually see the layout you previewed. The override is one-shot.
            int seed = _spawner.ConsumeSeedOverride(_seedRng.Next());
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
