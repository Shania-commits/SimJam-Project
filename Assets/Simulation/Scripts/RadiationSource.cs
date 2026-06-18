using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// The hidden radioactive source inside a barrel. Adds no renderer, label, or any
    /// other player-visible change; the detector finds it purely through RadiationField.
    public class RadiationSource : MonoBehaviour
    {
        public float ActivityCpsAt1m { get; private set; } = 5000f;
        public string IsotopeName { get; private set; } = "Cs-137";

        public Vector3 EmissionPoint
        {
            get
            {
                var emissionRenderer = GetComponentInChildren<Renderer>();
                return emissionRenderer != null ? emissionRenderer.bounds.center : transform.position;
            }
        }

        public void Configure(float activityCpsAt1m, string isotopeName)
        {
            ActivityCpsAt1m = Mathf.Max(0f, activityCpsAt1m);
            IsotopeName = string.IsNullOrEmpty(isotopeName) ? "Unknown" : isotopeName;
        }

        private void OnEnable()
        {
            RadiationField.Register(this);
        }

        private void OnDisable()
        {
            RadiationField.Unregister(this);
        }
    }
}
