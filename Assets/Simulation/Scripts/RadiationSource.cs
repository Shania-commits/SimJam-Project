using UnityEngine;

// =============================================================================
// RadiationSource.cs
//
// PURPOSE:  The invisible "hot" marker attached at runtime to the ONE hidden
//           radioactive barrel. It has no renderer/label of its own; it just
//           registers with the static RadiationField (OnEnable) so the handheld
//           detector can compute counts from it via inverse-square + occlusion.
//           It carries the source's activity (CPS at 1 m), isotope name, and
//           emission point (barrel bounds centre).
//
// HOW TO CUSTOMIZE:
//   IMPORTANT: This component has NO [SerializeField] fields, so it is NEVER
//   stored in a .unity scene file. It is added purely in code and its values
//   are set by Configure(activityCpsAt1m, isotopeName) at spawn time. Editing
//   the class defaults below (ActivityCpsAt1m = 5000f, IsotopeName = "Cs-137")
//   has NO effect at runtime, because Configure() always overwrites them. To
//   change what the player actually experiences, edit the CALLERS instead:
//
//   - Source strength (Lab scene): the activity passed in is a random log-uniform
//     value between m_minSourceActivityCps (default 1500) and m_maxSourceActivityCps
//     (default 30000). Those two ARE [SerializeField] on RadiationLabRoomSpawner,
//     so their live values live in the Lab scene YAML — edit them on the spawner
//     GameObject's RadiationLabRoomSpawner component in the Unity Inspector for the
//     Lab scene, NOT here. (Set in RadiationLabRoomSpawner.AssignHotSource.)
//   - Source strength (Tutorial scene): comes from m_hotBarrelActivityCps on
//     TutorialRoomInteractionController; edit that component in the Tutorial scene.
//   - Isotope label: chosen at random from the HARDCODED array
//     s_isotopeNames = { "Cs-137", "Co-60", "Ir-192", "Am-241" } in
//     RadiationLabRoomSpawner (not serialized) — edit that array to change the pool.
//     The Tutorial path passes a hardcoded "Cs-137" in
//     TutorialRoomInteractionController; edit that literal to change it.
//   - EmissionPoint is derived (barrel renderer bounds centre, else transform
//     position) and is not tunable; change it only by editing the EmissionPoint
//     getter method in this file.
// =============================================================================

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
