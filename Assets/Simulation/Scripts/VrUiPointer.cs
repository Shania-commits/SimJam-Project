using System;
using UnityEngine;
using UnityEngine.UI;

namespace SimJam
{
    /// <summary>
    /// Controller laser pointer for world-space uGUI buttons, intentionally independent of the
    /// EventSystem / OVRInputModule (Meta's module uses the legacy UnityEngine.Input class, which
    /// throws under this project's new-Input-System-only setup). Casts a ray from the right
    /// controller, draws a laser + cursor, and invokes the hovered button's onClick when the click
    /// button is pressed. The laser + cursor turn green and the cursor grows when aimed at a
    /// clickable button, so it's obvious when you're on target. Respects Button.interactable, so
    /// gated tutorial steps still can't be skipped. Added automatically by TutorialManager /
    /// StartScreenSpawner.
    /// </summary>
    [DisallowMultipleComponent]
    public class VrUiPointer : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float m_maxLength = 6f;
        [SerializeField] private OVRInput.RawButton m_clickButton = OVRInput.RawButton.RIndexTrigger;
        [SerializeField, Range(0f, 0.6f)] private float m_hitPadding = 0.25f; // forgiving hit area
        [SerializeField] private Color m_idleColor = new Color(0.35f, 0.8f, 1f, 0.9f);
        [SerializeField] private Color m_hitColor = new Color(0.3f, 1f, 0.45f, 0.95f);

        // True while the laser rests on a clickable button this frame; locomotion reads this to avoid
        // teleporting when the index trigger (also the click button) is used to click a UI button.
        public bool IsHoveringClickable { get; private set; }

        private OVRCameraRig m_rig;
        private Transform m_ray;
        private LineRenderer m_line;
        private Transform m_cursor;
        private Renderer m_cursorRenderer;
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
                IsHoveringClickable = false;
                SetVisible(false);
                return;
            }

            // Refresh the button list periodically (cheap for these small UIs; avoids per-frame alloc).
            if (Time.unscaledTime >= m_nextScan)
            {
                m_buttons = FindObjectsByType<Button>(FindObjectsInactive.Exclude);
                m_nextScan = Time.unscaledTime + 0.3f;
            }

            var ray = new Ray(m_ray.position, m_ray.forward);
            Button hovered = null;
            var hitPoint = m_ray.position + m_ray.forward * m_maxLength;
            var bestDist = float.MaxValue;

            foreach (var button in m_buttons)
            {
                if (button == null || !button.isActiveAndEnabled || !(button.transform is RectTransform rect))
                {
                    continue;
                }

                if (!RayHitsRect(ray, rect, m_hitPadding, out var point))
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

            var clickable = hovered != null && hovered.interactable;
            IsHoveringClickable = clickable;
            var color = clickable ? m_hitColor : m_idleColor;

            SetVisible(true);
            m_line.startColor = color;
            m_line.endColor = color;
            m_line.SetPosition(0, m_ray.position);
            m_line.SetPosition(1, hitPoint);

            if (m_cursor != null)
            {
                m_cursor.gameObject.SetActive(hovered != null);
                m_cursor.position = hitPoint;
                m_cursor.localScale = Vector3.one * (clickable ? 0.05f : 0.028f);
                if (m_cursorRenderer != null)
                {
                    m_cursorRenderer.sharedMaterial.color = color;
                }
            }

            if (clickable && OVRInput.GetDown(m_clickButton))
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

        // True if the ray crosses the button's world-space rectangle (inflated by padding); outputs
        // the world hit point.
        private static bool RayHitsRect(Ray ray, RectTransform rect, float padding, out Vector3 point)
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
            var r = rect.rect;
            var padX = r.width * padding;
            var padY = r.height * padding;
            return local.x >= r.xMin - padX && local.x <= r.xMax + padX
                   && local.y >= r.yMin - padY && local.y <= r.yMax + padY;
        }

        private void BuildLaser()
        {
            var laserGo = new GameObject("UI Laser Pointer");
            laserGo.transform.SetParent(transform, false);

            m_line = laserGo.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.positionCount = 2;
            m_line.widthMultiplier = 0.008f;
            m_line.numCapVertices = 4;
            m_line.material = new Material(Shader.Find("Sprites/Default"));
            m_line.startColor = m_idleColor;
            m_line.endColor = m_idleColor;

            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "UI Cursor";
            dot.transform.SetParent(laserGo.transform, false);
            dot.transform.localScale = Vector3.one * 0.028f;
            var col = dot.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            m_cursorRenderer = dot.GetComponent<Renderer>();
            if (m_cursorRenderer != null)
            {
                m_cursorRenderer.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { color = m_idleColor };
            }

            m_cursor = dot.transform;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
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
