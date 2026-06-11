using System;
using System.Reflection;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    internal static class MetaQuestHandVisuals
    {
        private const BindingFlags InstanceFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Material s_leftHandMaterial;
        private static Material s_rightHandMaterial;

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

        public static bool IsMetaHandVisual(GameObject visual)
        {
            return visual != null && visual.GetComponent<OVRHand>() != null;
        }

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
            renderer.sharedMaterial = isLeft ? LeftHandMaterial : RightHandMaterial;
            renderer.updateWhenOffscreen = false;

            root.AddComponent<OVRMeshRenderer>();
            root.SetActive(true);
            return root;
        }

        private static void ReplaceNonMetaVisual(ref GameObject visual)
        {
            if (visual == null || IsMetaHandVisual(visual))
            {
                return;
            }

            UnityEngine.Object.Destroy(visual);
            visual = null;
        }

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

        private static Material LeftHandMaterial => s_leftHandMaterial != null
            ? s_leftHandMaterial
            : s_leftHandMaterial = CreateHandMaterial("Meta Quest Left Hand Material", new Color(0.82f, 0.72f, 0.62f));

        private static Material RightHandMaterial => s_rightHandMaterial != null
            ? s_rightHandMaterial
            : s_rightHandMaterial = CreateHandMaterial("Meta Quest Right Hand Material", new Color(0.80f, 0.70f, 0.60f));

        private static Material CreateHandMaterial(string materialName, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

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
