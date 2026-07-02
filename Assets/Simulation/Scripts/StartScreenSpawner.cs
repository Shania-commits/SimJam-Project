using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// =============================================================================
// StartScreenSpawner.cs
//
// PURPOSE:  Builds the VR title/start screen at runtime: an 8x8 m play room plus a
//           wall-mounted world-space menu with a title, instructions, two movement-
//           mode buttons (walk vs. teleport), and a gated Continue button. Picking a
//           mode + Continue saves the movement preference, fades to black, and loads
//           the first gameplay scene. One MonoBehaviour builds everything in code.
//
// HOW TO CUSTOMIZE:
//   Most knobs below are [SerializeField], so their C# defaults are only fallbacks:
//   once this spawner lives on a GameObject in the start scene, the scene's .unity
//   YAML stores the actual values and OVERRIDES these defaults at runtime. To change
//   real behavior, find the start-screen scene under Assets/ (the scene whose spawner
//   loads "TutorialRoom"), select the GameObject holding StartScreenSpawner, and edit
//   the fields in the Inspector (or hand-edit the matching values in the scene YAML).
//     - m_titleText ("Radiation Detection Training"): heading on the panel.
//     - m_sceneToLoad ("TutorialRoom"): scene loaded after Continue. Change this to
//       point the start screen at a different next scene.
//     - m_backgroundSprite / m_buttonSprite: optional UI art (BackgroundUI_Wide,
//       Button_White); when empty the code falls back to flat dark/blue fills.
//     - m_panelSize (960x540) + m_worldSpaceScale (0.00225): design pixel size and
//       the world-units-per-pixel shrink factor for the world-space canvas.
//     - m_wallPanelHeight (1.5): eye height (m) the menu is mounted at on the wall.
//     - m_transitionFadeSeconds (0.6): duration of the fade-to-black before loading.
//     - m_startNarrationClip + m_playNarrationOnStart (true): "choose your movement"
//       voiceover and whether it auto-plays on load. Assign the clip in the Inspector.
//   HARDCODED (NOT serialized — edit the C# here, no Inspector knob):
//     - Instruction body text: literal strings in BuildUi().
//     - Button labels, positions, sizes, and font caps: the CreateButton/CreateText
//       calls in BuildUi(). Selected/unselected button tints: TintModeButton().
//     - Panel wall position (x=0, z=3.9) and facing: UpdatePanelPose().
//     - Play-room geometry (8x8 m, wall height 2.7, colours, light): BuildPlayRoom()
//       and CreateRoomMaterial(). Room is built at WORLD origin to match the rig.
//     - Editor-only desktop test keys (1/2 pick mode, Space/Enter start): Update() and
//       WasStartKeyPressed(); these compile out of device builds.
// =============================================================================

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
        /// <summary>Heading shown at the top of the title panel.</summary>
        [SerializeField] private string m_titleText = "Radiation Detection Training";
        /// <summary>Scene loaded once the player picks a movement style and confirms (defaults to the tutorial).</summary>
        [SerializeField] private string m_sceneToLoad = "TutorialRoom";

        [Header("Shania UI art (assign BackgroundUI_Wide + Button_White)")]
        /// <summary>Optional artwork for the panel background; falls back to a flat dark fill when unset.</summary>
        [SerializeField] private Sprite m_backgroundSprite;
        /// <summary>Optional artwork for the button faces; falls back to solid tint colours when unset.</summary>
        [SerializeField] private Sprite m_buttonSprite;

        [Header("Placement")]
        /// <summary>Design-space (pixel) dimensions of the world-space canvas before <see cref="m_worldSpaceScale"/> is applied.</summary>
        [SerializeField] private Vector2 m_panelSize = new Vector2(960f, 540f);
        /// <summary>World units per canvas pixel — shrinks the pixel-sized UI down to a readable real-world size.</summary>
        [SerializeField, Min(0.0005f)] private float m_worldSpaceScale = 0.00225f;
        /// <summary>Eye-height (metres) at which the menu is mounted on the front wall.</summary>
        [SerializeField] private float m_wallPanelHeight = 1.5f; // centre height of the wall-mounted menu
        /// <summary>Duration of the fade-to-black transition before the next scene loads.</summary>
        [SerializeField, Min(0f)] private float m_transitionFadeSeconds = 0.6f;

        [Header("Narration")]
        /// <summary>Voiceover that prompts the player to choose a movement style; plays once when the screen loads.</summary>
        [SerializeField] private AudioClip m_startNarrationClip; // "choose your movement" voiceover (auto-plays on load)
        /// <summary>Whether the narration clip auto-plays on load.</summary>
        [SerializeField] private bool m_playNarrationOnStart = true;

        /// <summary>The world-space canvas hosting the title, instructions, and buttons.</summary>
        private Canvas m_canvas;
        /// <summary>True once a scene load has been kicked off, so it can't be triggered twice.</summary>
        private bool m_loading;
        /// <summary>True once the panel has been positioned on the wall (positioning is one-shot).</summary>
        private bool m_panelAnchored;
        /// <summary>The movement style the player has picked; gates the Continue action until set.</summary>
        private MovementMode m_selectedMode = MovementMode.Unset;
        /// <summary>Button that selects smooth (walk) locomotion.</summary>
        private Button m_locomotionButton;
        /// <summary>Button that selects teleport locomotion.</summary>
        private Button m_teleportButton;
        /// <summary>Button that confirms the choice and starts the game; disabled until a mode is picked.</summary>
        private Button m_continueButton;
        /// <summary>2D audio source used to play the start-screen narration.</summary>
        private AudioSource m_narrationAudioSource;

        /// <summary>
        /// Builds the play room and title UI, ensures a UI pointer and free-roam locomotion helper exist,
        /// and plays the start narration.
        /// </summary>
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

            PlayStartNarration();
        }

        /// <summary>
        /// Plays the start-screen voiceover once on load. Safe no-op until the clip is assigned or when
        /// narration is disabled.
        /// </summary>
        private void PlayStartNarration()
        {
            if (!m_playNarrationOnStart || m_startNarrationClip == null)
            {
                return;
            }

            EnsureNarrationAudioSource();
            m_narrationAudioSource.clip = m_startNarrationClip;
            m_narrationAudioSource.Play();
        }

        /// <summary>Lazily creates/configures the 2D <see cref="AudioSource"/> used for narration.</summary>
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
            m_narrationAudioSource.spatialBlend = 0f; // 2D — audible regardless of head position
        }

        /// <summary>Keeps the panel anchored on the wall (runs the one-shot placement if not yet done).</summary>
        private void LateUpdate()
        {
            UpdatePanelPose();
        }

        /// <summary>
        /// Per-frame input handling: editor keyboard fallbacks for mode selection, plus the
        /// keyboard/controller start trigger once a mode has been chosen.
        /// </summary>
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

        /// <summary>
        /// Editor-only desktop fallback: returns true when Space/Enter is pressed so the title screen can
        /// be tested flat without a VR controller. Always false in a build.
        /// </summary>
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

        /// <summary>
        /// Saves the chosen movement preference and begins the fade-out-and-load transition. Ignored if
        /// already loading, if no mode is selected, or if no target scene is configured.
        /// </summary>
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

        /// <summary>
        /// Coroutine that fades the HMD view to black via <see cref="OVRScreenFade"/> (adding one to the
        /// main camera if needed) and then loads the target scene.
        /// </summary>
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

        /// <summary>
        /// Constructs the world-space title panel: background, title, instructions, the two movement-mode
        /// buttons, and a gated Continue button, then anchors the panel to the wall.
        /// </summary>
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

            // Clean, non-overlapping vertical stack: title band (top), instructions under it, the two mode
            // buttons across the middle with a clear centre gap, Continue at the bottom. Text auto-fits its box.
            var title = CreateText(panel, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -58f), new Vector2(880f, 90f), 44f);
            title.text = m_titleText;

            var instructions = CreateText(panel, "Instructions", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(860f, 112f), 26f);
            instructions.text = "Walk around this room and try both movement styles.\n" +
                                 "Left stick to walk  -  right INDEX (front) trigger to teleport.\n" +
                                 "Pick whichever feels best, then Continue.";

            m_locomotionButton = CreateButton(panel, "Locomotion Button", "Locomotion\n(walk)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-175f, -38f), new Vector2(300f, 104f), 28f);
            m_locomotionButton.onClick.AddListener(() => SelectMode(MovementMode.Smooth));

            m_teleportButton = CreateButton(panel, "Teleport Button", "Teleportation\n(trigger)", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(175f, -38f), new Vector2(300f, 104f), 28f);
            m_teleportButton.onClick.AddListener(() => SelectMode(MovementMode.Teleport));

            m_continueButton = CreateButton(panel, "Continue Button", "Continue", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 52f), new Vector2(300f, 80f), 36f);
            m_continueButton.onClick.AddListener(StartGame);
            m_continueButton.interactable = false;

            UpdatePanelPose();
        }

        /// <summary>
        /// One-shot placement of the menu flat against the room's front (+Z) wall at eye height, facing the
        /// player. Fixed (no billboard/follow) so it can never sink into the floor.
        /// </summary>
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

        /// <summary>
        /// Records the player's movement choice, enables the Continue button, and highlights the picked
        /// mode button.
        /// </summary>
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

        /// <summary>Tints a mode button green when it is the selected mode, otherwise blue.</summary>
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

        /// <summary>
        /// Builds the small 8x8 m room (floor, four walls, a directional light) that the player walks or
        /// teleports around in to try out both movement styles. Constructed at world origin to match the
        /// rig and locomotion helper.
        /// </summary>
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

        /// <summary>Creates a low-gloss Standard-shader material of the given colour for room surfaces.</summary>
        private static Material CreateRoomMaterial(string materialName, Color color)
        {
            var material = new Material(Shader.Find("Standard")) { name = materialName, color = color };
            material.SetFloat("_Glossiness", 0.2f);
            return material;
        }

        /// <summary>Spawns a scaled cube primitive (floor/wall) under the room root with the given material.</summary>
        private static void CreateRoomCube(Transform parent, string objectName, Vector3 localPosition, Vector3 localScale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>Helper that creates a child <see cref="RectTransform"/> with the given anchors, position, and size.</summary>
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

        /// <summary>
        /// Helper that creates a centered, auto-sizing white <see cref="TextMeshProUGUI"/> label inside a new
        /// rect so long strings shrink to fit rather than overflowing.
        /// </summary>
        private static TMP_Text CreateText(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, float fontSize)
        {
            var rect = CreateRect(parent, objectName, anchorMin, anchorMax, anchoredPosition, size);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            // Auto-fit: shrink the text to stay inside its box so long strings never overflow onto the
            // rows below (fontSize is the cap; it drops as far as ~half before clipping).
            text.enableAutoSizing = true;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = Mathf.Max(12f, fontSize * 0.5f);
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// Helper that creates a clickable <see cref="Button"/> with a sprite/tinted background and a
        /// centered auto-sizing label; used for the mode and Continue buttons.
        /// </summary>
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
