using System.Text;
using UnityEngine;

namespace SimJam.BarrelSimulator
{
    public class RadiationDetector : MonoBehaviour
    {
        private const float TickInterval = 0.1f;

        private OVRCameraRig m_rig;
        private TextMesh m_screenText;
        private Transform m_sensorTip;
        private GeigerAudio m_audio;
        private GrabbableTool m_grabTool;
        private Vector3 m_respawnPosition;
        private Quaternion m_respawnRotation;

        private System.Random m_random;
        private Rigidbody m_rigidbody;
        private StringBuilder m_stringBuilder;
        private float m_tickTimer;
        private float m_smoothedCps;
        private OVRInput.Controller m_lastVibratedController;
        private bool m_isVibrating;
        private bool m_initialized;

        public float SmoothedCps
        {
            get { return m_smoothedCps; }
        }

        private void Awake()
        {
            m_random = new System.Random(unchecked(System.Environment.TickCount * 17 + GetInstanceID()));
            m_rigidbody = GetComponent<Rigidbody>();
            m_stringBuilder = new StringBuilder(64);
        }

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

        private void OnDestroy()
        {
            if (m_grabTool != null)
            {
                m_grabTool.Released -= StopVibration;
            }
        }

        private void OnDisable()
        {
            StopVibration();
        }

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

            if (transform.position.y < -2f && (m_grabTool == null || !m_grabTool.IsHeld))
            {
                transform.SetPositionAndRotation(m_respawnPosition, m_respawnRotation);
                m_rigidbody.linearVelocity = Vector3.zero;
                m_rigidbody.angularVelocity = Vector3.zero;
            }
        }

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

            var level = Mathf.Clamp01((Mathf.Log10(Mathf.Max(SmoothedCps, 0.5f)) - Mathf.Log10(0.5f)) / (Mathf.Log10(20000f) - Mathf.Log10(0.5f)));
            var filled = Mathf.RoundToInt(level * 10f);
            m_stringBuilder.Append('|', filled);
            m_stringBuilder.Append('.', 10 - filled);

            m_screenText.text = m_stringBuilder.ToString();
        }

        private void UpdateHaptics()
        {
            if (m_grabTool == null || !m_grabTool.IsHeld || m_grabTool.HeldController == OVRInput.Controller.None)
            {
                StopVibration();
                return;
            }

            // Scale the haptic gate with the strongest registered source so vibration always
            // starts at roughly the same distance (~2 m aimed, unshielded) no matter how hot
            // the source rolled — a fixed CPS floor would let a strong source buzz the
            // controller from across both rooms, giving its location away.
            var floorCps = 15f;
            var sources = RadiationField.Sources;
            for (var i = 0; i < sources.Count; i++)
            {
                var source = sources[i];
                if (source != null)
                {
                    floorCps = Mathf.Max(floorCps, source.ActivityCpsAt1m * 0.25f);
                }
            }

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
