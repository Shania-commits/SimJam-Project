# SimJam — Customizing Guide

A practical "how to change things" guide for the SimJam VR radiation-detection
training game (Unity 6 / `6000.4.10f1`, Meta Quest 3, Built-in Render Pipeline,
OVR rig). For how the project is put together, read `ARCHITECTURE.md` first; this
document tells you **where to click to change a specific behavior**.

---

## READ THIS FIRST: the scene overrides the C# default

Almost every tunable in this game is a `[SerializeField]` field on a single
"spawner" MonoBehaviour, and **the scene file stores its own value for that
field.** Unity serializes an override of every serialized field into the
`.unity` scene YAML the moment the component exists in that scene. At runtime the
scene value wins — the `= 34f` default written in the C# source is only a
fallback used when the scene has *no* stored value for that field.

**Consequence:** editing the default in the `.cs` file and pressing Play changes
**nothing**, because the scene already carries an overriding value.

You can see this directly in this project. `RadiationLabRoomSpawner.cs` declares:

```csharp
[SerializeField] private Vector2 m_roomSizeFeet = new Vector2(20f, 20f);
[SerializeField] private int m_minBarrels = 11;
[SerializeField] private int m_maxBarrels = 45;
[SerializeField] private bool m_enableRoundTimer = true;
```

but `Assets/Simulation/Scenes/RadiationLabRoom.unity` overrides them:

```yaml
m_roomSizeFeet: {x: 42, y: 42}
m_minBarrels: 60
m_maxBarrels: 120
m_enableRoundTimer: 0        # timer is OFF in the shipped scene, despite the C# default of true
```

So the lab you actually play is a **42 ft** room with **60–120** barrels and **no
round timer** — none of which matches the C# defaults. Change the C# and nothing
moves; change the scene and it does.

### How to actually change a tunable

1. In Unity, open the scene that owns the behavior (see the table at the bottom
   — e.g. `Assets/Simulation/Scenes/RadiationLabRoom.unity`).
2. In the **Hierarchy**, select the GameObject that holds the spawner component
   (named in each section below — e.g. the `RadiationLabRoomSpawner` object).
3. In the **Inspector**, find the component and the field. Fields are grouped
   under bold `[Header]` bars (e.g. "Room scale", "Game loop", "Hidden radiation
   source"). The Inspector shows friendly labels: the C# field `m_guessConeAngle`
   appears as **"Guess Cone Angle"**, `m_startButtonPressRadius` as **"Start
   Button Press Radius"**, and so on (leading `m_` dropped, camelCase spaced).
4. Edit the value, then **save the scene** (`Ctrl+S`). Saving is what writes the
   override back into the `.unity` YAML.

You can also hand-edit the value directly in the scene YAML with a text editor
(the key is the raw field name, e.g. `m_guessConeAngle: 34`) — but do this with
Unity closed, or it will overwrite your edit on save.

**When editing the C# default IS the right move:** only when the scene has no
stored value for that field yet (a freshly added component/field), or when the
value isn't serialized at all. Two categories are hardcoded and *must* be edited
in C#:

- Non-serialized constants and layout math (shelf heights, panel colors, room
  wall geometry, the default 9-step tutorial fallback text). These are called out
  where relevant below.
- `MovementPreference.cs` — not a MonoBehaviour, lives in no scene; edit the C#.

---

## Common customizations

Unless noted, every step is: open the scene → select the GameObject → edit the
field in the Inspector → save the scene.

### 1. Room size and barrel count

**Scene:** `Assets/Simulation/Scenes/RadiationLabRoom.unity`
**GameObject:** `RadiationLabRoomSpawner`
**Component:** `RadiationLabRoomSpawner`

- **Room size** — under the **"Room scale"** header:
  - `m_roomSizeFeet` (**Room Size Feet**) — the main lab footprint, in **feet**
    (converted to metres internally; minimum 8 ft edge). Ships at `42 × 42`.
  - `m_spawnRoomSizeFeet` (**Spawn Room Size Feet**) — the adjacent waiting room
    that holds the START button and detector pedestal. Ships at `12 × 12`.
- **Barrel count** — under **"Scenario randomization"**:
  - `m_minBarrels` / `m_maxBarrels` (**Min/Max Barrels**) — how many barrels are
    requested per round; the actual count picked is random in `[min, max]`. Ships
    at `60 / 120`. If the layout can't fit the requested number it places as many
    as it can; `m_layoutRetryCount` controls how many seeded attempts it makes.
  - Related furniture knobs in the same group: `m_minTables` / `m_maxTables`,
    `m_minShelfUnits` / `m_maxShelfUnits`.

### 2. Difficulty

All on the **`RadiationLabRoomSpawner`** object in
`Assets/Simulation/Scenes/RadiationLabRoom.unity`.

- **Hidden-source strength** — under **"Hidden radiation source"**:
  `m_minSourceActivityCps` / `m_maxSourceActivityCps` (**Min/Max Source Activity
  Cps**) — the source's activity in counts-per-second at 1 m, drawn
  log-uniformly in this range each round (ships `1500`–`30000`). Lower the
  numbers → a fainter source that's harder to find; raise them → the meter
  screams from farther away.
- **Guess-cone forgiveness** — under **"Game loop"**: `m_guessConeAngle`
  (**Guess Cone Angle**) — the half-angle in degrees of the "you pointed roughly
  at the right drum" cone used by `GetAimedBarrel()`. Ships at `34°` (very
  forgiving; range-clamped 4–35). Smaller = you must aim more precisely to
  submit. Paired with `m_guessRayLength` (**Guess Ray Length**, reach in metres,
  ~12 m).
- **Tries per round** — under **"Game loop"**: `m_maxTries` (**Max Tries**) —
  guesses allowed before the round is lost. Ships at `2`.
- **Round timer** — under **"Game loop"**:
  - `m_enableRoundTimer` (**Enable Round Timer**) — master on/off. It is **off
    (`0`) in the shipped lab scene**, even though the C# default is `true`.
  - `m_roundDurationSeconds` (**Round Duration Seconds**) — time limit in seconds
    when enabled. This field is **not currently serialized in the scene**, so it
    would use the C# default of `180` (3 minutes) until you tick the timer on and
    set it. To use a timer: enable `m_enableRoundTimer` and set the duration in
    the Inspector.

### 3. START button poke sensitivity

**Scene:** `Assets/Simulation/Scenes/RadiationLabRoom.unity` →
**GameObject** `RadiationLabRoomSpawner` → **"Game loop"** header →
`m_startButtonPressRadius` (**Start Button Press Radius**). Ships at `0.15` m.
This is the effective poke radius of the wall START button — **larger = easier to
trigger** (your hand registers a press from farther out). Minimum `0.04`.

### 4. Shelf heights

Shelf tier heights are **not a serialized field** — they are hardcoded layout
math in `RadiationLabRoomSpawner.cs`, so you must edit the C# (this is one of the
"edit the source" exceptions). In the shelf-building code the tier surface Y is:

```csharp
// base 0.85 m, 0.52 m per tier
var surfaceY = 0.85f + tier * 0.52f + UnityEngine.Random.Range(-0.02f, 0.02f);
```

Change the `0.85f` base to raise/lower the whole shelf and the `0.52f` to change
the gap between tiers. There is a matching fallback constant a few lines below
(`0.85f + tier * 0.52f + m_shelfSurfaceClearance`) used when an authored shelf
prefab isn't measured — **keep the two in sync.** The one serialized shelf knob
you *can* set in the Inspector is `m_shelfSurfaceClearance` (**"Shelf asset"**
header) — a small gap so barrels don't z-fight the board — and
`m_shelfBarrelSpacing` (**"Visibility and walkability"**) for spacing between
barrels on a tier.

### 5. Movement: locomotion vs teleport

The player picks their style **once on the start screen**, and it persists into
the tutorial and lab.

- **Where it's chosen:** `Assets/Simulation/Scenes/StartScene.unity`. The
  `StartScreenSpawner` (on the GameObject literally named **`GameObject`**)
  builds two buttons — walk vs. teleport — plus a gated Continue. `StartRoomLocomotion`
  makes all styles active at once there so the player can try each.
- **How it persists:** picking a mode calls `MovementPreference.Save(...)`, which
  writes an int to **PlayerPrefs** under the key `SimJam.LocomotionMode`. On load,
  `RadiationLabRoomSpawner.ApplyMovementPreference()` and the tutorial controller
  call `MovementPreference.Load()` and set their `m_enableSmoothMove` /
  `m_enableTeleport` flags accordingly (`Smooth` → walk only; `Teleport` →
  teleport only). This survives scene loads and app restarts.
- **To change the default when nothing is stored:** edit `Load()` in
  `Assets/Simulation/Scripts/MovementPreference.cs` (this file is plain C#, not a
  MonoBehaviour, so there is no Inspector — edit the source). To add/rename a mode,
  edit the `MovementMode` enum there but keep the integer values stable (they're
  written to PlayerPrefs).
- **To force a style in a single scene for testing** (bypassing the start-screen
  choice): on the `RadiationLabRoomSpawner` (or the tutorial's `TutorialManager`
  object) under **"Locomotion"**, toggle `m_enableSmoothMove` / `m_enableTeleport`.
  Note these get overwritten by the saved preference if one exists — clear
  PlayerPrefs or launch the scene directly in-editor (Unset) to see them apply.

### 6. Audio and narration

All narration lives in `Assets/Audio/Narration/` (`00_choose_movement`,
`01_welcome` … `08_mission_briefing`, plus `movement_controls`,
`barrel_controls`). Win/loss SFX and their attribution live in `Assets/Audio/`.

**Replace a tutorial step's voiceover:**
1. Drop your new clip into `Assets/Audio/Narration/` (`.mp3`/`.wav`). To
   swap in place while keeping every reference, overwrite the existing file
   (see the arm-mesh section for why in-place beats renaming).
2. Open `Assets/Simulation/Scenes/TutorialRoom.unity`, select the
   **`TutorialManager`** GameObject.
3. On the **TutorialManager** component, expand **`m_steps`** (the "Tutorial
   screens" list). Each element is a `TutorialStep`; find the one you want and
   set its **`narrationClip`** field to your clip. It auto-plays when that step
   becomes current (gated by the component's `m_playNarrationOnStepStart`).

**Start-screen clip:** `Assets/Simulation/Scenes/StartScene.unity` → GameObject
**`GameObject`** → `StartScreenSpawner` → **"Narration"** header →
`m_startNarrationClip` (**Start Narration Clip**), with `m_playNarrationOnStart`
controlling auto-play. This is the "choose your movement" prompt.

**Win / loss SFX:** these are per-scene fields, so set them in **both** scenes:
- Lab: `Assets/Simulation/Scenes/RadiationLabRoom.unity` →
  `RadiationLabRoomSpawner` → **"Feedback audio"** header →
  `m_correctSubmitClip` (level-up chime on a correct guess) and
  `m_incorrectSubmitClip` (sting on a wrong guess). Played by
  `PlaySubmitFeedback`.
- Tutorial: `Assets/Simulation/Scenes/TutorialRoom.unity` → `TutorialManager`
  object → **`TutorialRoomInteractionController`** component → same
  `m_correctSubmitClip` / `m_incorrectSubmitClip` fields, played when the player
  submits one of the two teaching barrels.

The shipped clips are `337049…level-up-01.mp3` (correct) and
`351565…incorrect…violin.wav` (wrong) in `Assets/Audio/`.

### 7. Swapping the arm mesh

The IK arms are driven from a single FBX at **`Assets/Arms/arms.fbx`**, referenced
by both the lab (`RadiationLabRoomSpawner.m_customArmsPrefab`, **"Custom arms
(Mixamo FBX)"** header) and the tutorial controller
(`TutorialRoomInteractionController.m_customArmsPrefab`).

**To swap the mesh, overwrite `Assets/Arms/arms.fbx` in place — do NOT rename or
delete-and-re-add it.** Unity identifies assets by the **GUID** stored in
`arms.fbx.meta` (currently `38a4119925ba1ea458807788b731ec55`). The scenes
reference that GUID, not the filename. Overwriting the file in place keeps the
`.meta`/GUID, so both scenes keep pointing at the new mesh automatically. If you
rename the file or import a differently-named FBX, it gets a **new GUID**, the
`m_customArmsPrefab` references go null (invisible arms / fall back to procedural
hands), and you'd have to re-assign the prefab in both scenes by hand.

After swapping, the new rig is IK'd to the controllers; if it imports backward or
mis-scaled, tune the on-object fields under "Custom arms": `m_customArmsBodyYawOffset`
(try `180`), `m_customArmsChestOffset`, `m_customArmsTargetReach`, and the finger-curl
knobs. To drop custom arms entirely and use the procedural hands, clear
`m_customArmsPrefab`.

### 8. Adding or editing tutorial steps

**Scene:** `Assets/Simulation/Scenes/TutorialRoom.unity`
**GameObject:** `TutorialManager`
**Component:** `TutorialManager` → **`m_steps`** list (the "Tutorial screens" header).

Each element is a `TutorialStep` with these fields:
- `title` — heading on the caption panel.
- `caption` — body text (multi-line).
- `backgroundImage` — optional full-panel sprite.
- `narrationClip` — optional voiceover (see audio section).
- `nextButtonLabel` — advance-button text (e.g. `Continue`, `Begin Mission`).
- `canGoBack`, `canPause` — allow Previous / Pause on this step.
- `requireCompletionToContinue` — if true, the player must finish an interactive
  task before Continue unlocks (walk-to-marker, teleport, grab-and-submit). The
  gating for those is wired via `onStepStarted`/`onStepCompleted` events to the
  interactive controllers — copy an existing gated step as a template.

To **add** a step: expand `m_steps` in the Inspector, increase the list size (or
use the `+`), and fill in the new element; drag it to reorder. To **remove** one,
delete the element.

Note: the C# `AddStarterSteps()` method holds a hardcoded 9-step fallback
sequence, but it only runs **if `m_steps` is empty**. Because the shipped
`TutorialRoom.unity` already serializes a full list, that fallback is dormant —
so edit the scene's `m_steps`, not `AddStarterSteps()`, to change the real
tutorial. `m_startingStepIndex` (same component) sets which step opens first.

---

## Per-file quick reference

| File | What it's for | Main things you'd tweak |
| --- | --- | --- |
| `Assets/Simulation/Scenes/StartScene.unity` | Title screen + movement choice (build scene #1). | Fields on the `GameObject` that holds `StartScreenSpawner`. |
| `Assets/Simulation/Scenes/TutorialRoom.unity` | Guided tutorial (build scene #2). | Fields on the `TutorialManager` object (all 3 tutorial components live here). |
| `Assets/Simulation/Scenes/RadiationLabRoom.unity` | Main find-the-barrel game (build scene #3). | Fields on the `RadiationLabRoomSpawner` object — the source of almost every difficulty/room knob. |
| `Assets/Simulation/Scripts/RadiationLabRoomSpawner.cs` | Builds the whole lab and runs the game loop. | In the **scene**: room size, barrel min/max, source CPS, guess cone, tries, timer, START radius, arms, audio. In **C#** only: shelf heights (hardcoded `0.85f + tier * 0.52f`), wall geometry. |
| `Assets/Simulation/Scripts/TutorialManager.cs` | Owns tutorial steps + caption panel. | Scene `m_steps` (titles, captions, `narrationClip`, gating), `m_startingStepIndex`, `m_missionSceneName`. Hardcoded fallback: `AddStarterSteps()`. |
| `Assets/Simulation/Scripts/TutorialRoomInteractionController.cs` | Tutorial detector, teaching barrels, movement, gated steps. | Scene: `m_correctSubmitClip` / `m_incorrectSubmitClip`, barrel positions, `m_customArmsPrefab`, movement flags. |
| `Assets/Simulation/Scripts/TutorialRoomDresser.cs` | Static tutorial-room set decoration (on the same object). | Barrel prefab/material fields; mostly leave alone. |
| `Assets/Simulation/Scripts/StartScreenSpawner.cs` | Builds the VR title screen. | Scene: `m_titleText`, `m_sceneToLoad`, `m_startNarrationClip`, panel size/placement. Hardcoded: instruction body text, button labels/layout. |
| `Assets/Simulation/Scripts/MovementPreference.cs` | Persists walk-vs-teleport across scenes (plain C#, no scene). | Edit C# directly: default in `Load()`, `MovementMode` enum, PlayerPrefs `Key`. |
| `Assets/Simulation/Scripts/StartRoomLocomotion.cs` | Enables all locomotion styles on the start screen. | Rarely tweaked; movement speeds/room bounds. |
| `Assets/Simulation/Scripts/RadiationField.cs` | Scene-wide radiation physics (inverse-square, occlusion, background). | C#-level tuning of falloff/background if you want a different detector feel. |
| `Assets/Simulation/Scripts/RadiationDetector.cs` / `GeigerAudio.cs` | Meter sampling, screen readout, Geiger clicks, haptics. | Sample rate, smoothing, haptic radius, click synthesis. |
| `Assets/Simulation/Scripts/RadiationCountProfile.cs` | ScriptableObject pool of background count values. | Create/assign an asset to `m_radiationCountProfile` for custom background counts. |
| `Assets/Simulation/Scripts/DetectorModelBuilder.cs` / `DetectorScreenBinder.cs` | Builds/wraps the identiFINDER meter. | Assign a real detector prefab via `m_detectorPrefab` instead of the procedural wand. |
| `Assets/Simulation/Scripts/MixamoArmRig.cs` / `ProceduralArmRig.cs` / `MetaQuestHandVisuals.cs` | Arm/hand rigs. | Reach, offsets, finger curl (mostly exposed via the spawner's "Custom arms" fields). |
| `Assets/Arms/arms.fbx` | The skinned arm mesh referenced by both gameplay scenes. | **Overwrite in place** to swap the mesh (keeps the GUID); never rename. |
| `Assets/Audio/Narration/` | Per-step + start-screen voiceovers. | Add/replace clips, then assign on the relevant `narrationClip` / `m_startNarrationClip` field. |
| `Assets/Audio/` (`…level-up-01.mp3`, `…violin.wav`) | Win/loss SFX. | Assign to `m_correctSubmitClip` / `m_incorrectSubmitClip` in **both** gameplay scenes. |
| `ProjectSettings/EditorBuildSettings.asset` | Scene build order. | Add a scene here before loading it by name (e.g. a new mission scene). |

---

### Reminders

- **Save the scene after any Inspector edit** — that's what writes the override.
- **Built-in RP + Standard shader only.** Don't switch to URP without a full
  material pass (URP/Lit lookups render magenta here).
- **New Input System only.** Player input goes through OVRInput; legacy
  `UnityEngine.Input` throws at runtime.
- Some behavior only reproduces on-device (poke START button, teleport, haptics,
  shader stripping) — verify a **Build & Run** APK on the Quest, or Quest Link
  with the Meta XR Simulator disabled.
