using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SimJam
{
    /// <summary>
    /// Builds a simple VR title screen at launch: a world-space panel (so it renders in the HMD,
    /// unlike a ScreenSpaceOverlay) with a background, a title, and a single "Start" button.
    /// Pressing Start — the controller A button, or clicking if a UI pointer exists — fades to
    /// black and loads the first gameplay scene. Mirrors the project's "one spawner builds
    /// everything in code" pattern and reuses the same world-space-canvas approach as TutorialManager.
    /// </summary>
    public class StartScreenSpawner : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private string m_titleText = "Radiation Detection Training";
        [SerializeField] private string m_startButtonLabel = "Start";
        [SerializeField] private string m_sceneToLoad = "TutorialRoom";

        [Header("Shania UI art (assign BackgroundUI_Wide + Button_White)")]
        [SerializeField] private Sprite m_backgroundSprite;
        [SerializeField] private Sprite m_buttonSprite;

        [Header("Placement")]
        [SerializeField, Min(0.5f)] private float m_panelDistance = 2.2f;
        [SerializeField] private float m_panelVerticalOffset;
        [SerializeField] private Vector2 m_panelSize = new Vector2(960f, 540f);
        [SerializeField, Min(0.0005f)] private float m_worldSpaceScale = 0.00225f;
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;

        private Canvas m_canvas;
        private Transform m_followTarget;
        private bool m_loading;

        private void Start()
        {
            BuildUi();
            if (FindAnyObjectByType<VrUiPointer>() == null)
            {
                gameObject.AddComponent<VrUiPointer>();
            }
        }

        private void LateUpdate()
        {
            UpdatePanelPose();
        }

        private void Update()
        {
            // Start is via the controller laser clicking the Start button (VrUiPointer); the keyboard
            // key is the editor desktop fallback only.
            if (!m_loading && WasStartKeyPressed())
            {
                StartGame();
            }
        }

        // Editor desktop fallback: Space/Enter starts, so the title screen can be tested flat without
        // a VR controller (the world-space button isn't mouse-clickable in flat Play mode).
        private static bool WasStartKeyPressed()
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

        public void StartGame()
        {
            if (m_loading || string.IsNullOrWhiteSpace(m_sceneToLoad))
            {
                return;
            }

            m_loading = true;
            StartCoroutine(FadeOutAndLoad());
        }

        private IEnumerator FadeOutAndLoad()
        {
            // VR-correct fade: OVRScreenFade builds a world-space quad on the camera so it renders
            // in the HMD. Fade to black, then load the first gameplay scene.
            var fade = OVRScreenFade.instance;
            if (fade == null && Camera.main != null)
            {
                fade = Camera.main.gameObject.AddComponent<OVRScreenFade>();
                fade.fadeOnStart = false;
                yield return null;
            }

            if (fade != null)
            {
                fade.fadeTime = m_transitionFadeSeconds;
                fade.FadeOut();
                yield return new WaitForSecondsRealtime(m_transitionFadeSeconds);
            }

            SceneManager.LoadScene(m_sceneToLoad);
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Start Screen Canvas");
            canvasObject.transform.SetParent(transform, false);
            m_canvas = canvasObject.AddComponent<Canvas>();
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();

            m_canvas.renderMode = RenderMode.WorldSpace;
            m_canvas.GetComponent<RectTransform>().sizeDelta = m_panelSize;
            m_canvas.transform.localScale = Vector3.one * m_worldSpaceScale;

            var panel = CreateRect(m_canvas.transform, "Start Panel", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, m_panelSize);

            var background = CreateRect(panel, "Background", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.offsetMin = Vector2.zero;
            background.offsetMax = Vector2.zero;
            var backgroundImage = background.gameObject.AddComponent<Image>();
            backgroundImage.sprite = m_backgroundSprite;
            backgroundImage.color = m_backgroundSprite != null ? Color.white : new Color(0.03f, 0.035f, 0.035f, 0.96f);
            backgroundImage.raycastTarget = false;

            var title = CreateText(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(820f, 130f), 52f);
            title.text = m_titleText;

            var button = CreateButton(panel, "Start Button", m_startButtonLabel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(320f, 96f), 40f);
            button.onClick.AddListener(StartGame);

            UpdatePanelPose();
        }

        private void UpdatePanelPose()
        {
            if (m_canvas == null)
            {
                return;
            }

            if ((m_followTarget == null || !m_followTarget.gameObject.activeInHierarchy) && Camera.main != null)
            {
                m_followTarget = Camera.main.transform;
            }

            if (m_followTarget == null)
            {
                m_canvas.transform.position = new Vector3(0f, 1.55f + m_panelVerticalOffset, m_panelDistance);
                m_canvas.transform.rotation = Quaternion.identity;
                return;
            }

            m_canvas.transform.position = m_followTarget.position
                + m_followTarget.forward * m_panelDistance
                + Vector3.up * m_panelVerticalOffset;
            m_canvas.transform.rotation = m_followTarget.rotation;
        }

        private static RectTransform CreateRect(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            var rectObject = new GameObject(objectName);
            rectObject.transform.SetParent(parent, false);
            var rect = rectObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static TMP_Text CreateText(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, float fontSize)
        {
            var rect = CreateRect(parent, objectName, anchorMin, anchorMax, anchoredPosition, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string objectName, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, float maxFontSize)
        {
            var rect = CreateRect(parent, objectName, anchorMin, anchorMax, anchoredPosition, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = m_buttonSprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;

            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = m_buttonSprite == null ? new Color(0.04f, 0.24f, 0.52f, 0.95f) : Color.white;
            colors.highlightedColor = m_buttonSprite == null ? new Color(0.08f, 0.34f, 0.70f, 1f) : new Color(0.92f, 0.96f, 1f, 1f);
            colors.pressedColor = m_buttonSprite == null ? new Color(0.02f, 0.16f, 0.34f, 1f) : new Color(0.78f, 0.84f, 0.90f, 1f);
            button.colors = colors;

            var labelText = CreateText(rect, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, maxFontSize);
            labelText.text = label;
            labelText.color = m_buttonSprite == null ? Color.white : new Color(0.03f, 0.035f, 0.04f, 1f);
            labelText.enableAutoSizing = true;
            labelText.fontSizeMin = 18f;
            labelText.fontSizeMax = maxFontSize;
            return button;
        }
    }
}
