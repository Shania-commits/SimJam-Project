using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam.BarrelSimulator
{
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableTool : MonoBehaviour
    {
        public Vector3 HeldLocalPosition = new Vector3(0f, 0.01f, 0.04f);
        public Vector3 HeldLocalEuler = new Vector3(-35f, 0f, 0f);

        public bool IsHeld
        {
            get { return m_isHeld; }
        }

        public OVRInput.Controller HeldController
        {
            get { return m_heldController; }
        }

        public Transform HeldAnchor
        {
            get { return m_heldAnchor; }
        }

        public event System.Action Grabbed;
        public event System.Action Released;

        private const float k_releaseThreshold = 0.35f;

        private Collider m_collider;
        private Rigidbody m_rigidbody;
        private OVRCameraRig m_rig;
        private float m_grabRadius = 0.18f;
        private RigidbodyInterpolation m_savedInterpolation = RigidbodyInterpolation.Interpolate;
        private bool m_isHeld;
        private OVRInput.Controller m_heldController = OVRInput.Controller.None;
        private Transform m_heldAnchor;

        public void Initialize(OVRCameraRig rig, float grabRadius)
        {
            m_rig = rig;
            m_grabRadius = grabRadius;
        }

        private void Awake()
        {
            m_collider = GetComponent<Collider>();
            m_rigidbody = GetComponent<Rigidbody>();
            if (m_collider == null || m_rigidbody == null)
            {
                Debug.LogWarning("GrabbableTool requires a Collider and Rigidbody on the same GameObject. Disabling.", this);
                enabled = false;
            }
        }

        private void Update()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.gKey.wasPressedThisFrame)
            {
                if (m_isHeld && m_heldController == OVRInput.Controller.None)
                {
                    Release();
                    return;
                }
                if (!m_isHeld)
                {
                    MountToEditorCamera();
                    return;
                }
            }
#endif
            if (!m_isHeld)
            {
                if (m_rig == null)
                {
                    return;
                }
                if (TryGrab(m_rig.rightControllerAnchor, OVRInput.RawButton.RHandTrigger, OVRInput.Controller.RTouch))
                {
                    return;
                }
                if (TryGrab(m_rig.leftControllerAnchor, OVRInput.RawButton.LHandTrigger, OVRInput.Controller.LTouch))
                {
                    return;
                }
            }
            else if (m_heldController != OVRInput.Controller.None)
            {
                OVRInput.RawAxis1D axis = m_heldController == OVRInput.Controller.RTouch
                    ? OVRInput.RawAxis1D.RHandTrigger
                    : OVRInput.RawAxis1D.LHandTrigger;
                if (OVRInput.Get(axis) < k_releaseThreshold)
                {
                    Release();
                }
            }
        }

        private bool TryGrab(Transform anchor, OVRInput.RawButton button, OVRInput.Controller controller)
        {
            if (anchor == null || !OVRInput.GetDown(button))
            {
                return false;
            }
            Vector3 anchorPosition = anchor.position;
            Vector3 closestPoint = m_collider.ClosestPoint(anchorPosition);
            if (Vector3.Distance(anchorPosition, closestPoint) > m_grabRadius)
            {
                return false;
            }
            Grab(anchor, controller);
            return true;
        }

        private void Grab(Transform anchor, OVRInput.Controller controller)
        {
            // Interpolation extrapolates from physics poses and makes a hand-parented
            // kinematic body lag/jitter; suspend it while held.
            m_savedInterpolation = m_rigidbody.interpolation;
            m_rigidbody.interpolation = RigidbodyInterpolation.None;
            m_rigidbody.isKinematic = true;
            transform.SetParent(anchor, true);
            transform.localPosition = HeldLocalPosition;
            transform.localRotation = Quaternion.Euler(HeldLocalEuler);
            m_isHeld = true;
            m_heldController = controller;
            m_heldAnchor = anchor;
            if (Grabbed != null)
            {
                Grabbed.Invoke();
            }
        }

        private void Release()
        {
            transform.SetParent(null, true);
            if (m_rigidbody != null)
            {
                m_rigidbody.isKinematic = false;
                m_rigidbody.interpolation = m_savedInterpolation;
                if (m_heldController != OVRInput.Controller.None)
                {
                    Vector3 velocity = OVRInput.GetLocalControllerVelocity(m_heldController);
                    Vector3 angularVelocity = OVRInput.GetLocalControllerAngularVelocity(m_heldController);
                    Quaternion trackingRotation = (m_rig != null && m_rig.trackingSpace != null)
                        ? m_rig.trackingSpace.rotation
                        : Quaternion.identity;
                    m_rigidbody.linearVelocity = trackingRotation * velocity;
                    m_rigidbody.angularVelocity = trackingRotation * angularVelocity;
                }
                else
                {
                    m_rigidbody.linearVelocity = Vector3.zero;
                    m_rigidbody.angularVelocity = Vector3.zero;
                }
            }
            m_isHeld = false;
            m_heldController = OVRInput.Controller.None;
            m_heldAnchor = null;
            if (Released != null)
            {
                Released.Invoke();
            }
        }

#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
        private void MountToEditorCamera()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }
            Transform cameraTransform = mainCamera.transform;
            m_savedInterpolation = m_rigidbody.interpolation;
            m_rigidbody.interpolation = RigidbodyInterpolation.None;
            m_rigidbody.isKinematic = true;
            transform.SetParent(cameraTransform, true);
            transform.localPosition = new Vector3(0.18f, -0.12f, 0.35f);
            transform.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            m_isHeld = true;
            m_heldController = OVRInput.Controller.None;
            m_heldAnchor = cameraTransform;
            if (Grabbed != null)
            {
                Grabbed.Invoke();
            }
        }
#endif

        private void OnDisable()
        {
            // Do NOT reparent here: SetParent throws while the hierarchy is being
            // deactivated/destroyed. Just clear the held state and notify listeners
            // (e.g. so detector haptics stop).
            if (!m_isHeld)
            {
                return;
            }

            m_isHeld = false;
            m_heldController = OVRInput.Controller.None;
            m_heldAnchor = null;
            if (Released != null)
            {
                Released.Invoke();
            }
        }
    }
}
