using SimJam.BarrelSimulator;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        [SerializeField] private string m_missionSceneName = "RadiationLabRoom";

        [Header("Player")]
        [SerializeField] private OVRCameraRig m_cameraRig;
        [SerializeField] private Transform m_locomotionRoot;
        [SerializeField] private Transform m_cameraTransform;
        [SerializeField] private bool m_createOvrCameraRig = true;
        [SerializeField] private float m_defaultEyeHeight = 1.6f;
        [SerializeField] private Vector2 m_roomHalfExtents = new Vector2(3.55f, 3.55f);
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.28f;

        [Header("Movement")]
        [SerializeField] private bool m_enableSmoothMove;
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
        [SerializeField] private Vector3 m_detectorHomePosition = new Vector3(-2.25f, 0.92f, -2.20f);
        [SerializeField] private Vector3 m_detectorHomeEuler = new Vector3(0f, 98f, 90f);
        [SerializeField, Min(0.05f)] private float m_detectorGrabRadius = 0.18f;
        [SerializeField, Min(0f)] private float m_detectorCompletionCps = 3f;

        private GameObject m_teleportMarker;
        private Renderer m_teleportMarkerRenderer;
        private Material m_validTeleportMaterial;
        private Material m_invalidTeleportMaterial;
        private Vector3 m_currentTeleportTarget;
        private bool m_hasValidTeleportTarget;
        private float m_nextSnapTurnTime;
        private GrabbableTool m_detectorGrabTool;
        private RadiationDetector m_detector;
        private bool m_detectorWasGrabbed;

        private void Awake()
        {
            if (m_tutorialManager == null)
            {
                m_tutorialManager = GetComponent<TutorialManager>();
            }

            if (m_tutorialManager != null)
            {
                m_tutorialManager.FinalStepSubmitted += BeginMission;
            }

            ResolvePlayerReferences();
        }

        private void Start()
        {
            ResolvePlayerReferences();
            ConfigureTutorialRadiationSources();

            if (m_spawnWorkingDetector)
            {
                SpawnWorkingDetector();
            }
        }

        private void Update()
        {
            ResolvePlayerReferences();
            UpdateMovement();
            UpdateTeleport();
            UpdateDetectorCompletion();
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

            UpdateTeleportTarget();
            if (WasTeleportPressed() && m_hasValidTeleportTarget)
            {
                MoveRigTo(m_currentTeleportTarget);
                m_tutorialManager?.CompleteStepIfCurrent(m_teleportStepIndex);
            }
        }

        private bool WasTeleportPressed()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.LHandTrigger) || OVRInput.GetDown(OVRInput.RawButton.RHandTrigger))
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

        private void SpawnWorkingDetector()
        {
            if (m_detectorGrabTool != null)
            {
                return;
            }

            var parts = DetectorModelBuilder.Build();
            parts.Root.name = "Tutorial Working IdentiFINDER";
            parts.Root.transform.SetPositionAndRotation(m_detectorHomePosition, Quaternion.Euler(m_detectorHomeEuler));

            m_detectorGrabTool = parts.Root.AddComponent<GrabbableTool>();
            m_detectorGrabTool.Initialize(m_cameraRig, m_detectorGrabRadius);
            m_detectorGrabTool.Grabbed += MarkDetectorGrabbed;

            var audio = parts.Root.AddComponent<GeigerAudio>();
            audio.enabled = m_enableDetectorAudio;
            m_detector = parts.Root.AddComponent<RadiationDetector>();
            m_detector.Initialize(m_cameraRig, parts.ScreenText, parts.SensorTip, audio, m_detectorGrabTool, m_detectorHomePosition, Quaternion.Euler(m_detectorHomeEuler));
        }

        private void MarkDetectorGrabbed()
        {
            m_detectorWasGrabbed = true;
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

        private void ConfigureTutorialRadiationSources()
        {
            var barrelObjects = GameObject.FindObjectsByType<Transform>(FindObjectsInactive.Exclude);
            var sourceIndex = 0;
            for (var i = 0; i < barrelObjects.Length; i++)
            {
                var barrel = barrelObjects[i];
                if (barrel == null || !barrel.name.Contains("Floor Barrel"))
                {
                    continue;
                }

                var source = barrel.GetComponent<RadiationSource>();
                if (source == null)
                {
                    source = barrel.gameObject.AddComponent<RadiationSource>();
                }

                source.Configure(1500f + sourceIndex * 650f, "Tutorial Source");
                sourceIndex++;
            }
        }

        private void BeginMission()
        {
            if (string.IsNullOrWhiteSpace(m_missionSceneName))
            {
                return;
            }

            SceneManager.LoadScene(m_missionSceneName);
        }

        private static Material CreateMaterial(string materialName, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = materialName, color = color };
            material.SetFloat("_Glossiness", 0.35f);
            return material;
        }

        private void OnDestroy()
        {
            if (m_tutorialManager != null)
            {
                m_tutorialManager.FinalStepSubmitted -= BeginMission;
            }

            if (m_detectorGrabTool != null)
            {
                m_detectorGrabTool.Grabbed -= MarkDetectorGrabbed;
            }
        }
    }
}
