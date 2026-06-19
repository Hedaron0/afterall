using UnityEngine;

namespace AfterAll.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public class AmbienceLoop : MonoBehaviour
    {
        [SerializeField] private AudioClip _clip;
        [SerializeField] private float _volume = 0.22f;

        private void Awake()
        {
            var source = GetComponent<AudioSource>();
            source.clip = _clip;
            source.loop = true;
            source.volume = _volume;
            source.spatialBlend = 0f;
            source.playOnAwake = true;

            if (!source.isPlaying)
                source.Play();
        }
    }
}
