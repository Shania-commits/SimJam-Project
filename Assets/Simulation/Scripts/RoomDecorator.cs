using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public static class RoomDecorator
    {
        public delegate Material MaterialFactory(Color color, string name, float metallic, float smoothness, Texture2D albedo, Color? emission);

        public const float PedestalTopHeight = 1.02f;

        // Captured by DecorateOfficeRoom so the spawn-side door frame mirrors the doorway.
        private static float lastDoorwayWidth = 1.0f;
        private static float lastDoorwayHeight = 2.1f;

        private static Material blobShadowMaterial;

        public static void DecorateOfficeRoom(Transform roomRoot, Vector2 roomSizeMeters, float wallHeight, float doorwayWidth, float doorwayHeight, float sharedWallZ, MaterialFactory createMaterial)
        {
            lastDoorwayWidth = doorwayWidth;
            lastDoorwayHeight = doorwayHeight;

            float w = roomSizeMeters.x;
            float d = roomSizeMeters.y;

            Material baseboardMat = createMaterial(new Color(0.20f, 0.21f, 0.22f), "Baseboard", 0f, 0.2f, null, null);
            Material woodMat = createMaterial(new Color(0.32f, 0.20f, 0.10f), "Door Frame Wood", 0f, 0.3f, ProceduralTextureLibrary.WoodGrain256, null);
            Material exitMat = createMaterial(Color.white, "Exit Sign", 0f, 0.2f, ProceduralTextureLibrary.ExitSign128, new Color(0.05f, 0.5f, 0.15f));
            Material hazardMat = createMaterial(Color.white, "Hazard Stripe", 0f, 0.3f, ProceduralTextureLibrary.HazardStripe128, null);
            Material conduitMat = createMaterial(new Color(0.55f, 0.56f, 0.58f), "Conduit", 0.4f, 0.6f, null, null);
            Material radPosterMat = createMaterial(Color.white, "Radiation Poster", 0f, 0.2f, ProceduralTextureLibrary.RadiationPoster256, null);
            Material safePosterMat = createMaterial(Color.white, "Safety Poster", 0f, 0.2f, ProceduralTextureLibrary.SafetyPoster256, null);

            // Baseboards.
            CreatePart(PrimitiveType.Cube, "Baseboard North", roomRoot, new Vector3(0f, 0.05f, d * 0.5f - 0.06f), new Vector3(w, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);
            CreatePart(PrimitiveType.Cube, "Baseboard East", roomRoot, new Vector3(w * 0.5f - 0.06f, 0.05f, 0f), new Vector3(0.018f, 0.10f, d), Quaternion.identity, baseboardMat, false);
            CreatePart(PrimitiveType.Cube, "Baseboard West", roomRoot, new Vector3(-w * 0.5f + 0.06f, 0.05f, 0f), new Vector3(0.018f, 0.10f, d), Quaternion.identity, baseboardMat, false);

            float doorEdge = doorwayWidth * 0.5f + 0.05f;
            float segmentLength = w * 0.5f - doorEdge;
            float segmentCenter = (w * 0.5f + doorEdge) * 0.5f;
            CreatePart(PrimitiveType.Cube, "Baseboard Shared Left", roomRoot, new Vector3(-segmentCenter, 0.05f, sharedWallZ + 0.06f), new Vector3(segmentLength, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);
            CreatePart(PrimitiveType.Cube, "Baseboard Shared Right", roomRoot, new Vector3(segmentCenter, 0.05f, sharedWallZ + 0.06f), new Vector3(segmentLength, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);

            // Door frame on the lab side.
            float jambHeight = doorwayHeight + 0.04f;
            float jambX = doorwayWidth * 0.5f + 0.035f;
            float frameZ = sharedWallZ + 0.045f;
            CreatePart(PrimitiveType.Cube, "Door Jamb Left (Lab)", roomRoot, new Vector3(-jambX, jambHeight * 0.5f, frameZ), new Vector3(0.07f, jambHeight, 0.07f), Quaternion.identity, woodMat, false);
            CreatePart(PrimitiveType.Cube, "Door Jamb Right (Lab)", roomRoot, new Vector3(jambX, jambHeight * 0.5f, frameZ), new Vector3(0.07f, jambHeight, 0.07f), Quaternion.identity, woodMat, false);
            CreatePart(PrimitiveType.Cube, "Door Lintel (Lab)", roomRoot, new Vector3(0f, doorwayHeight + 0.005f, frameZ), new Vector3(doorwayWidth + 0.18f, 0.07f, 0.07f), Quaternion.identity, woodMat, false);

            // Exit sign over the doorway.
            CreatePart(PrimitiveType.Cube, "Exit Sign", roomRoot, new Vector3(0f, doorwayHeight + 0.22f, sharedWallZ + 0.10f), new Vector3(0.36f, 0.16f, 0.03f), Quaternion.identity, exitMat, false);

            // Hazard stripe on the floor across the doorway.
            CreatePart(PrimitiveType.Cube, "Hazard Stripe", roomRoot, new Vector3(0f, 0.004f, sharedWallZ + 0.30f), new Vector3(doorwayWidth + 0.4f, 0.006f, 0.35f), Quaternion.identity, hazardMat, false);

            // Ceiling conduits and junction boxes.
            float conduitY = wallHeight - 0.07f;
            CreatePart(PrimitiveType.Cylinder, "Conduit Depth A", roomRoot, new Vector3(-w * 0.5f + 0.4f, conduitY, 0f), new Vector3(0.05f, d * 0.5f, 0.05f), Quaternion.Euler(90f, 0f, 0f), conduitMat, false);
            CreatePart(PrimitiveType.Cylinder, "Conduit Depth B", roomRoot, new Vector3(-w * 0.5f + 0.55f, conduitY, 0f), new Vector3(0.05f, d * 0.5f, 0.05f), Quaternion.Euler(90f, 0f, 0f), conduitMat, false);
            CreatePart(PrimitiveType.Cylinder, "Conduit Width", roomRoot, new Vector3(0f, conduitY, d * 0.5f - 0.5f), new Vector3(0.05f, w * 0.5f, 0.05f), Quaternion.Euler(0f, 0f, 90f), conduitMat, false);
            CreatePart(PrimitiveType.Cube, "Junction Box A", roomRoot, new Vector3(-w * 0.5f + 0.4f, conduitY, d * 0.5f - 0.095f), new Vector3(0.14f, 0.14f, 0.09f), Quaternion.identity, conduitMat, false);
            CreatePart(PrimitiveType.Cube, "Junction Box B", roomRoot, new Vector3(-w * 0.5f + 0.55f, conduitY, d * 0.5f - 0.095f), new Vector3(0.14f, 0.14f, 0.09f), Quaternion.identity, conduitMat, false);
            CreatePart(PrimitiveType.Cube, "Junction Box C", roomRoot, new Vector3(-w * 0.5f + 0.095f, conduitY, d * 0.5f - 0.5f), new Vector3(0.14f, 0.14f, 0.09f), Quaternion.Euler(0f, 90f, 0f), conduitMat, false);

            // Wall posters.
            Vector3 posterScale = new Vector3(0.5f, 0.7f, 0.012f);
            CreatePart(PrimitiveType.Cube, "Radiation Poster (East)", roomRoot, new Vector3(w * 0.5f - 0.07f, 1.5f, 0f), posterScale, Quaternion.Euler(0f, 90f, 0f), radPosterMat, false);
            CreatePart(PrimitiveType.Cube, "Safety Poster (West)", roomRoot, new Vector3(-w * 0.5f + 0.07f, 1.5f, 0f), posterScale, Quaternion.Euler(0f, 90f, 0f), safePosterMat, false);
            CreatePart(PrimitiveType.Cube, "Radiation Poster (North)", roomRoot, new Vector3(0f, 1.5f, d * 0.5f - 0.07f), posterScale, Quaternion.identity, radPosterMat, false);
        }

        public static void DecorateSpawnRoom(Transform roomRoot, Vector3 spawnCenter, Vector2 spawnSizeMeters, float wallHeight, float sharedWallZ, MaterialFactory createMaterial)
        {
            float w = spawnSizeMeters.x;
            float d = spawnSizeMeters.y;
            float floorY = spawnCenter.y;

            Material baseboardMat = createMaterial(new Color(0.20f, 0.21f, 0.22f), "Baseboard", 0f, 0.2f, null, null);
            Material woodMat = createMaterial(new Color(0.32f, 0.20f, 0.10f), "Door Frame Wood", 0f, 0.3f, ProceduralTextureLibrary.WoodGrain256, null);
            Material safePosterMat = createMaterial(Color.white, "Safety Poster", 0f, 0.2f, ProceduralTextureLibrary.SafetyPoster256, null);

            // Baseboards.
            CreatePart(PrimitiveType.Cube, "Spawn Baseboard South", roomRoot, new Vector3(spawnCenter.x, floorY + 0.05f, spawnCenter.z - d * 0.5f + 0.06f), new Vector3(w, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);
            CreatePart(PrimitiveType.Cube, "Spawn Baseboard East", roomRoot, new Vector3(spawnCenter.x + w * 0.5f - 0.06f, floorY + 0.05f, spawnCenter.z), new Vector3(0.018f, 0.10f, d), Quaternion.identity, baseboardMat, false);
            CreatePart(PrimitiveType.Cube, "Spawn Baseboard West", roomRoot, new Vector3(spawnCenter.x - w * 0.5f + 0.06f, floorY + 0.05f, spawnCenter.z), new Vector3(0.018f, 0.10f, d), Quaternion.identity, baseboardMat, false);

            float doorEdge = lastDoorwayWidth * 0.5f + 0.05f;
            float leftWallX = spawnCenter.x - w * 0.5f;
            float rightWallX = spawnCenter.x + w * 0.5f;
            float baseboardZ = sharedWallZ - 0.06f;
            float leftLength = -doorEdge - leftWallX;
            if (leftLength > 0f)
            {
                CreatePart(PrimitiveType.Cube, "Spawn Baseboard Shared Left", roomRoot, new Vector3((leftWallX - doorEdge) * 0.5f, floorY + 0.05f, baseboardZ), new Vector3(leftLength, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);
            }
            float rightLength = rightWallX - doorEdge;
            if (rightLength > 0f)
            {
                CreatePart(PrimitiveType.Cube, "Spawn Baseboard Shared Right", roomRoot, new Vector3((rightWallX + doorEdge) * 0.5f, floorY + 0.05f, baseboardZ), new Vector3(rightLength, 0.10f, 0.018f), Quaternion.identity, baseboardMat, false);
            }

            // Door frame mirror on the spawn side.
            float jambHeight = lastDoorwayHeight + 0.04f;
            float jambX = lastDoorwayWidth * 0.5f + 0.035f;
            float frameZ = sharedWallZ - 0.045f;
            CreatePart(PrimitiveType.Cube, "Door Jamb Left (Spawn)", roomRoot, new Vector3(-jambX, floorY + jambHeight * 0.5f, frameZ), new Vector3(0.07f, jambHeight, 0.07f), Quaternion.identity, woodMat, false);
            CreatePart(PrimitiveType.Cube, "Door Jamb Right (Spawn)", roomRoot, new Vector3(jambX, floorY + jambHeight * 0.5f, frameZ), new Vector3(0.07f, jambHeight, 0.07f), Quaternion.identity, woodMat, false);
            CreatePart(PrimitiveType.Cube, "Door Lintel (Spawn)", roomRoot, new Vector3(0f, floorY + lastDoorwayHeight + 0.005f, frameZ), new Vector3(lastDoorwayWidth + 0.18f, 0.07f, 0.07f), Quaternion.identity, woodMat, false);

            // Poster on the spawn room's west wall.
            CreatePart(PrimitiveType.Cube, "Safety Poster (Spawn West)", roomRoot, new Vector3(spawnCenter.x - w * 0.5f + 0.07f, floorY + 1.5f, spawnCenter.z), new Vector3(0.5f, 0.7f, 0.012f), Quaternion.Euler(0f, 90f, 0f), safePosterMat, false);
        }

        public static GameObject BuildDetectorPedestal(Transform parent, Vector3 floorPosition, MaterialFactory createMaterial)
        {
            GameObject root = new GameObject("Detector Pedestal");
            root.transform.SetParent(parent, false);
            root.transform.position = floorPosition;

            Material concreteMat = createMaterial(new Color(0.88f, 0.88f, 0.86f), "Pedestal Concrete", 0f, 0.35f, ProceduralTextureLibrary.Concrete512, null);
            Material capMat = createMaterial(new Color(0.75f, 0.75f, 0.73f), "Pedestal Cap", 0f, 0.35f, ProceduralTextureLibrary.Concrete512, null);
            Material accentMat = createMaterial(new Color(0.16f, 0.56f, 0.72f), "Pedestal Accent", 0f, 0.5f, null, new Color(0.16f, 0.56f, 0.72f));
            Material plaqueMat = createMaterial(Color.white, "Detector Plaque", 0f, 0.4f, ProceduralTextureLibrary.DetectorPlaque256, null);

            // Structural parts keep colliders so the detector can rest on top.
            CreatePart(PrimitiveType.Cube, "Pedestal Base", root.transform, new Vector3(0f, 0.04f, 0f), new Vector3(0.45f, 0.08f, 0.45f), Quaternion.identity, concreteMat, true);
            CreatePart(PrimitiveType.Cube, "Pedestal Column", root.transform, new Vector3(0f, 0.52f, 0f), new Vector3(0.30f, 0.88f, 0.30f), Quaternion.identity, concreteMat, true);
            CreatePart(PrimitiveType.Cube, "Pedestal Cap", root.transform, new Vector3(0f, 0.99f, 0f), new Vector3(0.40f, 0.06f, 0.40f), Quaternion.identity, capMat, true);

            CreatePart(PrimitiveType.Cube, "Pedestal Accent Strip", root.transform, new Vector3(0f, 0.952f, 0f), new Vector3(0.34f, 0.014f, 0.34f), Quaternion.identity, accentMat, false);
            CreatePart(PrimitiveType.Cube, "Pedestal Plaque", root.transform, new Vector3(0f, 0.62f, -0.157f), new Vector3(0.22f, 0.13f, 0.014f), Quaternion.identity, plaqueMat, false);

            GameObject lightObject = new GameObject("Pedestal Light");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            Light pedestalLight = lightObject.AddComponent<Light>();
            pedestalLight.type = LightType.Point;
            pedestalLight.color = new Color(1f, 0.95f, 0.85f);
            pedestalLight.intensity = 0.85f;
            pedestalLight.range = 2.0f;
            pedestalLight.renderMode = LightRenderMode.ForceVertex;
            pedestalLight.shadows = LightShadows.None;

            return root;
        }

        public static GameObject AddBlobShadow(Transform parent, Vector3 worldPosition, float radius)
        {
            if (blobShadowMaterial == null)
            {
                Material loaded = Resources.Load<Material>("SimJamBlobShadow");
                if (loaded == null)
                {
                    return null;
                }
                blobShadowMaterial = new Material(loaded);
                blobShadowMaterial.mainTexture = ProceduralTextureLibrary.BlobShadow64;
            }

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Blob Shadow";
            quad.transform.SetParent(parent, false);
            quad.transform.position = worldPosition + Vector3.up * 0.006f;
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            quad.GetComponent<Collider>().enabled = false;
            quad.GetComponent<Renderer>().sharedMaterial = blobShadowMaterial;
            return quad;
        }

        private static GameObject CreatePart(PrimitiveType type, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Quaternion localRotation, Material material, bool colliderEnabled)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            part.GetComponent<Collider>().enabled = colliderEnabled;
            return part;
        }
    }
}
