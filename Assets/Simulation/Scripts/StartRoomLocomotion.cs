using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// =============================================================================
// StartRoomLocomotion.cs
//
// PURPOSE:  Player movement for the pre-mission start room. Lets the player try
//           all three locomotion styles before choosing one: smooth walk (left
//           stick / WASD), snap turn (right stick / Q,E) and teleport (right
//           index trigger / T). Moves the OVR rig, clamps it inside the room
//           bounds, and draws a coloured teleport landing marker. Deliberately
//           duplicated from the tutorial/lab movement so the real game loop stays
//           untouched.
//
// HOW TO CUSTOMIZE:
//   This component is created purely at RUNTIME: StartScreenSpawner.Start() calls
//   gameObject.AddComponent<StartRoomLocomotion>().Configure(new Vector2(4f, 4f)).
//   Because it is NOT authored into the StartScene as a scene object, there is no
//   .unity scene YAML override for these fields — so the DEFAULT values in the
//   [SerializeField] lines below ARE the runtime values, and editing them HERE does
//   change behaviour. (There is no Inspector component to edit for this one.)
//
//   THE ONE EXCEPTION — room size: m_roomHalfExtents is overwritten immediately after
//   creation by the hardcoded Configure(new Vector2(4f, 4f)) call in
//   StartScreenSpawner.Start(), so editing the m_roomHalfExtents default below has NO
//   effect. To change room bounds, edit that Configure(...) argument in
//   StartScreenSpawner.cs.
//
//   Tunable [SerializeField] knobs (edit the default value in this file):
//     - m_smoothMoveSpeed (1.35): walk speed in m/s for stick/WASD.
//     - m_thumbstickDeadzone (0.18): stick magnitude before walk engages (drift guard).
//     - m_snapTurnDegrees (30): yaw applied per snap-turn flick.
//     - m_snapTurnCooldown (0.35): min seconds between snap turns.
//     - m_maxTeleportDistance (10): furthest accepted teleport target, in metres.
//     - m_teleportMarkerRadius (0.28): radius of the flat teleport marker cylinder.
//     - m_roomHalfExtents (4,4): X/Z half-size box; OVERRIDDEN at runtime by
//       Configure() — edit the Vector2 in StartScreenSpawner.Start() instead.
//     - m_edgeMargin (0.3): inset from the room edge so the player can't clip walls.
//     - m_defaultEyeHeight (1.6): eye height used ONLY in the editor camera-only
//       fallback when no OVR rig exists.
//
//   HARDCODED values (not serialized — edit the named method to change):
//     - Snap-turn / teleport activation thresholds: turn flick 0.72 in
//       UpdateMovement(); teleport min hit distance 0.25 and invalid-fallback throw
//       1.5 in UpdateTeleportTarget().
//     - Teleport marker thickness 0.012 (EnsureTeleportMarker / UpdateTeleportTarget).
//     - Valid/invalid marker colours (green / red) in EnsureTeleportMarker() via
//       CreateMarkerMaterial().
//     - Editor key bindings (WASD/Q/E/T) in ReadMoveAxis(), ReadTurnAxis(),
//       WasTeleportPressed().
// =============================================================================

namespace SimJam
{
    /// Self-contained start-room locomotion so the player can TRY both styles before choosing one.
    /// Smooth walk (left stick / WASD), snap turn (right stick / Q,E) and teleport (right index
    /// trigger / T) are ALL active here. Deliberately duplicates the tutorial's locomotion rather than
    /// refactoring the working tutorial/lab movement. Skips teleport while a UI laser rests on a
    /// clickable button (the index trigger is also the UI click button).
    /// <summary>
    /// Start-room player movement: lets the player sample all three locomotion styles (smooth walk,
    /// snap turn, teleport) before the mission begins. Attached in the StartScene, it moves/rotates the
    /// OVR rig, clamps the player inside the room bounds, and draws a coloured teleport landing marker.
    /// Kept separate from the tutorial/lab movement code on purpose so the working game loop is untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public class StartRoomLocomotion : MonoBehaviour
    {
        /// <summary>Smooth-walk speed in metres/second when pushing the left stick (or WASD in editor).</summary>
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        /// <summary>Minimum thumbstick magnitude before smooth walk engages, to ignore stick drift.</summary>
        [SerializeField, Min(0.05f)] private float m_thumbstickDeadzone = 0.18f;
        /// <summary>Rotation applied per snap-turn flick of the right stick (or Q/E in editor).</summary>
        [SerializeField] private float m_snapTurnDegrees = 30f;
        /// <summary>Minimum seconds between snap turns so one flick does not spin the player repeatedly.</summary>
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.35f;
        /// <summary>Furthest floor distance (metres) that will accept a teleport target.</summary>
        [SerializeField, Min(1f)] private float m_maxTeleportDistance = 10f;
        /// <summary>Radius of the flat cylinder used as the teleport landing marker.</summary>
        [SerializeField, Min(0.05f)] private float m_teleportMarkerRadius = 0.28f;
        /// <summary>Half-size of the room on X/Z; movement and teleport targets are clamped to this box.</summary>
        [SerializeField] private Vector2 m_roomHalfExtents = new Vector2(4f, 4f);
        /// <summary>Inset kept from the room edge so the player cannot stand in/through the walls.</summary>
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.3f;
        /// <summary>Fallback eye height used only when no OVR rig exists (editor camera-only mode).</summary>
        [SerializeField, Min(0.5f)] private float m_defaultEyeHeight = 1.6f;

        /// <summary>The located OVR camera rig; the object actually translated/rotated for locomotion.</summary>
        private OVRCameraRig m_rig;
        /// <summary>Transform that gets moved/turned (the rig transform, or Camera.main as a fallback).</summary>
        private Transform m_locomotionRoot;
        /// <summary>The player's head/eye transform, used for camera-relative movement and turn pivot.</summary>
        private Transform m_cameraTransform;
        /// <summary>Cached UI laser pointer; teleport is suppressed while it hovers a clickable button.</summary>
        private VrUiPointer m_uiPointer;
        /// <summary>Runtime-created flat cylinder that previews where a teleport would land.</summary>
        private GameObject m_teleportMarker;
        /// <summary>Renderer of the teleport marker, swapped between the valid/invalid materials.</summary>
        private Renderer m_teleportMarkerRenderer;
        /// <summary>Green translucent material shown when the aimed floor point is a legal teleport target.</summary>
        private Material m_validTeleportMaterial;
        /// <summary>Red translucent material shown when the aimed point is out of range or invalid.</summary>
        private Material m_invalidTeleportMaterial;
        /// <summary>The current legal floor landing point captured this frame.</summary>
        private Vector3 m_currentTeleportTarget;
        /// <summary>True when <see cref="m_currentTeleportTarget"/> holds a usable target this frame.</summary>
        private bool m_hasValidTeleportTarget;
        /// <summary>Earliest <see cref="Time.time"/> a further snap turn is allowed (cooldown gate).</summary>
        private float m_nextSnapTurnTime;

        /// <summary>
        /// Called by the start-room spawner to hand this controller the actual room size so movement and
        /// teleport targets clamp to the real bounds instead of the default extents.
        /// </summary>
        public void Configure(Vector2 roomHalfExtents)
        {
            m_roomHalfExtents = roomHalfExtents;
        }

        /// <summary>Per-frame driver: (re)finds the rig, then processes walk/turn and teleport input.</summary>
        private void Update()
        {
            ResolveRig();
            UpdateMovement();
            UpdateTeleport();
        }

        /// <summary>
        /// Lazily locates the OVR rig and caches its transform + centre-eye anchor; if no rig is present
        /// (e.g. plain editor scene) it falls back to driving Camera.main directly.
        /// </summary>
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

        /// <summary>
        /// Applies smooth camera-relative walking from the left stick/WASD and camera-pivoted snap turns
        /// from the right stick/Q,E (rate-limited by the cooldown).
        /// </summary>
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

        /// <summary>
        /// Returns the desired move direction from the left thumbstick, overridden by WASD in the editor
        /// when the keyboard input exceeds the stick magnitude.
        /// </summary>
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

        /// <summary>
        /// Returns the horizontal snap-turn axis from the right thumbstick, overridden by Q (left) / E
        /// (right) in the editor.
        /// </summary>
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

        /// <summary>
        /// Moves the locomotion root to a room-clamped world position; in the editor camera-only fallback
        /// it also forces the eye height so the view does not sink to the floor.
        /// </summary>
        private void MoveRigTo(Vector3 worldPosition)
        {
            var clamped = ClampToRoom(worldPosition);
            if (m_locomotionRoot == m_cameraTransform && m_rig == null)
            {
                clamped.y = m_defaultEyeHeight;
            }

            m_locomotionRoot.position = clamped;
        }

        /// <summary>Clamps a world position to the room's X/Z bounds (minus the edge margin).</summary>
        private Vector3 ClampToRoom(Vector3 position)
        {
            var xLimit = Mathf.Max(0.5f, m_roomHalfExtents.x - m_edgeMargin);
            var zLimit = Mathf.Max(0.5f, m_roomHalfExtents.y - m_edgeMargin);
            position.x = Mathf.Clamp(position.x, -xLimit, xLimit);
            position.z = Mathf.Clamp(position.z, -zLimit, zLimit);
            return position;
        }

        /// <summary>Rotates the rig by the given yaw degrees around the player's head position.</summary>
        private void SnapTurn(float degrees)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var pivot = m_cameraTransform != null ? m_cameraTransform.position : m_locomotionRoot.position;
            m_locomotionRoot.RotateAround(pivot, Vector3.up, degrees);
        }

        /// <summary>
        /// Updates the teleport marker each frame and jumps the rig to the target on the index-trigger
        /// press, but skips entirely while the UI laser is hovering a clickable button (shared trigger).
        /// </summary>
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

        /// <summary>True on the frame the right index trigger (or the T key in editor) is pressed.</summary>
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

        /// <summary>
        /// Raycasts the aim ray against the floor plane, stores a clamped landing point when the hit is in
        /// range, and positions/recolours the marker (green valid, red invalid short-throw fallback).
        /// </summary>
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

        /// <summary>
        /// Returns the aim ray for teleporting: from the right controller anchor if available, otherwise
        /// from the camera (or this transform) as a fallback.
        /// </summary>
        private Ray GetTeleportRay()
        {
            if (m_rig != null && m_rig.rightControllerAnchor != null)
            {
                return new Ray(m_rig.rightControllerAnchor.position, m_rig.rightControllerAnchor.forward);
            }

            var camera = m_cameraTransform != null ? m_cameraTransform : transform;
            return new Ray(camera.position, camera.forward);
        }

        /// <summary>
        /// Lazily builds the teleport marker (a thin collider-less cylinder) and its valid/invalid
        /// materials the first time a teleport target is evaluated.
        /// </summary>
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

        /// <summary>
        /// Creates a Standard-shader marker material of the given colour, switching it to alpha-blended
        /// transparent mode when the colour has partial alpha (so the ring reads as translucent).
        /// </summary>
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
