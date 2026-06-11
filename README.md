# SimJam Quest 3 Barrel Simulator

Unity prototype for Meta Quest 3/3S. The app has three simulator modes: v1 uses Meta's passthrough camera sample pipeline to detect chairs on-device and place a radioactive barrel over each detected chair; v2 uses MRUK room data to randomly place several barrels on the user's real room floor; the current full-VR mode builds a randomized 20 ft office/lab room with props, walkable aisles, and non-grabbable barrels.

## Current Milestone

- Platform: Meta Quest 3/3S.
- V1 scene: `MultiObjectDetection`, with on-device Unity Inference Engine / YOLO chair detection.
- V2 scene: `RandomRoomBarrels`, with MRUK room/Scene API placement.
- Full-VR scene: `BasicVRRoom`, with a 20 ft x 20 ft randomized office/lab room connected to a warmer spawn room by a swinging door.
- Radiation Lab scene: `RadiationLabRoom`, the radiation-training evolution of `BasicVRRoom` (which stays unchanged): one hidden radioactive barrel per run, a grabbable procedural identiFINDER detector on a pedestal, inverse-square + shielding radiation physics, geiger audio, signal-gated haptics, grab-and-swing door, visible IK arms, and a procedural visual overhaul.
- V1 target class: `chair`.
- V2 placement: random floor-only barrel locations, constrained by room bounds, scanned scene volumes, wall clearance, player clearance, and spacing between barrels.
- Full-VR placement: randomized non-grabbable 1-45 barrel scenarios across floors, folding tables, and wall shelves, constrained by visibility and walkable aisle rules.
- Overlay: imported `Barrel_55_low.fbx`, `Barrel_30_low.fbx`, and `Barrel_5_low.fbx` are assigned in the full-VR scene and auto-fitted to 55/30/5 gallon dimensions.
- Counts: each barrel receives one random count value from a generated pool.
- Input: controllers for v1, v2, and full VR.

## Open In Unity

1. Open this folder in Unity Hub: `c:\Work\SimJam-Project`.
2. Use the Unity version requested by the copied Meta sample project in `ProjectSettings/ProjectVersion.txt`.
3. Let Unity restore packages from `Packages/manifest.json`.
4. Open the `MultiObjectDetection` scene under `Assets/PassthroughCameraApiSamples/MultiObjectDetection` for v1, `RandomRoomBarrels` under `Assets/Simulation/Scenes` for v2, or `BasicVRRoom` for full VR.
5. Build/run on a Quest 3/3S device. XR Simulator does not provide the headset passthrough camera or real room scan.
6. From the headset start menu, choose `MultiObjectDetection` for v1 or choose `RandomRoomBarrels` / `BasicVRRoom` under `SimJam Scenes`.

## Documentation

- `docs/FILE_GUIDE.md`: Explains the important files and folders in this repo.
- `docs/QUEST_BUILD_AND_TEST.md`: Step-by-step Quest build, launch, controls, and room-data troubleshooting.

## V1 Controls

- `A`: start detection from the initial prompt, then place barrels from current chair detections.
- `Y`: pause/resume continuous detection after the scan has started.
- `B`: clear spawned barrels.
- `X`: randomize count values on already spawned barrels.
- Right thumbstick up: make current and future barrels bigger.
- Right thumbstick down: make current and future barrels smaller.

## V2 Controls

- Scene: `RandomRoomBarrels`.
- Startup: waits for MRUK room data, then spawns a random 3-6 barrels.
- `A`: clear and reshuffle barrel locations, orientations, and counts.
- `B`: clear all barrels.
- `X`: randomize count values only.
- Right thumbstick up: make current and future barrels bigger.
- Right thumbstick down: make current and future barrels smaller.

V2 requires Quest room/space setup and Scene / Spatial Data permission. If no room data is available, the scene shows a floating message that distinguishes missing permission, missing room scan, poor lighting, and other MRUK load failures.

## Full VR Controls

- Scene: `BasicVRRoom`.
- Startup: creates a home-like spawn room connected by a swinging door to a white office-style barrel room with ceiling tiles and fluorescent lights, then spawns a randomized scenario.
- `A`: regenerate the whole scenario: barrel count, barrel sizes, tables, shelves, positions, orientations, and counts.
- `B`: clear the current scenario.
- `X`: randomize count values only.
- Left thumbstick: smooth movement.
- Right thumbstick left/right: snap turn.
- Right trigger: teleport to the visible floor target.
- Controller grip near either gold door knob: open or close the connecting door.

The full-VR scene shows simple controller-attached virtual hands. It does not use passthrough camera access, MRUK room data, Scene permission, or an external server. Barrels are fixed simulator objects and are not grabbable.

## Radiation Lab Controls

- Scene: `RadiationLabRoom` (the `BasicVRRoom` scene above is preserved unchanged).
- Startup: builds the two-room environment, places the identiFINDER detector on a lit
  pedestal in the spawn room, and hides exactly ONE radioactive source in a random
  barrel. The hot barrel looks identical to every other barrel.
- Grip near the identiFINDER: pick it up with either hand; release grip to drop it.
- Grip near a gold door knob: grab the door and physically swing it with your hand.
  Near-closed doors latch shut with a click.
- Detector screen: live CPS, uSv/h, and a log-scale bargraph. Geiger clicks and
  controller vibration scale with the reading. Vibration is hard-gated: it stays off
  until the signal is strong (close to the source and roughly aimed at it), and
  shielding matters - a barrel in front of the hot barrel cuts the reading to ~30%.
- `A`: regenerate the scenario (new layout + new hidden source). `B`: clear.
- `X`: re-roll values and move the hidden source. `Y`: debug labels (Editor /
  Development builds only - never in release).
- Locomotion: identical to Full VR (smooth move, snap turn, teleport).
- Desktop editor test: press `G` in Play Mode to mount the detector to the camera.

## Assigning The Custom Barrel Later

The patched `DetectionManager` has a `SimJam barrel simulator` section in the Inspector for v1. The `RandomRoomBarrelSpawner` in the v2 scene and `BasicVRRoomBarrelSpawner` in the full-VR scene expose barrel/count fields.

For v1/v2, assign your colleague's barrel prefab to `Barrel Prefab`. If this field is empty, the app creates a placeholder so the prototype is still testable. The current assigned model is `Assets/Barrels/Barrel_55_low.fbx`.

Useful fields:

- `Target Class Name`: keep as `chair` for v1.
- `Barrel Vertical Offset`: raises the barrel above the raycast hit point.
- `Custom Barrel Spawn Scale`: saved from the current barrel tuning as `X 0.19567`, `Y 0.1853504`, `Z 0.19567`.
- `Radiation Count Profile`: optional ScriptableObject for custom count pools.
- `Show Debug Count Labels`: toggles the floating `CPM` label above each barrel. In full VR this defaults off because high-count layouts can be visually crowded.

For full VR, the `BasicVRRoomBarrelSpawner > Barrel Prefabs` slots are assigned to `Assets/Barrels/Barrel_55_low.fbx`, `Assets/Barrels/Barrel_30_low.fbx`, and `Assets/Barrels/Barrel_5_low.fbx`. The scene auto-fits the imported models to the intended physical sizes so the 5 gallon barrel stays smaller than the 30 gallon barrel, and the 30 gallon barrel stays smaller than the 55 gallon barrel.

To create a custom count profile:

1. In Unity's Project view, right-click in an asset folder.
2. Choose `Create > SimJam > Radiation Count Profile`.
3. Adjust the min/max/count pool size or fill the explicit count pool.
4. Assign it to the `DetectionManager`.

## Later Goals

- Add Meta hand tracking after controller flow is stable.
- Replace chair detection with a custom/fine-tuned barrel detector.
- Integrate the separate handheld radiation detector work.
- Add real radiation behavior based on distance, shielding, and detector pose.

## Source Attribution

This project is based on Meta's official passthrough camera sample project:

- https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples
- https://developers.meta.com/horizon/documentation/unity/unity-pca-sentis/

The copied sample license is preserved in `META_SAMPLE_LICENSE.txt`.
