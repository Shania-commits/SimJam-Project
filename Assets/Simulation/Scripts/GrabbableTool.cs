using UnityEngine;
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Proximity-grab behaviour for a hand-held tool (the identiFINDER detector and the door knob).
    /// When a controller's hand-trigger is pulled while the controller anchor is within the grab
    /// radius of this object's collider, the object parents to that controller's anchor and snaps to a
    /// fixed held pose; releasing the trigger re-enables physics and imparts the controller's throw
    /// velocity. Fires <see cref="Grabbed"/> / <see cref="Released"/> so gameplay (detector haptics,
    /// audio, door logic) can react. Requires a Collider and Rigidbody on the same GameObject.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    [RequireComponent(typeof(Rigidbody))]
    public class GrabbableTool : MonoBehaviour
    {
        // Held pose is aligned to the tool's GRIP, not its pivot. The detector's grip handle sits
        // ~7.5 cm below its body-centre pivot, and the Meta "Natural" fist closes at the controller
        // anchor, so we raise + pitch the object so the handle nests in the fist and the body rises
        // up-and-forward out of the top of the hand (flashlight/scanner hold). +55deg about X makes
        // the tool's +Y axis (its aim/measurement axis) point forward-and-up where the user aims,
        // and (0,0.040,0.065) is the inverse-grip offset that seats the handle in the fist.
        /// <summary>Local position, relative to the controller anchor, the tool snaps to while held so
        /// its grip handle nests in the fist. See the comment above for the derivation.</summary>
        public Vector3 HeldLocalPosition = new Vector3(0f, 0.040f, 0.065f);
        /// <summary>Local rotation (Euler degrees) applied to the held tool; +55deg pitch aims its
        /// measurement axis forward-and-up like a flashlight/scanner hold.</summary>
        public Vector3 HeldLocalEuler = new Vector3(55f, 0f, 0f);
        // Spin the held tool 180deg about its own aim axis (local Y) so its screen faces the player
        // instead of away. The aim/measurement direction is unchanged.
        /// <summary>When true, spins the held tool 180deg about its own aim axis so the screen faces
        /// the player instead of away (aim direction is unchanged).</summary>
        public bool FlipHeldAboutAim;

        /// <summary>True while the tool is currently grabbed (by a controller or the editor camera).</summary>
        public bool IsHeld
        {
            get { return m_isHeld; }
        }

        /// <summary>Which OVR controller is holding the tool, or <see cref="OVRInput.Controller.None"/>
        /// when not held by a controller (e.g. mounted to the editor camera).</summary>
        public OVRInput.Controller HeldController
        {
            get { return m_heldController; }
        }

        /// <summary>The anchor transform the tool is parented to while held, or null when not held.</summary>
        public Transform HeldAnchor
        {
            get { return m_heldAnchor; }
        }

        /// <summary>Raised the moment the tool is grabbed (used to start detector haptics/audio, etc.).</summary>
        public event System.Action Grabbed;
        /// <summary>Raised the moment the tool is released or forcibly dropped (stops haptics/audio).</summary>
        public event System.Action Released;

        // Hand-trigger analog value below which a held tool is considered let go.
        private const float k_releaseThreshold = 0.35f;

        private Collider m_collider;
        private Rigidbody m_rigidbody;
        private OVRCameraRig m_rig;
        private float m_grabRadius = 0.18f;
        private RigidbodyInterpolation m_savedInterpolation = RigidbodyInterpolation.Interpolate;
        private bool m_isHeld;
        private OVRInput.Controller m_heldController = OVRInput.Controller.None;
        private Transform m_heldAnchor;

        /// <summary>Wires the tool to the OVR camera rig (source of controller anchors/velocity) and
        /// sets how close a controller must be to the collider to grab. Called by the spawner at build time.</summary>
        public void Initialize(OVRCameraRig rig, float grabRadius)
        {
            m_rig = rig;
            m_grabRadius = grabRadius;
        }

        /// <summary>Caches the required Collider and Rigidbody; disables the component if either is missing.</summary>
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

        /// <summary>Per-frame grab loop: when free, tries the right then left controller for a proximity
        /// grab; when held by a controller, releases once the hand-trigger relaxes below the threshold.
        /// In the editor, the G key toggles mounting the tool to the main camera for headset-free testing.</summary>
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

        /// <summary>If the given hand-trigger was just pressed and the anchor is within the grab radius
        /// of this tool's collider, grabs it. Returns true if a grab happened.</summary>
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

        /// <summary>Parents the tool to the controller anchor, makes it kinematic, snaps it to the held
        /// pose, records the holding controller, and raises <see cref="Grabbed"/>.</summary>
        private void Grab(Transform anchor, OVRInput.Controller controller)
        {
            // Interpolation extrapolates from physics poses and makes a hand-parented
            // kinematic body lag/jitter; suspend it while held.
            m_savedInterpolation = m_rigidbody.interpolation;
            m_rigidbody.interpolation = RigidbodyInterpolation.None;
            m_rigidbody.isKinematic = true;
            transform.SetParent(anchor, true);
            transform.localPosition = HeldLocalPosition;
            transform.localRotation = FlipHeldAboutAim
                ? Quaternion.Euler(HeldLocalEuler) * Quaternion.Euler(0f, 180f, 0f)
                : Quaternion.Euler(HeldLocalEuler);
            m_isHeld = true;
            m_heldController = controller;
            m_heldAnchor = anchor;
            if (Grabbed != null)
            {
                Grabbed.Invoke();
            }
        }

        /// <summary>Unparents the tool, re-enables physics, and imparts the controller's tracked linear
        /// and angular velocity (rotated into world/tracking space) so it can be thrown, then raises
        /// <see cref="Released"/>.</summary>
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
        /// <summary>Editor-only convenience: attaches the tool to the main camera at a fixed offset so it
        /// can be tested without a headset or controllers. Held with no controller assigned.</summary>
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

        /// <summary>If the tool is disabled/destroyed while held, clears the held state and notifies
        /// listeners (e.g. to stop detector haptics) without reparenting, which is unsafe during teardown.</summary>
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
