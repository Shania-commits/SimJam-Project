using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

// =============================================================================
// BasicVRRoomBarrelSpawner.cs
//
// PURPOSE:
//   The single MonoBehaviour that builds the entire "find the hidden radioactive
//   barrel" scene at runtime: an OVR camera rig with locomotion (smooth move, snap
//   turn, teleport), a two-room office shell with a swinging connecting door,
//   randomized props (folding tables + wall shelves), and a walkable, visibility-
//   checked field of 55/30/5-gallon drums that each carry a random radiation count
//   for the identiFINDER to read. Almost every object is code-generated.
//
// HOW TO CUSTOMIZE:
//   IMPORTANT: this component lives on the GameObject named "BasicVRRoomBarrelSpawner"
//   in the scene Assets/Simulation/Scenes/BasicVRRoom.unity. Every [SerializeField]
//   below already has a saved value in that scene's YAML, so changing a DEFAULT here
//   in C# does NOT change runtime behavior. To actually change these, edit the
//   BasicVRRoomBarrelSpawner component in the Unity Inspector (or the matching field
//   in the scene .unity file). The header groups map to Inspector sections:
//
//   - VR rig ("VR rig"): m_createOvrCameraRig (OVR rig vs plain camera),
//     m_playerStartPosition (spawn point; auto-clamped into the spawn room),
//     m_defaultEyeHeight (non-OVR camera height only).
//   - Room size ("Room scale"): m_roomSizeFeet / m_spawnRoomSizeFeet (footprints in
//     FEET, converted via FeetToMeters), m_wallHeight, m_wallThickness,
//     m_floorThickness, m_buildRoomGeometry (skip the shell entirely).
//   - Door ("Connecting door"): m_doorwayWidth/Height, m_doorOpenAngle (swing),
//     m_doorKnobInteractionRadius, m_doorToggleCooldown, m_doorSwingSpeed.
//   - Hands ("Controller hand visuals"): m_showControllerHands, m_controllerHandScale.
//   - Barrels ("Barrel prefabs"): m_barrelPrefabs (per-size prefab refs; null => a
//     colored placeholder cylinder is built instead), m_radiationCountProfile
//     (source of realistic counts), and m_barrel55/30/5ModelScale (pre-fit scale).
//   - Scenario randomization ("Scenario randomization"): m_minBarrels/m_maxBarrels
//     (drum count range), m_minTables/m_maxTables, m_minShelfUnits/m_maxShelfUnits,
//     m_layoutRetryCount (attempts to hit the requested count), and
//     m_useFixedSeed + m_fixedSeed for a reproducible layout.
//   - Spacing / walkability ("Visibility and walkability"): m_playerClearance,
//     m_playerRadius, m_centralAisleWidth (the cross-shaped clear aisle), and
//     m_floorBarrelSpacing / m_tableBarrelSpacing / m_shelfBarrelSpacing.
//   - Shelf asset ("Shelf asset"): m_wallShelfPrefab (null => primitive boards),
//     m_shelfSurfaceClearance.
//   - Locomotion ("Locomotion"): m_enableSmoothMove, m_enableTeleport,
//     m_smoothMoveSpeed, m_snapTurnDegrees, m_snapTurnCooldown, m_thumbstickDeadzone.
//   - Fallback counts ("Fallback counts"): m_fallbackCountPoolSize,
//     m_fallbackMinCount, m_fallbackMaxCount (used only when no RadiationCountProfile
//     is assigned).
//
//   HARDCODED (NOT serialized — edit the named method in this file, no Inspector):
//   - Per-drum physical size, color, and spawn-weight (bigger drums are rarer):
//     the BarrelSpec table in BuildBarrelSpecs().
//   - Debug controls in Update(): A/pinch regenerates the run, B clears it, X
//     re-rolls radiation counts.
//   - Floor grid spacing/jitter in BuildFloorSlots(); table slot layout in
//     BuildTableSlots(); shelf tier heights/spacing in TryCreateRandomShelf() and
//     BuildShelfSlots().
//   - Room/door/prop appearance: material colors and light intensity/range are
//     literals inside BuildRoomGeometry(), BuildFluorescentPanels(),
//     BuildHomeRoomProps(), CreateLamp(), CreatePlant(), and BuildConnectingDoor().
//   - Table/shelf size ranges and placement retries in TryCreateRandomTable() and
//     TryCreateRandomShelf().
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Single-MonoBehaviour spawner that builds an entire "find the hidden radioactive barrel" scene at
    /// runtime: an OVR camera rig with locomotion (smooth move, snap turn, teleport), a two-room office
    /// with a swinging connecting door, randomized props (folding tables and wall shelves), and a walkable,
    /// visibility-checked field of drums (55/30/5-gallon) that each carry a randomized radiation count for
    /// the identiFINDER to read. Almost every object is code-generated; the scene itself is near-empty.
    /// </summary>
    public class BasicVRRoomBarrelSpawner : MonoBehaviour
    {
        /// <summary>Conversion factor from feet (the units the room is authored in) to Unity meters.</summary>
        private const float FeetToMeters = 0.3048f;
        /// <summary>Default lab-room edge length in feet used when no override is supplied.</summary>
        private const float DefaultRoomFeet = 20f;
        /// <summary>Default lab-room edge length converted to meters.</summary>
        private const float DefaultRoomMeters = DefaultRoomFeet * FeetToMeters;

        /// <summary>The three physical drum sizes that can be spawned, in gallons.</summary>
        private enum BarrelSize
        {
            Gallon55,
            Gallon30,
            Gallon5
        }

        /// <summary>Which kind of surface a spawn slot sits on; controls spacing, allowed sizes, and visibility rules.</summary>
        private enum SurfaceKind
        {
            Floor,
            Table,
            Shelf
        }

        /// <summary>Identifies which wall a wall-mounted shelf is attached to.</summary>
        private enum WallSide
        {
            North,
            South,
            East,
            West
        }

        /// <summary>Inspector-assigned prefab references for each drum size (the base 55-gallon field is legacy-renamed).</summary>
        [Serializable]
        private struct BarrelPrefabSet
        {
            /// <summary>Prefab used for 55-gallon drums.</summary>
            [FormerlySerializedAs("m_barrelPrefab")] public GameObject barrel55GallonPrefab;
            /// <summary>Prefab used for 30-gallon drums.</summary>
            public GameObject barrel30GallonPrefab;
            /// <summary>Prefab used for 5-gallon drums/pails.</summary>
            public GameObject barrel5GallonPrefab;
        }

        /// <summary>Resolved per-size drum description (prefab, real-world dimensions, colour, selection weight) built once per layout pass.</summary>
        private struct BarrelSpec
        {
            /// <summary>Which drum size this spec describes.</summary>
            public BarrelSize Size;
            /// <summary>Human-readable label (e.g. "55 GAL") applied to the spawned object name.</summary>
            public string Label;
            /// <summary>Source prefab to instantiate, or null to fall back to a primitive placeholder.</summary>
            public GameObject Prefab;
            /// <summary>Initial local scale applied to the instantiated prefab before physical fit-up.</summary>
            public Vector3 ModelScale;
            /// <summary>Target physical diameter in meters used for fit-up, colliders, and spacing.</summary>
            public float Diameter;
            /// <summary>Target physical height in meters used for fit-up and colliders.</summary>
            public float Height;
            /// <summary>Height at which a side label would sit (retained for placement metadata).</summary>
            public float LabelHeight;
            /// <summary>Body colour used for the placeholder cylinder when no prefab is assigned.</summary>
            public Color BodyColor;
            /// <summary>Relative probability weight for choosing this size in a slot (smaller drums are favoured).</summary>
            public float SelectionWeight;
        }

        /// <summary>A candidate placement position for one drum, with orientation, surface type, and size constraints.</summary>
        private struct SpawnSlot
        {
            /// <summary>World position of the surface point the drum rests on.</summary>
            public Vector3 Position;
            /// <summary>Direction the drum's label should face (typically toward room centre).</summary>
            public Vector3 LabelForward;
            /// <summary>Surface kind (floor/table/shelf) this slot belongs to.</summary>
            public SurfaceKind Surface;
            /// <summary>Drum sizes permitted in this slot.</summary>
            public BarrelSize[] AllowedSizes;
            /// <summary>Whether the drum may be tipped on its side here.</summary>
            public bool AllowSideways;
            /// <summary>Yaw of the underlying surface, for aligning the drum.</summary>
            public float SurfaceYaw;
            /// <summary>Name of the prop that produced this slot (used in the spawned object's name).</summary>
            public string SourceName;
        }

        /// <summary>Record of an already-placed drum, used for spacing and line-of-sight checks against later slots.</summary>
        private struct PlacedBarrel
        {
            /// <summary>World position of the placed drum's base.</summary>
            public Vector3 Position;
            /// <summary>Horizontal radius of the placed drum.</summary>
            public float Radius;
            /// <summary>Height of the placed drum.</summary>
            public float Height;
            /// <summary>Size category of the placed drum.</summary>
            public BarrelSize Size;
            /// <summary>Surface the placed drum sits on.</summary>
            public SurfaceKind Surface;
        }

        /// <summary>An oriented rectangle on the floor plane used as a navigation/overlap obstacle footprint.</summary>
        private struct ObstacleRect
        {
            /// <summary>Rectangle centre in the XZ plane.</summary>
            public Vector2 Center;
            /// <summary>Half width/depth of the rectangle.</summary>
            public Vector2 HalfExtents;
            /// <summary>Rotation of the rectangle about the up axis, in degrees.</summary>
            public float YawDegrees;
        }

        /// <summary>Metadata about a spawned prop (table or shelf): its transform, footprint, and the surfaces drums can be placed on.</summary>
        private sealed class PropInfo
        {
            /// <summary>Root transform of the prop.</summary>
            public Transform Transform;
            /// <summary>Floor footprint used for navigation and overlap tests.</summary>
            public ObstacleRect Footprint;
            /// <summary>Primary usable surface height (top of table, or first shelf tier).</summary>
            public float SurfaceY;
            /// <summary>Inward-facing direction (toward the room) drums should face.</summary>
            public Vector3 Forward;
            /// <summary>Usable surface size (XZ) for laying out slots.</summary>
            public Vector2 Size;
            /// <summary>Number of stacked surfaces (shelf tiers).</summary>
            public int TierCount;
            /// <summary>Absolute Y height of each shelf tier's placement surface.</summary>
            public List<float> ShelfSurfaceHeights;
            /// <summary>Usable local slot area per shelf tier.</summary>
            public Vector2 ShelfSlotSize;
            /// <summary>Local-space centre offset for shelf slots (pushed toward the front edge).</summary>
            public Vector3 ShelfSlotCenterLocal;
            /// <summary>Which wall this shelf is mounted on.</summary>
            public WallSide Wall;
            /// <summary>Display name of the prop, propagated to spawned drum names.</summary>
            public string Name;
        }

        [Header("VR rig")]
        /// <summary>When true, find or create an OVRCameraRig; otherwise fall back to a plain main camera.</summary>
        [SerializeField] private bool m_createOvrCameraRig = true;
        /// <summary>Requested spawn position for the player; clamped to the spawn room if invalid.</summary>
        [SerializeField] private Vector3 m_playerStartPosition = Vector3.zero;
        /// <summary>Eye height in meters used only for the non-OVR camera fallback.</summary>
        [SerializeField, Min(0.5f)] private float m_defaultEyeHeight = 1.6f;

        [Header("Room scale")]
        /// <summary>When true, generate the full two-room shell (floors, walls, ceiling, lights, door).</summary>
        [SerializeField] private bool m_buildRoomGeometry = true;
        /// <summary>Lab-room footprint in feet.</summary>
        [SerializeField] private Vector2 m_roomSizeFeet = new Vector2(DefaultRoomFeet, DefaultRoomFeet);
        /// <summary>Adjoining spawn/home-room footprint in feet.</summary>
        [SerializeField] private Vector2 m_spawnRoomSizeFeet = new Vector2(12f, 12f);
        /// <summary>Wall/ceiling height in meters for both rooms.</summary>
        [SerializeField, Min(1f)] private float m_wallHeight = 2.75f;
        /// <summary>Thickness of generated walls in meters.</summary>
        [SerializeField, Min(0.02f)] private float m_wallThickness = 0.1f;
        /// <summary>Thickness of generated floor slabs in meters.</summary>
        [SerializeField, Min(0.02f)] private float m_floorThickness = 0.08f;
        /// <summary>Whether to draw the editor gizmo preview of room/aisle/door bounds.</summary>
        [SerializeField] private bool m_showEditorScalePreview = true;

        [Header("Connecting door")]
        /// <summary>Width of the doorway opening in the shared wall, in meters.</summary>
        [SerializeField, Min(0.6f)] private float m_doorwayWidth = 0.95f;
        /// <summary>Height of the doorway opening, in meters.</summary>
        [SerializeField, Min(1.5f)] private float m_doorwayHeight = 2.1f;
        /// <summary>Yaw angle (degrees) the door swings to when opened.</summary>
        [SerializeField, Range(-130f, 130f)] private float m_doorOpenAngle = -95f;
        /// <summary>How close a controller must be to a knob to toggle the door, in meters.</summary>
        [SerializeField, Min(0.05f)] private float m_doorKnobInteractionRadius = 0.24f;
        /// <summary>Minimum time between door open/close toggles, in seconds.</summary>
        [SerializeField, Min(0.05f)] private float m_doorToggleCooldown = 0.35f;
        /// <summary>Door swing speed in degrees per second.</summary>
        [SerializeField, Min(15f)] private float m_doorSwingSpeed = 150f;

        [Header("Controller hand visuals")]
        /// <summary>Whether to attach simple procedural hand meshes to the controller anchors.</summary>
        [SerializeField] private bool m_showControllerHands = true;
        /// <summary>Uniform scale applied to the controller hand visuals.</summary>
        [SerializeField, Min(0.1f)] private float m_controllerHandScale = 1f;

        [Header("Barrel prefabs")]
        /// <summary>Per-size drum prefab references.</summary>
        [SerializeField] private BarrelPrefabSet m_barrelPrefabs;
        /// <summary>Optional profile that supplies realistic randomized radiation counts; falls back to a generated pool if null.</summary>
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        /// <summary>Initial local scale for the 55-gallon prefab before physical fit-up.</summary>
        [SerializeField] private Vector3 m_barrel55ModelScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        /// <summary>Initial local scale for the 30-gallon prefab before physical fit-up.</summary>
        [SerializeField] private Vector3 m_barrel30ModelScale = new Vector3(0.14f, 0.14f, 0.14f);
        /// <summary>Initial local scale for the 5-gallon prefab before physical fit-up.</summary>
        [SerializeField] private Vector3 m_barrel5ModelScale = new Vector3(0.07f, 0.07f, 0.07f);

        [Header("Scenario randomization")]
        /// <summary>Minimum number of drums to attempt to spawn per run.</summary>
        [SerializeField, Min(1)] private int m_minBarrels = 1;
        /// <summary>Maximum number of drums to attempt to spawn per run.</summary>
        [SerializeField, Min(1)] private int m_maxBarrels = 45;
        /// <summary>Minimum number of folding tables to generate.</summary>
        [SerializeField, Min(0)] private int m_minTables;
        /// <summary>Maximum number of folding tables to generate.</summary>
        [SerializeField, Min(0)] private int m_maxTables = 4;
        /// <summary>Minimum number of wall shelf units to generate.</summary>
        [SerializeField, Min(0)] private int m_minShelfUnits = 2;
        /// <summary>Maximum number of wall shelf units to generate.</summary>
        [SerializeField, Min(0)] private int m_maxShelfUnits = 9;
        /// <summary>Number of full layout attempts to make while trying to hit the requested drum count.</summary>
        [SerializeField, Min(1)] private int m_layoutRetryCount = 10;
        /// <summary>When true, use <see cref="m_fixedSeed"/> for reproducible layouts.</summary>
        [SerializeField] private bool m_useFixedSeed;
        /// <summary>Deterministic seed used when <see cref="m_useFixedSeed"/> is enabled.</summary>
        [SerializeField] private int m_fixedSeed = 12345;

        [Header("Visibility and walkability")]
        /// <summary>Radius around the player start position kept clear of props/drums, in meters.</summary>
        [SerializeField, Min(0.1f)] private float m_playerClearance = 0.8f;
        /// <summary>Player collision radius used for walkability tests, in meters.</summary>
        [SerializeField, Min(0.05f)] private float m_playerRadius = 0.28f;
        /// <summary>Width of the cross-shaped central aisle kept clear through the lab, in meters.</summary>
        [SerializeField, Min(0.2f)] private float m_centralAisleWidth = 0.95f;
        /// <summary>Minimum center-to-center spacing between floor drums, in meters.</summary>
        [SerializeField, Min(0.05f)] private float m_floorBarrelSpacing = 0.64f;
        /// <summary>Minimum spacing between drums on a table, in meters.</summary>
        [SerializeField, Min(0.01f)] private float m_tableBarrelSpacing = 0.34f;
        /// <summary>Minimum spacing between drums on a shelf, in meters.</summary>
        [SerializeField, Min(0.01f)] private float m_shelfBarrelSpacing = 0.28f;

        [Header("Shelf asset")]
        /// <summary>Optional shelf model instantiated per tier; if null, primitive boards are built instead.</summary>
        [SerializeField] private GameObject m_wallShelfPrefab;
        /// <summary>Small vertical gap left above a shelf surface so drums do not clip into it, in meters.</summary>
        [SerializeField, Min(0.001f)] private float m_shelfSurfaceClearance = 0.015f;

        [Header("Locomotion")]
        /// <summary>Enables left-thumbstick smooth locomotion.</summary>
        [SerializeField] private bool m_enableSmoothMove = true;
        /// <summary>Enables right-controller aim-and-trigger teleport.</summary>
        [SerializeField] private bool m_enableTeleport = true;
        /// <summary>Smooth move speed in meters per second.</summary>
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        /// <summary>Degrees turned per snap-turn input.</summary>
        [SerializeField, Min(5f)] private float m_snapTurnDegrees = 30f;
        /// <summary>Minimum time between snap turns, in seconds.</summary>
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.3f;
        /// <summary>Thumbstick magnitude below which input is ignored.</summary>
        [SerializeField, Min(0.05f)] private float m_thumbstickDeadzone = 0.22f;

        [Header("Fallback counts")]
        /// <summary>Number of pre-generated radiation counts in the fallback pool.</summary>
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        /// <summary>Lower bound (inclusive) for fallback radiation counts.</summary>
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        /// <summary>Upper bound (inclusive) for fallback radiation counts.</summary>
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        /// <summary>All BarrelInstance components spawned this run.</summary>
        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        /// <summary>Every runtime-created GameObject, tracked for cleanup on clear.</summary>
        private readonly List<GameObject> m_spawnedObjects = new();
        /// <summary>Metadata for the tables generated this run.</summary>
        private readonly List<PropInfo> m_tables = new();
        /// <summary>Metadata for the shelves generated this run.</summary>
        private readonly List<PropInfo> m_shelves = new();
        /// <summary>Candidate drum placement slots for the current layout.</summary>
        private readonly List<SpawnSlot> m_spawnSlots = new();
        /// <summary>Drums already placed this layout pass, used for spacing/visibility tests.</summary>
        private readonly List<PlacedBarrel> m_placedBarrels = new();
        /// <summary>Floor footprints (props + floor drums) the player cannot walk through.</summary>
        private readonly List<ObstacleRect> m_navigationObstacles = new();
        /// <summary>Materials created at runtime, destroyed in <see cref="OnDestroy"/>.</summary>
        private readonly List<Material> m_runtimeMaterials = new();
        /// <summary>Cache keyed by colour/name so identical materials are reused.</summary>
        private readonly Dictionary<string, Material> m_materialCache = new();

        /// <summary>Pre-generated radiation counts used when no <see cref="RadiationCountProfile"/> is assigned.</summary>
        private int[] m_fallbackCountPool;
        /// <summary>The player's head/eye transform (center-eye anchor or fallback camera).</summary>
        private Transform m_cameraTransform;
        /// <summary>Transform moved by locomotion (the rig root or fallback camera).</summary>
        private Transform m_locomotionRoot;
        /// <summary>The active OVR camera rig, if one is used.</summary>
        private OVRCameraRig m_cameraRig;
        /// <summary>Parent transform holding all generated room shell geometry.</summary>
        private Transform m_roomRoot;
        /// <summary>Parent transform holding the randomized props and drums (rebuilt each run).</summary>
        private Transform m_scenarioRoot;
        /// <summary>Disc marker showing the current teleport destination.</summary>
        private GameObject m_teleportMarker;
        /// <summary>Renderer of the teleport marker, swapped between valid/invalid materials.</summary>
        private Renderer m_teleportMarkerRenderer;
        /// <summary>Green material shown when the teleport target is valid.</summary>
        private Material m_validTeleportMaterial;
        /// <summary>Red material shown when the teleport target is invalid.</summary>
        private Material m_invalidTeleportMaterial;
        /// <summary>Hinge transform the connecting door swings around.</summary>
        private Transform m_doorPivot;
        /// <summary>Knob on the lab-facing side of the door.</summary>
        private Transform m_labDoorKnob;
        /// <summary>Knob on the spawn-room-facing side of the door.</summary>
        private Transform m_spawnDoorKnob;
        /// <summary>Current door swing angle in degrees (animated toward the target).</summary>
        private float m_doorCurrentAngle;
        /// <summary>Whether the door is currently open.</summary>
        private bool m_isDoorOpen;
        /// <summary>Earliest time the door may be toggled again.</summary>
        private float m_nextDoorToggleTime;
        /// <summary>Procedural hand visual attached to the left controller.</summary>
        private GameObject m_leftHandVisual;
        /// <summary>Procedural hand visual attached to the right controller.</summary>
        private GameObject m_rightHandVisual;
        /// <summary>Latest computed teleport destination on the floor.</summary>
        private Vector3 m_currentTeleportTarget;
        /// <summary>Whether <see cref="m_currentTeleportTarget"/> is a legal destination.</summary>
        private bool m_hasValidTeleportTarget;
        /// <summary>Earliest time a snap turn may occur again.</summary>
        private float m_nextSnapTurnTime;

        /// <summary>Lab-room footprint in meters, floored at an 8ft minimum edge.</summary>
        private Vector2 RoomSizeMeters => new(
            Mathf.Max(8f, m_roomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_roomSizeFeet.y) * FeetToMeters);

        /// <summary>Spawn-room footprint in meters, floored at an 8ft minimum edge.</summary>
        private Vector2 SpawnRoomSizeMeters => new(
            Mathf.Max(8f, m_spawnRoomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_spawnRoomSizeFeet.y) * FeetToMeters);

        /// <summary>World-space center of the spawn room, placed just south of the shared wall.</summary>
        private Vector3 SpawnRoomCenter
        {
            get
            {
                var roomSize = RoomSizeMeters;
                var spawnSize = SpawnRoomSizeMeters;
                return new Vector3(0f, 0f, -roomSize.y * 0.5f - spawnSize.y * 0.5f);
            }
        }

        /// <summary>Validated player start position; the serialized value if it lies in the spawn room, otherwise a safe default there.</summary>
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

        /// <summary>Unity lifecycle: sets up the camera rig and caches the head transform before the first frame.</summary>
        private void Awake()
        {
            m_cameraTransform = EnsureCameraRig();
        }

        /// <summary>Unity lifecycle: builds the room shell (if enabled), controller hands, and the first randomized run.</summary>
        private void Start()
        {
            if (m_buildRoomGeometry)
            {
                BuildRoomGeometry();
            }

            EnsureControllerHandVisuals();
            GenerateRun();
        }

        /// <summary>Unity lifecycle: drives per-frame locomotion, door interaction/swing, and debug input (A regenerates, B clears, X re-rolls counts).</summary>
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

        /// <summary>Clears any prior scenario and generates a fresh randomized layout, retrying seeds to place as many valid drums as possible.</summary>
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

        /// <summary>Destroys all spawned props/drums and resets the per-run bookkeeping lists and scenario root.</summary>
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

        /// <summary>Re-rolls the radiation count on every already-spawned drum without changing the layout.</summary>
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

        /// <summary>Finds or creates the OVR camera rig (or a plain camera fallback), positions it at the player start, and returns the head transform.</summary>
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

        /// <summary>Applies the sim's standard camera settings (skybox clear, clip planes, single-pass stereo, no HDR) to a rig's center eye.</summary>
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

        /// <summary>Ensures controller hand visuals exist and are toggled to match the setting, preferring Meta's hand assets and falling back to procedural primitives.</summary>
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

        /// <summary>Enables or disables both controller hand visuals if they exist.</summary>
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

        /// <summary>Builds a simple procedural hand (palm, wrist, thumb, four fingers with knuckles) parented to a controller anchor.</summary>
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

        /// <summary>Creates one collider-free primitive piece of a procedural hand at the given local transform.</summary>
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

        /// <summary>Generates the full two-room shell: lab floor/walls/ceiling with a doorway, ceiling grid and lights, the adjoining home/spawn room, the connecting door, and home props.</summary>
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

        /// <summary>Builds the shared wall as left/right side segments plus a header, leaving a centered doorway opening.</summary>
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

        /// <summary>Builds the adjoining spawn/home room shell (floor, three walls, ceiling, grid, warm lights) south of the lab.</summary>
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

        /// <summary>Lays down a drop-ceiling grid of thin strips (2ft tile pattern) over a room footprint.</summary>
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

        /// <summary>Adds four emissive fluorescent light panels (each with a cool point light) to the lab ceiling.</summary>
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

        /// <summary>Adds two warm light panels (each with a warm point light) to the spawn/home room ceiling.</summary>
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

        /// <summary>Builds the hinged connecting door (two-tone faces, gold knobs on each side) and caches its pivot/knob transforms.</summary>
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

        /// <summary>Creates a collider-free knob (stem + sphere) on one face of the door and returns the knob sphere transform.</summary>
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

        /// <summary>Furnishes the spawn/home room with a rug, couch, coffee/side tables, a lamp, and a potted plant to establish the "home" mood.</summary>
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

        /// <summary>Builds a table lamp (base, stem, shade) with a warm point light for home-room ambience.</summary>
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

        /// <summary>Builds a potted plant (pot, stem, clustered leaf spheres) for the home room.</summary>
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

        /// <summary>Convenience wrapper that creates a solid, collidable cube under the room root.</summary>
        private void CreateRoomCube(string cubeName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreateChildCube(m_roomRoot, cubeName, localPosition, localScale, material, true);
        }

        /// <summary>Creates a textured cube primitive under a parent, optionally disabling its collider, and returns it.</summary>
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

        /// <summary>Assigns a shared material to a GameObject's renderer if it has one.</summary>
        private static void AssignMaterial(GameObject target, Material material)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        /// <summary>Disables a GameObject's collider if present (used for decorative, non-collidable pieces).</summary>
        private static void DisableCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        /// <summary>Randomly generates the lab's tables and wall shelves for this layout attempt, registering their navigation footprints.</summary>
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

        /// <summary>Attempts (up to a fixed retry budget) to place a non-overlapping folding table in a random quadrant and records its PropInfo.</summary>
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

        /// <summary>Mounts a multi-tier wall shelf on a random wall (using the shelf prefab per tier, or primitive boards as a fallback) and records its PropInfo and tier surfaces.</summary>
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

        /// <summary>Instantiates the shelf prefab for one tier, rescales it to the target length/depth, seats it against the wall at the requested surface height, and adds colliders; returns false if no prefab or bounds.</summary>
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

        /// <summary>Disables and destroys every collider under a prefab so custom fit-up colliders can replace them.</summary>
        private static void RemoveColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
        }

        /// <summary>Adds non-convex mesh colliders matching each mesh under a shelf so drums rest on the real geometry; returns true if any were added.</summary>
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

        /// <summary>Computes the combined renderer bounds of a hierarchy expressed in another transform's local space; returns false if it has no renderers.</summary>
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

        /// <summary>Adds a thin box collider at the top of a shelf tier's bounds so drums have a flat surface to rest on.</summary>
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

        /// <summary>Creates a collidable cube primitive for prop geometry (table tops/legs, primitive shelf boards) under a parent.</summary>
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

        /// <summary>Rebuilds and shuffles the full set of candidate drum slots from the floor grid, table tops, and shelf tiers.</summary>
        private void BuildSpawnSlots()
        {
            m_spawnSlots.Clear();
            BuildFloorSlots();
            BuildTableSlots();
            BuildShelfSlots();
            Shuffle(m_spawnSlots);
        }

        /// <summary>Generates floor slots on a grid, skipping the central aisle, the player clearance zone, and prop footprints.</summary>
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

        /// <summary>Generates a spread of slots on each table top (center allows any size and possible sideways placement; edges take smaller drums only).</summary>
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

        /// <summary>Generates evenly-spaced 5-gallon-only slots along each usable tier of every wall shelf, facing into the room.</summary>
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

        /// <summary>Walks the shuffled slots and spawns drums up to the requested count, honoring per-surface spacing and floor line-of-sight rules; returns how many were placed.</summary>
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

        /// <summary>Builds the per-size drum spec table (prefab, dimensions, colour, selection weight) for the current layout pass.</summary>
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

        /// <summary>Returns the slot's allowed sizes ordered by weighted-random draw, so preferred sizes are tried first when placing a drum.</summary>
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

        /// <summary>Instantiates one drum (prefab or placeholder) at a slot, orients/fits/seats it, ensures a collider, and initializes its BarrelInstance with a random radiation count.</summary>
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

        /// <summary>Builds a simple colored cylinder stand-in drum when no prefab is assigned for a size.</summary>
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

        /// <summary>Rescales a spawned prefab so its renderer bounds match the spec's real-world diameter and height.</summary>
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

        /// <summary>Nudges a drum up/down so its lowest renderer point rests exactly on the given surface height.</summary>
        private static void AlignRendererBottomToSurface(GameObject barrelObject, float surfaceY)
        {
            if (!TryGetRendererBounds(barrelObject, out var bounds))
            {
                return;
            }

            barrelObject.transform.position += Vector3.up * (surfaceY - bounds.min.y);
        }

        /// <summary>Adds an approximate box collider sized to the spec if the drum has no collider, so it blocks navigation/aim raycasts.</summary>
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

        /// <summary>Computes the combined world-space renderer bounds of a hierarchy; returns false if it has no renderers.</summary>
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

        /// <summary>Returns true if a candidate drum would keep the required spacing from all already-placed drums on comparable surfaces/heights.</summary>
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

        /// <summary>For floor slots, returns false if an existing floor drum would fully occlude the candidate along the sight line from room center (keeps drums individually visible).</summary>
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

        /// <summary>Returns the configured minimum drum spacing for a given surface kind.</summary>
        private float GetSpacing(SurfaceKind surface)
        {
            return surface switch
            {
                SurfaceKind.Table => m_tableBarrelSpacing,
                SurfaceKind.Shelf => m_shelfBarrelSpacing,
                _ => m_floorBarrelSpacing
            };
        }

        /// <summary>Per-frame locomotion: left-stick smooth movement, right-stick snap turning, and right-controller aim-and-trigger teleport.</summary>
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

        /// <summary>Raycasts the teleport aim against the floor, validates the destination (range, walkability, closed-door crossing), and updates the marker position/color.</summary>
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

        /// <summary>Returns the aiming ray for teleport, preferring the right controller anchor and falling back to the head/camera.</summary>
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

        /// <summary>Lazily creates the flat teleport marker disc and its valid/invalid materials on first use.</summary>
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

        /// <summary>Moves the locomotion root to a clamped, walkable world position (only if the destination is legal).</summary>
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

        /// <summary>Toggles the door open/closed when a controller grip is pressed while near either knob, respecting the toggle cooldown.</summary>
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

        /// <summary>Returns true if the controller is within the interaction radius of either door knob.</summary>
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

        /// <summary>Returns true if a point is within a radius of a (non-null) target transform.</summary>
        private static bool IsPointNearTransform(Vector3 point, Transform target, float radius)
        {
            return target != null && Vector3.Distance(point, target.position) <= radius;
        }

        /// <summary>Per-frame animates the door hinge toward its open or closed angle at the configured swing speed.</summary>
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

        /// <summary>Rotates the rig about the player's head by the given yaw, then re-clamps to keep them in a legal position.</summary>
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

        /// <summary>Returns true if a position is inside the walkable floor plan and clear of the closed door and all navigation obstacles.</summary>
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

        /// <summary>Clamps an out-of-bounds position to the nearest legal spot among the lab, the spawn room, or the doorway gap.</summary>
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

        /// <summary>Returns true if the position is inside either room and not blocked by the solid part of the shared wall.</summary>
        private bool IsInsideWalkableFloorPlan(Vector3 position, float margin)
        {
            var insideRoom = IsInsideLabRoom(position, margin) || IsInsideSpawnRoom(position, margin);
            return insideRoom && !IsBlockedBySharedWall(position, margin);
        }

        /// <summary>Returns true if a position falls within the closed door's blocking slab in the doorway.</summary>
        private bool IsInsideClosedDoorObstacle(Vector3 position, float padding)
        {
            var doorwayCenter = GetDoorwayCenter();
            var halfWidth = Mathf.Max(0.1f, m_doorwayWidth * 0.5f);
            var halfDepth = m_wallThickness * 0.5f;
            return Mathf.Abs(position.x - doorwayCenter.x) <= halfWidth + padding
                   && Mathf.Abs(position.z - doorwayCenter.z) <= halfDepth + padding * 0.5f;
        }

        /// <summary>Returns true if a teleport segment would pass through the doorway while the door is closed (blocking cross-room teleports).</summary>
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

        /// <summary>Returns true if a position lies within the lab room bounds (with a horizontal margin).</summary>
        private bool IsInsideLabRoom(Vector3 position, float margin)
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f - margin;
            var halfDepth = size.y * 0.5f;
            return Mathf.Abs(position.x) <= halfWidth
                   && position.z <= halfDepth - margin
                   && position.z >= -halfDepth;
        }

        /// <summary>Returns true if a position lies within the spawn/home room bounds (with a horizontal margin), open to the doorway on the north edge.</summary>
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

        /// <summary>Returns true if a position sits against the solid (non-doorway) part of the shared wall between the two rooms.</summary>
        private bool IsBlockedBySharedWall(Vector3 position, float margin)
        {
            var wallZ = -RoomSizeMeters.y * 0.5f;
            var doorHalfWidth = Mathf.Max(0.05f, m_doorwayWidth * 0.5f - margin * 0.3f);
            return Mathf.Abs(position.z - wallZ) < margin && Mathf.Abs(position.x) > doorHalfWidth;
        }

        /// <summary>Clamps a position into an axis-aligned rectangle (center + size) with a margin, preserving its Y.</summary>
        private static Vector3 ClampToRect(Vector3 position, Vector3 center, Vector2 size, float margin)
        {
            var halfWidth = Mathf.Max(0.05f, size.x * 0.5f - margin);
            var halfDepth = Mathf.Max(0.05f, size.y * 0.5f - margin);
            return new Vector3(
                Mathf.Clamp(position.x, center.x - halfWidth, center.x + halfWidth),
                position.y,
                Mathf.Clamp(position.z, center.z - halfDepth, center.z + halfDepth));
        }

        /// <summary>Returns the world-space center of the doorway on the shared wall.</summary>
        private Vector3 GetDoorwayCenter()
        {
            return new Vector3(0f, 0f, -RoomSizeMeters.y * 0.5f);
        }

        /// <summary>Returns true if a prop footprint clears the central aisle and player start, does not overlap existing obstacles, and fits inside the room.</summary>
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

        /// <summary>Returns true if any corner or the center of a padded rectangle intrudes into the cross-shaped central aisle.</summary>
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

        /// <summary>Returns true if a position lies within the cross-shaped central aisle kept clear through the lab.</summary>
        private bool IsInCentralAisle(Vector3 position)
        {
            return Mathf.Abs(position.x) < m_centralAisleWidth * 0.5f || Mathf.Abs(position.z) < m_centralAisleWidth * 0.5f;
        }

        /// <summary>Returns true if a padded position falls inside any registered navigation obstacle.</summary>
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

        /// <summary>Returns true if a point (XZ) lies inside an oriented rectangle expanded by a padding.</summary>
        private static bool IsPointInsideRect(Vector3 point, ObstacleRect rect, float padding)
        {
            var local = Rotate(new Vector2(point.x, point.z) - rect.Center, -rect.YawDegrees);
            return Mathf.Abs(local.x) <= rect.HalfExtents.x + padding && Mathf.Abs(local.y) <= rect.HalfExtents.y + padding;
        }

        /// <summary>Approximate oriented-rectangle overlap test: true if any padded corner of one rectangle lies inside the other.</summary>
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

        /// <summary>Returns true if every padded corner of a rectangle stays within the lab room bounds.</summary>
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

        /// <summary>Returns the four world-space (XZ) corners of an oriented rectangle expanded by a padding.</summary>
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

        /// <summary>Rotates a 2D point about the origin by a yaw in degrees.</summary>
        private static Vector2 Rotate(Vector2 point, float yawDegrees)
        {
            var radians = yawDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector2(point.x * cos - point.y * sin, point.x * sin + point.y * cos);
        }

        /// <summary>Returns a random floor position in one of the four room quadrants, kept outside the central aisle, for placing a prop.</summary>
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

        /// <summary>Returns the XZ-plane distance between two points, ignoring height.</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Lazily creates the parent transform that holds all per-run scenario objects.</summary>
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

        /// <summary>In-place Fisher-Yates shuffle used to randomize slot ordering.</summary>
        private static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var swapIndex = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
            }
        }

        /// <summary>Returns a random radiation count for a drum, from the assigned profile or the generated fallback pool.</summary>
        private int GetRandomRadiationCount()
        {
            if (m_radiationCountProfile != null)
            {
                return m_radiationCountProfile.GetRandomCount();
            }

            EnsureFallbackCountPool();
            return m_fallbackCountPool[UnityEngine.Random.Range(0, m_fallbackCountPool.Length)];
        }

        /// <summary>Rebuilds the fallback count pool if it is missing or has the wrong size.</summary>
        private void EnsureFallbackCountPool()
        {
            if (m_fallbackCountPool == null || m_fallbackCountPool.Length != Mathf.Max(1, m_fallbackCountPoolSize))
            {
                RebuildFallbackCountPool();
            }
        }

        /// <summary>Fills the fallback count pool with fresh random values within the configured min/max range.</summary>
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

        /// <summary>Returns a cached material for a colour/name, creating (and tracking for cleanup) a new URP-Lit-or-Standard material if none exists.</summary>
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

        /// <summary>Logs a status/diagnostic message prefixed with this spawner's name.</summary>
        private void SetStatus(string message)
        {
            Debug.Log($"BasicVRRoomBarrelSpawner: {message}");
        }

        /// <summary>Unity lifecycle: destroys all runtime-created materials and clears the material cache to avoid leaks.</summary>
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

        /// <summary>Unity editor callback: clamps inspector values to sane ranges (room/door sizes, min/max counts, aisle width).</summary>
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

        /// <summary>Unity editor callback: draws wireframe previews of the two rooms, doorway, player start, and central aisle when enabled.</summary>
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

        /// <summary>Outcome of a single layout pass: how many drums were successfully placed.</summary>
        private struct ScenarioResult
        {
            /// <summary>Number of drums placed during the pass.</summary>
            public int SpawnedCount;
        }
    }
}
