using UnityEngine;

namespace AfterAll.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AmbienceLoop : MonoBehaviour
    {
        [SerializeField] private AudioClip _clip;
        [SerializeField] [Range(0f, 1f)] private float _volume = 0.35f;

        private AudioSource _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();

            if (_clip == null)
            {
                Debug.LogError($"[AmbienceLoop] No AudioClip assigned on '{gameObject.name}'. Ambience will be silent.", this);
                return;
            }

            _source.clip = _clip;
            _source.loop = true;
            _source.volume = _volume;
            _source.spatialBlend = 0f;
            _source.playOnAwake = false;
            _source.Play();
        }

        public void SetVolume(float v) => _source.volume = Mathf.Clamp01(v);
    }
}
