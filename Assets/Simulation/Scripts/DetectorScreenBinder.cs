using TMPro;
using UnityEngine;
using UnityEngine.UI;

// =============================================================================
// DetectorScreenBinder.cs
//
// PURPOSE:  Runtime bridge that mirrors the handheld RadiationDetector's live
//   reading onto the identiFINDER's on-screen widgets: a TextMeshPro label
//   (count rate in CPS + converted dose in µSv/h) and a UI Slider "warmth" bar
//   that fills as the player closes in on the hidden radioactive barrel. This is
//   the only file that touches TextMesh Pro / uGUI, keeping RadiationDetector
//   itself UI-agnostic. It is added at runtime only when a detector prefab
//   supplies a screen (see the detector spawner that calls Bind()).
//
// HOW TO CUSTOMIZE:
//   This component has NO [SerializeField] fields, so nothing here is tuned via
//   the Unity Inspector or stored in a .unity scene file — every knob is a
//   hardcoded constant/literal in this C# file. Edit the values below directly:
//
//   - Refresh rate: TickInterval (const, top of class, 0.1f = 10 Hz). Controls how
//     often the screen redraws. Edit the const declaration. Update() throttles to it.
//
//   - Label text/format: edit the Refresh() method. The line
//       m_text.text = $"{cps:0} CPS\n{dose.ToString(...)} µSv/h";
//     controls the two-line readout. Change wording, the "0" number format, or the
//     100 µSv/h threshold that switches dose precision from "0.00" to "0" here.
//
//   - Dose conversion (CPS -> µSv/h): NOT in this file. It comes from
//     RadiationField.CpsToMicroSvPerHour(cps); edit that method in RadiationField.cs.
//
//   - Slider "warmth" curve: edit the Mathf.Clamp01(...) block in Refresh(). The
//     magic numbers 0.5f (floor CPS) and 20000f (CPS that fully fills the bar) set
//     the log10 range; change them to make the bar fill sooner/later. All hardcoded.
//
//   - Which widgets get driven: Bind() auto-finds the FIRST TMP_Text and FIRST
//     Slider under the identiFINDER prefab instance via GetComponentInChildren.
//     To retarget, change the detector prefab's child hierarchy (in the prefab
//     asset), not this script.
// =============================================================================

namespace SimJam.BarrelSimulator
{
    // Drives the colleague's identiFINDER screen — a TextMeshPro number and a UI Slider bar — from
    // the live RadiationDetector reading, so the number and the slider move together: the number is
    // the radiation level and the slider fills as the player gets closer to / better aimed at the
    // source. This is the ONLY file that depends on TextMesh Pro / uGUI, which keeps RadiationDetector
    // UI-agnostic; it is added at runtime only when a detector prefab is used.
    /// <summary>
    /// Runtime bridge that mirrors a <see cref="RadiationDetector"/>'s live reading onto the
    /// identiFINDER's on-screen widgets: a TextMeshPro count-rate/dose label and a UI Slider "warmth"
    /// bar that fills as the player closes in on the hidden radioactive barrel. Isolates all TMP/uGUI
    /// dependencies here so the detector model stays UI-agnostic; attached at runtime only when a
    /// detector prefab supplies a screen.
    /// </summary>
    public class DetectorScreenBinder : MonoBehaviour
    {
        /// <summary>Seconds between screen refreshes (10 Hz), matching the detector's sampling cadence.</summary>
        private const float TickInterval = 0.1f;

        /// <summary>The detector whose smoothed CPS reading drives the screen.</summary>
        private RadiationDetector m_detector;
        /// <summary>Cached TextMeshPro label showing the numeric count rate and dose.</summary>
        private TMP_Text m_text;
        /// <summary>Cached UI slider used as the log-scaled proximity/"warmth" bar.</summary>
        private Slider m_slider;
        /// <summary>Accumulates frame time so the screen updates only once per <see cref="TickInterval"/>.</summary>
        private float m_tickTimer;

        /// <summary>
        /// Wires this binder to a detector and locates the TMP label + Slider inside the given
        /// identiFINDER instance, then does an immediate refresh so the screen shows a value right away.
        /// </summary>
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

        /// <summary>Per-frame tick that throttles refreshes to <see cref="TickInterval"/> once a detector is bound.</summary>
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

        /// <summary>
        /// Reads the detector's smoothed CPS and pushes it to the screen: the label shows the count
        /// rate plus a converted dose in µSv/h, and the slider is set to a log-scaled 0..1 level that
        /// grows as the reading climbs toward the unshielded source.
        /// </summary>
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
