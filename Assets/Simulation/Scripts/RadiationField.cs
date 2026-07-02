using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// RadiationField.cs
//
// PURPOSE:  Static, scene-wide physics model of the radiation environment. Keeps
//   the registry of all active RadiationSource emitters (the hidden hot barrel and
//   any others) and computes the mean counts-per-second (CPS) a detector would see
//   at a given position/aim — combining background, inverse-square falloff,
//   shielding occlusion and a directional aim response. RadiationDetector queries
//   GetMeanCps + SamplePoisson every frame to drive the meter readout and clicks.
//
// HOW TO CUSTOMIZE:
//   NOTE: this is a plain static class (NOT a MonoBehaviour). NONE of these values
//   are [SerializeField], so there is NOTHING for this file in the Unity Inspector
//   or the .unity scene YAML. Every knob below is a hardcoded constant/literal —
//   to change behavior you MUST edit this C# file (values below are the live ones,
//   not defaults that a scene can override).
//
//   - BackgroundCps (const, top of class): the always-on baseline count rate so the
//     meter is never fully silent. Edit the constant declaration here.
//   - Point-blank multiplier "16f" (in GetMeanCps, distance < 1e-4f branch): how hot
//     the reading pins when the sensor is basically on top of a source. Edit in the
//     GetMeanCps method.
//   - Directional aim response "0.25f + 0.75f * Pow(...)" (in GetMeanCps): the aim
//     floor (0.25 when pointed away) and how sharply pointing at the source boosts
//     the reading. Edit in the GetMeanCps method.
//   - Distance clamp "0.25f" (dClamped in GetMeanCps): the minimum distance used in
//     the 1/d^2 term so close readings stay finite. Edit in the GetMeanCps method.
//   - Occlusion strength comes from each emitter's RadiationSource.ActivityCpsAt1m
//     and each wall/door's RadiationOccluder.AttenuationFactor — those ARE serialized
//     on their own components; tune them on the RadiationSource / RadiationOccluder
//     GameObjects (spawned at runtime by the scene spawner), not here.
//   - Poisson sampling crossover "30f" and the CpsToMicroSvPerHour calibration
//     "60f / 151f" are hardcoded in SamplePoisson / CpsToMicroSvPerHour respectively;
//     edit those methods to retune noise character or dose-rate conversion.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Static physics model for the whole scene's radiation environment. Holds the registry of all
    /// active <see cref="RadiationSource"/> emitters (typically the barrels) and computes the mean
    /// counts-per-second a detector would see at any position/orientation, combining inverse-square
    /// falloff, shielding occlusion, a directional aim response and a constant background. The
    /// per-frame detector (<c>RadiationDetector</c>) queries this to drive its Poisson-sampled CPS
    /// readout and geiger clicks.
    /// </summary>
    public static class RadiationField
    {
        /// <summary>
        /// Baseline count rate (counts per second) always present even with no source in range,
        /// representing natural background radiation so the meter is never completely silent.
        /// </summary>
        public const float BackgroundCps = 0.5f;

        /// <summary>All radiation emitters currently registered in the scene (the hot barrel plus any others).</summary>
        private static readonly List<RadiationSource> m_sources = new List<RadiationSource>();
        /// <summary>Reusable buffer for non-allocating occlusion raycasts between source and sensor.</summary>
        private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[64];
        /// <summary>Scratch set used to apply each occluder's attenuation only once per ray, even if multiple of its colliders are hit.</summary>
        private static readonly HashSet<RadiationOccluder> s_seenOccluders = new HashSet<RadiationOccluder>();

        /// <summary>Read-only view of the registered radiation sources, for inspection/debugging.</summary>
        public static IReadOnlyList<RadiationSource> Sources
        {
            get { return m_sources; }
        }

        /// <summary>
        /// Adds a source to the field so it contributes to detector readings. Ignores nulls and
        /// duplicates. Sources call this when they become active.
        /// </summary>
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

        /// <summary>
        /// Removes a source from the field so it no longer contributes to detector readings.
        /// Sources call this when they are disabled or destroyed.
        /// </summary>
        public static void Unregister(RadiationSource source)
        {
            if (source == null)
            {
                return;
            }
            m_sources.Remove(source);
        }

        /// <summary>
        /// Computes the expected (mean) count rate in CPS that a detector at <paramref name="sensorPosition"/>
        /// would read, summing background plus each source's contribution. Per source it applies
        /// inverse-square distance falloff, shielding attenuation (via a source-to-sensor occlusion
        /// raycast) and a directional aim weight based on how well <paramref name="sensorAxis"/> points
        /// at the source. This mean is what <see cref="SamplePoisson"/> then randomizes into an integer count.
        /// </summary>
        /// <param name="sensorPosition">World position of the detector's sensing point.</param>
        /// <param name="sensorAxis">World-space direction the detector is aimed (its sensitive axis); zero disables the directional weighting.</param>
        /// <param name="ignoreRoot">Optional transform whose colliders are ignored for occlusion (e.g. the detector/held-tool hierarchy so the player's own body/tool doesn't shield the reading).</param>
        /// <returns>Mean counts per second including background.</returns>
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

                // Sensor essentially on top of the source: skip the divide-by-tiny-distance and
                // just apply a large fixed multiple so the reading pins high instead of exploding.
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

                // Directional aim response: full sensitivity when the detector points straight at the
                // source, tapering to a 0.25 floor when aimed away, so sweeping toward the barrel matters.
                var weight = 1f;
                if (sensorAxis.sqrMagnitude > 1e-8f)
                {
                    var cosTheta = Vector3.Dot(sensorAxis.normalized, direction);
                    weight = 0.25f + 0.75f * Mathf.Pow((1f + cosTheta) * 0.5f, 2f);
                }

                // Clamp the distance so point-blank readings stay finite (avoids the 1/d^2 spike near d=0).
                var dClamped = Mathf.Max(distance, 0.25f);
                mean += source.ActivityCpsAt1m * attenuation * weight / (dClamped * dClamped);
            }

            return mean;
        }

        /// <summary>
        /// Draws a random integer count from a Poisson distribution with the given <paramref name="mean"/>,
        /// modelling the shot-noise of real radiation counting so the readout flickers realistically.
        /// Uses Knuth's exact algorithm for small means and a Gaussian (Box-Muller) approximation for
        /// large means where the exact method would be slow. Never returns a negative value.
        /// </summary>
        public static int SamplePoisson(float mean, System.Random random)
        {
            if (mean <= 0f)
            {
                return 0;
            }

            // Knuth's algorithm: exact for small means (cheap loop, avoids the Gaussian bias near zero).
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

            // Large mean: approximate Poisson by a Normal(mean, mean) via Box-Muller and round.
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

        /// <summary>
        /// Converts a count rate (CPS) into an approximate dose rate in microsieverts per hour for the
        /// detector's dose readout, using a fixed calibration factor tuned for the simulated meter.
        /// </summary>
        public static float CpsToMicroSvPerHour(float cps)
        {
            return cps * 60f / 151f;
        }
    }
}
