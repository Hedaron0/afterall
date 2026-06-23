using AfterAll.Player;
using UnityEngine;
using UnityEngine.UI;

namespace AfterAll.UI
{
    /// <summary>
    /// Sprint button beside the inventory slots.
    ///
    /// Mobile:  tap to toggle sprint on/off (the action is fed back via MobileSprintBridge).
    /// PC:      button visually reflects Shift-key state — no tap needed.
    ///
    /// The outer ring Image must be set to Fill Method: Radial 360 in the Inspector.
    /// fillAmount = 1 → full stamina. fillAmount → 0 → exhausted.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public class SprintButtonUI : MonoBehaviour
    {
        [Header("Ring")]
        [SerializeField] private Image _staminaRing;

        [Header("Fill (inner square)")]
        [SerializeField] private Image _fillImage;

        [Header("Colors")]
        [SerializeField] private Color _idleColor       = new Color(0.15f, 0.15f, 0.15f, 0.75f);
        [SerializeField] private Color _sprintingColor  = new Color(1f, 0.65f, 0.1f, 0.90f);
        [SerializeField] private Color _exhaustedColor  = new Color(0.55f, 0.1f, 0.1f, 0.80f);
        [SerializeField] private Color _ringFullColor   = new Color(1f, 0.85f, 0.2f, 0.90f);
        [SerializeField] private Color _ringLowColor    = new Color(0.9f, 0.2f, 0.15f, 0.90f);

        private Stamina _stamina;
        private bool _mobileToggleActive;

        private void Awake()
        {
            _stamina = FindAnyObjectByType<Stamina>();
            GetComponent<Button>().onClick.AddListener(OnButtonClick);
        }

        private void OnEnable()
        {
            Stamina.OnStaminaChanged += OnStaminaChanged;
            Refresh(_stamina != null ? _stamina.Normalized : 1f);
        }

        private void OnDisable()
        {
            Stamina.OnStaminaChanged -= OnStaminaChanged;
        }

        private void OnStaminaChanged(float normalized)
        {
            if (_stamina != null && !_stamina.CanSprint)
                ForceOff();
            else
                Refresh(normalized);
        }

        private void Refresh(float normalized)
        {
            if (_stamina != null && !_stamina.CanSprint && _mobileToggleActive)
                ForceOff();

            if (_staminaRing != null)
            {
                _staminaRing.fillAmount = normalized;
                _staminaRing.color = Color.Lerp(_ringLowColor, _ringFullColor, normalized);
            }

            if (_fillImage == null) return;

            if (_stamina != null && !_stamina.CanSprint)
                _fillImage.color = _exhaustedColor;
            else if (_mobileToggleActive)
                _fillImage.color = _sprintingColor;
            else
                _fillImage.color = _idleColor;
        }

        private void Update()
        {
            if (!MobileInput.IsActive) return;

            // Keep fill color synced while sprinting on mobile.
            Refresh(_stamina != null ? _stamina.Normalized : 1f);
        }

        private void OnButtonClick()
        {
            if (!MobileInput.IsActive) return;

            if (_stamina != null && !_stamina.CanSprint && !_mobileToggleActive) return;

            _mobileToggleActive = !_mobileToggleActive;
            MobileSprintBridge.SetWantsSprint(_mobileToggleActive);
            Refresh(_stamina != null ? _stamina.Normalized : 1f);
        }

        /// <summary>Called by Stamina when exhausted — force toggle off.</summary>
        public void ForceOff()
        {
            _mobileToggleActive = false;
            MobileSprintBridge.SetWantsSprint(false);
            Refresh(_stamina != null ? _stamina.Normalized : 1f);
        }
    }
}
