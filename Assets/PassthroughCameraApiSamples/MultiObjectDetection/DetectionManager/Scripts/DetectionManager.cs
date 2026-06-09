// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections;
using System.Collections.Generic;
using Meta.XR;
using Meta.XR.Samples;
using SimJam.BarrelSimulator;
using UnityEngine;
using UnityEngine.Events;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    [MetaCodeSample("PassthroughCameraApiSamples-MultiObjectDetection")]
    public class DetectionManager : MonoBehaviour
    {
        [SerializeField] private PassthroughCameraAccess m_cameraAccess;

        [Header("Placement configuration")]
        [SerializeField] private DetectionSpawnMarkerAnim m_spawnMarker;

        [SerializeField] private DetectionUiMenuManager m_uiMenuManager;
        [SerializeField] private SentisInferenceUiManager m_uiInference;
        [Space(10)]
        public UnityEvent<int> OnObjectsIdentified;

        [Header("SimJam barrel simulator")]
        [SerializeField] private GameObject m_barrelPrefab;
        [SerializeField] private RadiationCountProfile m_radiationCountProfile;
        [SerializeField] private string m_targetClassName = "chair";
        [SerializeField] private float m_barrelVerticalOffset = 0.35f;
        [SerializeField] private Vector3 m_customBarrelSpawnScale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        [SerializeField, Range(0.01f, 0.5f)] private float m_runtimeScaleStep = 0.15f;
        [SerializeField] private Vector2 m_runtimeScaleLimits = new Vector2(0.03f, 3f);
        [SerializeField] private Vector3 m_placeholderBarrelScale = new Vector3(0.45f, 0.45f, 0.45f);
        [SerializeField] private bool m_showDebugCountLabels = true;
        [SerializeField, Min(1)] private int m_fallbackCountPoolSize = 2048;
        [SerializeField, Min(0)] private int m_fallbackMinCount = 250;
        [SerializeField, Min(0)] private int m_fallbackMaxCount = 50000;

        private readonly List<DetectionSpawnMarkerAnim> m_spawnedEntities = new();
        private readonly List<BarrelInstance> m_spawnedBarrels = new();
        private int[] m_fallbackCountPool;
        private bool m_isStarted;
        internal OVRSpatialAnchor m_spatialAnchor;
        private bool m_isHeadsetTracking;

        private void Awake()
        {
            if (m_uiMenuManager == null)
            {
                m_uiMenuManager = FindFirstObjectByType<DetectionUiMenuManager>();
            }

            StartCoroutine(UpdateSpatialAnchor());
            OVRManager.TrackingLost += OnTrackingLost;
            OVRManager.TrackingAcquired += OnTrackingAcquired;
        }

        private void OnDestroy()
        {
            EraseSpatialAnchor();
            OVRManager.TrackingLost -= OnTrackingLost;
            OVRManager.TrackingAcquired -= OnTrackingAcquired;
        }

        private void OnTrackingLost() => m_isHeadsetTracking = false;
        private void OnTrackingAcquired() => m_isHeadsetTracking = true;

        private void Update()
        {
            if (!m_isStarted)
            {
                // Manage the Initial Ui Menu
                if (m_cameraAccess.IsPlaying)
                {
                    m_isStarted = true;
                }
            }
            else
            {
                // Press A button to spawn 3d markers
                if (InputManager.IsButtonADownOrPinchStarted())
                {
                    SpawnCurrentDetectedObjects();
                }
            }

            // Press B button to clean all markers
            if (InputManager.IsButtonBDownOrMiddleFingerPinchStarted())
            {
                CleanMarkers();
            }

            // Press X to assign new random count values to the spawned barrels
            if (InputManager.IsButtonXDown())
            {
                RandomizeRadiationCounts();
            }

            // Press Y to pause/resume the continuous detector.
            if (InputManager.IsButtonYDown() && m_uiMenuManager != null)
            {
                m_uiMenuManager.ToggleScanPaused();
            }

            // Right thumbstick up/down adjusts current and future barrel size while testing in-headset.
            if (InputManager.IsRightThumbstickUpDown())
            {
                AdjustRuntimeBarrelScale(1f + m_runtimeScaleStep);
            }
            else if (InputManager.IsRightThumbstickDownDown())
            {
                AdjustRuntimeBarrelScale(1f - m_runtimeScaleStep);
            }
        }

        private IEnumerator UpdateSpatialAnchor()
        {
            while (true)
            {
                yield return null;
                if (m_spatialAnchor == null)
                {
                    yield return CreateSpatialAnchorAndSave();
                    if (m_spatialAnchor == null)
                    {
                        continue;
                    }
                }

                if (!m_spatialAnchor.IsTracked)
                {
                    yield return RestoreSpatialAnchorTracking();
                }
            }

            IEnumerator CreateSpatialAnchorAndSave()
            {
                m_spatialAnchor = m_uiInference.ContentParent.gameObject.AddComponent<OVRSpatialAnchor>();

                // Wait for localization because SaveAnchorAsync() requires the anchor to be localized first.
                while (true)
                {
                    if (m_spatialAnchor == null)
                    {
                        // Spatial Anchor destroys itself when creation fails.
                        yield break;
                    }
                    if (m_spatialAnchor.Localized)
                    {
                        break;
                    }
                    yield return null;
                }

                // Save the anchor.
                var awaiter = m_spatialAnchor.SaveAnchorAsync().GetAwaiter();
                while (!awaiter.IsCompleted)
                {
                    yield return null;
                }
                var saveAnchorResult = awaiter.GetResult();
                if (!saveAnchorResult.Success)
                {
                    LogSpatialAnchor($"SaveAnchorAsync() failed {saveAnchorResult}", LogType.Error);
                    EraseSpatialAnchor();
                    yield break;
                }
                LogSpatialAnchor("created");
            }

            IEnumerator RestoreSpatialAnchorTracking()
            {
                // Try to restore spatial anchor tracking. If restoration fails, erase it.
                LogSpatialAnchor("tracking was lost, restoring...");
                const int numRetries = 20;
                for (int i = 0; i < numRetries; i++)
                {
                    yield return new WaitForSeconds(1f);
                    if (!m_isHeadsetTracking)
                    {
                        LogSpatialAnchor($"{nameof(m_isHeadsetTracking)} is false, retrying ({i})");
                        continue;
                    }

                    var unboundAnchors = new List<OVRSpatialAnchor.UnboundAnchor>(1);
                    var awaiter = OVRSpatialAnchor.LoadUnboundAnchorsAsync(new[]
                    {
                        m_spatialAnchor.Uuid
                    }, unboundAnchors).GetAwaiter();
                    while (!awaiter.IsCompleted)
                    {
                        yield return null;
                    }
                    var loadResult = awaiter.GetResult();
                    if (!loadResult.Success)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() failed {loadResult.Status}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    if (unboundAnchors.Count != 0)
                    {
                        LogSpatialAnchor($"LoadUnboundAnchorsAsync() unexpected count:{unboundAnchors.Count}, retrying ({i})", LogType.Error);
                        continue;
                    }
                    yield return null;
                    if (!m_spatialAnchor.IsTracked)
                    {
                        LogSpatialAnchor($"tracking is not restored, retrying ({i})");
                        continue;
                    }

                    LogSpatialAnchor("tracking was restored successfully");
                    yield break;
                }

                LogSpatialAnchor($"tracking restoration failed after {numRetries} retries", LogType.Warning);
                EraseSpatialAnchor();
            }
        }

        private void EraseSpatialAnchor()
        {
            if (m_spatialAnchor != null)
            {
                LogSpatialAnchor("EraseSpatialAnchor");
                m_spatialAnchor.EraseAnchorAsync();
                DestroyImmediate(m_spatialAnchor);
                m_spatialAnchor = null;

                CleanMarkers();
                m_uiInference.ClearAnnotations();
            }
        }

        private void CleanMarkers()
        {
            LogSpatialAnchor("Clean spawned barrels");
            foreach (var e in m_spawnedEntities)
            {
                Destroy(e.gameObject);
            }
            m_spawnedEntities.Clear();

            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel != null)
                {
                    Destroy(barrel.gameObject);
                }
            }
            m_spawnedBarrels.Clear();
            OnObjectsIdentified?.Invoke(-1);
        }

        private static void LogSpatialAnchor(string message, LogType logType = LogType.Log)
        {
            Debug.unityLogger.Log(logType, $"{nameof(OVRSpatialAnchor)}: {message}");
        }

        /// <summary>
        /// Spawn radioactive barrel overlays for the currently detected target objects.
        /// </summary>
        private void SpawnCurrentDetectedObjects()
        {
            var newCount = 0;
            foreach (SentisInferenceUiManager.BoundingBoxData box in m_uiInference.m_boxDrawn)
            {
                if (!IsTargetClass(box.ClassName))
                {
                    continue;
                }

                if (!HasExistingBarrelInBoundingBox(box))
                {
                    LogSpatialAnchor($"spawn radioactive barrel from {box.ClassName}");
                    var barrel = SpawnBarrel(box);
                    m_spawnedBarrels.Add(barrel);
                    newCount++;
                }
            }
            OnObjectsIdentified?.Invoke(newCount);

            bool HasExistingBarrelInBoundingBox(SentisInferenceUiManager.BoundingBoxData box)
            {
                foreach (var barrel in m_spawnedBarrels)
                {
                    if (barrel == null || barrel.SourceLabel != box.ClassName)
                    {
                        continue;
                    }

                    var barrelWorldPos = barrel.transform.position;
                    Vector2 localPos = box.BoxRectTransform.InverseTransformPoint(barrelWorldPos);
                    var sizeDelta = box.BoxRectTransform.sizeDelta;
                    var currentBox = new Rect(
                        -sizeDelta.x * 0.5f,
                        -sizeDelta.y * 0.5f,
                        sizeDelta.x,
                        sizeDelta.y
                    );

                    if (currentBox.Contains(localPos))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void RandomizeRadiationCounts()
        {
            if (m_spawnedBarrels.Count == 0)
            {
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
        }

        private void AdjustRuntimeBarrelScale(float multiplier)
        {
            var nextScale = m_customBarrelSpawnScale * multiplier;
            nextScale.x = Mathf.Clamp(nextScale.x, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            nextScale.y = Mathf.Clamp(nextScale.y, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            nextScale.z = Mathf.Clamp(nextScale.z, m_runtimeScaleLimits.x, m_runtimeScaleLimits.y);
            m_customBarrelSpawnScale = nextScale;

            foreach (var barrel in m_spawnedBarrels)
            {
                if (barrel != null)
                {
                    barrel.transform.localScale = m_customBarrelSpawnScale;
                }
            }

            Debug.Log($"Runtime barrel spawn scale set to {m_customBarrelSpawnScale}");
        }

        private BarrelInstance SpawnBarrel(SentisInferenceUiManager.BoundingBoxData box)
        {
            var position = box.BoxRectTransform.position + Vector3.up * m_barrelVerticalOffset;
            var rotation = GetUprightRotation(box.BoxRectTransform.forward);
            GameObject barrelObject;
            if (m_barrelPrefab != null)
            {
                barrelObject = Instantiate(m_barrelPrefab, position, rotation, m_uiInference.ContentParent);
                barrelObject.transform.localScale = m_customBarrelSpawnScale;
            }
            else
            {
                barrelObject = CreatePlaceholderBarrel(position, rotation, m_uiInference.ContentParent);
            }

            var barrel = barrelObject.GetComponent<BarrelInstance>();
            if (barrel == null)
            {
                barrel = barrelObject.AddComponent<BarrelInstance>();
            }

            barrel.Initialize(box.ClassName, GetRandomRadiationCount(), m_showDebugCountLabels);
            return barrel;
        }

        private GameObject CreatePlaceholderBarrel(Vector3 position, Quaternion rotation, Transform parent)
        {
            var root = new GameObject("Placeholder Radioactive Barrel");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, rotation);

            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = "Barrel Body";
            cylinder.transform.SetParent(root.transform, false);
            cylinder.transform.localScale = m_placeholderBarrelScale;

            var renderer = cylinder.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = new Color(1f, 0.86f, 0.12f);
            }

            return root;
        }

        private int GetRandomRadiationCount()
        {
            if (m_radiationCountProfile != null)
            {
                return m_radiationCountProfile.GetRandomCount();
            }

            EnsureFallbackCountPool();
            return m_fallbackCountPool[Random.Range(0, m_fallbackCountPool.Length)];
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
                m_fallbackCountPool[i] = Random.Range(safeMin, safeMax + 1);
            }
        }

        private bool IsTargetClass(string className)
        {
            return string.Equals(NormalizeClassName(className), NormalizeClassName(m_targetClassName), System.StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeClassName(string className)
        {
            return string.IsNullOrWhiteSpace(className) ? string.Empty : className.Trim().Replace(" ", "_");
        }

        private static Quaternion GetUprightRotation(Vector3 forward)
        {
            var flatForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                flatForward = Vector3.forward;
            }

            return Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        }
    }
}
