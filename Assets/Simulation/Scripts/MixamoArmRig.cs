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
    /// <summary>
    /// Runtime IK driver for the player's visible arms: places a Mixamo-rigged arm mesh onto the OVR rig
    /// (chest anchored to the head, wrists tracking the controllers) so the held identiFINDER detector
    /// appears to be gripped by real, bending arms. No Animator — pose is solved analytically each frame.
    /// </summary>
    public class MixamoArmRig : MonoBehaviour
    {
        // Tunables — sensible defaults; expose-and-nudge on-device. 0 = left, 1 = right.
        // The rig is auto-scaled at init so its shoulder->wrist reach == TargetArmReach, which fixes a
        // mis-scaled FBX (the cause of an always-straight elbow: when reach < shoulder->controller
        // distance the cos-law solver pins the elbow to full extension). MaxReachFraction is a small
        // minimum-bend floor so a fully-stretched arm still reads slightly bent, never locked.
        /// <summary>Target shoulder-to-wrist reach in metres; the whole rig is auto-scaled at init so its
        /// measured arm length matches this, correcting a mis-scaled FBX that would otherwise lock the elbow.</summary>
        public float TargetArmReach = 0.58f;
        /// <summary>Fraction of full arm length the solver may reach before clamping, so a fully-extended arm
        /// still reads slightly bent instead of snapping to a locked straight line.</summary>
        [Range(0.85f, 0.999f)] public float MaxReachFraction = 0.99f;
        // Max distance the shoulder may slide toward the controller when the target is beyond arm reach,
        // so the wrist reaches the controller and a held tool stays in the hand instead of floating. This
        // translates only the upper-arm bone, so the continuous shoulder skin stretches a little at the
        // extreme -- kept small and gated to near-full extension (and the shoulder is usually below the VR
        // field of view). 0 disables shoulder stretch entirely.
        /// <summary>Max metres the shoulder may slide toward the controller when it is beyond arm reach, so the
        /// wrist still meets the controller and a held tool stays in the hand instead of floating; 0 disables it.</summary>
        public float MaxShoulderStretch = 0.1f;
        /// <summary>Offset from the headset to the estimated torso point (chest bone), in yaw-aligned body space,
        /// used to position the arm rig below and slightly behind the head.</summary>
        public Vector3 ChestOffsetFromHead = new Vector3(0f, -0.16f, -0.05f);
        /// <summary>Offset from the controller anchor to the actual wrist IK target, in controller-local space,
        /// so the hand sits naturally around the grip instead of exactly at the controller origin.</summary>
        public Vector3 WristTargetLocalOffset = new Vector3(0f, -0.01f, -0.045f);
        /// <summary>Elbow pole hint (in chest-yaw space) biasing the elbow out/down/back; its x is mirrored per side.</summary>
        public Vector3 ElbowPoleLocal = new Vector3(0.3f, -0.4f, -0.1f); // x mirrored per side, chest-yaw space
        /// <summary>Extra yaw added to the body facing; set 180 if the imported rig faces backward.</summary>
        public float BodyYawOffsetDegrees;                                // set 180 if the body faces backward
        // Hand twist: auto-calibrated at runtime so the wrist follows the controller without a
        // hand-authored offset (we capture the hand's orientation relative to the controller once).
        /// <summary>When true, the hand bone's orientation is locked to the controller so the wrist twists with
        /// the player's real wrist (using an auto-captured offset); when false the hand just follows the forearm.</summary>
        public bool MatchHandToController = true;
        // Fingers curl from rest toward a closed fist as the grip trigger is squeezed. The curl axis
        // is the per-finger-bone local bend axis — Mixamo's varies, so it is tunable if curl looks off.
        /// <summary>Maximum degrees each finger joint curls toward a fist at full grip squeeze.</summary>
        [Range(0f, 130f)] public float FingerCurlAngle = 70f;
        // Legacy fallback axis -- unused by the current knuckle-line curl (kept for Inspector compat).
        /// <summary>Legacy per-bone curl axis, unused by the current knuckle-line curl; kept for Inspector compatibility.</summary>
        public Vector3 FingerCurlAxis = new Vector3(0f, 0f, 1f);
        // Flip to -1 if the fingers curl backward (away from the palm) on a given FBX.
        /// <summary>Sign of the finger curl direction; flip to -1 if fingers curl away from the palm on a given FBX.</summary>
        public float FingerCurlSign = -1f;

        /// <summary>The OVR camera rig this arm rig reads head and controller poses from.</summary>
        private OVRCameraRig m_rig;
        /// <summary>Root transform of the instantiated arms model, moved each frame to place the body.</summary>
        private Transform m_modelRoot;
        /// <summary>The chest bone (mixamorig:Spine2 or a fallback) used as the body anchor point.</summary>
        private Transform m_chest;
        /// <summary>Chest position in model-root local space, cached so the root can be positioned to land the chest at its target.</summary>
        private Vector3 m_chestLocalInRoot;

        /// <summary>Upper-arm bones per side (0 = left, 1 = right).</summary>
        private readonly Transform[] m_upperArm = new Transform[2];
        /// <summary>Forearm bones per side (0 = left, 1 = right).</summary>
        private readonly Transform[] m_foreArm = new Transform[2];
        /// <summary>Hand bones per side (0 = left, 1 = right).</summary>
        private readonly Transform[] m_hand = new Transform[2];
        /// <summary>Recorded upper-arm (shoulder-to-elbow) bone length per side, used by the two-bone IK solver.</summary>
        private readonly float[] m_upperLen = new float[2];
        /// <summary>Recorded forearm (elbow-to-wrist) bone length per side, used by the two-bone IK solver.</summary>
        private readonly float[] m_foreLen = new float[2];
        /// <summary>Rest-pose local position of each upper-arm bone, restored every frame to undo the shoulder-stretch slide.</summary>
        private readonly Vector3[] m_upperRestLocalPos = new Vector3[2];
        /// <summary>Rest-pose local direction from each upper-arm bone toward its child, so aiming is axis-agnostic.</summary>
        private readonly Vector3[] m_upperRestAxis = new Vector3[2];
        /// <summary>Rest-pose local direction from each forearm bone toward its child, so aiming is axis-agnostic.</summary>
        private readonly Vector3[] m_foreRestAxis = new Vector3[2];
        /// <summary>Captured rotation offset from controller to hand bone, applied so the wrist tracks the controller.</summary>
        private readonly Quaternion[] m_handOffset = new Quaternion[2];
        /// <summary>Whether each hand's twist offset has been captured yet; cleared to force a recalibration.</summary>
        private readonly bool[] m_handCalibrated = new bool[2];
        /// <summary>All curlable finger joints under each hand bone.</summary>
        private readonly Transform[][] m_fingerBones = new Transform[2][];
        /// <summary>Rest-pose local rotation of each finger joint, the base pose the curl rotates away from.</summary>
        private readonly Quaternion[][] m_fingerRest = new Quaternion[2][];
        /// <summary>Per-finger-joint local hinge axis (the knuckle line) about which the joint curls; zero for joints left uncurled.</summary>
        private readonly Vector3[][] m_fingerBendAxis = new Vector3[2][];
        /// <summary>True once bones are found and the rig is set up; the solver no-ops until then.</summary>
        private bool m_ready;
        // One-shot recalibration shortly after init so the wrist twist offset is captured from a settled,
        // tracked pose instead of frame 1 (controllers can read a bad orientation before tracking warms up).
        /// <summary>Time at which the one-shot post-init hand recalibration fires; -1 once consumed or disabled.</summary>
        private float m_settleRecalibrateAt = -1f;

        /// <summary>True when the rig has found its bones and is actively solving arm poses.</summary>
        public bool IsReady => m_ready;

        // Re-capture the wrist twist offset on the next solve. Call when a grab changes the arm pose
        // so the hand stops twisting relative to the forearm (the offset is otherwise latched once).
        /// <summary>Forces both hands to re-capture their controller-to-wrist twist offset on the next solve;
        /// called after a grab changes the arm pose so the hand stops twisting relative to the forearm.</summary>
        public void RecalibrateHands()
        {
            m_handCalibrated[0] = false;
            m_handCalibrated[1] = false;
        }

        // The hand bone transform (0 = left, 1 = right) for fingertip-based interactions; may be null.
        /// <summary>Returns the hand bone transform for the requested side (for fingertip-based interactions);
        /// may be null if the rig failed to find its bones.</summary>
        public Transform GetHandBone(bool left) => m_hand[left ? 0 : 1];

        /// <summary>Binds this rig to the OVR camera rig and an instantiated arms model: locates the chest and
        /// arm/finger bones, auto-scales the model to the target reach, and records rest lengths/axes so the
        /// per-frame IK solver can run. Sets IsReady on success.</summary>
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
                    m_upperRestLocalPos[i] = m_upperArm[i].localPosition; // rest offset, to undo shoulder-stretch each frame
                    // Rest-pose local direction from each bone toward its child (axis-agnostic).
                    m_upperRestAxis[i] = m_upperArm[i].InverseTransformPoint(m_foreArm[i].position).normalized;
                    m_foreRestAxis[i] = m_foreArm[i].InverseTransformPoint(m_hand[i].position).normalized;

                    // All finger joints (everything under the Hand bone) + their rest rotations. Curl is a
                    // hinge about the KNUCKLE LINE (index->pinky), the natural finger-flex axis, captured in
                    // each bone's local frame so every joint flexes about the SAME world axis -> a clean
                    // fist, not a mangled splay (a per-bone cross-product axis was inconsistent AND pointed
                    // the wrong way). The thumb (different rest orientation) is left uncurled.
                    var fingers = new List<Transform>();
                    CollectDescendants(m_hand[i], fingers);
                    m_fingerBones[i] = fingers.ToArray();
                    m_fingerRest[i] = new Quaternion[fingers.Count];
                    m_fingerBendAxis[i] = new Vector3[fingers.Count];

                    // Knuckle line index->pinky; auto-mirrors L/R. +this axis curls toward the palm.
                    var fingerPrefix = i == 0 ? "mixamorig:Left" : "mixamorig:Right";
                    var indexKnuckle = FindDeep(m_hand[i], fingerPrefix + "HandIndex1");
                    var pinkyKnuckle = FindDeep(m_hand[i], fingerPrefix + "HandPinky1");
                    var hingeWorld = Vector3.zero;
                    if (indexKnuckle != null && pinkyKnuckle != null)
                    {
                        var across = pinkyKnuckle.position - indexKnuckle.position;
                        if (across.sqrMagnitude > 1e-8f)
                        {
                            hingeWorld = across.normalized;
                        }
                    }

                    for (var f = 0; f < fingers.Count; f++)
                    {
                        var bone = fingers[f];
                        m_fingerRest[i][f] = bone.localRotation;
                        // Curl only real finger joints: must have a child, a valid hinge, and not be a thumb.
                        if (bone.childCount > 0 && hingeWorld.sqrMagnitude > 1e-8f && !bone.name.Contains("Thumb"))
                        {
                            m_fingerBendAxis[i][f] = bone.InverseTransformDirection(hingeWorld);
                        }
                        else
                        {
                            m_fingerBendAxis[i][f] = Vector3.zero; // thumb / fingertip / no hinge: don't curl
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("MixamoArmRig: could not find expected mixamorig arm bones; IK disabled.");
            }

            m_ready = ok;
            if (ok)
            {
                m_settleRecalibrateAt = Time.time + 0.75f;
            }
        }

        /// <summary>Per-frame driver (after the OVR rig updates): re-anchors the body to the head, then solves
        /// each connected arm so the wrist reaches its controller. Runs in LateUpdate parent-before-child.</summary>
        private void LateUpdate()
        {
            if (!m_ready || m_rig == null || m_rig.centerEyeAnchor == null)
            {
                return;
            }

            // Re-capture the wrist twist once, after tracking has settled, so the hands don't lock a bad
            // frame-1 orientation (e.g. a hand pointing sideways before the controller is tracked).
            if (m_settleRecalibrateAt > 0f && Time.time >= m_settleRecalibrateAt)
            {
                m_settleRecalibrateAt = -1f;
                RecalibrateHands();
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

        /// <summary>Analytic two-bone IK for one arm: optionally slides the shoulder toward the controller when
        /// out of reach, solves the elbow via the law of cosines using the pole hint, aims the upper arm and
        /// forearm along their rest axes, matches the wrist to the controller, and curls the fingers.</summary>
        private void SolveArm(int i, float side, Transform anchor, Quaternion chestYaw)
        {
            // Undo any previous-frame shoulder-stretch slide FIRST: body placement moves the model root
            // but does NOT restore this bone's local position, so without this the slide would accumulate
            // frame-over-frame and latch the shoulder permanently out of socket.
            m_upperArm[i].localPosition = m_upperRestLocalPos[i];
            var shoulder = m_upperArm[i].position;                       // actual shoulder joint, post body-placement
            var target = anchor.position + anchor.rotation * WristTargetLocalOffset;
            var l1 = m_upperLen[i];
            var l2 = m_foreLen[i];
            var maxReach = (l1 + l2) * MaxReachFraction;

            // Stretch by SHOULDER TRANSLATION (VRIK / VRArmIK pattern): if the controller is farther than
            // the arm can reach, slide the shoulder (and the whole arm below it) out toward the target so
            // the wrist still meets the controller, instead of clamping the hand short -- which is what
            // detaches a held tool (it floats in the gap) and over-extends/contorts the arm. Bone lengths
            // are untouched (no bone scaling); the continuous shoulder skin stretches only slightly at the
            // very extreme, so the cap is kept small. Non-cumulative: the localPosition restore at the top
            // of SolveArm undoes the previous frame's slide before this recomputes it.
            var reachVector = target - shoulder;
            var reachDist = reachVector.magnitude;
            if (MaxShoulderStretch > 0f && reachDist > maxReach && reachDist > 1e-4f)
            {
                var slide = Mathf.Min(reachDist - maxReach, MaxShoulderStretch);
                m_upperArm[i].position += reachVector / reachDist * slide;
                shoulder = m_upperArm[i].position;
            }

            var toTarget = target - shoulder;
            var dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 1e-4f, maxReach);
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
        /// <summary>Curls the given hand's finger joints from rest toward a fist in proportion to the grip
        /// trigger, hinging each about its knuckle-line axis; uncurlable joints (thumb, fingertips) stay at rest.</summary>
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

                // Hinge each curlable joint about the knuckle line; zero-axis bones (thumb, fingertips)
                // stay at their rest pose so they never splay/mangle.
                var axis = axes != null && f < axes.Length ? axes[f] : Vector3.zero;
                if (axis.sqrMagnitude < 1e-8f)
                {
                    bones[f].localRotation = rest[f];
                    continue;
                }

                bones[f].localRotation = rest[f] * Quaternion.AngleAxis(amount, axis);
            }
        }

        /// <summary>Recursively gathers every descendant transform of the given parent (used to collect all
        /// finger joints under a hand bone).</summary>
        private static void CollectDescendants(Transform parent, List<Transform> into)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                into.Add(child);
                CollectDescendants(child, into);
            }
        }

        /// <summary>Rotates a bone so its recorded rest axis points along the desired world direction, without
        /// assuming which local axis runs down the bone; no-ops if the direction is degenerate.</summary>
        private static void AimBoneAxis(Transform bone, Vector3 localAxis, Vector3 desiredWorldDir)
        {
            if (desiredWorldDir.sqrMagnitude < 1e-10f)
            {
                return;
            }

            var currentWorldDir = bone.TransformDirection(localAxis);
            bone.rotation = Quaternion.FromToRotation(currentWorldDir, desiredWorldDir.normalized) * bone.rotation;
        }

        /// <summary>Depth-first search for a descendant transform by exact name (used to locate mixamorig bones);
        /// returns null if not found.</summary>
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
