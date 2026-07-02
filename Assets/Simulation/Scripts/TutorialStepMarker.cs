using UnityEngine;

// =============================================================================
// TutorialStepMarker.cs
//
// PURPOSE:  A pulsing glowing disc on the tutorial-room floor that gates the
//   "walk to the marker" tutorial step. While its step is the current step it
//   shows and pulses the disc, tracks the player's headset, and reports the
//   step complete to the TutorialManager once the player stands within range.
//
// HOW TO CUSTOMIZE:
//   IMPORTANT: this component lives on a GameObject inside the TUTORIAL scene
//   (the TutorialRoom). Every [SerializeField] below has a saved value in that
//   scene's .unity YAML, so changing the DEFAULT here does NOT change runtime
//   behavior. To actually change behavior, select the marker GameObject in the
//   tutorial scene and edit the "Tutorial Step Marker" component in the Unity
//   Inspector (or edit the corresponding value in the scene YAML).
//
//   - m_tutorialManager (TutorialManager reference): the controller queried for
//     which step is active and told when this step is done. Wire it in the
//     Inspector; if null the marker does nothing (Update early-returns).
//   - m_stepIndex (default 3): which tutorial step this marker gates. Must match
//     the walk-to-marker step's index in the TutorialManager. Inspector.
//   - m_completionRadius (default 0.65 m): horizontal distance the player must
//     get within to complete the step; also drawn as the selection gizmo.
//     Inspector.
//   - m_playerTarget (Transform): what counts as the player's position. Leave
//     unset to auto-bind to Camera.main (the headset) at runtime. Inspector.
//   - m_markerColor (default light blue RGBA): tint + emissive color of the
//     auto-created disc and the editor gizmo. Inspector.
//   - m_pulseSpeed (default 2.5) / m_pulseAmount (default 0.18): frequency and
//     amplitude (fraction of base scale) of the attention pulse. Inspector.
//   - m_markerVisual (GameObject): the disc shown on the floor. If left unset,
//     one is auto-built at runtime. The fallback disc's SHAPE is HARDCODED (not
//     serialized): its local position (y=0.015), scale (0.65 x 0.02 x 0.65),
//     collider removal, and Standard-shader emissive material are all created in
//     CreateDefaultMarkerVisual() — edit that method to change the fallback disc.
//     To use a custom mesh instead, assign m_markerVisual in the Inspector.
// =============================================================================

namespace SimJam.Tutorial
{
    /// <summary>
    /// A pulsing floor marker that drives one of the TutorialRoom's gated steps: the "walk to the
    /// marker" step. While its assigned tutorial step is current, it shows a glowing disc on the
    /// floor and watches the player's headset; once the player steps within the completion radius it
    /// reports the step as done to the <see cref="TutorialManager"/> and hides itself.
    /// </summary>
    public class TutorialStepMarker : MonoBehaviour
    {
        /// <summary>The tutorial controller this marker belongs to; used to test which step is active and to report completion.</summary>
        [SerializeField] private TutorialManager m_tutorialManager;
        /// <summary>Index of the tutorial step this marker gates (defaults to the walk-to-marker step). The marker only shows and listens while this step is current.</summary>
        [SerializeField, Min(0)] private int m_stepIndex = 3;
        /// <summary>Horizontal distance (metres) the player must get within for the step to count as complete.</summary>
        [SerializeField, Min(0.1f)] private float m_completionRadius = 0.65f;
        /// <summary>Transform tracked as the player's position; falls back to the main camera (headset) if left unset.</summary>
        [SerializeField] private Transform m_playerTarget;
        /// <summary>The visible marker object shown on the floor; auto-created as a flat cylinder disc if not assigned.</summary>
        [SerializeField] private GameObject m_markerVisual;
        /// <summary>Tint (and emissive colour) of the auto-created marker disc, plus the selection gizmo colour.</summary>
        [SerializeField] private Color m_markerColor = new Color(0.1f, 0.75f, 1f, 0.72f);
        /// <summary>Frequency of the marker's attention-grabbing scale pulse.</summary>
        [SerializeField, Min(0.1f)] private float m_pulseSpeed = 2.5f;
        /// <summary>Amplitude of the scale pulse as a fraction of the base scale.</summary>
        [SerializeField, Min(0.01f)] private float m_pulseAmount = 0.18f;

        /// <summary>Cached original scale of the marker visual, used as the baseline for the pulse animation.</summary>
        private Vector3 m_baseVisualScale;
        /// <summary>Latches true once the player has reached the marker, so the step is only completed and hidden once.</summary>
        private bool m_hasCompleted;

        /// <summary>Creates a default marker disc if none was assigned, caches its base scale, and hides it until the step becomes active.</summary>
        private void Awake()
        {
            if (m_markerVisual == null)
            {
                m_markerVisual = CreateDefaultMarkerVisual();
            }

            m_baseVisualScale = m_markerVisual.transform.localScale;
            SetMarkerVisible(false);
        }

        /// <summary>While this marker's step is current and unfinished, pulses the disc and checks whether the player has walked close enough to complete the step.</summary>
        private void Update()
        {
            if (m_tutorialManager == null)
            {
                return;
            }

            var isActiveStep = m_tutorialManager.IsCurrentStep(m_stepIndex);
            SetMarkerVisible(isActiveStep && !m_hasCompleted);

            if (!isActiveStep || m_hasCompleted)
            {
                return;
            }

            PulseMarker();
            // Compare on the horizontal plane only: the player target is the headset (~1.6 m eye
            // height) while the marker sits on the floor, so a full 3D distance never drops below
            // head height and the step could never complete. Ignore the vertical offset.
            var toMarker = GetPlayerPosition() - transform.position;
            toMarker.y = 0f;
            if (toMarker.magnitude <= m_completionRadius)
            {
                m_hasCompleted = true;
                SetMarkerVisible(false);
                m_tutorialManager.CompleteStepIfCurrent(m_stepIndex);
            }
        }

        /// <summary>Returns the tracked player position, lazily binding to the main camera (headset) if no explicit target was set.</summary>
        private Vector3 GetPlayerPosition()
        {
            if (m_playerTarget == null && Camera.main != null)
            {
                m_playerTarget = Camera.main.transform;
            }

            return m_playerTarget != null ? m_playerTarget.position : Vector3.zero;
        }

        /// <summary>Animates a gentle sinusoidal scale pulse on the marker disc to draw the player's eye.</summary>
        private void PulseMarker()
        {
            if (m_markerVisual == null)
            {
                return;
            }

            var pulse = 1f + Mathf.Sin(Time.time * m_pulseSpeed) * m_pulseAmount;
            m_markerVisual.transform.localScale = m_baseVisualScale * pulse;
        }

        /// <summary>Shows or hides the marker disc, only toggling when its active state actually needs to change.</summary>
        private void SetMarkerVisible(bool isVisible)
        {
            if (m_markerVisual != null && m_markerVisual.activeSelf != isVisible)
            {
                m_markerVisual.SetActive(isVisible);
            }
        }

        /// <summary>Builds the fallback marker visual: a thin, colliderless emissive cylinder disc parented under this marker and tinted with <see cref="m_markerColor"/>.</summary>
        private GameObject CreateDefaultMarkerVisual()
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker Visual";
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0f, 0.015f, 0f);
            marker.transform.localScale = new Vector3(0.65f, 0.02f, 0.65f);

            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Standard"));
                material.color = m_markerColor;
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", m_markerColor * 1.5f);
                renderer.material = material;
            }

            return marker;
        }

        /// <summary>Draws the completion radius as a wire sphere in the editor to aid placement of the marker.</summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = m_markerColor;
            Gizmos.DrawWireSphere(transform.position, m_completionRadius);
        }
    }
}
