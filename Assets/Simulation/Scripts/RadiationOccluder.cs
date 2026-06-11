using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// Marks an object that attenuates radiation passing through it. The factor is the
    /// fraction of the signal that survives (0 = full shield, 1 = transparent).
    public class RadiationOccluder : MonoBehaviour
    {
        [SerializeField, Range(0f, 1f)] private float m_attenuationFactor = 0.5f;

        public float AttenuationFactor
        {
            get => m_attenuationFactor;
            set => m_attenuationFactor = Mathf.Clamp01(value);
        }

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
