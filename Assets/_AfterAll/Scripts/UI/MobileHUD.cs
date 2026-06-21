using UnityEngine;

namespace AfterAll.UI
{
    /// <summary>
    /// Activates mobile-only HUD elements (joystick, interact button) when running on
    /// Android or iOS. Hides them on PC / editor (unless _simulateMobileInEditor is on).
    ///
    /// Attach to the HUD object. Wire Joystick_BG and the InteractButton via the inspector,
    /// or run AfterAll → Setup Mobile HUD to wire everything automatically.
    /// </summary>
    public class MobileHUD : MonoBehaviour
    {
        [SerializeField] private GameObject _joystickRoot;
        [SerializeField] private GameObject _interactButtonRoot;

        [Tooltip("Show mobile UI during Editor Play Mode for layout testing.")]
        [SerializeField] private bool _simulateMobileInEditor;

        private void Awake()
        {
            bool mobile = IsMobilePlatform();

            if (_joystickRoot != null)
                _joystickRoot.SetActive(mobile);

            if (_interactButtonRoot != null)
                _interactButtonRoot.SetActive(mobile);

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
#endif
        }

        private bool IsMobilePlatform()
        {
#if UNITY_EDITOR
            return _simulateMobileInEditor;
#elif UNITY_ANDROID || UNITY_IOS
            return true;
#else
            return false;
#endif
        }
    }
}
