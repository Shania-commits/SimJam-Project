using SimJam.BarrelSimulator;
using UnityEngine;

namespace SimJam.Tutorial
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("SimJam/Tutorial Room Dresser")]
    public class TutorialRoomDresser : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private GameObject m_barrel55Prefab;
        [SerializeField] private GameObject m_barrel30Prefab;
        [SerializeField] private GameObject m_barrel5Prefab;

        [Header("Barrel materials (assign the lab's authored barrel materials; empty = procedural fallback)")]
        [SerializeField] private Material m_barrelLargeMetal;
        [SerializeField] private Material m_barrelLargePaint;

        [Header("Generation")]
        [SerializeField] private bool m_buildInEditor = true;
        [SerializeField] private bool m_buildAtRuntime = true;

        private const string GeneratedRootName = "Tutorial Room Dressing (Generated)";

        private Material m_tableMaterial;
        private Material m_metalMaterial;
        private Material m_darkMetalMaterial;
        private Material m_crateMaterial;
        private Material m_orangeMaterial;
        private Material m_blackMaterial;
        private Material m_scuffMaterial;
        private Material m_dustMaterial;
        private Material m_floorPatchMaterial;
        private Material m_whiteboardMaterial;
        private Material m_radiationPosterMaterial;
        private Material m_labelMaterial;
        private Material m_blueMaterial;
        private Material m_barrelBlackMaterial;
        private Material m_barrelGrayMaterial;

        // Match the lab's barrel model scale (RadiationLabRoom m_barrel55/30ModelScale) so tutorial
        // barrels are the same size as the lab. Only the height (Y) differed — they were too tall.
        private static readonly Vector3 FloorBarrel55Scale = new Vector3(0.19567f, 0.1853504f, 0.19567f);
        private static readonly Vector3 FloorBarrel30Scale = new Vector3(0.14f, 0.14f, 0.14f);
        private static readonly Vector3 ShelfSmallBarrelScale = new Vector3(0.12f, 0.12f, 0.12f);
        // Floor spot for the detector podium; must match TutorialRoomInteractionController.m_detectorHomePosition X/Z.
        private static readonly Vector3 DetectorPodiumFloor = new Vector3(-2.25f, 0f, -1.5f);

        private void Awake()
        {
            RebuildIfAllowed();
        }

        private void OnEnable()
        {
            RebuildIfAllowed();
        }

        private void Start()
        {
            RebuildIfAllowed();
        }

        private void RebuildIfAllowed()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (Application.isPlaying && !m_buildAtRuntime)
            {
                return;
            }

            if (!Application.isPlaying && !m_buildInEditor)
            {
                return;
            }

            BuildRoomDressing();
        }

        [ContextMenu("Rebuild Tutorial Dressing")]
        public void BuildRoomDressing()
        {
            HideLegacySceneProps();
            ClearGeneratedRoot();
            CreateMaterials();

            var root = new GameObject(GeneratedRootName);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;

            BuildAisle(root.transform);
            BuildEnvironmentalWear(root.transform);
            BuildTrainingTables(root.transform);
            BuildDetectorPodium(root.transform);
            BuildBackWallShelving(root.transform);
            BuildClutter(root.transform);
            BuildPostersAndWallMarks(root.transform);
            BuildLighting(root.transform);
        }

        private void ClearGeneratedRoot()
        {
            var existing = transform.Find(GeneratedRootName);
            if (existing == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(existing.gameObject);
            }
            else
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        private void HideLegacySceneProps()
        {
            foreach (var sceneTransform in Object.FindObjectsByType<Transform>())
            {
                if (sceneTransform == null || sceneTransform == transform || sceneTransform.IsChildOf(transform))
                {
                    continue;
                }

                var objectName = sceneTransform.gameObject.name;
                // Disable any legacy/baked barrel left in the scene (the dresser no longer spawns
                // barrels). The runtime teaching barrels are named "...Drum..." so they are immune.
                if (objectName.Contains("Barrel") || objectName == "shelf")
                {
                    sceneTransform.gameObject.SetActive(false);
                }
            }
        }

        private void CreateMaterials()
        {
            m_tableMaterial = CreateMaterial("Tutorial Table Plastic", new Color(0.82f, 0.82f, 0.78f), 0f, 0.34f, null);
            m_metalMaterial = CreateMaterial("Tutorial Galvanized Metal", new Color(0.48f, 0.50f, 0.52f), 0.55f, 0.42f, null);
            m_darkMetalMaterial = CreateMaterial("Tutorial Dark Metal", new Color(0.13f, 0.15f, 0.16f), 0.45f, 0.35f, null);
            m_crateMaterial = CreateMaterial("Tutorial Crate Plastic", new Color(0.16f, 0.24f, 0.20f), 0f, 0.45f, null);
            m_orangeMaterial = CreateMaterial("Tutorial Safety Orange", new Color(1f, 0.35f, 0.05f), 0f, 0.25f, null);
            m_blackMaterial = CreateMaterial("Tutorial Rubber Black", new Color(0.035f, 0.035f, 0.035f), 0f, 0.25f, null);
            m_scuffMaterial = CreateMaterial("Tutorial Scuff Marks", new Color(0.09f, 0.095f, 0.085f), 0f, 0.65f, null);
            m_dustMaterial = CreateMaterial("Tutorial Dusty Concrete", new Color(0.34f, 0.35f, 0.32f), 0f, 0.8f, null);
            m_floorPatchMaterial = CreateMaterial("Tutorial Concrete Floor Patch", new Color(0.47f, 0.49f, 0.45f), 0f, 0.18f, SimJam.BarrelSimulator.ProceduralTextureLibrary.Concrete512);
            m_whiteboardMaterial = CreateMaterial("Tutorial Whiteboard", new Color(0.90f, 0.94f, 0.92f), 0.2f, 0.55f, null);
            m_radiationPosterMaterial = CreateMaterial("Tutorial Radiation Poster", Color.white, 0f, 0.35f, SimJam.BarrelSimulator.ProceduralTextureLibrary.RadiationPoster256);
            m_labelMaterial = CreateMaterial("Tutorial Label Paper", new Color(0.92f, 0.90f, 0.76f), 0f, 0.32f, null);
            m_blueMaterial = CreateMaterial("Tutorial Blue Stencil", new Color(0.05f, 0.36f, 0.58f), 0f, 0.25f, null);
            // Prefer the lab's authored PBR barrel materials so tutorial barrels match the lab;
            // fall back to the procedural look only if the material slots are left empty.
            m_barrelBlackMaterial = m_barrelLargeMetal != null
                ? m_barrelLargeMetal
                : CreateMaterial("Tutorial Black Barrel", new Color(0.12f, 0.13f, 0.13f), 0.25f, 0.35f, null);
            m_barrelGrayMaterial = m_barrelLargePaint != null
                ? m_barrelLargePaint
                : CreateMaterial("Tutorial White Barrel", new Color(0.78f, 0.80f, 0.78f), 0.35f, 0.4f, null);
        }

        private static Material CreateMaterial(string materialName, Color color, float metallic, float smoothness, Texture2D texture)
        {
            var shader = Shader.Find("Standard");
            var material = new Material(shader) { name = materialName, color = color };
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", smoothness);
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            return material;
        }

        private void BuildAisle(Transform root)
        {
            CreateCube(root, "Covered Caution Tape South", new Vector3(0f, 0.012f, -1.98f), new Vector3(7.15f, 0.01f, 0.28f), m_floorPatchMaterial, false);
            CreateCube(root, "Covered Caution Tape Center", new Vector3(0f, 0.013f, -0.90f), new Vector3(7.15f, 0.01f, 0.28f), m_floorPatchMaterial, false);
        }

        private void BuildEnvironmentalWear(Transform root)
        {
            CreateCube(root, "Dust Patch Near Shelf", new Vector3(-2.72f, 0.021f, 2.55f), new Vector3(0.82f, 0.006f, 0.34f), m_dustMaterial, false, Quaternion.Euler(0f, 16f, 0f));
            CreateCube(root, "Dust Patch Near Table", new Vector3(-1.58f, 0.022f, -1.72f), new Vector3(0.58f, 0.006f, 0.22f), m_dustMaterial, false, Quaternion.Euler(0f, -12f, 0f));
            CreateCube(root, "Floor Scuff Left", new Vector3(-0.38f, 0.026f, 0.86f), new Vector3(0.72f, 0.005f, 0.035f), m_scuffMaterial, false, Quaternion.Euler(0f, 28f, 0f));
            CreateCube(root, "Floor Scuff Right", new Vector3(1.78f, 0.026f, -0.72f), new Vector3(0.54f, 0.005f, 0.03f), m_scuffMaterial, false, Quaternion.Euler(0f, -34f, 0f));
            CreateCube(root, "Floor Scrape Back", new Vector3(2.48f, 0.026f, 1.86f), new Vector3(0.42f, 0.005f, 0.026f), m_scuffMaterial, false, Quaternion.Euler(0f, 9f, 0f));
            CreateCube(root, "Left Wall Scuff", new Vector3(-3.88f, 0.86f, -0.42f), new Vector3(0.018f, 0.045f, 0.42f), m_scuffMaterial, false);
            CreateCube(root, "Back Wall Low Smudge", new Vector3(-1.72f, 0.72f, 3.875f), new Vector3(0.48f, 0.045f, 0.018f), m_scuffMaterial, false);
        }

        private void BuildTrainingTables(Transform root)
        {
            var spawnTable = BuildUtilityTable(root, "Spawn-Side Hold-Up Table", new Vector3(-1.95f, 0f, -2.35f), Quaternion.Euler(0f, 8f, 0f), new Vector2(1.7f, 0.74f));
            BuildClipboard(root, "Spawn Table Clipboard", spawnTable.TransformPoint(new Vector3(0.54f, 0.86f, -0.22f)), spawnTable.rotation * Quaternion.Euler(0f, 18f, 0f));
            // Detector now spawns on a floor podium (BuildDetectorPodium), not a table dock.
        }

        private void BuildDetectorPodium(Transform root)
        {
            // Reuse the lab's concrete pedestal (top at PedestalTopHeight = 1.02 m, colliders enabled) so
            // the identiFINDER always spawns on a proper podium. Adapt the dresser's CreateMaterial to
            // RoomDecorator's 6-arg MaterialFactory (emission unused here).
            RoomDecorator.MaterialFactory adapt = (color, name, metallic, smoothness, albedo, emission)
                => CreateMaterial(name, color, metallic, smoothness, albedo);
            RoomDecorator.BuildDetectorPedestal(root, DetectorPodiumFloor, adapt);
        }

        private void BuildClipboard(Transform parent, string objectName, Vector3 position, Quaternion rotation)
        {
            var clipboard = new GameObject(objectName);
            clipboard.transform.SetParent(parent, false);
            clipboard.transform.SetPositionAndRotation(position, rotation);

            CreateCube(clipboard.transform, "Clipboard Board", Vector3.zero, new Vector3(0.30f, 0.018f, 0.40f), m_crateMaterial, false);
            CreateCube(clipboard.transform, "Checklist Paper", new Vector3(0f, 0.012f, 0.025f), new Vector3(0.24f, 0.008f, 0.30f), m_labelMaterial, false);
            CreateCube(clipboard.transform, "Metal Clip", new Vector3(0f, 0.024f, 0.17f), new Vector3(0.14f, 0.014f, 0.045f), m_metalMaterial, false);
            CreateCube(clipboard.transform, "Checklist Line 1", new Vector3(0.015f, 0.030f, 0.075f), new Vector3(0.17f, 0.006f, 0.01f), m_darkMetalMaterial, false);
            CreateCube(clipboard.transform, "Checklist Line 2", new Vector3(0.015f, 0.031f, 0.018f), new Vector3(0.17f, 0.006f, 0.01f), m_darkMetalMaterial, false);
            CreateCube(clipboard.transform, "Checklist Line 3", new Vector3(0.015f, 0.032f, -0.039f), new Vector3(0.17f, 0.006f, 0.01f), m_darkMetalMaterial, false);
        }

        private Transform BuildUtilityTable(Transform root, string objectName, Vector3 position, Quaternion rotation, Vector2 size)
        {
            var tableRoot = new GameObject(objectName);
            tableRoot.transform.SetParent(root, false);
            tableRoot.transform.SetPositionAndRotation(position, rotation);

            CreateCube(tableRoot.transform, "Table Top", new Vector3(0f, 0.76f, 0f), new Vector3(size.x, 0.07f, size.y), m_tableMaterial, true);
            CreateCube(tableRoot.transform, "Back Rail", new Vector3(0f, 0.82f, size.y * 0.5f - 0.03f), new Vector3(size.x, 0.06f, 0.045f), m_metalMaterial, false);

            var legX = size.x * 0.42f;
            var legZ = size.y * 0.38f;
            CreateCube(tableRoot.transform, "Leg FL", new Vector3(-legX, 0.38f, -legZ), new Vector3(0.045f, 0.76f, 0.045f), m_metalMaterial, true);
            CreateCube(tableRoot.transform, "Leg FR", new Vector3(legX, 0.38f, -legZ), new Vector3(0.045f, 0.76f, 0.045f), m_metalMaterial, true);
            CreateCube(tableRoot.transform, "Leg BL", new Vector3(-legX, 0.38f, legZ), new Vector3(0.045f, 0.76f, 0.045f), m_metalMaterial, true);
            CreateCube(tableRoot.transform, "Leg BR", new Vector3(legX, 0.38f, legZ), new Vector3(0.045f, 0.76f, 0.045f), m_metalMaterial, true);

            return tableRoot.transform;
        }

        private void BuildBackWallShelving(Transform root)
        {
            var shelfRoot = new GameObject("Back Wall Shelving With Barrels");
            shelfRoot.transform.SetParent(root, false);
            shelfRoot.transform.localPosition = new Vector3(0f, 0f, 3.42f);
            shelfRoot.transform.localRotation = Quaternion.identity;

            const float width = 5.35f;
            const float depth = 0.58f;
            float[] shelfHeights = { 0.42f, 1.02f, 1.62f };

            CreateCube(shelfRoot.transform, "Left Upright", new Vector3(-width * 0.5f, 0.98f, 0f), new Vector3(0.075f, 1.95f, depth), m_darkMetalMaterial, true);
            CreateCube(shelfRoot.transform, "Center Upright", new Vector3(0f, 0.98f, 0f), new Vector3(0.06f, 1.95f, depth), m_darkMetalMaterial, true);
            CreateCube(shelfRoot.transform, "Right Upright", new Vector3(width * 0.5f, 0.98f, 0f), new Vector3(0.075f, 1.95f, depth), m_darkMetalMaterial, true);

            for (var i = 0; i < shelfHeights.Length; i++)
            {
                CreateCube(shelfRoot.transform, $"Shelf Deck {i + 1}", new Vector3(0f, shelfHeights[i], 0f), new Vector3(width, 0.07f, depth), m_metalMaterial, true);
            }

            // Shelf barrels removed: the tutorial now teaches with just two purpose-built barrels
            // (TutorialRoomInteractionController.SpawnTeachingBarrels). The shelving/bins/clutter stay.

            CreateStorageBin(shelfRoot.transform, "Back Shelf Bin A", new Vector3(-1.05f, 0.52f, -0.05f), new Vector3(0.52f, 0.20f, 0.34f));
            CreateStorageBin(shelfRoot.transform, "Back Shelf Bin B", new Vector3(0.78f, 0.52f, -0.05f), new Vector3(0.58f, 0.20f, 0.34f));
            CreateStorageBin(shelfRoot.transform, "Back Shelf Bin C", new Vector3(2.05f, 1.12f, -0.05f), new Vector3(0.44f, 0.18f, 0.32f));
            BuildShelfClutter(shelfRoot.transform);
        }

        private void BuildShelfClutter(Transform shelfRoot)
        {
            CreateCube(shelfRoot, "Shelf Binder Blue", new Vector3(-2.20f, 1.15f, -0.11f), new Vector3(0.11f, 0.36f, 0.26f), m_blueMaterial, false, Quaternion.Euler(0f, 0f, -3f));
            CreateCube(shelfRoot, "Shelf Binder Dark", new Vector3(-2.05f, 1.14f, -0.11f), new Vector3(0.10f, 0.34f, 0.26f), m_darkMetalMaterial, false, Quaternion.Euler(0f, 0f, 2f));
            CreateCube(shelfRoot, "Shelf Binder White", new Vector3(-1.90f, 1.13f, -0.11f), new Vector3(0.09f, 0.32f, 0.26f), m_labelMaterial, false);
            CreateCube(shelfRoot, "Sealed Sample Box A", new Vector3(1.56f, 0.55f, -0.04f), new Vector3(0.32f, 0.16f, 0.28f), m_crateMaterial, false);
            CreateCube(shelfRoot, "Sealed Sample Box Label A", new Vector3(1.56f, 0.56f, -0.19f), new Vector3(0.18f, 0.06f, 0.018f), m_labelMaterial, false);
            CreateCube(shelfRoot, "Flat Glove Box", new Vector3(-1.12f, 1.73f, -0.04f), new Vector3(0.48f, 0.12f, 0.30f), m_labelMaterial, false);
        }

        private void BuildClutter(Transform root)
        {
            CreateStorageBin(root, "Left Floor Storage Bin", new Vector3(-3.25f, 0.15f, -2.25f), new Vector3(0.58f, 0.28f, 0.44f));
            CreateStorageBin(root, "Left Floor Storage Bin Stacked", new Vector3(-3.18f, 0.46f, -2.25f), new Vector3(0.50f, 0.26f, 0.40f));
            CreateStorageBin(root, "Right Floor Equipment Case", new Vector3(3.13f, 0.14f, -1.85f), new Vector3(0.68f, 0.24f, 0.40f));
            CreateStorageBin(root, "Right Wall Sample Bin", new Vector3(3.12f, 0.15f, 0.38f), new Vector3(0.52f, 0.28f, 0.38f));
            CreateStorageBin(root, "Back Corner Tape Bin", new Vector3(-3.18f, 0.14f, 2.68f), new Vector3(0.50f, 0.24f, 0.36f));

            // Floor barrel clusters removed: the tutorial teaches with two purpose-built barrels only.

        }

        private enum BarrelClusterLayout
        {
            WallCluster,
            CornerCluster,
            OpenFloorCluster
        }

        private void BuildFloorBarrelCluster(Transform parent, string objectName, Vector3 position, Quaternion rotation, BarrelClusterLayout layout)
        {
            var clusterRoot = new GameObject(objectName);
            clusterRoot.transform.SetParent(parent, false);
            clusterRoot.transform.SetPositionAndRotation(position, rotation);

            switch (layout)
            {
                case BarrelClusterLayout.CornerCluster:
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel A", new Vector3(-0.16f, 0f, -0.10f), Quaternion.Euler(0f, -8f, 0f), FloorBarrel55Scale, m_barrelBlackMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel B", new Vector3(0.18f, 0f, 0.02f), Quaternion.Euler(0f, 16f, 0f), FloorBarrel55Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel A", new Vector3(0.05f, 0f, 0.28f), Quaternion.Euler(0f, -14f, 0f), FloorBarrel30Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel B", new Vector3(-0.34f, 0f, 0.14f), Quaternion.Euler(0f, 24f, 0f), FloorBarrel30Scale, m_barrelBlackMaterial, 0f);
                    break;

                case BarrelClusterLayout.OpenFloorCluster:
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel A", new Vector3(-0.28f, 0f, -0.08f), Quaternion.Euler(0f, -22f, 0f), FloorBarrel55Scale, m_barrelBlackMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel B", new Vector3(0.02f, 0f, 0.00f), Quaternion.Euler(0f, 6f, 0f), FloorBarrel55Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel C", new Vector3(0.28f, 0f, 0.12f), Quaternion.Euler(0f, 18f, 0f), FloorBarrel55Scale, m_barrelBlackMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel A", new Vector3(-0.12f, 0f, 0.32f), Quaternion.Euler(0f, 34f, 0f), FloorBarrel30Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel B", new Vector3(0.22f, 0f, -0.26f), Quaternion.Euler(0f, -30f, 0f), FloorBarrel30Scale, m_barrelGrayMaterial, 0f);
                    break;

                default:
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel A", new Vector3(-0.30f, 0f, -0.08f), Quaternion.Euler(0f, -8f, 0f), FloorBarrel55Scale, m_barrelBlackMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel B", new Vector3(-0.04f, 0f, 0.08f), Quaternion.Euler(0f, 14f, 0f), FloorBarrel55Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel55Prefab, "55 Gallon Floor Barrel C", new Vector3(0.26f, 0f, -0.06f), Quaternion.Euler(0f, -18f, 0f), FloorBarrel55Scale, m_barrelBlackMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel A", new Vector3(-0.22f, 0f, 0.30f), Quaternion.Euler(0f, 22f, 0f), FloorBarrel30Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel B", new Vector3(0.12f, 0f, 0.32f), Quaternion.Euler(0f, -28f, 0f), FloorBarrel30Scale, m_barrelGrayMaterial, 0f);
                    SpawnAssetBarrel(clusterRoot.transform, m_barrel30Prefab, "30 Gallon Floor Barrel C", new Vector3(0.38f, 0f, 0.12f), Quaternion.Euler(0f, 38f, 0f), FloorBarrel30Scale, m_barrelBlackMaterial, 0f);
                    break;
            }
        }

        private void SpawnAssetBarrel(Transform parent, GameObject prefab, string objectName, Vector3 position, Quaternion rotation, Vector3 scale, Material material, float surfaceY)
        {
            if (prefab == null)
            {
                return;
            }

            var barrel = Instantiate(prefab, parent);
            barrel.name = objectName;
            barrel.transform.localPosition = position;
            barrel.transform.localRotation = rotation;
            barrel.transform.localScale = scale;
            AssignMaterialToRenderers(barrel, material);
            AlignRendererBottomToSurface(barrel, surfaceY);
        }

        private void BuildPostersAndWallMarks(Transform root)
        {
            CreateCube(root, "Left Wall Radiation Poster", new Vector3(-3.895f, 1.70f, 1.45f), new Vector3(0.025f, 0.72f, 0.54f), m_radiationPosterMaterial, false);
            BuildWhiteboard(root);
            BuildWallClock(root);
        }

        private void BuildLighting(Transform root)
        {
            CreatePointLight(root, "Tutorial Shelf Accent Light", new Vector3(0f, 2.45f, 2.4f), new Color(0.75f, 0.9f, 1f), 0.65f, 3.6f);
            CreatePointLight(root, "Tutorial Table Task Light", new Vector3(-1.9f, 2.1f, -2.35f), new Color(1f, 0.92f, 0.78f), 0.85f, 2.8f);
            CreatePointLight(root, "Tutorial Barrel Cluster Fill Light", new Vector3(1.6f, 2.35f, 0.85f), new Color(0.86f, 0.92f, 1f), 0.50f, 4.2f);
        }

        private void BuildWhiteboard(Transform root)
        {
            CreateCube(root, "Left Wall Whiteboard", new Vector3(-3.875f, 1.74f, -0.35f), new Vector3(0.025f, 0.72f, 1.35f), m_whiteboardMaterial, false);
            CreateCube(root, "Whiteboard Top Rail", new Vector3(-3.855f, 2.115f, -0.35f), new Vector3(0.035f, 0.035f, 1.42f), m_metalMaterial, false);
            CreateCube(root, "Whiteboard Bottom Rail", new Vector3(-3.855f, 1.365f, -0.35f), new Vector3(0.035f, 0.035f, 1.42f), m_metalMaterial, false);
            CreateCube(root, "Whiteboard Left Rail", new Vector3(-3.855f, 1.74f, -1.045f), new Vector3(0.035f, 0.76f, 0.035f), m_metalMaterial, false);
            CreateCube(root, "Whiteboard Right Rail", new Vector3(-3.855f, 1.74f, 0.345f), new Vector3(0.035f, 0.76f, 0.035f), m_metalMaterial, false);
            CreateCube(root, "Whiteboard Marker Line 1", new Vector3(-3.842f, 1.88f, -0.50f), new Vector3(0.012f, 0.018f, 0.62f), m_blueMaterial, false);
            CreateCube(root, "Whiteboard Marker Line 2", new Vector3(-3.842f, 1.74f, -0.42f), new Vector3(0.012f, 0.018f, 0.78f), m_darkMetalMaterial, false);
            CreateCube(root, "Whiteboard Marker Line 3", new Vector3(-3.842f, 1.60f, -0.57f), new Vector3(0.012f, 0.018f, 0.48f), m_darkMetalMaterial, false);
            CreateCube(root, "Whiteboard Eraser", new Vector3(-3.82f, 1.39f, 0.12f), new Vector3(0.05f, 0.055f, 0.24f), m_darkMetalMaterial, false);
        }

        private void BuildWallClock(Transform root)
        {
            var clock = new GameObject("Spawn Wall Clock");
            clock.transform.SetParent(root, false);
            clock.transform.localPosition = new Vector3(-3.50f, 2f, -3.86f);
            clock.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            CreateCylinder(clock.transform, "Clock Rim", new Vector3(0f, 0.006f, 0f), new Vector3(0.235f, 0.012f, 0.235f), m_darkMetalMaterial, false);
            CreateCylinder(clock.transform, "Clock Face", new Vector3(0f, -0.006f, 0f), new Vector3(0.22f, 0.018f, 0.22f), m_labelMaterial, false);
            CreateCube(clock.transform, "Hour Hand", new Vector3(0.035f, -0.018f, 0f), new Vector3(0.09f, 0.008f, 0.012f), m_blackMaterial, false, Quaternion.Euler(0f, 0f, -18f));
            CreateCube(clock.transform, "Minute Hand", new Vector3(-0.012f, -0.020f, 0.045f), new Vector3(0.012f, 0.008f, 0.14f), m_blackMaterial, false, Quaternion.Euler(0f, 0f, 12f));
            CreateCylinder(clock.transform, "Clock Center Pin", new Vector3(0f, -0.022f, 0f), new Vector3(0.025f, 0.006f, 0.025f), m_blackMaterial, false);
        }

        private void CreatePointLight(Transform parent, string objectName, Vector3 localPosition, Color color, float intensity, float range)
        {
            var lightObject = new GameObject(objectName);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localPosition = localPosition;
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.renderMode = LightRenderMode.ForceVertex;
            light.shadows = LightShadows.None;
        }

        private void CreateStorageBin(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale)
        {
            var bin = CreateCube(parent, objectName, localPosition, localScale, m_crateMaterial, true);
            CreateCube(bin.transform, "Bin Lid", new Vector3(0f, 0.56f, 0f), new Vector3(1.05f, 0.08f, 1.06f), m_darkMetalMaterial, false);
            CreateCube(bin.transform, "Front Label", new Vector3(0f, 0.05f, -0.515f), new Vector3(0.46f, 0.28f, 0.025f), m_labelMaterial, false);
        }

        private void SpawnBarrel(Transform parent, GameObject prefab, string objectName, Vector3 position, Quaternion rotation, Vector3 scale, float surfaceY)
        {
            GameObject barrel;
            if (prefab != null)
            {
                barrel = Instantiate(prefab, parent);
                barrel.name = objectName;
                barrel.transform.SetPositionAndRotation(position, rotation);
                barrel.transform.localScale = scale;
                AlignRendererBottomToSurface(barrel, surfaceY);
            }
            else
            {
                barrel = CreateCylinder(parent, objectName, position, new Vector3(0.22f, 0.36f, 0.22f), m_orangeMaterial, true);
                barrel.transform.rotation = rotation;
            }
        }

        private static void AlignRendererBottomToSurface(GameObject target, float surfaceY)
        {
            if (!TryGetRendererBounds(target, out var bounds))
            {
                return;
            }

            target.transform.position += Vector3.up * (surfaceY - bounds.min.y);
        }

        private static void AssignMaterialToRenderers(GameObject target, Material material)
        {
            if (target == null || material == null)
            {
                return;
            }

            foreach (var renderer in target.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterial = material;
            }
        }

        private static bool TryGetRendererBounds(GameObject target, out Bounds bounds)
        {
            var renderers = target.GetComponentsInChildren<Renderer>();
            bounds = new Bounds(target.transform.position, Vector3.zero);
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
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

        private GameObject CreateCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material, bool colliderEnabled, Quaternion? localRotation = null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation ?? Quaternion.identity;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            cube.GetComponent<Collider>().enabled = colliderEnabled;
            return cube;
        }

        private GameObject CreateCylinder(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material, bool colliderEnabled, Quaternion? localRotation = null)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = objectName;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localRotation = localRotation ?? Quaternion.identity;
            cylinder.transform.localScale = localScale;
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            cylinder.GetComponent<Collider>().enabled = colliderEnabled;
            return cylinder;
        }

        private static void AddWallText(Transform parent, string text, Vector3 position, Quaternion rotation, float characterSize, Color color, string objectName)
        {
            var textObject = new GameObject(objectName);
            textObject.transform.SetParent(parent, false);
            textObject.transform.SetPositionAndRotation(position, rotation);

            var textMesh = textObject.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 96;
            textMesh.characterSize = 0.1f;
            textMesh.color = color;

            textObject.transform.localScale = Vector3.one * characterSize;
        }
    }

#if UNITY_EDITOR
    public static class TutorialRoomDresserEditorMenu
    {
        [UnityEditor.MenuItem("SimJam/Rebuild Tutorial Room Dressing")]
        private static void RebuildTutorialRoomDressing()
        {
            foreach (var dresser in Object.FindObjectsByType<TutorialRoomDresser>())
            {
                dresser.BuildRoomDressing();
                UnityEditor.EditorUtility.SetDirty(dresser);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        }
    }
#endif
}
