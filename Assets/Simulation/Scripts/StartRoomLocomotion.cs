using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam
{
    /// Self-contained start-room locomotion so the player can TRY both styles before choosing one.
    /// Smooth walk (left stick / WASD), snap turn (right stick / Q,E) and teleport (right index
    /// trigger / T) are ALL active here. Deliberately duplicates the tutorial's locomotion rather than
    /// refactoring the working tutorial/lab movement. Skips teleport while a UI laser rests on a
    /// clickable button (the index trigger is also the UI click button).
    [DisallowMultipleComponent]
    public class StartRoomLocomotion : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        [SerializeField, Min(0.05f)] private float m_thumbstickDeadzone = 0.18f;
        [SerializeField] private float m_snapTurnDegrees = 30f;
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.35f;
        [SerializeField, Min(1f)] private float m_maxTeleportDistance = 10f;
        [SerializeField, Min(0.05f)] private float m_teleportMarkerRadius = 0.28f;
        [SerializeField] private Vector2 m_roomHalfExtents = new Vector2(4f, 4f);
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.3f;
        [SerializeField, Min(0.5f)] private float m_defaultEyeHeight = 1.6f;

        private OVRCameraRig m_rig;
        private Transform m_locomotionRoot;
        private Transform m_cameraTransform;
        private VrUiPointer m_uiPointer;
        private GameObject m_teleportMarker;
        private Renderer m_teleportMarkerRenderer;
        private Material m_validTeleportMaterial;
        private Material m_invalidTeleportMaterial;
        private Vector3 m_currentTeleportTarget;
        private bool m_hasValidTeleportTarget;
        private float m_nextSnapTurnTime;

        public void Configure(Vector2 roomHalfExtents)
        {
            m_roomHalfExtents = roomHalfExtents;
        }

        private void Update()
        {
            ResolveRig();
            UpdateMovement();
            UpdateTeleport();
        }

        private void ResolveRig()
        {
            if (m_rig == null)
            {
                m_rig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (m_rig != null)
            {
                m_locomotionRoot = m_rig.transform;
                m_cameraTransform = m_rig.centerEyeAnchor;
            }
            else if (m_cameraTransform == null && Camera.main != null)
            {
                m_cameraTransform = Camera.main.transform;
                m_locomotionRoot = m_cameraTransform;
            }
        }

        private void UpdateMovement()
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var moveAxis = ReadMoveAxis();
            if (moveAxis.sqrMagnitude > m_thumbstickDeadzone * m_thumbstickDeadzone)
            {
                var forward = m_cameraTransform != null ? m_cameraTransform.forward : m_locomotionRoot.forward;
                forward.y = 0f;
                forward.Normalize();

                var right = m_cameraTransform != null ? m_cameraTransform.right : m_locomotionRoot.right;
                right.y = 0f;
                right.Normalize();

                var move = forward * moveAxis.y + right * moveAxis.x;
                if (move.sqrMagnitude > 1f)
                {
                    move.Normalize();
                }

                MoveRigTo(m_locomotionRoot.position + move * (m_smoothMoveSpeed * Time.deltaTime));
            }

            var turnAxis = ReadTurnAxis();
            if (Time.time >= m_nextSnapTurnTime && Mathf.Abs(turnAxis) > 0.72f)
            {
                SnapTurn(turnAxis > 0f ? m_snapTurnDegrees : -m_snapTurnDegrees);
                m_nextSnapTurnTime = Time.time + m_snapTurnCooldown;
            }
        }

        private Vector2 ReadMoveAxis()
        {
            var axis = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                var keyboardAxis = Vector2.zero;
                if (Keyboard.current.aKey.isPressed) keyboardAxis.x -= 1f;
                if (Keyboard.current.dKey.isPressed) keyboardAxis.x += 1f;
                if (Keyboard.current.sKey.isPressed) keyboardAxis.y -= 1f;
                if (Keyboard.current.wKey.isPressed) keyboardAxis.y += 1f;
                if (keyboardAxis.sqrMagnitude > axis.sqrMagnitude)
                {
                    axis = keyboardAxis.normalized;
                }
            }
#endif
            return axis;
        }

        private float ReadTurnAxis()
        {
            var axis = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick).x;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.qKey.isPressed) axis = -1f;
                if (Keyboard.current.eKey.isPressed) axis = 1f;
            }
#endif
            return axis;
        }

        private void MoveRigTo(Vector3 worldPosition)
        {
            var clamped = ClampToRoom(worldPosition);
            if (m_locomotionRoot == m_cameraTransform && m_rig == null)
            {
                clamped.y = m_defaultEyeHeight;
            }

            m_locomotionRoot.position = clamped;
        }

        private Vector3 ClampToRoom(Vector3 position)
        {
            var xLimit = Mathf.Max(0.5f, m_roomHalfExtents.x - m_edgeMargin);
            var zLimit = Mathf.Max(0.5f, m_roomHalfExtents.y - m_edgeMargin);
            position.x = Mathf.Clamp(position.x, -xLimit, xLimit);
            position.z = Mathf.Clamp(position.z, -zLimit, zLimit);
            return position;
        }

        private void SnapTurn(float degrees)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var pivot = m_cameraTransform != null ? m_cameraTransform.position : m_locomotionRoot.position;
            m_locomotionRoot.RotateAround(pivot, Vector3.up, degrees);
        }

        private void UpdateTeleport()
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            // Don't teleport when the index trigger is being used to click a UI button (the trigger is
            // both the teleport button and the UI click button).
            if (m_uiPointer == null)
            {
                m_uiPointer = FindAnyObjectByType<VrUiPointer>();
            }

            if (m_uiPointer != null && m_uiPointer.IsHoveringClickable)
            {
                return;
            }

            UpdateTeleportTarget();
            if (WasTeleportPressed() && m_hasValidTeleportTarget)
            {
                MoveRigTo(m_currentTeleportTarget);
            }
        }

        private static bool WasTeleportPressed()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
            {
                return true;
            }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        private void UpdateTeleportTarget()
        {
            EnsureTeleportMarker();

            var ray = GetTeleportRay();
            var floor = new Plane(Vector3.up, Vector3.zero);
            m_hasValidTeleportTarget = false;

            if (floor.Raycast(ray, out var hitDistance) && hitDistance > 0.25f && hitDistance < m_maxTeleportDistance)
            {
                var point = ClampToRoom(ray.GetPoint(hitDistance));
                point.y = 0f;
                m_currentTeleportTarget = point;
                m_hasValidTeleportTarget = true;
            }

            if (m_hasValidTeleportTarget)
            {
                m_teleportMarker.transform.position = m_currentTeleportTarget + Vector3.up * 0.012f;
                m_teleportMarkerRenderer.sharedMaterial = m_validTeleportMaterial;
            }
            else
            {
                var fallback = ray.origin + ray.direction.normalized * 1.5f;
                fallback.y = 0.012f;
                m_teleportMarker.transform.position = fallback;
                m_teleportMarkerRenderer.sharedMaterial = m_invalidTeleportMaterial;
            }
        }

        private Ray GetTeleportRay()
        {
            if (m_rig != null && m_rig.rightControllerAnchor != null)
            {
                return new Ray(m_rig.rightControllerAnchor.position, m_rig.rightControllerAnchor.forward);
            }

            var camera = m_cameraTransform != null ? m_cameraTransform : transform;
            return new Ray(camera.position, camera.forward);
        }

        private void EnsureTeleportMarker()
        {
            if (m_teleportMarker != null)
            {
                return;
            }

            m_validTeleportMaterial = CreateMarkerMaterial("Start Valid Teleport", new Color(0.1f, 0.9f, 0.35f, 0.75f));
            m_invalidTeleportMaterial = CreateMarkerMaterial("Start Invalid Teleport", new Color(0.9f, 0.1f, 0.1f, 0.75f));
            m_teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_teleportMarker.name = "Start Teleport Target";
            m_teleportMarker.transform.localScale = new Vector3(m_teleportMarkerRadius, 0.012f, m_teleportMarkerRadius);
            m_teleportMarkerRenderer = m_teleportMarker.GetComponent<Renderer>();

            var markerCollider = m_teleportMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                markerCollider.enabled = false;
            }
        }

        private static Material CreateMarkerMaterial(string materialName, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = materialName, color = color };
            if (color.a < 1f)
            {
                material.SetFloat("_Mode", 3f);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = 3000;
            }

            return material;
        }
    }
}
