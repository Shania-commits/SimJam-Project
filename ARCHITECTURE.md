# SimJam — Architecture

SimJam is a mixed-reality / VR **radiation-detection training game** for the **Meta Quest 3**.
The player picks up a handheld **identiFINDER** survey meter and hunts for the single hidden
radioactive drum among many visually-identical barrels, sweeping the detector and reading its
counts-per-second (CPS) / dose readout, the synthesized Geiger click train, and the controller
haptics to home in on the source. This document describes how the project is structured so that a
new contributor can navigate the code, scenes, and build.

## Overview / premise

- One barrel in the lab is a real radioactive source; every other drum is an inert decoy that only
  reads natural background. The barrels are deliberately identical in appearance, so the source can
  only be located with the detector — not by looking.
- A round is a find-the-barrel loop: poke the wall **START** button to randomize a scenario, sweep
  the room with the meter, then aim at the drum you suspect and submit your guess. You get a limited
  number of tries (default 2) and, in the lab, a countdown (default 3 minutes). Each round ends on a
  correct guess, running out of tries, or the timer expiring, and shows an end-of-round stats panel.
- The experience is front-loaded with a title screen and a guided tutorial so a first-time player
  learns movement, teleport, and detector operation before entering the main game.

## Platform & build

- **Engine:** Unity **6000.4.10f1** (Unity 6), using the **Built-in Render Pipeline**. Custom
  runtime materials are created against the **Standard** shader. The project is *not* on URP — a
  URP/Lit material lookup renders magenta here, so the render pipeline should not be swapped casually.
- **XR runtime:** **OpenXR** with the **Meta XR feature** for Android/Quest.
- **Input / rig:** player input and tracking go through **OVRInput** and **OVRCameraRig** (the legacy
  Meta rig), not the XR Interaction Toolkit. The project runs on the **New Input System only**
  (`activeInputHandler: 1`); the legacy `UnityEngine.Input` class throws at runtime, so any editor
  keyboard fallbacks read from `Keyboard.current` behind `#if UNITY_EDITOR`.
- **Build target:** Android, **IL2CPP**, **ARM64**, **Vulkan**, single-pass-instanced stereo.
  Deploy with `File ▸ Build & Run` over USB (Quest in Developer Mode), or iterate in-editor over
  Quest Link (deactivate the Meta XR Simulator first when using a real headset).
- The project descends from Meta's Passthrough Camera API sample; the bundle id still reads
  `com.samples.passthroughcamera` and should be rebranded before any distribution.

## Scene flow

Build order lives in `ProjectSettings/EditorBuildSettings.asset`:
**`StartScene` → `TutorialRoom` → `RadiationLabRoom`.** (Several sample scenes and the older
`BasicVRRoom` / `RandomRoomBarrels` reference scenes remain in the project but are disabled in the
build list.)

### StartScene — title screen + movement choice
Built by `StartScreenSpawner`: a world-space title panel (so it renders in the headset rather than a
screen-space overlay) with a background, heading, and a **Start** button. `StartRoomLocomotion` makes
all three locomotion styles — smooth walk, snap turn, and teleport — active at once so the player can
try each before committing. The chosen style is saved via `MovementPreference` and honoured by every
later scene. A "choose your movement" narration clip plays once on load. Confirming fades to black and
loads `TutorialRoom`.

### TutorialRoom — guided 9-step tutorial
`TutorialManager` owns an ordered list of tutorial steps and builds a world-space caption panel
(title / body / step counter / Continue-Previous-Pause buttons) at runtime. The default script is a
**9-step** sequence (indices 0–8): Welcome, Introduction, Tutorial Overview, then four teaching steps
— Movement, Teleportation, Detector Operations, and Submit a Reading — followed by Tutorial Complete
and a final **Mission Briefing** whose "Begin Mission" button hands off to the lab with a fade. Each
step can optionally auto-play a per-step narration clip.

Three steps are *gated* — the player must complete an interactive task before Continue unlocks:
- **Walk to the marker** — `TutorialStepMarker` shows a pulsing floor disc and reports completion
  when the headset enters its radius.
- **Teleport** — `TutorialRoomInteractionController` watches for a successful teleport.
- **Grab and read / submit** — the same controller spawns a grabbable detector plus **two teaching
  barrels** (one hot "SUBMIT THIS" drum, one inert decoy) so the player rehearses picking up the
  meter, watching the reading climb, and submitting the correct barrel. It plays win/loss SFX on
  submit. `TutorialRoomDresser` fills the rest of the room with non-interactive set decoration.

### RadiationLabRoom — the main game
Built by `RadiationLabRoomSpawner`. It procedurally constructs the lab shell, props, shelves, and
barrels, seeds exactly one hidden `RadiationSource`, presents the grabbable detector on a pedestal,
builds a **poke-driven START button** and an **auto-opening connecting door**, wires up VR
locomotion, and runs the game loop:

- **Phases:** `Waiting → Playing → Resolved`. Poking START randomizes a fresh scenario and begins a
  round.
- **Guessing:** `GetAimedBarrel()` uses a forgiving angular **guess cone** (default 34° half-angle,
  ~12 m reach) from the detector (or the right controller when the meter isn't held) plus a direct
  raycast fallback, so the player only has to point roughly at a drum. `SubmitGuess()` compares the
  aimed barrel to the hot source and resolves the round.
- **Round timer:** optional, defaulting on at 180 s to match the tutorial's "3 minutes" briefing; a
  timeout resolves the round as over.
- **Stats screen:** on resolve, `ShowStatsPanel()` displays time, scanned-vs-total accuracy, and
  remaining unscanned barrels, and prompts the player to press START for a new round.

## Runtime / procedural architecture

Each scene is a near-empty bootstrap. **One spawner MonoBehaviour per scene** builds essentially all
of its contents at runtime with `AddComponent` and primitive/prefab meshes — the room shell, props,
barrels, detector, arms, UI, START button, door, locomotion, and the whole game loop are created in
code, not authored in the `.unity` file. As a result, a scene diff reveals very little about actual
behavior; the logic lives in the scripts.

Consequences worth knowing:
- Runtime materials are created against the Standard shader, tracked, and freed in `OnDestroy`.
- Emissive/transparent material variants that would otherwise be stripped from a build are forced in
  via pre-authored `Resources` materials (`SimJamEmissiveScreen`, `SimJamBlobShadow`), and
  runtime-created effects use stripping-safe shaders.
- Textures for the lab are drawn pixel-by-pixel at runtime by `ProceduralTextureLibrary` (no bitmap
  assets shipped), and barrel labels are size-only so drums stay visually identical.

## Per-script reference

Scripts live in `Assets/Simulation/Scripts/`.

### Spawners
- **`RadiationLabRoomSpawner`** — the largest script; builds the whole lab (shell, props, barrels,
  detector, START button, door, arms) at runtime and owns the Waiting→Playing→Resolved game loop, the
  guess cone (`GetAimedBarrel`), scan tracking, the stats panel, the poke START button, and the door.
- **`BasicVRRoomBarrelSpawner`** — the original barrel-room spawner kept untouched as a working
  reference; the lab spawner descends from it.
- **`RandomRoomBarrelSpawner`** — an earlier randomized-room spawner variant retained for reference.

### Radiation model
- **`RadiationField`** — static scene-wide physics: a registry of all active sources that computes
  the mean CPS at a sensor position from inverse-square falloff, occlusion raycasts, a directional aim
  weight, and a constant background; also provides Poisson sampling and a CPS→µSv/h conversion.
- **`RadiationSource`** — the invisible emitter on the one hot barrel; registers/unregisters with the
  field and carries activity (CPS at 1 m) and isotope identity.
- **`RadiationDetector`** — the meter's brain; samples the field at the sensor tip at 10 Hz, draws a
  Poisson count, smooths it into a CPS reading, and drives the screen text, Geiger audio, and
  controller haptics (haptics gated to a close confirmation radius). Self-respawns if dropped.
- **`GeigerAudio`** — synthesizes a click waveform at runtime and fires Poisson-distributed clicks
  whose rate tracks the detector's CPS, spatialized to the handheld meter.
- **`BarrelInstance`** — per-barrel metadata (source label, count); shows a debug billboard label in
  editor/dev builds only, force-hidden in release so the source can't be read off the drum.
- **`RadiationCountProfile`** — a ScriptableObject supplying a pool of raw count values (explicit list
  or a random min/max range) used to seed barrel activity.
- **`RadiationOccluder`** — marks an object as a radiation shield with a 0..1 attenuation factor; a
  static `Attach` helper tags runtime-built props (walls, shelves, drums).

### Arms / hands
- **`MixamoArmRig`** — no-Animator two-bone IK arms on a Mixamo-rigged mesh driven from the OVR rig
  (chest anchored to the head, wrists tracking controllers); auto-scales reach, calibrates wrist
  twist on grab, and curls fingers toward a fist with grip.
- **`ProceduralArmRig`** — a lighter alternative built from primitive cylinders/spheres with the same
  two-bone IK, trading fidelity for zero mesh dependencies.
- **`MetaQuestHandVisuals`** — sets up the authentic Meta Quest hand mesh (OVRHand/OVRSkeleton/OVRMesh)
  on the rig's hand anchors via reflection, forcing the mesh to stay rendered while a controller is held.

### Interaction
- **`GrabbableTool`** — proximity grab via the OVR hand-trigger for the detector and the door knob;
  snaps to a fixed held pose, restores physics on release, and fires `Grabbed` / `Released`.
- **`VrUiPointer`** — a controller laser that clicks world-space uGUI buttons directly (bypassing
  OVRInputModule, which relies on the legacy Input class); tints green on a clickable target and
  respects `Button.interactable` so gated steps can't be skipped.
- **`StartRoomLocomotion`** — self-contained start-room movement enabling smooth walk, snap turn, and
  teleport simultaneously, clamped to room bounds, so the player can sample every style.

### Tutorial / UI
- **`TutorialManager`** — owns the tutorial steps, builds the world-space caption panel, advances /
  rewinds, gates interactive steps, plays per-step narration, and hands off to the lab on the final step.
- **`TutorialRoomInteractionController`** — spawns the tutorial detector, teaching barrels, and arms;
  drives player locomotion; and completes the movement/teleport/submit gated steps.
- **`TutorialRoomDresser`** — procedurally builds the tutorial room's static set decoration (tables,
  shelving, clutter, whiteboard, posters, lighting) from primitives and runtime materials.
- **`TutorialStepMarker`** — a pulsing floor disc that completes the walk-to-marker step when the
  player steps into its radius.
- **`StartScreenSpawner`** — builds the VR title screen (world-space panel + Start button) and, on
  confirm, fades out and loads the tutorial.
- **`TeleportEffects`** — a shared, code-built one-shot particle burst played at a teleport landing
  point, used by both the tutorial and the lab.

### Detector build
- **`DetectorModelBuilder`** — static factory that assembles the identiFINDER meter, either from Unity
  primitives (`Build`) or wrapping an authored prefab (`BuildFromPrefab`); returns the grab collider,
  rigidbody, screen text, and sensor-tip transform the radiation model samples from.
- **`DetectorScreenBinder`** — runtime bridge that mirrors a detector's live reading onto an authored
  prefab's TextMeshPro label and a UI "warmth" slider, keeping `RadiationDetector` UI-agnostic.

### Content
- **`RoomDecorator`** — static helper that layers cosmetic detail (baseboards, door frames, exit signs,
  hazard stripes, ceiling conduits, the detector pedestal, fake blob shadows) onto the built rooms.
- **`ProceduralTextureLibrary`** — central cache of code-generated textures (concrete, painted wall,
  wood, ceiling tile, posters, hazard stripes, plaques, blob shadow, size-only barrel labels), drawn
  on first use so no bitmaps ship.

### Misc
- **`MovementPreference`** — a `MovementMode` enum plus a static PlayerPrefs-backed helper that
  persists the start-screen locomotion choice across scene loads.

## Audio & narration

- **Narration** clips live in `Assets/Audio/Narration/` (`00_choose_movement`, `01_welcome`
  through `08_mission_briefing`, plus `movement_controls` / `barrel_controls`). Tutorial steps can
  each carry a narration clip that auto-plays when the step becomes current
  (`TutorialManager.PlayNarration`); a per-manager audio source is created on demand.
- The **start screen** plays the "choose your movement" clip once on load
  (`StartScreenSpawner.PlayStartNarration`).
- **Win / loss SFX** play on submit in both rooms — a level-up chime on a correct guess and an
  "incorrect" sting on a wrong one — sourced from the clips in `Assets/Audio/`
  (`337049…level-up-01.mp3` and `351565…incorrect…violin.wav`). The lab plays them via
  `RadiationLabRoomSpawner.PlaySubmitFeedback`; the tutorial's teaching-barrel submit does the same.

## Key conventions & gotchas

- **Scene YAML overrides C# field defaults.** Each scene serializes an override block for its spawner,
  so editing a field's default value in C# does **not** change what the scene does. Tunables (room
  size, barrel counts, round-timer flag, START press radius, guess-cone angle, etc.) must be edited in
  the `.unity` YAML or via the Inspector.
- **Built-in RP + Standard shader.** Runtime materials use the Standard shader; a URP/Lit lookup
  renders magenta. Do not switch render pipelines without a full material pass.
- **New Input System only.** The legacy `UnityEngine.Input` class throws at runtime; player input goes
  through OVRInput, and editor keyboard fallbacks use `Keyboard.current` under `#if UNITY_EDITOR`.
- **Runtime materials are tracked and freed** in `OnDestroy`. Stripping-sensitive materials/shaders are
  routed through pre-authored `Resources` assets (`SimJamEmissiveScreen`, `SimJamBlobShadow`) or
  stripping-safe shaders so they survive an ARM64 build.
- **Barrels are intentionally identical.** Labels are size-only and the source barrel adds no visible
  marker in release builds, so the drum can only be found with the meter.

## How to test

- **In-editor, no headset.** Open a scene, press Play, and click the Game view for focus. Keyboard
  fallbacks exist for flat-desktop testing: in the tutorial, **Space/Enter** advances and
  **Backspace** goes back; movement/teleport/grab helpers cover the gated steps. The lab has no
  keyboard locomotion — use the **Meta XR Simulator** to walk and aim there. The world-space VR panels
  aren't mouse-clickable in flat Play mode, which is why the keyboard fallbacks exist.
- **On-headset.** Real teleport, grab, and haptics, the poke START button and door, on-device audio,
  and correct shader/stripping behavior only reproduce in an actual **Build & Run** APK on the Quest
  (or over Quest Link with the Meta XR Simulator disabled).
