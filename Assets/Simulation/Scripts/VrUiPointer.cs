using System;
using UnityEngine;
using UnityEngine.UI;

// =============================================================================
// VrUiPointer.cs
//
// PURPOSE:  A controller laser pointer for world-space uGUI buttons. Casts a ray
//           from the right-hand controller, draws a laser beam + sphere cursor,
//           and invokes the hovered Button's onClick when the click button is
//           pressed. Deliberately bypasses Meta's OVRInputModule (that module uses
//           the legacy Input class, which throws in this new-Input-System-only
//           project). It is added automatically at runtime by TutorialManager /
//           StartScreenSpawner, so there is no persistent GameObject carrying it in
//           any scene file.
//
// HOW TO CUSTOMIZE:
//   IMPORTANT NUANCE FOR THIS FILE: because VrUiPointer is created in code (via
//   AddComponent) and is NOT saved on a GameObject in any .unity scene, the
//   [SerializeField] DEFAULTS BELOW *do* take effect at runtime — there is no
//   scene/Inspector override to fight with. Edit the defaults here to change
//   behavior. (This is the opposite of most SimJam components, whose serialized
//   values are pinned in the scene YAML and must be edited in the Unity Inspector.)
//   If you later drag this component onto a saved GameObject, the scene value wins
//   and you must edit it in the Inspector instead.
//
//   - m_maxLength (float, default 6): how far the laser reaches in metres before it
//     stops looking for buttons. Edit the [SerializeField] default in this file.
//   - m_clickButton (OVRInput.RawButton, default RIndexTrigger): which controller
//     button counts as a "click" on the hovered UI button. Edit the default here.
//     NOTE: the right index trigger also drives teleport locomotion, which is why
//     other scripts read IsHoveringClickable to suppress teleport while on a button.
//   - m_hitPadding (float 0..0.6, default 0.25): how much each button's rect is
//     inflated when testing for a hit — larger = easier/forgiving aim. Edit here.
//   - m_idleColor / m_hitColor (Color): laser + cursor tint when NOT / WHEN aimed at
//     an interactable button (cyan vs green). Edit the [SerializeField] defaults here.
//   - Cursor sizes are HARDCODED (not serialized): 0.05 when clickable, 0.028
//     otherwise. Change them in Update() (the m_cursor.localScale line) and in the
//     initial 0.028f value in BuildLaser().
//   - Laser thickness / material / cursor shader are HARDCODED in BuildLaser()
//     (widthMultiplier 0.008, "Sprites/Default" shader). Edit that method.
//   - Button-list rescan interval is HARDCODED at 0.3s in Update(); edit the
//     "m_nextScan = Time.unscaledTime + 0.3f" line to rescan more/less often.
// =============================================================================
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
        /// <summary>How far the laser ray reaches (metres) before it stops looking for buttons.</summary>
        [SerializeField, Min(0.5f)] private float m_maxLength = 6f;
        /// <summary>OVR controller button that counts as a "click" on the hovered UI button (right index trigger by default).</summary>
        [SerializeField] private OVRInput.RawButton m_clickButton = OVRInput.RawButton.RIndexTrigger;
        /// <summary>Fraction the button's rect is inflated by when testing for a hit, giving a more forgiving aim target.</summary>
        [SerializeField, Range(0f, 0.6f)] private float m_hitPadding = 0.25f; // forgiving hit area
        /// <summary>Laser/cursor tint when not aimed at a clickable button (cyan).</summary>
        [SerializeField] private Color m_idleColor = new Color(0.35f, 0.8f, 1f, 0.9f);
        /// <summary>Laser/cursor tint when aimed at an interactable button (green), signalling "on target".</summary>
        [SerializeField] private Color m_hitColor = new Color(0.3f, 1f, 0.45f, 0.95f);

        /// <summary>
        /// True while the laser rests on a clickable button this frame; locomotion reads this to avoid
        /// teleporting when the index trigger (also the click button) is used to click a UI button.
        /// </summary>
        // True while the laser rests on a clickable button this frame; locomotion reads this to avoid
        // teleporting when the index trigger (also the click button) is used to click a UI button.
        public bool IsHoveringClickable { get; private set; }

        /// <summary>Cached OVR camera rig, used to locate the right-hand controller anchor to cast from.</summary>
        private OVRCameraRig m_rig;
        /// <summary>Transform the laser originates from (right hand/controller anchor).</summary>
        private Transform m_ray;
        /// <summary>Runtime-built line that visually draws the laser beam.</summary>
        private LineRenderer m_line;
        /// <summary>Transform of the sphere cursor drawn at the laser's hit point.</summary>
        private Transform m_cursor;
        /// <summary>Renderer of the cursor sphere, recoloured to match the idle/hit state.</summary>
        private Renderer m_cursorRenderer;
        /// <summary>Cached set of scene buttons the laser can hit, refreshed periodically.</summary>
        private Button[] m_buttons = Array.Empty<Button>();
        /// <summary>Unscaled time at which the button list is next re-scanned.</summary>
        private float m_nextScan;

        /// <summary>Builds the laser + cursor visuals when the pointer first comes alive.</summary>
        private void Start()
        {
            BuildLaser();
        }

        /// <summary>
        /// Per-frame pointer loop: aims the ray from the controller, finds the nearest hit button,
        /// updates the laser/cursor colour + cursor size, exposes <see cref="IsHoveringClickable"/>,
        /// and invokes the hovered button's onClick when the click button is pressed.
        /// </summary>
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

        /// <summary>
        /// Lazily resolves the transform the laser fires from by locating the OVR rig and preferring
        /// its right-hand anchor (falling back to the right controller anchor). Caches on success.
        /// </summary>
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

        /// <summary>
        /// True if the ray crosses the button's world-space rectangle (inflated by padding); outputs
        /// the world hit point. Builds the rect's plane from its world corners, raycasts it, then
        /// range-checks the hit in the rect's local space.
        /// </summary>
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

        /// <summary>
        /// Constructs the laser visuals at runtime: a thin LineRenderer beam and a small sphere
        /// cursor (collider stripped), both using the shader-stripping-safe "Sprites/Default" shader.
        /// Starts hidden.
        /// </summary>
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

        /// <summary>Shows or hides the laser beam, and always hides the cursor when turning invisible.</summary>
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
