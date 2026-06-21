using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

namespace AfterAll.UI
{
    /// <summary>
    /// Toggles invisible mobile action strips (jump / crouch only).
    /// Move + look + interact are handled in code on the Player — no joystick, no E button.
    /// </summary>
    public class MobileHUD : MonoBehaviour
    {
        [SerializeField] private GameObject _mobileControlsRoot;

        [Tooltip("Show mobile UI during Editor Play Mode for layout testing.")]
        [SerializeField] private bool _simulateMobileInEditor;

        private void Awake() => ApplyMobileMode();

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying)
                ApplyMobileMode();
        }
#endif

        private void OnDestroy()
        {
#if UNITY_EDITOR
            TouchSimulation.Disable();
#endif
        }

        private void ApplyMobileMode()
        {
            MobileInput.SetSimulateInEditor(_simulateMobileInEditor);

            if (_mobileControlsRoot != null)
                _mobileControlsRoot.SetActive(MobileInput.IsActive);

#if UNITY_EDITOR
            if (_simulateMobileInEditor)
            {
                EnhancedTouchSupport.Enable();
                TouchSimulation.Enable();
            }
            else
            {
                TouchSimulation.Disable();
            }
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
#endif
        }
    }
}