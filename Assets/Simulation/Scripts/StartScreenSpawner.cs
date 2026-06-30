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
        [SerializeField] private string m_sceneToLoad = "TutorialRoom";

        [Header("Shania UI art (assign BackgroundUI_Wide + Button_White)")]
        [SerializeField] private Sprite m_backgroundSprite;
        [SerializeField] private Sprite m_buttonSprite;

        [Header("Placement")]
        [SerializeField] private Vector2 m_panelSize = new Vector2(960f, 540f);
        [SerializeField, Min(0.0005f)] private float m_worldSpaceScale = 0.00225f;
        [SerializeField] private float m_wallPanelHeight = 1.5f; // centre height of the wall-mounted menu
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;

        private Canvas m_canvas;
        private bool m_loading;
        private bool m_panelAnchored;
        private MovementMode m_selectedMode = MovementMode.Unset;
        private Button m_locomotionButton;
        private Button m_teleportButton;
        private Button m_continueButton;

        private void Start()
        {
            BuildPlayRoom();
            BuildUi();
            if (FindAnyObjectByType<VrUiPointer>() == null)
            {
                gameObject.AddComponent<VrUiPointer>();
            }

            if (FindAnyObjectByType<StartRoomLocomotion>() == null)
            {
                gameObject.AddComponent<StartRoomLocomotion>().Configure(new Vector2(4f, 4f));
            }
        }

        private void LateUpdate()
        {
            UpdatePanelPose();
        }

        private void Update()
        {
#if UNITY_EDITOR && ENABLE_INPUT_SYSTEM
            // Editor desktop fallback: 1 picks locomotion, 2 picks teleportation (no VR controller).
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) SelectMode(MovementMode.Smooth);
                if (Keyboard.current.digit2Key.wasPressedThisFrame) SelectMode(MovementMode.Teleport);
            }
#endif

            // Continue is via the controller laser clicking the gated Continue button (VrUiPointer); the
            // keyboard key is the editor desktop fallback only and also requires a mode pick first.
            if (!m_loading && m_selectedMode != MovementMode.Unset && WasStartKeyPressed())
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
            if (m_loading || m_selectedMode == MovementMode.Unset || string.IsNullOrWhiteSpace(m_sceneToLoad))
            {
                return;
            }

            MovementPreference.Save(m_selectedMode);
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

            var title = CreateText(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -64f), new Vector2(880f, 96f), 46f);
            title.text = m_titleText;

            var instructions = CreateText(panel, "Instructions", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -188f), new Vector2(880f, 150f), 28f);
            instructions.text = "Walk around this room and try both movement styles.\n" +
                                 "Left stick to walk  -  right INDEX (front) trigger to teleport.\n" +
                                 "Pick whichever feels best, then Continue.";

            m_locomotionButton = CreateButton(panel, "Locomotion Button", "Locomotion\n(walk)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-180f, -10f), new Vector2(330f, 120f), 30f);
            m_locomotionButton.onClick.AddListener(() => SelectMode(MovementMode.Smooth));

            m_teleportButton = CreateButton(panel, "Teleport Button", "Teleportation\n(trigger)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(180f, -10f), new Vector2(330f, 120f), 30f);
            m_teleportButton.onClick.AddListener(() => SelectMode(MovementMode.Teleport));

            m_continueButton = CreateButton(panel, "Continue Button", "Continue", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(330f, 92f), 38f);
            m_continueButton.onClick.AddListener(StartGame);
            m_continueButton.interactable = false;

            UpdatePanelPose();
        }

        private void UpdatePanelPose()
        {
            if (m_canvas == null || m_panelAnchored)
            {
                return;
            }

            // Mount the menu flat on the room's front (+Z) wall at eye height, readable from the room.
            // Fixed (no billboard/follow) so it can never end up in the floor. The room is built at world
            // origin with the +Z wall centre at z = +4, so sit the panel just in front of it. The canvas
            // forward points INTO the wall (+Z) so its readable face points back at the player.
            m_canvas.transform.SetPositionAndRotation(
                new Vector3(0f, m_wallPanelHeight, 3.9f),
                Quaternion.LookRotation(Vector3.forward, Vector3.up));
            m_panelAnchored = true;
        }

        private void SelectMode(MovementMode mode)
        {
            m_selectedMode = mode;
            if (m_continueButton != null)
            {
                m_continueButton.interactable = true;
            }

            TintModeButton(m_locomotionButton, mode == MovementMode.Smooth);
            TintModeButton(m_teleportButton, mode == MovementMode.Teleport);
        }

        private static void TintModeButton(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            var colors = button.colors;
            colors.normalColor = selected ? new Color(0.13f, 0.62f, 0.32f, 1f) : new Color(0.04f, 0.24f, 0.52f, 0.95f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
        }

        private void BuildPlayRoom()
        {
            var roomRoot = new GameObject("Start Play Room");
            // Build at WORLD origin, NOT under the spawner (which is offset in the scene): the rig starts
            // at the origin and StartRoomLocomotion clamps/teleports around the origin, so the room walls
            // must be centered there too -- otherwise the player walks/teleports straight through them.
            roomRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var floorMat = CreateRoomMaterial("Start Floor Mat", new Color(0.34f, 0.36f, 0.40f));
            var wallMat = CreateRoomMaterial("Start Wall Mat", new Color(0.46f, 0.48f, 0.52f));

            const float half = 4f;        // 8 x 8 m room
            const float wallHeight = 2.7f;
            const float wallThick = 0.15f;

            CreateRoomCube(roomRoot.transform, "Start Floor", new Vector3(0f, -0.05f, 0f), new Vector3(half * 2f, 0.1f, half * 2f), floorMat);
            CreateRoomCube(roomRoot.transform, "Start Wall North", new Vector3(0f, wallHeight * 0.5f, half), new Vector3(half * 2f, wallHeight, wallThick), wallMat);
            CreateRoomCube(roomRoot.transform, "Start Wall South", new Vector3(0f, wallHeight * 0.5f, -half), new Vector3(half * 2f, wallHeight, wallThick), wallMat);
            CreateRoomCube(roomRoot.transform, "Start Wall East", new Vector3(half, wallHeight * 0.5f, 0f), new Vector3(wallThick, wallHeight, half * 2f), wallMat);
            CreateRoomCube(roomRoot.transform, "Start Wall West", new Vector3(-half, wallHeight * 0.5f, 0f), new Vector3(wallThick, wallHeight, half * 2f), wallMat);

            if (FindAnyObjectByType<Light>() == null)
            {
                var lightObject = new GameObject("Start Room Light");
                lightObject.transform.SetParent(roomRoot.transform, false);
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var directional = lightObject.AddComponent<Light>();
                directional.type = LightType.Directional;
                directional.intensity = 1.1f;
                directional.color = new Color(1f, 0.97f, 0.92f);
            }
        }

        private static Material CreateRoomMaterial(string materialName, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = materialName, color = color };
            material.SetFloat("_Glossiness", 0.2f);
            return material;
        }

        private static void CreateRoomCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
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
