using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public class ProceduralArmRig : MonoBehaviour
    {
        private const float UpperArmLength = 0.28f;
        private const float ForearmLength = 0.26f;
        private const float UpperArmRadius = 0.040f;
        private const float ForearmRadius = 0.035f;
        private const float WristRadius = 0.030f;

        private OVRCameraRig m_rig;
        private bool m_initialized;
        private readonly GameObject[] m_containers = new GameObject[2];
        private readonly Transform[] m_shoulderSpheres = new Transform[2];
        private readonly Transform[] m_upperArms = new Transform[2];
        private readonly Transform[] m_elbowSpheres = new Transform[2];
        private readonly Transform[] m_forearms = new Transform[2];
        private readonly Transform[] m_wrists = new Transform[2];

        public void Initialize(OVRCameraRig rig, Material sleeveMaterial, Material skinMaterial)
        {
            m_rig = rig;

            for (int i = 0; i < 2; i++)
            {
                if (m_containers[i] != null)
                {
                    Destroy(m_containers[i]);
                }

                GameObject container = new GameObject(i == 0 ? "Left Arm" : "Right Arm");
                container.transform.SetParent(transform, false);
                m_containers[i] = container;

                m_shoulderSpheres[i] = CreatePart(PrimitiveType.Sphere, container.transform, "Shoulder", sleeveMaterial, new Vector3(0.10f, 0.09f, 0.10f));
                m_upperArms[i] = CreatePart(PrimitiveType.Cylinder, container.transform, "Upper Arm", sleeveMaterial, Vector3.one);
                m_elbowSpheres[i] = CreatePart(PrimitiveType.Sphere, container.transform, "Elbow", sleeveMaterial, new Vector3(0.075f, 0.075f, 0.075f));
                m_forearms[i] = CreatePart(PrimitiveType.Cylinder, container.transform, "Forearm", sleeveMaterial, Vector3.one);
                m_wrists[i] = CreatePart(PrimitiveType.Cylinder, container.transform, "Wrist", skinMaterial, Vector3.one);
            }

            m_initialized = true;
        }

        private void LateUpdate()
        {
            if (!m_initialized || m_rig == null || m_rig.centerEyeAnchor == null)
            {
                HideAll();
                return;
            }

            Transform head = m_rig.centerEyeAnchor;
            Quaternion headYaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                OVRInput.Controller controller = i == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
                if (!OVRInput.IsControllerConnected(controller))
                {
                    m_containers[i].SetActive(false);
                    continue;
                }

                m_containers[i].SetActive(true);

                Transform anchor = i == 0 ? m_rig.leftControllerAnchor : m_rig.rightControllerAnchor;
                Vector3 shoulder = head.position + headYaw * new Vector3(side * 0.17f, -0.13f, -0.04f);
                Vector3 wristTarget = anchor.position + anchor.rotation * new Vector3(0f, -0.01f, -0.045f);
                Vector3 elbow = SolveElbow(shoulder, wristTarget, headYaw, side);
                Vector3 handEnd = anchor.position + anchor.rotation * new Vector3(0f, -0.018f, -0.005f);

                m_shoulderSpheres[i].position = shoulder;
                PlaceSegment(m_upperArms[i], shoulder, elbow, UpperArmRadius);
                m_elbowSpheres[i].position = elbow;
                PlaceSegment(m_forearms[i], elbow, wristTarget, ForearmRadius);
                PlaceSegment(m_wrists[i], wristTarget, handEnd, WristRadius);
            }
        }

        private void HideAll()
        {
            for (int i = 0; i < 2; i++)
            {
                if (m_containers[i] != null)
                {
                    m_containers[i].SetActive(false);
                }
            }
        }

        private static Vector3 SolveElbow(Vector3 shoulder, Vector3 wristTarget, Quaternion headYaw, float side)
        {
            float a = UpperArmLength;
            float b = ForearmLength;
            Vector3 toTarget = wristTarget - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, a + b - 0.01f);
            Vector3 n = toTarget.normalized;
            Vector3 hint = headYaw * new Vector3(side * 0.35f, -0.8f, -0.25f);
            Vector3 perp = hint - n * Vector3.Dot(hint, n);
            if (perp.sqrMagnitude < 1e-4f)
            {
                perp = Vector3.down - n * Vector3.Dot(Vector3.down, n);
                if (perp.sqrMagnitude < 1e-4f)
                {
                    perp = Vector3.forward - n * Vector3.Dot(Vector3.forward, n);
                }
            }

            perp.Normalize();
            float cosAlpha = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinAlpha = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAlpha * cosAlpha));
            return shoulder + n * (a * cosAlpha) + perp * (a * sinAlpha);
        }

        private static void PlaceSegment(Transform segment, Vector3 p0, Vector3 p1, float radius)
        {
            Vector3 offset = p1 - p0;
            float length = offset.magnitude;
            if (length < 1e-5f)
            {
                segment.position = p0;
                return;
            }

            segment.position = (p0 + p1) * 0.5f;
            segment.rotation = Quaternion.FromToRotation(Vector3.up, offset / length);
            segment.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
        }

        private static Transform CreatePart(PrimitiveType type, Transform parent, string partName, Material material, Vector3 scale)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localScale = scale;
            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null)
            {
                Destroy(partCollider);
            }

            part.GetComponent<MeshRenderer>().sharedMaterial = material;
            return part.transform;
        }
    }
}
