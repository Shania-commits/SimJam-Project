# SimJam Quest 3 Passthrough Barrel Simulator

Unity mixed-reality prototype for Meta Quest 3/3S. The app has two simulator modes: v1 uses Meta's passthrough camera sample pipeline to detect chairs on-device and place a radioactive barrel over each detected chair; v2 uses MRUK room data to randomly place several barrels on the user's real room floor.

## Current Milestone

- Platform: Meta Quest 3/3S passthrough, not a full VR-only scene.
- V1 scene: `MultiObjectDetection`, with on-device Unity Inference Engine / YOLO chair detection.
- V2 scene: `RandomRoomBarrels`, with MRUK room/Scene API placement.
- V1 target class: `chair`.
- V2 placement: random floor-only barrel locations, constrained by room bounds, scanned scene volumes, wall clearance, player clearance, and spacing between barrels.
- Overlay: the imported `Barrel_55_low.fbx` is assigned; a yellow placeholder appears only if the barrel prefab reference is missing.
- Counts: each barrel receives one random count value from a generated pool.
- Input: controllers for v1 and v2.

## Open In Unity

1. Open this folder in Unity Hub: `c:\Work\SimJam-Project`.
2. Use the Unity version requested by the copied Meta sample project in `ProjectSettings/ProjectVersion.txt`.
3. Let Unity restore packages from `Packages/manifest.json`.
4. Open the `MultiObjectDetection` scene under `Assets/PassthroughCameraApiSamples/MultiObjectDetection` for v1, or `RandomRoomBarrels` under `Assets/Simulation/Scenes` for v2.
5. Build/run on a Quest 3/3S device. XR Simulator does not provide the headset passthrough camera or real room scan.
6. From the headset start menu, choose `MultiObjectDetection` for v1 or `RandomRoomBarrels` under `SimJam Scenes` for v2.

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

## Assigning The Custom Barrel Later

The patched `DetectionManager` has a `SimJam barrel simulator` section in the Inspector for v1. The `RandomRoomBarrelSpawner` in the v2 scene has matching barrel/count/scale fields.

Assign your colleague's barrel prefab to `Barrel Prefab`. If this field is empty, the app creates a yellow cylinder placeholder so the prototype is still testable. The current assigned model is `Assets/Barrels/Barrel_55_low.fbx`.

Useful fields:

- `Target Class Name`: keep as `chair` for v1.
- `Barrel Vertical Offset`: raises the barrel above the raycast hit point.
- `Custom Barrel Spawn Scale`: saved from the current barrel tuning as `X 0.19567`, `Y 0.1853504`, `Z 0.19567`.
- `Radiation Count Profile`: optional ScriptableObject for custom count pools.
- `Show Debug Count Labels`: toggles the floating `CPM` label above each barrel.

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
