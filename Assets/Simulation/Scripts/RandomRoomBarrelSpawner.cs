using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Meta.XR.MRUtilityKit;
using PassthroughCameraSamples.MultiObjectDetection;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public class RandomRoomBarrelSpawner : MonoBehaviour
    {
        [Header("Barrel prefab")]
        [SerializeField] private GameObject m_barrelPrefab;
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        [SerializeField] private Vector3 m_barrelSpawnScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        [SerializeField, Range(0.01f, 0.5f)] private float m_runtimeScaleStep = 0.15f;
        [SerializeField] private Vector2 m_runtimeScaleLimits = new Vector2(0.03f, 3f);
        [SerializeField] private bool m_showDebugCountLabels = true;

        [Header("Status label")]
        [SerializeField, Min(0.5f)] private float m_statusLabelDistance = 2.25f;
        [SerializeField] private float m_statusLabelVerticalOffset = -0.55f;
        [SerializeField, Min(0.01f)] private float m_statusLabelCharacterSize = 0.04f;
        [SerializeField, Min(8)] private int m_statusLabelFontSize = 48;
        [SerializeField, Min(1f)] private float m_scenePermissionWaitSeconds = 20f;

        [Header("Run randomization")]
        [SerializeField, Min(1)] private int m_minBarrels = 3;
        [SerializeField, Min(1)] private int m_maxBarrels = 6;
        [SerializeField, Range(0f, 1f)] private float m_uprightProbability = 0.6f;
        [SerializeField] private bool m_useFixedSeed;
        [SerializeField] private int m_fixedSeed = 12345;

        [Header("Placement constraints")]
        [SerializeField, Min(0.05f)] private float m_floorEdgeClearance = 0.45f;
        [SerializeField, Min(0.05f)] private float m_wallClearance = 0.6f;
        [SerializeField, Min(0.05f)] private float m_sceneVolumeClearance = 0.25f;
        [SerializeField, Min(0.05f)] private float m_barrelSpacing = 0.85f;
        [SerializeField, Min(0.05f)] private float m_playerClearance = 1.1f;
        [SerializeField, Min(1)] private int m_maxAttemptsPerBarrel = 120;
        [SerializeField] private float m_uprightFloorOffset = 0.35f;
        [SerializeField] private float m_sidewaysFloorOffset = 0.18f;

        [Header("Fallback counts")]
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        private readonly List<Vector3> m_spawnedPositions = new();
        private int[] m_fallbackCountPool;
        private MRUKRoom m_currentRoom;
        private TextMesh m_statusLabel;
        private string m_roomLoadFailureStatus;
        private bool m_isReady;
        private bool m_isGenerating;
        private bool m_isLoadingRoom;

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

        private static void ConfigureMrukForManualDeviceLoad(MRUK mruk)
        {
            mruk.SceneSettings ??= new MRUK.MRUKSettings();
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.Device;
            mruk.SceneSettings.LoadSceneOnStartup = false;
            mruk.SceneSettings.EnableHighFidelityScene = true;
        }

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

        private Vector3 GetPlayerPosition()
        {
            if (Camera.main != null)
            {
                return Camera.main.transform.position;
            }

            return Vector3.zero;
        }

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

        private void SetStatus(string message)
        {
            Debug.Log($"RandomRoomBarrelSpawner: {message}");
            if (m_statusLabel != null)
            {
                m_statusLabel.text = message;
            }
        }

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
