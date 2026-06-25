using UnityEngine;

namespace SimJam.Tutorial
{
    public class TutorialStepMarker : MonoBehaviour
    {
        [SerializeField] private TutorialManager m_tutorialManager;
        [SerializeField, Min(0)] private int m_stepIndex = 3;
        [SerializeField, Min(0.1f)] private float m_completionRadius = 0.65f;
        [SerializeField] private Transform m_playerTarget;
        [SerializeField] private GameObject m_markerVisual;
        [SerializeField] private Color m_markerColor = new Color(0.1f, 0.75f, 1f, 0.72f);
        [SerializeField, Min(0.1f)] private float m_pulseSpeed = 2.5f;
        [SerializeField, Min(0.01f)] private float m_pulseAmount = 0.18f;

        private Vector3 m_baseVisualScale;
        private bool m_hasCompleted;

        private void Awake()
        {
            if (m_markerVisual == null)
            {
                m_markerVisual = CreateDefaultMarkerVisual();
            }

            m_baseVisualScale = m_markerVisual.transform.localScale;
            SetMarkerVisible(false);
        }

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
            if (Vector3.Distance(GetPlayerPosition(), transform.position) <= m_completionRadius)
            {
                m_hasCompleted = true;
                SetMarkerVisible(false);
                m_tutorialManager.CompleteStepIfCurrent(m_stepIndex);
            }
        }

        private Vector3 GetPlayerPosition()
        {
            if (m_playerTarget == null && Camera.main != null)
            {
                m_playerTarget = Camera.main.transform;
            }

            return m_playerTarget != null ? m_playerTarget.position : Vector3.zero;
        }

        private void PulseMarker()
        {
            if (m_markerVisual == null)
            {
                return;
            }

            var pulse = 1f + Mathf.Sin(Time.time * m_pulseSpeed) * m_pulseAmount;
            m_markerVisual.transform.localScale = m_baseVisualScale * pulse;
        }

        private void SetMarkerVisible(bool isVisible)
        {
            if (m_markerVisual != null && m_markerVisual.activeSelf != isVisible)
            {
                m_markerVisual.SetActive(isVisible);
            }
        }

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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = m_markerColor;
            Gizmos.DrawWireSphere(transform.position, m_completionRadius);
        }
    }
}
