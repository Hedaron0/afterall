using UnityEngine;

namespace AfterAll.Audio
{
  /// <summary>
  /// Called by PlayerMovement with horizontal distance moved each frame.
  /// </summary>
  public class FootstepAudio : MonoBehaviour
  {
    [SerializeField] private AudioClip[] _clips;
    [SerializeField] private float _stepDistance = 1.4f;
    [SerializeField] private float _volume = 0.55f;

    private AudioSource _source;
    private float _distanceSinceStep;
    private int _clipIndex;

    private void Awake()
    {
      _source = GetComponent<AudioSource>();
      if (_source == null)
        _source = gameObject.AddComponent<AudioSource>();

      _source.playOnAwake = false;
      _source.loop = false;
      _source.spatialBlend = 0f;
      _source.volume = 1f;
    }

    public void RegisterMovement(float horizontalDistance, bool grounded)
    {
      if (!grounded || horizontalDistance <= 0f || _clips == null || _clips.Length == 0)
      {
        if (!grounded)
          _distanceSinceStep = 0f;
        return;
      }

      _distanceSinceStep += horizontalDistance;
      while (_distanceSinceStep >= _stepDistance)
      {
        _distanceSinceStep -= _stepDistance;
        PlayStep();
      }
    }

    private void PlayStep()
    {
      AudioClip clip = _clips[_clipIndex % _clips.Length];
      _clipIndex++;
      _source.PlayOneShot(clip, _volume);
    }
  }
}
