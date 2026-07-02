using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Per-barrel metadata component attached to each spawned drum in the lab. Stores the barrel's
    /// isotope/source label and its radiation count, and (in editor/dev builds only) shows a
    /// floating billboard label above the barrel for debugging which drum is the hidden source.
    /// One barrel is the real radioactive source; the rest are decoys with a background count.
    /// </summary>
    public class BarrelInstance : MonoBehaviour
    {
        /// <summary>
        /// Optional TextMesh billboard used to display the source label and CPM above the barrel.
        /// Created lazily in editor/dev builds; kept hidden in release builds so players cannot cheat.
        /// </summary>
        [SerializeField] private TextMesh debugLabel;
        /// <summary>
        /// Local offset (relative to the barrel) at which the debug label is positioned, roughly at
        /// the top of the drum so the text floats just above it.
        /// </summary>
        [SerializeField] private Vector3 debugLabelLocalPosition = new Vector3(0f, 0.85f, 0f);

        /// <summary>
        /// Human-readable identifier for this barrel's radiation source (e.g. isotope name or a
        /// generic label). Shown on the debug label and used to describe the barrel.
        /// </summary>
        public string SourceLabel { get; private set; }
        /// <summary>
        /// The barrel's radiation count in CPM (counts per minute). The hidden source barrel has a
        /// high value; decoy barrels sit at a low background level.
        /// </summary>
        public int RadiationCount { get; private set; }

        /// <summary>
        /// Configures the barrel with its source label and radiation count and applies the initial
        /// debug-label visibility. Called by the spawner right after the barrel is created.
        /// </summary>
        /// <param name="sourceLabel">Identifier for this barrel's radiation source.</param>
        /// <param name="radiationCount">Radiation count in CPM for this barrel.</param>
        /// <param name="showDebugLabel">Whether to display the floating debug label (editor/dev only).</param>
        public void Initialize(string sourceLabel, int radiationCount, bool showDebugLabel)
        {
            SourceLabel = sourceLabel;
            RadiationCount = radiationCount;
            SetDebugLabelVisible(showDebugLabel);
            UpdateDebugLabel();
        }

        /// <summary>
        /// Updates the barrel's radiation count at runtime and refreshes the debug label text.
        /// </summary>
        /// <param name="radiationCount">New radiation count in CPM.</param>
        public void SetRadiationCount(int radiationCount)
        {
            RadiationCount = radiationCount;
            UpdateDebugLabel();
        }

        /// <summary>
        /// Shows or hides the floating radiation-count debug label. In editor/development builds it
        /// creates the label on demand when made visible; in release builds it is force-hidden so the
        /// barrel's radiation value is never exposed to the player.
        /// </summary>
        /// <param name="isVisible">Whether the debug label should be shown.</param>
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

        /// <summary>
        /// Billboards the debug label so it always faces the main camera (the player's headset),
        /// keeping the radiation readout legible from any angle. No-op when the label is absent or hidden.
        /// </summary>
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

        /// <summary>
        /// Lazily creates the TextMesh debug label as a child of the barrel with sensible styling
        /// (yellow, centered) if it does not already exist.
        /// </summary>
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

        /// <summary>
        /// Refreshes the debug label text to show the current source label and radiation count in CPM.
        /// </summary>
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
