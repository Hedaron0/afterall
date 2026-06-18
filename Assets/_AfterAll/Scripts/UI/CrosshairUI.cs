using UnityEngine;
using UnityEngine.UI;
using AfterAll.Interaction;

namespace AfterAll.UI
{
    public class CrosshairUI : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor _interactor;
        [SerializeField] private Image _crosshair;

        private void Awake()
        {
            if (_interactor == null)
                _interactor = FindFirstObjectByType<PlayerInteractor>();

            if (_crosshair != null)
                _crosshair.enabled = false;
        }

        private void Update()
        {
            if (_crosshair == null || _interactor == null)
                return;

            _crosshair.enabled = _interactor.HasInteractableTarget;
        }
    }
}
