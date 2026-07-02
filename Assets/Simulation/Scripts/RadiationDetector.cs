using System.Text;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    /// <summary>
    /// Runtime brain of the handheld identiFINDER survey meter. Each fixed tick it queries the shared
    /// <see cref="RadiationField"/> at the sensor tip, draws a Poisson-distributed count, smooths it into
    /// a CPS reading, then feeds that reading to the on-meter screen text, the geiger click audio, and the
    /// controller haptics. Also self-respawns the tool if it is dropped through the floor. Added at runtime
    /// by the scene spawner and configured via <see cref="Initialize"/>.
    /// </summary>
    public class RadiationDetector : MonoBehaviour
    {
        /// <summary>Fixed simulation step (seconds) at which a new count is sampled and the readout refreshed — 10 Hz.</summary>
        private const float TickInterval = 0.1f;

        /// <summary>The OVR camera rig (player head/hands); retained for context though not sampled directly here.</summary>
        private OVRCameraRig m_rig;
        /// <summary>The world-space text on the meter's screen showing CPS, dose rate, and a bar-graph.</summary>
        private TextMesh m_screenText;
        /// <summary>Transform at the meter's detector tip; the point the radiation field is sampled from.</summary>
        private Transform m_sensorTip;
        /// <summary>Synthesized geiger click train that is rate-driven by the current smoothed CPS.</summary>
        private GeigerAudio m_audio;
        /// <summary>Proximity grab component for the meter; supplies held state / holding controller and release events.</summary>
        private GrabbableTool m_grabTool;
        /// <summary>World position the meter snaps back to if it falls out of the world.</summary>
        private Vector3 m_respawnPosition;
        /// <summary>World rotation the meter snaps back to if it falls out of the world.</summary>
        private Quaternion m_respawnRotation;

        /// <summary>Per-instance RNG used for the Poisson count draw so each meter is statistically independent.</summary>
        private System.Random m_random;
        /// <summary>Cached rigidbody, zeroed on respawn to stop residual motion after a fall.</summary>
        private Rigidbody m_rigidbody;
        /// <summary>Reused buffer for composing the screen text without per-frame allocations.</summary>
        private StringBuilder m_stringBuilder;
        /// <summary>Accumulates elapsed time between ticks so <see cref="Tick"/> runs at a fixed 10 Hz.</summary>
        private float m_tickTimer;
        /// <summary>Exponentially-smoothed counts-per-second — the value shown, clicked, and buzzed.</summary>
        private float m_smoothedCps;
        /// <summary>Which controller was last sent a vibration command, so it can be silenced on release.</summary>
        private OVRInput.Controller m_lastVibratedController;
        /// <summary>True while a haptic vibration is currently being driven, guarding the stop call.</summary>
        private bool m_isVibrating;
        /// <summary>Gate that keeps <see cref="Update"/> idle until <see cref="Initialize"/> has run.</summary>
        private bool m_initialized;

        /// <summary>The current exponentially-smoothed counts-per-second reading; read by external stats / gating logic.</summary>
        public float SmoothedCps
        {
            get { return m_smoothedCps; }
        }

        /// <summary>Sets up the per-instance RNG (seeded from tick count and instance id), caches the rigidbody, and allocates the text buffer.</summary>
        private void Awake()
        {
            m_random = new System.Random(unchecked(System.Environment.TickCount * 17 + GetInstanceID()));
            m_rigidbody = GetComponent<Rigidbody>();
            m_stringBuilder = new StringBuilder(64);
        }

        /// <summary>
        /// Wires the detector to its scene dependencies (rig, screen, sensor tip, audio, grab tool) and stores its
        /// fall-recovery pose. Subscribes to the grab tool's Released event so haptics stop when the meter is dropped,
        /// then flips <see cref="m_initialized"/> so ticking begins.
        /// </summary>
        public void Initialize(OVRCameraRig rig, TextMesh screenText, Transform sensorTip, GeigerAudio audio, GrabbableTool grabTool, Vector3 respawnPosition, Quaternion respawnRotation)
        {
            m_rig = rig;
            m_screenText = screenText;
            m_sensorTip = sensorTip;
            m_audio = audio;
            m_grabTool = grabTool;
            m_respawnPosition = respawnPosition;
            m_respawnRotation = respawnRotation;

            if (m_grabTool != null)
            {
                m_grabTool.Released += StopVibration;
            }

            m_initialized = true;
        }

        /// <summary>Unsubscribes from the grab tool's Released event to avoid a dangling handler.</summary>
        private void OnDestroy()
        {
            if (m_grabTool != null)
            {
                m_grabTool.Released -= StopVibration;
            }
        }

        /// <summary>Ensures the controller stops buzzing if the meter is disabled while vibrating.</summary>
        private void OnDisable()
        {
            StopVibration();
        }

        /// <summary>Advances the fixed-rate simulation ticks, updates haptics, and respawns the meter if it drops below the world.</summary>
        private void Update()
        {
            if (!m_initialized)
            {
                return;
            }

            m_tickTimer += Time.deltaTime;
            while (m_tickTimer >= TickInterval)
            {
                m_tickTimer -= TickInterval;
                Tick();
            }

            UpdateHaptics();

            // If the meter has fallen out of the world and no one is holding it, snap it back to its start pose.
            if (transform.position.y < -2f && (m_grabTool == null || !m_grabTool.IsHeld))
            {
                transform.SetPositionAndRotation(m_respawnPosition, m_respawnRotation);
                m_rigidbody.linearVelocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>
        /// One 10 Hz sample: reads the mean CPS from the radiation field at the sensor tip (aimed along the tool's up
        /// axis), draws a Poisson count, converts to an instantaneous CPS and blends it into the smoothed reading, then
        /// drives the geiger click rate and rebuilds the on-screen CPS / dose-rate / bar-graph text.
        /// </summary>
        private void Tick()
        {
            var mean = RadiationField.GetMeanCps(m_sensorTip.position, transform.up, transform);
            var counts = RadiationField.SamplePoisson(mean * TickInterval, m_random);
            var instantCps = counts / TickInterval;
            m_smoothedCps = Mathf.Lerp(m_smoothedCps, instantCps, 0.16f);

            m_audio.SetRate(m_smoothedCps);

            m_stringBuilder.Length = 0;
            m_stringBuilder.Append(SmoothedCps.ToString("0"));
            m_stringBuilder.Append(" CPS");
            m_stringBuilder.Append('\n');
            var doseRate = RadiationField.CpsToMicroSvPerHour(SmoothedCps);
            m_stringBuilder.Append(doseRate.ToString(doseRate >= 100f ? "0" : "0.00"));
            m_stringBuilder.Append(" \u00B5Sv/h");
            m_stringBuilder.Append('\n');

            // Map CPS onto a 0..1 bar-graph on a log scale (0.5 CPS background floor up to 20000 CPS full-scale).
            var level = Mathf.Clamp01((Mathf.Log10(Mathf.Max(SmoothedCps, 0.5f)) - Mathf.Log10(0.5f)) / (Mathf.Log10(20000f) - Mathf.Log10(0.5f)));
            var filled = Mathf.RoundToInt(level * 10f);
            m_stringBuilder.Append('|', filled);
            m_stringBuilder.Append('.', 10 - filled);

            if (m_screenText != null)
            {
                m_screenText.text = m_stringBuilder.ToString();
            }
        }

        /// <summary>
        /// Buzzes the holding controller in proportion to the smoothed CPS, but only above a per-source scaled floor so a
        /// hot source can only be confirmed at close range (~0.7 m) rather than betrayed from across the room. Does nothing
        /// (and silences any buzz) when the meter is not held.
        /// </summary>
        private void UpdateHaptics()
        {
            if (m_grabTool == null || !m_grabTool.IsHeld || m_grabTool.HeldController == OVRInput.Controller.None)
            {
                StopVibration();
                return;
            }

            // Scale the haptic gate with the strongest registered source so vibration always
            // starts at roughly the same distance no matter how hot the source rolled — a fixed
            // CPS floor would let a strong source buzz the controller from across both rooms,
            // giving its location away. The 2.0x multiplier means the buzz only kicks in within
            // ~0.7 m aimed/unshielded (reading ~= activity / d^2), a tight confirmation radius
            // rather than a long-range hint.
            var floorCps = 15f;
            var sources = RadiationField.Sources;
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source != null)
                {
                    floorCps = Mathf.Max(floorCps, source.ActivityCpsAt1m * 2.0f);
                }
            }

            // Ramp intensity 0..1 across one decade (log10 span 1.30103) of CPS above the floor.
            var s = Mathf.Clamp01((Mathf.Log10(Mathf.Max(m_smoothedCps, 0.0001f)) - Mathf.Log10(floorCps)) / 1.30103f);
            if (s <= 0f)
            {
                StopVibration();
            }
            else
            {
                OVRInput.SetControllerVibration(0.3f + 0.7f * s, 0.15f + 0.85f * s, m_grabTool.HeldController);
                m_lastVibratedController = m_grabTool.HeldController;
                m_isVibrating = true;
            }
        }

        /// <summary>Silences any active controller vibration and clears the vibrating flag; safe to call repeatedly.</summary>
        private void StopVibration()
        {
            if (m_isVibrating)
            {
                OVRInput.SetControllerVibration(0f, 0f, m_lastVibratedController);
                m_isVibrating = false;
            }
        }
    }
}
