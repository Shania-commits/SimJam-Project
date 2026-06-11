using UnityEngine;

namespace SimJam.BarrelSimulator
{
    [RequireComponent(typeof(AudioSource))]
    public class GeigerAudio : MonoBehaviour
    {
        private AudioSource m_audioSource;
        private AudioClip m_clip;
        private System.Random m_random;
        private float m_rate;

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

            int sampleCount = Mathf.Max(64, (int)(sampleRate * 0.003f));
            float[] samples = new float[sampleCount];
            float peak = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = i / (float)sampleRate;
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

        public void SetRate(float clicksPerSecond)
        {
            m_rate = Mathf.Clamp(clicksPerSecond, 0f, 120f);
        }

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
