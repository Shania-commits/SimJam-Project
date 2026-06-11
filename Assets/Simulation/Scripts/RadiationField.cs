using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public static class RadiationField
    {
        public const float BackgroundCps = 0.5f;

        private static readonly List<RadiationSource> m_sources = new List<RadiationSource>();
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[64];
        private static readonly HashSet<RadiationOccluder> s_seenOccluders = new HashSet<RadiationOccluder>();

        public static IReadOnlyList<RadiationSource> Sources
        {
            get { return m_sources; }
        }

        public static void Register(RadiationSource source)
        {
            if (source == null)
            {
                return;
            }
            if (!m_sources.Contains(source))
            {
                m_sources.Add(source);
            }
        }

        public static void Unregister(RadiationSource source)
        {
            if (source == null)
            {
                return;
            }
            m_sources.Remove(source);
        }

        public static float GetMeanCps(Vector3 sensorPosition, Vector3 sensorAxis, Transform ignoreRoot)
        {
            var mean = BackgroundCps;

            for (var i = m_sources.Count - 1; i >= 0; i--)
            {
                var source = m_sources[i];
                if (source == null)
                {
                    m_sources.RemoveAt(i);
                    continue;
                }

                var emission = source.EmissionPoint;
                var toSource = emission - sensorPosition;
                var distance = toSource.magnitude;

                if (distance < 1e-4f)
                {
                    mean += source.ActivityCpsAt1m * 16f;
                    continue;
                }

                var direction = toSource.normalized;
                var attenuation = 1f;

                // Cast from the source toward the sensor, not the other way around: raycasts
                // never hit a collider their origin is inside, and the emission point (inside
                // the hot barrel) can never be embedded in room shielding — whereas a player can
                // trivially push the sensor tip inside a wall or the door and would otherwise
                // read fully unshielded through it.
                var ray = new Ray(emission, -direction);
                var hitCount = Physics.RaycastNonAlloc(
                    ray,
                    s_hitBuffer,
                    distance,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore);

                s_seenOccluders.Clear();
                for (var h = 0; h < hitCount; h++)
                {
                    var hitTransform = s_hitBuffer[h].collider.transform;
                    if (hitTransform.IsChildOf(source.transform))
                    {
                        continue;
                    }
                    if (ignoreRoot != null && hitTransform.IsChildOf(ignoreRoot))
                    {
                        continue;
                    }

                    var occluder = s_hitBuffer[h].collider.GetComponentInParent<RadiationOccluder>();
                    if (occluder == null)
                    {
                        continue;
                    }
                    if (!s_seenOccluders.Add(occluder))
                    {
                        continue;
                    }

                    attenuation *= Mathf.Clamp01(occluder.AttenuationFactor);
                }

                var weight = 1f;
                if (sensorAxis.sqrMagnitude > 1e-8f)
                {
                    var cosTheta = Vector3.Dot(sensorAxis.normalized, direction);
                    weight = 0.25f + 0.75f * Mathf.Pow((1f + cosTheta) * 0.5f, 2f);
                }

                var dClamped = Mathf.Max(distance, 0.25f);
                mean += source.ActivityCpsAt1m * attenuation * weight / (dClamped * dClamped);
            }

            return mean;
        }

        public static int SamplePoisson(float mean, System.Random random)
        {
            if (mean <= 0f)
            {
                return 0;
            }

            if (mean < 30f)
            {
                var limit = Math.Exp(-mean);
                var k = 0;
                var p = 1.0;
                do
                {
                    k++;
                    p *= random.NextDouble();
                }
                while (p > limit);
                return k - 1;
            }

            var u1 = random.NextDouble();
            if (u1 <= double.Epsilon)
            {
                u1 = double.Epsilon;
            }
            var u2 = random.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            var result = (int)Math.Round(mean + Math.Sqrt(mean) * z);
            return Math.Max(0, result);
        }

        public static float CpsToMicroSvPerHour(float cps)
        {
            return cps * 60f / 151f;
        }
    }
}
