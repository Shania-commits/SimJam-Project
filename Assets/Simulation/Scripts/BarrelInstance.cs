using UnityEngine;

// =============================================================================
// BarrelInstance.cs
//
// PURPOSE:  Per-barrel metadata component attached at runtime to every spawned
//           drum in the lab. Holds the barrel's source label + radiation count
//           (CPM) and, in editor/dev builds only, shows a floating billboard
//           "cheat" label above the barrel telling you which drum is the hidden
//           source. Values are assigned by the spawner via Initialize(); this
//           component does not decide which barrel is radioactive.
//
// HOW TO CUSTOMIZE:
//   NOTE: This component is created and configured entirely in code by the barrel
//   spawner MonoBehaviour — it is NOT placed on a prefab/GameObject in a .unity
//   scene. So the two [SerializeField] defaults below are almost never overridden
//   by scene YAML; they are the effective runtime values unless the spawner sets
//   them. The label/CPM/source values themselves come from the spawner, not here.
//
//   - debugLabel ([SerializeField], line ~17): the TextMesh used for the floating
//     readout. Left null in normal use — it is auto-created lazily by
//     EnsureDebugLabel(). Only wire it in the Inspector if this component ever
//     lives on a prefab and you want a pre-authored label.
//   - debugLabelLocalPosition ([SerializeField], default (0, 0.85, 0)): height/
//     offset of the floating label above the barrel. Raise Y to float it higher.
//     Change the default here, OR override in the Inspector if BarrelInstance is
//     ever added to a scene/prefab GameObject.
//   - SourceLabel / RadiationCount (public, set only via Initialize() /
//     SetRadiationCount()): the barrel's name and CPM. HARDCODED entry point is
//     the SPAWNER, not this file — edit the spawner MonoBehaviour that calls
//     Initialize() to change which barrel is the source and its count values.
//   - Debug-label visibility gate: wrapped in #if UNITY_EDITOR ||
//     DEVELOPMENT_BUILD (SetDebugLabelVisible, line ~66). Release builds ALWAYS
//     force the label hidden so players cannot cheat — to expose it in a real
//     build you must edit those preprocessor branches here.
//   - Label styling is HARDCODED in EnsureDebugLabel() (line ~111): characterSize
//     0.06, fontSize 64, color Color.yellow, centered. Edit that method directly.
//   - Label text format is HARDCODED in UpdateDebugLabel() (line ~133) as
//     "{SourceLabel}\n{RadiationCount} CPM". Edit that method to reformat.
// =============================================================================
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
