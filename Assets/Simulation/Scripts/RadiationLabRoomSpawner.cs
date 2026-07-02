using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

// =============================================================================
// RadiationLabRoomSpawner.cs
//
// PURPOSE:  The one MonoBehaviour that builds and runs the RadiationLabRoom scene
//   at runtime. It constructs the two-room lab (main lab + adjacent spawn/waiting
//   room), scatters visually-identical 55/30/5-gallon barrels across the floor,
//   tables and wall shelves, hides exactly ONE radioactive source among them,
//   spawns the grabbable identiFINDER detector on its pedestal, the wall START
//   button and the swinging door, wires up VR hands/arms and locomotion, and owns
//   the Waiting -> Playing -> Resolved find-the-barrel game loop plus the
//   end-of-round stats screen.
//
// HOW TO CUSTOMIZE:
//   CRITICAL: nearly every knob below is a [SerializeField], so its value is ALSO
//   baked into the scene file. Editing the C# default here does NOT change what
//   the game does — the scene's saved value wins. To actually change behavior,
//   open scene "Assets/Simulation/Scenes/RadiationLabRoom.unity", select the
//   GameObject named "RadiationLabRoomSpawner" (it holds this component), and edit
//   the field in the Inspector (or hand-edit the same value in the scene's YAML).
//   Truly hardcoded values (const / literals in a method) are called out below with
//   the exact method to edit — those you change here in the C# file.
//
//   - Round difficulty (Inspector, "Game loop" + "Scenario randomization" headers):
//       m_maxTries controls guesses per round; m_enableRoundTimer /
//       m_roundDurationSeconds control the ~3-minute clock; m_minBarrels /
//       m_maxBarrels set how many drums spawn; m_minTables/m_maxTables and
//       m_minShelfUnits/m_maxShelfUnits set how much furniture appears;
//       m_guessConeAngle / m_guessRayLength set how forgiving the "point at a
//       barrel" aim is.
//   - Hidden source (Inspector, "Hidden radiation source" header):
//       m_minSourceActivityCps / m_maxSourceActivityCps set the log-uniform
//       activity range. The isotope label is picked from the HARDCODED array
//       s_isotopeNames (top of this file) inside AssignHotSource(); edit that array
//       here in C# to change the isotope names.
//   - Room scale (Inspector, "Room scale" header): m_roomSizeFeet /
//       m_spawnRoomSizeFeet (feet), m_wallHeight, m_wallThickness, m_floorThickness.
//       Set m_buildRoomGeometry false to keep an authored scene instead of building
//       one at runtime. FeetToMeters / DefaultRoomFeet are hardcoded consts here.
//   - Door + START button (Inspector, "Connecting door" / "Game loop"):
//       m_doorwayWidth/Height, m_doorOpenAngle, m_doorLatchAngle,
//       m_startButtonPressRadius (bigger = easier to poke). The button's world
//       position, size and text ("START"/"IN ROUND") are HARDCODED in
//       EnsureStartButton() and RefreshStartButtonVisual().
//   - Detector (Inspector, "Detector" header): assign m_detectorPrefab to use the
//       real identiFINDER (null = procedural wand), m_detectorTargetHeight scales
//       it, m_detectorHeldLocalPosition / m_detectorHeldLocalEuler set the held
//       pose, m_pedestalOffsetFromSpawnCenter places the pedestal.
//   - Barrels' look (Inspector, "Barrel prefabs" header): assign the 3 prefabs in
//       m_barrelPrefabs and the PBR materials (m_barrelLargeMetal/Paint,
//       m_barrelSmallMetal/Paint); leave materials null to keep procedural tints.
//       The per-size physical dimensions, tint colors and selection weights live in
//       the HARDCODED BuildBarrelSpec/spec table in this file (search "BarrelSpec"),
//       not in the Inspector.
//   - Movement (Inspector, "Locomotion" header): m_enableSmoothMove /
//       m_enableTeleport, m_smoothMoveSpeed, m_snapTurnDegrees, m_maxTeleportDistance.
//       NOTE: at runtime ApplyMovementPreference() OVERRIDES these two toggles with
//       the smooth-vs-teleport choice the player made on the start screen (saved in
//       PlayerPrefs via MovementPreference); to ignore that choice, edit
//       ApplyMovementPreference() here in C#.
//   - Custom skinned arms (Inspector, "Custom arms (Mixamo FBX)" header): assign
//       m_customArmsPrefab to replace the default Meta hands, then tune
//       m_customArmsChestOffset / m_customArmsBodyYawOffset / m_customArmsTargetReach
//       on-device.
//   - Determinism: enable m_useFixedSeed + set m_fixedSeed (Inspector, "Scenario
//       randomization") to reproduce the exact same layout every round.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// Radiation-training variant of BasicVRRoomBarrelSpawner (which is kept untouched as a
    /// working reference). Adds: consistent per-type barrel sizing, exactly one hidden
    /// radioactive source per run, radiation physics with shielding, a grabbable identiFINDER
    /// detector presented on a pedestal, physically grabbed door, visible IK arms, and a
    /// procedural visual overhaul tuned for Quest 3.
    /// <summary>
    /// The single MonoBehaviour that builds and runs the RadiationLabRoom scene at runtime: it
    /// constructs the two-room lab geometry, scatters visually-identical barrels of three sizes,
    /// hides exactly one radioactive source among them, spawns the grabbable identiFINDER detector,
    /// the poke START button and the swinging connecting door, wires up VR locomotion, and owns the
    /// Waiting -> Playing -> Resolved find-the-barrel game loop with its end-of-round stats screen.
    /// </summary>
    public class RadiationLabRoomSpawner : MonoBehaviour
    {
        /// <summary>Conversion factor from feet (the Inspector unit for room size) to metres.</summary>
        private const float FeetToMeters = 0.3048f;
        /// <summary>Default square room edge length in feet used when no size is authored.</summary>
        private const float DefaultRoomFeet = 20f;

        /// <summary>Isotope labels randomly assigned to the hidden source (purely cosmetic flavour).</summary>
        private static readonly string[] s_isotopeNames = { "Cs-137", "Co-60", "Ir-192", "Am-241" };

        /// <summary>The three physical drum sizes the spawner can place (55/30/5 US gallon).</summary>
        private enum BarrelSize
        {
            Gallon55,
            Gallon30,
            Gallon5
        }

        /// <summary>What kind of surface a barrel spawn slot sits on (drives spacing/size rules).</summary>
        private enum SurfaceKind
        {
            Floor,
            Table,
            Shelf
        }

        /// <summary>Which wall a wall-mounted shelf is attached to.</summary>
        private enum WallSide
        {
            North,
            South,
            East,
            West
        }

        /// <summary>Inspector-assigned prefab references for each of the three barrel sizes.</summary>
        [Serializable]
        private struct BarrelPrefabSet
        {
            /// <summary>Model prefab for the 55-gallon drum (renamed from the legacy single-prefab field).</summary>
            [FormerlySerializedAs("m_barrelPrefab")] public GameObject barrel55GallonPrefab;
            /// <summary>Model prefab for the 30-gallon drum.</summary>
            public GameObject barrel30GallonPrefab;
            /// <summary>Model prefab for the 5-gallon pail.</summary>
            public GameObject barrel5GallonPrefab;
        }

        /// <summary>
        /// Immutable per-size description of a barrel: its prefab, physical dimensions, label text,
        /// procedural tint colour, and how likely this size is to be chosen for a slot.
        /// </summary>
        private struct BarrelSpec
        {
            /// <summary>Which of the three drum sizes this spec describes.</summary>
            public BarrelSize Size;
            /// <summary>Human-readable size label ("55 GAL", etc.) used for the sticker and object name.</summary>
            public string Label;
            /// <summary>Source model prefab, or null to build a procedural placeholder cylinder.</summary>
            public GameObject Prefab;
            /// <summary>Initial import scale applied before fitting the prefab to the physical size.</summary>
            public Vector3 ModelScale;
            /// <summary>Target physical diameter in metres.</summary>
            public float Diameter;
            /// <summary>Target physical height in metres.</summary>
            public float Height;
            /// <summary>Height above the barrel base at which the wrap-around size sticker is centred.</summary>
            public float LabelHeight;
            /// <summary>Fallback body tint for procedural/placeholder barrels.</summary>
            public Color BodyColor;
            /// <summary>Relative weight for the weighted random size pick (5 gal is most common).</summary>
            public float SelectionWeight;
        }

        /// <summary>A candidate spot a single barrel may be placed at, with its orientation and rules.</summary>
        private struct SpawnSlot
        {
            /// <summary>World position of the surface point the barrel base rests on.</summary>
            public Vector3 Position;
            /// <summary>Direction the size sticker should face (toward the room centre).</summary>
            public Vector3 LabelForward;
            /// <summary>Which surface kind (floor/table/shelf) this slot belongs to.</summary>
            public SurfaceKind Surface;
            /// <summary>Barrel sizes permitted in this slot.</summary>
            public BarrelSize[] AllowedSizes;
            /// <summary>Whether a barrel here may (historically) be laid on its side.</summary>
            public bool AllowSideways;
            /// <summary>Yaw of the underlying surface, used to orient the barrel/sticker.</summary>
            public float SurfaceYaw;
            /// <summary>Name of the owning surface (floor/table/shelf) for the spawned object's name.</summary>
            public string SourceName;
        }

        /// <summary>Record of an already-placed barrel, used for spacing and line-of-sight tests.</summary>
        private struct PlacedBarrel
        {
            /// <summary>World base position of the placed barrel.</summary>
            public Vector3 Position;
            /// <summary>Barrel radius in metres.</summary>
            public float Radius;
            /// <summary>Barrel height in metres.</summary>
            public float Height;
            /// <summary>Which of the three sizes was placed.</summary>
            public BarrelSize Size;
            /// <summary>Which surface kind it was placed on.</summary>
            public SurfaceKind Surface;
        }

        /// <summary>An oriented (yawed) rectangle footprint on the floor used for keep-out tests.</summary>
        private struct ObstacleRect
        {
            /// <summary>Rectangle centre in world XZ.</summary>
            public Vector2 Center;
            /// <summary>Half-width/half-depth of the rectangle in its own local axes.</summary>
            public Vector2 HalfExtents;
            /// <summary>Rotation of the rectangle about world up, in degrees.</summary>
            public float YawDegrees;
        }

        /// <summary>Runtime metadata about a placed prop (table or shelf) used to derive barrel slots.</summary>
        private sealed class PropInfo
        {
            /// <summary>Root transform of the spawned prop.</summary>
            public Transform Transform;
            /// <summary>Floor footprint used for navigation/placement keep-outs.</summary>
            public ObstacleRect Footprint;
            /// <summary>World Y of the primary usable surface (first tier for shelves).</summary>
            public float SurfaceY;
            /// <summary>Forward/inward facing direction (toward the room) for sticker orientation.</summary>
            public Vector3 Forward;
            /// <summary>Usable surface size (table top, or per-tier shelf slot span).</summary>
            public Vector2 Size;
            /// <summary>Number of stacked shelf tiers (1 for a table).</summary>
            public int TierCount;
            /// <summary>World Y of each shelf tier's usable surface.</summary>
            public List<float> ShelfSurfaceHeights;
            /// <summary>Usable slot span (length x depth) per shelf tier.</summary>
            public Vector2 ShelfSlotSize;
            /// <summary>Local centre of the usable slot region on a shelf board.</summary>
            public Vector3 ShelfSlotCenterLocal;
            /// <summary>Which wall a shelf is mounted on.</summary>
            public WallSide Wall;
            /// <summary>Display name of the prop (used for barrel object naming).</summary>
            public string Name;
        }

        [Header("VR rig")]
        /// <summary>When true, ensure an OVRCameraRig exists (spawning one if needed); else use Camera.main.</summary>
        [SerializeField] private bool m_createOvrCameraRig = true;
        /// <summary>Requested player start position; snapped into the spawn room if outside it.</summary>
        [SerializeField] private Vector3 m_playerStartPosition = Vector3.zero;
        /// <summary>Eye height used only for the non-VR fallback camera (headset supplies its own).</summary>
        [SerializeField, Min(0.5f)] private float m_defaultEyeHeight = 1.6f;

        [Header("Room scale")]
        /// <summary>When true, build the full room geometry on Start; off leaves an authored scene intact.</summary>
        [SerializeField] private bool m_buildRoomGeometry = true;
        /// <summary>Lab (main game) room footprint in feet.</summary>
        [SerializeField] private Vector2 m_roomSizeFeet = new Vector2(DefaultRoomFeet, DefaultRoomFeet);
        /// <summary>Adjacent spawn/waiting room footprint in feet (holds the START button + home props).</summary>
        [SerializeField] private Vector2 m_spawnRoomSizeFeet = new Vector2(12f, 12f);
        /// <summary>Interior wall height in metres.</summary>
        [SerializeField, Min(1f)] private float m_wallHeight = 2.75f;
        /// <summary>Wall thickness in metres.</summary>
        [SerializeField, Min(0.02f)] private float m_wallThickness = 0.1f;
        /// <summary>Floor slab thickness in metres.</summary>
        [SerializeField, Min(0.02f)] private float m_floorThickness = 0.08f;
        /// <summary>When true, draw wireframe room/aisle/player gizmos in the editor scene view.</summary>
        [SerializeField] private bool m_showEditorScalePreview = true;

        [Header("Connecting door")]
        /// <summary>Clear width of the doorway between the two rooms, in metres.</summary>
        [SerializeField, Min(0.6f)] private float m_doorwayWidth = 0.95f;
        /// <summary>Clear height of the doorway, in metres.</summary>
        [SerializeField, Min(1.5f)] private float m_doorwayHeight = 2.1f;
        /// <summary>Fully-open hinge angle in degrees (sign chooses the swing direction).</summary>
        [SerializeField, Range(-130f, 130f)] private float m_doorOpenAngle = -95f;
        /// <summary>Proximity radius within which a controller can grab or poke a door knob.</summary>
        [SerializeField, Min(0.05f)] private float m_doorKnobInteractionRadius = 0.24f;
        /// <summary>How aggressively the door follows the hand/auto target (higher = snappier).</summary>
        [SerializeField, Min(1f)] private float m_doorFollowSharpness = 12f;
        /// <summary>Angle below which a released, nearly-closed door latches shut with a click.</summary>
        [SerializeField, Range(2f, 30f)] private float m_doorLatchAngle = 10f;
        /// <summary>Open angle past which the door is treated as passable for teleport/walk navigation.</summary>
        [SerializeField, Range(20f, 90f)] private float m_doorNavigationOpenAngle = 60f;

        [Header("Controller hand visuals")]
        /// <summary>When true (and not using custom arms), show simple hand visuals on the controllers.</summary>
        [SerializeField] private bool m_showControllerHands = true;
        /// <summary>Uniform scale applied to the procedural controller hand visuals.</summary>
        [SerializeField, Min(0.1f)] private float m_controllerHandScale = 1f;
        /// <summary>When true (and not using custom arms), attach the procedural two-bone IK arms.</summary>
        [SerializeField] private bool m_showArms = true;
        // Optional: the SDK's authentic Meta/Lit hand material (BasicHandMaterial). Assigned in
        // the scene; if left null the hands fall back to a solid Standard skin material.
        /// <summary>Optional authentic Meta hand material; null falls back to a solid Standard skin.</summary>
        [SerializeField] private Material m_metaHandMaterial;

        [Header("Custom arms (Mixamo FBX)")]
        // Assign the imported arms FBX/prefab here to REPLACE the Meta hands + procedural arms with
        // a skinned mesh IK'd to the controllers. Leave null to keep the default hands. Tune the
        // offsets on-device: chest offset positions the torso under the headset, body-yaw flips the
        // facing if the model imports backward (try 180), and the hand options match the wrist to
        // the controller (off => the hand simply follows the forearm).
        /// <summary>Skinned arms FBX/prefab that, when assigned, replaces the Meta hands + procedural arms.</summary>
        [SerializeField] private GameObject m_customArmsPrefab;
        /// <summary>Offset from the headset to the arm rig's chest anchor (positions the torso under the head).</summary>
        [SerializeField] private Vector3 m_customArmsChestOffset = new Vector3(0f, -0.16f, -0.05f);
        /// <summary>Extra body yaw in degrees to flip a backward-imported model (try 180).</summary>
        [SerializeField, Range(-180f, 180f)] private float m_customArmsBodyYawOffset;
        // The arm rig is auto-scaled so its shoulder->wrist reach equals this (metres). Fixes a
        // mis-scaled FBX so the elbow can actually bend; raise it if the elbow still won't bend.
        /// <summary>Target shoulder-to-wrist reach (metres); the arm rig is auto-scaled to match.</summary>
        [SerializeField, Min(0.2f)] private float m_customArmsTargetReach = 0.64f;
        /// <summary>How far the fingers curl toward the palm, in degrees.</summary>
        [SerializeField, Range(0f, 130f)] private float m_customArmsFingerCurlAngle = 70f;
        /// <summary>Local rotation axis about which finger bones curl.</summary>
        [SerializeField] private Vector3 m_customArmsFingerCurlAxis = new Vector3(0f, 0f, 1f);
        /// <summary>Sign that flips the finger-curl direction to match the model's bone orientation.</summary>
        [SerializeField] private float m_customArmsFingerCurlSign = -1f; // curl direction (flipped)

        [Header("Barrel prefabs")]
        /// <summary>The three barrel model prefabs (55/30/5 gallon).</summary>
        [SerializeField] private BarrelPrefabSet m_barrelPrefabs;
        /// <summary>Optional data asset supplying realistic per-barrel background count values.</summary>
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        /// <summary>Initial import scale for the 55-gallon prefab before physical fitting.</summary>
        [SerializeField] private Vector3 m_barrel55ModelScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        /// <summary>Initial import scale for the 30-gallon prefab before physical fitting.</summary>
        [SerializeField] private Vector3 m_barrel30ModelScale = new Vector3(0.14f, 0.14f, 0.14f);
        /// <summary>Initial import scale for the 5-gallon prefab before physical fitting.</summary>
        [SerializeField] private Vector3 m_barrel5ModelScale = new Vector3(0.07f, 0.07f, 0.07f);
        // Colleague's authored PBR barrel materials (Standard shader, textures with labels baked in).
        // 55-gal + 30-gal share one Metal/Paint group; the 5-gal has its own. When assigned, each
        // barrel randomly gets the Metal or Paint variant for its size, replacing the procedural
        // tinting. Leave null to keep the procedural tints. The hot barrel looks identical (no tell).
        /// <summary>Authored metal PBR material for large (55/30 gal) barrels; null keeps procedural tints.</summary>
        [SerializeField] private Material m_barrelLargeMetal;   // 55 + 30 gal
        /// <summary>Authored painted PBR material for large (55/30 gal) barrels.</summary>
        [SerializeField] private Material m_barrelLargePaint;
        /// <summary>Authored metal PBR material for the small (5 gal) barrel.</summary>
        [SerializeField] private Material m_barrelSmallMetal;   // 5 gal
        /// <summary>Authored painted PBR material for the small (5 gal) barrel.</summary>
        [SerializeField] private Material m_barrelSmallPaint;
        // The procedural curved "55/30/5 GAL" size sticker. Her BaseColor textures carry the paint
        // look but no size text, so keep it on by default; turn off if her textures ever add labels.
        /// <summary>When true, wrap the procedural curved "NN GAL" size sticker onto each barrel.</summary>
        [SerializeField] private bool m_showBarrelSizeStickers = true;

        [Header("Hidden radiation source")]
        /// <summary>Minimum activity (counts per second) the hidden source can be assigned.</summary>
        [SerializeField, Min(1f)] private float m_minSourceActivityCps = 1500f;
        /// <summary>Maximum activity (counts per second) the hidden source can be assigned.</summary>
        [SerializeField, Min(1f)] private float m_maxSourceActivityCps = 30000f;

        [Header("Detector")]
        /// <summary>Proximity radius within which a controller can grab the detector off its pedestal.</summary>
        [SerializeField, Min(0.05f)] private float m_detectorGrabRadius = 0.18f;
        /// <summary>Offset from the spawn-room centre to the detector pedestal.</summary>
        [SerializeField] private Vector3 m_pedestalOffsetFromSpawnCenter = new Vector3(1.1f, 0f, 0.45f);
        // Assign the colleague's identifinderPrefab (combined model + TMP/slider screen) to use the
        // real detector instead of the procedural wand. Null => procedural DetectorModelBuilder. The
        // model is auto-scaled to m_detectorTargetHeight and re-centered (its prefab pivot is offset),
        // so import scale doesn't matter. Held pose is tunable on-device.
        /// <summary>Optional real identiFINDER prefab; null uses the procedural DetectorModelBuilder wand.</summary>
        [SerializeField] private GameObject m_detectorPrefab;
        /// <summary>Height in metres the detector model is auto-scaled to (import scale is normalised out).</summary>
        [SerializeField, Min(0.05f)] private float m_detectorTargetHeight = 0.25f;
        /// <summary>Local position of the detector relative to the holding controller when grabbed.</summary>
        [SerializeField] private Vector3 m_detectorHeldLocalPosition = new Vector3(0f, 0.040f, 0.065f);
        /// <summary>Local rotation (Euler) of the detector relative to the holding controller when grabbed.</summary>
        [SerializeField] private Vector3 m_detectorHeldLocalEuler = new Vector3(55f, 0f, 0f);

        [Header("Game loop")]
        // Player presses the wall START button to randomize a round, then aims the detector at the
        // barrel they suspect and presses A to submit. They get m_maxTries attempts per round.
        /// <summary>Number of guesses allowed per round before it resolves as a loss.</summary>
        [SerializeField, Min(1)] private int m_maxTries = 2;
        // When enabled, a round auto-resolves as a loss after m_roundDurationSeconds. The tutorial's
        // Mission Briefing promises the player "3 minutes", so this defaults on for the lab scene.
        /// <summary>When true, a round auto-resolves as a loss once the timer expires.</summary>
        [SerializeField] private bool m_enableRoundTimer = true;
        /// <summary>Round time limit in seconds when the timer is enabled (~3 minutes).</summary>
        [SerializeField, Min(10f)] private float m_roundDurationSeconds = 180f;
        /// <summary>Duration of the fade-from-black on scene entry.</summary>
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;
        /// <summary>Effective poke radius for the wall START button (larger = easier to trigger).</summary>
        [SerializeField, Min(0.04f)] private float m_startButtonPressRadius = 0.15f; // larger = easier to poke
        /// <summary>Maximum distance a guess aim ray reaches to find a barrel.</summary>
        [SerializeField, Min(0.5f)] private float m_guessRayLength = 12f;
        // Half-angle of the forgiving "aim cone" for submitting a guess: the player only needs to
        // point roughly at a drum, not pixel-perfectly. The closest-to-centre barrel within this
        // cone is the pick.
        /// <summary>Half-angle of the forgiving aim cone for submitting a guess (degrees).</summary>
        [SerializeField, Range(4f, 35f)] private float m_guessConeAngle = 34f; // very forgiving "general area" aim

        [Header("Feedback audio")]
        /// <summary>Chime played on a correct guess.</summary>
        [SerializeField] private AudioClip m_correctSubmitClip;
        /// <summary>Sting played on an incorrect guess.</summary>
        [SerializeField] private AudioClip m_incorrectSubmitClip;

        [Header("Scenario randomization")]
        /// <summary>Minimum number of barrels requested per round.</summary>
        [SerializeField, Min(1)] private int m_minBarrels = 11;
        /// <summary>Maximum number of barrels requested per round.</summary>
        [SerializeField, Min(1)] private int m_maxBarrels = 45;
        /// <summary>Minimum number of folding tables placed per round.</summary>
        [SerializeField, Min(0)] private int m_minTables;
        /// <summary>Maximum number of folding tables placed per round.</summary>
        [SerializeField, Min(0)] private int m_maxTables = 4;
        /// <summary>Minimum number of wall shelf units placed per round.</summary>
        [SerializeField, Min(0)] private int m_minShelfUnits = 2;
        /// <summary>Maximum number of wall shelf units placed per round.</summary>
        [SerializeField, Min(0)] private int m_maxShelfUnits = 9;
        /// <summary>How many differently-seeded layout attempts to try to fit all requested barrels.</summary>
        [SerializeField, Min(1)] private int m_layoutRetryCount = 10;
        /// <summary>When true, use m_fixedSeed for deterministic layouts instead of a time-based seed.</summary>
        [SerializeField] private bool m_useFixedSeed;
        /// <summary>Deterministic RNG seed used when m_useFixedSeed is enabled.</summary>
        [SerializeField] private int m_fixedSeed = 12345;

        [Header("Visibility and walkability")]
        /// <summary>Radius around the player start kept clear of barrels/props.</summary>
        [SerializeField, Min(0.1f)] private float m_playerClearance = 0.8f;
        /// <summary>Approximate player body radius used for doorway-crossing width checks.</summary>
        [SerializeField, Min(0.05f)] private float m_playerRadius = 0.28f;
        /// <summary>Width of the always-clear cross-shaped central aisle through the lab.</summary>
        [SerializeField, Min(0.2f)] private float m_centralAisleWidth = 0.95f;
        /// <summary>Minimum centre-to-centre spacing between floor barrels.</summary>
        [SerializeField, Min(0.05f)] private float m_floorBarrelSpacing = 0.64f;
        /// <summary>Minimum spacing between barrels sharing a table top.</summary>
        [SerializeField, Min(0.01f)] private float m_tableBarrelSpacing = 0.34f;
        /// <summary>Minimum spacing between barrels sharing a shelf tier.</summary>
        [SerializeField, Min(0.01f)] private float m_shelfBarrelSpacing = 0.28f;
        // Tight per-barrel walk/teleport keep-out so the player can step right up to a barrel to
        // scan it (net keep-out ~= barrel radius + this) without standing inside the cylinder.
        /// <summary>Extra padding beyond a floor barrel's radius that the player is kept out of.</summary>
        [SerializeField, Min(0.01f)] private float m_barrelClearance = 0.05f;

        [Header("Shelf asset")]
        /// <summary>Optional wall shelf model prefab; null uses procedurally-built shelf boards.</summary>
        [SerializeField] private GameObject m_wallShelfPrefab;
        /// <summary>Small gap added above a shelf surface so placed barrels don't z-fight the board.</summary>
        [SerializeField, Min(0.001f)] private float m_shelfSurfaceClearance = 0.015f;

        [Header("Locomotion")]
        /// <summary>When true, thumbstick smooth locomotion is enabled (overridden by start-screen choice).</summary>
        [SerializeField] private bool m_enableSmoothMove = true;
        /// <summary>When true, controller-aimed teleport locomotion is enabled.</summary>
        [SerializeField] private bool m_enableTeleport = true;
        /// <summary>Smooth movement speed in metres/second.</summary>
        [SerializeField, Min(0.1f)] private float m_smoothMoveSpeed = 1.35f;
        /// <summary>Degrees turned per snap-turn flick.</summary>
        [SerializeField, Min(5f)] private float m_snapTurnDegrees = 30f;
        /// <summary>Cooldown between snap turns, in seconds.</summary>
        [SerializeField, Min(0.05f)] private float m_snapTurnCooldown = 0.35f;
        /// <summary>Thumbstick magnitude below which input is ignored.</summary>
        [SerializeField, Min(0.05f)] private float m_thumbstickDeadzone = 0.18f;
        // Teleport reliability: aim snaps to the nearest standable spot within a small search ring
        // instead of being rejected outright, so near-wall/near-edge aims always land. m_edgeMargin
        // is how close to a wall you can stand (was a 0.36 m dead-zone); keep it < m_playerRadius.
        /// <summary>How close to a wall/edge the player may stand (keep-out band width).</summary>
        [SerializeField, Min(0.05f)] private float m_edgeMargin = 0.2f;
        /// <summary>Ring step size used when searching outward for the nearest standable teleport spot.</summary>
        [SerializeField, Min(0.04f)] private float m_teleportSnapStep = 0.12f;
        /// <summary>Maximum teleport distance in metres.</summary>
        [SerializeField, Min(2f)] private float m_maxTeleportDistance = 16f;
        /// <summary>Exponential smoothing rate for the teleport marker to kill controller-ray jitter.</summary>
        [SerializeField, Min(1f)] private float m_teleportMarkerSmoothing = 18f;

        [Header("Fallback counts")]
        /// <summary>Size of the pre-generated count pool used when no RadiationCountProfile is assigned.</summary>
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        /// <summary>Minimum value in the fallback background-count pool.</summary>
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        /// <summary>Maximum value in the fallback background-count pool.</summary>
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        /// <summary>All BarrelInstance components spawned for the current round.</summary>
        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        /// <summary>Every scenario GameObject created this round, destroyed on ClearScenario.</summary>
        private readonly List<GameObject> m_spawnedObjects = new();
        /// <summary>Metadata for each placed folding table.</summary>
        private readonly List<PropInfo> m_tables = new();
        /// <summary>Metadata for each placed wall shelf.</summary>
        private readonly List<PropInfo> m_shelves = new();
        /// <summary>Shuffled candidate barrel spawn slots for the current layout.</summary>
        private readonly List<SpawnSlot> m_spawnSlots = new();
        /// <summary>Barrels actually placed this round (for spacing/visibility tests).</summary>
        private readonly List<PlacedBarrel> m_placedBarrels = new();
        /// <summary>Furniture/pedestal footprints the player is kept out of (player-radius padding).</summary>
        private readonly List<ObstacleRect> m_navigationObstacles = new();
        // Floor-barrel keep-outs are kept separate from m_navigationObstacles so they can use the
        // tight m_barrelClearance padding (not the full player-radius furniture padding).
        /// <summary>Floor-barrel keep-outs using the tight m_barrelClearance padding.</summary>
        private readonly List<ObstacleRect> m_barrelObstacles = new();
        /// <summary>All runtime-created materials, destroyed in OnDestroy to avoid leaks.</summary>
        private readonly List<Material> m_runtimeMaterials = new();
        /// <summary>Cache of materials keyed by their creation parameters so they are shared.</summary>
        private readonly Dictionary<string, Material> m_materialCache = new();
        /// <summary>Cache of the fitted local scale per barrel size (computed once per size).</summary>
        private readonly Dictionary<BarrelSize, Vector3> m_fittedScaleCache = new();
        /// <summary>Cache of the tint/PBR material variant set per barrel size.</summary>
        private readonly Dictionary<BarrelSize, Material[]> m_barrelTintVariants = new();
        /// <summary>Cache of the curved size-sticker mesh per barrel size.</summary>
        private readonly Dictionary<BarrelSize, Mesh> m_barrelLabelMeshes = new();

        /// <summary>Pre-generated pool of background count values used when no profile is assigned.</summary>
        private int[] m_fallbackCountPool;
        /// <summary>The center-eye/head transform (drives head-locked UI and camera-fallback aiming).</summary>
        private Transform m_cameraTransform;
        /// <summary>The transform moved/rotated by locomotion (the rig root, or the fallback camera).</summary>
        private Transform m_locomotionRoot;
        /// <summary>The active OVRCameraRig, or null in the non-VR fallback path.</summary>
        private OVRCameraRig m_cameraRig;
        /// <summary>Root of the built static room geometry (walls, floor, props, door, button).</summary>
        private Transform m_roomRoot;
        /// <summary>Root of the per-round randomized scenario (barrels/tables/shelves); destroyed on regen.</summary>
        private Transform m_scenarioRoot;
        /// <summary>The floor ring visual shown at the aimed teleport destination.</summary>
        private GameObject m_teleportMarker;
        /// <summary>Renderer of the teleport marker (swapped between valid/invalid materials).</summary>
        private Renderer m_teleportMarkerRenderer;
        /// <summary>Green emissive material for a valid teleport target.</summary>
        private Material m_validTeleportMaterial;
        /// <summary>Red emissive material for a blocked teleport target.</summary>
        private Material m_invalidTeleportMaterial;
        /// <summary>Head-locked message panel root used for hints and game-loop results.</summary>
        private GameObject m_messagePanel;
        /// <summary>Text mesh of the message panel.</summary>
        private TextMesh m_messageText;
        /// <summary>Transparent backing material of the message panel (fades with the text).</summary>
        private Material m_messageBackingMaterial;
        /// <summary>Backing quad transform, auto-sized to fit the current message text.</summary>
        private Transform m_messageBacking;
        /// <summary>Time.time when the current message began (drives fade in/hold/out).</summary>
        private float m_messageStartTime;
        /// <summary>How long the current message holds at full opacity before fading out.</summary>
        private float m_messageHoldDuration;
        /// <summary>Base colour of the current message text.</summary>
        private Color m_messageColor = new Color(1f, 0.86f, 0.2f);
        /// <summary>True while the backing box still needs resizing to the freshly-set text.</summary>
        private bool m_messageResizePending;
        /// <summary>Hinge transform the swinging connecting door rotates about.</summary>
        private Transform m_doorPivot;
        /// <summary>The lab-side door knob (grab/poke target).</summary>
        private Transform m_labDoorKnob;
        /// <summary>The spawn-room-side door knob (grab/poke target).</summary>
        private Transform m_spawnDoorKnob;
        /// <summary>Current door hinge angle in degrees.</summary>
        private float m_doorCurrentAngle;
        /// <summary>Controller currently manually swinging the door, or None.</summary>
        private OVRInput.Controller m_doorGrabController = OVRInput.Controller.None;
        /// <summary>Last controller that grabbed the door (used for the latch haptic).</summary>
        private OVRInput.Controller m_lastDoorGrabController = OVRInput.Controller.None;
        /// <summary>Anchor transform of the hand currently gripping the door.</summary>
        private Transform m_doorGrabAnchor;
        /// <summary>Angle offset captured at grab so the door doesn't jump to the hand angle.</summary>
        private float m_doorGrabAngleOffset;
        private bool m_doorAutoActive;   // poke-driven auto open/close (overridden by a manual swing)
        private float m_doorAutoTarget;  // 0 = closed, m_doorOpenAngle = open
        /// <summary>Re-armed once the hand leaves the knob so each fresh poke toggles the door once.</summary>
        private bool m_doorAutoArmed = true;
        /// <summary>True while a left-controller haptic pulse is scheduled to stop.</summary>
        private bool m_leftPulseActive;
        /// <summary>Time.time at which the left haptic pulse should stop.</summary>
        private float m_leftPulseEndTime;
        /// <summary>True while a right-controller haptic pulse is scheduled to stop.</summary>
        private bool m_rightPulseActive;
        /// <summary>Time.time at which the right haptic pulse should stop.</summary>
        private float m_rightPulseEndTime;
        /// <summary>Left controller hand visual instance.</summary>
        private GameObject m_leftHandVisual;
        /// <summary>Right controller hand visual instance.</summary>
        private GameObject m_rightHandVisual;
        /// <summary>The procedural two-bone IK arm rig (used when custom arms aren't assigned).</summary>
        private ProceduralArmRig m_armRig;
        /// <summary>Instantiated custom (Mixamo) arms GameObject.</summary>
        private GameObject m_customArmsInstance;
        /// <summary>The MixamoArmRig driving the custom arms from the OVR rig.</summary>
        private MixamoArmRig m_customArmRig;
        /// <summary>Root of the grabbable detector (its local +Y is the aim/sensor axis).</summary>
        private GameObject m_detectorRoot;
        /// <summary>Proximity-grab component on the detector.</summary>
        private GrabbableTool m_detectorGrabTool;
        /// <summary>Sensor tip transform at the top of the detector (aim ray origin).</summary>
        private Transform m_detectorSensorTip;
        /// <summary>Home position the detector rests at on its pedestal.</summary>
        private Vector3 m_detectorHomePosition;
        /// <summary>Home rotation the detector rests at on its pedestal.</summary>
        private Quaternion m_detectorHomeRotation = Quaternion.identity;
        /// <summary>Footprint keep-out for the detector pedestal.</summary>
        private ObstacleRect m_pedestalFootprint;
        /// <summary>True once the pedestal has been built and its footprint is valid.</summary>
        private bool m_hasPedestal;
        /// <summary>The RadiationSource component attached to the current hidden radioactive barrel.</summary>
        private RadiationSource m_hotSource;

        // --- Game loop (Waiting -> Playing -> Resolved) ---
        // The wall START button drives randomization; the freed-up A button submits a guess by
        // pointing the detector at a barrel. Public hooks below let a designer port in their own UI.
        /// <summary>Current game-loop phase (Waiting for START, Playing, or Resolved).</summary>
        private SimulationPhase m_phase = SimulationPhase.Waiting;
        /// <summary>Number of incorrect guesses used this round.</summary>
        private int m_triesUsed;
        /// <summary>Time.time the current round began.</summary>
        private float m_roundStartTime;
        /// <summary>Next time a periodic "time remaining" status line should be logged.</summary>
        private float m_nextTimerStatusTime;
        // End-of-round stats: barrels the player swept with the held detector, and the elapsed time
        // captured at resolution (RoundElapsedSeconds returns 0 once the phase flips to Resolved).
        /// <summary>Set of barrels the held detector has been aimed at (drives the scan-accuracy stat).</summary>
        private readonly HashSet<BarrelInstance> m_scannedBarrels = new HashSet<BarrelInstance>();
        /// <summary>Elapsed round time captured at resolution (before the phase flip zeroes it).</summary>
        private float m_roundElapsedAtResolve;
        /// <summary>Root of the wall-mounted START button assembly.</summary>
        private GameObject m_startButtonRoot;
        /// <summary>The pokeable button cap that travels inward when pushed.</summary>
        private Transform m_startButtonCap;
        /// <summary>The START/IN-ROUND text on the button cap.</summary>
        private TextMesh m_startButtonLabel;
        /// <summary>Unique emissive material recolored per phase for the button cap.</summary>
        private Material m_startButtonMaterial;
        /// <summary>Rest (un-pressed) local position of the button cap.</summary>
        private Vector3 m_startButtonCapRestPosition;
        /// <summary>Re-armed once the hand leaves so the button fires once per press.</summary>
        private bool m_startButtonArmed = true;
        /// <summary>True while a hand is currently touching the button (for a first-contact tick).</summary>
        private bool m_startButtonTouching;

        /// <summary>The exact snapped teleport destination committed on trigger.</summary>
        private Vector3 m_currentTeleportTarget;
        /// <summary>True when the aimed teleport target is currently valid (standable).</summary>
        private bool m_hasValidTeleportTarget;
        /// <summary>Jitter-smoothed marker position (visual only; commit uses the exact target).</summary>
        private Vector3 m_teleportMarkerSmoothedPosition;
        /// <summary>Earliest Time.time the next snap turn is allowed.</summary>
        private float m_nextSnapTurnTime;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Dev-only toggle showing each barrel's debug count label.</summary>
        private bool m_debugLabelsVisible;
#endif

        /// <summary>Lab room footprint in metres (clamped to a minimum 8 ft edge).</summary>
        private Vector2 RoomSizeMeters => new(
            Mathf.Max(8f, m_roomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_roomSizeFeet.y) * FeetToMeters);

        /// <summary>Spawn room footprint in metres (clamped to a minimum 8 ft edge).</summary>
        private Vector2 SpawnRoomSizeMeters => new(
            Mathf.Max(8f, m_spawnRoomSizeFeet.x) * FeetToMeters,
            Mathf.Max(8f, m_spawnRoomSizeFeet.y) * FeetToMeters);

        /// <summary>World centre of the spawn room, placed just south of the lab across the shared wall.</summary>
        private Vector3 SpawnRoomCenter
        {
            get
            {
                var roomSize = RoomSizeMeters;
                var spawnSize = SpawnRoomSizeMeters;
                return new Vector3(0f, 0f, -roomSize.y * 0.5f - spawnSize.y * 0.5f);
            }
        }

        /// <summary>Player start position, snapped into the spawn room if the serialized value lies outside it.</summary>
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

        /// <summary>True when the door is open far enough to walk/teleport through it.</summary>
        private bool IsDoorOpenForNavigation => Mathf.Abs(m_doorCurrentAngle) >= m_doorNavigationOpenAngle;

        /// <summary>Unity lifecycle: ensures the camera rig exists before anything else runs.</summary>
        private void Awake()
        {
            m_cameraTransform = EnsureCameraRig();
        }

        // Apply the movement style chosen on the start screen (persisted via PlayerPrefs). If no choice
        // was recorded (e.g. this scene launched directly), keep the serialized flags. Snap turn in the
        // lab is unconditional, so it stays on either way.
        /// <summary>Applies the smooth/teleport movement mode chosen on the start screen (via PlayerPrefs).</summary>
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

        /// <summary>
        /// Unity lifecycle: builds the room, hands, arms, detector and START button, then leaves the
        /// lab in the Waiting phase until the player pokes START (which is the only thing that
        /// randomizes a round).
        /// </summary>
        private void Start()
        {
            ApplyMovementPreference();
            ConfigureAmbientLighting();
            EnsureScreenFade();

            if (m_buildRoomGeometry)
            {
                BuildRoomGeometry();
            }

            EnsureControllerHandVisuals();
            EnsureArmRig();
            EnsureCustomArms();
            EnsureDetector();
            EnsureStartButton();

            // Polished game loop: the lab stays empty until the player presses the wall START
            // button. That press is now the ONLY thing that randomizes the round.
            m_phase = SimulationPhase.Waiting;
            RefreshStartButtonVisual();
            SetStatus("Press the START button on the wall to begin.");
            ShowMessage("Press START on the wall\nto begin a round", new Color(0.7f, 0.95f, 1f), 4f);
        }

        /// <summary>
        /// Unity lifecycle: per-frame pump for hands/arms, locomotion, door, haptics, the START
        /// button, the round timer, scan tracking, and guess submission (plus editor/dev shortcuts).
        /// </summary>
        private void Update()
        {
            EnsureControllerHandVisuals();
            EnsureArmRig();
            EnsureCustomArms();
            HandleLocomotion();
            HandleDoorGrab();
            UpdateDoorMotion();
            UpdateHapticPulse();
            UpdateStartButton();
            UpdateRoundTimer();
            PollScannedBarrels();

            // A button (or hand pinch) submits a guess: whatever barrel the detector is aimed at
            // is the player's answer. The A button no longer reshuffles the barrels — only the
            // wall START button does. Submission is a no-op outside an active round.
            if (IsSubmitPressed())
            {
                SubmitGuess();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Dev-only helpers (not part of the player game loop): B re-rolls a round, X re-rolls
            // counts + hidden source, Y toggles debug labels. Players can no longer change the
            // layout once it is shuffled.
            if (InputManager.IsButtonBDownOrMiddleFingerPinchStarted())
            {
                StartSimulation();
            }

            if (InputManager.IsButtonXDown() && !IsDetectorHeldLeft())
            {
                RandomizeRadiationCounts();
                AssignHotSource();
            }

            if (InputManager.IsButtonYDown())
            {
                m_debugLabelsVisible = !m_debugLabelsVisible;
                foreach (var barrel in m_spawnedBarrels)
                {
                    if (barrel != null)
                    {
                        barrel.SetDebugLabelVisible(m_debugLabelsVisible);
                    }
                }
            }
#endif

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            // Keyboard fallbacks for testing in the editor without a headset: B = press START,
            // N = submit the guess the camera/detector is aimed at.
            if (UnityEngine.InputSystem.Keyboard.current != null)
            {
                if (UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame)
                {
                    StartSimulation();
                }

                if (UnityEngine.InputSystem.Keyboard.current.nKey.wasPressedThisFrame)
                {
                    SubmitGuess();
                }
            }
#endif
        }

        /// <summary>True when the detector is currently held in the left hand.</summary>
        private bool IsDetectorHeldLeft()
        {
            return m_detectorGrabTool != null && m_detectorGrabTool.HeldController == OVRInput.Controller.LTouch;
        }

        // Submit = A / index pinch; also X when the detector is held in the LEFT hand, so a left-handed
        // grip can submit with the same hand.
        /// <summary>True this frame when the player pressed the submit-guess input.</summary>
        private bool IsSubmitPressed()
        {
            return InputManager.IsButtonADownOrPinchStarted()
                || (IsDetectorHeldLeft() && OVRInput.GetDown(OVRInput.RawButton.X));
        }

        // ---------------------------------------------------------------------------------------
        // Public game-loop API. These are the integration hooks for a designer-authored UI/UX:
        // call StartSimulation()/SubmitGuess() from buttons, and subscribe to the events to drive
        // custom pop-ups. The placeholder in-world button + head-locked messages below are wired
        // through these same entry points and can be removed once a real UI is ported in.
        // ---------------------------------------------------------------------------------------

        /// <summary>The three states of the find-the-barrel round.</summary>
        public enum SimulationPhase { Waiting, Playing, Resolved }

        /// <summary>Result of a submitted guess.</summary>
        public enum GuessOutcome { NoTarget, Incorrect, Correct, TimeExpired }

        /// <summary>Fires when a fresh round has been randomized and is ready to play.</summary>
        public event Action OnSimulationStarted;

        /// <summary>Fires after every submitted guess: (outcome, triesUsed, maxTries).</summary>
        public event Action<GuessOutcome, int, int> OnGuessResolved;

        /// <summary>The current game-loop phase.</summary>
        public SimulationPhase Phase => m_phase;

        /// <summary>Number of incorrect guesses used this round.</summary>
        public int TriesUsed => m_triesUsed;

        /// <summary>Maximum guesses allowed per round (at least 1).</summary>
        public int MaxTries => Mathf.Max(1, m_maxTries);

        /// <summary>Guesses still remaining this round.</summary>
        public int TriesRemaining => Mathf.Max(0, MaxTries - m_triesUsed);

        /// <summary>Configured round duration in seconds (at least 10).</summary>
        public float RoundDurationSeconds => Mathf.Max(10f, m_roundDurationSeconds);

        /// <summary>Seconds elapsed in the current round (0 unless actively Playing).</summary>
        public float RoundElapsedSeconds => m_phase == SimulationPhase.Playing
            ? Mathf.Max(0f, Time.time - m_roundStartTime)
            : 0f;

        /// <summary>Seconds remaining in the current round (0 unless the timer is on and Playing).</summary>
        public float RoundRemainingSeconds => m_enableRoundTimer && m_phase == SimulationPhase.Playing
            ? Mathf.Max(0f, RoundDurationSeconds - RoundElapsedSeconds)
            : 0f;

        /// <summary>True while a round is actively being played.</summary>
        public bool IsRoundActive => m_phase == SimulationPhase.Playing;

        /// <summary>Randomize a new round and begin play. Safe to call from any phase.</summary>
        public void StartSimulation()
        {
            GenerateRun();
            m_triesUsed = 0;
            m_scannedBarrels.Clear();
            m_roundStartTime = Time.time;
            m_nextTimerStatusTime = Time.time;
            m_phase = SimulationPhase.Playing;
            RefreshStartButtonVisual();
            SetStatus(m_enableRoundTimer
                ? $"Round started — {FormatRoundTime(RoundRemainingSeconds)} to find the hidden material."
                : "Round started — find the hidden radioactive material.");
            ShowMessage(m_enableRoundTimer
                ? $"Find the radioactive material\n{FormatRoundTime(RoundRemainingSeconds)} on the clock"
                : "Find the radioactive\nmaterial", new Color(0.7f, 0.95f, 1f), 3f);
            OnSimulationStarted?.Invoke();
        }

        /// <summary>
        /// Submit the barrel the detector is currently pointed at as the player's answer.
        /// Returns the outcome and (placeholder) shows a red/green result message.
        /// </summary>
        public GuessOutcome SubmitGuess()
        {
            if (m_phase != SimulationPhase.Playing)
            {
                return GuessOutcome.NoTarget;
            }

            // Capture elapsed time before any phase flip (RoundElapsedSeconds returns 0 once Resolved).
            m_roundElapsedAtResolve = RoundElapsedSeconds;

            var target = GetAimedBarrel();
            if (target == null)
            {
                ShowMessage("Point the detector at a\nbarrel, then press submit",
                    new Color(1f, 0.86f, 0.2f), 2.5f);
                return GuessOutcome.NoTarget;
            }

            var correct = m_hotSource != null && target.gameObject == m_hotSource.gameObject;
            GuessOutcome outcome;
            if (correct)
            {
                outcome = GuessOutcome.Correct;
                m_phase = SimulationPhase.Resolved;
                PulseHaptic(DetectorController(), 0.35f, 0.6f, 0.25f);
                SetStatus("Correct! Press START for a new round.");
            }
            else
            {
                m_triesUsed++;
                outcome = GuessOutcome.Incorrect;
                if (m_triesUsed >= MaxTries)
                {
                    m_phase = SimulationPhase.Resolved;
                    SetStatus("Out of tries. Press START for a new round.");
                }
                else
                {
                    var left = TriesRemaining;
                    ShowMessage($"That is incorrect\n{left} {(left == 1 ? "try" : "tries")} left",
                        new Color(1f, 0.25f, 0.25f), 4f);
                    SetStatus($"Incorrect — {left} {(left == 1 ? "try" : "tries")} remaining.");
                }

                PulseHaptic(DetectorController(), 0.5f, 0.5f, 0.12f);
            }

            PlaySubmitFeedback(correct);
            RefreshStartButtonVisual();
            if (m_phase == SimulationPhase.Resolved)
            {
                ShowStatsPanel(correct);
            }

            OnGuessResolved?.Invoke(outcome, m_triesUsed, MaxTries);
            return outcome;
        }

        /// <summary>Lazily-created 2D audio source that plays the submit-guess win/loss clips.</summary>
        private AudioSource m_submitAudioSource;

        // Win chime on a correct guess, loss sting on a wrong one (2D so it's heard anywhere).
        /// <summary>Plays the correct/incorrect submit feedback clip.</summary>
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

        // Poll the barrel the held detector is aimed at and record it as "scanned" (accuracy stat).
        /// <summary>While playing with the detector held, records the aimed-at barrel as scanned.</summary>
        private void PollScannedBarrels()
        {
            if (m_phase != SimulationPhase.Playing)
            {
                return;
            }

            if (m_detectorGrabTool == null || !m_detectorGrabTool.IsHeld)
            {
                return;
            }

            var aimed = GetAimedBarrel();
            if (aimed != null)
            {
                m_scannedBarrels.Add(aimed);
            }
        }

        /// <summary>Counts scanned barrels that still exist (ignores any destroyed entries).</summary>
        private int CountScannedAlive()
        {
            var count = 0;
            foreach (var barrel in m_scannedBarrels)
            {
                if (barrel != null)
                {
                    count++;
                }
            }

            return count;
        }

        // End-of-round stats panel: total time, scan accuracy, barrels remaining. Reuses the head-locked
        // transparent message panel; a long hold keeps it up until the next round's StartSimulation
        // message replaces it.
        /// <summary>Shows the end-of-round stats (time, scan accuracy, barrels remaining) on the message panel.</summary>
        private void ShowStatsPanel(bool correct)
        {
            var total = m_spawnedBarrels.Count;
            var scanned = CountScannedAlive();
            var remaining = Mathf.Max(0, total - scanned);
            var pct = total > 0 ? Mathf.RoundToInt(100f * scanned / total) : 0;
            var header = correct ? "MATERIAL FOUND" : "ROUND OVER";
            var color = correct ? new Color(0.25f, 1f, 0.4f) : new Color(1f, 0.45f, 0.2f);
            ShowMessage(
                $"{header}\nTIME  {FormatRoundTime(m_roundElapsedAtResolve)}\n" +
                $"SCANNED  {scanned}/{total}  ({pct}%)\nREMAINING  {remaining}\n" +
                "Press START for a new round", color, 9999f);
        }

        /// <summary>Counts down the round clock, logging periodic status and resolving a loss at zero.</summary>
        private void UpdateRoundTimer()
        {
            if (!m_enableRoundTimer || m_phase != SimulationPhase.Playing)
            {
                return;
            }

            var remaining = RoundRemainingSeconds;
            if (remaining <= 0f)
            {
                ResolveTimeExpired();
                return;
            }

            if (Time.time >= m_nextTimerStatusTime)
            {
                SetStatus($"{FormatRoundTime(remaining)} remaining.");
                m_nextTimerStatusTime = Time.time + 15f;
            }
        }

        /// <summary>Resolves the round as a timed-out loss and notifies subscribers.</summary>
        private void ResolveTimeExpired()
        {
            if (m_phase != SimulationPhase.Playing)
            {
                return;
            }

            m_phase = SimulationPhase.Resolved;
            RefreshStartButtonVisual();
            ShowMessage("Time expired\nPress START to retry", new Color(1f, 0.45f, 0.2f), 6f);
            SetStatus("Time expired. Press START for a new round.");
            OnGuessResolved?.Invoke(GuessOutcome.TimeExpired, m_triesUsed, MaxTries);
        }

        /// <summary>Formats a seconds value as a "M:SS" clock string.</summary>
        private static string FormatRoundTime(float seconds)
        {
            var clampedSeconds = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{clampedSeconds / 60}:{clampedSeconds % 60:00}";
        }

        /// <summary>
        /// The barrel the detector (or, if not held, the right controller) is aimed at. Uses a
        /// forgiving angular cone rather than a pin-thin ray: the closest-to-centre barrel within
        /// m_guessConeAngle and m_guessRayLength is the pick, so the player only has to point
        /// roughly at a drum. Falls back to a direct raycast hit so a dead-on point always counts.
        /// </summary>
        public BarrelInstance GetAimedBarrel()
        {
            if (!TryGetAimRay(out var origin, out var direction))
            {
                return null;
            }

            BarrelInstance best = null;
            var bestAngle = Mathf.Max(2f, m_guessConeAngle);
            var maxDistance = Mathf.Max(0.5f, m_guessRayLength);

            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel == null)
                {
                    continue;
                }

                var collider = barrel.GetComponentInChildren<Collider>();
                var center = collider != null ? collider.bounds.center : barrel.transform.position;
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

            // Dead-on physics hit wins even if it is just outside the cone math (e.g. a very close,
            // wide drum), so pointing straight at a barrel is never rejected.
            if (best == null && Physics.Raycast(origin, direction, out var hit, maxDistance, ~0, QueryTriggerInteraction.Ignore))
            {
                best = hit.collider.GetComponentInParent<BarrelInstance>();
            }

            return best;
        }

        /// <summary>
        /// Resolves the current aim ray: the detector sensor tip/up when held, else the right
        /// controller, else the camera. Returns false only if none are available.
        /// </summary>
        private bool TryGetAimRay(out Vector3 origin, out Vector3 direction)
        {
            if (m_detectorGrabTool != null && m_detectorGrabTool.IsHeld && m_detectorSensorTip != null && m_detectorRoot != null)
            {
                origin = m_detectorSensorTip.position;
                direction = m_detectorRoot.transform.up;
                return true;
            }

            if (m_cameraRig != null && m_cameraRig.rightControllerAnchor != null)
            {
                origin = m_cameraRig.rightControllerAnchor.position;
                direction = m_cameraRig.rightControllerAnchor.forward;
                return true;
            }

            if (m_cameraTransform != null)
            {
                origin = m_cameraTransform.position;
                direction = m_cameraTransform.forward;
                return true;
            }

            origin = Vector3.zero;
            direction = Vector3.forward;
            return false;
        }

        /// <summary>The controller holding the detector, or the right controller if it isn't held.</summary>
        private OVRInput.Controller DetectorController()
        {
            return m_detectorGrabTool != null && m_detectorGrabTool.IsHeld
                ? m_detectorGrabTool.HeldController
                : OVRInput.Controller.RTouch;
        }

        /// <summary>
        /// Clears and rebuilds the randomized scenario (props, slots, barrels), retrying several
        /// seeds to fit all requested barrels, then assigns exactly one hidden radioactive source.
        /// </summary>
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

            AssignHotSource();

            var finalCount = foundFullScenario ? requestedCount : bestResult.SpawnedCount;
            SetStatus(finalCount >= requestedCount
                ? $"Randomized lab room: {finalCount} barrels. One hidden source assigned."
                : $"Requested {requestedCount}; placed {finalCount} visible/walkable barrels. One hidden source assigned.");
        }

        /// <summary>Destroys all per-round scenario objects and resets the placement/obstacle bookkeeping.</summary>
        public void ClearScenario()
        {
            RemoveHotSource();

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
            m_barrelObstacles.Clear();
            RestorePersistentObstacles();

            if (m_scenarioRoot != null)
            {
                Destroy(m_scenarioRoot.gameObject);
                m_scenarioRoot = null;
            }
        }

        /// <summary>Re-rolls every barrel's background count value and re-assigns the hidden source.</summary>
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

            SetStatus("Re-rolled radiation values and the hidden source.");
        }

        /// <summary>
        /// Picks one random barrel as the hidden source and attaches a RadiationSource with a
        /// log-uniform activity and a random isotope label. All other barrels stay inert.
        /// </summary>
        private void AssignHotSource()
        {
            RemoveHotSource();
            if (m_spawnedBarrels.Count == 0)
            {
                return;
            }

            var index = UnityEngine.Random.Range(0, m_spawnedBarrels.Count);
            var barrel = m_spawnedBarrels[index];
            if (barrel == null)
            {
                return;
            }

            var safeMin = Mathf.Max(1f, Mathf.Min(m_minSourceActivityCps, m_maxSourceActivityCps));
            var safeMax = Mathf.Max(safeMin, Mathf.Max(m_minSourceActivityCps, m_maxSourceActivityCps));
            var activity = Mathf.Pow(10f, UnityEngine.Random.Range(Mathf.Log10(safeMin), Mathf.Log10(safeMax)));
            var isotope = s_isotopeNames[UnityEngine.Random.Range(0, s_isotopeNames.Length)];

            m_hotSource = barrel.gameObject.AddComponent<RadiationSource>();
            m_hotSource.Configure(activity, isotope);
        }

        /// <summary>Destroys the current hidden RadiationSource component, if any.</summary>
        private void RemoveHotSource()
        {
            if (m_hotSource != null)
            {
                Destroy(m_hotSource);
            }

            m_hotSource = null;
        }

        /// <summary>Adds an OVRScreenFade to the camera so the lab fades up from black on load.</summary>
        private void EnsureScreenFade()
        {
            // Fade up from black when the lab loads (pairs with the tutorial's fade-out). OVRScreenFade
            // renders a world-space quad on the camera, so it shows correctly in the HMD.
            if (OVRScreenFade.instance != null)
            {
                return;
            }

            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            var fade = cam.gameObject.AddComponent<OVRScreenFade>();
            fade.fadeOnStart = true;
            fade.fadeTime = m_transitionFadeSeconds;
        }

        /// <summary>
        /// Finds or spawns the OVRCameraRig (or a plain fallback camera), positions it at the player
        /// start, and returns the head/eye transform used for head-locked UI and aiming.
        /// </summary>
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
                    EnsureAudioListener();
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
                EnsureAudioListener();
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

        /// <summary>Ensures an active AudioListener exists (adds one to the rig's eye anchor if needed).</summary>
        private void EnsureAudioListener()
        {
            // The scene's Main Camera owns the only AudioListener and gets deactivated when the
            // OVR rig spawns; OVRCameraRig never adds one, so geiger audio would be silent on device.
            var existing = FindAnyObjectByType<AudioListener>();
            if (existing != null && existing.isActiveAndEnabled)
            {
                return;
            }

            if (m_cameraRig != null && m_cameraRig.centerEyeAnchor != null
                && m_cameraRig.centerEyeAnchor.GetComponent<AudioListener>() == null)
            {
                m_cameraRig.centerEyeAnchor.gameObject.AddComponent<AudioListener>();
            }
        }

        /// <summary>Applies the desired camera settings (skybox clear, clip planes, HDR off) to the rig.</summary>
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

        /// <summary>Sets up trilight ambient colours and disables directional sun shadows (Quest cost).</summary>
        private void ConfigureAmbientLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.67f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.43f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.23f, 0.22f, 0.21f);

            // Realtime sun shadows add Quest cost and only darken an enclosed interior.
            foreach (var sceneLight in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (sceneLight.type == LightType.Directional)
                {
                    sceneLight.shadows = LightShadows.None;
                }
            }
        }

        /// <summary>True when a custom arms prefab is assigned (which replaces the Meta hands + procedural arms).</summary>
        private bool UseCustomArms => m_customArmsPrefab != null;

        /// <summary>Creates/toggles the controller hand visuals (Meta hands or a procedural fallback).</summary>
        private void EnsureControllerHandVisuals()
        {
            // Custom arms replace the Meta hands entirely.
            if (UseCustomArms)
            {
                SetControllerHandVisualActive(false);
                return;
            }

            if (!m_showControllerHands || m_cameraRig == null)
            {
                SetControllerHandVisualActive(false);
                return;
            }

            m_cameraRig.EnsureGameObjectIntegrity();
            MetaQuestHandVisuals.OverrideHandMaterial = m_metaHandMaterial;
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

        /// <summary>Lazily creates the procedural two-bone IK arm rig (unless custom arms are used).</summary>
        private void EnsureArmRig()
        {
            // Custom arms supply their own (skinned) arms; skip the procedural capsule arms.
            if (UseCustomArms || !m_showArms || m_armRig != null || m_cameraRig == null)
            {
                return;
            }

            var armObject = new GameObject("Procedural Arm Rig");
            armObject.transform.SetParent(transform, false);
            m_armRig = armObject.AddComponent<ProceduralArmRig>();
            var sleeveMaterial = CreateMaterial(new Color(0.78f, 0.80f, 0.82f), "Lab Coat Sleeve", 0f, 0.3f, null, null);
            var skinMaterial = CreateMaterial(new Color(0.82f, 0.72f, 0.62f), "Controller Hand Skin");
            m_armRig.Initialize(m_cameraRig, sleeveMaterial, skinMaterial);
        }

        // Instantiates the assigned Mixamo arms FBX once and drives it via MixamoArmRig. Built like
        // the detector/pedestal: once, outside the scenario root, so A/Start regen never destroys it.
        /// <summary>Instantiates and configures the custom Mixamo arms once, applying a skin material and IK.</summary>
        private void EnsureCustomArms()
        {
            if (!UseCustomArms || m_customArmsInstance != null || m_cameraRig == null)
            {
                return;
            }

            m_customArmsInstance = Instantiate(m_customArmsPrefab, transform);
            m_customArmsInstance.name = "Custom Arms";

            // The Mixamo FBX imports without its textures, so give every skinned mesh a skin material
            // (a solid Standard material never renders magenta on the Built-in pipeline), and stop it
            // from being frustum-culled once IK moves the bones outside the baked bounds.
            var skin = CreateMaterial(new Color(0.80f, 0.66f, 0.55f), "Custom Arm Skin", 0f, 0.25f, null, null);
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
            m_customArmRig.ChestOffsetFromHead = m_customArmsChestOffset;
            m_customArmRig.BodyYawOffsetDegrees = m_customArmsBodyYawOffset;
            m_customArmRig.TargetArmReach = m_customArmsTargetReach;
            m_customArmRig.FingerCurlAngle = m_customArmsFingerCurlAngle;
            m_customArmRig.FingerCurlAxis = m_customArmsFingerCurlAxis;
            m_customArmRig.FingerCurlSign = m_customArmsFingerCurlSign;
            m_customArmRig.Initialize(m_cameraRig, m_customArmsInstance);
        }

        /// <summary>On detector grab/release, re-syncs the custom arm rig's wrist twist so the hand doesn't rotate.</summary>
        private void OnDetectorGrabChanged()
        {
            if (m_customArmRig != null && m_customArmRig.IsReady)
            {
                m_customArmRig.RecalibrateHands();
            }
        }

        /// <summary>
        /// Builds the detector pedestal and the grabbable identiFINDER once: the real prefab if
        /// assigned else the procedural wand, wiring up GrabbableTool + GeigerAudio + RadiationDetector
        /// (and the screen binder). Survives round regeneration.
        /// </summary>
        private void EnsureDetector()
        {
            if (m_detectorRoot != null)
            {
                return;
            }

            var pedestalPosition = SpawnRoomCenter + m_pedestalOffsetFromSpawnCenter;
            var pedestalParent = m_roomRoot != null ? m_roomRoot : transform;
            RoomDecorator.BuildDetectorPedestal(pedestalParent, pedestalPosition, DecorMaterialFactory);

            m_pedestalFootprint = new ObstacleRect
            {
                Center = new Vector2(pedestalPosition.x, pedestalPosition.z),
                HalfExtents = new Vector2(0.31f, 0.31f),
                YawDegrees = 0f
            };
            m_hasPedestal = true;
            RestorePersistentObstacles();

            m_detectorHomeRotation = Quaternion.identity;

            // Build the model: the colleague's identiFINDER prefab if assigned, else the procedural
            // wand. Both converge on the same GrabbableTool + GeigerAudio + RadiationDetector wiring.
            TextMesh legacyScreen;
            GameObject screenSource;
            float colliderBottomOffset;
            if (m_detectorPrefab != null)
            {
                BuildCustomDetectorModel(out m_detectorRoot, out m_detectorSensorTip, out colliderBottomOffset, out screenSource);
                legacyScreen = null;
            }
            else
            {
                var parts = DetectorModelBuilder.Build();
                m_detectorRoot = parts.Root;
                m_detectorSensorTip = parts.SensorTip;
                legacyScreen = parts.ScreenText;
                screenSource = null;
                colliderBottomOffset = 0.117f; // procedural collider bottom sits ~0.115 below the root
            }

            m_detectorHomePosition = pedestalPosition + new Vector3(0f, RoomDecorator.PedestalTopHeight + colliderBottomOffset, 0f);
            m_detectorRoot.transform.SetPositionAndRotation(m_detectorHomePosition, m_detectorHomeRotation);

            // Rest on the pedestal without tipping/rolling off (it spawns upright, which is unstable):
            // kinematic at spawn. GrabbableTool makes it dynamic again on release after the first grab.
            var detectorBody = m_detectorRoot.GetComponent<Rigidbody>();
            if (detectorBody != null)
            {
                detectorBody.isKinematic = true;
            }

            m_detectorGrabTool = m_detectorRoot.AddComponent<GrabbableTool>();
            m_detectorGrabTool.Initialize(m_cameraRig, m_detectorGrabRadius);
            m_detectorGrabTool.HeldLocalPosition = m_detectorHeldLocalPosition;
            m_detectorGrabTool.HeldLocalEuler = m_detectorHeldLocalEuler;
            m_detectorGrabTool.FlipHeldAboutAim = true; // screen faces the player when held
            // Re-calibrate the arm rig's wrist twist when the detector is grabbed/released so the hand
            // doesn't twist (the offset is otherwise latched once in the relaxed startup pose).
            m_detectorGrabTool.Grabbed += OnDetectorGrabChanged;
            m_detectorGrabTool.Released += OnDetectorGrabChanged;

            var geigerAudio = m_detectorRoot.AddComponent<GeigerAudio>();
            var detector = m_detectorRoot.AddComponent<RadiationDetector>();
            detector.Initialize(m_cameraRig, legacyScreen, m_detectorSensorTip, geigerAudio, m_detectorGrabTool,
                m_detectorHomePosition, m_detectorHomeRotation);

            // Drive the colleague's TMP number + slider from the live reading (both move together).
            if (screenSource != null)
            {
                m_detectorRoot.AddComponent<DetectorScreenBinder>().Bind(detector, screenSource);
            }
        }

        // Builds a grabbable detector from the colleague's identiFINDER prefab (combined model +
        // world-space screen). The prefab's pivot is offset ~300 units from the mesh, so we adopt it
        // under a clean root, auto-scale it to m_detectorTargetHeight (import scale is unknown), and
        // re-centre the mesh on the root. Root local +Y is the wand aim axis (sensor tip at the top).
        /// <summary>Adopts the identiFINDER prefab under a clean, auto-scaled, re-centred root with a collider, body and sensor tip.</summary>
        private void BuildCustomDetectorModel(out GameObject root, out Transform sensorTip, out float colliderBottomOffset, out GameObject screenSource)
        {
            root = new GameObject("identiFINDER");
            var instance = Instantiate(m_detectorPrefab);
            instance.name = "identiFINDER Model";
            instance.transform.SetParent(root.transform, false);
            screenSource = instance;

            // A world-space readout needs no UI input; drop the prefab's EventSystem (avoids dupes).
            foreach (var eventSystem in instance.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true))
            {
                Destroy(eventSystem.gameObject);
            }

            // Auto-scale to a sane size, then translate so the mesh centre sits at the root origin.
            if (TryGetMeshBounds(instance, out var bounds) && bounds.size.y > 1e-4f)
            {
                instance.transform.localScale *= Mathf.Clamp(m_detectorTargetHeight / bounds.size.y, 1e-4f, 1000f);
                if (TryGetMeshBounds(instance, out bounds))
                {
                    instance.transform.position += root.transform.position - bounds.center;
                    TryGetMeshBounds(instance, out bounds);
                }
            }
            else
            {
                bounds = new Bounds(root.transform.position, new Vector3(0.08f, m_detectorTargetHeight, 0.06f));
            }

            var halfHeight = Mathf.Max(0.02f, bounds.size.y * 0.5f);

            var box = root.AddComponent<BoxCollider>();
            box.center = root.transform.InverseTransformPoint(bounds.center);
            box.size = bounds.size;

            var rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 0.4f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.isKinematic = false;

            var tip = new GameObject("Sensor Tip");
            tip.transform.SetParent(root.transform, false);
            tip.transform.localPosition = new Vector3(0f, halfHeight, 0f);
            sensorTip = tip.transform;

            colliderBottomOffset = halfHeight + 0.01f;
        }

        // Bounds of the 3D mesh ONLY — skips the world-space screen Canvas + sprite overlay, whose
        // authored ~300-unit offset would otherwise blow up the bounds and shrink/misplace the model.
        /// <summary>Computes world bounds of the mesh renderers only, excluding sprites/Canvas overlays.</summary>
        private static bool TryGetMeshBounds(GameObject root, out Bounds bounds)
        {
            bounds = default;
            var hasBounds = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer is SpriteRenderer || renderer.GetComponentInParent<Canvas>() != null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }

        /// <summary>Re-adds the persistent (non-scenario) obstacles like the pedestal after a clear.</summary>
        private void RestorePersistentObstacles()
        {
            if (m_hasPedestal)
            {
                m_navigationObstacles.Add(m_pedestalFootprint);
            }
        }

        // Builds the physical START button on the waiting-room side of the shared wall, beside the
        // doorway. Poking it (proximity to either controller) calls StartSimulation(). Built once
        // in Start(); it survives ClearScenario/GenerateRun like the detector and pedestal.
        /// <summary>Builds the wall-mounted poke START button (housing, emissive cap, label) once.</summary>
        private void EnsureStartButton()
        {
            if (m_startButtonRoot != null)
            {
                return;
            }

            var sharedWallZ = -RoomSizeMeters.y * 0.5f - m_wallThickness * 0.5f;
            var wallSurfaceZ = sharedWallZ - m_wallThickness * 0.5f; // spawn-room face of the shared wall
            var spawnHalfWidth = SpawnRoomSizeMeters.x * 0.5f - 0.3f;
            var buttonX = Mathf.Min(m_doorwayWidth * 0.5f + 0.55f, Mathf.Max(0.6f, spawnHalfWidth));
            const float buttonY = 1.3f;

            var root = new GameObject("Start Button");
            var parent = m_roomRoot != null ? m_roomRoot : transform;
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(buttonX, buttonY, wallSurfaceZ);
            root.transform.localRotation = Quaternion.identity;
            m_startButtonRoot = root;

            // Dark housing plate flush against the wall (+z is into the wall, away from the player).
            var housingMaterial = CreateMaterial(new Color(0.12f, 0.13f, 0.15f), "Start Button Housing", 0.35f, 0.4f, null, null);
            var housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = "Start Button Housing";
            housing.transform.SetParent(root.transform, false);
            housing.transform.localPosition = new Vector3(0f, 0f, 0.018f);
            housing.transform.localScale = new Vector3(0.42f, 0.42f, 0.04f);
            AssignMaterial(housing, housingMaterial);
            DisableCollider(housing);

            // Bright emissive cap the player pokes. It is recolored per phase, so it owns a unique
            // material instance (CreateMaterial caches/shares by params and must not be recolored).
            var emissiveBase = Resources.Load<Material>("SimJamEmissiveScreen");
            m_startButtonMaterial = emissiveBase != null ? new Material(emissiveBase) : new Material(Shader.Find("Standard"));
            if (emissiveBase == null)
            {
                m_startButtonMaterial.EnableKeyword("_EMISSION");
            }
            m_startButtonMaterial.name = "Start Button Cap";
            m_runtimeMaterials.Add(m_startButtonMaterial);

            // Round arcade-style cap (a cylinder turned to face the player) the player mashes.
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cap.name = "Start Button Cap";
            cap.transform.SetParent(root.transform, false);
            m_startButtonCapRestPosition = new Vector3(0f, 0f, -0.05f); // protrudes toward the player (-z)
            cap.transform.localPosition = m_startButtonCapRestPosition;
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // round face toward player/wall
            cap.transform.localScale = new Vector3(0.3f, 0.04f, 0.3f);   // dia 0.3 m, depth 0.08 m
            cap.GetComponent<Renderer>().sharedMaterial = m_startButtonMaterial;
            DisableCollider(cap);
            m_startButtonCap = cap.transform;

            // "START" label on the player-facing (-z) side of the cap. Identity rotation matches the
            // head-locked hint convention: a viewer looking along +z reads the text correctly.
            var labelObject = new GameObject("Start Button Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0f, -0.1f);
            labelObject.transform.localRotation = Quaternion.identity;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_startButtonLabel = labelObject.AddComponent<TextMesh>();
            m_startButtonLabel.font = font;
            labelObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            m_startButtonLabel.anchor = TextAnchor.MiddleCenter;
            m_startButtonLabel.alignment = TextAlignment.Center;
            m_startButtonLabel.characterSize = 0.018f;
            m_startButtonLabel.fontSize = 96;
            m_startButtonLabel.color = Color.white;
            m_startButtonLabel.text = "START";

            RefreshStartButtonVisual();
        }

        /// <summary>Per-frame: pushes the button cap in with the hand and fires StartSimulation on a mash.</summary>
        private void UpdateStartButton()
        {
            if (m_startButtonRoot == null || m_startButtonCap == null)
            {
                return;
            }

            // Distance from the nearest hand to the cap. Mash it in with your hand — no aim/click.
            var dist = NearestHandDistanceToStart(out var nearController);
            const float maxTravel = 0.024f;
            const float fireTravel = 0.012f; // less push needed to fire (more sensitive)
            var penetration = Mathf.Max(0f, Mathf.Max(0.04f, m_startButtonPressRadius) - dist);
            var travel = Mathf.Clamp(penetration, 0f, maxTravel);

            // Push the cap inward (+z toward the wall) proportional to how far the hand has pushed in.
            var target = m_startButtonCapRestPosition + new Vector3(0f, 0f, travel);
            m_startButtonCap.localPosition = Vector3.MoveTowards(m_startButtonCap.localPosition, target, 0.6f * Time.deltaTime);

            // Re-arm once the hand pulls back out of the button.
            if (penetration <= 0f)
            {
                m_startButtonArmed = true;
                m_startButtonTouching = false;
                return;
            }

            // Light haptic tick on first contact so the touch is felt (and a dead button is obvious).
            if (!m_startButtonTouching)
            {
                m_startButtonTouching = true;
                PulseHaptic(nearController, 0.3f, 0.3f, 0.03f);
            }

            // Fire once when mashed in past the threshold (not on first contact), never mid-round.
            if (m_startButtonArmed && travel >= fireTravel && m_phase != SimulationPhase.Playing)
            {
                m_startButtonArmed = false;
                PulseHaptic(nearController, 0.6f, 0.5f, 0.1f);
                StartSimulation();
            }
        }

        // Nearest distance from either hand (the Mixamo fingertip when the rig is up, else the
        // controller anchor) to the START cap. Outputs which controller is nearest for haptics.
        /// <summary>Nearest distance from either hand to the START cap's front face; outputs the nearest controller.</summary>
        private float NearestHandDistanceToStart(out OVRInput.Controller nearController)
        {
            nearController = OVRInput.Controller.RTouch;
            if (m_cameraRig == null || m_startButtonCap == null)
            {
                return float.MaxValue;
            }

            // Measure to the cap's player-facing front face, not its centre (which is buried in the housing).
            var capPos = m_startButtonCap.position - m_startButtonRoot.transform.forward * 0.04f;
            var best = float.MaxValue;
            var rightPoint = StartHandPoint(false);
            if (rightPoint.HasValue)
            {
                best = Vector3.Distance(rightPoint.Value, capPos);
                nearController = OVRInput.Controller.RTouch;
            }

            var leftPoint = StartHandPoint(true);
            if (leftPoint.HasValue)
            {
                var d = Vector3.Distance(leftPoint.Value, capPos);
                if (d < best)
                {
                    best = d;
                    nearController = OVRInput.Controller.LTouch;
                }
            }

            return best;
        }

        /// <summary>The world fingertip point for a hand (controller anchor pushed forward), or null.</summary>
        private Vector3? StartHandPoint(bool left)
        {
            // Use the controller anchor (where the physical hand is) pushed forward to the fingertip,
            // NOT the IK wrist bone (which sits ~15 cm behind the fingertip and never reaches the cap).
            var anchor = left ? m_cameraRig.leftControllerAnchor : m_cameraRig.rightControllerAnchor;
            return anchor != null ? anchor.position + anchor.forward * 0.07f : (Vector3?)null;
        }

        /// <summary>Recolours the START button and swaps its label between START and IN ROUND per phase.</summary>
        private void RefreshStartButtonVisual()
        {
            var playing = m_phase == SimulationPhase.Playing;

            if (m_startButtonMaterial != null)
            {
                if (playing)
                {
                    var amber = new Color(0.85f, 0.5f, 0.1f);
                    m_startButtonMaterial.color = amber;
                    m_startButtonMaterial.SetColor("_EmissionColor", amber * 0.6f);
                }
                else
                {
                    m_startButtonMaterial.color = new Color(0.15f, 0.8f, 0.25f);
                    m_startButtonMaterial.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.35f) * 0.9f);
                }
            }

            if (m_startButtonLabel != null)
            {
                m_startButtonLabel.text = playing ? "IN\nROUND" : "START";
                m_startButtonLabel.color = playing ? new Color(1f, 0.85f, 0.6f) : Color.white;
            }
        }

        /// <summary>Shows or hides both controller hand visuals.</summary>
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

        /// <summary>Builds a simple primitive-based hand (palm, wrist, thumb, fingers) under a controller anchor.</summary>
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

        /// <summary>Creates one collider-free primitive piece of a procedural hand.</summary>
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

        /// <summary>
        /// Builds all static geometry: the lab room shell, the adjacent spawn room, ceilings/lights,
        /// the connecting door, home-room props, radiation occluders, and the decorator dressing.
        /// </summary>
        private void BuildRoomGeometry()
        {
            if (m_roomRoot != null)
            {
                Destroy(m_roomRoot.gameObject);
            }

            var roomObject = new GameObject("Generated Radiation Lab Room");
            roomObject.transform.SetParent(transform, false);
            m_roomRoot = roomObject.transform;

            var floorMaterial = CreateMaterial(new Color(0.78f, 0.79f, 0.78f), "Lab Concrete Floor", 0f, 0.32f, ProceduralTextureLibrary.Concrete512, null);
            floorMaterial.mainTextureScale = new Vector2(4f, 4f);
            var wallMaterial = CreateMaterial(new Color(0.92f, 0.93f, 0.92f), "Painted Lab Wall", 0f, 0.18f, ProceduralTextureLibrary.PaintedWall256, null);
            wallMaterial.mainTextureScale = new Vector2(3f, 1.5f);
            var ceilingMaterial = CreateMaterial(new Color(0.86f, 0.88f, 0.87f), "Ceiling Tile", 0f, 0.1f, ProceduralTextureLibrary.CeilingTile256, null);
            ceilingMaterial.mainTextureScale = new Vector2(6f, 6f);
            var gridMaterial = CreateMaterial(new Color(0.58f, 0.6f, 0.62f), "Ceiling Grid", 0.4f, 0.35f, null, null);
            var lightPanelMaterial = CreateMaterial(new Color(0.85f, 0.98f, 1f), "Fluorescent Panel", 0f, 0.5f, null, new Color(0.75f, 0.95f, 1f) * 1.6f);
            var homeFloorMaterial = CreateMaterial(new Color(0.72f, 0.6f, 0.47f), "Warm Home Floor", 0f, 0.4f, ProceduralTextureLibrary.WoodGrain256, null);
            homeFloorMaterial.mainTextureScale = new Vector2(3f, 3f);
            var homeWallMaterial = CreateMaterial(new Color(0.96f, 0.94f, 0.89f), "Warm Home Wall", 0f, 0.18f, ProceduralTextureLibrary.PaintedWall256, null);
            var brownDoorMaterial = CreateMaterial(new Color(0.42f, 0.26f, 0.12f), "Brown Door Face", 0f, 0.4f, ProceduralTextureLibrary.WoodGrain256, null);
            var whiteDoorMaterial = CreateMaterial(new Color(0.96f, 0.95f, 0.9f), "White Door Face", 0f, 0.45f, null, null);
            var goldMaterial = CreateMaterial(new Color(1f, 0.68f, 0.16f), "Gold Door Knob", 0.85f, 0.75f, null, null);

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

            AttachRoomOccluders();
            RoomDecorator.DecorateOfficeRoom(m_roomRoot, size, m_wallHeight, m_doorwayWidth, m_doorwayHeight, sharedWallZ, DecorMaterialFactory);
            RoomDecorator.DecorateSpawnRoom(m_roomRoot, SpawnRoomCenter, SpawnRoomSizeMeters, m_wallHeight, sharedWallZ, DecorMaterialFactory);
        }

        /// <summary>Attaches strong RadiationOccluder shielding to every solid room collider (walls/floor/ceiling).</summary>
        private void AttachRoomOccluders()
        {
            // Every solid room cube (walls, floor, ceiling) shields radiation strongly.
            foreach (var collider in m_roomRoot.GetComponentsInChildren<Collider>())
            {
                if (collider.enabled && collider.GetComponentInParent<RadiationOccluder>() == null)
                {
                    RadiationOccluder.Attach(collider.gameObject, 0.2f);
                }
            }
        }

        /// <summary>Material-creation delegate passed to RoomDecorator so decor shares this cache.</summary>
        private Material DecorMaterialFactory(Color color, string materialName, float metallic, float smoothness, Texture2D albedo, Color? emission)
        {
            return CreateMaterial(color, materialName, metallic, smoothness, albedo, emission);
        }

        /// <summary>Builds a wall as left/right segments plus a header, leaving the doorway opening.</summary>
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

        /// <summary>Builds the adjacent warm spawn/waiting room shell, ceiling grid and lights.</summary>
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

        /// <summary>Lays a drop-ceiling grid of thin strips across a room footprint.</summary>
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

        /// <summary>Builds the lab's four emissive fluorescent panels each with a vertex point light.</summary>
        private void BuildFluorescentPanels(Vector3 center, Material lightPanelMaterial)
        {
            var frameMaterial = CreateMaterial(new Color(0.4f, 0.42f, 0.44f), "Light Panel Frame", 0.3f, 0.3f, null, null);
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
                CreateRoomCube("Fluorescent Light Frame", panelPosition + Vector3.up * 0.012f, new Vector3(1.02f, 0.014f, 0.34f), frameMaterial);

                var lightObject = new GameObject("Fluorescent Point Light");
                lightObject.transform.SetParent(m_roomRoot, false);
                lightObject.transform.localPosition = panelPosition + Vector3.down * 0.15f;
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.88f, 0.97f, 1f);
                light.intensity = 1.25f;
                light.range = 4.5f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForceVertex;
            }
        }

        /// <summary>Builds the spawn room's two warm ceiling light panels each with a point light.</summary>
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
                light.renderMode = LightRenderMode.ForceVertex;
            }
        }

        /// <summary>Builds the swinging door on the shared wall: hinge, brown/white faces, and two knobs.</summary>
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

            var doorCore = CreateChildCube(m_doorPivot, "Door Brown Core", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, 0f), new Vector3(doorWidth, doorHeight, doorThickness), brownDoorMaterial, true);
            RadiationOccluder.Attach(doorCore, 0.45f);
            CreateChildCube(m_doorPivot, "Door Lab Brown Face", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, doorThickness * 0.5f + 0.003f), new Vector3(doorWidth * 0.96f, doorHeight * 0.96f, 0.006f), brownDoorMaterial, false);
            CreateChildCube(m_doorPivot, "Door Spawn White Face", new Vector3(doorWidth * 0.5f, doorHeight * 0.5f, -doorThickness * 0.5f - 0.003f), new Vector3(doorWidth * 0.96f, doorHeight * 0.96f, 0.006f), whiteDoorMaterial, false);

            m_labDoorKnob = CreateDoorKnob(m_doorPivot, "Lab Side Gold Knob", new Vector3(doorWidth - 0.15f, 0.96f, doorThickness * 0.5f + 0.055f), goldMaterial, true);
            m_spawnDoorKnob = CreateDoorKnob(m_doorPivot, "Spawn Side Gold Knob", new Vector3(doorWidth - 0.15f, 0.96f, -doorThickness * 0.5f - 0.055f), goldMaterial, false);
        }

        /// <summary>Creates one gold door knob (stem + sphere) on the given face of the door.</summary>
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

        /// <summary>Furnishes the spawn room with a couch, tables, rug, lamp and plant (the "home" set dressing).</summary>
        private void BuildHomeRoomProps()
        {
            var center = SpawnRoomCenter;
            var size = SpawnRoomSizeMeters;
            var halfDepth = size.y * 0.5f;
            var warmWood = CreateMaterial(new Color(0.5f, 0.33f, 0.17f), "Warm Wood", 0f, 0.42f, ProceduralTextureLibrary.WoodGrain256, null);
            var couchMaterial = CreateMaterial(new Color(0.43f, 0.49f, 0.55f), "Soft Couch Fabric", 0f, 0.08f, null, null);
            var rugMaterial = CreateMaterial(new Color(0.58f, 0.18f, 0.14f), "Home Area Rug", 0f, 0.05f, null, null);
            var plantPotMaterial = CreateMaterial(new Color(0.38f, 0.22f, 0.12f), "Plant Pot", 0f, 0.3f, null, null);
            var plantLeafMaterial = CreateMaterial(new Color(0.12f, 0.42f, 0.2f), "Plant Leaves", 0f, 0.25f, null, null);
            var lampShadeMaterial = CreateMaterial(new Color(0.96f, 0.86f, 0.62f), "Warm Lamp Shade", 0f, 0.3f, null, null);

            CreateRoomCube("Home Area Rug", center + new Vector3(0f, 0.014f, -0.2f), new Vector3(1.9f, 0.026f, 1.25f), rugMaterial);

            var couchZ = center.z - halfDepth + 0.45f;
            CreateRoomCube("Home Couch Seat", new Vector3(center.x, 0.22f, couchZ), new Vector3(1.55f, 0.36f, 0.5f), couchMaterial);
            CreateRoomCube("Home Couch Back", new Vector3(center.x, 0.58f, couchZ - 0.23f), new Vector3(1.65f, 0.72f, 0.16f), couchMaterial);
            CreateRoomCube("Home Couch Left Arm", new Vector3(center.x - 0.88f, 0.36f, couchZ), new Vector3(0.18f, 0.48f, 0.56f), couchMaterial);
            CreateRoomCube("Home Couch Right Arm", new Vector3(center.x + 0.88f, 0.36f, couchZ), new Vector3(0.18f, 0.48f, 0.56f), couchMaterial);
            RoomDecorator.AddBlobShadow(m_roomRoot, new Vector3(center.x, 0f, couchZ - 0.1f), 1.05f);

            CreateRoomCube("Home Coffee Table", center + new Vector3(0f, 0.22f, 0.15f), new Vector3(1.0f, 0.12f, 0.48f), warmWood);
            CreateRoomCube("Home Coffee Table Base", center + new Vector3(0f, 0.1f, 0.15f), new Vector3(0.16f, 0.2f, 0.16f), warmWood);
            RoomDecorator.AddBlobShadow(m_roomRoot, center + new Vector3(0f, 0f, 0.15f), 0.6f);

            CreateRoomCube("Home Side Table", center + new Vector3(-1.35f, 0.34f, -0.75f), new Vector3(0.46f, 0.12f, 0.46f), warmWood);
            CreateRoomCube("Home Side Table Base", center + new Vector3(-1.35f, 0.17f, -0.75f), new Vector3(0.14f, 0.34f, 0.14f), warmWood);
            RoomDecorator.AddBlobShadow(m_roomRoot, center + new Vector3(-1.35f, 0f, -0.75f), 0.32f);
            CreateLamp(center + new Vector3(-1.35f, 0.4f, -0.75f), lampShadeMaterial);

            CreatePlant(center + new Vector3(1.35f, 0f, -1.15f), plantPotMaterial, plantLeafMaterial);
            RoomDecorator.AddBlobShadow(m_roomRoot, center + new Vector3(1.35f, 0f, -1.15f), 0.28f);
        }

        /// <summary>Builds a warm table lamp (base, stem, shade) with a soft point light.</summary>
        private void CreateLamp(Vector3 basePosition, Material shadeMaterial)
        {
            var stemMaterial = CreateMaterial(new Color(0.82f, 0.62f, 0.3f), "Lamp Stem", 0.7f, 0.6f, null, null);
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
            light.renderMode = LightRenderMode.ForceVertex;
        }

        /// <summary>Builds a potted plant (pot, stem, several leaf spheres) as home-room dressing.</summary>
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

        /// <summary>Creates a collidable cube parented to the room root.</summary>
        private void CreateRoomCube(string cubeName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreateChildCube(m_roomRoot, cubeName, localPosition, localScale, material, true);
        }

        /// <summary>Creates a cube primitive under a parent, optionally with its collider disabled.</summary>
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

        /// <summary>Disables a GameObject's collider if present.</summary>
        private static void DisableCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        /// <summary>Randomly places a set of folding tables and wall shelves for the current attempt.</summary>
        private void BuildRandomizedScenarioProps()
        {
            EnsureScenarioRoot();
            m_tables.Clear();
            m_shelves.Clear();
            m_navigationObstacles.Clear();
            m_barrelObstacles.Clear();
            RestorePersistentObstacles();

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

        /// <summary>Attempts to place one non-overlapping folding table (top + legs) and record its slots.</summary>
        private void TryCreateRandomTable(int tableIndex)
        {
            var tableMaterial = CreateMaterial(new Color(0.94f, 0.94f, 0.91f), "Plastic Folding Table", 0f, 0.45f, null, null);
            var legMaterial = CreateMaterial(new Color(0.55f, 0.56f, 0.58f), "Table Metal Legs", 0.7f, 0.55f, null, null);
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
                RadiationOccluder.Attach(tableRoot, 0.75f);

                CreatePropCube(tableRoot.transform, "Table Top", new Vector3(0f, tableHeight, 0f), new Vector3(tableSize.x, 0.07f, tableSize.y), tableMaterial);
                var legX = tableSize.x * 0.42f;
                var legZ = tableSize.y * 0.38f;
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(-legX, tableHeight * 0.5f, -legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(legX, tableHeight * 0.5f, -legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(-legX, tableHeight * 0.5f, legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                CreatePropCube(tableRoot.transform, "Leg", new Vector3(legX, tableHeight * 0.5f, legZ), new Vector3(0.045f, tableHeight, 0.045f), legMaterial);
                RoomDecorator.AddBlobShadow(tableRoot.transform, position, Mathf.Max(tableSize.x, tableSize.y) * 0.62f);

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

        /// <summary>Attempts to place one non-overlapping multi-tier wall shelf and record its slots/keep-outs.</summary>
        private void TryCreateRandomShelf(int shelfIndex)
        {
            var shelfMaterial = CreateMaterial(new Color(0.9f, 0.91f, 0.9f), "Wall Shelf", 0.1f, 0.35f, null, null);
            var bracketMaterial = CreateMaterial(new Color(0.45f, 0.46f, 0.48f), "Shelf Bracket", 0.6f, 0.4f, null, null);
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f;
            var halfDepth = size.y * 0.5f;
            // The shelf back face is pinned to the root's local z=0 plane (both the procedural
            // board and the asset path), so this inset is the exact air gap to the wall inner
            // face. 5 mm reads as flush/wall-mounted without z-fighting.
            const float wallMountInset = 0.005f;
            var wallOptions = new[] { WallSide.North, WallSide.East, WallSide.West };

            var wall = WallSide.North;
            var length = 0f;
            var depth = 0f;
            var tiers = 0;
            var position = Vector3.zero;
            var rotation = Quaternion.identity;
            var inward = Vector3.forward;
            var footprint = default(ObstacleRect);
            var placed = false;

            // Try several wall positions; reject any that overlaps an already-placed shelf. If no
            // clear spot is found, place NONE (one shelf or none -- never overlapping boards).
            for (var attempt = 0; attempt < 30; attempt++)
            {
                wall = wallOptions[UnityEngine.Random.Range(0, wallOptions.Length)];
                length = UnityEngine.Random.Range(1.05f, 2.05f);
                // Deep enough that a 5 gal barrel (0.28 dia) sits centered on the board with side
                // clearance instead of overhanging the front edge.
                depth = UnityEngine.Random.Range(0.34f, 0.46f);
                tiers = UnityEngine.Random.Range(1, 4);
                var sideOffset = UnityEngine.Random.Range(-1.65f, 1.65f);

                switch (wall)
                {
                    case WallSide.East:
                        position = new Vector3(halfWidth - wallMountInset, 0f, sideOffset);
                        rotation = Quaternion.Euler(0f, 90f, 0f);
                        inward = Vector3.left;
                        break;
                    case WallSide.West:
                        position = new Vector3(-halfWidth + wallMountInset, 0f, sideOffset);
                        rotation = Quaternion.Euler(0f, -90f, 0f);
                        inward = Vector3.right;
                        break;
                    default:
                        position = new Vector3(sideOffset, 0f, halfDepth - wallMountInset);
                        rotation = Quaternion.identity;
                        inward = Vector3.back;
                        break;
                }

                footprint = new ObstacleRect
                {
                    Center = new Vector2(position.x, position.z),
                    HalfExtents = new Vector2(length * 0.5f, depth * 0.5f),
                    YawDegrees = rotation.eulerAngles.y
                };

                if (!ShelfFootprintOverlaps(footprint))
                {
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                return;
            }

            var shelfRoot = new GameObject($"Random Wall Shelf {shelfIndex + 1}");
            shelfRoot.transform.SetParent(m_scenarioRoot, false);
            shelfRoot.transform.SetPositionAndRotation(position, rotation);
            m_spawnedObjects.Add(shelfRoot);
            RadiationOccluder.Attach(shelfRoot, 0.8f);

            var surfaceHeights = new List<float>(tiers);
            var shelfSlotSize = new Vector2(length, depth);
            // Centre the barrel on the board (was 0.64 toward the front, which overhung the edge).
            var shelfSlotCenter = new Vector3(0f, 0f, -depth * 0.5f);

            for (var tier = 0; tier < tiers; tier++)
            {
                // Lowered so the top tier is easier to reach in VR: base 0.85 m (was 1.05 m), 0.52 m
                // tier spacing (was 0.58 m) still leaves ~0.10 m of clear air above a 0.36 m-tall 5 gal
                // barrel before the shelf above it. Top of a 3-tier shelf drops ~2.21 m -> ~1.89 m.
                var surfaceY = 0.85f + tier * 0.52f + UnityEngine.Random.Range(-0.02f, 0.02f);
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
                // Brackets tucked near the wall (back of the board, local z ~ -depth*0.18) like a
                // real wall-mounted shelf, instead of floating toward the room.
                CreatePropCube(shelfRoot.transform, "Shelf Bracket", new Vector3(-length * 0.36f, surfaceY - 0.12f, -depth * 0.18f), new Vector3(0.035f, 0.22f, 0.035f), bracketMaterial);
                CreatePropCube(shelfRoot.transform, "Shelf Bracket", new Vector3(length * 0.36f, surfaceY - 0.12f, -depth * 0.18f), new Vector3(0.035f, 0.22f, 0.035f), bracketMaterial);
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
                SurfaceY = surfaceHeights.Count > 0 ? surfaceHeights[0] : 0.85f,
                Forward = inward,
                Size = shelfSlotSize,
                TierCount = surfaceHeights.Count,
                ShelfSurfaceHeights = surfaceHeights,
                ShelfSlotSize = shelfSlotSize,
                ShelfSlotCenterLocal = shelfSlotCenter,
                Wall = wall,
                Name = shelfRoot.name
            });

            // Register the shelf as a floor-placement keep-out so tall floor barrels never spawn
            // beneath the board/brackets and impale them. The stored PropInfo footprint is
            // centered on the WALL mount; the board actually projects inward by `depth`, so shift
            // the obstacle center inward by depth*0.5. Inflate by the same 0.12 margin tables use
            // (the floor-slot test then adds its own barrel-radius padding).
            var shelfObstacleCenter = new Vector2(position.x, position.z)
                + new Vector2(inward.x, inward.z) * (depth * 0.5f);
            m_navigationObstacles.Add(new ObstacleRect
            {
                Center = shelfObstacleCenter,
                HalfExtents = new Vector2(length * 0.5f + 0.12f, depth * 0.5f + 0.12f),
                YawDegrees = rotation.eulerAngles.y
            });
        }

        /// <summary>True if a candidate shelf footprint overlaps any already-placed shelf.</summary>
        private bool ShelfFootprintOverlaps(ObstacleRect candidate)
        {
            foreach (var shelf in m_shelves)
            {
                if (FootprintsOverlap(candidate, shelf.Footprint, 0.15f))
                {
                    return true;
                }
            }

            return false;
        }

        // Exact world-axis-aligned overlap test. Every footprint in this scene is axis-aligned to a
        // wall (yaw 0/90/270), so this is exact -- unlike the corner-containment RectsOverlap, which
        // misses two perpendicular rects crossing in a "+" (a 0deg table over a 90deg table, or
        // shelves meeting at a corner).
        /// <summary>Exact axis-aligned overlap test between two wall-aligned footprints, plus a gap.</summary>
        private static bool FootprintsOverlap(ObstacleRect a, ObstacleRect b, float gap)
        {
            var halfA = WorldAlignedHalfExtents(a);
            var halfB = WorldAlignedHalfExtents(b);
            var dx = Mathf.Abs(a.Center.x - b.Center.x);
            var dz = Mathf.Abs(a.Center.y - b.Center.y);
            return dx < halfA.x + halfB.x + gap && dz < halfA.y + halfB.y + gap;
        }

        /// <summary>Returns a wall-aligned rect's half extents in world axes (swapping at ~90 degrees).</summary>
        private static Vector2 WorldAlignedHalfExtents(ObstacleRect rect)
        {
            // Axis-aligned at yaw 0 (N/S) or 90/270 (E/W). At ~90 deg the rect's local length/depth
            // map to world Z/X, so swap to get the true world-axis-aligned half extents.
            var yaw = Mathf.Abs(Mathf.DeltaAngle(rect.YawDegrees, 0f));
            var rotated = yaw > 45f && yaw < 135f;
            return rotated ? new Vector2(rect.HalfExtents.y, rect.HalfExtents.x) : rect.HalfExtents;
        }

        /// <summary>
        /// Instantiates the wall-shelf asset for one tier, scaling and positioning it to the target
        /// span/height and adding colliders. Returns false (so a procedural board is built) if no
        /// asset is assigned or its bounds can't be measured.
        /// </summary>
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

        /// <summary>Disables and destroys every collider under a GameObject.</summary>
        private static void RemoveColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Destroy(collider);
            }
        }

        /// <summary>Adds a non-convex MeshCollider to each mesh under a shelf; true if any was added.</summary>
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

        /// <summary>Computes the renderer bounds of a subtree expressed in a target transform's local space.</summary>
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

        /// <summary>Adds a thin box collider at the top of a shelf tier so barrels rest on its surface.</summary>
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

        /// <summary>Creates a collidable cube prop (table/shelf part) under a parent transform.</summary>
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

        /// <summary>Rebuilds and shuffles the full set of candidate barrel slots (floor, tables, shelves).</summary>
        private void BuildSpawnSlots()
        {
            m_spawnSlots.Clear();
            BuildFloorSlots();
            BuildTableSlots();
            BuildShelfSlots();
            Shuffle(m_spawnSlots);
        }

        /// <summary>Generates a grid of floor slots, skipping the aisle, player clearance and obstacles.</summary>
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

        /// <summary>Generates barrel slots across each table top (a central slot plus spread positions).</summary>
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

        /// <summary>Generates a row of 5-gallon-only barrel slots along each usable shelf tier.</summary>
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
                        : 0.85f + tier * 0.52f + m_shelfSurfaceClearance;
                    // Inset the run of slots by a 5 gal barrel's radius (~0.12) + margin so the end
                    // barrels stay fully on the board instead of hanging off the ends.
                    var halfUsable = Mathf.Max(0f, usableSize.x * 0.5f - 0.17f);
                    var count = Mathf.Max(1, Mathf.FloorToInt(halfUsable * 2f / m_shelfBarrelSpacing));
                    for (var i = 0; i < count; i++)
                    {
                        var t = count == 1 ? 0.5f : i / (float)(count - 1);
                        var x = Mathf.Lerp(-halfUsable, halfUsable, t);
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

        /// <summary>
        /// Walks the shuffled slots and spawns barrels up to the requested count, choosing a weighted
        /// random size per slot that satisfies spacing and line-of-sight rules. Returns the count placed.
        /// </summary>
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

                    // All barrels stand upright now. Keep the exact RNG draw (and its && short-
                    // circuit via AllowSideways/Surface) so the UnityEngine.Random stream consumed
                    // before AssignHotSource is byte-identical to before -- the hidden radioactive
                    // source must not change. Only the orientation is forced upright.
                    _ = slot.AllowSideways && slot.Surface != SurfaceKind.Shelf && UnityEngine.Random.value < 0.28f;
                    const bool isSideways = false;
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
                        m_barrelObstacles.Add(new ObstacleRect
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

        /// <summary>Builds the per-size BarrelSpec table (dimensions, labels, colours, selection weights).</summary>
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
                    Diameter = 0.24f,
                    Height = 0.31f,
                    LabelHeight = 0.17f,
                    BodyColor = new Color(0.93f, 0.83f, 0.22f),
                    SelectionWeight = 0.6f
                }
            };
        }

        /// <summary>Returns the slot's allowed sizes ordered by a weighted random draw (best pick first).</summary>
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

        /// <summary>
        /// Instantiates a single barrel (prefab or placeholder) at a slot, fits its scale, applies
        /// cosmetics/sticker, adds a collider and occluder, and returns its BarrelInstance.
        /// </summary>
        private BarrelInstance SpawnBarrel(SpawnSlot slot, BarrelSpec spec, bool isSideways)
        {
            EnsureScenarioRoot();
            var labelForward = slot.LabelForward.sqrMagnitude > 0.001f ? slot.LabelForward.normalized : Vector3.forward;
            // Random spin around the barrel's own axis so the same model face never repeats.
            var spinYaw = UnityEngine.Random.Range(0f, 360f);
            var yawRotation = Quaternion.LookRotation(labelForward, Vector3.up) * Quaternion.Euler(0f, spinYaw, 0f);
            var finalRotation = isSideways ? yawRotation * Quaternion.Euler(0f, 0f, 90f) : yawRotation;
            var verticalOffset = isSideways ? spec.Diameter * 0.5f : spec.Height * 0.5f;
            var position = slot.Position + Vector3.up * verticalOffset;

            GameObject barrelObject;
            if (spec.Prefab != null)
            {
                // Instantiate unrotated, fit the scale against the unrotated bounds (a rotated
                // AABB inflates by up to sqrt(2) and used to distort every barrel differently),
                // and only then apply the final rotation.
                barrelObject = Instantiate(spec.Prefab, position, Quaternion.identity, m_scenarioRoot);
                ApplyFittedScale(barrelObject, spec);
                barrelObject.transform.rotation = finalRotation;
                AlignRendererBottomToSurface(barrelObject, slot.Position.y);
                ApplyBarrelCosmetics(barrelObject, spec);
            }
            else
            {
                barrelObject = CreatePlaceholderBarrel(position, finalRotation, spec, isSideways);
            }

            barrelObject.name = $"{spec.Label} Barrel ({slot.SourceName})";
            m_spawnedObjects.Add(barrelObject);
            EnsureApproximateCollider(barrelObject, spec);
            RadiationOccluder.Attach(barrelObject, 0.3f);

            if (slot.Surface == SurfaceKind.Floor)
            {
                RoomDecorator.AddBlobShadow(m_scenarioRoot, slot.Position, spec.Diameter * 0.62f);
            }

            var barrel = barrelObject.GetComponent<BarrelInstance>();
            if (barrel == null)
            {
                barrel = barrelObject.AddComponent<BarrelInstance>();
            }

            var showDebugLabel = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            showDebugLabel = m_debugLabelsVisible;
#endif
            barrel.Initialize(spec.Label, GetRandomRadiationCount(), showDebugLabel);
            if (m_showBarrelSizeStickers)
            {
                AddBarrelSizeSticker(spec, slot, isSideways, labelForward, finalRotation);
            }

            return barrel;
        }

        /// <summary>Builds and orients the curved "NN GAL" size sticker that wraps around a barrel.</summary>
        private void AddBarrelSizeSticker(BarrelSpec spec, SpawnSlot slot, bool isSideways, Vector3 labelForward, Quaternion barrelRotation)
        {
            var labelMaterial = GetBarrelLabelMaterial(spec);
            if (labelMaterial == null)
            {
                return;
            }

            var dir = labelForward.sqrMagnitude > 0.001f ? labelForward.normalized : Vector3.forward;
            // The barrel model's cylinder axis is its local Y; the label arc wraps around it.
            var axis = (barrelRotation * Vector3.up).normalized;

            Vector3 center;
            Vector3 faceDir;
            if (!isSideways)
            {
                // Upright: wrap around the vertical barrel at label height, facing the room.
                center = new Vector3(slot.Position.x, slot.Position.y + spec.LabelHeight, slot.Position.z);
                faceDir = dir;
            }
            else
            {
                // Lying barrel: wrap around the (now horizontal) barrel, label facing up.
                center = new Vector3(slot.Position.x, slot.Position.y + spec.Diameter * 0.5f, slot.Position.z);
                faceDir = Vector3.up;
            }

            // Orient so local +Y = cylinder axis and local +Z = the room/up-facing direction.
            var forward = Vector3.ProjectOnPlane(faceDir, axis);
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.ProjectOnPlane(dir, axis);
            }
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            var sticker = new GameObject($"{spec.Label} Sticker");
            sticker.transform.SetParent(m_scenarioRoot, false);
            sticker.transform.SetPositionAndRotation(center, Quaternion.LookRotation(forward.normalized, axis));
            sticker.AddComponent<MeshFilter>().sharedMesh = GetBarrelLabelMesh(spec);
            var renderer = sticker.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = labelMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_spawnedObjects.Add(sticker);
        }

        /// <summary>Returns (and caches) the curved decal mesh sized to a barrel's surface.</summary>
        private Mesh GetBarrelLabelMesh(BarrelSpec spec)
        {
            if (m_barrelLabelMeshes.TryGetValue(spec.Size, out var cached) && cached != null)
            {
                return cached;
            }

            // A curved decal that conforms to the barrel surface (proud by 4 mm) and wraps ~80 deg
            // around the front, so the label hugs the drum instead of floating as a flat plane.
            var radius = spec.Diameter * 0.5f + 0.004f;
            var height = spec.Diameter * 0.52f;
            var mesh = BuildCurvedLabelMesh(radius, 80f, height);
            m_barrelLabelMeshes[spec.Size] = mesh;
            return mesh;
        }

        /// <summary>Builds a double-sided curved arc mesh that conforms to a barrel's cylindrical surface.</summary>
        private static Mesh BuildCurvedLabelMesh(float radius, float arcDegrees, float height)
        {
            const int segments = 20;
            var mesh = new Mesh { name = "Barrel Label Arc" };
            var ringCount = segments + 1;
            var vertices = new Vector3[ringCount * 2];
            var normals = new Vector3[ringCount * 2];
            var uvs = new Vector2[ringCount * 2];
            var halfHeight = height * 0.5f;
            var startAngle = -arcDegrees * 0.5f * Mathf.Deg2Rad;
            var stepAngle = arcDegrees * Mathf.Deg2Rad / segments;

            for (var i = 0; i < ringCount; i++)
            {
                var angle = startAngle + stepAngle * i;
                var outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                var radial = outward * radius;
                var baseIndex = i * 2;
                vertices[baseIndex] = new Vector3(radial.x, -halfHeight, radial.z);
                vertices[baseIndex + 1] = new Vector3(radial.x, halfHeight, radial.z);
                normals[baseIndex] = outward;
                normals[baseIndex + 1] = outward;
                // Flip U so the label reads left-to-right for a viewer in the room (the arc's +X
                // edge is on the viewer's left, so unflipped UVs would mirror the text).
                var u = 1f - i / (float)segments;
                uvs[baseIndex] = new Vector2(u, 0f);
                uvs[baseIndex + 1] = new Vector2(u, 1f);
            }

            // Double-sided so the label is visible regardless of which way the arc faces.
            var triangles = new int[segments * 12];
            for (var i = 0; i < segments; i++)
            {
                var b = i * 2;
                var t = i * 12;
                triangles[t] = b;
                triangles[t + 1] = b + 1;
                triangles[t + 2] = b + 3;
                triangles[t + 3] = b;
                triangles[t + 4] = b + 3;
                triangles[t + 5] = b + 2;
                triangles[t + 6] = b;
                triangles[t + 7] = b + 3;
                triangles[t + 8] = b + 1;
                triangles[t + 9] = b;
                triangles[t + 10] = b + 2;
                triangles[t + 11] = b + 3;
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Returns the sticker material carrying the correct "NN GAL" label texture for a size.</summary>
        private Material GetBarrelLabelMaterial(BarrelSpec spec)
        {
            var labelTexture = spec.Size switch
            {
                BarrelSize.Gallon55 => ProceduralTextureLibrary.BarrelLabel55,
                BarrelSize.Gallon30 => ProceduralTextureLibrary.BarrelLabel30,
                _ => ProceduralTextureLibrary.BarrelLabel5
            };

            return CreateMaterial(Color.white, $"{spec.Label} Sticker Material", 0f, 0.12f, labelTexture, null);
        }

        /// <summary>Applies (and caches) the scale that fits a barrel prefab to its physical dimensions.</summary>
        private void ApplyFittedScale(GameObject barrelObject, BarrelSpec spec)
        {
            if (m_fittedScaleCache.TryGetValue(spec.Size, out var cachedScale))
            {
                barrelObject.transform.localScale = cachedScale;
                return;
            }

            barrelObject.transform.localScale = spec.ModelScale;
            FitPrefabToPhysicalSize(barrelObject, spec);
            m_fittedScaleCache[spec.Size] = barrelObject.transform.localScale;
        }

        /// <summary>Assigns a random tint/PBR variant material to every renderer of a barrel.</summary>
        private void ApplyBarrelCosmetics(GameObject barrelObject, BarrelSpec spec)
        {
            var variants = GetBarrelTintVariants(barrelObject, spec);
            if (variants == null || variants.Length == 0)
            {
                return;
            }

            var material = variants[UnityEngine.Random.Range(0, variants.Length)];
            foreach (var renderer in barrelObject.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
            }
        }

        // Returns the colleague's Metal/Paint material pair for a barrel size (55 + 30 share a group,
        // 5-gal has its own), or null if none are assigned (then the procedural tints are used).
        /// <summary>Returns the authored Metal/Paint material variants for a size, or null to use tints.</summary>
        private Material[] GetBarrelMaterialOverride(BarrelSize size)
        {
            var metal = size == BarrelSize.Gallon5 ? m_barrelSmallMetal : m_barrelLargeMetal;
            var paint = size == BarrelSize.Gallon5 ? m_barrelSmallPaint : m_barrelLargePaint;
            if (metal != null && paint != null)
            {
                return new[] { metal, paint };
            }

            if (metal != null)
            {
                return new[] { metal };
            }

            return paint != null ? new[] { paint } : null;
        }

        /// <summary>
        /// Returns (and caches) the per-size cosmetic material variants: the authored override pair
        /// if assigned, else four procedurally tinted clones of the prefab's base material.
        /// </summary>
        private Material[] GetBarrelTintVariants(GameObject barrelObject, BarrelSpec spec)
        {
            if (m_barrelTintVariants.TryGetValue(spec.Size, out var cachedVariants))
            {
                return cachedVariants;
            }

            // Colleague's authored PBR materials override the procedural tints when assigned.
            var overrideVariants = GetBarrelMaterialOverride(spec.Size);
            if (overrideVariants != null && overrideVariants.Length > 0)
            {
                m_barrelTintVariants[spec.Size] = overrideVariants;
                return overrideVariants;
            }

            var sourceRenderer = barrelObject.GetComponentInChildren<Renderer>();
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null)
            {
                m_barrelTintVariants[spec.Size] = Array.Empty<Material>();
                return null;
            }

            var baseMaterial = sourceRenderer.sharedMaterial;
            var baseColor = baseMaterial.HasProperty("_Color") ? baseMaterial.color : spec.BodyColor;
            var variants = new Material[4];
            var tints = new[] { 1f, 0.86f, 1.08f, 0.94f };
            var smoothnessValues = new[] { 0.42f, 0.34f, 0.46f, 0.3f };

            for (var i = 0; i < variants.Length; i++)
            {
                var variant = new Material(baseMaterial)
                {
                    name = $"{spec.Label} Tint {i + 1}"
                };

                var tinted = baseColor * tints[i];
                if (i == 3)
                {
                    // Weathered variant: desaturated and a little darker.
                    var luma = tinted.r * 0.3f + tinted.g * 0.59f + tinted.b * 0.11f;
                    tinted = Color.Lerp(tinted, new Color(luma, luma, luma, tinted.a), 0.45f) * 0.92f;
                }

                tinted.a = 1f;
                if (variant.HasProperty("_Color"))
                {
                    variant.color = tinted;
                }

                if (variant.HasProperty("_Metallic"))
                {
                    variant.SetFloat("_Metallic", 0.55f);
                }

                if (variant.HasProperty("_Glossiness"))
                {
                    variant.SetFloat("_Glossiness", smoothnessValues[i]);
                }

                m_runtimeMaterials.Add(variant);
                variants[i] = variant;
            }

            m_barrelTintVariants[spec.Size] = variants;
            return variants;
        }

        /// <summary>Builds a simple cylinder stand-in barrel when no prefab is assigned for a size.</summary>
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

        /// <summary>Scales a barrel prefab so its renderer bounds match the spec's diameter and height.</summary>
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

        /// <summary>Shifts a barrel vertically so its rendered bottom rests exactly on the surface Y.</summary>
        private static void AlignRendererBottomToSurface(GameObject barrelObject, float surfaceY)
        {
            if (!TryGetRendererBounds(barrelObject, out var bounds))
            {
                return;
            }

            barrelObject.transform.position += Vector3.up * (surfaceY - bounds.min.y);
        }

        /// <summary>Adds a box collider sized to the barrel spec if the prefab has none.</summary>
        private static void EnsureApproximateCollider(GameObject barrelObject, BarrelSpec spec)
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

            // BoxCollider.size is local space: the barrel axis is always local Y no matter how
            // the root is rotated (sideways barrels previously got a wrongly permuted box).
            collider.size = new Vector3(
                spec.Diameter / lossyScale.x,
                spec.Height / lossyScale.y,
                spec.Diameter / lossyScale.z);

            if (TryGetRendererBounds(barrelObject, out var bounds))
            {
                collider.center = barrelObject.transform.InverseTransformPoint(bounds.center);
            }
        }

        /// <summary>Computes the combined world renderer bounds of a subtree; false if it has no renderers.</summary>
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

        /// <summary>True if a candidate slot keeps the required spacing from all placed barrels at a similar height.</summary>
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

        /// <summary>True if a floor slot isn't hidden behind an existing floor barrel from the room centre's view.</summary>
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

        /// <summary>Returns the minimum barrel spacing for a surface kind.</summary>
        private float GetSpacing(SurfaceKind surface)
        {
            return surface switch
            {
                SurfaceKind.Table => m_tableBarrelSpacing,
                SurfaceKind.Shelf => m_shelfBarrelSpacing,
                _ => m_floorBarrelSpacing
            };
        }

        /// <summary>Per-frame locomotion: smooth thumbstick move, snap turn, and teleport aim/commit.</summary>
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
                if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    if (m_hasValidTeleportTarget)
                    {
                        CommitTeleport(m_currentTeleportTarget);
                        HideMessage();
                    }
                    else
                    {
                        // Pressed teleport while aiming at a blocked (red) spot.
                        ShowMessage("Can't move there\nAim at a GREEN ring", new Color(1f, 0.86f, 0.2f), 2.5f);
                    }
                }
            }

            UpdateMessagePanel();
        }

        /// <summary>Raycasts to the floor, snaps to the nearest standable spot, and updates the marker visual.</summary>
        private void UpdateTeleportTarget()
        {
            EnsureTeleportMarker();

            var ray = GetTeleportRay();
            var floorPlane = new Plane(Vector3.up, Vector3.zero);
            var wasValid = m_hasValidTeleportTarget;
            m_hasValidTeleportTarget = false;
            if (floorPlane.Raycast(ray, out var hitDistance))
            {
                var candidate = ray.GetPoint(hitDistance);
                candidate.y = 0f;
                // Reliability: instead of pass/fail on the exact ray hit, snap to the nearest
                // standable spot (so near-wall / near-edge aims land beside the obstacle rather
                // than going red). Door-crossing is checked on the SNAPPED point.
                if (hitDistance > 0.25f && hitDistance < m_maxTeleportDistance
                    && TryFindStandablePosition(candidate, out var snapped)
                    && !TeleportCrossesClosedDoor(ray.origin, snapped)
                    && !TeleportCrossesSolidWall(ray.origin, snapped))
                {
                    m_currentTeleportTarget = snapped;
                    m_hasValidTeleportTarget = true;
                }
            }

            m_teleportMarker.SetActive(m_enableTeleport);
            if (m_hasValidTeleportTarget)
            {
                // Smooth the marker visual (frame-rate independent) to kill controller-ray jitter;
                // commit still uses the exact snapped target. Re-acquire snaps instantly.
                if (!wasValid)
                {
                    m_teleportMarkerSmoothedPosition = m_currentTeleportTarget;
                }
                else
                {
                    var t = 1f - Mathf.Exp(-m_teleportMarkerSmoothing * Time.deltaTime);
                    m_teleportMarkerSmoothedPosition = Vector3.Lerp(m_teleportMarkerSmoothedPosition, m_currentTeleportTarget, t);
                }

                m_teleportMarker.transform.position = m_teleportMarkerSmoothedPosition + Vector3.up * 0.012f;
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

        // Returns the nearest standable position to an aimed floor point, searching outward in
        // rings. This is what makes teleport reliable: a hit a few cm into a wall, barrel keep-out,
        // or just off a valid spot resolves to the closest place the player can actually stand,
        // rather than being rejected. All keep-outs (walls, furniture, barrels, closed door) are
        // still enforced at every sample via IsStandable.
        /// <summary>Finds the nearest standable spot to an aim point by searching outward in rings/spokes.</summary>
        private bool TryFindStandablePosition(Vector3 aim, out Vector3 result)
        {
            aim.y = 0f;
            if (IsStandable(aim))
            {
                result = aim;
                return true;
            }

            const int rings = 6;
            const int spokes = 12;
            var step = Mathf.Max(0.04f, m_teleportSnapStep);
            for (var r = 1; r <= rings; r++)
            {
                var radius = r * step;
                for (var s = 0; s < spokes; s++)
                {
                    var angle = Mathf.PI * 2f * s / spokes;
                    var sample = aim + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (IsStandable(sample))
                    {
                        result = sample;
                        return true;
                    }
                }
            }

            result = aim;
            return false;
        }

        // Geometric standability test (no ClampToRoom self-reject): inside a room, clear of the
        // shared wall, the closed door, furniture footprints, and barrel keep-outs.
        /// <summary>True if a point is inside a walkable room and clear of the door, furniture and barrels.</summary>
        private bool IsStandable(Vector3 position)
        {
            if (!IsInsideWalkableFloorPlan(position, m_edgeMargin))
            {
                return false;
            }

            if (!IsDoorOpenForNavigation && IsInsideClosedDoorObstacle(position, m_edgeMargin))
            {
                return false;
            }

            foreach (var obstacle in m_navigationObstacles)
            {
                if (IsPointInsideRect(position, obstacle, m_edgeMargin))
                {
                    return false;
                }
            }

            foreach (var barrel in m_barrelObstacles)
            {
                if (IsPointInsideRect(position, barrel, m_barrelClearance))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The teleport aim ray from the right controller (or camera in the fallback path).</summary>
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

        /// <summary>Lazily creates the teleport target ring and its valid/invalid materials.</summary>
        private void EnsureTeleportMarker()
        {
            if (m_teleportMarker != null)
            {
                return;
            }

            m_validTeleportMaterial = CreateMaterial(new Color(0.1f, 0.9f, 0.35f, 0.75f), "Valid Teleport", 0f, 0.5f, null, new Color(0.1f, 0.9f, 0.35f) * 2.2f);
            m_invalidTeleportMaterial = CreateMaterial(new Color(0.9f, 0.1f, 0.1f, 0.75f), "Invalid Teleport", 0f, 0.5f, null, new Color(0.9f, 0.1f, 0.1f) * 2.2f);
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

        /// <summary>Moves the rig to a position, clamped inside the rooms (free glide, no per-barrel snag).</summary>
        private void MoveRigTo(Vector3 worldPosition)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            var clamped = ClampToRoom(worldPosition);
            clamped.y = m_locomotionRoot == m_cameraTransform && m_cameraRig == null ? m_defaultEyeHeight : 0f;
            // Free glide like the tutorial: keep the room clamp but drop the per-barrel walkable gate
            // that made the player snag/stick against every keep-out.
            m_locomotionRoot.position = clamped;
        }

        /// <summary>Teleports the rig to a validated target and spawns the arrival burst effect.</summary>
        private void CommitTeleport(Vector3 worldPosition)
        {
            if (m_locomotionRoot == null)
            {
                return;
            }

            // The target already passed ClampToRoom + IsWalkablePosition this frame in
            // UpdateTeleportTarget. Re-clamp defensively (keeps the rig inside the room) but commit
            // even if a sub-0.02 m clamp nudge would flip the strict re-check -- a validated
            // teleport must never silently fail.
            var clamped = ClampToRoom(worldPosition);
            clamped.y = m_locomotionRoot == m_cameraTransform && m_cameraRig == null ? m_defaultEyeHeight : 0f;
            m_locomotionRoot.position = clamped;
            TeleportEffects.SpawnArrivalBurst(new Vector3(clamped.x, worldPosition.y, clamped.z), new Color(0.2f, 1f, 0.4f));
        }

        // Shared head-locked message panel used for teleport hints AND game-loop results (red
        // "incorrect" / green "found it"). The dark backing auto-sizes to the text every time a
        // message is shown, so copy never overflows its box. A designer can ignore this entirely
        // and drive their own UI off OnSimulationStarted / OnGuessResolved instead.
        /// <summary>Lazily builds the head-locked message panel (backing quad + text) parented to the camera.</summary>
        private void EnsureMessagePanel()
        {
            if (m_messagePanel != null || m_cameraTransform == null)
            {
                return;
            }

            var panelRoot = new GameObject("Message Panel");
            panelRoot.transform.SetParent(m_cameraTransform, false);
            panelRoot.transform.localPosition = new Vector3(0f, -0.12f, 1.1f);
            panelRoot.transform.localRotation = Quaternion.identity;

            // Dark backing for contrast. Clone the project's transparent blob material so the
            // alpha-blend Standard variant ships in the Quest build and the panel can fade.
            var blobBase = Resources.Load<Material>("SimJamBlobShadow");
            if (blobBase != null)
            {
                m_messageBackingMaterial = new Material(blobBase) { name = "Message Backing" };
                m_messageBackingMaterial.mainTexture = null;
                m_messageBackingMaterial.color = new Color(0.04f, 0.04f, 0.05f, 0.78f);
                m_runtimeMaterials.Add(m_messageBackingMaterial);

                var backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                backing.name = "Message Backing";
                backing.transform.SetParent(panelRoot.transform, false);
                backing.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                backing.transform.localScale = new Vector3(0.6f, 0.26f, 0.004f);
                DisableCollider(backing);
                backing.GetComponent<Renderer>().sharedMaterial = m_messageBackingMaterial;
                m_messageBacking = backing.transform;
            }

            var textObject = new GameObject("Message Text");
            textObject.transform.SetParent(panelRoot.transform, false);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            m_messageText = textObject.AddComponent<TextMesh>();
            m_messageText.font = font;
            textObject.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            m_messageText.anchor = TextAnchor.MiddleCenter;
            m_messageText.alignment = TextAlignment.Center;
            // Smaller than before so copy fits comfortably; the backing then sizes itself to match.
            m_messageText.characterSize = 0.012f;
            m_messageText.fontSize = 90;
            m_messageText.color = m_messageColor;

            panelRoot.SetActive(false);
            m_messagePanel = panelRoot;
        }

        // Public-friendly entry point so a ported-in UI could also reuse the placeholder panel.
        /// <summary>Displays a message on the head-locked panel with a colour and hold duration.</summary>
        private void ShowMessage(string text, Color color, float holdDuration)
        {
            EnsureMessagePanel();
            if (m_messagePanel == null || m_messageText == null)
            {
                return;
            }

            m_messageColor = color;
            m_messageText.text = text;
            m_messageHoldDuration = Mathf.Max(0.1f, holdDuration);
            m_messageStartTime = Time.time;
            m_messageResizePending = true;
            SetMessageAlpha(0f);
            m_messagePanel.SetActive(true);
        }

        /// <summary>Immediately hides the message panel.</summary>
        private void HideMessage()
        {
            if (m_messagePanel != null && m_messagePanel.activeSelf)
            {
                m_messagePanel.SetActive(false);
            }
        }

        /// <summary>Per-frame: auto-fits the backing box to the text and drives the fade in/hold/out.</summary>
        private void UpdateMessagePanel()
        {
            if (m_messagePanel == null || !m_messagePanel.activeSelf)
            {
                return;
            }

            // Auto-fit the backing box to the text once the TextMesh has generated its mesh (its
            // bounds are zero for a frame). The box is sized in the panel's own frame (world AABB /
            // lossyScale), so it always contains the text — copy can never overflow the box.
            if (m_messageResizePending && m_messageBacking != null)
            {
                var renderer = m_messageText.GetComponent<Renderer>();
                if (renderer != null && renderer.bounds.size.x > 1e-4f)
                {
                    var worldSize = renderer.bounds.size;
                    var lossyScale = m_messagePanel.transform.lossyScale;
                    var localWidth = worldSize.x / Mathf.Max(1e-4f, Mathf.Abs(lossyScale.x));
                    var localHeight = worldSize.y / Mathf.Max(1e-4f, Mathf.Abs(lossyScale.y));
                    localWidth = Mathf.Clamp(localWidth + 0.14f, 0.24f, 1.6f);
                    localHeight = Mathf.Clamp(localHeight + 0.1f, 0.14f, 0.9f);
                    m_messageBacking.localScale = new Vector3(localWidth, localHeight, 0.004f);
                    m_messageResizePending = false;
                }
            }

            // Fade in, hold (per-message), fade out, then hide.
            const float fadeIn = 0.25f;
            const float fadeOut = 0.45f;
            var hold = m_messageHoldDuration;
            var elapsed = Time.time - m_messageStartTime;

            float alpha;
            if (elapsed < fadeIn)
            {
                alpha = elapsed / fadeIn;
            }
            else if (elapsed < fadeIn + hold)
            {
                alpha = 1f;
            }
            else if (elapsed < fadeIn + hold + fadeOut)
            {
                alpha = 1f - (elapsed - fadeIn - hold) / fadeOut;
            }
            else
            {
                m_messagePanel.SetActive(false);
                return;
            }

            SetMessageAlpha(Mathf.Clamp01(alpha));
        }

        /// <summary>Applies an alpha to both the message text and its backing material.</summary>
        private void SetMessageAlpha(float alpha)
        {
            if (m_messageText != null)
            {
                var textColor = m_messageColor;
                textColor.a = alpha;
                m_messageText.color = textColor;
            }

            if (m_messageBackingMaterial != null)
            {
                var backingColor = m_messageBackingMaterial.color;
                backingColor.a = alpha * 0.82f;
                m_messageBackingMaterial.color = backingColor;
            }
        }

        /// <summary>Starts/maintains/ends a manual door grip, or falls back to poke-to-open selection.</summary>
        private void HandleDoorGrab()
        {
            if (m_doorPivot == null)
            {
                return;
            }

            if (m_doorGrabController == OVRInput.Controller.None)
            {
                TryStartDoorGrab(m_cameraRig != null ? m_cameraRig.rightControllerAnchor : null, OVRInput.RawButton.RHandTrigger, OVRInput.Controller.RTouch);
                TryStartDoorGrab(m_cameraRig != null ? m_cameraRig.leftControllerAnchor : null, OVRInput.RawButton.LHandTrigger, OVRInput.Controller.LTouch);
                if (m_doorGrabController == OVRInput.Controller.None)
                {
                    UpdateDoorPokeSelection();
                }

                return;
            }

            var gripAxis = m_doorGrabController == OVRInput.Controller.LTouch
                ? OVRInput.RawAxis1D.LHandTrigger
                : OVRInput.RawAxis1D.RHandTrigger;
            if (m_doorGrabAnchor == null || OVRInput.Get(gripAxis) < 0.35f)
            {
                EndDoorGrab();
            }
        }

        /// <summary>Begins a manual door grip if the controller grips while near a knob.</summary>
        private void TryStartDoorGrab(Transform anchor, OVRInput.RawButton gripButton, OVRInput.Controller controller)
        {
            if (m_doorGrabController != OVRInput.Controller.None || anchor == null)
            {
                return;
            }

            if (!OVRInput.GetDown(gripButton) || !IsControllerNearDoorKnob(anchor))
            {
                return;
            }

            m_doorGrabController = controller;
            m_lastDoorGrabController = controller;
            m_doorGrabAnchor = anchor;
            m_doorGrabAngleOffset = Mathf.DeltaAngle(ComputeHandHingeAngle(anchor.position), m_doorCurrentAngle);
            PulseHaptic(controller, 0.4f, 0.25f, 0.06f);
        }

        /// <summary>Releases the manual door grip.</summary>
        private void EndDoorGrab()
        {
            m_doorGrabController = OVRInput.Controller.None;
            m_doorGrabAnchor = null;
        }

        // Poke (proximity, no grip) the door knob to auto-open/-close it — no aim, no manual swing.
        // Armed-once: each fresh approach toggles the door, then re-arms when the hand leaves.
        /// <summary>Poking a knob toggles the door open/closed via the auto driver (re-arms when hand leaves).</summary>
        private void UpdateDoorPokeSelection()
        {
            var nearKnob = m_cameraRig != null
                && (IsControllerNearDoorKnob(m_cameraRig.rightControllerAnchor)
                    || IsControllerNearDoorKnob(m_cameraRig.leftControllerAnchor));

            if (!nearKnob)
            {
                m_doorAutoArmed = true;
                return;
            }

            if (!m_doorAutoArmed)
            {
                return;
            }

            m_doorAutoArmed = false;
            m_doorAutoActive = true;
            // Toggle: if the auto target is open, close it; otherwise open it.
            m_doorAutoTarget = Mathf.Abs(m_doorAutoTarget) > 1f ? 0f : m_doorOpenAngle;
            PulseHaptic(OVRInput.Controller.RTouch, 0.5f, 0.4f, 0.06f);
        }

        /// <summary>Converts a world hand position into the door hinge angle it implies.</summary>
        private float ComputeHandHingeAngle(Vector3 worldPosition)
        {
            var parentSpace = m_doorPivot.parent != null
                ? m_doorPivot.parent.InverseTransformPoint(worldPosition)
                : worldPosition;
            var offset = parentSpace - m_doorPivot.localPosition;
            if (new Vector2(offset.x, offset.z).sqrMagnitude < 0.0001f)
            {
                return m_doorCurrentAngle;
            }

            // Door yaw 0 points along +X from the hinge; Unity yaw rotates +X toward -Z.
            return Mathf.Atan2(-offset.z, offset.x) * Mathf.Rad2Deg;
        }

        /// <summary>Advances the door angle each frame: manual swing, auto open/close, or latch shut.</summary>
        private void UpdateDoorMotion()
        {
            if (m_doorPivot == null)
            {
                return;
            }

            var minAngle = Mathf.Min(0f, m_doorOpenAngle);
            var maxAngle = Mathf.Max(0f, m_doorOpenAngle);

            if (m_doorGrabController != OVRInput.Controller.None && m_doorGrabAnchor != null)
            {
                // Manual swing (fallback) overrides the auto-driver.
                m_doorAutoActive = false;
                var targetAngle = ComputeHandHingeAngle(m_doorGrabAnchor.position) + m_doorGrabAngleOffset;
                targetAngle = Mathf.Clamp(targetAngle, minAngle, maxAngle);
                var follow = 1f - Mathf.Exp(-m_doorFollowSharpness * Time.deltaTime);
                m_doorCurrentAngle = Mathf.LerpAngle(m_doorCurrentAngle, targetAngle, follow);
            }
            else if (m_doorAutoActive)
            {
                // Auto-open/close (knob poked): smooth-follow toward the auto target angle.
                var follow = 1f - Mathf.Exp(-m_doorFollowSharpness * Time.deltaTime);
                m_doorCurrentAngle = Mathf.LerpAngle(m_doorCurrentAngle, m_doorAutoTarget, follow);
            }
            else if (m_doorCurrentAngle != 0f && Mathf.Abs(m_doorCurrentAngle) <= m_doorLatchAngle)
            {
                // Latch: a nearly-closed released door settles shut with a click.
                m_doorCurrentAngle = Mathf.MoveTowards(m_doorCurrentAngle, 0f, 60f * Time.deltaTime);
                if (m_doorCurrentAngle == 0f)
                {
                    PulseHaptic(m_lastDoorGrabController, 0.6f, 0.4f, 0.08f);
                }
            }

            m_doorCurrentAngle = Mathf.Clamp(m_doorCurrentAngle, minAngle, maxAngle);
            m_doorPivot.localRotation = Quaternion.Euler(0f, m_doorCurrentAngle, 0f);
        }

        /// <summary>Starts a timed haptic pulse on a controller (stopped later by UpdateHapticPulse).</summary>
        private void PulseHaptic(OVRInput.Controller controller, float frequency, float amplitude, float duration)
        {
            if (controller == OVRInput.Controller.None)
            {
                return;
            }

            OVRInput.SetControllerVibration(frequency, amplitude, controller);
            if (controller == OVRInput.Controller.LTouch)
            {
                m_leftPulseActive = true;
                m_leftPulseEndTime = Time.time + duration;
            }
            else
            {
                m_rightPulseActive = true;
                m_rightPulseEndTime = Time.time + duration;
            }
        }

        /// <summary>Stops each controller's haptic pulse once its scheduled end time has passed.</summary>
        private void UpdateHapticPulse()
        {
            if (m_leftPulseActive && Time.time >= m_leftPulseEndTime)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch);
                m_leftPulseActive = false;
            }

            if (m_rightPulseActive && Time.time >= m_rightPulseEndTime)
            {
                OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.RTouch);
                m_rightPulseActive = false;
            }
        }

        /// <summary>True if a controller is within the interaction radius of either door knob.</summary>
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

        /// <summary>True if a point is within a radius of a (possibly null) target transform.</summary>
        private static bool IsPointNearTransform(Vector3 point, Transform target, float radius)
        {
            return target != null && Vector3.Distance(point, target.position) <= radius;
        }

        /// <summary>Rotates the rig about the player's head by a fixed yaw (snap turn).</summary>
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

        /// <summary>True if a position is fully valid to stand on (in-room, clear of walls/door/props/barrels).</summary>
        private bool IsWalkablePosition(Vector3 position)
        {
            var clamped = ClampToRoom(position);
            if (HorizontalDistance(clamped, position) > 0.02f)
            {
                return false;
            }

            if (!IsInsideWalkableFloorPlan(position, m_edgeMargin))
            {
                return false;
            }

            if (!IsDoorOpenForNavigation && IsInsideClosedDoorObstacle(position, m_edgeMargin))
            {
                return false;
            }

            foreach (var obstacle in m_navigationObstacles)
            {
                if (IsPointInsideRect(position, obstacle, m_edgeMargin))
                {
                    return false;
                }
            }

            // Floor barrels use a tight keep-out (~radius + m_barrelClearance) so the player can
            // step right up to a barrel to scan it, but never stand inside the cylinder.
            foreach (var barrel in m_barrelObstacles)
            {
                if (IsPointInsideRect(position, barrel, m_barrelClearance))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Clamps a position into the nearest walkable region (lab, spawn room, or doorway).</summary>
        private Vector3 ClampToRoom(Vector3 position)
        {
            if (IsInsideWalkableFloorPlan(position, m_edgeMargin))
            {
                return position;
            }

            var margin = m_edgeMargin;
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

        /// <summary>True if a position is inside either room and not blocked by the solid shared wall.</summary>
        private bool IsInsideWalkableFloorPlan(Vector3 position, float margin)
        {
            var insideRoom = IsInsideLabRoom(position, margin) || IsInsideSpawnRoom(position, margin);
            return insideRoom && !IsBlockedBySharedWall(position, margin);
        }

        /// <summary>True if a position falls within the closed door's swept doorway keep-out.</summary>
        private bool IsInsideClosedDoorObstacle(Vector3 position, float padding)
        {
            var doorwayCenter = GetDoorwayCenter();
            var halfWidth = Mathf.Max(0.1f, m_doorwayWidth * 0.5f);
            var halfDepth = m_wallThickness * 0.5f;
            return Mathf.Abs(position.x - doorwayCenter.x) <= halfWidth + padding
                   && Mathf.Abs(position.z - doorwayCenter.z) <= halfDepth + padding * 0.5f;
        }

        /// <summary>True if a teleport segment would pass through the doorway while the door is shut.</summary>
        private bool TeleportCrossesClosedDoor(Vector3 from, Vector3 to)
        {
            if (IsDoorOpenForNavigation)
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

        // Blocks teleporting straight through the SOLID part of the shared wall (the segments either
        // side of the doorway), regardless of door state. The nearest-standable snap can otherwise
        // land a target up to ~0.72 m past the 0.20 m wall keep-out band — i.e. in the other room —
        // when the near side is fully blocked by drums/furniture against the wall. A legitimate
        // through-the-doorway teleport (|crossingX| within the opening) is still allowed here and is
        // gated separately by TeleportCrossesClosedDoor when the door is shut.
        /// <summary>True if a teleport segment would pass through the solid part of the shared wall.</summary>
        private bool TeleportCrossesSolidWall(Vector3 from, Vector3 to)
        {
            var wallZ = GetDoorwayCenter().z;
            var fromSide = from.z - wallZ;
            var toSide = to.z - wallZ;
            if (Mathf.Approximately(fromSide, 0f) || Mathf.Approximately(toSide, 0f) || Mathf.Sign(fromSide) == Mathf.Sign(toSide))
            {
                return false;
            }

            var segmentZ = to.z - from.z;
            if (Mathf.Approximately(segmentZ, 0f))
            {
                return false;
            }

            var t = Mathf.Clamp01((wallZ - from.z) / segmentZ);
            var crossingX = Mathf.Lerp(from.x, to.x, t);
            // Outside the doorway opening => it is solid wall, so the crossing is never allowed.
            return Mathf.Abs(crossingX) > m_doorwayWidth * 0.5f;
        }

        /// <summary>True if a position lies inside the lab room bounds (minus a margin).</summary>
        private bool IsInsideLabRoom(Vector3 position, float margin)
        {
            var size = RoomSizeMeters;
            var halfWidth = size.x * 0.5f - margin;
            var halfDepth = size.y * 0.5f;
            return Mathf.Abs(position.x) <= halfWidth
                   && position.z <= halfDepth - margin
                   && position.z >= -halfDepth;
        }

        /// <summary>True if a position lies inside the spawn room bounds (minus a margin).</summary>
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

        /// <summary>True if a position is on the shared wall line but outside the doorway opening.</summary>
        private bool IsBlockedBySharedWall(Vector3 position, float margin)
        {
            var wallZ = -RoomSizeMeters.y * 0.5f;
            var doorHalfWidth = Mathf.Max(0.05f, m_doorwayWidth * 0.5f - margin * 0.3f);
            return Mathf.Abs(position.z - wallZ) < margin && Mathf.Abs(position.x) > doorHalfWidth;
        }

        /// <summary>Clamps a position into a centred, margin-inset rectangle (keeping its Y).</summary>
        private static Vector3 ClampToRect(Vector3 position, Vector3 center, Vector2 size, float margin)
        {
            var halfWidth = Mathf.Max(0.05f, size.x * 0.5f - margin);
            var halfDepth = Mathf.Max(0.05f, size.y * 0.5f - margin);
            return new Vector3(
                Mathf.Clamp(position.x, center.x - halfWidth, center.x + halfWidth),
                position.y,
                Mathf.Clamp(position.z, center.z - halfDepth, center.z + halfDepth));
        }

        /// <summary>World centre of the doorway opening on the shared wall.</summary>
        private Vector3 GetDoorwayCenter()
        {
            return new Vector3(0f, 0f, -RoomSizeMeters.y * 0.5f);
        }

        /// <summary>True if a prop footprint clears the aisle, player start, other props and the room bounds.</summary>
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
                if (FootprintsOverlap(footprint, existing, padding))
                {
                    return false;
                }
            }

            return IsRectInsideRoom(footprint, padding);
        }

        /// <summary>True if any corner or the centre of a rect intrudes into the central cross aisle.</summary>
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

        /// <summary>True if a position lies within the central cross aisle (kept clear of barrels).</summary>
        private bool IsInCentralAisle(Vector3 position)
        {
            return Mathf.Abs(position.x) < m_centralAisleWidth * 0.5f || Mathf.Abs(position.z) < m_centralAisleWidth * 0.5f;
        }

        /// <summary>True if a position lies inside any furniture/pedestal navigation obstacle.</summary>
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

        /// <summary>True if a point lies inside an oriented rect (plus padding), tested in the rect's local frame.</summary>
        private static bool IsPointInsideRect(Vector3 point, ObstacleRect rect, float padding)
        {
            var local = Rotate(new Vector2(point.x, point.z) - rect.Center, -rect.YawDegrees);
            return Mathf.Abs(local.x) <= rect.HalfExtents.x + padding && Mathf.Abs(local.y) <= rect.HalfExtents.y + padding;
        }

        /// <summary>Corner-containment overlap test between two oriented rects (see FootprintsOverlap for the exact variant).</summary>
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

        /// <summary>True if every (padded) corner of a rect lies within the lab room bounds.</summary>
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

        /// <summary>Returns the four world-space corners of an oriented, padded rect.</summary>
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

        /// <summary>Returns a random floor position in one of the four quadrants, outside the central aisle.</summary>
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

        /// <summary>Distance between two points ignoring their Y (horizontal plane).</summary>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Lazily creates the root object that holds all per-round scenario content.</summary>
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

        /// <summary>In-place Fisher-Yates shuffle of a list using the Unity RNG.</summary>
        private static void Shuffle<T>(IList<T> list)
        {
            for (var i = list.Count - 1; i > 0; i--)
            {
                var swapIndex = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[swapIndex]) = (list[swapIndex], list[i]);
            }
        }

        /// <summary>Returns a random background count value from the profile, or the fallback pool.</summary>
        private int GetRandomRadiationCount()
        {
            if (m_radiationCountProfile != null)
            {
                return m_radiationCountProfile.GetRandomCount();
            }

            EnsureFallbackCountPool();
            return m_fallbackCountPool[UnityEngine.Random.Range(0, m_fallbackCountPool.Length)];
        }

        /// <summary>Rebuilds the fallback count pool if it is missing or the wrong size.</summary>
        private void EnsureFallbackCountPool()
        {
            if (m_fallbackCountPool == null || m_fallbackCountPool.Length != Mathf.Max(1, m_fallbackCountPoolSize))
            {
                RebuildFallbackCountPool();
            }
        }

        /// <summary>Fills the fallback count pool with random values in the configured min/max range.</summary>
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

        /// <summary>Convenience overload: creates a plain Standard-shader material with default metallic/smoothness.</summary>
        private Material CreateMaterial(Color color, string materialName)
        {
            return CreateMaterial(color, materialName, 0f, 0.5f, null, null);
        }

        /// <summary>
        /// Creates (or returns a cached) Standard-shader material with the given colour, PBR values,
        /// optional albedo and optional emission (which clones the pre-authored emissive base so the
        /// _EMISSION variant survives shader stripping). Tracked for cleanup in OnDestroy.
        /// </summary>
        private Material CreateMaterial(Color color, string materialName, float metallic, float smoothness, Texture2D albedo, Color? emission)
        {
            var albedoId = albedo != null ? albedo.GetInstanceID() : 0;
            var emissionKey = emission.HasValue
                ? $"{emission.Value.r:0.00}_{emission.Value.g:0.00}_{emission.Value.b:0.00}"
                : "none";
            var key = $"{materialName}_{color.r:0.000}_{color.g:0.000}_{color.b:0.000}_{color.a:0.000}_{metallic:0.00}_{smoothness:0.00}_{albedoId}_{emissionKey}";
            if (m_materialCache.TryGetValue(key, out var existingMaterial))
            {
                return existingMaterial;
            }

            Material material;
            if (emission.HasValue)
            {
                // Clone the pre-authored emissive material so the _EMISSION variant ships in builds.
                var emissiveBase = Resources.Load<Material>("SimJamEmissiveScreen");
                material = emissiveBase != null ? new Material(emissiveBase) : new Material(Shader.Find("Standard"));
                if (emissiveBase == null)
                {
                    material.EnableKeyword("_EMISSION");
                }

                material.SetColor("_EmissionColor", emission.Value);
            }
            else
            {
                material = new Material(Shader.Find("Standard"));
            }

            material.name = materialName;
            material.color = color;
            material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            if (albedo != null)
            {
                material.mainTexture = albedo;
            }

            m_materialCache[key] = material;
            m_runtimeMaterials.Add(material);
            return material;
        }

        /// <summary>Logs a prefixed status/debug line (no on-screen UI).</summary>
        private void SetStatus(string message)
        {
            Debug.Log($"RadiationLabRoomSpawner: {message}");
        }

        /// <summary>Unity lifecycle: destroys all runtime-created materials and label meshes to avoid leaks.</summary>
        private void OnDestroy()
        {
            foreach (var runtimeMaterial in m_runtimeMaterials)
            {
                if (runtimeMaterial != null)
                {
                    Destroy(runtimeMaterial);
                }
            }

            foreach (var labelMesh in m_barrelLabelMeshes.Values)
            {
                if (labelMesh != null)
                {
                    Destroy(labelMesh);
                }
            }

            m_runtimeMaterials.Clear();
            m_materialCache.Clear();
            m_barrelTintVariants.Clear();
            m_barrelLabelMeshes.Clear();
        }

        /// <summary>Editor lifecycle: clamps serialized tunables to valid ranges and clears the fit cache.</summary>
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
            m_maxSourceActivityCps = Mathf.Max(m_minSourceActivityCps, m_maxSourceActivityCps);
            m_fittedScaleCache.Clear();
        }

        /// <summary>Editor lifecycle: draws wireframe previews of the rooms, doorway, aisle and player start.</summary>
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

        /// <summary>Result of a single layout attempt: how many barrels were successfully placed.</summary>
        private struct ScenarioResult
        {
            /// <summary>Number of barrels placed in this layout attempt.</summary>
            public int SpawnedCount;
        }
    }
}
