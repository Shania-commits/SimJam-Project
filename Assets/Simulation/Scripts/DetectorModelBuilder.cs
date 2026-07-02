using UnityEngine;

// =============================================================================
// DetectorModelBuilder.cs
//
// PURPOSE:  Static factory that assembles the handheld "identiFINDER" survey
//           meter the player uses to hunt the hidden radioactive barrel. Build()
//           constructs the whole meter from Unity primitives (body, grip, screen,
//           buttons, speaker holes, labels); BuildFromPrefab() wraps an authored
//           art prefab instead. Both return a grabbable root with a box collider,
//           rigidbody, and a "Sensor Tip" transform the radiation model samples.
//
// HOW TO CUSTOMIZE:
//   NOTE: This class has NO [SerializeField] fields and lives in NO scene. Every
//   value below is HARDCODED in this file, so editing it here DOES change runtime
//   behavior directly (there is no Inspector/scene override to worry about). The
//   spawners call these static methods; there is no component to edit in the
//   Unity Inspector.
//
//   - Detector colors/finish: edit the Material lines at the top of Build() —
//     `rubber`, `body`, `accent` (via MakeStandardMaterial(color, metallic,
//     glossiness)) and the green LCD in MakeScreenMaterial() (baseColor + emission).
//   - Detector shape / part layout: each CreatePart(...) call in Build() places one
//     primitive by name, local position, and scale (metres). Add/remove/move parts
//     there. The ribbed grip, screen bezel/face, buttons, and speaker-hole loop are
//     all defined in Build().
//   - On-screen readout default text ("0 CPS / 0.00 uSv/h ...") and the
//     "identiFINDER" brand label: the CreateTextMesh(...) calls in Build(); the
//     returned ScreenText is overwritten each frame by the readout binder at runtime.
//   - Sensor sample point: the `sensorTip.transform.localPosition` in Build() (and
//     `tip.transform.localPosition` in BuildFromPrefab) sets WHERE radiation is
//     measured on the model.
//   - Grab/physics feel: the Rigidbody block in Build() and BuildFromPrefab
//     (`mass`, `collisionDetectionMode`, `interpolation`, `isKinematic`) and the
//     BoxCollider `size`/`center`. Do NOT switch collisionDetectionMode away from
//     ContinuousSpeculative — the grab code toggles isKinematic and other CCD modes
//     error on kinematic bodies.
//   - Prefab path: BuildFromPrefab(prefab, targetHeight) auto-scales the mesh to
//     `targetHeight` (metres) and re-centres it. The prefab itself and targetHeight
//     are chosen by the CALLER (the lab/tutorial spawner), not here. TryGetMeshBounds
//     deliberately ignores the screen Canvas/sprite when measuring — change it there
//     if you want the screen included in auto-scale.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Static factory that assembles the handheld identiFINDER survey meter used to hunt the hidden
    /// radioactive barrel. Offers two paths: <see cref="Build"/> constructs the whole meter procedurally
    /// out of Unity primitives (body, grip, screen, buttons, speaker holes, brand label), while
    /// <see cref="BuildFromPrefab"/> wraps an authored art prefab. Both return a grabbable root with a
    /// collider, rigidbody, and a sensor-tip transform the radiation model samples from.
    /// </summary>
    public static class DetectorModelBuilder
    {
        /// <summary>
        /// Bundle of the key components on a procedurally built detector, handed back to the spawner so
        /// it can drive the readout, sample radiation at the tip, and attach grab/physics behaviour.
        /// </summary>
        public struct DetectorParts
        {
            /// <summary>Root GameObject that parents every mesh piece and carries the collider/body.</summary>
            public GameObject Root;
            /// <summary>The green LCD text the CPS / dose readout is written into each frame.</summary>
            public TextMesh ScreenText;
            /// <summary>Empty transform at the sensor dome; the radiation field is measured from this point.</summary>
            public Transform SensorTip;
            /// <summary>Grab collider covering the whole meter (used for proximity grabbing).</summary>
            public BoxCollider BodyCollider;
            /// <summary>Physics body for the meter; toggled kinematic while held by the grab code.</summary>
            public Rigidbody Body;
        }

        /// <summary>
        /// Builds a complete identiFINDER out of Unity primitives (no external art) — body, rubber rails
        /// and bumper, sensor dome, ribbed grip, bezelled screen, buttons, speaker grille, and text labels —
        /// then adds the grab collider, rigidbody, and sensor tip. Returns the assembled parts.
        /// </summary>
        public static DetectorParts Build()
        {
            GameObject root = new GameObject("identiFINDER");

            Material rubber = MakeStandardMaterial(new Color(0.71f, 0.76f, 0.10f), 0f, 0.25f);
            Material body = MakeStandardMaterial(new Color(0.15f, 0.16f, 0.17f), 0.2f, 0.45f);
            Material accent = MakeStandardMaterial(new Color(0.45f, 0.46f, 0.48f), 0f, 0.35f);
            Material screen = MakeScreenMaterial();

            CreatePart(root, "Main Body", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.068f, 0.20f, 0.042f), body);
            CreatePart(root, "Left Rail", PrimitiveType.Cube, new Vector3(-0.036f, 0f, 0f), new Vector3(0.012f, 0.21f, 0.046f), rubber);
            CreatePart(root, "Right Rail", PrimitiveType.Cube, new Vector3(0.036f, 0f, 0f), new Vector3(0.012f, 0.21f, 0.046f), rubber);
            CreatePart(root, "Top Bumper", PrimitiveType.Cube, new Vector3(0f, 0.105f, 0f), new Vector3(0.075f, 0.018f, 0.048f), rubber);
            CreatePart(root, "Sensor Dome", PrimitiveType.Sphere, new Vector3(0f, 0.118f, 0f), new Vector3(0.05f, 0.025f, 0.04f), body);
            CreatePart(root, "Grip", PrimitiveType.Cube, new Vector3(0f, -0.075f, 0.002f), new Vector3(0.058f, 0.07f, 0.040f), rubber);

            CreatePart(root, "Grip Rib 1", PrimitiveType.Cube, new Vector3(0f, -0.055f, -0.021f), new Vector3(0.05f, 0.008f, 0.004f), body);
            CreatePart(root, "Grip Rib 2", PrimitiveType.Cube, new Vector3(0f, -0.075f, -0.021f), new Vector3(0.05f, 0.008f, 0.004f), body);
            CreatePart(root, "Grip Rib 3", PrimitiveType.Cube, new Vector3(0f, -0.095f, -0.021f), new Vector3(0.05f, 0.008f, 0.004f), body);

            CreatePart(root, "Screen Bezel", PrimitiveType.Cube, new Vector3(0f, 0.055f, -0.0225f), new Vector3(0.058f, 0.048f, 0.004f), body);
            CreatePart(root, "Screen Face", PrimitiveType.Cube, new Vector3(0f, 0.055f, -0.0250f), new Vector3(0.050f, 0.040f, 0.0015f), screen);

            Vector3 buttonScale = new Vector3(0.009f, 0.002f, 0.009f);
            Quaternion buttonRotation = Quaternion.Euler(90f, 0f, 0f);
            CreatePart(root, "Button 1", PrimitiveType.Cylinder, new Vector3(-0.012f, 0.012f, -0.023f), buttonScale, accent, buttonRotation);
            CreatePart(root, "Button 2", PrimitiveType.Cylinder, new Vector3(0.012f, 0.012f, -0.023f), buttonScale, accent, buttonRotation);
            CreatePart(root, "Button 3", PrimitiveType.Cylinder, new Vector3(-0.012f, -0.004f, -0.023f), buttonScale, accent, buttonRotation);
            CreatePart(root, "Button 4", PrimitiveType.Cylinder, new Vector3(0.012f, -0.004f, -0.023f), buttonScale, accent, buttonRotation);
            CreatePart(root, "Power Button", PrimitiveType.Cylinder, new Vector3(0f, -0.022f, -0.023f), new Vector3(0.011f, 0.002f, 0.011f), rubber, buttonRotation);

            Vector3 speakerCenter = new Vector3(0.018f, -0.038f, -0.022f);
            Vector3 speakerScale = new Vector3(0.0035f, 0.0035f, 0.0035f);
            Vector2[] speakerOffsets = new Vector2[]
            {
                new Vector2(0f, 0f),
                new Vector2(0.005f, 0.005f),
                new Vector2(-0.005f, 0.005f),
                new Vector2(0.005f, -0.005f),
                new Vector2(-0.005f, -0.005f)
            };
            for (int i = 0; i < speakerOffsets.Length; i++)
            {
                Vector3 position = speakerCenter + new Vector3(speakerOffsets[i].x, speakerOffsets[i].y, 0f);
                CreatePart(root, "Speaker Hole " + (i + 1), PrimitiveType.Sphere, position, speakerScale, body);
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            TextMesh screenText = CreateTextMesh(
                root,
                "Screen Readout",
                new Vector3(0f, 0.055f, -0.0262f),
                0.0015f,
                64,
                new Color(0.55f, 1.0f, 0.6f),
                "0 CPS\n0.00 \u00B5Sv/h\n..........",
                font);

            CreateTextMesh(
                root,
                "Brand Label",
                new Vector3(0f, 0.024f, -0.0235f),
                0.0008f,
                48,
                new Color(0.85f, 0.88f, 0.9f),
                "identiFINDER",
                font);

            GameObject sensorTip = new GameObject("Sensor Tip");
            sensorTip.transform.SetParent(root.transform, false);
            sensorTip.transform.localPosition = new Vector3(0f, 0.13f, 0f);

            BoxCollider bodyCollider = root.AddComponent<BoxCollider>();
            bodyCollider.size = new Vector3(0.08f, 0.24f, 0.06f);
            bodyCollider.center = new Vector3(0f, 0.005f, 0f);

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = 0.4f;
            // ContinuousSpeculative stays valid while the grab code toggles isKinematic
            // (sweep-based CCD on a kinematic body logs an error and falls back).
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.isKinematic = false;

            DetectorParts parts = new DetectorParts();
            parts.Root = root;
            parts.ScreenText = screenText;
            parts.SensorTip = sensorTip.transform;
            parts.BodyCollider = bodyCollider;
            parts.Body = rigidbody;
            return parts;
        }

        /// <summary>
        /// Bundle returned by <see cref="BuildFromPrefab"/>: the wrapper root plus the pieces the spawner
        /// needs to place the meter in the hand, sample radiation at the tip, and locate the screen source.
        /// </summary>
        public struct PrefabDetector
        {
            /// <summary>Clean wrapper root carrying the collider/body, with the art prefab parented under it.</summary>
            public GameObject Root;
            /// <summary>Empty transform at the top of the mesh; the radiation field is measured from here.</summary>
            public Transform SensorTip;
            /// <summary>Distance from the root down to the collider's bottom, so the meter can rest on a surface.</summary>
            public float ColliderBottomOffset;
            /// <summary>The instantiated prefab whose world-space screen the readout binder writes into.</summary>
            public GameObject ScreenSource;
        }

        // The SAME identiFINDER as the lab: instantiate the colleague's prefab, auto-scale it to a sane
        // height, re-centre the mesh on a clean root, and add a grab collider + body + sensor tip. Mirrors
        // RadiationLabRoomSpawner.BuildCustomDetectorModel so the tutorial and lab show the identical model.
        /// <summary>
        /// Wraps an authored identiFINDER art prefab into a grabbable meter: instantiates it, strips any
        /// duplicate EventSystem, scales it to <paramref name="targetHeight"/>, re-centres it on a clean
        /// root, and adds a fitted box collider, rigidbody, and sensor tip. Kept in sync with the lab
        /// spawner so the tutorial and the lab display the identical model.
        /// </summary>
        /// <param name="prefab">The authored identiFINDER model to instantiate and wrap.</param>
        /// <param name="targetHeight">Desired world-space height (metres) to auto-scale the mesh to.</param>
        public static PrefabDetector BuildFromPrefab(GameObject prefab, float targetHeight)
        {
            var root = new GameObject("identiFINDER");
            var instance = Object.Instantiate(prefab);
            instance.name = "identiFINDER Model";
            instance.transform.SetParent(root.transform, false);

            // A world-space readout needs no UI input; drop the prefab's EventSystem (avoids dupes).
            foreach (var eventSystem in instance.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true))
            {
                Object.Destroy(eventSystem.gameObject);
            }

            Bounds bounds;
            if (TryGetMeshBounds(instance, out bounds) && bounds.size.y > 1e-4f)
            {
                instance.transform.localScale *= Mathf.Clamp(targetHeight / bounds.size.y, 1e-4f, 1000f);
                if (TryGetMeshBounds(instance, out bounds))
                {
                    instance.transform.position += root.transform.position - bounds.center;
                    TryGetMeshBounds(instance, out bounds);
                }
            }
            else
            {
                bounds = new Bounds(root.transform.position, new Vector3(0.08f, targetHeight, 0.06f));
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

            return new PrefabDetector
            {
                Root = root,
                SensorTip = tip.transform,
                ColliderBottomOffset = halfHeight + 0.01f,
                ScreenSource = instance,
            };
        }

        // Bounds of the 3D mesh ONLY -- skips the world-space screen Canvas + sprite overlay, whose
        // authored offset would otherwise blow up the bounds and shrink/misplace the model.
        /// <summary>
        /// Computes the combined world-space bounds of the prefab's solid mesh renderers, deliberately
        /// ignoring UI Canvas renderers and sprite overlays (the screen) so the auto-scale/re-centre
        /// logic measures only the physical model. Returns false when no qualifying renderer is found.
        /// </summary>
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

        /// <summary>
        /// Creates a runtime material on the Built-in pipeline's Standard shader with the given base
        /// colour, metallic, and glossiness — used for the meter's body, rubber, and accent pieces.
        /// </summary>
        private static Material MakeStandardMaterial(Color color, float metallic, float glossiness)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", glossiness);
            return material;
        }

        /// <summary>
        /// Builds the glowing green LCD material for the screen face. Prefers the pre-authored
        /// <c>SimJamEmissiveScreen</c> material (forced into builds to survive shader stripping) and
        /// falls back to a Standard shader with emission enabled if it is missing.
        /// </summary>
        private static Material MakeScreenMaterial()
        {
            Color baseColor = new Color(0.02f, 0.05f, 0.03f);
            Color emission = new Color(0.10f, 0.45f, 0.18f) * 1.6f;
            Material loaded = Resources.Load<Material>("SimJamEmissiveScreen");
            Material material;
            if (loaded != null)
            {
                material = new Material(loaded);
            }
            else
            {
                material = new Material(Shader.Find("Standard"));
                material.EnableKeyword("_EMISSION");
            }
            material.color = baseColor;
            material.SetColor("_EmissionColor", emission);
            return material;
        }

        /// <summary>
        /// Overload of <see cref="CreatePart(GameObject,string,PrimitiveType,Vector3,Vector3,Material,Quaternion)"/>
        /// that spawns the primitive with no rotation (identity).
        /// </summary>
        private static void CreatePart(GameObject root, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreatePart(root, name, type, localPosition, localScale, material, Quaternion.identity);
        }

        /// <summary>
        /// Spawns a single Unity primitive as a child of the detector root at the given local transform
        /// and material. Disables the primitive's own collider (the meter uses one shared box collider)
        /// so these pieces are visual-only.
        /// </summary>
        private static void CreatePart(GameObject root, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material, Quaternion localRotation)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = name;
            part.transform.SetParent(root.transform, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = localRotation;
            part.transform.localScale = localScale;
            part.GetComponent<Collider>().enabled = false;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Creates a child <see cref="TextMesh"/> for on-model text (the screen readout and the
        /// "identiFINDER" brand label), configured centre-anchored, plain (non-rich) text using the
        /// supplied legacy font. Returns the TextMesh so its content can be updated at runtime.
        /// </summary>
        private static TextMesh CreateTextMesh(GameObject root, string name, Vector3 localPosition, float characterSize, int fontSize, Color color, string text, Font font)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPosition;
            // TextMesh glyphs read correctly when viewed from the object's -Z side, which is
            // exactly where the user looks at the -Z-facing screen — identity, NOT 180 flipped.
            go.transform.localRotation = Quaternion.identity;

            TextMesh textMesh = go.AddComponent<TextMesh>();
            textMesh.font = font;
            textMesh.characterSize = characterSize;
            textMesh.fontSize = fontSize;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = color;
            textMesh.richText = false;
            textMesh.text = text;

            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return textMesh;
        }
    }
}
