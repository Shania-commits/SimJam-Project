using SimJam.BarrelSimulator;
using UnityEngine;

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam.Tutorial
{
    [DisallowMultipleComponent]
    [AddComponentMenu("SimJam/Tutorial Room Interaction Controller")]
    public class TutorialRoomInteractionController : MonoBehaviour
    {
        [Header("Tutorial")]
        [SerializeField] private TutorialManager m_tutorialManager;
        [SerializeField, Min(0)] private int m_teleportStepIndex = 4;
        [SerializeField, Min(0)] private int m_detectorStepIndex = 5;

        [Header("Player")]
        [SerializeField] private OVRCameraRig m_cameraRig;
        [SerializeField] private Transform m_locomotionRoot;
        [SerializeField] private Transform m_cameraTransform;
        [SerializeField] private bool m_createOvrCameraRig = true;
        [SerializeField] private float m_defaultEyeHeight = 1.6f;
        [SerializeField] private Vector2 m_roomHalfExtents = new Vector2(3.55f, 3.55f);
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.28f;

        [Header("Movement")]
        [SerializeField] private bool m_enableSmoothMove = true;
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        [SerializeField] private bool m_enableSnapTurn = true;
        [SerializeField, Min(5f)] private float m_snapTurnDegrees = 30f;
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.35f;
        [SerializeField, Range(0.05f, 0.95f)] private float m_thumbstickDeadzone = 0.18f;

        [Header("Teleport")]
        [SerializeField] private bool m_enableTeleport = true;
        [SerializeField, Min(1f)] private float m_maxTeleportDistance = 10f;
        [SerializeField, Min(0.05f)] private float m_teleportMarkerRadius = 0.28f;

        [Header("Detector")]
        [SerializeField] private bool m_spawnWorkingDetector = true;
        [SerializeField] private bool m_enableDetectorAudio;
        [SerializeField] private Vector3 m_detectorHomePosition = new Vector3(-2.25f, 1.16f, -1.5f);
        [SerializeField] private Vector3 m_detectorHomeEuler = new Vector3(0f, 98f, 90f);
        [SerializeField, Min(0.05f)] private float m_detectorGrabRadius = 0.18f;
        [SerializeField, Min(0f)] private float m_detectorCompletionCps = 3f;
        // Use the lab's identiFINDER prefab (same model) when assigned; null falls back to the
        // procedural DetectorModelBuilder wand.
        [SerializeField] private GameObject m_detectorPrefab;
        [SerializeField, Min(0.05f)] private float m_detectorTargetHeight = 0.25f;
        // Held pose mirrors the lab's so the tutorial grip matches (defaults equal GrabbableTool's).
        [SerializeField] private Vector3 m_detectorHeldLocalPosition = new Vector3(0f, 0.040f, 0.065f);
        [SerializeField] private Vector3 m_detectorHeldLocalEuler = new Vector3(55f, 0f, 0f);

        [Header("Teaching Barrels")]
        // Two barrels (one inert, one radioactive) the player scans + submits to practice the real
        // find-and-submit loop. Assign the lab's 55-gal barrel model so they match the mission barrels.
        [SerializeField] private GameObject m_teachingBarrelPrefab;
        // Kept far apart (opposite sides of the room) so the hot barrel's inverse-square field does
        // not bathe the inert one -- the player must walk up to each to see the contrast.
        [SerializeField] private Vector3 m_hotBarrelPosition = new Vector3(2.8f, 0f, 0.3f);     // "SUBMIT THIS"
        [SerializeField] private Vector3 m_inertBarrelPosition = new Vector3(-2.8f, 0f, 0.3f);  // "DON'T SUBMIT"
        [SerializeField, Min(100f)] private float m_hotBarrelActivityCps = 6000f;
        [SerializeField, Min(0)] private int m_teachingSubmitStepIndex = 6;
        [SerializeField, Range(4f, 35f)] private float m_teachingGuessConeAngle = 16f;

        [Header("Arms")]
        [SerializeField] private GameObject m_customArmsPrefab;
        // Arm IK tunables (defaults match the lab rig). Adjust in the Inspector + re-Play to dial
        // orientation (body yaw), elbow naturalness (target reach + elbow pole), and the wrist.
        [SerializeField] private Vector3 m_armsChestOffset = new Vector3(0f, -0.2f, -0.05f); // lower attach
        [SerializeField, Range(-180f, 180f)] private float m_armsBodyYawOffset;
        [SerializeField, Min(0.2f)] private float m_armsTargetReach = 0.64f; // sized so the IK arm reaches the controller (grab no longer floats)
        [SerializeField] private Vector3 m_armsElbowPole = new Vector3(0.3f, -0.4f, -0.1f);
        [SerializeField] private bool m_armsMatchHandToController = true;
        // Gentle finger curl on grab. MixamoArmRig now bends each bone around its own palm-ward axis
        // so it no longer disorients; flip the sign if fingers curl backward (away from the palm).
        [SerializeField, Range(0f, 130f)] private float m_armsFingerCurlAngle = 30f;
        [SerializeField] private float m_armsFingerCurlSign = 1f;

        private GameObject m_teleportMarker;
        private Renderer m_teleportMarkerRenderer;
        private Material m_validTeleportMaterial;
        private Material m_invalidTeleportMaterial;
        private Vector3 m_currentTeleportTarget;
        private bool m_hasValidTeleportTarget;
        private float m_nextSnapTurnTime;
        private GrabbableTool m_detectorGrabTool;
        private RadiationDetector m_detector;
        private VrUiPointer m_uiPointer;
        private Transform m_detectorRootTf;
        private Transform m_detectorSensorTip;
        private GameObject m_hotBarrel;
        private GameObject m_inertBarrel;
        private Transform m_hotLabel;
        private Transform m_inertLabel;
        private bool m_teachingSolved;
        private bool m_detectorWasGrabbed;
        private GameObject m_customArmsInstance;
        private MixamoArmRig m_customArmRig;

        private void Awake()
        {
            ApplyMovementPreference();

            if (m_tutorialManager == null)
            {
                m_tutorialManager = GetComponent<TutorialManager>();
            }

            ResolvePlayerReferences();
        }

        // Apply the movement style chosen on the start screen (persisted via PlayerPrefs). If no choice
        // was recorded (e.g. this scene launched directly in-editor), keep the serialized flags. Snap
        // turn is left unchanged in both modes.
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

        private void EnsureAudioListener()
        {
            if (m_cameraRig != null && m_cameraRig.centerEyeAnchor != null
                && m_cameraRig.centerEyeAnchor.GetComponent<AudioListener>() == null)
            {
                m_cameraRig.centerEyeAnchor.gameObject.AddComponent<AudioListener>();
            }
        }

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
            if (m_locomotionRoot == m_cameraTransform && m_cameraRig == null)
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

        private Ray GetTeleportRay()
        {
            if (m_cameraRig != null && m_cameraRig.rightControllerAnchor != null)
            {
                return new Ray(m_cameraRig.rightControllerAnchor.position, m_cameraRig.rightControllerAnchor.forward);
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
        private GameObject CreateTeachingBarrel(string objectName, Vector3 floorPosition)
        {
            GameObject barrel = m_teachingBarrelPrefab != null
                ? Instantiate(m_teachingBarrelPrefab)
                : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = objectName;
            barrel.transform.SetParent(transform, true);
            // floorPosition.y = 0 assumes the barrel model's pivot is at its base (true for the lab
            // 55-gal prefab); a centre-pivoted model would need a half-height offset.
            barrel.transform.SetPositionAndRotation(floorPosition, Quaternion.identity);
            FitTeachingBarrel(barrel);
            EnsureLabBarrelCollider(barrel);
            return barrel;
        }

        // Uniform-fit the model to ~0.9 m tall (the 55-gal height) so it matches the lab barrels.
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

        private void BillboardTeachingLabels()
        {
            FaceCamera(m_hotLabel);
            FaceCamera(m_inertLabel);
        }

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

            if (!InputManager.IsButtonADownOrPinchStarted())
            {
                return;
            }

            var aimed = GetTeachingAimedBarrel();
            if (aimed != null && aimed == m_hotBarrel)
            {
                m_teachingSolved = true;
                m_tutorialManager.CompleteStepIfCurrent(m_teachingSubmitStepIndex);
                if (m_hotLabel != null)
                {
                    var tm = m_hotLabel.GetComponent<TextMesh>();
                    if (tm != null)
                    {
                        tm.text = "SUBMITTED";
                    }
                }
            }
        }

        // Lightweight aim test scoped to the two teaching barrels (cone from the detector sensor tip
        // along the detector's aim axis, with a dead-on raycast fallback). Mirrors the lab's GetAimedBarrel.
        private GameObject GetTeachingAimedBarrel()
        {
            if (m_detectorGrabTool == null || !m_detectorGrabTool.IsHeld
                || m_detectorSensorTip == null || m_detectorRootTf == null)
            {
                return null;
            }

            var origin = m_detectorSensorTip.position;
            var direction = m_detectorRootTf.up;
            const float maxDistance = 4f;

            GameObject best = null;
            var bestAngle = Mathf.Max(2f, m_teachingGuessConeAngle);
            foreach (var barrel in new[] { m_hotBarrel, m_inertBarrel })
            {
                if (barrel == null)
                {
                    continue;
                }

                var barrelCollider = barrel.GetComponentInChildren<Collider>();
                var center = barrelCollider != null ? barrelCollider.bounds.center : barrel.transform.position;
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

        private void MarkDetectorGrabbed()
        {
            m_detectorWasGrabbed = true;
        }

        private void RecalibrateArms()
        {
            if (m_customArmRig != null && m_customArmRig.IsReady)
            {
                m_customArmRig.RecalibrateHands();
            }
        }

        private void CompleteDetectorStep()
        {
            m_tutorialManager?.CompleteStepIfCurrent(m_detectorStepIndex);
        }

        private void UpdateDetectorCompletion()
        {
            if (m_detectorWasGrabbed && m_detector != null && m_detector.SmoothedCps >= m_detectorCompletionCps)
            {
                CompleteDetectorStep();
            }
        }

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
