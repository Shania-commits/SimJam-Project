using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam.Tutorial
{
    /// <summary>
    /// Drives the guided TutorialRoom: owns the ordered list of tutorial steps, builds a world-space
    /// caption panel (title / body / step counter / Continue-Previous-Pause buttons) at runtime,
    /// advances/rewinds between steps, gates the interactive steps until the player completes them,
    /// plays per-step narration, and on the final step hands off to the main mission scene with a fade.
    /// Interactive controllers (walk-to-marker, teleport, grab-and-read) call CompleteStepIfCurrent to
    /// unlock a gated step.
    /// </summary>
    public class TutorialManager : MonoBehaviour
    {
        /// <summary>
        /// One screen of the tutorial: its title/caption text, optional background art and narration
        /// clip, the label shown on the advance button, whether the player may go back or pause here,
        /// whether continuing is gated on an interactive completion, and events fired on start/complete.
        /// </summary>
        [Serializable]
        public class TutorialStep
        {
            /// <summary>Heading shown at the top of the caption panel for this step.</summary>
            public string title;
            /// <summary>Body text describing what the player should read or do on this step.</summary>
            [TextArea(3, 8)] public string caption;
            /// <summary>Optional full-panel background image; falls back to the default caption sprite/color.</summary>
            public Sprite backgroundImage;
            /// <summary>Optional voiceover clip played when this step starts.</summary>
            public AudioClip narrationClip;
            /// <summary>Text on the advance button (e.g. "Continue", "Begin Mission").</summary>
            public string nextButtonLabel = "Continue";
            /// <summary>Whether the Previous button is allowed on this step.</summary>
            public bool canGoBack = true;
            /// <summary>Whether the player may pause while on this step.</summary>
            public bool canPause = true;
            /// <summary>If true, the player must complete an interactive task before Continue unlocks.</summary>
            public bool requireCompletionToContinue;
            /// <summary>Invoked when this step becomes the current step (used to arm interactive tasks).</summary>
            public UnityEvent onStepStarted;
            /// <summary>Invoked when this step is completed or advanced past.</summary>
            public UnityEvent onStepCompleted;
        }

        [Header("Tutorial screens")]
        /// <summary>Ordered list of tutorial steps; if left empty a default set is added at Awake.</summary>
        [SerializeField] private List<TutorialStep> m_steps = new();
        /// <summary>Step index to open on first show (clamped to the valid range).</summary>
        [SerializeField] private int m_startingStepIndex;

        [Header("Optional UI references")]
        /// <summary>Canvas hosting the caption panel; created automatically if not assigned.</summary>
        [SerializeField] private Canvas m_canvas;
        /// <summary>Root rect of the caption panel that holds all the child widgets.</summary>
        [SerializeField] private RectTransform m_panelRoot;
        /// <summary>Full-panel background image swapped per step.</summary>
        [SerializeField] private Image m_backgroundImage;
        /// <summary>Text element showing the current step's title.</summary>
        [SerializeField] private TMP_Text m_titleText;
        /// <summary>Text element showing the current step's body caption.</summary>
        [SerializeField] private TMP_Text m_captionText;
        /// <summary>Text element showing "position/total" over the visible (non-skipped) steps.</summary>
        [SerializeField] private TMP_Text m_stepCounterText;
        /// <summary>Label on the advance button, updated to each step's nextButtonLabel.</summary>
        [SerializeField] private TMP_Text m_nextButtonLabelText;
        /// <summary>Overlay panel shown while paused.</summary>
        [SerializeField] private GameObject m_pausePanel;
        /// <summary>Button that returns to the previous step.</summary>
        [SerializeField] private Button m_backButton;
        /// <summary>Button that advances to the next step (or begins the mission on the last step).</summary>
        [SerializeField] private Button m_nextButton;
        /// <summary>Button that toggles the pause overlay.</summary>
        [SerializeField] private Button m_pauseButton;
        /// <summary>Button on the pause overlay that resumes the tutorial.</summary>
        [SerializeField] private Button m_resumeButton;

        [Header("UI art")]
        /// <summary>Fallback background sprite used when a step has no backgroundImage.</summary>
        [SerializeField] private Sprite m_defaultCaptionBackgroundSprite;
        /// <summary>Sprite applied to runtime-created buttons; drives their tint scheme when null.</summary>
        [SerializeField] private Sprite m_buttonSprite;

        [Header("Narration")]
        /// <summary>Audio source used for per-step voiceover; auto-created if missing.</summary>
        [SerializeField] private AudioSource m_narrationAudioSource;
        /// <summary>Whether to start a step's narration clip automatically when the step opens.</summary>
        [SerializeField] private bool m_playNarrationOnStepStart = true;

        [Header("VR panel placement")]
        /// <summary>If true, build the default caption UI at runtime when references are missing.</summary>
        [SerializeField] private bool m_createDefaultUiIfMissing = true;
        /// <summary>Use a world-space canvas (VR) instead of a screen-space overlay (flat desktop).</summary>
        [SerializeField] private bool m_useWorldSpaceCanvas;
        /// <summary>If true, place the world panel relative to the camera on first show.</summary>
        [SerializeField] private bool m_keepPanelInFrontOfCamera = true;
        /// <summary>Unused legacy follow distance for a head-locked panel (kept for inspector data).</summary>
        [SerializeField, Min(0.5f)] private float m_panelDistance = 2.2f;
        // Distance out to anchor the panel as a flat screen on the wall the player faces at spawn.
        /// <summary>Distance out to anchor the panel as a flat screen on the wall the player faces at spawn.</summary>
        [SerializeField, Min(0.5f)] private float m_panelWallDistance = 3.2f;
        /// <summary>Vertical offset applied to the anchored world panel height.</summary>
        [SerializeField] private float m_panelVerticalOffset;
        /// <summary>Pixel size of the panel rect (also the canvas reference size).</summary>
        [SerializeField] private Vector2 m_panelSize = new Vector2(960f, 540f);
        /// <summary>Local scale applied to the world-space canvas to shrink pixels to metres.</summary>
        [SerializeField, Min(0.0005f)] private float m_worldSpaceScale = 0.00225f;

        [Header("Runtime controls")]
        /// <summary>If true, pausing sets Time.timeScale to 0 (and restores it on resume).</summary>
        [SerializeField] private bool m_pauseWithTimeScale = true;

        [Header("Mission hand-off")]
        // On the final step ("Begin Mission"), load the main game scene. The scene must be in
        // Build Settings (File > Build Settings) for LoadScene-by-name to work.
        /// <summary>Whether finishing the tutorial loads the mission scene (vs. just ending).</summary>
        [SerializeField] private bool m_loadMissionSceneOnFinish = true;
        /// <summary>Name of the main game scene to load when the tutorial finishes.</summary>
        [SerializeField] private string m_missionSceneName = "RadiationLabRoom";
        /// <summary>Duration of the fade-to-black before the mission scene loads.</summary>
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;

        /// <summary>Index of the step currently being shown.</summary>
        private int m_currentStepIndex;
        // Steps hidden for the chosen movement mode (the unused locomotion/teleport slide). They stay
        // in the list so every other step index is unaffected; navigation + the counter skip over them.
        /// <summary>Indices of steps hidden for the chosen movement mode; navigation and the counter skip them.</summary>
        private readonly HashSet<int> m_skippedSteps = new();
        /// <summary>Whether the current gated step's interactive task has been completed.</summary>
        private bool m_currentStepComplete;
        /// <summary>Whether the tutorial is currently paused.</summary>
        private bool m_isPaused;
        /// <summary>Time.timeScale captured on pause so it can be restored on resume.</summary>
        private float m_timeScaleBeforePause = 1f;
        /// <summary>Transform (usually the main camera) used to place the world panel on first show.</summary>
        private Transform m_panelFollowTarget;
        /// <summary>Set once the world panel has been anchored so it stops re-positioning.</summary>
        private bool m_panelAnchored;

        /// <summary>Index of the step currently displayed.</summary>
        public int CurrentStepIndex => m_currentStepIndex;
        /// <summary>Whether the tutorial is paused.</summary>
        public bool IsPaused => m_isPaused;

        /// <summary>Returns true if the given step index is the one currently being shown.</summary>
        public bool IsCurrentStep(int stepIndex)
        {
            return m_currentStepIndex == stepIndex;
        }

        /// <summary>
        /// Seeds default steps if none authored, builds the default UI, wires buttons, clamps to the
        /// starting step, shows it, and ensures a VrUiPointer exists so the laser can click the panel.
        /// </summary>
        private void Awake()
        {
            if (m_steps.Count == 0)
            {
                AddStarterSteps();
            }

            if (m_createDefaultUiIfMissing)
            {
                EnsureDefaultUi();
            }

            WireButtons();
            m_currentStepIndex = Mathf.Clamp(m_startingStepIndex, 0, Mathf.Max(0, m_steps.Count - 1));
            ShowCurrentStep();
            EnsureUiPointer();
        }

        /// <summary>Adds the controller laser-pointer clicker if one is not already in the scene.</summary>
        private void EnsureUiPointer()
        {
            if (FindAnyObjectByType<VrUiPointer>() == null)
            {
                gameObject.AddComponent<VrUiPointer>();
            }
        }

        /// <summary>Keeps the world-space panel positioned (anchored once) after transforms update.</summary>
        private void LateUpdate()
        {
            if (m_useWorldSpaceCanvas && m_keepPanelInFrontOfCamera)
            {
                UpdateWorldPanelPose();
            }
        }

        /// <summary>Handles pause toggling and the editor keyboard fallbacks for advancing/going back.</summary>
        private void Update()
        {
            if (WasPausePressed() && CurrentStepAllowsPause())
            {
                TogglePause();
            }

            // Forward/back is now driven by the controller laser pointer clicking the on-screen
            // Continue/Previous buttons (VrUiPointer). These keyboard keys are the editor desktop
            // fallback only. GoToNextStep still enforces gated-step completion either way.
            if (!m_isPaused)
            {
                if (WasAdvanceKeyPressed())
                {
                    GoToNextStep();
                }
                else if (WasBackKeyPressed())
                {
                    GoToPreviousStep();
                }
            }
        }

        /// <summary>
        /// Advances to the next visible step. Blocks while a gated step is incomplete, hands off to the
        /// mission when already on the last step, and skips any steps hidden for the movement mode.
        /// </summary>
        public void GoToNextStep()
        {
            if (m_steps.Count == 0)
            {
                return;
            }

            var currentStep = m_steps[m_currentStepIndex];
            if (currentStep.requireCompletionToContinue && !m_currentStepComplete)
            {
                return;
            }

            // On the last step the "next" button becomes "Begin Mission": hand off to the game.
            if (m_currentStepIndex >= m_steps.Count - 1)
            {
                FinishTutorial();
                return;
            }

            m_currentStepIndex++;
            // Skip the movement slide hidden for the chosen locomotion mode (never the final step).
            while (m_currentStepIndex < m_steps.Count - 1 && m_skippedSteps.Contains(m_currentStepIndex))
            {
                m_currentStepIndex++;
            }

            ShowCurrentStep();
        }

        /// <summary>
        /// Completes the tutorial and (optionally) loads the main mission scene. Wired to the
        /// final step's "Begin Mission" button via GoToNextStep.
        /// </summary>
        public void FinishTutorial()
        {
            if (m_steps.Count > 0)
            {
                m_steps[m_currentStepIndex].onStepCompleted?.Invoke();
            }

            StopNarration();

            if (!m_loadMissionSceneOnFinish || string.IsNullOrWhiteSpace(m_missionSceneName))
            {
                return;
            }

            // Make sure a paused timescale doesn't carry into the loaded scene.
            if (m_pauseWithTimeScale)
            {
                Time.timeScale = 1f;
            }

            StartCoroutine(FadeOutAndLoadMission());
        }

        /// <summary>
        /// Coroutine that fades the HMD view to black using OVRScreenFade (VR-correct, world-space quad
        /// on the camera) and then loads the mission scene.
        /// </summary>
        private System.Collections.IEnumerator FadeOutAndLoadMission()
        {
            // VR-correct fade: OVRScreenFade builds a world-space quad on the camera, so it renders
            // in the HMD (a ScreenSpaceOverlay fade would not). Fade to black, then load the mission.
            var fade = OVRScreenFade.instance;
            if (fade == null && Camera.main != null)
            {
                fade = Camera.main.gameObject.AddComponent<OVRScreenFade>();
                fade.fadeOnStart = false;
                // Wait one frame so the new component's Start() builds its fade mesh/material.
                yield return null;
            }

            if (fade != null)
            {
                fade.fadeTime = m_transitionFadeSeconds;
                fade.FadeOut();
                yield return new WaitForSecondsRealtime(m_transitionFadeSeconds);
            }

            SceneManager.LoadScene(m_missionSceneName);
        }

        /// <summary>
        /// Returns to the previous visible step if the current step allows going back, skipping any
        /// steps hidden for the movement mode.
        /// </summary>
        public void GoToPreviousStep()
        {
            if (m_steps.Count == 0 || m_currentStepIndex <= 0 || !m_steps[m_currentStepIndex].canGoBack)
            {
                return;
            }

            m_currentStepIndex--;
            while (m_currentStepIndex > 0 && m_skippedSteps.Contains(m_currentStepIndex))
            {
                m_currentStepIndex--;
            }

            ShowCurrentStep();
        }

        /// <summary>
        /// Marks the current step complete (unlocking its gated Continue button), fires its completed
        /// event, and refreshes the control states.
        /// </summary>
        public void CompleteCurrentStep()
        {
            if (m_steps.Count == 0)
            {
                return;
            }

            m_currentStepComplete = true;
            m_steps[m_currentStepIndex].onStepCompleted?.Invoke();
            RefreshControls();
        }

        /// <summary>
        /// Completes the given step only if it is the one currently shown. Called by the interactive
        /// tutorial controllers when the player finishes a gated task (walk, teleport, grab-and-read).
        /// </summary>
        public void CompleteStepIfCurrent(int stepIndex)
        {
            if (m_currentStepIndex == stepIndex)
            {
                CompleteCurrentStep();
            }
        }

        /// <summary>
        /// Pauses the tutorial (if the current step allows it): freezes time, pauses narration, and
        /// shows the pause overlay.
        /// </summary>
        public void PauseTutorial()
        {
            if (m_isPaused || !CurrentStepAllowsPause())
            {
                return;
            }

            m_isPaused = true;
            if (m_pauseWithTimeScale)
            {
                m_timeScaleBeforePause = Time.timeScale;
                Time.timeScale = 0f;
            }

            if (m_narrationAudioSource != null && m_narrationAudioSource.isPlaying)
            {
                m_narrationAudioSource.Pause();
            }

            RefreshControls();
        }

        /// <summary>Resumes from pause: restores time, un-pauses narration, and hides the pause overlay.</summary>
        public void ResumeTutorial()
        {
            if (!m_isPaused)
            {
                return;
            }

            m_isPaused = false;
            if (m_pauseWithTimeScale)
            {
                Time.timeScale = m_timeScaleBeforePause;
            }

            if (m_narrationAudioSource != null)
            {
                m_narrationAudioSource.UnPause();
            }

            RefreshControls();
        }

        /// <summary>Toggles between paused and resumed.</summary>
        public void TogglePause()
        {
            if (m_isPaused)
            {
                ResumeTutorial();
            }
            else
            {
                PauseTutorial();
            }
        }

        /// <summary>Resumes and jumps back to the first step.</summary>
        public void RestartTutorial()
        {
            ResumeTutorial();
            m_currentStepIndex = 0;
            ShowCurrentStep();
        }

        // Hide a step for the chosen movement mode (the unused locomotion/teleport slide). The step
        // stays in the list so every other step index is unaffected; navigation + the counter skip it.
        /// <summary>
        /// Marks a step as skipped (hidden) or visible for the chosen movement mode without altering the
        /// list, then refreshes the step counter. Skipped steps are jumped over by navigation.
        /// </summary>
        public void SetStepSkipped(int stepIndex, bool skipped)
        {
            if (stepIndex < 0)
            {
                return;
            }

            if (skipped)
            {
                m_skippedSteps.Add(stepIndex);
            }
            else
            {
                m_skippedSteps.Remove(stepIndex);
            }

            RefreshStepCounter();
        }

        /// <summary>Number of steps counted in the "x/total" readout (total minus skipped steps).</summary>
        private int VisibleStepCount()
        {
            return Mathf.Max(0, m_steps.Count - m_skippedSteps.Count);
        }

        /// <summary>1-based position of a step among the visible (non-skipped) steps, for the counter.</summary>
        private int VisiblePosition(int index)
        {
            var position = 0;
            for (var i = 0; i <= index && i < m_steps.Count; i++)
            {
                if (!m_skippedSteps.Contains(i))
                {
                    position++;
                }
            }

            return position;
        }

        /// <summary>Updates the "position/total" counter text for the current step.</summary>
        private void RefreshStepCounter()
        {
            if (m_steps.Count == 0)
            {
                return;
            }

            SetText(m_stepCounterText, $"{VisiblePosition(m_currentStepIndex)}/{VisibleStepCount()}");
        }

        /// <summary>
        /// Renders the current step: sets title/caption/counter/button label, applies the background
        /// sprite (or default), fires onStepStarted, plays narration, and refreshes control states.
        /// </summary>
        private void ShowCurrentStep()
        {
            m_currentStepComplete = false;

            if (m_steps.Count == 0)
            {
                SetText(m_titleText, string.Empty);
                SetText(m_captionText, string.Empty);
                SetText(m_stepCounterText, string.Empty);
                StopNarration();
                RefreshControls();
                return;
            }

            var step = m_steps[m_currentStepIndex];
            SetText(m_titleText, step.title);
            SetText(m_captionText, step.caption);
            RefreshStepCounter();
            SetText(m_nextButtonLabelText, string.IsNullOrWhiteSpace(step.nextButtonLabel) ? "Continue" : step.nextButtonLabel);

            if (m_backgroundImage != null)
            {
                var backgroundSprite = step.backgroundImage != null ? step.backgroundImage : m_defaultCaptionBackgroundSprite;
                m_backgroundImage.sprite = backgroundSprite;
                m_backgroundImage.color = backgroundSprite == null
                    ? new Color(0.03f, 0.035f, 0.035f, 0.94f)
                    : Color.white;
                m_backgroundImage.type = Image.Type.Simple;
            }

            step.onStepStarted?.Invoke();
            PlayNarration(step);
            RefreshControls();
        }

        /// <summary>Plays the given step's narration clip (if enabled and a clip exists).</summary>
        private void PlayNarration(TutorialStep step)
        {
            if (!m_playNarrationOnStepStart)
            {
                return;
            }

            EnsureNarrationAudioSource();
            if (m_narrationAudioSource == null)
            {
                return;
            }

            m_narrationAudioSource.Stop();
            m_narrationAudioSource.clip = step.narrationClip;
            if (step.narrationClip != null)
            {
                m_narrationAudioSource.Play();
            }
        }

        /// <summary>Stops any playing narration and clears the current clip.</summary>
        private void StopNarration()
        {
            if (m_narrationAudioSource == null)
            {
                return;
            }

            m_narrationAudioSource.Stop();
            m_narrationAudioSource.clip = null;
        }

        /// <summary>Lazily finds or adds a 2D AudioSource used for the tutorial voiceover.</summary>
        private void EnsureNarrationAudioSource()
        {
            if (m_narrationAudioSource != null)
            {
                return;
            }

            m_narrationAudioSource = GetComponent<AudioSource>();
            if (m_narrationAudioSource == null)
            {
                m_narrationAudioSource = gameObject.AddComponent<AudioSource>();
            }

            m_narrationAudioSource.playOnAwake = false;
            m_narrationAudioSource.loop = false;
            m_narrationAudioSource.spatialBlend = 0f; // 2D so voiceover is heard from anywhere in the room
        }

        /// <summary>
        /// Syncs the pause overlay and the Back/Next/Pause button visibility and interactability to the
        /// current step, pause state, and gated-completion state.
        /// </summary>
        private void RefreshControls()
        {
            if (m_pausePanel != null)
            {
                m_pausePanel.SetActive(m_isPaused);
            }

            if (m_backButton != null)
            {
                var showBackButton = m_steps.Count > 0 && m_currentStepIndex > 0 && m_steps[m_currentStepIndex].canGoBack;
                m_backButton.gameObject.SetActive(showBackButton);
                m_backButton.interactable = !m_isPaused && showBackButton;
                SetNextButtonPlacement(showBackButton);
            }

            if (m_nextButton != null)
            {
                var isLastStep = m_currentStepIndex >= m_steps.Count - 1;
                var isLocked = m_steps.Count > 0 && m_steps[m_currentStepIndex].requireCompletionToContinue && !m_currentStepComplete;
                // The last step stays interactable when it hands off to the mission ("Begin Mission").
                var lastStepFinishes = isLastStep && m_loadMissionSceneOnFinish;
                m_nextButton.interactable = !m_isPaused && !isLocked && (!isLastStep || lastStepFinishes);
            }

            if (m_pauseButton != null)
            {
                m_pauseButton.interactable = CurrentStepAllowsPause();
            }
        }

        /// <summary>
        /// Re-anchors the Next button: bottom-right when a Back button is present, centered otherwise.
        /// </summary>
        private void SetNextButtonPlacement(bool hasBackButton)
        {
            if (m_nextButton == null)
            {
                return;
            }

            var rectTransform = m_nextButton.GetComponent<RectTransform>();
            if (rectTransform == null)
            {
                return;
            }

            var anchor = hasBackButton ? new Vector2(1f, 0f) : new Vector2(0.5f, 0f);
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.anchoredPosition = hasBackButton ? new Vector2(-150f, 74f) : new Vector2(0f, 74f);
        }

        /// <summary>Whether the current step permits pausing (true when there are no steps).</summary>
        private bool CurrentStepAllowsPause()
        {
            return m_steps.Count == 0 || m_steps[m_currentStepIndex].canPause;
        }

        /// <summary>Null-safe helper that assigns text to a TMP element if it exists.</summary>
        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }

        /// <summary>Hooks the Back/Next/Pause/Resume buttons to their handlers (removed first to avoid dupes).</summary>
        private void WireButtons()
        {
            if (m_backButton != null)
            {
                m_backButton.onClick.RemoveListener(GoToPreviousStep);
                m_backButton.onClick.AddListener(GoToPreviousStep);
            }

            if (m_nextButton != null)
            {
                m_nextButton.onClick.RemoveListener(GoToNextStep);
                m_nextButton.onClick.AddListener(GoToNextStep);
                if (m_nextButtonLabelText == null)
                {
                    m_nextButtonLabelText = m_nextButton.GetComponentInChildren<TMP_Text>();
                }
            }

            if (m_pauseButton != null)
            {
                m_pauseButton.onClick.RemoveListener(TogglePause);
                m_pauseButton.onClick.AddListener(TogglePause);
            }

            if (m_resumeButton != null)
            {
                m_resumeButton.onClick.RemoveListener(ResumeTutorial);
                m_resumeButton.onClick.AddListener(ResumeTutorial);
            }
        }

        /// <summary>True the frame Escape is pressed (used to toggle pause).</summary>
        private bool WasPausePressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }

        // Editor desktop fallback so the tutorial can be driven without a VR controller: Space/Enter
        // advances, Backspace goes back. The world-space VR panel isn't mouse-clickable in flat Play
        // mode and OVRInput has no controller there, so these make flat desktop testing possible.
        /// <summary>Editor-only: true the frame Space/Enter is pressed, to advance a step on desktop.</summary>
        private static bool WasAdvanceKeyPressed()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            return Keyboard.current != null
                   && (Keyboard.current.spaceKey.wasPressedThisFrame
                       || Keyboard.current.enterKey.wasPressedThisFrame
                       || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
#else
            return false;
#endif
        }

        /// <summary>Editor-only: true the frame Backspace is pressed, to go back a step on desktop.</summary>
        private static bool WasBackKeyPressed()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>
        /// Populates the default 9-step training sequence (welcome, intro, overview, the three gated
        /// interactive steps, submit-a-reading, complete, and mission briefing) when none are authored.
        /// </summary>
        private void AddStarterSteps()
        {
            m_steps.Add(new TutorialStep
            {
                title = "Welcome",
                caption = "Welcome to the nuclear detection training simulation.\n\nA virtual reality training simulation.",
                nextButtonLabel = "Begin Your Training",
                canGoBack = false
            });
            m_steps.Add(new TutorialStep
            {
                title = "Introduction",
                caption = "Radiation detection equipment is used to inspect containers and identify radioactive materials.\n\nIn this simulation, you will learn how to use an IdentifINDER detector to inspect containers and identify elevated radiation levels."
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial Overview",
                caption = "Before beginning the mission, you will complete a brief tutorial covering:\n\n- Movement\n- Teleportation\n- Operating the detector\n- Understanding barrel readings"
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial: Movement",
                caption = "Push the LEFT thumbstick to walk through the environment, and push the RIGHT thumbstick left or right to snap-turn.\n\nWalk to the highlighted marker to continue.",
                requireCompletionToContinue = true
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial: Teleportation",
                caption = "Aim at the floor with your RIGHT controller and pull the RIGHT INDEX (front) trigger to teleport.\n\nTeleport to continue.",
                requireCompletionToContinue = true
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial: Detector Operations",
                caption = "Reach toward the IdentiFINDER and hold the GRIP (hand trigger) on either controller to pick it up.\n\nPoint it at a barrel until the reading climbs to continue.",
                requireCompletionToContinue = true
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial: Submit a Reading",
                caption = "Scan BOTH barrels with the detector. One reads far higher than the other.\n\nWalk up to each barrel, then aim at the HOT one (SUBMIT THIS) and press the A button (or pinch) to submit.",
                requireCompletionToContinue = true
            });
            m_steps.Add(new TutorialStep
            {
                title = "Tutorial Complete",
                caption = "You have completed the basic inspection tutorial.\n\nReview the mission briefing before entering the container inspection room.",
                nextButtonLabel = "Mission Briefing"
            });
            m_steps.Add(new TutorialStep
            {
                title = "Mission Briefing",
                caption = "You will have 3 minutes to inspect the containers using the IdentiFINDER detector.\n\nRadiation levels are randomized across each barrel in the room. Compare detector readings across all containers and determine which container exhibits the highest radiation level.",
                nextButtonLabel = "Begin Mission"
            });
        }

        /// <summary>
        /// Builds the caption panel and all its widgets (canvas, background, title/body/counter text,
        /// Back/Next/Pause buttons, and the pause overlay) at runtime for any references left unassigned.
        /// </summary>
        private void EnsureDefaultUi()
        {
            if (m_canvas == null)
            {
                var canvasObject = new GameObject("Tutorial Panel Canvas");
                canvasObject.transform.SetParent(transform, false);
                m_canvas = canvasObject.AddComponent<Canvas>();
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            ConfigureCanvas();
            var root = m_canvas.transform;

            if (m_panelRoot == null)
            {
                m_panelRoot = CreateRect(root, "Tutorial Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, m_panelSize);
                var panelImage = m_panelRoot.gameObject.AddComponent<Image>();
                panelImage.color = Color.clear;
                panelImage.raycastTarget = false;
            }

            if (m_backgroundImage == null)
            {
                var background = CreateRect(m_panelRoot, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
                background.offsetMin = Vector2.zero;
                background.offsetMax = Vector2.zero;
                m_backgroundImage = background.gameObject.AddComponent<Image>();
                m_backgroundImage.color = new Color(0.03f, 0.035f, 0.035f, 0.94f);
                m_backgroundImage.raycastTarget = false;
            }

            if (m_titleText == null)
            {
                m_titleText = CreateText(m_panelRoot, "Title Text", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -94f), new Vector2(760f, 64f), 42f, TextAlignmentOptions.Center);
            }

            if (m_captionText == null)
            {
                m_captionText = CreateText(m_panelRoot, "Body Text", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(720f, 230f), 24f, TextAlignmentOptions.Center);
            }

            if (m_stepCounterText == null)
            {
                m_stepCounterText = CreateText(m_panelRoot, "Step Counter", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-68f, -34f), new Vector2(110f, 36f), 18f, TextAlignmentOptions.Center);
            }

            if (m_backButton == null)
            {
                m_backButton = CreateButton(m_panelRoot, "Previous Button", "Previous", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(150f, 74f));
            }

            if (m_nextButton == null)
            {
                m_nextButton = CreateButton(m_panelRoot, "Continue Button", "Continue", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-150f, 74f));
                m_nextButtonLabelText = m_nextButton.GetComponentInChildren<TMP_Text>();
            }
            else if (m_nextButtonLabelText == null)
            {
                m_nextButtonLabelText = m_nextButton.GetComponentInChildren<TMP_Text>();
            }

            if (m_pauseButton == null)
            {
                m_pauseButton = CreateButton(m_panelRoot, "Pause Button", "Pause", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(120f, -58f), new Vector2(170f, 54f), 22f);
            }

            if (m_pausePanel == null)
            {
                m_pausePanel = CreatePausePanel(m_panelRoot);
            }
        }

        /// <summary>
        /// Sets the canvas render mode: screen-space overlay for flat desktop, or world-space (scaled to
        /// metres and anchored in the room) for VR.
        /// </summary>
        private void ConfigureCanvas()
        {
            var rectTransform = m_canvas.GetComponent<RectTransform>();
            rectTransform.sizeDelta = m_panelSize;

            if (!m_useWorldSpaceCanvas)
            {
                m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }

            m_canvas.renderMode = RenderMode.WorldSpace;
            m_canvas.transform.localScale = Vector3.one * m_worldSpaceScale;
            UpdateWorldPanelPose();
        }

        /// <summary>
        /// Anchors the world-space panel ONCE as a flat screen out on the wall the player faces at spawn
        /// (levelled to horizontal), then freezes it so it does not head-lock or swim.
        /// </summary>
        private void UpdateWorldPanelPose()
        {
            if (m_canvas == null || m_panelAnchored)
            {
                return;
            }

            if ((m_panelFollowTarget == null || !m_panelFollowTarget.gameObject.activeInHierarchy) && Camera.main != null)
            {
                m_panelFollowTarget = Camera.main.transform;
            }

            if (m_panelFollowTarget == null)
            {
                return;
            }

            // Anchor the panel ONCE as a flat screen out on the wall the player faces at spawn, then
            // freeze it (no head-lock swim). It stays put; the player can look around it freely.
            var flatForward = m_panelFollowTarget.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 1e-4f)
            {
                flatForward = Vector3.forward;
            }

            flatForward.Normalize();
            m_canvas.transform.position = m_panelFollowTarget.position
                + flatForward * m_panelWallDistance
                + Vector3.up * m_panelVerticalOffset;
            m_canvas.transform.rotation = Quaternion.LookRotation(flatForward, Vector3.up);
            m_panelAnchored = true;
        }

        /// <summary>Creates a child GameObject with a configured RectTransform and returns it.</summary>
        private static RectTransform CreateRect(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            var rectObject = new GameObject(objectName);
            rectObject.transform.SetParent(parent, false);

            var rectTransform = rectObject.AddComponent<RectTransform>();
            rectTransform.anchorMin = anchorMin;
            rectTransform.anchorMax = anchorMax;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = size;
            return rectTransform;
        }

        /// <summary>Creates a non-raycasting TextMeshPro UGUI text element under the given parent.</summary>
        private TMP_Text CreateText(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            var rectTransform = CreateRect(parent, objectName, anchorMin, anchorMax, anchoredPosition, size);
            var text = rectTransform.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Convenience overload that creates a button with the default size and font.</summary>
        private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition)
        {
            return CreateButton(parent, objectName, label, anchorMin, anchorMax, anchoredPosition, new Vector2(230f, 72f), 26f);
        }

        /// <summary>
        /// Creates a uGUI Button with the shared sprite/tint scheme and an auto-sizing centered label.
        /// </summary>
        private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, float maxFontSize)
        {
            var rectTransform = CreateRect(parent, objectName, anchorMin, anchorMax, anchoredPosition, size);

            var image = rectTransform.gameObject.AddComponent<Image>();
            image.sprite = m_buttonSprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;

            var button = rectTransform.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = m_buttonSprite == null ? new Color(0.04f, 0.24f, 0.52f, 0.95f) : Color.white;
            colors.highlightedColor = m_buttonSprite == null ? new Color(0.08f, 0.34f, 0.70f, 1f) : new Color(0.92f, 0.96f, 1f, 1f);
            colors.pressedColor = m_buttonSprite == null ? new Color(0.02f, 0.16f, 0.34f, 1f) : new Color(0.78f, 0.84f, 0.90f, 1f);
            colors.disabledColor = m_buttonSprite == null ? new Color(0.06f, 0.07f, 0.08f, 0.35f) : new Color(0.45f, 0.45f, 0.45f, 0.42f);
            button.colors = colors;

            var labelText = CreateText(rectTransform, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 20f, TextAlignmentOptions.Center);
            labelText.text = label;
            labelText.color = m_buttonSprite == null ? Color.white : new Color(0.03f, 0.035f, 0.04f, 1f);
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 16f;
            labelText.fontSizeMax = maxFontSize;
            labelText.margin = new Vector4(24f, 8f, 24f, 8f);

            return button;
        }

        /// <summary>
        /// Builds the darkened pause overlay ("Paused" title + Resume button), starts it hidden, and
        /// returns its GameObject.
        /// </summary>
        private GameObject CreatePausePanel(Transform parent)
        {
            var panel = CreateRect(parent, "Pause Panel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;

            var image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.74f);

            var title = CreateText(panel, "Paused Text", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 46f), new Vector2(360f, 54f), 38f, TextAlignmentOptions.Center);
            title.text = "Paused";

            m_resumeButton = CreateButton(panel, "Resume Button", "Resume", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -32f));
            panel.gameObject.SetActive(false);
            return panel.gameObject;
        }
    }
}
