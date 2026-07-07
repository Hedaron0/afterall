using UnityEngine;

namespace AfterAll.Environment
{
    [CreateAssetMenu(fileName = "RoomContentProfile", menuName = "AfterAll/Room Content Profile")]
    public class RoomContentProfile : ScriptableObject
    {
        [Header("Density Caps")]
        [Tooltip("Maximum props per room. -1 = no cap.")]
        [SerializeField] private int _maxProps = -1;

        [Tooltip("Maximum pillars per room. -1 = no cap.")]
        [SerializeField] private int _maxPillars = -1;

        [Tooltip("Maximum ceiling lights per room. -1 = no cap.")]
        [SerializeField] private int _maxCeilingLights = -1;

        [Header("Pillar Override")]
        [Tooltip("When >= 0, replaces each Pillar marker's spawn chance. -1 = use marker value.")]
        [SerializeField, Range(-1f, 1f)] private float _pillarSpawnChanceOverride = -1f;

        public int MaxProps => _maxProps;
        public int MaxPillars => _maxPillars;
        public int MaxCeilingLights => _maxCeilingLights;
        public float PillarSpawnChanceOverride => _pillarSpawnChanceOverride;

        public bool HasPropCap => _maxProps >= 0;
        public bool HasPillarCap => _maxPillars >= 0;
        public bool HasCeilingLightCap => _maxCeilingLights >= 0;
        public bool HasPillarChanceOverride => _pillarSpawnChanceOverride >= 0f;
    }
}
