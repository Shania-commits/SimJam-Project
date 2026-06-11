using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public static class DetectorModelBuilder
    {
        public struct DetectorParts
        {
            public GameObject Root;
            public TextMesh ScreenText;
            public Transform SensorTip;
            public BoxCollider BodyCollider;
            public Rigidbody Body;
        }

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

        private static Material MakeStandardMaterial(Color color, float metallic, float glossiness)
        {
            Material material = new Material(Shader.Find("Standard"));
            material.color = color;
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Glossiness", glossiness);
            return material;
        }

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

        private static void CreatePart(GameObject root, string name, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            CreatePart(root, name, type, localPosition, localScale, material, Quaternion.identity);
        }

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
