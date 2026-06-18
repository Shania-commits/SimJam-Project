using UnityEngine;

namespace SimJam.BarrelSimulator
{
    // Drives a Mixamo-rigged arms mesh (mixamorig:* bones) from the OVR rig with no Animator:
    //   * anchors the chest (Spine2) to a torso point estimated from the headset (yaw-only),
    //   * runs analytic two-bone IK per arm so the Hand bone reaches the controller, elbow bending
    //     down/back via a pole hint,
    //   * optionally matches the Hand bone to the controller orientation.
    //
    // Bones are aimed along their RECORDED REST AXIS (the rest-pose local direction to the child),
    // so the solver is independent of which local axis Mixamo/the FBX importer made "down the bone".
    // Everything runs in LateUpdate, strictly parent-before-child, because writing a parent's world
    // rotation immediately moves its children that same frame.
    public class MixamoArmRig : MonoBehaviour
    {
        // Tunables — sensible defaults; expose-and-nudge on-device. 0 = left, 1 = right.
        public Vector3 ChestOffsetFromHead = new Vector3(0f, -0.13f, -0.05f);
        public Vector3 WristTargetLocalOffset = new Vector3(0f, -0.01f, -0.045f);
        public Vector3 ElbowPoleLocal = new Vector3(0.3f, -0.4f, -0.1f); // x mirrored per side, chest-yaw space
        public float BodyYawOffsetDegrees;                                // set 180 if the body faces backward
        public bool MatchHandToController;                                // off => hand follows the forearm
        public Vector3 HandEulerOffsetRight = Vector3.zero;               // applied to controller rotation when matching
        public Vector3 HandEulerOffsetLeft = Vector3.zero;

        private OVRCameraRig m_rig;
        private Transform m_modelRoot;
        private Transform m_chest;
        private Vector3 m_chestLocalInRoot;

        private readonly Transform[] m_upperArm = new Transform[2];
        private readonly Transform[] m_foreArm = new Transform[2];
        private readonly Transform[] m_hand = new Transform[2];
        private readonly float[] m_upperLen = new float[2];
        private readonly float[] m_foreLen = new float[2];
        private readonly Vector3[] m_upperRestAxis = new Vector3[2];
        private readonly Vector3[] m_foreRestAxis = new Vector3[2];
        private bool m_ready;

        public bool IsReady => m_ready;

        public void Initialize(OVRCameraRig rig, GameObject armsInstance)
        {
            m_rig = rig;
            m_modelRoot = armsInstance.transform;

            m_chest = FindDeep(m_modelRoot, "mixamorig:Spine2");
            if (m_chest == null)
            {
                // Fall back to the highest spine/root bone if Spine2 was renamed/removed.
                m_chest = FindDeep(m_modelRoot, "mixamorig:Spine1")
                          ?? FindDeep(m_modelRoot, "mixamorig:Spine")
                          ?? FindDeep(m_modelRoot, "mixamorig:Hips")
                          ?? m_modelRoot;
            }

            m_chestLocalInRoot = m_modelRoot.InverseTransformPoint(m_chest.position);

            var ok = true;
            for (var i = 0; i < 2; i++)
            {
                var prefix = i == 0 ? "mixamorig:Left" : "mixamorig:Right";
                m_upperArm[i] = FindDeep(m_modelRoot, prefix + "Arm");
                m_foreArm[i] = FindDeep(m_modelRoot, prefix + "ForeArm");
                m_hand[i] = FindDeep(m_modelRoot, prefix + "Hand");

                if (m_upperArm[i] == null || m_foreArm[i] == null || m_hand[i] == null)
                {
                    ok = false;
                    continue;
                }

                m_upperLen[i] = Vector3.Distance(m_upperArm[i].position, m_foreArm[i].position);
                m_foreLen[i] = Vector3.Distance(m_foreArm[i].position, m_hand[i].position);
                // Rest-pose local direction from each bone toward its child (axis-agnostic).
                m_upperRestAxis[i] = m_upperArm[i].InverseTransformPoint(m_foreArm[i].position).normalized;
                m_foreRestAxis[i] = m_foreArm[i].InverseTransformPoint(m_hand[i].position).normalized;
            }

            if (!ok)
            {
                Debug.LogWarning("MixamoArmRig: could not find expected mixamorig arm bones; IK disabled.");
            }

            m_ready = ok;
        }

        private void LateUpdate()
        {
            if (!m_ready || m_rig == null || m_rig.centerEyeAnchor == null)
            {
                return;
            }

            var head = m_rig.centerEyeAnchor;
            var chestYaw = Quaternion.Euler(0f, head.eulerAngles.y + BodyYawOffsetDegrees, 0f);

            // Place the body: drive the model root so the chest bone lands at the torso point and the
            // body faces the head yaw. Using TransformPoint preserves the rig's rest pose and scale.
            m_modelRoot.rotation = chestYaw;
            var chestNow = m_modelRoot.TransformPoint(m_chestLocalInRoot);
            var chestTarget = head.position + chestYaw * ChestOffsetFromHead;
            m_modelRoot.position += chestTarget - chestNow;

            for (var i = 0; i < 2; i++)
            {
                if (m_upperArm[i] == null)
                {
                    continue;
                }

                var controller = i == 0 ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
                var anchor = i == 0 ? m_rig.leftControllerAnchor : m_rig.rightControllerAnchor;
                if (anchor == null || !OVRInput.IsControllerConnected(controller))
                {
                    continue;
                }

                var side = i == 0 ? -1f : 1f;
                SolveArm(i, side, anchor, chestYaw);
            }
        }

        private void SolveArm(int i, float side, Transform anchor, Quaternion chestYaw)
        {
            var shoulder = m_upperArm[i].position;                       // actual shoulder joint, post body-placement
            var target = anchor.position + anchor.rotation * WristTargetLocalOffset;
            var l1 = m_upperLen[i];
            var l2 = m_foreLen[i];

            var toTarget = target - shoulder;
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, (l1 + l2) * 0.999f);
            if (dist < 1e-4f)
            {
                return;
            }

            var dirToTarget = toTarget.normalized;
            var cosA = Mathf.Clamp((l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist), -1f, 1f);
            var a = Mathf.Acos(cosA);

            // Pole hint: elbow wants to sit out/down/back of the chest, mirrored per side.
            var poleHint = shoulder + chestYaw * new Vector3(side * ElbowPoleLocal.x, ElbowPoleLocal.y, ElbowPoleLocal.z);
            var poleDir = poleHint - shoulder;
            var bendAxis = Vector3.Cross(dirToTarget, poleDir);
            if (bendAxis.sqrMagnitude < 1e-6f)
            {
                bendAxis = Vector3.Cross(dirToTarget, Vector3.up);
                if (bendAxis.sqrMagnitude < 1e-6f)
                {
                    bendAxis = Vector3.Cross(dirToTarget, Vector3.forward);
                }
            }

            bendAxis.Normalize();
            var elbowPerp = Vector3.Cross(bendAxis, dirToTarget).normalized; // perp component toward the pole side
            var elbowPos = shoulder + dirToTarget * (l1 * Mathf.Cos(a)) + elbowPerp * (l1 * Mathf.Sin(a));

            // Aim upper arm at the elbow, then (after it has moved its child) aim the forearm at the wrist.
            AimBoneAxis(m_upperArm[i], m_upperRestAxis[i], elbowPos - m_upperArm[i].position);
            AimBoneAxis(m_foreArm[i], m_foreRestAxis[i], target - m_foreArm[i].position);

            if (MatchHandToController && m_hand[i] != null)
            {
                var offset = i == 0 ? HandEulerOffsetLeft : HandEulerOffsetRight;
                m_hand[i].rotation = anchor.rotation * Quaternion.Euler(offset);
            }
        }

        private static void AimBoneAxis(Transform bone, Vector3 localAxis, Vector3 desiredWorldDir)
        {
            if (desiredWorldDir.sqrMagnitude < 1e-10f)
            {
                return;
            }

            var currentWorldDir = bone.TransformDirection(localAxis);
            bone.rotation = Quaternion.FromToRotation(currentWorldDir, desiredWorldDir.normalized) * bone.rotation;
        }

        private static Transform FindDeep(Transform root, string boneName)
        {
            if (root.name == boneName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), boneName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
