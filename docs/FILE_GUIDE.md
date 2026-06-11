# SimJam Project File Guide

This repo is a Unity project for a Meta Quest 3/3S barrel simulator.
It contains Meta's official passthrough camera sample project plus SimJam-specific passthrough, room-data, and full-VR simulator code and scenes.

## Root Files

- `.gitignore`: Keeps Unity generated folders, builds, APKs, IDE files, and local recovery data out of Git.
- `README.md`: Main overview, controls, setup notes, and source attribution.
- `META_SAMPLE_LICENSE.txt`: Preserved license text from Meta's copied sample project.
- `Packages/manifest.json`: Unity package dependencies, including Meta MRUK, Meta XR Core, Unity Inference Engine, OpenXR, XR Management, and Android Logcat.
- `Packages/packages-lock.json`: Exact resolved package versions used by Unity.

## SimJam Files

- `Assets/Simulation/Scenes/RandomRoomBarrels.unity`: V2 scene. It uses Quest MRUK room data to spawn random barrel copies on the real room floor.
- `Assets/Simulation/Scenes/BasicVRRoom.unity`: Full-VR scene. It builds a randomized 20 ft x 20 ft office/lab room and spawns non-grabbable barrel scenarios inside it.
- `Assets/Simulation/Scripts/RandomRoomBarrelSpawner.cs`: V2 controller. Loads Quest room data, requests Scene permission, chooses valid floor positions, rejects unsafe/cluttered positions, spawns barrels, randomizes counts, handles controller controls, and shows status messages.
- `Assets/Simulation/Scripts/BasicVRRoomBarrelSpawner.cs`: Full-VR controller. Creates/uses an Oculus camera rig, adds controller-hand visuals, draws editor scale gizmos, generates the connected spawn room and office barrel room, handles grip-based door interaction, randomizes tables/shelves/barrels, and handles teleport/smooth locomotion.
- `Assets/Simulation/Scripts/BarrelInstance.cs`: Component attached to each spawned barrel. Stores the source label/orientation, world position, random radiation count, and optional floating count label.
- `Assets/Simulation/Scripts/RadiationCountProfile.cs`: Optional ScriptableObject for configuring radiation count pools instead of using fallback random values.

## Radiation Lab Files (identiFINDER training scene)

- `Assets/Simulation/Scenes/RadiationLabRoom.unity`: Radiation-training scene. Same two-room setup as `BasicVRRoom` (which is preserved unchanged) but driven by `RadiationLabRoomSpawner` with one hidden radioactive barrel per run, a grabbable detector, and the visual overhaul.
- `Assets/Simulation/Scripts/RadiationLabRoomSpawner.cs`: Radiation Lab controller. Clone of `BasicVRRoomBarrelSpawner` plus: consistent per-type barrel sizing (fit computed on unrotated bounds and cached per type), exactly one hidden `RadiationSource` per run, radiation occluder tagging, detector + pedestal spawn, grab-and-swing door, visible IK arms, per-barrel paint variation, and procedural room decoration.
- `Assets/Simulation/Scripts/RadiationField.cs`: Static radiation model. Background rate, inverse-square falloff, raycast-based shielding through `RadiationOccluder`s, directional sensitivity, Poisson sampling, CPS to uSv/h conversion.
- `Assets/Simulation/Scripts/RadiationSource.cs`: Hidden source component on the one hot barrel (activity + isotope). Adds no visible change.
- `Assets/Simulation/Scripts/RadiationOccluder.cs`: Per-object attenuation factor (barrel 0.3, wall 0.2, door 0.45, table 0.75, shelf 0.8).
- `Assets/Simulation/Scripts/RadiationDetector.cs`: Instrument brain. 10 Hz Poisson-sampled readings, screen text, geiger click rate, and hard-gated haptics (zero below 15 CPS so distance gives no hint).
- `Assets/Simulation/Scripts/GrabbableTool.cs`: Generic proximity + grip grab/release for either hand, with physics throw on release and a `G`-key editor fallback.
- `Assets/Simulation/Scripts/DetectorModelBuilder.cs`: Builds the procedural identiFINDER model (body, rubber armor, emissive screen with live TextMesh readout, buttons, sensor tip).
- `Assets/Simulation/Scripts/GeigerAudio.cs`: Procedurally generated geiger click audio; click rate follows the live reading.
- `Assets/Simulation/Scripts/ProceduralArmRig.cs`: Visible arms. Two-bone IK from a headset-estimated shoulder to each controller.
- `Assets/Simulation/Scripts/RoomDecorator.cs`: Baseboards, door frames, EXIT sign, hazard stripe, posters, ceiling conduits, blob shadows, and the detector pedestal.
- `Assets/Simulation/Scripts/ProceduralTextureLibrary.cs`: Runtime-generated textures (concrete, paint, wood, ceiling tile, hazard stripes, signs, posters, blob shadow).
- `Assets/Simulation/Resources/SimJamEmissiveScreen.mat` / `SimJamBlobShadow.mat`: Pre-authored Standard-shader materials that guarantee the emissive and transparent shader variants ship in Quest builds; runtime code clones them.
- `Assets/Barrels/Barrel_55_low.fbx`: 55 gallon barrel model. Used by v1/v2 and assigned to the full-VR 55 gallon slot.
- `Assets/Barrels/Barrel_30_low.fbx`: 30 gallon barrel model. Assigned to the full-VR 30 gallon slot.
- `Assets/Barrels/Barrel_5_low.fbx`: 5 gallon barrel model. Assigned to the full-VR 5 gallon slot.
- `Assets/Barrels/*.fbx.meta`: Unity metadata for the barrel assets. Important because scenes reference the models by these GUIDs.

## Modified Meta Sample Files

- `Assets/PassthroughCameraApiSamples/MultiObjectDetection/MultiObjectDetection.unity`: V1 scene. It keeps Meta's object detection sample but assigns the custom barrel prefab and SimJam defaults.
- `Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Scripts/DetectionManager.cs`: V1 behavior. Filters detections to `chair`, spawns barrels, prevents duplicates, assigns count values, and supports clear/randomize/scale controls.
- `Assets/PassthroughCameraApiSamples/MultiObjectDetection/DetectionManager/Scripts/DetectionUiMenuManager.cs`: V1 menu/status text adjusted for the SimJam barrel workflow.
- `Assets/PassthroughCameraApiSamples/PassthroughCamera/Scripts/InputManager.cs`: Adds controller helpers for `X`, `Y`, and right thumbstick up/down.
- `Assets/PassthroughCameraApiSamples/StartScene/Scripts/StartMenu.cs`: Adds a `SimJam Scenes` section so `RandomRoomBarrels` and `BasicVRRoom` appear clearly in the headset scene menu.

## Meta Sample Project Areas

- `Assets/PassthroughCameraApiSamples/StartScene`: Headset scene selection menu and shared debug UI.
- `Assets/PassthroughCameraApiSamples/PassthroughCamera`: Passthrough camera permission/access helpers.
- `Assets/PassthroughCameraApiSamples/MultiObjectDetection`: On-device YOLO / Unity Inference Engine object detection sample used as v1.
- `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model`: YOLO model files and class labels used by v1 chair detection.
- `Assets/PassthroughCameraApiSamples/MultiObjectDetection/EnvironmentRaycast`: Meta environment raycast helpers used by the sample.
- `Assets/PassthroughCameraApiSamples/CameraViewer`: Meta sample scene for viewing headset camera data.
- `Assets/PassthroughCameraApiSamples/CameraToWorld`: Meta sample scene for camera-to-world coordinate mapping.
- `Assets/PassthroughCameraApiSamples/BrightnessEstimation`: Meta sample scene for passthrough brightness estimation.
- `Assets/PassthroughCameraApiSamples/ShaderSample`: Meta sample scene for passthrough shader behavior.

## Quest/XR Configuration

- `Assets/Plugins/Android/AndroidManifest.xml`: Android/Quest manifest. Declares Quest 3/3S support, passthrough feature, Scene permission, anchor API permission, and headset camera permission.
- `Assets/MetaXR`: Meta XR project settings assets.
- `Assets/Oculus`: Oculus/Meta project config assets.
- `Assets/XR`: OpenXR and XR loader settings.
- `Assets/Resources`: Meta runtime/build config resources generated by the packages.
- `ProjectSettings/EditorBuildSettings.asset`: Build scene list. `StartScene` is first, and the SimJam scenes are included.
- `ProjectSettings/ProjectSettings.asset`: Unity player/app settings, Android package id, min/target SDK, product name, and build version.
- `ProjectSettings/ProjectVersion.txt`: Unity editor version for the project.
- Other `ProjectSettings/*.asset` files: Unity project-wide settings for graphics, quality, physics, tags, input, audio, packages, and editor behavior.

## Unity Metadata

- `*.meta` files: Unity metadata files. Keep these committed. They preserve GUIDs used by scenes, prefabs, scripts, materials, textures, and models.

## Not Committed

These are intentionally ignored and should be regenerated locally:

- `Library/`: Unity import cache and generated assemblies.
- `Temp/`, `Obj/`, `Logs/`, `.utmp/`: Unity/Android temporary build state.
- `Build/`, `Builds/`, `*.apk`, `*.aab`: Local build outputs.
- `UserSettings/`: Local Unity editor preferences.
- `Assets/_Recovery/`: Unity crash/recovery scenes.
