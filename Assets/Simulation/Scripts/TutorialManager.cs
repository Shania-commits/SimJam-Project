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
    public class TutorialManager : MonoBehaviour
    {
        [Serializable]
        public class TutorialStep
        {
            public string title;
            [TextArea(3, 8)] public string caption;
            public Sprite backgroundImage;
            public string nextButtonLabel = "Continue";
            public bool canGoBack = true;
            public bool canPause = true;
            public bool requireCompletionToContinue;
            public UnityEvent onStepStarted;
            public UnityEvent onStepCompleted;
        }

        [Header("Tutorial screens")]
        [SerializeField] private List<TutorialStep> m_steps = new();
        [SerializeField] private int m_startingStepIndex;

        [Header("Optional UI references")]
        [SerializeField] private Canvas m_canvas;
        [SerializeField] private RectTransform m_panelRoot;
        [SerializeField] private Image m_backgroundImage;
        [SerializeField] private TMP_Text m_titleText;
        [SerializeField] private TMP_Text m_captionText;
        [SerializeField] private TMP_Text m_stepCounterText;
        [SerializeField] private TMP_Text m_nextButtonLabelText;
        [SerializeField] private GameObject m_pausePanel;
        [SerializeField] private Button m_backButton;
        [SerializeField] private Button m_nextButton;
        [SerializeField] private Button m_pauseButton;
        [SerializeField] private Button m_resumeButton;

        [Header("UI art")]
        [SerializeField] private Sprite m_defaultCaptionBackgroundSprite;
        [SerializeField] private Sprite m_buttonSprite;

        [Header("VR panel placement")]
        [SerializeField] private bool m_createDefaultUiIfMissing = true;
        [SerializeField] private bool m_useWorldSpaceCanvas;
        [SerializeField] private bool m_keepPanelInFrontOfCamera = true;
        [SerializeField, Min(0.5f)] private float m_panelDistance = 2.2f;
        [SerializeField] private float m_panelVerticalOffset;
        [SerializeField] private Vector2 m_panelSize = new Vector2(960f, 540f);
        [SerializeField, Min(0.0005f)] private float m_worldSpaceScale = 0.00225f;

        [Header("Runtime controls")]
        [SerializeField] private bool m_pauseWithTimeScale = true;

        [Header("Mission hand-off")]
        // On the final step ("Begin Mission"), load the main game scene. The scene must be in
        // Build Settings (File > Build Settings) for LoadScene-by-name to work.
        [SerializeField] private bool m_loadMissionSceneOnFinish = true;
        [SerializeField] private string m_missionSceneName = "RadiationLabRoom";
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;

        private int m_currentStepIndex;
        private bool m_currentStepComplete;
        private bool m_isPaused;
        private float m_timeScaleBeforePause = 1f;
        private Transform m_panelFollowTarget;

        public int CurrentStepIndex => m_currentStepIndex;
        public bool IsPaused => m_isPaused;

        public bool IsCurrentStep(int stepIndex)
        {
            return m_currentStepIndex == stepIndex;
        }

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

        private void EnsureUiPointer()
        {
            if (FindAnyObjectByType<VrUiPointer>() == null)
            {
                gameObject.AddComponent<VrUiPointer>();
            }
        }

        private void LateUpdate()
        {
            if (m_useWorldSpaceCanvas && m_keepPanelInFrontOfCamera)
            {
                UpdateWorldPanelPose();
            }
        }

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

        public void GoToPreviousStep()
        {
            if (m_steps.Count == 0 || m_currentStepIndex <= 0 || !m_steps[m_currentStepIndex].canGoBack)
            {
                return;
            }

            m_currentStepIndex--;
            ShowCurrentStep();
        }

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

        public void CompleteStepIfCurrent(int stepIndex)
        {
            if (m_currentStepIndex == stepIndex)
            {
                CompleteCurrentStep();
            }
        }

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

            RefreshControls();
        }

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

            RefreshControls();
        }

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

        public void RestartTutorial()
        {
            ResumeTutorial();
            m_currentStepIndex = 0;
            ShowCurrentStep();
        }

        private void ShowCurrentStep()
        {
            m_currentStepComplete = false;

            if (m_steps.Count == 0)
            {
                SetText(m_titleText, string.Empty);
                SetText(m_captionText, string.Empty);
                SetText(m_stepCounterText, string.Empty);
                RefreshControls();
                return;
            }

            var step = m_steps[m_currentStepIndex];
            SetText(m_titleText, step.title);
            SetText(m_captionText, step.caption);
            SetText(m_stepCounterText, $"{m_currentStepIndex + 1}/{m_steps.Count}");
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
            RefreshControls();
        }

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

        private bool CurrentStepAllowsPause()
        {
            return m_steps.Count == 0 || m_steps[m_currentStepIndex].canPause;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
            {
                text.text = value;
            }
        }

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

        private static bool WasBackKeyPressed()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.backspaceKey.wasPressedThisFrame;
#else
            return false;
#endif
        }

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

        private void UpdateWorldPanelPose()
        {
            if (m_canvas == null)
            {
                return;
            }

            if ((m_panelFollowTarget == null || !m_panelFollowTarget.gameObject.activeInHierarchy) && Camera.main != null)
            {
                m_panelFollowTarget = Camera.main.transform;
            }

            if (m_panelFollowTarget == null)
            {
                m_canvas.transform.position = new Vector3(0f, 1.55f + m_panelVerticalOffset, 1.8f);
                m_canvas.transform.rotation = Quaternion.identity;
                return;
            }

            m_canvas.transform.position = m_panelFollowTarget.position
                + m_panelFollowTarget.forward * m_panelDistance
                + Vector3.up * m_panelVerticalOffset;
            m_canvas.transform.rotation = m_panelFollowTarget.rotation;
        }

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

        private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition)
        {
            return CreateButton(parent, objectName, label, anchorMin, anchorMax, anchoredPosition, new Vector2(230f, 72f), 26f);
        }

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
