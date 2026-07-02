using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// A lightweight, primitive-mesh (no Animator, no skinned mesh) pair of VR arms built and posed
    /// entirely at runtime. Each frame it anchors a shoulder near the head, aims the wrist at the
    /// OVR controller, and solves a two-bone IK chain (upper arm + forearm) so the player sees stubby
    /// arms reaching toward whatever their hands are holding (e.g. the identiFINDER detector). A simpler
    /// alternative to <c>MixamoArmRig</c> that trades fidelity for zero dependencies.
    /// </summary>
    public class ProceduralArmRig : MonoBehaviour
    {
        /// <summary>Length in metres of the shoulder-to-elbow segment used by the IK solver.</summary>
        private const float UpperArmLength = 0.28f;
        /// <summary>Length in metres of the elbow-to-wrist segment used by the IK solver.</summary>
        private const float ForearmLength = 0.26f;
        /// <summary>Cylinder radius (metres) of the upper-arm segment mesh.</summary>
        private const float UpperArmRadius = 0.040f;
        /// <summary>Cylinder radius (metres) of the forearm segment mesh.</summary>
        private const float ForearmRadius = 0.035f;
        /// <summary>Cylinder radius (metres) of the wrist/hand stub mesh.</summary>
        private const float WristRadius = 0.030f;

        /// <summary>The OVR camera rig supplying head and controller anchor transforms that drive the arms.</summary>
        private OVRCameraRig m_rig;
        /// <summary>True once <see cref="Initialize"/> has built the limb primitives; gates <see cref="LateUpdate"/>.</summary>
        private bool m_initialized;
        /// <summary>Root GameObject per side ([0]=left, [1]=right) toggled on/off with controller connection.</summary>
        private readonly GameObject[] m_containers = new GameObject[2];
        /// <summary>Sphere marking the shoulder joint per side ([0]=left, [1]=right).</summary>
        private readonly Transform[] m_shoulderSpheres = new Transform[2];
        /// <summary>Cylinder for the shoulder-to-elbow segment per side ([0]=left, [1]=right).</summary>
        private readonly Transform[] m_upperArms = new Transform[2];
        /// <summary>Sphere marking the elbow joint per side ([0]=left, [1]=right).</summary>
        private readonly Transform[] m_elbowSpheres = new Transform[2];
        /// <summary>Cylinder for the elbow-to-wrist segment per side ([0]=left, [1]=right).</summary>
        private readonly Transform[] m_forearms = new Transform[2];
        /// <summary>Cylinder for the skin-coloured wrist/hand stub per side ([0]=left, [1]=right).</summary>
        private readonly Transform[] m_wrists = new Transform[2];

        /// <summary>
        /// Builds the primitive limb hierarchy for both arms (shoulder, upper arm, elbow, forearm, wrist)
        /// and stores the supplied rig so <see cref="LateUpdate"/> can pose them. Safe to call again: any
        /// previously created containers are destroyed first. Sleeve material clothes the arm segments;
        /// skin material is used for the exposed wrist stub.
        /// </summary>
        /// <param name="rig">OVR camera rig providing head and controller anchors.</param>
        /// <param name="sleeveMaterial">Material for the shoulder, upper arm, elbow and forearm parts.</param>
        /// <param name="skinMaterial">Material for the exposed wrist/hand stub.</param>
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

        /// <summary>
        /// Poses both arms after the camera has updated: derives each shoulder from the head yaw, aims the
        /// wrist at the controller anchor, solves the elbow via <see cref="SolveElbow"/>, and stretches the
        /// segment primitives into place. Hides everything if the rig is missing, and hides an individual
        /// arm whose controller is not connected.
        /// </summary>
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
                // Shoulder sits below and slightly behind the head, offset outward by side (-1 left / +1 right).
                Vector3 shoulder = head.position + headYaw * new Vector3(side * 0.17f, -0.13f, -0.04f);
                // Wrist target and hand end are nudged behind/under the controller so the mesh reads as a forearm gripping the tool.
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

        /// <summary>Deactivates both arm containers, used when the rig is unavailable.</summary>
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

        /// <summary>
        /// Two-bone IK solver: given the shoulder and wrist positions and the two fixed bone lengths,
        /// returns the elbow position. Uses the law of cosines to find how far along the shoulder→wrist
        /// axis the elbow projects, then pushes it out along a "pole" direction (derived from a downward,
        /// slightly outward hint biased by <paramref name="side"/>) so the elbow bends naturally down and out.
        /// The target distance is clamped so the chain never over-extends.
        /// </summary>
        private static Vector3 SolveElbow(Vector3 shoulder, Vector3 wristTarget, Quaternion headYaw, float side)
        {
            float a = UpperArmLength;
            float b = ForearmLength;
            Vector3 toTarget = wristTarget - shoulder;
            float d = Mathf.Clamp(toTarget.magnitude, 0.05f, a + b - 0.01f);
            Vector3 n = toTarget.normalized;
            // Pole hint: bias the elbow downward and outward; project it perpendicular to the shoulder→wrist axis.
            Vector3 hint = headYaw * new Vector3(side * 0.35f, -0.8f, -0.25f);
            Vector3 perp = hint - n * Vector3.Dot(hint, n);
            // Fallbacks if the hint is (near-)parallel to the arm axis: try world down, then world forward.
            if (perp.sqrMagnitude < 1e-4f)
            {
                perp = Vector3.down - n * Vector3.Dot(Vector3.down, n);
                if (perp.sqrMagnitude < 1e-4f)
                {
                    perp = Vector3.forward - n * Vector3.Dot(Vector3.forward, n);
                }
            }

            perp.Normalize();
            // Law of cosines: angle at the shoulder between the arm axis and the upper-arm bone.
            float cosAlpha = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
            float sinAlpha = Mathf.Sqrt(Mathf.Max(0f, 1f - cosAlpha * cosAlpha));
            return shoulder + n * (a * cosAlpha) + perp * (a * sinAlpha);
        }

        /// <summary>
        /// Positions, orients and scales a Unity cylinder primitive so it spans from <paramref name="p0"/>
        /// to <paramref name="p1"/> with the given radius. Cylinders are 2 units tall along local Y and
        /// centred at their midpoint, hence the half-length Y scale. Degenerate (zero-length) segments are
        /// just parked at <paramref name="p0"/>.
        /// </summary>
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

        /// <summary>
        /// Creates a named primitive (sphere or cylinder) parented under <paramref name="parent"/>, strips its
        /// auto-generated collider (arms are purely visual), assigns the material, and returns its transform.
        /// </summary>
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
