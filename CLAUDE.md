# CLAUDE.md — SimJam radiation-training VR sim

Guidance for AI assistants working in this repo. Keep it short; update it when the architecture changes.

## What this is
A **Meta Quest 3 mixed-reality/VR radiation-detection training game** built in **Unity 6 (6000.4.10f1)**.
The player picks up a handheld **identiFINDER** survey meter and must find the **one hidden radioactive
barrel** among many visually-identical drums by sweeping the detector (CPS/dose readout + geiger clicks
+ controller haptics). Built on top of Meta's Passthrough Camera API sample. Descends from a
procedural, code-generated world: each scene is a near-empty bootstrap whose contents are built at
runtime by **one spawner MonoBehaviour**.

## Platform / build
- **Engine:** Unity 6000.4.10f1, **Built-in Render Pipeline** (custom materials use the **Standard**
  shader — a URP/Lit lookup renders magenta; do not switch pipelines casually).
- **XR:** OpenXR 1.16 + **Meta XR feature** for Android/Quest; input via **OVRInput / OVRCameraRig**
  (legacy Meta), NOT XRI. **New Input System only** (`activeInputHandler: 1`) — the legacy
  `UnityEngine.Input` class throws at runtime, so editor keyboard fallbacks use `Keyboard.current`.
- **Build:** Android, IL2CPP, ARM64, Vulkan, Single-Pass-Instanced. `File ▸ Build & Run` (USB,
  Developer Mode) or Quest Link play-in-editor (deactivate the Meta XR Simulator first).
- **Bundle id** is still the sample's `com.samples.passthroughcamera` (fine for sideloading; rebrand
  before distribution).

## Scenes & flow (build order in `ProjectSettings/EditorBuildSettings.asset`)
`StartScene` → `TutorialRoom` → `RadiationLabRoom`.
- **StartScene** — VR title screen (`StartScreenSpawner`), Shania UI, a **Start** button → loads the tutorial.
- **TutorialRoom** — 8-step guided tutorial (`TutorialManager` + `TutorialRoomInteractionController` +
  `TutorialRoomDresser`); 3 gated steps (walk-to-marker, teleport, grab detector & read ≥3 CPS); the
  final "Begin Mission" loads the lab.
- **RadiationLabRoom** — the main game: a procedurally built lab, grabbable detector, hidden source,
  poke-START button, auto-opening door, and the find-the-barrel guess loop with an end-of-round stats screen.

## Key scripts (`Assets/Simulation/Scripts/`)
- **RadiationLabRoomSpawner** — the big one (~3900 lines): builds the lab + props + barrels + detector
  + START button + door + arms at runtime; owns the Waiting→Playing→Resolved game loop, the guess
  cone (`GetAimedBarrel`), scan tracking + stats panel, the poke START button, and the door.
- **MixamoArmRig** — no-Animator two-bone IK arms driven from the OVR rig; `RecalibrateHands()` re-syncs
  the wrist twist on grab; per-bone finger curl toward the palm.
- **GrabbableTool** — proximity grab (OVRInput hand-trigger) for the detector + door; fires `Grabbed`/`Released`.
- **Radiation model** — `RadiationField` (static inverse-square + occlusion + aim model), `RadiationSource`
  (per-barrel emitter), `RadiationDetector` (10 Hz Poisson sampling → smoothed CPS), `GeigerAudio`
  (synthesized click train), `BarrelInstance` (per-barrel metadata).
- **Tutorial / UI** — `TutorialManager` (world-space caption panel + step gating), `VrUiPointer`
  (controller laser that clicks world-space uGUI buttons — avoids OVRInputModule, which uses legacy
  Input), `StartScreenSpawner`, `TeleportEffects` (emissive ring + arrival burst).

## Features added on branch `tutorial-integration` (this work)
Tutorial cherry-picked + wired to the game (hand-off, 3-min round, build order); world-space tutorial
UI + laser-pointer clicking; teleport glow/burst; scene-transition fades; tutorial arms + grab-twist
fix; **end-of-round stats screen** (time / scanned-accuracy / remaining) replacing the countdown;
**more shelves + barrels**; **poke/mash START button**; **door auto-opens on a knob poke**.

## Testing
- **Editor (no headset):** open a scene, Play, click the Game view for focus. Keyboard fallbacks
  (`#if UNITY_EDITOR`, `Keyboard.current`): tutorial **Space**=advance / **Backspace**=back / **WASD**
  move / **T** teleport / **G** mount detector; lab **B**=START, **N**=submit, **G**=mount. Lab has no
  keyboard locomotion — use the **Meta XR Simulator** to walk/aim there.
- **Headset:** real teleport/grab/haptics, the poke START + door, and on-device shader behavior only
  reproduce in an actual **Build & Run** APK.

## Conventions / gotchas
- **Scenes serialize an override block for the spawner — editing a C# field's default does NOT change
  the scene.** Tunables (room size, barrel counts, timer flag, press radius) must be edited in the
  `.unity` YAML (or the Inspector).
- One spawner builds each scene; almost every gameplay component is `AddComponent`'d at runtime, so a
  `.unity` diff reveals little about behavior.
- Runtime materials are tracked and freed in `OnDestroy`; emissive/transparent variants are forced into
  builds via the pre-authored `SimJamEmissiveScreen` / `SimJamBlobShadow` materials (shader-stripping
  workaround). Runtime-created shaders (e.g. particle burst) use `Sprites/Default` to survive stripping.
- Don't push to GitHub or change the lab room's visuals/colliders without being asked.
