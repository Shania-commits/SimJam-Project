using System.Collections.Generic;
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
        // The rig is auto-scaled at init so its shoulder->wrist reach == TargetArmReach, which fixes a
        // mis-scaled FBX (the cause of an always-straight elbow: when reach < shoulder->controller
        // distance the cos-law solver pins the elbow to full extension). MaxReachFraction is a small
        // minimum-bend floor so a fully-stretched arm still reads slightly bent, never locked.
        public float TargetArmReach = 0.58f;
        [Range(0.85f, 0.999f)] public float MaxReachFraction = 0.97f;
        public Vector3 ChestOffsetFromHead = new Vector3(0f, -0.16f, -0.05f);
        public Vector3 WristTargetLocalOffset = new Vector3(0f, -0.01f, -0.045f);
        public Vector3 ElbowPoleLocal = new Vector3(0.3f, -0.4f, -0.1f); // x mirrored per side, chest-yaw space
        public float BodyYawOffsetDegrees;                                // set 180 if the body faces backward
        // Hand twist: auto-calibrated at runtime so the wrist follows the controller without a
        // hand-authored offset (we capture the hand's orientation relative to the controller once).
        public bool MatchHandToController = true;
        // Fingers curl from rest toward a closed fist as the grip trigger is squeezed. The curl axis
        // is the per-finger-bone local bend axis — Mixamo's varies, so it is tunable if curl looks off.
        [Range(0f, 130f)] public float FingerCurlAngle = 70f;
        public Vector3 FingerCurlAxis = new Vector3(0f, 0f, 1f);
        // Flip to -1 if the fingers curl backward (away from the palm) on a given FBX.
        public float FingerCurlSign = 1f;

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
        private readonly Quaternion[] m_handOffset = new Quaternion[2];
        private readonly bool[] m_handCalibrated = new bool[2];
        private readonly Transform[][] m_fingerBones = new Transform[2][];
        private readonly Quaternion[][] m_fingerRest = new Quaternion[2][];
        private readonly Vector3[][] m_fingerBendAxis = new Vector3[2][];
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

            // Find the arm bones first (needed to measure reach before recording lengths).
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
                }
            }

            // Auto-scale the whole rig so its arm reach == TargetArmReach. This corrects a mis-scaled
            // FBX (e.g. a Blender export in cm), which is what makes the elbow permanently straight.
            // Must happen BEFORE we record bone lengths (recorded from world-space distances below).
            if (ok)
            {
                var rawReach = Vector3.Distance(m_upperArm[1].position, m_foreArm[1].position)
                               + Vector3.Distance(m_foreArm[1].position, m_hand[1].position);
                if (rawReach > 1e-6f)
                {
                    m_modelRoot.localScale *= Mathf.Clamp(TargetArmReach / rawReach, 1e-4f, 10000f);
                }

                Debug.Log($"MixamoArmRig: raw arm reach {rawReach:F4} m -> scaled to ~{TargetArmReach:F2} m " +
                          $"(root scale {m_modelRoot.localScale.x:F4})");
            }

            m_chestLocalInRoot = m_modelRoot.InverseTransformPoint(m_chest.position);

            if (ok)
            {
                for (var i = 0; i < 2; i++)
                {
                    m_upperLen[i] = Vector3.Distance(m_upperArm[i].position, m_foreArm[i].position);
                    m_foreLen[i] = Vector3.Distance(m_foreArm[i].position, m_hand[i].position);
                    // Rest-pose local direction from each bone toward its child (axis-agnostic).
                    m_upperRestAxis[i] = m_upperArm[i].InverseTransformPoint(m_foreArm[i].position).normalized;
                    m_foreRestAxis[i] = m_foreArm[i].InverseTransformPoint(m_hand[i].position).normalized;

                    // All finger joints (everything under the Hand bone) + their rest rotations, plus a
                    // per-bone bend axis computed from the hand geometry so curl bends toward the palm.
                    // A single fixed local axis splays because Mixamo's per-bone orientations vary.
                    var fingers = new List<Transform>();
                    CollectDescendants(m_hand[i], fingers);
                    m_fingerBones[i] = fingers.ToArray();
                    m_fingerRest[i] = new Quaternion[fingers.Count];
                    m_fingerBendAxis[i] = new Vector3[fingers.Count];

                    // Estimate the palm normal from the knuckle fan (auto-mirrors for left/right).
                    var fingerPrefix = i == 0 ? "mixamorig:Left" : "mixamorig:Right";
                    var indexKnuckle = FindDeep(m_hand[i], fingerPrefix + "HandIndex1");
                    var middleKnuckle = FindDeep(m_hand[i], fingerPrefix + "HandMiddle1");
                    var pinkyKnuckle = FindDeep(m_hand[i], fingerPrefix + "HandPinky1");
                    var palmNormal = Vector3.up;
                    if (indexKnuckle != null && middleKnuckle != null && pinkyKnuckle != null)
                    {
                        var alongFingers = (middleKnuckle.position - m_hand[i].position).normalized;
                        var acrossKnuckles = (pinkyKnuckle.position - indexKnuckle.position).normalized;
                        var n = Vector3.Cross(alongFingers, acrossKnuckles);
                        if (n.sqrMagnitude > 1e-8f)
                        {
                            palmNormal = n.normalized;
                        }
                    }

                    for (var f = 0; f < fingers.Count; f++)
                    {
                        var bone = fingers[f];
                        m_fingerRest[i][f] = bone.localRotation;
                        if (bone.childCount > 0)
                        {
                            var dirToChild = bone.GetChild(0).position - bone.position;
                            var bendAxisWorld = Vector3.Cross(dirToChild.normalized, palmNormal);
                            m_fingerBendAxis[i][f] = bendAxisWorld.sqrMagnitude > 1e-8f
                                ? bone.InverseTransformDirection(bendAxisWorld.normalized)
                                : Vector3.zero;
                        }
                        else
                        {
                            m_fingerBendAxis[i][f] = Vector3.zero; // fingertip: no child, don't curl
                        }
                    }
                }
            }
            else
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
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, (l1 + l2) * MaxReachFraction);
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

            // Wrist: lock the hand's orientation to the controller (twists with your real wrist). The
            // offset is captured once from the hand's natural rest-following pose, so no hand-authored
            // angle is needed; if off => the hand just keeps following the forearm.
            if (MatchHandToController && m_hand[i] != null)
            {
                if (!m_handCalibrated[i])
                {
                    m_handOffset[i] = Quaternion.Inverse(anchor.rotation) * m_hand[i].rotation;
                    m_handCalibrated[i] = true;
                }

                m_hand[i].rotation = anchor.rotation * m_handOffset[i];
            }

            CurlFingers(i);
        }

        // Curls every finger joint from its rest pose toward a fist, proportional to the grip trigger,
        // so squeezing to grab visibly closes the hand.
        private void CurlFingers(int i)
        {
            var bones = m_fingerBones[i];
            if (bones == null || bones.Length == 0)
            {
                return;
            }

            var axes = m_fingerBendAxis[i];
            var grip = OVRInput.Get(i == 0 ? OVRInput.RawAxis1D.LHandTrigger : OVRInput.RawAxis1D.RHandTrigger);
            var amount = Mathf.Clamp01(grip) * FingerCurlAngle * FingerCurlSign;
            var rest = m_fingerRest[i];
            for (var f = 0; f < bones.Length; f++)
            {
                if (bones[f] == null)
                {
                    continue;
                }

                // Bend around each bone's palm-ward axis; fall back to the legacy fixed axis if unknown.
                var axis = axes != null && f < axes.Length && axes[f].sqrMagnitude > 1e-8f ? axes[f] : FingerCurlAxis;
                bones[f].localRotation = rest[f] * Quaternion.AngleAxis(amount, axis);
            }
        }

        private static void CollectDescendants(Transform parent, List<Transform> into)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                into.Add(child);
                CollectDescendants(child, into);
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
