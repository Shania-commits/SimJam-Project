using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SimJam.BarrelSimulator
{
    public class BasicVRRoomBarrelSpawner : MonoBehaviour
    {
        private const float FeetToMeters = 0.3048f;
        private const float DefaultRoomFeet = 20f;
        private const float DefaultRoomMeters = DefaultRoomFeet * FeetToMeters;

        private enum BarrelSize
        {
            Gallon55,
            Gallon30,
            Gallon5
        }

        private enum SurfaceKind
        {
            Floor,
            Table,
            Shelf
        }

        private enum WallSide
        {
            North,
            South,
            East,
            West
        }

        [Serializable]
        private struct BarrelPrefabSet
        {
            [FormerlySerializedAs("m_barrelPrefab")] public GameObject barrel55GallonPrefab;
            public GameObject barrel30GallonPrefab;
            public GameObject barrel5GallonPrefab;
        }

        private struct BarrelSpec
        {
            public BarrelSize Size;
            public string Label;
            public GameObject Prefab;
            public Vector3 ModelScale;
            public float Diameter;
            public float Height;
            public float LabelHeight;
            public Color BodyColor;
            public float SelectionWeight;
        }

        private struct SpawnSlot
        {
            public Vector3 Position;
            public Vector3 LabelForward;
            public SurfaceKind Surface;
            public BarrelSize[] AllowedSizes;
            public bool AllowSideways;
            public float SurfaceYaw;
            public string SourceName;
        }

        private struct PlacedBarrel
        {
            public Vector3 Position;
            public float Radius;
            public float Height;
            public BarrelSize Size;
            public SurfaceKind Surface;
        }

        private struct ObstacleRect
        {
            public Vector2 Center;
            public Vector2 HalfExtents;
            public float YawDegrees;
        }

        private sealed class PropInfo
        {
            public Transform Transform;
            public ObstacleRect Footprint;
            public float SurfaceY;
            public Vector3 Forward;
            public Vector2 Size;
            public int TierCount;
            public List<float> ShelfSurfaceHeights;
            public Vector2 ShelfSlotSize;
            public Vector3 ShelfSlotCenterLocal;
            public WallSide Wall;
            public string Name;
        }

        [Header("VR rig")]
        [SerializeField] private bool m_createOvrCameraRig = true;
        [SerializeField] private Vector3 m_playerStartPosition = Vector3.zero;
        [SerializeField, Min(0.5f)] private float m_defaultEyeHeight = 1.6f;

        [Header("Room scale")]
        [SerializeField] private bool m_buildRoomGeometry = true;
        [SerializeField] private Vector2 m_roomSizeFeet = new Vector2(DefaultRoomFeet, DefaultRoomFeet);
        [SerializeField] private Vector2 m_spawnRoomSizeFeet = new Vector2(12f, 12f);
        [SerializeField, Min(1f)] private float m_wallHeight = 2.75f;
        [SerializeField, Min(0.02f)] private float m_wallThickness = 0.1f;
        [SerializeField, Min(0.02f)] private float m_floorThickness = 0.08f;
        [SerializeField] private bool m_showEditorScalePreview = true;

        [Header("Connecting door")]
        [SerializeField, Min(0.6f)] private float m_doorwayWidth = 0.95f;
        [SerializeField, Min(1.5f)] private float m_doorwayHeight = 2.1f;
        [SerializeField, Range(-130f, 130f)] private float m_doorOpenAngle = -95f;
        [SerializeField, Min(0.05f)] private float m_doorKnobInteractionRadius = 0.24f;
        [SerializeField, Min(0.05f)] private float m_doorToggleCooldown = 0.35f;
        [SerializeField, Min(15f)] private float m_doorSwingSpeed = 150f;

        [Header("Controller hand visuals")]
        [SerializeField] private bool m_showControllerHands = true;
        [SerializeField, Min(0.1f)] private float m_controllerHandScale = 1f;

        [Header("Barrel prefabs")]
        [SerializeField] private BarrelPrefabSet m_barrelPrefabs;
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        [SerializeField] private Vector3 m_barrel55ModelScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        [SerializeField] private Vector3 m_barrel30ModelScale = new Vector3(0.14f, 0.14f, 0.14f);
        [SerializeField] private Vector3 m_barrel5ModelScale = new Vector3(0.07f, 0.07f, 0.07f);

        [Header("Scenario randomization")]
        [SerializeField, Min(1)] private int m_minBarrels = 1;
        [SerializeField, Min(1)] private int m_maxBarrels = 45;
        [SerializeField, Min(0)] private int m_minTables;
        [SerializeField, Min(0)] private int m_maxTables = 4;
        [SerializeField, Min(0)] private int m_minShelfUnits = 2;
        [SerializeField, Min(0)] private int m_maxShelfUnits = 9;
        [SerializeField, Min(1)] private int m_layoutRetryCount = 10;
        [SerializeField] private bool m_useFixedSeed;
        [SerializeField] private int m_fixedSeed = 12345;

        [Header("Visibility and walkability")]
        [SerializeField, Min(0.1f)] private float m_playerClearance = 0.8f;
        [SerializeField, Min(0.05f)] private float m_playerRadius = 0.28f;
        [SerializeField, Min(0.2f)] private float m_centralAisleWidth = 0.95f;
        [SerializeField, Min(0.05f)] private float m_floorBarrelSpacing = 0.64f;
        [SerializeField, Min(0.01f)] private float m_tableBarrelSpacing = 0.34f;
        [SerializeField, Min(0.01f)] private float m_shelfBarrelSpacing = 0.28f;

        [Header("Shelf asset")]
        [SerializeField] private GameObject m_wallShelfPrefab;
        [SerializeField, Min(0.001f)] private float m_shelfSurfaceClearance = 0.015f;

        [Header("Locomotion")]
        [SerializeField] private bool m_enableSmoothMove = true;
        [SerializeField] private bool m_enableTeleport = true;
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        [SerializeField, Min(5f)] private float m_snapTurnDegrees = 30f;
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.3f;
        [SerializeField, Min(0.05f)] private float m_thumbstickDeadzone = 0.22f;

        [Header("Fallback counts")]
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        private readonly List<GameObject> m_spawnedObjects = new();
        private readonly List<PropInfo> m_tables = new();
        private readonly List<PropInfo> m_shelves = new();
        private readonly List<SpawnSlot> m_spawnSlots = new();
        private readonly List<PlacedBarrel> m_placedBarrels = new();
        private readonly List<ObstacleRect> m_navigationObstacles = new();
        private readonly List<Material> m_runtimeMaterials = new();
        private readonly Dictionary<string, Material> m_materialCache = new();

        private int[] m_fallbackCountPool;
        private Transform m_cameraTransform;
        private Transform m_locomotionRoot;
        private OVRCameraRig m_cameraRig;
        private Transform m_roomRoot;
        private Transform m_scenarioRoot;
        private GameObject m_teleportMarker;
        private Renderer m_teleportMarkerRenderer;
        private Material m_validTeleportMaterial;
        private Material m_invalidTeleportMaterial;
        private Transform m_doorPivot;
        private Transform m_labDoorKnob;
        private Transform m_spawnDoorKnob;
        private float m_doorCurrentAngle;
        private bool m_isDoorOpen;
        private float m_nextDoorToggleTime;
        private GameObject m_leftHandVisual;
        private GameObject m_rightHandVisual;
        private Vector3 m_currentTeleportTarget;
        private bool m_hasValidTeleportTarget;
        private float m_nextSnapTurnTime;

        private Vector2 RoomSizeMeters => new(
            Mathf.Max(8f, m_roomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_roomSizeFeet.y) * FeetToMeters);

        private Vector2 SpawnRoomSizeMeters => new(
            Mathf.Max(8f, m_spawnRoomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_spawnRoomSizeFeet.y) * FeetToMeters);

        private Vector3 SpawnRoomCenter
        {
            get
            {
                var roomSize = RoomSizeMeters;
                var spawnSize = SpawnRoomSizeMeters;
                return new Vector3(0f, 0f, -roomSize.y * 0.5f - spawnSize.y * 0.5f);
            }
        }

        private Vector3 PlayerStartPosition
        {
            get
            {
                if (IsInsideSpawnRoom(m_playerStartPosition, 0.05f))
                {
                    return m_playerStartPosition;
                }

                var spawnSize = SpawnRoomSizeMeters;
                return SpawnRoomCenter + new Vector3(0f, 0f, -spawnSize.y * 0.18f);
            }
        }

        private void Awake()
        {
            m_cameraTransform = EnsureCameraRig();
        }

        private void Start()
        {
            if (m_buildRoomGeometry)
            {
                BuildRoomGeometry();
            }

            EnsureControllerHandVisuals();
            GenerateRun();
        }

        private void Update()
        {
            EnsureControllerHandVisuals();
            HandleLocomotion();
            HandleDoorInteraction();
            UpdateDoorSwing();

            if (InputManager.IsButtonADownOrPinchStarted())
            {
                GenerateRun();
            }

            if (InputManager.IsButtonBDownOrMiddleFingerPinchStarted())
            {
                ClearScenario();
                SetStatus("Scenario cleared.");
            }

            if (InputManager.IsButtonXDown())
            {
                RandomizeRadiationCounts();
            }
        }

        public void GenerateRun()
        {
            ClearScenario();

            var baseSeed = m_useFixedSeed
                ? m_fixedSeed
                : unchecked(Environment.TickCount ^ UnityEngine.Random.Range(int.MinValue, int.MaxValue));
            UnityEngine.Random.InitState(baseSeed);

            var requestedCount = UnityEngine.Random.Range(
                Mathf.Min(m_minBarrels, m_maxBarrels),
                Mathf.Max(m_minBarrels, m_maxBarrels) + 1);

            ScenarioResult bestResult = default;
            var bestSeed = baseSeed;
            var foundFullScenario = false;
            var attemptCount = Mathf.Max(1, m_layoutRetryCount);

            for (var attempt = 0; attempt < attemptCount; attempt++)
            {
                var attemptSeed = unchecked(baseSeed + attempt * 7919);
                UnityEngine.Random.InitState(attemptSeed);
                ClearScenario();
                BuildRandomizedScenarioProps();
                BuildSpawnSlots();
                var result = SpawnBarrelsForCurrentLayout(requestedCount);

                if (result.SpawnedCount > bestResult.SpawnedCount)
                {
                    bestResult = result;
                    bestSeed = attemptSeed;
                }

                if (result.SpawnedCount >= requestedCount)
                {
                    foundFullScenario = true;
                    break;
                }
            }

            if (!foundFullScenario)
            {
                ClearScenario();
                UnityEngine.Random.InitState(bestSeed);
                BuildRandomizedScenarioProps();
                BuildSpawnSlots();
                bestResult = SpawnBarrelsForCurrentLayout(requestedCount);
            }

            var finalCount = foundFullScenario ? requestedCount : bestResult.SpawnedCount;
            SetStatus(finalCount >= requestedCount
                ? $"Randomized office room: {finalCount} barrels."
                : $"Requested {requestedCount}; placed {finalCount} visible/walkable barrels.");
        }

        public void ClearScenario()
        {
            foreach (var spawnedObject in m_spawnedObjects)
            {
                if (spawnedObject != null)
                {
                    Destroy(spawnedObject);
                }
            }

            m_spawnedObjects.Clear();
            m_spawnedBarrels.Clear();
            m_tables.Clear();
            m_shelves.Clear();
            m_spawnSlots.Clear();
            m_placedBarrels.Clear();
            m_navigationObstacles.Clear();

            if (m_scenarioRoot != null)
            {
                Destroy(m_scenarioRoot.gameObject);
                m_scenarioRoot = null;
            }
        }

        public void RandomizeRadiationCounts()
        {
            if (m_spawnedBarrels.Count == 0)
            {
                SetStatus("No barrels to randomize.");
                return;
            }

            m_radiationCountProfile?.RebuildGeneratedPool();
            RebuildFallbackCountPool();

            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel != null)
                {
                    barrel.SetRadiationCount(GetRandomRadiationCount());
                }
            }

            SetStatus("Randomized barrel counts.");
        }

        private Transform EnsureCameraRig()
        {
            if (m_createOvrCameraRig)
            {
                var existingRig = FindAnyObjectByType<OVRCameraRig>();
                if (existingRig != null)
                {
                    m_cameraRig = existingRig;
                    m_locomotionRoot = existingRig.transform;
                    ConfigureRig(existingRig);
                    MoveRigTo(PlayerStartPosition);
                    return existingRig.centerEyeAnchor;
                }

                var defaultCamera = Camera.main;
                if (defaultCamera != null)
                {
                    defaultCamera.gameObject.SetActive(false);
                }

                var rigObject = new GameObject("OVRCameraRig");
                rigObject.transform.SetPositionAndRotation(PlayerStartPosition, Quaternion.identity);
                m_locomotionRoot = rigObject.transform;

                var manager = FindAnyObjectByType<OVRManager>();
                if (manager == null)
                {
                    manager = rigObject.AddComponent<OVRManager>();
                }

                manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
                manager.isInsightPassthroughEnabled = false;

                m_cameraRig = rigObject.AddComponent<OVRCameraRig>();
                m_cameraRig.EnsureGameObjectIntegrity();
                ConfigureRig(m_cameraRig);
                return m_cameraRig.centerEyeAnchor;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                cameraObject.AddComponent<AudioListener>();
            }

            camera.transform.SetPositionAndRotation(PlayerStartPosition + Vector3.up * m_defaultEyeHeight, Quaternion.identity);
            camera.stereoTargetEye = StereoTargetEyeMask.Both;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 100f;
            m_locomotionRoot = camera.transform;
            return camera.transform;
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
        }

        private void EnsureControllerHandVisuals()
        {
            if (!m_showControllerHands || m_cameraRig == null)
            {
                SetControllerHandVisualActive(false);
                return;
            }

            m_cameraRig.EnsureGameObjectIntegrity();
            if (MetaQuestHandVisuals.TryEnsure(m_cameraRig, ref m_leftHandVisual, ref m_rightHandVisual, m_controllerHandScale))
            {
                SetControllerHandVisualActive(true);
                return;
            }

            if (m_cameraRig.leftControllerAnchor != null && m_leftHandVisual == null)
            {
                m_leftHandVisual = CreateControllerHandVisual(m_cameraRig.leftControllerAnchor, true);
            }

            if (m_cameraRig.rightControllerAnchor != null && m_rightHandVisual == null)
            {
                m_rightHandVisual = CreateControllerHandVisual(m_cameraRig.rightControllerAnchor, false);
            }

            SetControllerHandVisualActive(true);
        }

        private void SetControllerHandVisualActive(bool isActive)
        {
            if (m_leftHandVisual != null)
            {
                m_leftHandVisual.SetActive(isActive);
            }

            if (m_rightHandVisual != null)
            {
                m_rightHandVisual.SetActive(isActive);
            }
        }

        private GameObject CreateControllerHandVisual(Transform anchor, bool isLeft)
        {
            var root = new GameObject(isLeft ? "Left Controller Hand Visual" : "Right Controller Hand Visual");
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * m_controllerHandScale;

            var handMaterial = CreateMaterial(new Color(0.82f, 0.72f, 0.62f), "Controller Hand Skin");
            var jointMaterial = CreateMaterial(new Color(0.68f, 0.6f, 0.52f), "Controller Hand Joints");
            var side = isLeft ? -1f : 1f;

            CreateHandPart(root.transform, "Palm", PrimitiveType.Sphere, new Vector3(0f, -0.018f, 0.065f), Quaternion.identity, new Vector3(0.08f, 0.04f, 0.105f), handMaterial);
            CreateHandPart(root.transform, "Wrist", PrimitiveType.Cylinder, new Vector3(0f, -0.018f, -0.035f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.035f, 0.055f, 0.035f), handMaterial);
            CreateHandPart(root.transform, "Thumb", PrimitiveType.Cylinder, new Vector3(side * 0.075f, -0.005f, 0.058f), Quaternion.Euler(70f, 0f, side * 38f), new Vector3(0.017f, 0.052f, 0.017f), handMaterial);

            for (var i = 0; i < 4; i++)
            {
                var x = Mathf.Lerp(-0.045f, 0.045f, i / 3f);
                CreateHandPart(root.transform, $"Finger {i + 1}", PrimitiveType.Cylinder, new Vector3(x, 0.002f, 0.155f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.014f, 0.062f, 0.014f), handMaterial);
                CreateHandPart(root.transform, $"Knuckle {i + 1}", PrimitiveType.Sphere, new Vector3(x, 0.005f, 0.095f), Quaternion.identity, new Vector3(0.024f, 0.018f, 0.024f), jointMaterial);
            }

            return root;
        }

        private static void CreateHandPart(Transform parent, string partName, PrimitiveType primitiveType, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Material material)
        {
            var part = GameObject.CreatePrimitive(primitiveType);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            AssignMaterial(part, material);
            DisableCollider(part);
        }

        private void BuildRoomGeometry()
        {
            if (m_roomRoot != null)
            {
                Destroy(m_roomRoot.gameObject);
            }

            var roomObject = new GameObject("Generated 20ft Office Room");
            roomObject.transform.SetParent(transform, false);
            m_roomRoot = roomObject.transform;

            var floorMaterial = CreateMaterial(new Color(0.93f, 0.94f, 0.92f), "Office Floor");
            var wallMaterial = CreateMaterial(Color.white, "White Wall");
            var ceilingMaterial = CreateMaterial(new Color(0.86f, 0.88f, 0.87f), "Ceiling Tile");
            var gridMaterial = CreateMaterial(new Color(0.58f, 0.6f, 0.62f), "Ceiling Grid");
            var lightPanelMaterial = CreateMaterial(new Color(0.85f, 0.98f, 1f), "Fluorescent Panel");
            var homeFloorMaterial = CreateMaterial(new Color(0.64f, 0.53f, 0.42f), "Warm Home Floor");
            var homeWallMaterial = CreateMaterial(new Color(0.96f, 0.94f, 0.89f), "Warm Home Wall");
            var brownDoorMaterial = CreateMaterial(new Color(0.36f, 0.19f, 0.08f), "Brown Door Face");
            var whiteDoorMaterial = CreateMaterial(new Color(0.96f, 0.95f, 0.9f), "White Door Face");
            var goldMaterial = CreateMaterial(new Color(1f, 0.68f, 0.16f), "Gold Door Knob");
            lightPanelMaterial.EnableKeyword("_EMISSION");
            lightPanelMaterial.SetColor("_EmissionColor", new Color(0.75f, 0.95f, 1f) * 1.4f);

            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;
            var sharedWallZ = -halfDepth - m_wallThickness * 0.5f;

            CreateRoomCube("Floor", new Vector3(0f, -m_floorThickness * 0.5f, 0f), new Vector3(size.x, m_floorThickness, size.y), floorMaterial);
            CreateRoomCube("North Wall", new Vector3(0f, m_wallHeight * 0.5f, halfDepth + m_wallThickness * 0.5f), new Vector3(size.x + m_wallThickness * 2f, m_wallHeight, m_wallThickness), wallMaterial);
            CreateDoorwayWallSegments("Shared Wall", sharedWallZ, size.x + m_wallThickness * 2f, wallMaterial);
            CreateRoomCube("East Wall", new Vector3(halfWidth + m_wallThickness * 0.5f, m_wallHeight * 0.5f, 0f), new Vector3(m_wallThickness, m_wallHeight, size.y), wallMaterial);
            CreateRoomCube("West Wall", new Vector3(-halfWidth - m_wallThickness * 0.5f, m_wallHeight * 0.5f, 0f), new Vector3(m_wallThickness, m_wallHeight, size.y), wallMaterial);
            CreateRoomCube("Drop Ceiling", new Vector3(0f, m_wallHeight + 0.025f, 0f), new Vector3(size.x, 0.05f, size.y), ceilingMaterial);

            BuildCeilingGrid(size, Vector3.zero, gridMaterial);
            BuildFluorescentPanels(Vector3.zero, lightPanelMaterial);
            BuildSpawnRoomGeometry(homeFloorMaterial, homeWallMaterial, ceilingMaterial, gridMaterial, lightPanelMaterial);
            BuildConnectingDoor(sharedWallZ, brownDoorMaterial, whiteDoorMaterial, goldMaterial);
            BuildHomeRoomProps();
        }

        private void CreateDoorwayWallSegments(string prefix, float wallCenterZ, float wallWidth, Material wallMaterial)
        {
            var safeDoorWidth = Mathf.Min(m_doorwayWidth, wallWidth - m_wallThickness * 2f);
            var sideWidth = Mathf.Max(0.05f, (wallWidth - safeDoorWidth) * 0.5f);
            var sideCenterOffset = safeDoorWidth * 0.5f + sideWidth * 0.5f;
            var headerHeight = Mathf.Max(0.05f, m_wallHeight - m_doorwayHeight);

            CreateRoomCube($"{prefix} Left Segment", new Vector3(-sideCenterOffset, m_wallHeight * 0.5f, wallCenterZ), new Vector3(sideWidth, m_wallHeight, m_wallThickness), wallMaterial);
            CreateRoomCube($"{prefix} Right Segment", new Vector3(sideCenterOffset, m_wallHeight * 0.5f, wallCenterZ), new Vector3(sideWidth, m_wallHeight, m_wallThickness), wallMaterial);
            CreateRoomCube($"{prefix} Header", new Vector3(0f, m_doorwayHeight + headerHeight * 0.5f, wallCenterZ), new Vector3(safeDoorWidth, headerHeight, m_wallThickness), wallMaterial);
        }

        private void BuildSpawnRoomGeometry(Material floorMaterial, Material wallMaterial, Material ceilingMaterial, Material gridMaterial, Material lightPanelMaterial)
        {
            var size = SpawnRoomSizeMeters;
            var center = SpawnRoomCenter;
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;

            CreateRoomCube("Spawn Room Floor", center + new Vector3(0f, -m_floorThickness * 0.5f, 0f), new Vector3(size.x, m_floorThickness, size.y), floorMaterial);
            CreateRoomCube("Spawn Room South Wall", center + new Vector3(0f, m_wallHeight * 0.5f, -halfDepth - m_wallThickness * 0.5f), new Vector3(size.x + m_wallThickness * 2f, m_wallHeight, m_wallThickness), wallMaterial);
            CreateRoomCube("Spawn Room East Wall", center + new Vector3(halfWidth + m_wallThickness * 0.5f, m_wallHeight * 0.5f, 0f), new Vector3(m_wallThickness, m_wallHeight, size.y), wallMaterial);
            CreateRoomCube("Spawn Room West Wall", center + new Vector3(-halfWidth - m_wallThickness * 0.5f, m_wallHeight * 0.5f, 0f), new Vector3(m_wallThickness, m_wallHeight, size.y), wallMaterial);
            CreateRoomCube("Spawn Room Ceiling", center + new Vector3(0f, m_wallHeight + 0.025f, 0f), new Vector3(size.x, 0.05f, size.y), ceilingMaterial);

            BuildCeilingGrid(size, center, gridMaterial);
            BuildSpawnRoomLights(center, lightPanelMaterial);
        }

        private void BuildCeilingGrid(Vector2 size, Vector3 center, Material gridMaterial)
        {
            const float tileSize = 0.61f;
            const float stripThickness = 0.018f;
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;

            for (var x = -halfWidth; x <= halfWidth + 0.001f; x += tileSize)
            {
                CreateRoomCube("Ceiling Grid X", center + new Vector3(x, m_wallHeight + 0.055f, 0f), new Vector3(stripThickness, 0.012f, size.y), gridMaterial);
            }

            for (var z = -halfDepth; z <= halfDepth + 0.001f; z += tileSize)
            {
                CreateRoomCube("Ceiling Grid Z", center + new Vector3(0f, m_wallHeight + 0.058f, z), new Vector3(size.x, 0.012f, stripThickness), gridMaterial);
            }
        }

        private void BuildFluorescentPanels(Vector3 center, Material lightPanelMaterial)
        {
            var panelPositions = new[]
            {
                center + new Vector3(-1.4f, m_wallHeight + 0.075f, -1.25f),
                center + new Vector3(1.4f, m_wallHeight + 0.075f, -1.25f),
                center + new Vector3(-1.4f, m_wallHeight + 0.075f, 1.25f),
                center + new Vector3(1.4f, m_wallHeight + 0.075f, 1.25f)
            };

            foreach (var panelPosition in panelPositions)
            {
                CreateRoomCube("Fluorescent Light Panel", panelPosition, new Vector3(0.95f, 0.018f, 0.28f), lightPanelMaterial);

                var lightObject = new GameObject("Fluorescent Point Light");
                lightObject.transform.SetParent(m_roomRoot, false);
                lightObject.transform.localPosition = panelPosition + Vector3.down * 0.15f;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.88f, 0.97f, 1f);
                light.intensity = 1.25f;
                light.range = 4.5f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildSpawnRoomLights(Vector3 center, Material lightPanelMaterial)
        {
            var positions = new[]
            {
                center + new Vector3(-0.85f, m_wallHeight + 0.075f, -0.65f),
                center + new Vector3(0.85f, m_wallHeight + 0.075f, 0.65f)
            };

            foreach (var panelPosition in positions)
            {
                CreateRoomCube("Warm Office Light Panel", panelPosition, new Vector3(0.8f, 0.018f, 0.24f), lightPanelMaterial);

                var lightObject = new GameObject("Warm Office Point Light");
                lightObject.transform.SetParent(m_roomRoot, false);
                lightObject.transform.localPosition = panelPosition + Vector3.down * 0.2f;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.88f, 0.68f);
                light.intensity = 1.1f;
                light.range = 3.7f;
                light.shadows = LightShadows.None;
            }
        }

        private void BuildConnectingDoor(float sharedWallZ, Material brownDoorMaterial, Material whiteDoorMaterial, Material goldMaterial)
        {
            const float doorThickness = 0.055f;
            var doorWidth = Mathf.Max(0.55f, m_doorwayWidth - 0.08f);
            var doorHeight = Mathf.Min(m_doorwayHeight - 0.04f, m_wallHeight - 0.08f);
            var hingeX = -doorWidth * 0.5f;

            var pivotObject = new GameObject("Swinging Door Hinge");
            pivotObject.transform.SetParent(m_roomRoot, false);
            pivotObject.transform.localPosition = new Vector3(hingeX, 0f, sharedWallZ);
            pivotObject.transform.localRotation = Quaternion.identity;
            m_doorPivot = pivotObject.transform;
            m_doorCurrentAngle = 0f;
            m_isDoorOpen = false;

            CreateChildCube(m_doorPivot, "Door Brown Core", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, 0f), new Vector3(doorWidth, doorHeight, doorThickness), brownDoorMaterial, true);
            CreateChildCube(m_doorPivot, "Door Lab Brown Face", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, doorThickness * 0.5f + 0.003f), new Vector3(doorWidth * 0.96f, doorHeight * 0.96f, 0.006f), brownDoorMaterial, false);
            CreateChildCube(m_doorPivot, "Door Spawn White Face", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, -doorThickness * 0.5f - 0.003f), new Vector3(doorWidth * 0.96f, doorHeight * 0.96f, 0.006f), whiteDoorMaterial, false);

            m_labDoorKnob = CreateDoorKnob(m_doorPivot, "Lab Side Gold Knob", new Vector3(doorWidth - 0.15f, 0.96f, doorThickness * 0.5f + 0.055f), goldMaterial, true);
            m_spawnDoorKnob = CreateDoorKnob(m_doorPivot, "Spawn Side Gold Knob", new Vector3(doorWidth - 0.15f, 0.96f, -doorThickness * 0.5f - 0.055f), goldMaterial, false);
        }

        private Transform CreateDoorKnob(Transform parent, string knobName, Vector3 localPosition, Material material, bool facesLab)
        {
            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = $"{knobName} Stem";
            stem.transform.SetParent(parent, false);
            stem.transform.localPosition = localPosition + new Vector3(0f, 0f, facesLab ? -0.02f : 0.02f);
            stem.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            stem.transform.localScale = new Vector3(0.022f, 0.035f, 0.022f);
            AssignMaterial(stem, material);
            DisableCollider(stem);

            var knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = knobName;
            knob.transform.SetParent(parent, false);
            knob.transform.localPosition = localPosition;
            knob.transform.localScale = new Vector3(0.085f, 0.085f, 0.085f);
            AssignMaterial(knob, material);
            DisableCollider(knob);
            return knob.transform;
        }

        private void BuildHomeRoomProps()
        {
            var center = SpawnRoomCenter;
            var size = SpawnRoomSizeMeters;
            var halfDepth = size.y * 0.5f;
            var warmWood = CreateMaterial(new Color(0.45f, 0.28f, 0.14f), "Warm Wood");
            var couchMaterial = CreateMaterial(new Color(0.43f, 0.49f, 0.55f), "Soft Couch Fabric");
            var rugMaterial = CreateMaterial(new Color(0.58f, 0.18f, 0.14f), "Home Area Rug");
            var plantPotMaterial = CreateMaterial(new Color(0.38f, 0.22f, 0.12f), "Plant Pot");
            var plantLeafMaterial = CreateMaterial(new Color(0.12f, 0.42f, 0.2f), "Plant Leaves");
            var lampShadeMaterial = CreateMaterial(new Color(0.96f, 0.86f, 0.62f), "Warm Lamp Shade");

            CreateRoomCube("Home Area Rug", center + new Vector3(0f, 0.014f, -0.2f), new Vector3(1.9f, 0.026f, 1.25f), rugMaterial);

            var couchZ = center.z - halfDepth + 0.45f;
            CreateRoomCube("Home Couch Seat", new Vector3(center.x, 0.22f, couchZ), new Vector3(1.55f, 0.36f, 0.5f), couchMaterial);
            CreateRoomCube("Home Couch Back", new Vector3(center.x, 0.58f, couchZ - 0.23f), new Vector3(1.65f, 0.72f, 0.16f), couchMaterial);
            CreateRoomCube("Home Couch Left Arm", new Vector3(center.x - 0.88f, 0.36f, couchZ), new Vector3(0.18f, 0.48f, 0.56f), couchMaterial);
            CreateRoomCube("Home Couch Right Arm", new Vector3(center.x + 0.88f, 0.36f, couchZ), new Vector3(0.18f, 0.48f, 0.56f), couchMaterial);

            CreateRoomCube("Home Coffee Table", center + new Vector3(0f, 0.22f, 0.15f), new Vector3(1.0f, 0.12f, 0.48f), warmWood);
            CreateRoomCube("Home Coffee Table Base", center + new Vector3(0f, 0.1f, 0.15f), new Vector3(0.16f, 0.2f, 0.16f), warmWood);

            CreateRoomCube("Home Side Table", center + new Vector3(-1.35f, 0.34f, -0.75f), new Vector3(0.46f, 0.12f, 0.46f), warmWood);
            CreateRoomCube("Home Side Table Base", center + new Vector3(-1.35f, 0.17f, -0.75f), new Vector3(0.14f, 0.34f, 0.14f), warmWood);
            CreateLamp(center + new Vector3(-1.35f, 0.4f, -0.75f), lampShadeMaterial);

            CreatePlant(center + new Vector3(1.35f, 0f, -1.15f), plantPotMaterial, plantLeafMaterial);
        }

        private void CreateLamp(Vector3 basePosition, Material shadeMaterial)
        {
            var stemMaterial = CreateMaterial(new Color(0.82f, 0.62f, 0.3f), "Lamp Stem");
            var baseDisk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseDisk.name = "Home Table Lamp Base";
            baseDisk.transform.SetParent(m_roomRoot, false);
            baseDisk.transform.localPosition = basePosition + Vector3.up * 0.015f;
            baseDisk.transform.localScale = new Vector3(0.13f, 0.015f, 0.13f);
            AssignMaterial(baseDisk, stemMaterial);
            DisableCollider(baseDisk);

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Home Table Lamp Stem";
            stem.transform.SetParent(m_roomRoot, false);
            stem.transform.localPosition = basePosition + Vector3.up * 0.16f;
            stem.transform.localScale = new Vector3(0.025f, 0.16f, 0.025f);
            AssignMaterial(stem, stemMaterial);
            DisableCollider(stem);

            var shade = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shade.name = "Home Table Lamp Shade";
            shade.transform.SetParent(m_roomRoot, false);
            shade.transform.localPosition = basePosition + Vector3.up * 0.35f;
            shade.transform.localScale = new Vector3(0.22f, 0.13f, 0.22f);
            AssignMaterial(shade, shadeMaterial);
            DisableCollider(shade);

            var lampLight = new GameObject("Home Table Lamp Light");
            lampLight.transform.SetParent(m_roomRoot, false);
            lampLight.transform.localPosition = shade.transform.localPosition;
            var light = lampLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.78f, 0.48f);
            light.intensity = 0.65f;
            light.range = 2.2f;
            light.shadows = LightShadows.None;
        }

        private void CreatePlant(Vector3 basePosition, Material potMaterial, Material leafMaterial)
        {
            var pot = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pot.name = "Home Plant Pot";
            pot.transform.SetParent(m_roomRoot, false);
            pot.transform.localPosition = basePosition + Vector3.up * 0.16f;
            pot.transform.localScale = new Vector3(0.22f, 0.16f, 0.22f);
            AssignMaterial(pot, potMaterial);

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Home Plant Stem";
            stem.transform.SetParent(m_roomRoot, false);
            stem.transform.localPosition = basePosition + Vector3.up * 0.52f;
            stem.transform.localScale = new Vector3(0.035f, 0.36f, 0.035f);
            AssignMaterial(stem, leafMaterial);
            DisableCollider(stem);

            var leafOffsets = new[]
            {
                new Vector3(0f, 0.86f, 0f),
                new Vector3(0.16f, 0.7f, 0.06f),
                new Vector3(-0.16f, 0.68f, -0.04f),
                new Vector3(0.06f, 0.62f, -0.17f)
            };

            foreach (var offset in leafOffsets)
            {
                var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                leaf.name = "Home Plant Leaves";
                leaf.transform.SetParent(m_roomRoot, false);
                leaf.transform.localPosition = basePosition + offset;
                leaf.transform.localScale = new Vector3(0.24f, 0.16f, 0.24f);
                AssignMaterial(leaf, leafMaterial);
                DisableCollider(leaf);
            }
        }

        private void CreateRoomCube(string cubeName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreateChildCube(m_roomRoot, cubeName, localPosition, localScale, material, true);
        }

        private GameObject CreateChildCube(Transform parent, string cubeName, Vector3 localPosition, Vector3 localScale, Material material, bool colliderEnabled)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = cubeName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;

            AssignMaterial(cube, material);
            if (!colliderEnabled)
            {
                DisableCollider(cube);
            }

            return cube;
        }

        private static void AssignMaterial(GameObject target, Material material)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static void DisableCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private void BuildRandomizedScenarioProps()
        {
            EnsureScenarioRoot();
            m_tables.Clear();
            m_shelves.Clear();
            m_navigationObstacles.Clear();

            var tableCount = UnityEngine.Random.Range(m_minTables, m_maxTables + 1);
            var shelfCount = UnityEngine.Random.Range(m_minShelfUnits, m_maxShelfUnits + 1);

            for (var i = 0; i < tableCount; i++)
            {
                TryCreateRandomTable(i);
            }

            for (var i = 0; i < shelfCount; i++)
            {
                TryCreateRandomShelf(i);
            }
        }

        private void TryCreateRandomTable(int tableIndex)
        {
            var tableMaterial = CreateMaterial(new Color(0.94f, 0.94f, 0.91f), "Plastic Folding Table");
            var legMaterial = CreateMaterial(new Color(0.55f, 0.56f, 0.58f), "Table Metal Legs");
            var tableSize = new Vector2(UnityEngine.Random.Range(1.25f, 1.65f), UnityEngine.Random.Range(0.62f, 0.8f));
            const float tableHeight = 0.74f;

            for (var attempt = 0; attempt < 60; attempt++)
            {
                var yaw = UnityEngine.Random.value > 0.5f ? 0f : 90f;
                var position = GetRandomQuadrantPosition(0.7f);
                var footprint = new ObstacleRect
                {
                    Center = new Vector2(position.x, position.z),
                    HalfExtents = tableSize * 0.5f + Vector2.one * 0.12f,
                    YawDegrees = yaw
                };

                if (!IsFootprintAllowed(footprint, 0.18f))
                {
                    continue;
                }

                var tableRoot = new GameObject($"Random Folding Table {tableIndex + 1}");
                tableRoot.transform.SetParent(m_scenarioRoot, false);
                tableRoot.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                m_spawnedObjects.Add(tableRoot);

                CreatePropCube(tableRoot.transform, "Table Top", new Vector3(0f, tableHeight, 0f), new Vector3(tableSize.x, 0.07f, tableSize.y), tableMaterial);
                var legX = tableSize.x * 0.42f;
                var legZ = tableSize.y * 0.38f;
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(-legX, tableHeight * 0.5f, -legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(legX, tableHeight * 0.5f, -legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(-legX, tableHeight * 0.5f, legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(legX, tableHeight * 0.5f, legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);

                var forward = tableRoot.transform.forward;
                m_tables.Add(new PropInfo
                {
                    Transform = tableRoot.transform,
                    Footprint = footprint,
                    SurfaceY = tableHeight + 0.055f,
                    Forward = forward,
                    Size = tableSize,
                    Name = tableRoot.name
                });
                m_navigationObstacles.Add(footprint);
                return;
            }

            Debug.LogWarning("Could not create a non-overlapping folding table.");
        }

        private void TryCreateRandomShelf(int shelfIndex)
        {
            var shelfMaterial = CreateMaterial(new Color(0.9f, 0.91f, 0.9f), "Wall Shelf");
            var bracketMaterial = CreateMaterial(new Color(0.45f, 0.46f, 0.48f), "Shelf Bracket");
            var size = RoomSizeMeters;
            var wallOptions = new[] { WallSide.North, WallSide.East, WallSide.West };
            var wall = wallOptions[UnityEngine.Random.Range(0, wallOptions.Length)];
            var length = UnityEngine.Random.Range(1.05f, 2.05f);
            var depth = UnityEngine.Random.Range(0.28f, 0.38f);
            var tiers = UnityEngine.Random.Range(1, 4);
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;
            var sideOffset = UnityEngine.Random.Range(-1.65f, 1.65f);
            const float wallMountInset = 0.012f;

            Vector3 position;
            Quaternion rotation;
            Vector3 inward;
            switch (wall)
            {
                case WallSide.North:
                    position = new Vector3(sideOffset, 0f, halfDepth - wallMountInset);
                    rotation = Quaternion.identity;
                    inward = Vector3.back;
                    break;
                case WallSide.South:
                    position = new Vector3(sideOffset, 0f, -halfDepth + wallMountInset);
                    rotation = Quaternion.Euler(0f, 180f, 0f);
                    inward = Vector3.forward;
                    break;
                case WallSide.East:
                    position = new Vector3(halfWidth - wallMountInset, 0f, sideOffset);
                    rotation = Quaternion.Euler(0f, 90f, 0f);
                    inward = Vector3.left;
                    break;
                default:
                    position = new Vector3(-halfWidth + wallMountInset, 0f, sideOffset);
                    rotation = Quaternion.Euler(0f, -90f, 0f);
                    inward = Vector3.right;
                    break;
            }

            var shelfRoot = new GameObject($"Random Wall Shelf {shelfIndex + 1}");
            shelfRoot.transform.SetParent(m_scenarioRoot, false);
            shelfRoot.transform.SetPositionAndRotation(position, rotation);
            m_spawnedObjects.Add(shelfRoot);

            var surfaceHeights = new List<float>(tiers);
            var shelfSlotSize = new Vector2(length, depth);
            var shelfSlotCenter = new Vector3(0f, 0f, -depth * 0.64f);

            for (var tier = 0; tier < tiers; tier++)
            {
                var surfaceY = 1.05f + tier * 0.37f + UnityEngine.Random.Range(-0.04f, 0.04f);
                if (TryCreateShelfAssetTier(shelfRoot.transform, tier, surfaceY, length, depth, out var shelfBounds))
                {
                    shelfSlotSize = new Vector2(
                        Mathf.Max(0.2f, shelfBounds.size.x - 0.16f),
                        Mathf.Max(0.12f, shelfBounds.size.z - 0.08f));
                    shelfSlotCenter = new Vector3(shelfBounds.center.x, 0f, shelfBounds.center.z - shelfBounds.size.z * 0.14f);
                    surfaceHeights.Add(shelfBounds.max.y + m_shelfSurfaceClearance);
                    continue;
                }

                const float shelfThickness = 0.055f;
                var boardCenterY = surfaceY - shelfThickness * 0.5f;
                CreatePropCube(shelfRoot.transform, "Shelf Board", new Vector3(0f, boardCenterY, -depth * 0.5f), new Vector3(length, shelfThickness, depth), shelfMaterial);
                CreatePropCube(shelfRoot.transform, "Shelf Bracket", new Vector3(-length * 0.36f, surfaceY - 0.12f, -depth * 0.38f), new Vector3(0.035f, 0.22f, 0.035f), bracketMaterial);
                CreatePropCube(shelfRoot.transform, "Shelf Bracket", new Vector3(length * 0.36f, surfaceY - 0.12f, -depth * 0.38f), new Vector3(0.035f, 0.22f, 0.035f), bracketMaterial);
                AddShelfSurfaceCollider(shelfRoot.transform, new Bounds(new Vector3(0f, boardCenterY, -depth * 0.5f), new Vector3(length, shelfThickness, depth)), tier);
                surfaceHeights.Add(surfaceY + m_shelfSurfaceClearance);
            }

            m_shelves.Add(new PropInfo
            {
                Transform = shelfRoot.transform,
                Footprint = new ObstacleRect
                {
                    Center = new Vector2(position.x, position.z),
                    HalfExtents = new Vector2(length * 0.5f, depth * 0.5f),
                    YawDegrees = rotation.eulerAngles.y
                },
                SurfaceY = surfaceHeights.Count > 0 ? surfaceHeights[0] : 1.05f,
                Forward = inward,
                Size = shelfSlotSize,
                TierCount = surfaceHeights.Count,
                ShelfSurfaceHeights = surfaceHeights,
                ShelfSlotSize = shelfSlotSize,
                ShelfSlotCenterLocal = shelfSlotCenter,
                Wall = wall,
                Name = shelfRoot.name
            });
        }

        private bool TryCreateShelfAssetTier(Transform shelfRoot, int tierIndex, float surfaceY, float targetLength, float targetDepth, out Bounds localBounds)
        {
            localBounds = default;
            if (m_wallShelfPrefab == null)
            {
                return false;
            }

            var holder = new GameObject($"Shelf Asset Tier {tierIndex + 1}");
            holder.transform.SetParent(shelfRoot, false);
            var visual = Instantiate(m_wallShelfPrefab, holder.transform, false);
            visual.name = m_wallShelfPrefab.name;

            RemoveColliders(holder);

            if (!TryGetLocalRendererBounds(shelfRoot, holder, out localBounds))
            {
                Destroy(holder);
                return false;
            }

            var scale = holder.transform.localScale;
            if (localBounds.size.x > 0.001f)
            {
                scale.x *= targetLength / localBounds.size.x;
            }

            if (localBounds.size.z > 0.001f)
            {
                scale.z *= targetDepth / localBounds.size.z;
            }

            holder.transform.localScale = scale;
            TryGetLocalRendererBounds(shelfRoot, holder, out localBounds);
            holder.transform.localPosition -= new Vector3(localBounds.center.x, 0f, localBounds.max.z);
            TryGetLocalRendererBounds(shelfRoot, holder, out localBounds);
            holder.transform.localPosition += Vector3.up * (surfaceY - localBounds.max.y);
            TryGetLocalRendererBounds(shelfRoot, holder, out localBounds);
            if (!AddShelfMeshColliders(holder))
            {
                AddShelfSurfaceCollider(shelfRoot, localBounds, tierIndex);
            }

            return true;
        }

        private static void RemoveColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
        }

        private static bool AddShelfMeshColliders(GameObject root)
        {
            var addedCollider = false;
            foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null)
                {
                    continue;
                }

                var meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = false;
                addedCollider = true;
            }

            return addedCollider;
        }

        private static bool TryGetLocalRendererBounds(Transform localSpace, GameObject root, out Bounds localBounds)
        {
            localBounds = default;
            var hasBounds = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var bounds = renderer.bounds;
                var min = bounds.min;
                var max = bounds.max;
                var corners = new[]
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z)
                };

                foreach (var corner in corners)
                {
                    var localCorner = localSpace.InverseTransformPoint(corner);
                    if (!hasBounds)
                    {
                        localBounds = new Bounds(localCorner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(localCorner);
                    }
                }
            }

            return hasBounds;
        }

        private static void AddShelfSurfaceCollider(Transform shelfRoot, Bounds localBounds, int tierIndex)
        {
            var colliderObject = new GameObject($"Shelf Surface Collider {tierIndex + 1}");
            colliderObject.transform.SetParent(shelfRoot, false);
            colliderObject.transform.localPosition = new Vector3(localBounds.center.x, localBounds.max.y - 0.025f, localBounds.center.z);

            var collider = colliderObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(
                Mathf.Max(0.05f, localBounds.size.x),
                0.05f,
                Mathf.Max(0.05f, localBounds.size.z));
        }

        private void CreatePropCube(Transform parent, string cubeName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = cubeName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;

            var renderer = cube.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private void BuildSpawnSlots()
        {
            m_spawnSlots.Clear();
            BuildFloorSlots();
            BuildTableSlots();
            BuildShelfSlots();
            Shuffle(m_spawnSlots);
        }

        private void BuildFloorSlots()
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;
            const float step = 0.56f;

            for (var x = -halfWidth + 0.52f; x <= halfWidth - 0.52f; x += step)
            {
                for (var z = -halfDepth + 0.52f; z <= halfDepth - 0.52f; z += step)
                {
                    var candidate = new Vector3(x + UnityEngine.Random.Range(-0.08f, 0.08f), 0f, z + UnityEngine.Random.Range(-0.08f, 0.08f));
                    if (IsInCentralAisle(candidate) || HorizontalDistance(candidate, PlayerStartPosition) <= m_playerClearance)
                    {
                        continue;
                    }

                    if (IsInsideAnyNavigationObstacle(candidate, 0.38f))
                    {
                        continue;
                    }

                    var forward = Vector3.zero - candidate;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.001f)
                    {
                        forward = Vector3.forward;
                    }

                    m_spawnSlots.Add(new SpawnSlot
                    {
                        Position = candidate,
                        LabelForward = forward.normalized,
                        Surface = SurfaceKind.Floor,
                        AllowedSizes = new[] { BarrelSize.Gallon55, BarrelSize.Gallon30, BarrelSize.Gallon5 },
                        AllowSideways = true,
                        SurfaceYaw = 0f,
                        SourceName = "floor"
                    });
                }
            }
        }

        private void BuildTableSlots()
        {
            foreach (var table in m_tables)
            {
                var localPositions = new List<Vector3>
                {
                    Vector3.zero,
                    new Vector3(-table.Size.x * 0.28f, 0f, 0f),
                    new Vector3(table.Size.x * 0.28f, 0f, 0f),
                    new Vector3(0f, 0f, -table.Size.y * 0.24f),
                    new Vector3(0f, 0f, table.Size.y * 0.24f),
                    new Vector3(-table.Size.x * 0.28f, 0f, -table.Size.y * 0.24f),
                    new Vector3(table.Size.x * 0.28f, 0f, table.Size.y * 0.24f)
                };

                foreach (var localPosition in localPositions)
                {
                    var worldPosition = table.Transform.TransformPoint(localPosition);
                    worldPosition.y = table.SurfaceY;
                    var forward = Vector3.zero - worldPosition;
                    forward.y = 0f;
                    if (forward.sqrMagnitude < 0.001f)
                    {
                        forward = table.Transform.forward;
                    }

                    m_spawnSlots.Add(new SpawnSlot
                    {
                        Position = worldPosition,
                        LabelForward = forward.normalized,
                        Surface = SurfaceKind.Table,
                        AllowedSizes = localPosition == Vector3.zero
                            ? new[] { BarrelSize.Gallon55, BarrelSize.Gallon30, BarrelSize.Gallon5 }
                            : new[] { BarrelSize.Gallon30, BarrelSize.Gallon5 },
                        AllowSideways = localPosition == Vector3.zero && UnityEngine.Random.value > 0.45f,
                        SurfaceYaw = table.Transform.eulerAngles.y,
                        SourceName = table.Name
                    });
                }
            }
        }

        private void BuildShelfSlots()
        {
            foreach (var shelf in m_shelves)
            {
                var surfaceHeights = shelf.ShelfSurfaceHeights != null && shelf.ShelfSurfaceHeights.Count > 0
                    ? shelf.ShelfSurfaceHeights
                    : null;
                var tierCount = surfaceHeights != null ? surfaceHeights.Count : shelf.TierCount;
                var usableSize = shelf.ShelfSlotSize.sqrMagnitude > 0.001f ? shelf.ShelfSlotSize : shelf.Size;
                var slotCenter = shelf.ShelfSlotCenterLocal;

                for (var tier = 0; tier < tierCount; tier++)
                {
                    var surfaceY = surfaceHeights != null
                        ? surfaceHeights[tier]
                        : 1.05f + tier * 0.37f + m_shelfSurfaceClearance;
                    var count = Mathf.Max(1, Mathf.FloorToInt((usableSize.x - 0.18f) / m_shelfBarrelSpacing));
                    for (var i = 0; i < count; i++)
                    {
                        var t = count == 1 ? 0.5f : i / (float)(count - 1);
                        var x = Mathf.Lerp(-usableSize.x * 0.5f, usableSize.x * 0.5f, t);
                        var localPosition = new Vector3(slotCenter.x + x, surfaceY, slotCenter.z);
                        var worldPosition = shelf.Transform.TransformPoint(localPosition);

                        m_spawnSlots.Add(new SpawnSlot
                        {
                            Position = worldPosition,
                            LabelForward = shelf.Forward.normalized,
                            Surface = SurfaceKind.Shelf,
                            AllowedSizes = new[] { BarrelSize.Gallon5 },
                            AllowSideways = false,
                            SurfaceYaw = shelf.Transform.eulerAngles.y,
                            SourceName = shelf.Name
                        });
                    }
                }
            }
        }

        private ScenarioResult SpawnBarrelsForCurrentLayout(int requestedCount)
        {
            var result = new ScenarioResult();
            var specs = BuildBarrelSpecs();

            foreach (var slot in m_spawnSlots)
            {
                if (result.SpawnedCount >= requestedCount)
                {
                    break;
                }

                var sizeOptions = BuildWeightedSizeOptions(slot, specs);
                foreach (var size in sizeOptions)
                {
                    var spec = specs[size];
                    var radius = spec.Diameter * 0.5f;
                    var spacing = GetSpacing(slot.Surface);
                    if (!IsSlotSpacingSafe(slot, radius, spacing))
                    {
                        continue;
                    }

                    if (!IsSlotVisibilitySafe(slot, spec))
                    {
                        continue;
                    }

                    var isSideways = slot.AllowSideways && slot.Surface != SurfaceKind.Shelf && UnityEngine.Random.value < 0.28f;
                    var spawned = SpawnBarrel(slot, spec, isSideways);
                    m_spawnedBarrels.Add(spawned);
                    m_placedBarrels.Add(new PlacedBarrel
                    {
                        Position = slot.Position,
                        Radius = radius,
                        Height = spec.Height,
                        Size = spec.Size,
                        Surface = slot.Surface
                    });

                    if (slot.Surface == SurfaceKind.Floor)
                    {
                        m_navigationObstacles.Add(new ObstacleRect
                        {
                            Center = new Vector2(slot.Position.x, slot.Position.z),
                            HalfExtents = new Vector2(radius, radius),
                            YawDegrees = 0f
                        });
                    }

                    result.SpawnedCount++;
                    break;
                }
            }

            return result;
        }

        private Dictionary<BarrelSize, BarrelSpec> BuildBarrelSpecs()
        {
            return new Dictionary<BarrelSize, BarrelSpec>
            {
                [BarrelSize.Gallon55] = new BarrelSpec
                {
                    Size = BarrelSize.Gallon55,
                    Label = "55 GAL",
                    Prefab = m_barrelPrefabs.barrel55GallonPrefab,
                    ModelScale = m_barrel55ModelScale,
                    Diameter = 0.58f,
                    Height = 0.9f,
                    LabelHeight = 0.46f,
                    BodyColor = new Color(0.17f, 0.28f, 0.42f),
                    SelectionWeight = 0.14f
                },
                [BarrelSize.Gallon30] = new BarrelSpec
                {
                    Size = BarrelSize.Gallon30,
                    Label = "30 GAL",
                    Prefab = m_barrelPrefabs.barrel30GallonPrefab,
                    ModelScale = m_barrel30ModelScale,
                    Diameter = 0.48f,
                    Height = 0.72f,
                    LabelHeight = 0.36f,
                    BodyColor = new Color(0.32f, 0.35f, 0.38f),
                    SelectionWeight = 0.26f
                },
                [BarrelSize.Gallon5] = new BarrelSpec
                {
                    Size = BarrelSize.Gallon5,
                    Label = "5 GAL",
                    Prefab = m_barrelPrefabs.barrel5GallonPrefab,
                    ModelScale = m_barrel5ModelScale,
                    Diameter = 0.28f,
                    Height = 0.36f,
                    LabelHeight = 0.2f,
                    BodyColor = new Color(0.93f, 0.83f, 0.22f),
                    SelectionWeight = 0.6f
                }
            };
        }

        private List<BarrelSize> BuildWeightedSizeOptions(SpawnSlot slot, Dictionary<BarrelSize, BarrelSpec> specs)
        {
            var options = new List<BarrelSize>();
            var available = new List<BarrelSize>(slot.AllowedSizes);
            while (available.Count > 0)
            {
                var totalWeight = 0f;
                foreach (var size in available)
                {
                    totalWeight += specs[size].SelectionWeight;
                }

                var roll = UnityEngine.Random.Range(0f, totalWeight);
                for (var i = 0; i < available.Count; i++)
                {
                    roll -= specs[available[i]].SelectionWeight;
                    if (roll > 0f)
                    {
                        continue;
                    }

                    options.Add(available[i]);
                    available.RemoveAt(i);
                    break;
                }
            }

            return options;
        }

        private BarrelInstance SpawnBarrel(SpawnSlot slot, BarrelSpec spec, bool isSideways)
        {
            EnsureScenarioRoot();
            var labelForward = slot.LabelForward.sqrMagnitude > 0.001f ? slot.LabelForward.normalized : Vector3.forward;
            var yawRotation = Quaternion.LookRotation(labelForward, Vector3.up);
            var finalRotation = isSideways ? yawRotation * Quaternion.Euler(0f, 0f, 90f) : yawRotation;
            var verticalOffset = isSideways ? spec.Diameter * 0.5f : spec.Height * 0.5f;
            var position = slot.Position + Vector3.up * verticalOffset;

            GameObject barrelObject;
            if (spec.Prefab != null)
            {
                barrelObject = Instantiate(spec.Prefab, position, yawRotation, m_scenarioRoot);
                barrelObject.transform.localScale = spec.ModelScale;
                FitPrefabToPhysicalSize(barrelObject, spec);
                barrelObject.transform.rotation = finalRotation;
                AlignRendererBottomToSurface(barrelObject, slot.Position.y);
            }
            else
            {
                barrelObject = CreatePlaceholderBarrel(position, finalRotation, spec, isSideways);
            }

            barrelObject.name = $"{spec.Label} Barrel ({slot.SourceName})";
            m_spawnedObjects.Add(barrelObject);
            EnsureApproximateCollider(barrelObject, spec, isSideways);

            var barrel = barrelObject.GetComponent<BarrelInstance>();
            if (barrel == null)
            {
                barrel = barrelObject.AddComponent<BarrelInstance>();
            }

            barrel.Initialize(spec.Label, GetRandomRadiationCount(), false);
            return barrel;
        }

        private GameObject CreatePlaceholderBarrel(Vector3 position, Quaternion rotation, BarrelSpec spec, bool isSideways)
        {
            var root = new GameObject($"Placeholder {spec.Label} Barrel");
            root.transform.SetParent(m_scenarioRoot, false);
            root.transform.SetPositionAndRotation(position, rotation);

            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Barrel Body";
            cylinder.transform.SetParent(root.transform, false);
            cylinder.transform.localScale = new Vector3(spec.Diameter, spec.Height * 0.5f, spec.Diameter);

            var renderer = cylinder.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = CreateMaterial(spec.BodyColor, $"{spec.Label} Placeholder Material");
            }

            return root;
        }

        private static void FitPrefabToPhysicalSize(GameObject barrelObject, BarrelSpec spec)
        {
            if (!TryGetRendererBounds(barrelObject, out var bounds))
            {
                return;
            }

            var modelHeight = Mathf.Max(bounds.size.y, 0.001f);
            var modelDiameter = Mathf.Max(bounds.size.x, bounds.size.z, 0.001f);
            var targetScale = barrelObject.transform.localScale;
            targetScale.x *= spec.Diameter / modelDiameter;
            targetScale.y *= spec.Height / modelHeight;
            targetScale.z *= spec.Diameter / modelDiameter;
            barrelObject.transform.localScale = targetScale;
        }

        private static void AlignRendererBottomToSurface(GameObject barrelObject, float surfaceY)
        {
            if (!TryGetRendererBounds(barrelObject, out var bounds))
            {
                return;
            }

            barrelObject.transform.position += Vector3.up * (surfaceY - bounds.min.y);
        }

        private static void EnsureApproximateCollider(GameObject barrelObject, BarrelSpec spec, bool isSideways)
        {
            if (barrelObject.GetComponentInChildren<Collider>() != null)
            {
                return;
            }

            var collider = barrelObject.AddComponent<BoxCollider>();
            var lossyScale = barrelObject.transform.lossyScale;
            lossyScale.x = Mathf.Approximately(lossyScale.x, 0f) ? 1f : lossyScale.x;
            lossyScale.y = Mathf.Approximately(lossyScale.y, 0f) ? 1f : lossyScale.y;
            lossyScale.z = Mathf.Approximately(lossyScale.z, 0f) ? 1f : lossyScale.z;

            var worldSize = new Vector3(spec.Diameter, spec.Height, spec.Diameter);
            collider.size = new Vector3(worldSize.x / lossyScale.x, worldSize.y / lossyScale.y, worldSize.z / lossyScale.z);

            if (TryGetRendererBounds(barrelObject, out var bounds))
            {
                collider.center = barrelObject.transform.InverseTransformPoint(bounds.center);
            }
        }

        private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            bounds = default;
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(renderer.bounds);
            }

            return hasBounds;
        }

        private bool IsSlotSpacingSafe(SpawnSlot slot, float radius, float spacing)
        {
            foreach (var placed in m_placedBarrels)
            {
                if (slot.Surface != placed.Surface && (slot.Surface == SurfaceKind.Shelf || placed.Surface == SurfaceKind.Shelf))
                {
                    continue;
                }

                var required = Mathf.Max(spacing, radius + placed.Radius + 0.05f);
                if (HorizontalDistance(slot.Position, placed.Position) < required && Mathf.Abs(slot.Position.y - placed.Position.y) < 0.35f)
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsSlotVisibilitySafe(SpawnSlot slot, BarrelSpec spec)
        {
            if (slot.Surface == SurfaceKind.Shelf || slot.Surface == SurfaceKind.Table)
            {
                return true;
            }

            var observer = Vector3.zero;
            var target = new Vector3(slot.Position.x, 0f, slot.Position.z);
            var toTarget = target - observer;
            var targetDistance = toTarget.magnitude;
            if (targetDistance < 0.1f)
            {
                return true;
            }

            var direction = toTarget / targetDistance;
            foreach (var placed in m_placedBarrels)
            {
                if (placed.Surface != SurfaceKind.Floor)
                {
                    continue;
                }

                var existing = new Vector3(placed.Position.x, 0f, placed.Position.z);
                var existingDistance = Vector3.Dot(existing - observer, direction);
                if (existingDistance <= 0.1f || existingDistance >= targetDistance - 0.1f)
                {
                    continue;
                }

                var closest = observer + direction * existingDistance;
                var sideDistance = Vector3.Distance(closest, existing);
                if (sideDistance < placed.Radius + spec.Diameter * 0.25f)
                {
                    return false;
                }
            }

            return true;
        }

        private float GetSpacing(SurfaceKind surface)
        {
            return surface switch
            {
                SurfaceKind.Table => m_tableBarrelSpacing,
                SurfaceKind.Shelf => m_shelfBarrelSpacing,
                _ => m_floorBarrelSpacing
            };
        }

        private void HandleLocomotion()
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            if (m_enableSmoothMove)
            {
                var moveAxis = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
                if (moveAxis.sqrMagnitude > m_thumbstickDeadzone * m_thumbstickDeadzone)
                {
                    var cameraForward = m_cameraTransform != null ? m_cameraTransform.forward : m_locomotionRoot.forward;
                    cameraForward.y = 0f;
                    cameraForward.Normalize();
                    var cameraRight = m_cameraTransform != null ? m_cameraTransform.right : m_locomotionRoot.right;
                    cameraRight.y = 0f;
                    cameraRight.Normalize();

                    var moveDirection = cameraForward * moveAxis.y + cameraRight * moveAxis.x;
                    if (moveDirection.sqrMagnitude > 1f)
                    {
                        moveDirection.Normalize();
                    }

                    var nextPosition = m_locomotionRoot.position + moveDirection * (m_smoothMoveSpeed * Time.deltaTime);
                    MoveRigTo(nextPosition);
                }
            }

            var turnAxis = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
            if (Time.time >= m_nextSnapTurnTime && Mathf.Abs(turnAxis.x) > 0.72f)
            {
                SnapTurn(turnAxis.x > 0f ? m_snapTurnDegrees : -m_snapTurnDegrees);
                m_nextSnapTurnTime = Time.time + m_snapTurnCooldown;
            }

            if (m_enableTeleport)
            {
                UpdateTeleportTarget();
                if (m_hasValidTeleportTarget && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    MoveRigTo(m_currentTeleportTarget);
                }
            }
        }

        private void UpdateTeleportTarget()
        {
            EnsureTeleportMarker();

            var ray = GetTeleportRay();
            var floorPlane = new Plane(Vector3.up, Vector3.zero);
            m_hasValidTeleportTarget = false;
            if (floorPlane.Raycast(ray, out var hitDistance))
            {
                var candidate = ray.GetPoint(hitDistance);
                candidate.y = 0f;
                if (hitDistance > 0.25f && hitDistance < 8f && IsWalkablePosition(candidate) && !TeleportCrossesClosedDoor(ray.origin, candidate))
                {
                    m_currentTeleportTarget = candidate;
                    m_hasValidTeleportTarget = true;
                }
            }

            m_teleportMarker.SetActive(m_enableTeleport);
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
                var anchor = m_cameraRig.rightControllerAnchor;
                return new Ray(anchor.position, anchor.forward);
            }

            var cameraTransform = m_cameraTransform != null ? m_cameraTransform : Camera.main != null ? Camera.main.transform : transform;
            return new Ray(cameraTransform.position, cameraTransform.forward);
        }

        private void EnsureTeleportMarker()
        {
            if (m_teleportMarker != null)
            {
                return;
            }

            m_validTeleportMaterial = CreateMaterial(new Color(0.1f, 0.9f, 0.35f, 0.75f), "Valid Teleport");
            m_invalidTeleportMaterial = CreateMaterial(new Color(0.9f, 0.1f, 0.1f, 0.75f), "Invalid Teleport");
            m_teleportMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            m_teleportMarker.name = "Teleport Target";
            m_teleportMarker.transform.localScale = new Vector3(0.28f, 0.012f, 0.28f);
            m_teleportMarkerRenderer = m_teleportMarker.GetComponent<Renderer>();
            var collider = m_teleportMarker.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private void MoveRigTo(Vector3 worldPosition)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var clamped = ClampToRoom(worldPosition);
            clamped.y = m_locomotionRoot == m_cameraTransform && m_cameraRig == null ? m_defaultEyeHeight : 0f;
            if (IsWalkablePosition(clamped))
            {
                m_locomotionRoot.position = clamped;
            }
        }

        private void HandleDoorInteraction()
        {
            if (m_doorPivot == null || Time.time < m_nextDoorToggleTime)
            {
                return;
            }

            var leftGripPressed = OVRInput.GetDown(OVRInput.RawButton.LHandTrigger);
            var rightGripPressed = OVRInput.GetDown(OVRInput.RawButton.RHandTrigger);
            if (!leftGripPressed && !rightGripPressed)
            {
                return;
            }

            var leftNearKnob = leftGripPressed && IsControllerNearDoorKnob(m_cameraRig != null ? m_cameraRig.leftControllerAnchor : null);
            var rightNearKnob = rightGripPressed && IsControllerNearDoorKnob(m_cameraRig != null ? m_cameraRig.rightControllerAnchor : null);
            if (!leftNearKnob && !rightNearKnob)
            {
                return;
            }

            m_isDoorOpen = !m_isDoorOpen;
            m_nextDoorToggleTime = Time.time + m_doorToggleCooldown;
        }

        private bool IsControllerNearDoorKnob(Transform controllerAnchor)
        {
            if (controllerAnchor == null)
            {
                return false;
            }

            var radius = Mathf.Max(0.05f, m_doorKnobInteractionRadius);
            return IsPointNearTransform(controllerAnchor.position, m_labDoorKnob, radius)
                   || IsPointNearTransform(controllerAnchor.position, m_spawnDoorKnob, radius);
        }

        private static bool IsPointNearTransform(Vector3 point, Transform target, float radius)
        {
            return target != null && Vector3.Distance(point, target.position) <= radius;
        }

        private void UpdateDoorSwing()
        {
            if (m_doorPivot == null)
            {
                return;
            }

            var targetAngle = m_isDoorOpen ? m_doorOpenAngle : 0f;
            m_doorCurrentAngle = Mathf.MoveTowardsAngle(m_doorCurrentAngle, targetAngle, m_doorSwingSpeed * Time.deltaTime);
            m_doorPivot.localRotation = Quaternion.Euler(0f, m_doorCurrentAngle, 0f);
        }

        private void SnapTurn(float yawDegrees)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var pivot = m_cameraTransform != null ? m_cameraTransform.position : m_locomotionRoot.position;
            m_locomotionRoot.RotateAround(pivot, Vector3.up, yawDegrees);
            MoveRigTo(m_locomotionRoot.position);
        }

        private bool IsWalkablePosition(Vector3 position)
        {
            var clamped = ClampToRoom(position);
            if (HorizontalDistance(clamped, position) > 0.02f)
            {
                return false;
            }

            if (!IsInsideWalkableFloorPlan(position, m_playerRadius + 0.08f))
            {
                return false;
            }

            if (!m_isDoorOpen && IsInsideClosedDoorObstacle(position, m_playerRadius + 0.08f))
            {
                return false;
            }

            foreach (var obstacle in m_navigationObstacles)
            {
                if (IsPointInsideRect(position, obstacle, m_playerRadius + 0.08f))
                {
                    return false;
                }
            }

            return true;
        }

        private Vector3 ClampToRoom(Vector3 position)
        {
            if (IsInsideWalkableFloorPlan(position, m_playerRadius + 0.08f))
            {
                return position;
            }

            var margin = m_playerRadius + 0.08f;
            var labCandidate = ClampToRect(position, Vector3.zero, RoomSizeMeters, margin);
            var spawnCandidate = ClampToRect(position, SpawnRoomCenter, SpawnRoomSizeMeters, margin);
            var doorCandidate = GetDoorwayCenter();
            var doorHalfWidth = Mathf.Max(0.05f, m_doorwayWidth * 0.5f - margin);
            doorCandidate.x = Mathf.Clamp(position.x, -doorHalfWidth, doorHalfWidth);

            var bestCandidate = labCandidate;
            var bestDistance = HorizontalDistance(position, labCandidate);
            var spawnDistance = HorizontalDistance(position, spawnCandidate);
            if (spawnDistance < bestDistance)
            {
                bestCandidate = spawnCandidate;
                bestDistance = spawnDistance;
            }

            var doorDistance = HorizontalDistance(position, doorCandidate);
            if (doorDistance < bestDistance)
            {
                bestCandidate = doorCandidate;
            }

            bestCandidate.y = position.y;
            return bestCandidate;
        }

        private bool IsInsideWalkableFloorPlan(Vector3 position, float margin)
        {
            var insideRoom = IsInsideLabRoom(position, margin) || IsInsideSpawnRoom(position, margin);
            return insideRoom && !IsBlockedBySharedWall(position, margin);
        }

        private bool IsInsideClosedDoorObstacle(Vector3 position, float padding)
        {
            var doorwayCenter = GetDoorwayCenter();
            var halfWidth = Mathf.Max(0.1f, m_doorwayWidth * 0.5f);
            var halfDepth = m_wallThickness * 0.5f;
            return Mathf.Abs(position.x - doorwayCenter.x) <= halfWidth + padding
                   && Mathf.Abs(position.z - doorwayCenter.z) <= halfDepth + padding * 0.5f;
        }

        private bool TeleportCrossesClosedDoor(Vector3 from, Vector3 to)
        {
            if (m_isDoorOpen)
            {
                return false;
            }

            var doorZ = GetDoorwayCenter().z;
            var fromSide = from.z - doorZ;
            var toSide = to.z - doorZ;
            if (Mathf.Approximately(fromSide, 0f) || Mathf.Approximately(toSide, 0f) || Mathf.Sign(fromSide) == Mathf.Sign(toSide))
            {
                return false;
            }

            var segmentZ = to.z - from.z;
            if (Mathf.Approximately(segmentZ, 0f))
            {
                return false;
            }

            var t = Mathf.Clamp01((doorZ - from.z) / segmentZ);
            var crossingX = Mathf.Lerp(from.x, to.x, t);
            return Mathf.Abs(crossingX) <= m_doorwayWidth * 0.5f + m_playerRadius;
        }

        private bool IsInsideLabRoom(Vector3 position, float margin)
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f - margin;
            var halfDepth = size.y * 0.5f;
            return Mathf.Abs(position.x) <= halfWidth
                   && position.z <= halfDepth - margin
                   && position.z >= -halfDepth;
        }

        private bool IsInsideSpawnRoom(Vector3 position, float margin)
        {
            var center = SpawnRoomCenter;
            var size = SpawnRoomSizeMeters;
            var halfWidth = size.x * 0.5f - margin;
            var halfDepth = size.y * 0.5f - margin;
            var northEdge = center.z + size.y * 0.5f;
            return Mathf.Abs(position.x - center.x) <= halfWidth
                   && position.z >= center.z - halfDepth
                   && position.z <= northEdge;
        }

        private bool IsBlockedBySharedWall(Vector3 position, float margin)
        {
            var wallZ = -RoomSizeMeters.y * 0.5f;
            var doorHalfWidth = Mathf.Max(0.05f, m_doorwayWidth * 0.5f - margin * 0.3f);
            return Mathf.Abs(position.z - wallZ) < margin && Mathf.Abs(position.x) > doorHalfWidth;
        }

        private static Vector3 ClampToRect(Vector3 position, Vector3 center, Vector2 size, float margin)
        {
            var halfWidth = Mathf.Max(0.05f, size.x * 0.5f - margin);
            var halfDepth = Mathf.Max(0.05f, size.y * 0.5f - margin);
            return new Vector3(
                Mathf.Clamp(position.x, center.x - halfWidth, center.x + halfWidth),
                position.y,
                Mathf.Clamp(position.z, center.z - halfDepth, center.z + halfDepth));
        }

        private Vector3 GetDoorwayCenter()
        {
            return new Vector3(0f, 0f, -RoomSizeMeters.y * 0.5f);
        }

        private bool IsFootprintAllowed(ObstacleRect footprint, float padding)
        {
            if (RectTouchesCentralAisle(footprint, padding))
            {
                return false;
            }

            if (HorizontalDistance(new Vector3(footprint.Center.x, 0f, footprint.Center.y), PlayerStartPosition) < m_playerClearance)
            {
                return false;
            }

            foreach (var existing in m_navigationObstacles)
            {
                if (RectsOverlap(footprint, existing, padding))
                {
                    return false;
                }
            }

            return IsRectInsideRoom(footprint, padding);
        }

        private bool RectTouchesCentralAisle(ObstacleRect rect, float padding)
        {
            var samples = GetRectCorners(rect, padding);
            foreach (var sample in samples)
            {
                if (Mathf.Abs(sample.x) < m_centralAisleWidth * 0.5f || Mathf.Abs(sample.y) < m_centralAisleWidth * 0.5f)
                {
                    return true;
                }
            }

            return Mathf.Abs(rect.Center.x) < m_centralAisleWidth * 0.5f || Mathf.Abs(rect.Center.y) < m_centralAisleWidth * 0.5f;
        }

        private bool IsInCentralAisle(Vector3 position)
        {
            return Mathf.Abs(position.x) < m_centralAisleWidth * 0.5f || Mathf.Abs(position.z) < m_centralAisleWidth * 0.5f;
        }

        private bool IsInsideAnyNavigationObstacle(Vector3 position, float padding)
        {
            foreach (var obstacle in m_navigationObstacles)
            {
                if (IsPointInsideRect(position, obstacle, padding))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPointInsideRect(Vector3 point, ObstacleRect rect, float padding)
        {
            var local = Rotate(new Vector2(point.x, point.z) - rect.Center, -rect.YawDegrees);
            return Mathf.Abs(local.x) <= rect.HalfExtents.x + padding && Mathf.Abs(local.y) <= rect.HalfExtents.y + padding;
        }

        private static bool RectsOverlap(ObstacleRect a, ObstacleRect b, float padding)
        {
            foreach (var corner in GetRectCorners(a, padding))
            {
                if (IsPointInsideRect(new Vector3(corner.x, 0f, corner.y), b, padding))
                {
                    return true;
                }
            }

            foreach (var corner in GetRectCorners(b, padding))
            {
                if (IsPointInsideRect(new Vector3(corner.x, 0f, corner.y), a, padding))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsRectInsideRoom(ObstacleRect rect, float padding)
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f - padding;
            var halfDepth = size.y * 0.5f - padding;
            foreach (var corner in GetRectCorners(rect, padding))
            {
                if (Mathf.Abs(corner.x) > halfWidth || Mathf.Abs(corner.y) > halfDepth)
                {
                    return false;
                }
            }

            return true;
        }

        private static Vector2[] GetRectCorners(ObstacleRect rect, float padding)
        {
            var half = rect.HalfExtents + Vector2.one * padding;
            var corners = new[]
            {
                new Vector2(-half.x, -half.y),
                new Vector2(half.x, -half.y),
                new Vector2(half.x, half.y),
                new Vector2(-half.x, half.y)
            };

            for (var i = 0; i < corners.Length; i++)
            {
                corners[i] = rect.Center + Rotate(corners[i], rect.YawDegrees);
            }

            return corners;
        }

        private static Vector2 Rotate(Vector2 point, float yawDegrees)
        {
            var radians = yawDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        }

        private Vector3 GetRandomQuadrantPosition(float margin)
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f - margin;
            var halfDepth = size.y * 0.5f - margin;
            var xSign = UnityEngine.Random.value > 0.5f ? 1f : -1f;
            var zSign = UnityEngine.Random.value > 0.5f ? 1f : -1f;
            var x = UnityEngine.Random.Range(m_centralAisleWidth * 0.65f, halfWidth) * xSign;
            var z = UnityEngine.Random.Range(m_centralAisleWidth * 0.65f, halfDepth) * zSign;
            return new Vector3(x, 0f, z);
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void EnsureScenarioRoot()
        {
            if (m_scenarioRoot != null)
            {
                return;
            }

            var scenarioObject = new GameObject("Generated Randomized Scenario");
            scenarioObject.transform.SetParent(transform, false);
            m_scenarioRoot = scenarioObject.transform;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var swapIndex = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
            }
        }

        private int GetRandomRadiationCount()
        {
            if (m_radiationCountProfile != null)
            {
                return m_radiationCountProfile.GetRandomCount();
            }

            EnsureFallbackCountPool();
            return m_fallbackCountPool[UnityEngine.Random.Range(0, m_fallbackCountPool.Length)];
        }

        private void EnsureFallbackCountPool()
        {
            if (m_fallbackCountPool == null || m_fallbackCountPool.Length != Mathf.Max(1, m_fallbackCountPoolSize))
            {
                RebuildFallbackCountPool();
            }
        }

        private void RebuildFallbackCountPool()
        {
            var safePoolSize = Mathf.Max(1, m_fallbackCountPoolSize);
            var safeMin = Mathf.Min(m_fallbackMinCount, m_fallbackMaxCount);
            var safeMax = Mathf.Max(m_fallbackMinCount, m_fallbackMaxCount);
            m_fallbackCountPool = new int[safePoolSize];

            for (var i = 0; i < m_fallbackCountPool.Length; i++)
            {
                m_fallbackCountPool[i] = UnityEngine.Random.Range(safeMin, safeMax + 1);
            }
        }

        private Material CreateMaterial(Color color, string materialName)
        {
            var key = $"{materialName}_{color.r:0.000}_{color.g:0.000}_{color.b:0.000}_{color.a:0.000}";
            if (m_materialCache.TryGetValue(key, out var existingMaterial))
            {
                return existingMaterial;
            }

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
            {
                name = materialName,
                color = color
            };
            m_materialCache[key] = material;
            m_runtimeMaterials.Add(material);
            return material;
        }

        private void SetStatus(string message)
        {
            Debug.Log($"BasicVRRoomBarrelSpawner: {message}");
        }

        private void OnDestroy()
        {
            foreach (var runtimeMaterial in m_runtimeMaterials)
            {
                if (runtimeMaterial != null)
                {
                    Destroy(runtimeMaterial);
                }
            }

            m_runtimeMaterials.Clear();
            m_materialCache.Clear();
        }

        private void OnValidate()
        {
            m_roomSizeFeet.x = Mathf.Max(8f, m_roomSizeFeet.x);
            m_roomSizeFeet.y = Mathf.Max(8f, m_roomSizeFeet.y);
            m_spawnRoomSizeFeet.x = Mathf.Max(8f, m_spawnRoomSizeFeet.x);
            m_spawnRoomSizeFeet.y = Mathf.Max(8f, m_spawnRoomSizeFeet.y);
            m_doorwayWidth = Mathf.Min(m_doorwayWidth, Mathf.Min(RoomSizeMeters.x, SpawnRoomSizeMeters.x) - m_wallThickness * 2f);
            m_doorwayHeight = Mathf.Min(m_doorwayHeight, m_wallHeight - 0.1f);
            m_maxBarrels = Mathf.Max(m_minBarrels, m_maxBarrels);
            m_maxTables = Mathf.Max(m_minTables, m_maxTables);
            m_maxShelfUnits = Mathf.Max(m_minShelfUnits, m_maxShelfUnits);
            m_centralAisleWidth = Mathf.Min(m_centralAisleWidth, Mathf.Min(RoomSizeMeters.x, RoomSizeMeters.y) * 0.45f);
        }

        private void OnDrawGizmos()
        {
            if (!m_showEditorScalePreview)
            {
                return;
            }

            var size = RoomSizeMeters;
            var floorCenter = transform.position;

            Gizmos.color = new Color(0.1f, 0.45f, 1f, 0.9f);
            Gizmos.DrawWireCube(floorCenter + new Vector3(0f, 0.01f, 0f), new Vector3(size.x, 0.02f, size.y));
            Gizmos.color = new Color(0.1f, 0.45f, 1f, 0.3f);
            Gizmos.DrawWireCube(floorCenter + new Vector3(0f, m_wallHeight * 0.5f, 0f), new Vector3(size.x, m_wallHeight, size.y));

            var spawnSize = SpawnRoomSizeMeters;
            var spawnCenter = floorCenter + SpawnRoomCenter;
            Gizmos.color = new Color(0.9f, 0.55f, 0.18f, 0.9f);
            Gizmos.DrawWireCube(spawnCenter + new Vector3(0f, 0.01f, 0f), new Vector3(spawnSize.x, 0.02f, spawnSize.y));
            Gizmos.color = new Color(0.9f, 0.55f, 0.18f, 0.3f);
            Gizmos.DrawWireCube(spawnCenter + new Vector3(0f, m_wallHeight * 0.5f, 0f), new Vector3(spawnSize.x, m_wallHeight, spawnSize.y));
            Gizmos.color = new Color(0.55f, 0.27f, 0.08f, 0.9f);
            Gizmos.DrawWireCube(floorCenter + GetDoorwayCenter() + Vector3.up * (m_doorwayHeight * 0.5f), new Vector3(m_doorwayWidth, m_doorwayHeight, 0.04f));

            Gizmos.color = new Color(0f, 0.8f, 0.25f, 0.9f);
            var playerStart = PlayerStartPosition;
            Gizmos.DrawLine(floorCenter + playerStart, floorCenter + playerStart + Vector3.up * m_defaultEyeHeight);
            Gizmos.DrawWireSphere(floorCenter + playerStart + Vector3.up * m_defaultEyeHeight, 0.08f);

            Gizmos.color = new Color(1f, 0.8f, 0.05f, 0.75f);
            Gizmos.DrawWireCube(floorCenter, new Vector3(m_centralAisleWidth, 0.025f, size.y));
            Gizmos.DrawWireCube(floorCenter, new Vector3(size.x, 0.025f, m_centralAisleWidth));

        }

        private struct ScenarioResult
        {
            public int SpawnedCount;
        }
    }
}
