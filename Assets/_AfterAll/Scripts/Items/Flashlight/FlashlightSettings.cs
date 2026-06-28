using UnityEngine;

namespace AfterAll.Items.Flashlight
{
    [CreateAssetMenu(menuName = "AfterAll/Flashlight Settings", fileName = "FlashlightSettings")]
    public sealed class FlashlightSettings : ScriptableObject
    {
        [Header("Beam")]
        [SerializeField] private Color _color = new(1f, 0.92f, 0.78f);
        [SerializeField] private float _intensity = 6f;
        [SerializeField] private float _range = 24f;
        [SerializeField] private float _spotAngle = 42f;
        [SerializeField] private float _innerSpotAngle = 22f;
        [SerializeField] private LightShadows _shadows = LightShadows.Soft;
        [SerializeField] private float _shadowStrength = 0.7f;

        [Header("Behaviour")]
        [SerializeField] private bool _autoOnWhenEquipped = true;

        [Header("Flicker")]
        [SerializeField] private float _flickerAmount = 0.08f;
        [SerializeField] private float _flickerSpeed = 12f;
        [SerializeField] private float _dropoutChance = 0.002f;
        [SerializeField] private float _dropoutDuration = 0.04f;

        [Header("Audio")]
        [SerializeField] private AudioClip _toggleOnClip;
        [SerializeField] private AudioClip _toggleOffClip;
        [SerializeField] private AudioClip _humLoopClip;
        [SerializeField] private float _clickVolume = 0.45f;
        [SerializeField] private float _humVolume = 0.12f;

        public Color Color => _color;
        public float Intensity => _intensity;
        public float Range => _range;
        public float SpotAngle => _spotAngle;
        public float InnerSpotAngle => _innerSpotAngle;
        public LightShadows Shadows => _shadows;
        public float ShadowStrength => _shadowStrength;
        public bool AutoOnWhenEquipped => _autoOnWhenEquipped;
        public float FlickerAmount => _flickerAmount;
        public float FlickerSpeed => _flickerSpeed;
        public float DropoutChance => _dropoutChance;
        public float DropoutDuration => _dropoutDuration;
        public AudioClip ToggleOnClip => _toggleOnClip;
        public AudioClip ToggleOffClip => _toggleOffClip;
        public AudioClip HumLoopClip => _humLoopClip;
        public float ClickVolume => _clickVolume;
        public float HumVolume => _humVolume;
    }
}
