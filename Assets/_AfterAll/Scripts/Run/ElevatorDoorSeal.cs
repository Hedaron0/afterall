using UnityEngine;

namespace AfterAll.Run
{
    /// <summary>
    /// Temporary elevator door: toggles a placeholder wall GameObject to seal/open the
    /// elevator opening during floor transitions. Placeholder for the real door anim+SFX.
    /// </summary>
    public class ElevatorDoorSeal : MonoBehaviour
    {
        [SerializeField] private GameObject _doorObject;

        public void Close()
        {
            if (_doorObject != null)
                _doorObject.SetActive(true);
        }

        public void Open()
        {
            if (_doorObject != null)
                _doorObject.SetActive(false);
        }
    }
}
