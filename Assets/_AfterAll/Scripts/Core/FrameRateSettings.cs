using UnityEngine;

namespace AfterAll.Core
{
    [CreateAssetMenu(fileName = "FrameRateSettings", menuName = "AfterAll/Settings/Frame Rate")]
    public class FrameRateSettings : ScriptableObject
    {
        [SerializeField] private FrameRateMode _defaultMode = FrameRateMode.Unlimited;
        [SerializeField] [Min(1)] private int _customFps = 120;

        public FrameRateMode DefaultMode => _defaultMode;
        public int CustomFps => _customFps;
    }
}
