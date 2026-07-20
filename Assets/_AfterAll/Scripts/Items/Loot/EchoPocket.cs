using System;
using System.Collections;
using System.Collections.Generic;
using AfterAll.Items;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AfterAll.Items.Loot
{
    /// <summary>
    /// S3 pockets: fixed-count carry receiver for Small Echoes only. Bulky Echoes go to
    /// BulkyCarrier instead (Core Design §6b: "carried in hands R.E.P.O.-style, one at a time,
    /// slows you, no sprint" — a separate mechanic, not extra pocket capacity).
    /// Q ("DropAll" action) flushes the whole pocket, one physical toss at a time with a short
    /// gap between each (Harun's "tik... tik... tik" cadence) — typically used standing in the
    /// elevator so ElevatorStashVolume picks them up, but works anywhere.
    /// </summary>
    public class EchoPocket : MonoBehaviour, IItemReceiver
    {
        [SerializeField, Min(1)] private int _slotCount = 4;

        [Header("Flush (Q)")]
        [SerializeField] private InputActionReference _flushAllAction;
        [SerializeField] private float _flushInterval = 0.4f;
        [SerializeField] private float _flushThrowImpulse = 3f;
        [SerializeField] private float _flushSpawnDistance = 0.8f;
        [Tooltip("Tiny random tumble nudge per tossed item, same idea as BulkyCarrier's throw spin.")]
        [SerializeField] private float _flushSpinImpulse = 0.05f;

        private readonly List<ItemDefinition> _carried = new();
        private Camera _camera;
        private Coroutine _flushRoutine;

        public IReadOnlyList<ItemDefinition> Carried => _carried;
        public int SlotCount => _slotCount;
        public bool IsFlushing => _flushRoutine != null;

        public event Action<ItemDefinition, int> ItemReceived;

        private void Awake()
        {
            _camera = GetComponentInChildren<Camera>();
        }

        private void OnEnable()
        {
            if (_flushAllAction != null)
                _flushAllAction.action.Enable();
        }

        private void OnDisable()
        {
            if (_flushAllAction != null)
                _flushAllAction.action.Disable();
        }

        private void Update()
        {
            if (!IsFlushing && _carried.Count > 0 &&
                _flushAllAction != null && _flushAllAction.action.WasPressedThisFrame())
            {
                _flushRoutine = StartCoroutine(FlushRoutine());
            }
        }

        public bool CanReceive(ItemDefinition item) =>
            item != null
            && item.Category == ItemCategory.Loot
            && !IsBulky(item)
            && _carried.Count < _slotCount;

        public bool TryReceive(ItemDefinition item, int amount = 1)
        {
            if (amount < 1 || !CanReceive(item) || _carried.Count + amount > _slotCount)
                return false;

            for (int i = 0; i < amount; i++)
                _carried.Add(item);

            ItemReceived?.Invoke(item, amount);
            return true;
        }

        /// <summary>Total value without clearing — for UI/prompt text.</summary>
        public int PeekValue()
        {
            int total = 0;
            foreach (ItemDefinition item in _carried)
            {
                if (EchoDefinition.TryGetFor(item, out EchoDefinition def))
                    total += def.Value;
            }

            return total;
        }

        /// <summary>Sums carried value and clears the pockets. Call on deposit/extract.</summary>
        public int Bank()
        {
            int total = PeekValue();
            _carried.Clear();
            return total;
        }

        /// <summary>Drops everything without banking it. Call on player death.</summary>
        public void Clear()
        {
            _carried.Clear();
            if (_flushRoutine != null)
            {
                StopCoroutine(_flushRoutine);
                _flushRoutine = null;
            }
        }

        private IEnumerator FlushRoutine()
        {
            while (_carried.Count > 0)
            {
                ItemDefinition item = _carried[^1];
                _carried.RemoveAt(_carried.Count - 1);
                TossOne(item);

                if (_carried.Count > 0)
                    yield return new WaitForSeconds(_flushInterval);
            }

            _flushRoutine = null;
        }

        private void TossOne(ItemDefinition item)
        {
            GameObject prefab = item.WorldPickupPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[AfterAll] EchoPocket: {item.DisplayName} has no WorldPickupPrefab set — flushed item lost.");
                return;
            }

            Vector3 forward = _camera != null ? _camera.transform.forward : transform.forward;
            Vector3 spawnPos = (_camera != null ? _camera.transform.position : transform.position) + forward * _flushSpawnDistance;

            GameObject spawned = Instantiate(prefab, spawnPos, Quaternion.identity);
            // S4: each flushed toss is audible (Q-flush spits items one at a time).
            Entities.NoiseEvents.Report(spawnPos, 8f);
            if (spawned.TryGetComponent(out Rigidbody rb))
            {
                rb.AddForce(forward * _flushThrowImpulse, ForceMode.Impulse);

                // Small random tumble nudge so tossed pocket items don't fly perfectly flat —
                // same small-nudge approach as BulkyCarrier.TryThrow, physics does the rest.
                Vector3 randomSpinAxis = new Vector3(
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f),
                    UnityEngine.Random.Range(-1f, 1f)).normalized;
                rb.AddTorque(randomSpinAxis * _flushSpinImpulse, ForceMode.Impulse);
            }
        }

        private static bool IsBulky(ItemDefinition item) =>
            EchoDefinition.TryGetFor(item, out EchoDefinition def) && def.SizeClass == EchoSizeClass.Bulky;
    }
}
