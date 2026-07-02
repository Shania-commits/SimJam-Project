using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Synthesizes and plays the identiFINDER's Geiger-counter click train. Builds a short click
    /// waveform at runtime (no imported audio asset) and, each frame, fires a Poisson-distributed
    /// burst of clicks whose average rate is driven by the detector's current CPS. Slight per-click
    /// pitch and volume jitter keeps the crackle from sounding mechanical. Spatialized so the audio
    /// appears to come from the handheld meter itself.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class GeigerAudio : MonoBehaviour
    {
        /// <summary>Spatialized 3D source used to play the synthesized click one-shots.</summary>
        private AudioSource m_audioSource;
        /// <summary>The procedurally generated single-click waveform played on each tick.</summary>
        private AudioClip m_clip;
        /// <summary>PRNG used for waveform noise plus per-frame Poisson sampling and pitch/volume jitter.</summary>
        private System.Random m_random;
        /// <summary>Target average clicks per second (0-120), typically fed from the detector's CPS reading.</summary>
        private float m_rate;

        /// <summary>
        /// Configures the AudioSource for near-field 3D playback and synthesizes the click waveform:
        /// a ~3 ms burst of white noise mixed with a 5.5 kHz tone under a fast exponential decay
        /// envelope, then peak-normalized. The result is baked into a one-shot AudioClip.
        /// </summary>
        private void Awake()
        {
            m_audioSource = GetComponent<AudioSource>();
            m_audioSource.playOnAwake = false;
            m_audioSource.loop = false;
            m_audioSource.spatialBlend = 1f;
            m_audioSource.minDistance = 0.1f;
            m_audioSource.maxDistance = 6f;
            m_audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            m_audioSource.volume = 0.85f;

            m_random = new System.Random(unchecked(System.Environment.TickCount * 31 + GetInstanceID()));

            int sampleRate = AudioSettings.outputSampleRate;
            if (sampleRate <= 0)
            {
                sampleRate = 44100;
            }

            // Click is ~3 ms long (at least 64 samples so very low output rates still produce audio).
            int sampleCount = Mathf.Max(64, (int)(sampleRate * 0.003f));
            float[] samples = new float[sampleCount];
            float peak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
                // Sharp exponential attack/decay (~0.8 ms time constant) gives the transient "tick".
                float envelope = Mathf.Exp(-t / 0.0008f);
                float noise = (float)(m_random.NextDouble() * 2.0 - 1.0);
                float value = (noise * 0.7f + Mathf.Sin(2f * Mathf.PI * 5500f * t) * 0.5f) * envelope;
                samples[i] = value;
                float abs = Mathf.Abs(value);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            // Peak-normalize to ~0.8 so every generated click has consistent loudness headroom.
            if (peak > 0f)
            {
                float scale = 0.8f / peak;
                for (int i = 0; i < sampleCount; i++)
                {
                    samples[i] *= scale;
                }
            }

            m_clip = AudioClip.Create("GeigerClick", sampleCount, 1, sampleRate, false);
            m_clip.SetData(samples, 0);
        }

        /// <summary>
        /// Sets the desired average click rate (clicks per second), clamped to 0-120. Callers
        /// (e.g. the RadiationDetector) map their smoothed CPS reading through this each frame so
        /// the crackle density tracks how "hot" the meter currently is.
        /// </summary>
        public void SetRate(float clicksPerSecond)
        {
            m_rate = Mathf.Clamp(clicksPerSecond, 0f, 120f);
        }

        /// <summary>
        /// Each frame, draws a Poisson-distributed number of clicks for the elapsed time at the
        /// current rate (capped at 3 per frame to avoid audio spam), then plays each with randomized
        /// pitch and volume so repeated clicks sound organic rather than looped.
        /// </summary>
        private void Update()
        {
            float expected = m_rate * Time.deltaTime;
            int n = SamplePoisson(expected);
            if (n > 3)
            {
                n = 3;
            }

            for (int i = 0; i < n; i++)
            {
                m_audioSource.pitch = 0.95f + 0.13f * (float)m_random.NextDouble();
                m_audioSource.PlayOneShot(m_clip, 0.8f + 0.2f * (float)m_random.NextDouble());
            }
        }

        /// <summary>
        /// Draws a random count from a Poisson distribution with the given mean using Knuth's
        /// algorithm, modelling the random arrival of radioactive decay events within a time slice.
        /// </summary>
        private int SamplePoisson(float mean)
        {
            if (mean <= 0f)
            {
                return 0;
            }

            double L = System.Math.Exp(-mean);
            int k = 0;
            double p = 1.0;
            do
            {
                k++;
                p *= m_random.NextDouble();
            }
            while (p > L);
            return k - 1;
        }
    }
}
