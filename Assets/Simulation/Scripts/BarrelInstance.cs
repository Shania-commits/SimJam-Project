using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public class BarrelInstance : MonoBehaviour
    {
        [SerializeField] private TextMesh debugLabel;
        [SerializeField] private Vector3 debugLabelLocalPosition = new Vector3(0f, 0.85f, 0f);

        public string SourceLabel { get; private set; }
        public int RadiationCount { get; private set; }

        public void Initialize(string sourceLabel, int radiationCount, bool showDebugLabel)
        {
            SourceLabel = sourceLabel;
            RadiationCount = radiationCount;
            SetDebugLabelVisible(showDebugLabel);
            UpdateDebugLabel();
        }

        public void SetRadiationCount(int radiationCount)
        {
            RadiationCount = radiationCount;
            UpdateDebugLabel();
        }

        public void SetDebugLabelVisible(bool isVisible)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (isVisible)
            {
                EnsureDebugLabel();
                UpdateDebugLabel();
            }

            if (debugLabel != null)
            {
                debugLabel.gameObject.SetActive(isVisible);
            }
#else
            // Release builds must never reveal radiation values above barrels.
            if (debugLabel != null)
            {
                debugLabel.gameObject.SetActive(false);
            }
#endif
        }

        private void LateUpdate()
        {
            if (debugLabel == null || !debugLabel.gameObject.activeSelf || Camera.main == null)
            {
                return;
            }

            var labelTransform = debugLabel.transform;
            var directionToCamera = labelTransform.position - Camera.main.transform.position;
            if (directionToCamera.sqrMagnitude > 0.0001f)
            {
                labelTransform.rotation = Quaternion.LookRotation(directionToCamera.normalized, Vector3.up);
            }
        }

        private void EnsureDebugLabel()
        {
            if (debugLabel != null)
            {
                return;
            }

            var labelObject = new GameObject("Radiation Count Label");
            labelObject.transform.SetParent(transform, false);
            labelObject.transform.localPosition = debugLabelLocalPosition;

            debugLabel = labelObject.AddComponent<TextMesh>();
            debugLabel.anchor = TextAnchor.MiddleCenter;
            debugLabel.alignment = TextAlignment.Center;
            debugLabel.characterSize = 0.06f;
            debugLabel.fontSize = 64;
            debugLabel.color = Color.yellow;
        }

        private void UpdateDebugLabel()
        {
            if (debugLabel == null)
            {
                return;
            }

            debugLabel.text = $"{SourceLabel}\n{RadiationCount} CPM";
        }
    }
}
