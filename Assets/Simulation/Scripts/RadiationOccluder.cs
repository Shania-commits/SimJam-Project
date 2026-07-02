using UnityEngine;

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
