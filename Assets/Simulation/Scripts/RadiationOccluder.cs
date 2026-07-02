using UnityEngine;

// =============================================================================
// RadiationOccluder.cs
//
// PURPOSE:  Marks a scene object (walls, shelves, drums, etc.) as a partial
//           radiation shield. When RadiationField casts a ray from the hidden
//           source to the detector, any RadiationOccluder in the way multiplies
//           the surviving signal by its attenuation factor, so props behind
//           cover read weaker on the handheld detector.
//
// HOW TO CUSTOMIZE:
//   - m_attenuationFactor ([SerializeField, Range(0..1)], default 0.5):
//     the fraction of signal that PASSES THROUGH this object.
//     0 = a perfect shield (blocks everything), 1 = fully transparent.
//     WHERE: This component is added and configured AT RUNTIME by the scene
//     spawner via the static Attach(target, attenuationFactor) helper below —
//     it is not authored on a GameObject in a .unity scene. To change how much
//     a given prop shields, edit the attenuationFactor value passed into
//     RadiationOccluder.Attach(...) at each call site in the spawner
//     MonoBehaviour that builds that scene (search the spawner scripts for
//     "RadiationOccluder.Attach"). Changing the 0.5 default here only affects
//     occluders created WITHOUT an explicit factor, which currently do not
//     exist because Attach always sets one.
//   - The attenuation is CONSUMED by the raycast/occlusion code in
//     RadiationField (not here). To change HOW factors combine along a ray
//     (e.g. multiply vs. add) or which layers are tested, edit RadiationField,
//     not this file.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// Marks an object that attenuates radiation passing through it. The factor is the
    /// fraction of the signal that survives (0 = full shield, 1 = transparent).
    public class RadiationOccluder : MonoBehaviour
    {
        /// <summary>
        /// Fraction of the radiation signal that survives passage through this object,
        /// consumed by the RadiationField occlusion raycasts (0 = fully shields, 1 = fully transparent).
        /// </summary>
        [SerializeField, Range(0f, 1f)] private float m_attenuationFactor = 0.5f;

        /// <summary>
        /// Public accessor for the attenuation factor; setting clamps the value into the valid 0..1 range.
        /// </summary>
        public float AttenuationFactor
        {
            get => m_attenuationFactor;
            set => m_attenuationFactor = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Convenience helper used by the spawner to tag a runtime-built prop (walls, shelves, drums)
        /// as a radiation shield: finds or adds a RadiationOccluder on the target and sets its factor.
        /// Returns the component, or null if the target is null.
        /// </summary>
        public static RadiationOccluder Attach(GameObject target, float attenuationFactor)
        {
            if (target == null)
            {
                return null;
            }

            var occluder = target.GetComponent<RadiationOccluder>();
            if (occluder == null)
            {
                occluder = target.AddComponent<RadiationOccluder>();
            }

            occluder.AttenuationFactor = attenuationFactor;
            return occluder;
        }
    }
}
