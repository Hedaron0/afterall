using System;
using UnityEngine;

namespace AfterAll.Entities
{
    /// <summary>
    /// S4: global noise bus. Anything audible calls Report; the hunter (and later the escalation
    /// director, §5.2 tertiary layer) subscribes. loudnessRadius is meters — a listener farther
    /// than that never hears the event.
    /// </summary>
    public static class NoiseEvents
    {
        public static event Action<Vector3, float> NoiseReported;

        public static void Report(Vector3 worldPos, float loudnessRadius)
        {
            if (loudnessRadius <= 0f)
                return;

            NoiseReported?.Invoke(worldPos, loudnessRadius);
        }
    }
}
