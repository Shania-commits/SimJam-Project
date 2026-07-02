using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Spawner that scatters radioactive-barrel props onto the player's real-world floor using the
    /// Meta MR Utility Kit (MRUK) room scan. Instead of building a fixed procedural lab, it loads the
    /// Quest's captured room geometry at runtime and places a randomized number of barrels on valid
    /// floor spots (respecting wall/edge/player/spacing clearances), assigns each a random radiation
    /// count, and shows a head-locked status label. Controller buttons let the player reshuffle, clear,
    /// re-roll counts, and rescale the barrels live. This is the mixed-reality "play in your own room"
    /// variant of the find-the-hidden-barrel game loop.
    /// </summary>
    public class RandomRoomBarrelSpawner : MonoBehaviour
    {
        [Header("Barrel prefab")]
        /// <summary>Prefab instantiated for each spawned barrel; if null, a primitive placeholder barrel is built instead.</summary>
        [SerializeField] private GameObject m_barrelPrefab;
        /// <summary>Optional profile that supplies realistic random radiation counts per barrel; falls back to a generated pool when unset.</summary>
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        /// <summary>Local scale applied to each spawned barrel prefab (tuned to the identiFINDER-scale drum art).</summary>
        [SerializeField] private Vector3 m_barrelSpawnScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        /// <summary>Fractional step used when the player grows/shrinks barrels at runtime via the thumbstick.</summary>
        [SerializeField, Range(0.01f, 0.5f)] private float m_runtimeScaleStep = 0.15f;
        /// <summary>Min (x) and max (y) clamp bounds applied to each axis of the live barrel scale.</summary>
        [SerializeField] private Vector2 m_runtimeScaleLimits = new Vector2(0.03f, 3f);
        /// <summary>When true, each barrel displays a floating debug label showing its assigned radiation count.</summary>
        [SerializeField] private bool m_showDebugCountLabels = true;

        [Header("Status label")]
        /// <summary>Distance in front of the camera at which the head-locked status label floats.</summary>
        [SerializeField, Min(0.5f)] private float m_statusLabelDistance = 2.25f;
        /// <summary>Vertical offset (world Y) applied to the head-locked status label so it sits below eye line.</summary>
        [SerializeField] private float m_statusLabelVerticalOffset = -0.55f;
        /// <summary>World character size of the status label TextMesh.</summary>
        [SerializeField, Min(0.01f)] private float m_statusLabelCharacterSize = 0.04f;
        /// <summary>Font point size used when rendering the status label TextMesh.</summary>
        [SerializeField, Min(8)] private int m_statusLabelFontSize = 48;
        /// <summary>Seconds to wait for the user to grant the Scene/Spatial-Data permission before giving up.</summary>
        [SerializeField, Min(1f)] private float m_scenePermissionWaitSeconds = 20f;

        [Header("Run randomization")]
        /// <summary>Inclusive lower bound on the number of barrels spawned per run.</summary>
        [SerializeField, Min(1)] private int m_minBarrels = 3;
        /// <summary>Inclusive upper bound on the number of barrels spawned per run.</summary>
        [SerializeField, Min(1)] private int m_maxBarrels = 6;
        /// <summary>Probability [0..1] that any given barrel is placed upright rather than lying on its side.</summary>
        [SerializeField, Range(0f, 1f)] private float m_uprightProbability = 0.6f;
        /// <summary>When true, seeds Unity's RNG with <see cref="m_fixedSeed"/> so runs are reproducible.</summary>
        [SerializeField] private bool m_useFixedSeed;
        /// <summary>Deterministic seed used for placement/orientation when <see cref="m_useFixedSeed"/> is enabled.</summary>
        [SerializeField] private int m_fixedSeed = 12345;

        [Header("Placement constraints")]
        /// <summary>Minimum distance a barrel must sit from the floor's edge when sampling a surface point.</summary>
        [SerializeField, Min(0.05f)] private float m_floorEdgeClearance = 0.45f;
        /// <summary>Minimum distance a candidate spot must keep from any wall face.</summary>
        [SerializeField, Min(0.05f)] private float m_wallClearance = 0.6f;
        /// <summary>Clearance used when rejecting candidates that overlap detected scene volumes (furniture, etc.).</summary>
        [SerializeField, Min(0.05f)] private float m_sceneVolumeClearance = 0.25f;
        /// <summary>Minimum spacing enforced between two spawned barrels.</summary>
        [SerializeField, Min(0.05f)] private float m_barrelSpacing = 0.85f;
        /// <summary>Minimum distance a barrel must keep from the player's head so nothing spawns on top of them.</summary>
        [SerializeField, Min(0.05f)] private float m_playerClearance = 1.1f;
        /// <summary>How many random surface samples to try before giving up on placing a single barrel.</summary>
        [SerializeField, Min(1)] private int m_maxAttemptsPerBarrel = 120;
        /// <summary>Height offset above the floor for an upright barrel (raises its pivot to the drum's mid-height).</summary>
        [SerializeField] private float m_uprightFloorOffset = 0.35f;
        /// <summary>Height offset above the floor for a barrel lying on its side.</summary>
        [SerializeField] private float m_sidewaysFloorOffset = 0.18f;

        [Header("Fallback counts")]
        /// <summary>Size of the pre-generated random-count pool used when no <see cref="RadiationCountProfile"/> is assigned.</summary>
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        /// <summary>Lower bound for values in the fallback random-count pool.</summary>
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        /// <summary>Upper bound for values in the fallback random-count pool.</summary>
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        /// <summary>All barrel instances spawned in the current run (destroyed on clear/reshuffle).</summary>
        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        /// <summary>World positions of spawned barrels, used to enforce inter-barrel spacing.</summary>
        private readonly List<Vector3> m_spawnedPositions = new();
        /// <summary>Cached pool of random radiation counts used when no profile is assigned.</summary>
        private int[] m_fallbackCountPool;
        /// <summary>The MRUK room scan currently used as the placement surface source.</summary>
        private MRUKRoom m_currentRoom;
        /// <summary>Head-locked TextMesh that reports loading/placement status to the player.</summary>
        private TextMesh m_statusLabel;
        /// <summary>Human-readable reason the room failed to load, shown to the player and reused on retry.</summary>
        private string m_roomLoadFailureStatus;
        /// <summary>True once a room has loaded successfully and runs can be generated.</summary>
        private bool m_isReady;
        /// <summary>Guards <see cref="GenerateRun"/> against re-entry while a run is being built.</summary>
        private bool m_isGenerating;
        /// <summary>True while the async MRUK room load is in progress.</summary>
        private bool m_isLoadingRoom;

        /// <summary>
        /// Unity entry point: disables the borrowed object-detection sample components, spins up the
        /// status label, then asynchronously loads the Quest room scan and generates the first run.
        /// </summary>
        private async void Start()
        {
            DisableObjectDetectionSampleComponents();
            EnsureStatusLabel();
            SetStatus("Loading room data...");

            try
            {
                m_isLoadingRoom = true;
                m_currentRoom = await LoadRoomAsync();
                m_isLoadingRoom = false;
                if (m_currentRoom == null)
                {
                    SetStatus(m_roomLoadFailureStatus ?? "No room data. Complete Quest space setup, then reopen this scene.");
                    return;
                }

                m_isReady = true;
                GenerateRun();
            }
            catch (Exception exception)
            {
                m_isLoadingRoom = false;
                Debug.LogException(exception);
                SetStatus("Room randomization failed. Check Unity logs.");
            }
        }

        /// <summary>
        /// Per-frame loop: keeps the status label in front of the player and polls controller input for
        /// reshuffle (A/pinch), clear (B/middle-pinch), re-roll counts (X), and grow/shrink (thumbstick).
        /// </summary>
        private void Update()
        {
            UpdateStatusLabelPose();

            if (InputManager.IsButtonADownOrPinchStarted())
            {
                if (m_isReady)
                {
                    GenerateRun();
                }
                else
                {
                    SetStatus(m_isLoadingRoom
                        ? "Loading Quest room data. Finish Space Setup if prompted."
                        : m_roomLoadFailureStatus ?? "No room loaded. Reopen this scene after Space Setup.");
                }
            }

            if (InputManager.IsButtonBDownOrMiddleFingerPinchStarted())
            {
                ClearBarrels();
                SetStatus("Barrels cleared. Press A to reshuffle.");
            }

            if (InputManager.IsButtonXDown())
            {
                RandomizeRadiationCounts();
            }

            if (InputManager.IsRightThumbstickUpDown())
            {
                AdjustRuntimeBarrelScale(1f + m_runtimeScaleStep);
            }
            else if (InputManager.IsRightThumbstickDownDown())
            {
                AdjustRuntimeBarrelScale(1f - m_runtimeScaleStep);
            }
        }

        /// <summary>
        /// Clears any existing barrels and lays out a fresh randomized run: picks a barrel count, builds
        /// an upright/sideways orientation plan, then searches the room for valid floor placements and
        /// spawns a barrel at each. Reports how many of the target count were successfully placed.
        /// </summary>
        public void GenerateRun()
        {
            if (!m_isReady || m_currentRoom == null || m_isGenerating)
            {
                return;
            }

            m_isGenerating = true;
            ClearBarrels();

            if (m_useFixedSeed)
            {
                UnityEngine.Random.InitState(m_fixedSeed);
            }

            try
            {
                var targetCount = UnityEngine.Random.Range(Mathf.Min(m_minBarrels, m_maxBarrels), Mathf.Max(m_minBarrels, m_maxBarrels) + 1);
                var orientationPlan = BuildOrientationPlan(targetCount);
                var spawnedCount = 0;
                for (var i = 0; i < targetCount; i++)
                {
                    if (TryFindPlacement(orientationPlan[i], out var position, out var rotation, out var orientationName))
                    {
                        var barrel = SpawnBarrel(position, rotation, orientationName);
                        m_spawnedBarrels.Add(barrel);
                        m_spawnedPositions.Add(position);
                        spawnedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find a valid room placement for barrel {i + 1}/{targetCount}.");
                    }
                }

                SetStatus(spawnedCount == targetCount
                    ? $"Randomized {spawnedCount} barrels. A reshuffles."
                    : $"Placed {spawnedCount}/{targetCount}; room may be tight.");
            }
            finally
            {
                m_isGenerating = false;
            }
        }

        /// <summary>
        /// Builds a per-barrel upright/sideways plan by rolling <see cref="m_uprightProbability"/> for each
        /// slot, then forces at least one flip when every barrel came out the same way so runs always mix
        /// both orientations (only when more than one barrel is requested).
        /// </summary>
        private bool[] BuildOrientationPlan(int targetCount)
        {
            var orientations = new bool[targetCount];
            var uprightCount = 0;
            for (var i = 0; i < orientations.Length; i++)
            {
                orientations[i] = UnityEngine.Random.value <= m_uprightProbability;
                if (orientations[i])
                {
                    uprightCount++;
                }
            }

            if (targetCount > 1 && (uprightCount == 0 || uprightCount == targetCount))
            {
                var flipIndex = UnityEngine.Random.Range(0, targetCount);
                orientations[flipIndex] = !orientations[flipIndex];
            }

            return orientations;
        }

        /// <summary>Destroys all spawned barrel GameObjects and resets the tracking lists.</summary>
        public void ClearBarrels()
        {
            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel != null)
                {
                    Destroy(barrel.gameObject);
                }
            }

            m_spawnedBarrels.Clear();
            m_spawnedPositions.Clear();
        }

        /// <summary>
        /// Re-rolls the radiation count on every currently spawned barrel without moving them, rebuilding
        /// both the profile's generated pool and the fallback pool first so the new values are fresh.
        /// </summary>
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

        /// <summary>
        /// Ensures Scene permission, obtains (or creates) the MRUK instance, configures it for a manual
        /// device load, and returns the current room scan — requesting Space Setup capture if none exists.
        /// Returns null and sets <see cref="m_roomLoadFailureStatus"/> on any failure.
        /// </summary>
        private async Task<MRUKRoom> LoadRoomAsync()
        {
            m_roomLoadFailureStatus = null;

            if (!await EnsureScenePermissionAsync())
            {
                return null;
            }

            var mruk = MRUK.Instance;
            if (mruk == null)
            {
                var mrukObject = new GameObject("MRUK Runtime Loader");
                mruk = mrukObject.AddComponent<MRUK>();
            }

            ConfigureMrukForManualDeviceLoad(mruk);

            var room = GetCurrentRoom(mruk);
            if (room != null)
            {
                return room;
            }

            SetStatus("Loading Quest room scan...");
            var result = await mruk.LoadSceneFromDevice(
                requestSceneCaptureIfNoDataFound: true,
                removeMissingRooms: true,
                sceneModel: MRUK.SceneModel.V2FallbackV1);

            if (result != MRUK.LoadDeviceResult.Success)
            {
                Debug.LogWarning($"MRUK room load failed: {result}");
                m_roomLoadFailureStatus = GetRoomLoadFailureStatus(result);
                return null;
            }

            room = GetCurrentRoom(mruk);
            if (room == null)
            {
                m_roomLoadFailureStatus = "MRUK loaded, but no room was returned. Redo Quest Space Setup, then reopen.";
            }

            return room;
        }

        /// <summary>
        /// Requests the Quest Scene/Spatial-Data permission if not already granted and polls until it is
        /// granted or the wait times out. Returns true once permission is available.
        /// </summary>
        private async Task<bool> EnsureScenePermissionAsync()
        {
            if (OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
            {
                return true;
            }

            SetStatus("Allow Scene permission in the headset...");
            OVRPermissionsRequester.Request(new[] { OVRPermissionsRequester.Permission.Scene });

            var timeoutAt = DateTime.UtcNow.AddSeconds(m_scenePermissionWaitSeconds);
            while (DateTime.UtcNow < timeoutAt)
            {
                if (OVRPermissionsRequester.IsPermissionGranted(OVRPermissionsRequester.Permission.Scene))
                {
                    return true;
                }

                await Task.Delay(250);
            }

            m_roomLoadFailureStatus = "Scene permission not granted. Allow Spatial Data/Scene permission, then reopen.";
            return false;
        }

        /// <summary>Maps an MRUK device-load failure code to a player-facing instruction on how to fix it.</summary>
        private static string GetRoomLoadFailureStatus(MRUK.LoadDeviceResult result)
        {
            return result switch
            {
                MRUK.LoadDeviceResult.NoScenePermission => "Scene permission denied. Allow Spatial Data/Scene permission, then reopen.",
                MRUK.LoadDeviceResult.NoRoomsFound => "No Quest room scan found. Run Space Setup, save it, then reopen.",
                MRUK.LoadDeviceResult.DiscoveryOngoing => "Quest is still loading scene data. Wait a moment, then press A.",
                MRUK.LoadDeviceResult.FailureInsufficientView => "Quest needs a better view. Look around the room, then reopen.",
                MRUK.LoadDeviceResult.FailurePermissionInsufficient => "Scene permission is insufficient. Check app permissions, then reopen.",
                MRUK.LoadDeviceResult.FailureTooDark => "Room is too dark for scene data. Add light, then reopen.",
                MRUK.LoadDeviceResult.FailureTooBright => "Room is too bright for scene data. Reduce glare, then reopen.",
                _ => $"MRUK room load failed: {result}. Redo Space Setup, then reopen."
            };
        }

        /// <summary>
        /// Sets MRUK's scene settings to load room data from the device on demand (not on startup) with
        /// high-fidelity geometry, so this spawner controls exactly when the room is fetched.
        /// </summary>
        private static void ConfigureMrukForManualDeviceLoad(MRUK mruk)
        {
            mruk.SceneSettings ??= new MRUK.MRUKSettings();
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            mruk.SceneSettings.LoadSceneOnStartup = false;
            mruk.SceneSettings.EnableHighFidelityScene = true;
        }

        /// <summary>Returns MRUK's active room, falling back to the first loaded room, or null if none exist.</summary>
        private static MRUKRoom GetCurrentRoom(MRUK mruk)
        {
            if (mruk == null)
            {
                return null;
            }

            var room = mruk.GetCurrentRoom();
            if (room != null)
            {
                return room;
            }

            return mruk.Rooms.Count > 0 ? mruk.Rooms[0] : null;
        }

        /// <summary>
        /// Repeatedly samples random points on the room's floor and returns the first that satisfies all
        /// placement constraints (inside room, clear of scene volumes, walls, the player, and other
        /// barrels). Outputs the accepted world position, a floor-aligned rotation with random yaw (tipped
        /// 90 deg for sideways barrels), and a descriptive orientation name. Returns false if no valid
        /// spot is found within <see cref="m_maxAttemptsPerBarrel"/> attempts.
        /// </summary>
        private bool TryFindPlacement(bool isUpright, out Vector3 position, out Quaternion rotation, out string orientationName)
        {
            var floorFilter = new LabelFilter(MRUKAnchor.SceneLabels.FLOOR);
            var wallFilter = new LabelFilter(MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE);
            var playerPosition = GetPlayerPosition();

            for (var attempt = 0; attempt < m_maxAttemptsPerBarrel; attempt++)
            {
                if (!m_currentRoom.GenerateRandomPositionOnSurface(MRUK.SurfaceType.FACING_UP, m_floorEdgeClearance, floorFilter, out var candidate, out var normal))
                {
                    break;
                }

                if (!m_currentRoom.IsPositionInRoom(candidate))
                {
                    continue;
                }

                if (m_currentRoom.IsPositionInSceneVolume(candidate, m_sceneVolumeClearance))
                {
                    continue;
                }

                var wallDistance = m_currentRoom.TryGetClosestSurfacePosition(candidate, out _, out _, out _, wallFilter);
                if (wallDistance <= m_wallClearance)
                {
                    continue;
                }

                if (Vector3.Distance(candidate, playerPosition) <= m_playerClearance)
                {
                    continue;
                }

                if (IsTooCloseToExistingBarrel(candidate))
                {
                    continue;
                }

                var yaw = UnityEngine.Random.Range(0f, 360f);
                var floorNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
                var baseRotation = Quaternion.FromToRotation(Vector3.up, floorNormal) * Quaternion.AngleAxis(yaw, Vector3.up);
                rotation = isUpright ? baseRotation : baseRotation * Quaternion.Euler(0f, 0f, 90f);
                position = candidate + floorNormal * (isUpright ? m_uprightFloorOffset : m_sidewaysFloorOffset);
                orientationName = isUpright ? "floor_upright" : "floor_sideways";
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            orientationName = "none";
            return false;
        }

        /// <summary>
        /// Instantiates the barrel prefab (or a primitive placeholder) at the given pose, ensures it has a
        /// <see cref="BarrelInstance"/>, and initializes it with the orientation name and a random count.
        /// </summary>
        private BarrelInstance SpawnBarrel(Vector3 position, Quaternion rotation, string orientationName)
        {
            GameObject barrelObject;
            if (m_barrelPrefab != null)
            {
                barrelObject = Instantiate(m_barrelPrefab, position, rotation, transform);
                barrelObject.transform.localScale = m_barrelSpawnScale;
            }
            else
            {
                barrelObject = CreatePlaceholderBarrel(position, rotation, transform);
            }

            var barrel = barrelObject.GetComponent<BarrelInstance>();
            if (barrel == null)
            {
                barrel = barrelObject.AddComponent<BarrelInstance>();
            }

            barrel.Initialize(orientationName, GetRandomRadiationCount(), m_showDebugCountLabels);
            return barrel;
        }

        /// <summary>
        /// Builds a simple yellow cylinder stand-in barrel used when no <see cref="m_barrelPrefab"/> is
        /// assigned, so placement/gameplay can still be tested without the real drum art.
        /// </summary>
        private GameObject CreatePlaceholderBarrel(Vector3 position, Quaternion rotation, Transform parent)
        {
            var root = new GameObject("Placeholder Random Barrel");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);

            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Barrel Body";
            cylinder.transform.SetParent(root.transform, false);
            cylinder.transform.localScale = new Vector3(0.45f, 0.45f, 0.45f);

            var renderer = cylinder.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(1f, 0.86f, 0.12f);
            }

            return root;
        }

        /// <summary>Returns true if the candidate position is within <see cref="m_barrelSpacing"/> of any already-placed barrel.</summary>
        private bool IsTooCloseToExistingBarrel(Vector3 candidate)
        {
            foreach (var existingPosition in m_spawnedPositions)
            {
                if (Vector3.Distance(candidate, existingPosition) <= m_barrelSpacing)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Returns the player's head position (main camera), or the origin if no camera is present.</summary>
        private Vector3 GetPlayerPosition()
        {
            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Multiplies the current barrel spawn scale (clamped to the configured limits) and applies it live
        /// to every spawned barrel, letting the player calibrate drum size in-headset via the thumbstick.
        /// </summary>
        private void AdjustRuntimeBarrelScale(float multiplier)
        {
            var nextScale = m_barrelSpawnScale * multiplier;
            nextScale.x = Mathf.Clamp(nextScale.x, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            nextScale.y = Mathf.Clamp(nextScale.y, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            nextScale.z = Mathf.Clamp(nextScale.z, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            m_barrelSpawnScale = nextScale;

            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel != null)
                {
                    barrel.transform.localScale = m_barrelSpawnScale;
                }
            }

            SetStatus($"Barrel scale: {m_barrelSpawnScale.x:0.00}, {m_barrelSpawnScale.y:0.00}, {m_barrelSpawnScale.z:0.00}");
        }

        /// <summary>
        /// Returns a random radiation count from the assigned <see cref="RadiationCountProfile"/>, or from
        /// the internally generated fallback pool when no profile is set.
        /// </summary>
        private int GetRandomRadiationCount()
        {
            if (m_radiationCountProfile != null)
            {
                return m_radiationCountProfile.GetRandomCount();
            }

            EnsureFallbackCountPool();
            return m_fallbackCountPool[UnityEngine.Random.Range(0, m_fallbackCountPool.Length)];
        }

        /// <summary>Lazily (re)builds the fallback count pool if it is missing or no longer matches the configured size.</summary>
        private void EnsureFallbackCountPool()
        {
            if (m_fallbackCountPool == null || m_fallbackCountPool.Length != Mathf.Max(1, m_fallbackCountPoolSize))
            {
                RebuildFallbackCountPool();
            }
        }

        /// <summary>Fills the fallback count pool with fresh uniform-random values between the configured min and max.</summary>
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

        /// <summary>
        /// Disables and deactivates the leftover Passthrough Camera object-detection sample managers
        /// (detection, Sentis inference, and their UI) so the borrowed sample scene does not run its ML
        /// pipeline alongside this barrel game.
        /// </summary>
        private void DisableObjectDetectionSampleComponents()
        {
            foreach (var manager in FindObjectsByType<DetectionManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                manager.enabled = false;
                manager.gameObject.SetActive(false);
            }

            foreach (var manager in FindObjectsByType<SentisInferenceRunManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                manager.enabled = false;
                manager.gameObject.SetActive(false);
            }

            foreach (var manager in FindObjectsByType<DetectionUiMenuManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                manager.enabled = false;
                manager.gameObject.SetActive(false);
            }

            foreach (var manager in FindObjectsByType<SentisInferenceUiManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                manager.enabled = false;
            }
        }

        /// <summary>Lazily creates the head-locked status TextMesh (centered white text) parented to this spawner.</summary>
        private void EnsureStatusLabel()
        {
            if (m_statusLabel != null)
            {
                return;
            }

            var labelObject = new GameObject("Random Room Barrel Status");
            labelObject.transform.SetParent(transform, false);
            m_statusLabel = labelObject.AddComponent<TextMesh>();
            m_statusLabel.anchor = TextAnchor.MiddleCenter;
            m_statusLabel.alignment = TextAlignment.Center;
            m_statusLabel.characterSize = m_statusLabelCharacterSize;
            m_statusLabel.fontSize = m_statusLabelFontSize;
            m_statusLabel.color = Color.white;
        }

        /// <summary>Logs the given message and mirrors it onto the in-world status label.</summary>
        private void SetStatus(string message)
        {
            Debug.Log($"RandomRoomBarrelSpawner: {message}");
            if (m_statusLabel != null)
            {
                m_statusLabel.text = message;
            }
        }

        /// <summary>
        /// Repositions the status label each frame to float a fixed distance in front of the camera and
        /// face the player, keeping it readable as the head moves.
        /// </summary>
        private void UpdateStatusLabelPose()
        {
            if (m_statusLabel == null || Camera.main == null)
            {
                return;
            }

            var cameraTransform = Camera.main.transform;
            var labelTransform = m_statusLabel.transform;
            labelTransform.position = cameraTransform.position + cameraTransform.forward * m_statusLabelDistance + Vector3.up * m_statusLabelVerticalOffset;
            labelTransform.rotation = Quaternion.LookRotation(labelTransform.position - cameraTransform.position, Vector3.up);
        }
    }
}
