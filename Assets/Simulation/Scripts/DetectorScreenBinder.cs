using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SimJam.BarrelSimulator
{
    // Drives the colleague's identiFINDER screen — a TextMeshPro number and a UI Slider bar — from
    // the live RadiationDetector reading, so the number and the slider move together: the number is
    // the radiation level and the slider fills as the player gets closer to / better aimed at the
    // source. This is the ONLY file that depends on TextMesh Pro / uGUI, which keeps RadiationDetector
    // UI-agnostic; it is added at runtime only when a detector prefab is used.
    public class DetectorScreenBinder : MonoBehaviour
    {
        private const float TickInterval = 0.1f;

        private RadiationDetector m_detector;
        private TMP_Text m_text;
        private Slider m_slider;
        private float m_tickTimer;

        // screenSource is the instantiated identiFINDER (the prefab instance) that owns the screen.
        public void Bind(RadiationDetector detector, GameObject screenSource)
        {
            m_detector = detector;
            if (screenSource != null)
            {
                m_text = screenSource.GetComponentInChildren<TMP_Text>(true);
                m_slider = screenSource.GetComponentInChildren<Slider>(true);
            }

            Refresh();
        }

        private void Update()
        {
            if (m_detector == null)
            {
                return;
            }

            m_tickTimer += Time.deltaTime;
            if (m_tickTimer < TickInterval)
            {
                return;
            }

            m_tickTimer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            if (m_detector == null)
            {
                return;
            }

            var cps = m_detector.SmoothedCps;

            if (m_text != null)
            {
                // The radiation level as a number (count rate); both lines stay on the small screen.
                var dose = RadiationField.CpsToMicroSvPerHour(cps);
                m_text.text = $"{cps:0} CPS\n{dose.ToString(dose >= 100f ? "0" : "0.00")} µSv/h";
            }

            if (m_slider != null)
            {
                // Same log-scaled 0..1 "warmth" level the detector uses, so the bar fills as the
                // reading climbs (i.e. as you close in on the unshielded source).
                var level = Mathf.Clamp01(
                    (Mathf.Log10(Mathf.Max(cps, 0.5f)) - Mathf.Log10(0.5f)) /
                    (Mathf.Log10(20000f) - Mathf.Log10(0.5f)));
                m_slider.SetValueWithoutNotify(level);
            }
        }
    }
}
