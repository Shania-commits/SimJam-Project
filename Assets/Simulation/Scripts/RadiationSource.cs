using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// The hidden radioactive source inside a barrel. Adds no renderer, label, or any
    /// other player-visible change; the detector finds it purely through RadiationField.
    /// <summary>
    /// Component attached to the one hidden radioactive barrel. It is a pure emitter with no
    /// visible footprint: it registers itself with the static <see cref="RadiationField"/> on
    /// enable so the detector can compute counts from it via inverse-square + occlusion, and
    /// carries only the source's activity, isotope identity, and physical emission point.
    /// </summary>
    public class RadiationSource : MonoBehaviour
    {
        /// <summary>
        /// The source's strength expressed as counts-per-second measured at 1 metre with no
        /// occlusion; <see cref="RadiationField"/> scales this by inverse-square distance.
        /// Defaults to a strong Cs-137-like 5000 CPS until <see cref="Configure"/> overrides it.
        /// </summary>
        public float ActivityCpsAt1m { get; private set; } = 5000f;
        /// <summary>
        /// Human-readable isotope label (e.g. "Cs-137") used for the detector's identification
        /// readout; defaults to Cs-137 until <see cref="Configure"/> sets it.
        /// </summary>
        public string IsotopeName { get; private set; } = "Cs-137";

        /// <summary>
        /// World-space point the radiation appears to emanate from — the centre of the barrel's
        /// renderer bounds when one exists, otherwise the transform position. Used as the source
        /// end of the distance/occlusion calculation in <see cref="RadiationField"/>.
        /// </summary>
        public Vector3 EmissionPoint
        {
            get
            {
                var emissionRenderer = GetComponentInChildren<Renderer>();
                return emissionRenderer != null ? emissionRenderer.bounds.center : transform.position;
            }
        }

        /// <summary>
        /// Sets this source's activity and isotope, called by the spawner when it seeds the one
        /// hot barrel. Clamps activity to non-negative and substitutes "Unknown" for an empty
        /// isotope name.
        /// </summary>
        public void Configure(float activityCpsAt1m, string isotopeName)
        {
            ActivityCpsAt1m = Mathf.Max(0f, activityCpsAt1m);
            IsotopeName = string.IsNullOrEmpty(isotopeName) ? "Unknown" : isotopeName;
        }

        /// <summary>
        /// Registers this source with the static <see cref="RadiationField"/> so the detector
        /// begins accounting for it while the barrel is active.
        /// </summary>
        private void OnEnable()
        {
            RadiationField.Register(this);
        }

        /// <summary>
        /// Unregisters this source from the static <see cref="RadiationField"/> when the barrel
        /// is disabled or destroyed so it no longer contributes counts.
        /// </summary>
        private void OnDisable()
        {
            RadiationField.Unregister(this);
        }
    }
}
