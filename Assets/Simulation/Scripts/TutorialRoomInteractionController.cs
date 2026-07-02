using SimJam.BarrelSimulator;
using UnityEngine;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam.Tutorial
{
    /// <summary>
    /// Drives all hands-on interaction in the <c>TutorialRoom</c> scene: it stands up the OVR player
    /// rig, provides smooth-move/snap-turn/teleport locomotion, spawns a working identiFINDER detector
    /// the player can grab, spawns two teaching barrels (one hot, one inert) so the player rehearses the
    /// scan-and-submit loop, and unlocks the matching gated <see cref="TutorialManager"/> steps as the
    /// player teleports, reads the detector, and correctly submits the radioactive barrel. Also spawns
    /// the player's IK arms. Companion to <c>TutorialManager</c> (captions/gating) — this component is
    /// the "doing" half of the tutorial.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SimJam/Tutorial Room Interaction Controller")]
    public class TutorialRoomInteractionController : MonoBehaviour
    {
        [Header("Tutorial")]
        /// <summary>The step-gating manager whose gated steps this controller completes; auto-found on the same object if unset.</summary>
        [SerializeField] private TutorialManager m_tutorialManager;
        /// <summary>Index of the "teleport to the marker" step this controller completes when the player teleports.</summary>
        [SerializeField, Min(0)] private int m_teleportStepIndex = 4;
        /// <summary>Index of the smooth-locomotion "walk" step (skipped when teleport is the chosen movement mode).</summary>
        [SerializeField, Min(0)] private int m_movementStepIndex = 3;
        /// <summary>Index of the "grab the detector and read >= completion CPS" step this controller completes.</summary>
        [SerializeField, Min(0)] private int m_detectorStepIndex = 5;

        [Header("Player")]
        /// <summary>The Meta OVR camera rig used as the player; auto-found or created at runtime if unset.</summary>
        [SerializeField] private OVRCameraRig m_cameraRig;
        /// <summary>Transform moved/rotated by locomotion (the rig root, or the camera when no rig exists).</summary>
        [SerializeField] private Transform m_locomotionRoot;
        /// <summary>The head/eye transform used for forward direction, teleport rays, and label billboarding.</summary>
        [SerializeField] private Transform m_cameraTransform;
        /// <summary>When true, build a minimal OVRCameraRig at runtime if none is found in the scene.</summary>
        [SerializeField] private bool m_createOvrCameraRig = true;
        /// <summary>Eye height applied to a flat (non-rig) camera so the editor view sits at standing height.</summary>
        [SerializeField] private float m_defaultEyeHeight = 1.6f;
        /// <summary>Half-size of the play area (X,Z) used to clamp locomotion and teleport targets to the room.</summary>
        [SerializeField] private Vector2 m_roomHalfExtents = new Vector2(3.55f, 3.55f);
        /// <summary>Inset from the room edge kept clear so the player cannot walk/teleport into the walls.</summary>
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.28f;

        [Header("Movement")]
        /// <summary>Enables left-thumbstick (or WASD in-editor) continuous locomotion.</summary>
        [SerializeField] private bool m_enableSmoothMove = true;
        /// <summary>Continuous-move speed in metres per second.</summary>
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        /// <summary>Enables right-thumbstick (or Q/E in-editor) snap turning.</summary>
        [SerializeField] private bool m_enableSnapTurn = true;
        /// <summary>Degrees rotated per snap-turn flick.</summary>
        [SerializeField, Min(5f)] private float m_snapTurnDegrees = 30f;
        /// <summary>Minimum seconds between snap turns to prevent rapid repeated rotation.</summary>
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.35f;
        /// <summary>Thumbstick magnitude below which continuous-move input is ignored (deadzone).</summary>
        [SerializeField, Range(0.05f, 0.95f)] private float m_thumbstickDeadzone = 0.18f;

        [Header("Teleport")]
        /// <summary>Enables point-and-click teleport locomotion via the right index trigger.</summary>
        [SerializeField] private bool m_enableTeleport = true;
        /// <summary>Maximum ray distance (metres) at which a floor point counts as a valid teleport target.</summary>
        [SerializeField, Min(1f)] private float m_maxTeleportDistance = 10f;
        /// <summary>Radius of the on-floor teleport reticle disc.</summary>
        [SerializeField, Min(0.05f)] private float m_teleportMarkerRadius = 0.28f;

        [Header("Detector")]
        /// <summary>When true, spawn a working, grabbable identiFINDER detector in the tutorial room.</summary>
        [SerializeField] private bool m_spawnWorkingDetector = true;
        /// <summary>When true, enable the geiger click audio on the tutorial detector.</summary>
        [SerializeField] private bool m_enableDetectorAudio;
        /// <summary>World-space rest position of the detector on its podium before it is grabbed.</summary>
        [SerializeField] private Vector3 m_detectorHomePosition = new Vector3(-2.25f, 1.16f, -1.5f);
        /// <summary>World-space rest rotation (Euler) of the detector on its podium.</summary>
        [SerializeField] private Vector3 m_detectorHomeEuler = new Vector3(0f, 98f, 90f);
        /// <summary>Proximity radius within which a controller can grab the detector.</summary>
        [SerializeField, Min(0.05f)] private float m_detectorGrabRadius = 0.18f;
        /// <summary>Smoothed CPS the held detector must read to complete the detector step.</summary>
        [SerializeField, Min(0f)] private float m_detectorCompletionCps = 3f;
        // Use the lab's identiFINDER prefab (same model) when assigned; null falls back to the
        // procedural DetectorModelBuilder wand.
        /// <summary>Optional lab identiFINDER prefab; when null the detector is built procedurally.</summary>
        [SerializeField] private GameObject m_detectorPrefab;
        /// <summary>Target height (metres) the prefab detector model is uniformly scaled to.</summary>
        [SerializeField, Min(0.05f)] private float m_detectorTargetHeight = 0.25f;
        // Held pose mirrors the lab's so the tutorial grip matches (defaults equal GrabbableTool's).
        /// <summary>Local position of the detector relative to the hand while held (mirrors the lab grip).</summary>
        [SerializeField] private Vector3 m_detectorHeldLocalPosition = new Vector3(0f, 0.040f, 0.065f);
        /// <summary>Local rotation (Euler) of the detector relative to the hand while held.</summary>
        [SerializeField] private Vector3 m_detectorHeldLocalEuler = new Vector3(55f, 0f, 0f);

        [Header("Teaching Barrels")]
        // Two barrels (one inert, one radioactive) the player scans + submits to practice the real
        // find-and-submit loop. Assign the lab's 55-gal barrel model so they match the mission barrels.
        /// <summary>Optional 55-gal barrel model shared with the lab; when null teaching barrels are cylinders.</summary>
        [SerializeField] private GameObject m_teachingBarrelPrefab;
        // Kept far apart (opposite sides of the room) so the hot barrel's inverse-square field does
        // not bathe the inert one -- the player must walk up to each to see the contrast.
        /// <summary>Floor position of the radioactive teaching barrel the player must submit.</summary>
        [SerializeField] private Vector3 m_hotBarrelPosition = new Vector3(2.8f, 0f, 0.3f);     // "SUBMIT THIS"
        /// <summary>Floor position of the inert decoy barrel the player must not submit.</summary>
        [SerializeField] private Vector3 m_inertBarrelPosition = new Vector3(-2.8f, 0f, 0.3f);  // "DON'T SUBMIT"
        /// <summary>Emitted activity (CPS) of the hot teaching barrel's radiation source.</summary>
        [SerializeField, Min(100f)] private float m_hotBarrelActivityCps = 6000f;
        /// <summary>Index of the "submit the radioactive barrel" teaching step this controller completes.</summary>
        [SerializeField, Min(0)] private int m_teachingSubmitStepIndex = 6;
        /// <summary>Half-angle (degrees) of the forgiving aim cone used to test which teaching barrel is submitted.</summary>
        [SerializeField, Range(4f, 35f)] private float m_teachingGuessConeAngle = 34f; // very forgiving "general area" aim
        [Header("Feedback audio")]
        /// <summary>Chime played when the player correctly submits the hot barrel.</summary>
        [SerializeField] private AudioClip m_correctSubmitClip;
        /// <summary>Sting played when the player wrongly submits the inert barrel.</summary>
        [SerializeField] private AudioClip m_incorrectSubmitClip;

        [Header("Arms")]
        /// <summary>Mixamo arm model spawned once so the player sees IK-driven arms (mirrors the lab rig).</summary>
        [SerializeField] private GameObject m_customArmsPrefab;
        // Arm IK tunables (defaults match the lab rig). Adjust in the Inspector + re-Play to dial
        // orientation (body yaw), elbow naturalness (target reach + elbow pole), and the wrist.
        /// <summary>Offset from the head to the chest/shoulder anchor the arms hang from.</summary>
        [SerializeField] private Vector3 m_armsChestOffset = new Vector3(0f, -0.2f, -0.05f); // lower attach
        /// <summary>Extra body yaw (degrees) applied to the arm rig's facing direction.</summary>
        [SerializeField, Range(-180f, 180f)] private float m_armsBodyYawOffset;
        /// <summary>Total arm reach length used by the IK so the hand meets the controller without floating.</summary>
        [SerializeField, Min(0.2f)] private float m_armsTargetReach = 0.64f; // sized so the IK arm reaches the controller (grab no longer floats)
        /// <summary>Local elbow pole vector steering the natural bend of the IK elbow.</summary>
        [SerializeField] private Vector3 m_armsElbowPole = new Vector3(0.3f, -0.4f, -0.1f);
        /// <summary>When true, the IK hand matches the controller's rotation.</summary>
        [SerializeField] private bool m_armsMatchHandToController = true;
        // Gentle finger curl on grab. MixamoArmRig now bends each bone around its own palm-ward axis
        // so it no longer disorients; flip the sign if fingers curl backward (away from the palm).
        /// <summary>Degrees each finger bone curls toward the palm on grab.</summary>
        [SerializeField, Range(0f, 130f)] private float m_armsFingerCurlAngle = 30f;
        /// <summary>Sign flipping the finger-curl direction if fingers bend away from the palm.</summary>
        [SerializeField] private float m_armsFingerCurlSign = -1f; // curl direction (flipped)

        /// <summary>The on-floor teleport reticle disc primitive.</summary>
        private GameObject m_teleportMarker;
        /// <summary>Renderer of the teleport marker, swapped between valid/invalid materials.</summary>
        private Renderer m_teleportMarkerRenderer;
        /// <summary>Green glowing material shown when the current teleport target is valid.</summary>
        private Material m_validTeleportMaterial;
        /// <summary>Red glowing material shown when there is no valid teleport target.</summary>
        private Material m_invalidTeleportMaterial;
        /// <summary>World position the player will teleport to when the trigger is pressed.</summary>
        private Vector3 m_currentTeleportTarget;
        /// <summary>True when <see cref="m_currentTeleportTarget"/> is a valid in-room floor point.</summary>
        private bool m_hasValidTeleportTarget;
        /// <summary>Earliest time (<see cref="Time.time"/>) at which the next snap turn is allowed.</summary>
        private float m_nextSnapTurnTime;
        /// <summary>Grab behaviour on the spawned detector.</summary>
        private GrabbableTool m_detectorGrabTool;
        /// <summary>The spawned detector's radiation reading component.</summary>
        private RadiationDetector m_detector;
        /// <summary>Cached UI laser pointer, checked so teleport does not fire while clicking a UI button.</summary>
        private VrUiPointer m_uiPointer;
        /// <summary>The detector root transform, whose up axis defines the aim direction.</summary>
        private Transform m_detectorRootTf;
        /// <summary>The detector's sensor tip, used as the origin of the teaching aim cone.</summary>
        private Transform m_detectorSensorTip;
        /// <summary>The radioactive teaching barrel instance (the correct submit target).</summary>
        private GameObject m_hotBarrel;
        /// <summary>The inert decoy teaching barrel instance.</summary>
        private GameObject m_inertBarrel;
        /// <summary>Floating "SUBMIT THIS" label above the hot barrel.</summary>
        private Transform m_hotLabel;
        /// <summary>Floating "DON'T SUBMIT" label above the inert barrel.</summary>
        private Transform m_inertLabel;
        /// <summary>True once the player has correctly submitted the hot barrel (stops further submit checks).</summary>
        private bool m_teachingSolved;
        /// <summary>True once the detector has been grabbed at least once (a precondition of the detector step).</summary>
        private bool m_detectorWasGrabbed;
        /// <summary>The instantiated custom arms object.</summary>
        private GameObject m_customArmsInstance;
        /// <summary>The IK rig component driving the custom arms.</summary>
        private MixamoArmRig m_customArmRig;

        /// <summary>Applies the saved movement preference, links the tutorial manager, hides the unused movement slide, and resolves player references.</summary>
        private void Awake()
        {
            ApplyMovementPreference();

            if (m_tutorialManager == null)
            {
                m_tutorialManager = GetComponent<TutorialManager>();
            }

            // Show only the movement slide matching the chosen locomotion; hide the other, so the
            // tutorial has a single movement step (locomotion OR teleport, never both).
            if (m_tutorialManager != null)
            {
                m_tutorialManager.SetStepSkipped(m_teleportStepIndex, !m_enableTeleport);
                m_tutorialManager.SetStepSkipped(m_movementStepIndex, !m_enableSmoothMove);
            }

            ResolvePlayerReferences();
        }

        // Apply the movement style chosen on the start screen (persisted via PlayerPrefs). If no choice
        // was recorded (e.g. this scene launched directly in-editor), keep the serialized flags. Snap
        // turn is left unchanged in both modes.
        /// <summary>Overrides the smooth-move/teleport flags with the locomotion mode the player picked on the start screen.</summary>
        private void ApplyMovementPreference()
        {
            var mode = MovementPreference.Load();
            if (mode == MovementMode.Smooth)
            {
                m_enableSmoothMove = true;
                m_enableTeleport = false;
            }
            else if (mode == MovementMode.Teleport)
            {
                m_enableSmoothMove = false;
                m_enableTeleport = true;
            }
        }

        /// <summary>Resolves player references then spawns the detector, teaching barrels, and custom arms.</summary>
        private void Start()
        {
            ResolvePlayerReferences();

            if (m_spawnWorkingDetector)
            {
                SpawnWorkingDetector();
            }

            SpawnTeachingBarrels();
            EnsureCustomArms();
        }

        /// <summary>Per-frame pump: refreshes references and arms, runs locomotion/teleport, and checks the gated detector and submit steps.</summary>
        private void Update()
        {
            ResolvePlayerReferences();
            EnsureCustomArms();
            UpdateMovement();
            UpdateTeleport();
            // If the player chose locomotion (teleport disabled), the gated teleport step can never be
            // completed by teleporting -- auto-unlock it so a Smooth player isn't soft-locked at step 4.
            if (!m_enableTeleport && m_tutorialManager != null && m_tutorialManager.IsCurrentStep(m_teleportStepIndex))
            {
                m_tutorialManager.CompleteStepIfCurrent(m_teleportStepIndex);
            }

            UpdateDetectorCompletion();
            UpdateTeachingSubmit();
            BillboardTeachingLabels();
        }

        /// <summary>Finds or creates the OVR rig and caches the camera and locomotion-root transforms (falling back to Camera.main when there is no rig).</summary>
        private void ResolvePlayerReferences()
        {
            if (m_cameraRig == null)
            {
                m_cameraRig = FindAnyObjectByType<OVRCameraRig>();
            }

            if (m_cameraRig == null && m_createOvrCameraRig)
            {
                CreateOvrCameraRig();
            }

            if (m_cameraRig != null)
            {
                ConfigureRig(m_cameraRig);
                m_cameraTransform = m_cameraRig.centerEyeAnchor;
                m_locomotionRoot = m_cameraRig.transform;
                return;
            }

            if (m_cameraTransform == null)
            {
                if (Camera.main != null)
                {
                    m_cameraTransform = Camera.main.transform;
                }
            }

            if (m_locomotionRoot == null)
            {
                m_locomotionRoot = m_cameraTransform;
            }
        }

        /// <summary>Builds a floor-level OVRCameraRig (plus OVRManager and audio listener) at runtime, disabling the scene's default camera.</summary>
        private void CreateOvrCameraRig()
        {
            var defaultCamera = Camera.main;
            if (defaultCamera != null)
            {
                defaultCamera.gameObject.SetActive(false);
            }

            var rigObject = new GameObject("OVRCameraRig");
            rigObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var manager = FindAnyObjectByType<OVRManager>();
            if (manager == null)
            {
                manager = rigObject.AddComponent<OVRManager>();
            }

            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
            manager.isInsightPassthroughEnabled = false;

            m_cameraRig = rigObject.AddComponent<OVRCameraRig>();
            ConfigureRig(m_cameraRig);
            EnsureAudioListener();
        }

        /// <summary>Applies the sim's standard center-eye camera settings (skybox clear, clip planes, stereo, MainCamera tag) to the given rig.</summary>
        private static void ConfigureRig(OVRCameraRig rig)
        {
            if (rig == null)
            {
                return;
            }

            rig.EnsureGameObjectIntegrity();
            rig.usePerEyeCameras = false;
            rig.disableEyeAnchorCameras = false;

            var centerCamera = rig.centerEyeAnchor != null ? rig.centerEyeAnchor.GetComponent<Camera>() : null;
            if (centerCamera == null)
            {
                return;
            }

            centerCamera.clearFlags = CameraClearFlags.Skybox;
            centerCamera.nearClipPlane = 0.05f;
            centerCamera.farClipPlane = 100f;
            centerCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            centerCamera.allowHDR = false;
            centerCamera.gameObject.tag = "MainCamera";
        }

        /// <summary>Adds an AudioListener to the rig's center eye if one is missing, so submit/geiger audio is audible.</summary>
        private void EnsureAudioListener()
        {
            if (m_cameraRig != null && m_cameraRig.centerEyeAnchor != null
                && m_cameraRig.centerEyeAnchor.GetComponent<AudioListener>() == null)
            {
                m_cameraRig.centerEyeAnchor.gameObject.AddComponent<AudioListener>();
            }
        }

        /// <summary>Applies head-relative continuous locomotion and cooldown-gated snap turning from the thumbsticks (or editor keys).</summary>
        private void UpdateMovement()
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            if (m_enableSmoothMove)
            {
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
            }

            if (m_enableSnapTurn)
            {
                var turnAxis = ReadTurnAxis();
                if (Time.time >= m_nextSnapTurnTime && Mathf.Abs(turnAxis) > 0.72f)
                {
                    SnapTurn(turnAxis > 0f ? m_snapTurnDegrees : -m_snapTurnDegrees);
                    m_nextSnapTurnTime = Time.time + m_snapTurnCooldown;
                }
            }
        }

        /// <summary>Reads the left thumbstick move vector, overridden by WASD keyboard input when running in-editor.</summary>
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

        /// <summary>Reads the right thumbstick horizontal turn value, overridden by Q/E keyboard input when running in-editor.</summary>
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

        /// <summary>Moves the locomotion root to a room-clamped position, pinning eye height when driving a flat (non-rig) camera.</summary>
        private void MoveRigTo(Vector3 worldPosition)
        {
            var clamped = ClampToRoom(worldPosition);
            if (m_locomotionRoot == m_cameraTransform && m_cameraRig == null)
            {
                clamped.y = m_defaultEyeHeight;
            }

            m_locomotionRoot.position = clamped;
        }

        /// <summary>Clamps a world position's X/Z inside the room's half-extents minus the edge margin.</summary>
        private Vector3 ClampToRoom(Vector3 position)
        {
            var xLimit = Mathf.Max(0.5f, m_roomHalfExtents.x - m_edgeMargin);
            var zLimit = Mathf.Max(0.5f, m_roomHalfExtents.y - m_edgeMargin);
            position.x = Mathf.Clamp(position.x, -xLimit, xLimit);
            position.z = Mathf.Clamp(position.z, -zLimit, zLimit);
            return position;
        }

        /// <summary>Rotates the locomotion root about the vertical axis through the head so the view stays centred during a snap turn.</summary>
        private void SnapTurn(float degrees)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var pivot = m_cameraTransform != null ? m_cameraTransform.position : m_locomotionRoot.position;
            m_locomotionRoot.RotateAround(pivot, Vector3.up, degrees);
        }

        /// <summary>Updates the teleport reticle and, on trigger press over a valid floor point, moves the player there, spawns an arrival burst, and completes the teleport step. Suppressed while aiming at a clickable UI element.</summary>
        private void UpdateTeleport()
        {
            if (!m_enableTeleport || m_locomotionRoot == null)
            {
                return;
            }

            // Don't teleport when the index trigger is being used to click a UI button (the trigger is
            // both the teleport button and the slide-advance click button).
            if (m_uiPointer == null)
            {
                m_uiPointer = FindAnyObjectByType<VrUiPointer>();
            }

            if (m_uiPointer != null && m_uiPointer.IsHoveringClickable)
            {
                if (m_teleportMarker != null)
                {
                    m_teleportMarker.SetActive(false);
                }

                return;
            }

            UpdateTeleportTarget();
            if (WasTeleportPressed() && m_hasValidTeleportTarget)
            {
                MoveRigTo(m_currentTeleportTarget);
                TeleportEffects.SpawnArrivalBurst(m_currentTeleportTarget, new Color(0.2f, 1f, 0.4f));
                m_tutorialManager?.CompleteStepIfCurrent(m_teleportStepIndex);
            }
        }

        /// <summary>Returns true on the frame the right index trigger (or the editor T key) is pressed to confirm a teleport.</summary>
        private bool WasTeleportPressed()
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

        /// <summary>Raycasts the controller aim against the floor plane, computes a room-clamped target, and positions/recolours the reticle to show validity.</summary>
        private void UpdateTeleportTarget()
        {
            EnsureTeleportMarker();
            m_teleportMarker.SetActive(true);

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

        /// <summary>Returns the teleport aim ray from the right controller anchor, falling back to the camera when no rig exists.</summary>
        private Ray GetTeleportRay()
        {
            if (m_cameraRig != null && m_cameraRig.rightControllerAnchor != null)
            {
                return new Ray(m_cameraRig.rightControllerAnchor.position, m_cameraRig.rightControllerAnchor.forward);
            }

            var camera = m_cameraTransform != null ? m_cameraTransform : transform;
            return new Ray(camera.position, camera.forward);
        }

        /// <summary>Lazily builds the flat cylinder teleport reticle and its valid/invalid glow materials, disabling its collider.</summary>
        private void EnsureTeleportMarker()
        {
            if (m_teleportMarker != null)
            {
                return;
            }

            m_validTeleportMaterial = CreateMaterial("Tutorial Valid Teleport", new Color(0.1f, 0.9f, 0.35f, 0.75f));
            m_invalidTeleportMaterial = CreateMaterial("Tutorial Invalid Teleport", new Color(0.9f, 0.1f, 0.1f, 0.75f));
            m_teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_teleportMarker.name = "Tutorial Teleport Target";
            m_teleportMarker.transform.localScale = new Vector3(m_teleportMarkerRadius, 0.012f, m_teleportMarkerRadius);
            m_teleportMarkerRenderer = m_teleportMarker.GetComponent<Renderer>();

            var collider = m_teleportMarker.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        // Spawns the Mixamo arms once (like the lab's EnsureCustomArms) so the tutorial shows the
        // player's arms. Uses MixamoArmRig's built-in default tunables.
        /// <summary>Instantiates the custom arms once, gives their skinned meshes a plain skin material, and configures/initializes the <see cref="MixamoArmRig"/> IK from the serialized tunables.</summary>
        private void EnsureCustomArms()
        {
            if (m_customArmsPrefab == null || m_customArmsInstance != null || m_cameraRig == null)
            {
                return;
            }

            m_customArmsInstance = Instantiate(m_customArmsPrefab, transform);
            m_customArmsInstance.name = "Tutorial Custom Arms";

            // The Mixamo FBX imports without textures; give every skinned mesh a solid skin material (a
            // plain Standard material never renders magenta on the Built-in pipeline) and stop frustum
            // culling once IK moves the bones outside the baked bounds.
            var skin = new Material(Shader.Find("Standard")) { name = "Tutorial Arm Skin", color = new Color(0.80f, 0.66f, 0.55f) };
            skin.SetFloat("_Glossiness", 0.25f);
            foreach (var smr in m_customArmsInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.updateWhenOffscreen = true;
                var existing = smr.sharedMaterials;
                if (existing.Length == 0 || existing[0] == null || existing[0].mainTexture == null)
                {
                    var mats = new Material[Mathf.Max(1, existing.Length)];
                    for (var i = 0; i < mats.Length; i++)
                    {
                        mats[i] = skin;
                    }

                    smr.sharedMaterials = mats;
                }
            }

            m_customArmRig = m_customArmsInstance.AddComponent<MixamoArmRig>();
            m_customArmRig.ChestOffsetFromHead = m_armsChestOffset;
            m_customArmRig.BodyYawOffsetDegrees = m_armsBodyYawOffset;
            m_customArmRig.TargetArmReach = m_armsTargetReach;
            m_customArmRig.ElbowPoleLocal = m_armsElbowPole;
            m_customArmRig.MatchHandToController = m_armsMatchHandToController;
            m_customArmRig.FingerCurlAngle = m_armsFingerCurlAngle;
            m_customArmRig.FingerCurlSign = m_armsFingerCurlSign;
            m_customArmRig.Initialize(m_cameraRig, m_customArmsInstance);
        }

        /// <summary>Builds a grabbable, working identiFINDER (from prefab or procedurally), rests it kinematically on its podium, and wires up its grab behaviour, geiger audio, radiation reading, and screen binder plus the arm-recalibration hooks.</summary>
        private void SpawnWorkingDetector()
        {
            if (m_detectorGrabTool != null)
            {
                return;
            }

            GameObject detectorRoot;
            Transform sensorTip;
            TextMesh legacyScreen;
            GameObject screenSource;
            if (m_detectorPrefab != null)
            {
                // Same identiFINDER as the lab (prefab model + world-space TMP screen).
                var built = DetectorModelBuilder.BuildFromPrefab(m_detectorPrefab, m_detectorTargetHeight);
                detectorRoot = built.Root;
                sensorTip = built.SensorTip;
                screenSource = built.ScreenSource;
                legacyScreen = null;
            }
            else
            {
                var parts = DetectorModelBuilder.Build();
                detectorRoot = parts.Root;
                sensorTip = parts.SensorTip;
                legacyScreen = parts.ScreenText;
                screenSource = null;
            }

            detectorRoot.name = "Tutorial Working IdentiFINDER";
            detectorRoot.transform.SetPositionAndRotation(m_detectorHomePosition, Quaternion.Euler(m_detectorHomeEuler));
            m_detectorRootTf = detectorRoot.transform;
            m_detectorSensorTip = sensorTip;

            // Rest on the podium without falling or rolling off: kinematic at spawn. GrabbableTool makes
            // it dynamic again on release after the first grab.
            var detectorBody = detectorRoot.GetComponent<Rigidbody>();
            if (detectorBody != null)
            {
                detectorBody.isKinematic = true;
            }

            m_detectorGrabTool = detectorRoot.AddComponent<GrabbableTool>();
            m_detectorGrabTool.Initialize(m_cameraRig, m_detectorGrabRadius);
            m_detectorGrabTool.HeldLocalPosition = m_detectorHeldLocalPosition;
            m_detectorGrabTool.HeldLocalEuler = m_detectorHeldLocalEuler;
            m_detectorGrabTool.FlipHeldAboutAim = true; // screen faces the player when held
            m_detectorGrabTool.Grabbed += MarkDetectorGrabbed;
            // Re-calibrate the arm rig's wrist twist on grab/release so the hand doesn't twist.
            m_detectorGrabTool.Grabbed += RecalibrateArms;
            m_detectorGrabTool.Released += RecalibrateArms;

            var audio = detectorRoot.AddComponent<GeigerAudio>();
            audio.enabled = m_enableDetectorAudio;
            m_detector = detectorRoot.AddComponent<RadiationDetector>();
            m_detector.Initialize(m_cameraRig, legacyScreen, sensorTip, audio, m_detectorGrabTool, m_detectorHomePosition, Quaternion.Euler(m_detectorHomeEuler));

            // Drive the prefab's TMP number + slider from the live reading.
            if (screenSource != null)
            {
                detectorRoot.AddComponent<DetectorScreenBinder>().Bind(m_detector, screenSource);
            }
        }

        /// <summary>Spawns the hot ("SUBMIT THIS") and inert ("DON'T SUBMIT") teaching barrels once, giving the hot one a radiation source and each a floating label.</summary>
        private void SpawnTeachingBarrels()
        {
            if (m_hotBarrel != null || m_inertBarrel != null)
            {
                return;
            }

            // Named "Drum" (not "Barrel") so the dresser's legacy-barrel hide pass never disables them.
            m_hotBarrel = CreateTeachingBarrel("Tutorial Hot Drum (SUBMIT)", m_hotBarrelPosition);
            m_hotBarrel.AddComponent<RadiationSource>().Configure(m_hotBarrelActivityCps, "Cs-137");
            m_hotLabel = CreateBarrelLabel(m_hotBarrel, "SUBMIT THIS", new Color(0.30f, 1f, 0.45f));

            m_inertBarrel = CreateTeachingBarrel("Tutorial Inert Drum", m_inertBarrelPosition);
            m_inertLabel = CreateBarrelLabel(m_inertBarrel, "DON'T SUBMIT", new Color(1f, 0.42f, 0.36f));
        }

        // One barrel matching the lab's 55-gal drum (same model + same approximate collider), on the floor.
        /// <summary>Creates a single teaching barrel from the prefab (or a cylinder), fits it to drum size, gives it a collider, and seats its visible base on the floor.</summary>
        private GameObject CreateTeachingBarrel(string objectName, Vector3 floorPosition)
        {
            GameObject barrel = m_teachingBarrelPrefab != null
                ? Instantiate(m_teachingBarrelPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = objectName;
            barrel.transform.SetParent(transform, true);
            barrel.transform.SetPositionAndRotation(floorPosition, Quaternion.identity);
            FitTeachingBarrel(barrel);
            EnsureLabBarrelCollider(barrel);
            // Rest the VISIBLE mesh bottom on the floor: the barrel FBX pivot is not at its base, so a
            // plain y=0 placement sinks the drum into the ground.
            if (TryGetBarrelBounds(barrel, out var bounds))
            {
                barrel.transform.position += new Vector3(0f, floorPosition.y - bounds.min.y, 0f);
            }

            return barrel;
        }

        // Uniform-fit the model to ~0.9 m tall (the 55-gal height) so it matches the lab barrels.
        /// <summary>Uniformly scales a barrel so its rendered height is ~0.9 m, matching the lab's 55-gal drums.</summary>
        private static void FitTeachingBarrel(GameObject barrel)
        {
            const float targetHeight = 0.9f;
            if (TryGetBarrelBounds(barrel, out var bounds) && bounds.size.y > 1e-4f)
            {
                barrel.transform.localScale *= targetHeight / bounds.size.y;
            }
            else
            {
                barrel.transform.localScale = new Vector3(0.58f, targetHeight * 0.5f, 0.58f);
            }
        }

        // The same collider the lab gives its barrels (EnsureApproximateCollider, 55-gal spec): a
        // BoxCollider sized 0.58 x 0.9 x 0.58 in world space, centered on the mesh. Skipped if the
        // prefab already ships a collider (then the teaching barrel uses the prefab's, like the lab).
        /// <summary>Adds a 55-gal-sized BoxCollider centred on the barrel mesh, unless the prefab already provides a collider.</summary>
        private static void EnsureLabBarrelCollider(GameObject barrel)
        {
            if (barrel.GetComponentInChildren<Collider>() != null)
            {
                return;
            }

            const float diameter = 0.58f;
            const float height = 0.9f;
            var box = barrel.AddComponent<BoxCollider>();
            var s = barrel.transform.lossyScale;
            s.x = Mathf.Approximately(s.x, 0f) ? 1f : s.x;
            s.y = Mathf.Approximately(s.y, 0f) ? 1f : s.y;
            s.z = Mathf.Approximately(s.z, 0f) ? 1f : s.z;
            box.size = new Vector3(diameter / s.x, height / s.y, diameter / s.z);
            if (TryGetBarrelBounds(barrel, out var bounds))
            {
                box.center = barrel.transform.InverseTransformPoint(bounds.center);
            }
        }

        /// <summary>Computes the combined world-space renderer bounds of a barrel; returns false if it has no renderers.</summary>
        private static bool TryGetBarrelBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var has = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return has;
        }

        // Floating text above a barrel (parented to the room so it stays unscaled + readable).
        /// <summary>Creates a coloured floating <see cref="TextMesh"/> label anchored above a barrel's visible mesh.</summary>
        private Transform CreateBarrelLabel(GameObject barrel, string text, Color color)
        {
            var labelObject = new GameObject(barrel.name + " Label");
            labelObject.transform.SetParent(transform, false);
            // Anchor above the VISIBLE mesh, not the transform pivot (the barrel FBX pivot is offset
            // horizontally, so the drum renders to the side of its transform position).
            var anchor = barrel.transform.position + new Vector3(0f, 1.25f, 0f);
            if (TryGetBarrelBounds(barrel, out var labelBounds))
            {
                anchor = labelBounds.center + Vector3.up * (labelBounds.extents.y + 0.35f);
            }

            labelObject.transform.position = anchor;

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var label = labelObject.AddComponent<TextMesh>();
            label.font = font;
            labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            label.text = text;
            label.color = color;
            label.fontSize = 64;
            label.characterSize = 0.012f;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            return labelObject.transform;
        }

        /// <summary>Rotates both barrel labels each frame so they always face the player.</summary>
        private void BillboardTeachingLabels()
        {
            FaceCamera(m_hotLabel);
            FaceCamera(m_inertLabel);
        }

        /// <summary>Orients a single label to look at the camera (no-op if the label or camera is missing).</summary>
        private void FaceCamera(Transform label)
        {
            if (label == null || m_cameraTransform == null)
            {
                return;
            }

            var toLabel = label.position - m_cameraTransform.position;
            if (toLabel.sqrMagnitude > 1e-6f)
            {
                label.rotation = Quaternion.LookRotation(toLabel);
            }
        }

        // Submit (A button / pinch, the SAME control as the lab) aimed at the HOT barrel completes the
        // teaching step. Only active while that step is current and until solved once.
        /// <summary>While the submit step is current, checks for a submit press and completes the step (with a win chime + "SUBMITTED" label) when aimed at the hot barrel, or plays a loss sting for the inert one.</summary>
        private void UpdateTeachingSubmit()
        {
            if (m_teachingSolved || m_tutorialManager == null
                || !m_tutorialManager.IsCurrentStep(m_teachingSubmitStepIndex))
            {
                return;
            }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            // Editor flat-mode fallback: N completes the submit step without a held/aimed detector.
            if (Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame)
            {
                m_teachingSolved = true;
                m_tutorialManager.CompleteStepIfCurrent(m_teachingSubmitStepIndex);
                return;
            }
#endif

            if (!IsSubmitPressed())
            {
                return;
            }

            var aimed = GetTeachingAimedBarrel();
            if (aimed != null && aimed == m_hotBarrel)
            {
                m_teachingSolved = true;
                m_tutorialManager.CompleteStepIfCurrent(m_teachingSubmitStepIndex);
                PlaySubmitFeedback(true);
                if (m_hotLabel != null)
                {
                    var tm = m_hotLabel.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        tm.text = "SUBMITTED";
                    }
                }
            }
            else if (aimed == m_inertBarrel)
            {
                PlaySubmitFeedback(false);
            }
        }

        // Submit = A / index pinch; also X when the detector is held in the LEFT hand.
        /// <summary>Returns true on the frame the submit control is pressed (A/pinch, or X when the detector is held in the left hand).</summary>
        private bool IsSubmitPressed()
        {
            return InputManager.IsButtonADownOrPinchStarted()
                || (m_detectorGrabTool != null
                    && m_detectorGrabTool.HeldController == OVRInput.Controller.LTouch
                    && OVRInput.GetDown(OVRInput.RawButton.X));
        }

        /// <summary>Lazily created 2D audio source used to play the submit win/loss feedback clips.</summary>
        private AudioSource m_submitAudioSource;

        // Win chime on the hot barrel, loss sting on the inert one (2D so it's heard anywhere).
        /// <summary>Plays the correct/incorrect submit clip through a lazily-created 2D audio source.</summary>
        private void PlaySubmitFeedback(bool correct)
        {
            var clip = correct ? m_correctSubmitClip : m_incorrectSubmitClip;
            if (clip == null)
            {
                return;
            }

            if (m_submitAudioSource == null)
            {
                m_submitAudioSource = gameObject.AddComponent<AudioSource>();
                m_submitAudioSource.playOnAwake = false;
                m_submitAudioSource.spatialBlend = 0f;
            }

            m_submitAudioSource.PlayOneShot(clip);
        }

        // Lightweight aim test scoped to the two teaching barrels (cone from the detector sensor tip
        // along the detector's aim axis, with a dead-on raycast fallback). Mirrors the lab's GetAimedBarrel.
        /// <summary>Returns whichever of the two teaching barrels the held detector is aimed at, using a forgiving cone from the sensor tip (with a raycast fallback), or null if none.</summary>
        private GameObject GetTeachingAimedBarrel()
        {
            if (m_detectorGrabTool == null || !m_detectorGrabTool.IsHeld
                || m_detectorSensorTip == null || m_detectorRootTf == null)
            {
                return null;
            }

            var origin = m_detectorSensorTip.position;
            var direction = m_detectorRootTf.up;
            const float maxDistance = 12f; // match the lab's guess ray length so distance scanning works too

            GameObject best = null;
            var bestAngle = Mathf.Max(2f, m_teachingGuessConeAngle);
            foreach (var barrel in new[] { m_hotBarrel, m_inertBarrel })
            {
                if (barrel == null)
                {
                    continue;
                }

                // Aim at the VISIBLE barrel's vertical center (renderer bounds), not the collider's:
                // the prefab collider can sit low, which drops the scan target under the floor. The
                // renderer bounds center sits at the barrel's mid-height, so the cone covers the drum.
                var center = TryGetBarrelBounds(barrel, out var barrelBounds)
                    ? barrelBounds.center
                    : barrel.transform.position;
                var toBarrel = center - origin;
                var distance = toBarrel.magnitude;
                if (distance < 1e-3f || distance > maxDistance)
                {
                    continue;
                }

                var angle = Vector3.Angle(direction, toBarrel);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = barrel;
                }
            }

            if (best == null && Physics.Raycast(origin, direction, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                var t = hit.collider.transform;
                if (m_hotBarrel != null && t.IsChildOf(m_hotBarrel.transform))
                {
                    best = m_hotBarrel;
                }
                else if (m_inertBarrel != null && t.IsChildOf(m_inertBarrel.transform))
                {
                    best = m_inertBarrel;
                }
            }

            return best;
        }

        /// <summary>Grab callback that records the detector has been picked up (a precondition of the detector step).</summary>
        private void MarkDetectorGrabbed()
        {
            m_detectorWasGrabbed = true;
        }

        /// <summary>Grab/release callback that re-syncs the arm rig's wrist twist so the held hand does not twist.</summary>
        private void RecalibrateArms()
        {
            if (m_customArmRig != null && m_customArmRig.IsReady)
            {
                m_customArmRig.RecalibrateHands();
            }
        }

        /// <summary>Completes the detector step if it is the current tutorial step.</summary>
        private void CompleteDetectorStep()
        {
            m_tutorialManager?.CompleteStepIfCurrent(m_detectorStepIndex);
        }

        /// <summary>Completes the detector step once the grabbed detector reads at or above the required smoothed CPS.</summary>
        private void UpdateDetectorCompletion()
        {
            if (m_detectorWasGrabbed && m_detector != null && m_detector.SmoothedCps >= m_detectorCompletionCps)
            {
                CompleteDetectorStep();
            }
        }

        /// <summary>Creates a Standard-shader material with the given colour and an emissive glow so it reads clearly in the headset.</summary>
        private static Material CreateMaterial(string materialName, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = materialName, color = color };
            material.SetFloat("_Glossiness", 0.35f);
            // Glow so the teleport reticle reads clearly in the headset.
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(color.r, color.g, color.b) * 2.2f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return material;
        }

        /// <summary>Unsubscribes the grab/release event handlers from the detector to avoid dangling references.</summary>
        private void OnDestroy()
        {
            if (m_detectorGrabTool != null)
            {
                m_detectorGrabTool.Grabbed -= MarkDetectorGrabbed;
                m_detectorGrabTool.Grabbed -= RecalibrateArms;
                m_detectorGrabTool.Released -= RecalibrateArms;
            }
        }
    }
}
