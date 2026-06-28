using System;
using UnityEngine;
using UnityEngine.UI;

namespace SimJam
{
    /// <summary>
    /// Controller laser pointer for world-space uGUI buttons, intentionally independent of the
    /// EventSystem / OVRInputModule (Meta's module uses the legacy UnityEngine.Input class, which
    /// throws under this project's new-Input-System-only setup). It casts a ray from the right
    /// controller, draws a laser + cursor to the button under the ray, and invokes that button's
    /// onClick when the click button is pressed. Respects Button.interactable, so gated tutorial
    /// steps still can't be skipped. Added automatically by TutorialManager / StartScreenSpawner.
    /// </summary>
    [DisallowMultipleComponent]
    public class VrUiPointer : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float m_maxLength = 5f;
        [SerializeField] private OVRInput.RawButton m_clickButton = OVRInput.RawButton.A;
        [SerializeField] private Color m_laserColor = new Color(0.35f, 0.8f, 1f, 0.9f);

        private OVRCameraRig m_rig;
        private Transform m_ray;
        private LineRenderer m_line;
        private Transform m_cursor;
        private Button[] m_buttons = Array.Empty<Button>();
        private float m_nextScan;

        private void Start()
        {
            BuildLaser();
        }

        private void Update()
        {
            ResolveRay();
            if (m_ray == null || m_line == null)
            {
                SetLaserVisible(false);
                return;
            }

            // Refresh the button list periodically (cheap for these small UIs; avoids per-frame alloc).
            if (Time.unscaledTime >= m_nextScan)
            {
                m_buttons = FindObjectsByType<Button>(FindObjectsInactive.Exclude);
                m_nextScan = Time.unscaledTime + 0.4f;
            }

            var ray = new Ray(m_ray.position, m_ray.forward);
            Button hovered = null;
            var hitPoint = m_ray.position + m_ray.forward * m_maxLength;
            var bestDist = float.MaxValue;

            foreach (var button in m_buttons)
            {
                if (button == null || !button.isActiveAndEnabled)
                {
                    continue;
                }

                if (!(button.transform is RectTransform rect) || !RayHitsRect(ray, rect, out var point))
                {
                    continue;
                }

                var d = Vector3.Distance(ray.origin, point);
                if (d < bestDist)
                {
                    bestDist = d;
                    hitPoint = point;
                    hovered = button;
                }
            }

            SetLaserVisible(true);
            m_line.SetPosition(0, m_ray.position);
            m_line.SetPosition(1, hitPoint);
            if (m_cursor != null)
            {
                m_cursor.position = hitPoint;
                m_cursor.gameObject.SetActive(hovered != null);
            }

            if (hovered != null && hovered.interactable && OVRInput.GetDown(m_clickButton))
            {
                hovered.onClick.Invoke();
            }
        }

        private void ResolveRay()
        {
            if (m_ray != null)
            {
                return;
            }

            if (m_rig == null)
            {
                m_rig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (m_rig != null)
            {
                m_ray = m_rig.rightHandAnchor != null ? m_rig.rightHandAnchor : m_rig.rightControllerAnchor;
            }
        }

        // True if the ray crosses the button's world-space rectangle; outputs the world hit point.
        private static bool RayHitsRect(Ray ray, RectTransform rect, out Vector3 point)
        {
            point = default;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners); // bottom-left, top-left, top-right, bottom-right
            var plane = new Plane(corners[0], corners[1], corners[2]);
            if (!plane.Raycast(ray, out var enter))
            {
                return false;
            }

            point = ray.GetPoint(enter);
            var local = rect.InverseTransformPoint(point);
            return rect.rect.Contains(new Vector2(local.x, local.y));
        }

        private void BuildLaser()
        {
            var laserGo = new GameObject("UI Laser Pointer");
            laserGo.transform.SetParent(transform, false);

            m_line = laserGo.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.positionCount = 2;
            m_line.widthMultiplier = 0.004f;
            m_line.numCapVertices = 4;
            m_line.material = new Material(Shader.Find("Sprites/Default"));
            m_line.startColor = m_laserColor;
            m_line.endColor = m_laserColor;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "UI Cursor";
            dot.transform.SetParent(laserGo.transform, false);
            dot.transform.localScale = Vector3.one * 0.025f;
            var col = dot.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var rend = dot.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = m_laserColor };
            }

            m_cursor = dot.transform;
            SetLaserVisible(false);
        }

        private void SetLaserVisible(bool visible)
        {
            if (m_line != null)
            {
                m_line.enabled = visible;
            }

            if (m_cursor != null && !visible)
            {
                m_cursor.gameObject.SetActive(false);
            }
        }
    }
}
