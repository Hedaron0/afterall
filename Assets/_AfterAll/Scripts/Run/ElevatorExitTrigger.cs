using AfterAll.Interaction;
using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// Trigger volume across the elevator's exit doorway. Marks the run as "exploring"
    /// once the player has actually stepped out onto the floor.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class ElevatorExitTrigger : MonoBehaviour
    {
        private RunDirector _runDirector;

        private void Awake()
        {
            _runDirector = FindAnyObjectByType<RunDirector>();
            if (_runDirector == null)
                Debug.LogError("[ElevatorExitTrigger] No RunDirector found in scene.", this);
        }

        private void OnTriggerExit(Collider other)
        {
            if (_runDirector == null)
                return;

            if (other.GetComponentInParent<PlayerInteractor>() == null)
                return;

            _runDirector.OnExploreStarted();
        }
    }
}
