using System;
using System.Reflection;
using UnityEngine;

// =============================================================================
// MetaQuestHandVisuals.cs
//
// PURPOSE:  Static helper that builds and manages the authentic Meta Quest hand
//   meshes (OVRHand + OVRSkeleton + OVRMesh + SkinnedMeshRenderer + OVRMeshRenderer)
//   under the OVRCameraRig's hand anchors, so the player sees natural, controller-
//   driven hands while holding the identiFINDER detector. It forces the hand mesh
//   to stay rendered even when optical hand-tracking confidence drops (which is
//   exactly when a controller is in hand).
//
// HOW TO CUSTOMIZE:
//   NOTE: This is a static utility class, NOT a MonoBehaviour/[SerializeField]
//   component. NONE of these values live in the .unity scene files, so unlike most
//   scripts in this project you DO edit them here in C#. It has no Inspector
//   presence; a spawner calls TryEnsure(...) at runtime. The only exception is the
//   OVRManager settings poked in ConfigureControllerDrivenHands, which read/write a
//   live OVRManager in the scene at runtime.
//
//   - OverrideHandMaterial (public static property, this file): if the caller sets
//     it before calling TryEnsure, that material is used for BOTH hand meshes
//     instead of the code-built fallback skin. Assign it from the calling spawner
//     (e.g. to the SDK's Meta/Lit BasicHandMaterial). Leave null to keep the
//     fallback. Search callers of `MetaQuestHandVisuals` to find where to set it.
//   - scale (parameter of TryEnsure / CreateHand): overall hand-mesh scale, passed
//     in by the caller. Clamped to a floor of 0.1 in CreateHand
//     (localScale = Vector3.one * Mathf.Max(0.1f, scale)). Change the number the
//     caller passes; change the 0.1f floor in CreateHand.
//   - Fallback skin colours (HARDCODED, not serialized): edit the Color literals in
//     the LeftHandMaterial / RightHandMaterial properties near the bottom of this
//     file (currently new Color(0.82f,0.72f,0.62f) left and (0.80f,0.70f,0.60f)
//     right). Smoothness (0.35f) is set in CreateHandMaterial. These only apply when
//     OverrideHandMaterial is null.
//   - Always-render behavior (HARDCODED): CreateHand sets OVRMeshRenderer
//     _confidenceBehavior = None and _systemGestureBehavior = None, and OVRHand
//     m_showState = Always, so the mesh never hides when a controller is held.
//     Change these enum values in CreateHand if you want confidence-based hiding.
//   - Controller-driven poses (runtime scene state): ConfigureControllerDrivenHands
//     enables OVRManager.SimultaneousHandsAndControllersEnabled,
//     launchSimultaneousHandsControllersOnStartup, and
//     controllerDrivenHandPosesType = Natural on the live OVRManager, plus the
//     matching OVRPlugin calls. Edit that method to change pose sourcing.
//   - Shader is "Standard" (built-in pipeline) in CreateHandMaterial — do NOT swap
//     to a URP/Lit lookup here or the hands render magenta.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Builds and manages the authentic Meta Quest hand-mesh visuals (OVRHand/OVRSkeleton/OVRMesh)
    /// attached to the OVR camera rig's hand anchors, so the player sees natural, controller-driven
    /// hands while holding the identiFINDER detector. Uses reflection to set Meta SDK serialized
    /// fields and forces the hand mesh to stay rendered even when optical hand-tracking confidence
    /// drops (which happens whenever a controller is in hand).
    /// </summary>
    internal static class MetaQuestHandVisuals
    {
        /// <summary>Reflection binding flags used to reach both public and private instance fields on Meta SDK components.</summary>
        private const BindingFlags InstanceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        /// <summary>Cached fallback skin material for the left hand mesh, lazily created on first use.</summary>
        private static Material s_leftHandMaterial;
        /// <summary>Cached fallback skin material for the right hand mesh, lazily created on first use.</summary>
        private static Material s_rightHandMaterial;

        /// <summary>
        /// Optional material applied to both hand meshes instead of the code-built fallback skin.
        /// </summary>
        // When set (e.g. to the SDK's authentic Meta/Lit BasicHandMaterial), it is used for the
        // hand mesh instead of the code-built fallback skin material. Optional; null keeps the
        // fallback. Set by the caller before TryEnsure; leaving it null preserves prior behavior.
        public static Material OverrideHandMaterial { get; set; }

        /// <summary>
        /// Ensures both left and right Meta hand visuals exist under the rig's hand anchors, creating
        /// them if missing and replacing any non-Meta placeholder visuals. Also configures the rig for
        /// simultaneous controller-and-hand, controller-driven poses. Returns true if at least one
        /// hand visual is present after the call.
        /// </summary>
        public static bool TryEnsure(OVRCameraRig rig, ref GameObject leftHandVisual, ref GameObject rightHandVisual, float scale)
        {
            if (rig == null)
            {
                return false;
            }

            rig.EnsureGameObjectIntegrity();
            ConfigureControllerDrivenHands(rig);

            if (rig.leftHandAnchor != null)
            {
                ReplaceNonMetaVisual(ref leftHandVisual);
                if (leftHandVisual == null)
                {
                    leftHandVisual = CreateHand(rig.leftHandAnchor, true, scale);
                }
            }

            if (rig.rightHandAnchor != null)
            {
                ReplaceNonMetaVisual(ref rightHandVisual);
                if (rightHandVisual == null)
                {
                    rightHandVisual = CreateHand(rig.rightHandAnchor, false, scale);
                }
            }

            return leftHandVisual != null || rightHandVisual != null;
        }

        /// <summary>
        /// Returns true if the given visual is one of our Meta hand meshes (identified by the presence
        /// of an OVRHand component), as opposed to some other placeholder or custom arm rig.
        /// </summary>
        public static bool IsMetaHandVisual(GameObject visual)
        {
            return visual != null && visual.GetComponent<OVRHand>() != null;
        }

        /// <summary>
        /// Turns on Meta's simultaneous-hands-and-controllers mode with natural controller-driven hand
        /// poses, via both the OVRManager component and the lower-level OVRPlugin calls, so the hand
        /// mesh mimics the pose of the held controller (Quest home-screen style hands).
        /// </summary>
        private static void ConfigureControllerDrivenHands(OVRCameraRig rig)
        {
            var manager = rig.GetComponent<OVRManager>();
            if (manager == null)
            {
                manager = UnityEngine.Object.FindAnyObjectByType<OVRManager>();
            }

            if (manager != null)
            {
                manager.launchSimultaneousHandsControllersOnStartup = true;
                manager.SimultaneousHandsAndControllersEnabled = true;
                manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.Natural;
            }

            try
            {
                OVRPlugin.SetSimultaneousHandsAndControllersEnabled(true);
                OVRPlugin.SetControllerDrivenHandPoses(true);
                OVRPlugin.SetControllerDrivenHandPosesAreNatural(true);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Meta controller-driven hand pose setup was skipped: {exception.Message}");
            }
        }

        /// <summary>
        /// Builds a single Meta hand visual (OVRHand + OVRSkeleton + OVRMesh + SkinnedMeshRenderer +
        /// OVRMeshRenderer) parented to the given hand anchor, wired for the correct handedness, scale,
        /// and skin material, and configured to always render. Returns the new hand root GameObject.
        /// </summary>
        private static GameObject CreateHand(Transform handAnchor, bool isLeft, float scale)
        {
            var root = new GameObject(isLeft ? "Meta Quest Left Hand Visual" : "Meta Quest Right Hand Visual");
            root.SetActive(false);
            root.transform.SetParent(handAnchor, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);

            var hand = root.AddComponent<OVRHand>();
            SetSerializedField(hand, "HandType", isLeft ? OVRHand.Hand.HandLeft : OVRHand.Hand.HandRight);
            hand.m_showState = OVRInput.InputDeviceShowState.Always;

            var skeleton = root.AddComponent<OVRSkeleton>();
            SetSerializedField(skeleton, "_skeletonType", isLeft ? OVRSkeleton.SkeletonType.XRHandLeft : OVRSkeleton.SkeletonType.XRHandRight);
            SetSerializedField(skeleton, "_updateRootPose", false);
            SetSerializedField(skeleton, "_updateRootScale", true);
            SetSerializedField(skeleton, "_enablePhysicsCapsules", false);
            SetSerializedField(skeleton, "_applyBoneTranslations", true);

            var mesh = root.AddComponent<OVRMesh>();
            SetSerializedField(mesh, "_meshType", isLeft ? OVRMesh.MeshType.XRHandLeft : OVRMesh.MeshType.XRHandRight);

            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterial = OverrideHandMaterial != null
                ? OverrideHandMaterial
                : (isLeft ? LeftHandMaterial : RightHandMaterial);
            renderer.updateWhenOffscreen = false;

            var meshRenderer = root.AddComponent<OVRMeshRenderer>();
            // Keep the solid hand mesh rendered while controllers are held. The default
            // ToggleRenderer behavior disables the SkinnedMeshRenderer whenever optical hand
            // tracking confidence is not High (which is exactly when a controller is in hand),
            // so the real Meta hand mesh kept getting switched off and read as broken/"skeleton"
            // hands. None never hides it; the controller-driven poses configured above keep it
            // posed naturally — matching the Quest home-screen hands.
            SetSerializedField(meshRenderer, "_confidenceBehavior", OVRMeshRenderer.ConfidenceBehavior.None);
            SetSerializedField(meshRenderer, "_systemGestureBehavior", OVRMeshRenderer.SystemGestureBehavior.None);

            root.SetActive(true);
            return root;
        }

        /// <summary>
        /// Destroys and clears the referenced visual if it is not one of our Meta hand meshes, so a
        /// fresh Meta hand can be created in its place. Leaves existing Meta visuals untouched.
        /// </summary>
        private static void ReplaceNonMetaVisual(ref GameObject visual)
        {
            if (visual == null || IsMetaHandVisual(visual))
            {
                return;
            }

            UnityEngine.Object.Destroy(visual);
            visual = null;
        }

        /// <summary>
        /// Sets a serialized (public or private) instance field on a Meta SDK component by name using
        /// reflection, logging a warning if the field is not found. Used to configure fields the SDK
        /// only exposes through the inspector.
        /// </summary>
        private static void SetSerializedField<T>(T target, string fieldName, object value)
        {
            var field = typeof(T).GetField(fieldName, InstanceFieldFlags);
            if (field == null)
            {
                Debug.LogWarning($"Could not configure Meta hand field {typeof(T).Name}.{fieldName}.");
                return;
            }

            field.SetValue(target, value);
        }

        /// <summary>Lazily-created fallback skin material for the left hand mesh (warm skin tone).</summary>
        private static Material LeftHandMaterial => s_leftHandMaterial != null
            ? s_leftHandMaterial
            : s_leftHandMaterial = CreateHandMaterial("Meta Quest Left Hand Material", new Color(0.82f, 0.72f, 0.62f));

        /// <summary>Lazily-created fallback skin material for the right hand mesh (warm skin tone).</summary>
        private static Material RightHandMaterial => s_rightHandMaterial != null
            ? s_rightHandMaterial
            : s_rightHandMaterial = CreateHandMaterial("Meta Quest Right Hand Material", new Color(0.80f, 0.70f, 0.60f));

        /// <summary>
        /// Creates a simple Standard-shader skin material with the given name and colour for use as the
        /// hand mesh fallback when no override material is supplied.
        /// </summary>
        private static Material CreateHandMaterial(string materialName, Color color)
        {
            // Built-in pipeline project: use Standard. (A URP/Lit lookup that resolved here
            // would render magenta, so don't attempt it.)
            var shader = Shader.Find("Standard");

            var material = new Material(shader)
            {
                name = materialName,
                color = color
            };

            material.SetFloat("_Smoothness", 0.35f);
            return material;
        }
    }
}
