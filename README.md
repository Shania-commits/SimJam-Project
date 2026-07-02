# SimJam VR Radiation Detection Trainer

SimJam is a virtual reality training game for the Meta Quest 3 and 3S. The player picks up a handheld radiation survey meter (an identiFINDER) and sweeps a cluttered laboratory to find the one barrel that hides a radioactive source, then submits a reading. Every run builds a brand new randomized room and hides the radioactive barrel in a different place, so the player has to actually use the detector instead of memorizing a spot. A guided tutorial teaches movement and detector use before the real mission begins.

This document explains what every file in the project is for, and then how to open, build, and run the game on your own Quest headset from Unity.

## Tech stack

* Engine: Unity 6000.4.10f1, Built in Render Pipeline (Standard shader).
* Language: C#. Almost every scene is assembled in code at runtime by a single spawner script.
* VR: OpenXR with the Meta XR feature set, using OVRInput and OVRCameraRig, and the Unity New Input System.
* Target: Meta Quest 3 and 3S, built for Android with IL2CPP, ARM64, and the Vulkan graphics backend.

## Project scaffold

### Documentation (repository root)

* `README.md`: this file.
* `ARCHITECTURE.md`: a deeper explanation of how the systems fit together, including scene flow, the radiation model, and the arm rig.
* `CUSTOMIZING.md`: a practical guide to changing the game from the Unity Inspector, including a full reference for every randomization and barrel placement knob.
* `docs/FILE_GUIDE.md`: a companion list of the important files and folders.
* `docs/QUEST_BUILD_AND_TEST.md`: build, launch, and troubleshooting notes for the headset.
* `META_SAMPLE_LICENSE.txt`: the license for the Meta passthrough sample this project was scaffolded from.

### Game code: `Assets/Simulation/Scripts`

This folder holds the game. Each scene is built at runtime by one spawner component that adds everything else in code, so the scene files themselves are nearly empty.

World builders and game loop:
* `RadiationLabRoomSpawner.cs`: the main game. It builds the randomized two room lab, spawns the barrels and props, hides one radioactive source, and runs the full round loop (start, scan, submit, win or loss, end of round stats). This is the only spawner used in the shipped game.
* `BasicVRRoomBarrelSpawner.cs`: an earlier full VR room builder, used only in the developer scene `BasicVRRoom`.
* `RandomRoomBarrelSpawner.cs`: a mixed reality variant that places barrels on your real scanned room floor, used only in the developer scene `RandomRoomBarrels`.
* `RoomDecorator.cs`: a helper that builds shared props and decor such as pedestals, signs, and blob shadows.
* `ProceduralTextureLibrary.cs`: generates textures at runtime so the look does not depend on imported image files.

Radiation model:
* `RadiationField.cs`: the physics. It turns distance, aim, and shielding into a counts per second reading and a dose value.
* `RadiationSource.cs`: a component placed on the one hot barrel that emits activity.
* `RadiationDetector.cs`: reads the field every tick and drives the counts, the dose, and the haptics.
* `RadiationOccluder.cs`: marks props such as walls, tables, and other barrels as shielding that lowers the reading behind them.
* `BarrelInstance.cs`: per barrel data, including its count value and an optional debug label.
* `RadiationCountProfile.cs`: an optional ScriptableObject that supplies a pool of realistic background count values.
* `GeigerAudio.cs`: synthesizes the geiger clicks whose rate follows the reading.

Arms, hands, and holding tools:
* `MixamoArmRig.cs`: the visible arms. An analytic two bone inverse kinematics rig that reaches from the body to each controller with no animator.
* `ProceduralArmRig.cs`: a simpler procedural arm used as a fallback.
* `MetaQuestHandVisuals.cs`: the default Meta controller hand visuals.
* `GrabbableTool.cs`: lets a controller pick up the detector by proximity and hold it.

Interaction and movement:
* `VrUiPointer.cs`: a laser pointer that lets the controller click world space menu buttons.
* `StartRoomLocomotion.cs`: smooth movement, snap turn, and teleport inside the start room.
* `MovementPreference.cs`: saves the movement style the player chose on the start screen so every scene uses it.
* `TeleportEffects.cs`: the particle burst shown when teleporting.

Detector build:
* `DetectorModelBuilder.cs`: builds the procedural detector wand when no real model is assigned.
* `DetectorScreenBinder.cs`: drives the numbers and the bar graph on the detector screen.

Start screen, tutorial, and UI:
* `StartScreenSpawner.cs`: builds the title screen and the movement choice, and plays the start narration.
* `TutorialManager.cs`: owns the ordered list of tutorial steps, shows the caption panel, plays per step narration, and hands off to the mission at the end.
* `TutorialRoomInteractionController.cs`: the tutorial gameplay, including the two teaching barrels, the gated steps, the arms, and the submit teaching.
* `TutorialRoomDresser.cs`: dresses the tutorial room with the detector podium, shelves, and decor.
* `TutorialStepMarker.cs`: the glowing floor marker the player walks to during the movement step.

### Scenes: `Assets/Simulation/Scenes`

* `StartScene.unity`: the entry point. A large empty room where the player tries both movement styles and picks one, which then stays locked for the rest of the session.
* `TutorialRoom.unity`: the nine step guided tutorial, with narration, a detector on a podium, and two teaching barrels that show what to submit and what not to submit.
* `RadiationLabRoom.unity`: the main mission. The randomized lab where the player finds the hidden source. This is the scene you tune in the Inspector.
* `BasicVRRoom.unity`: a developer scene for the earlier full VR room. It is not part of the shipped flow.
* `RandomRoomBarrels.unity`: a developer scene for the mixed reality room variant. It is not part of the shipped flow.

### Art and content: `Assets`

* `Assets/Arms/arms.fbx`: the skinned arm mesh used by the arm rig. Overwrite this file in place to swap the arms without breaking references.
* `Assets/Audio`: the win chime, the loss sting, an attribution note, and a `Narration` folder holding the tutorial voiceovers and the start screen prompt.
* `Assets/Models`: the barrel models and the identiFINDER model.
* `Assets/Materials`: barrel, arm, and detector materials.
* `Assets/Prefabs`: the identiFINDER prefab and the teleport particle prefabs.
* `Assets/Textures`, `Assets/UI`, `Assets/WallShelves`: image, interface, and shelf art.
* `Assets/Simulation/Resources`: assets the simulation loads at runtime.

### Meta SDK and sample scaffolding

These folders come from the Meta sample this project was built on. You normally do not edit them.

* `Assets/PassthroughCameraApiSamples`: Meta's passthrough camera sample, including its own scenes. The project was scaffolded from this.
* `Assets/MetaAssets`, `Assets/MetaXR`, `Assets/Oculus`, `Assets/Resources`, `Assets/XR`, `Assets/Plugins/Android`: Meta and Unity XR settings, prefabs, and platform plugins.
* `Packages/manifest.json`: the package list, including OpenXR and the Meta Mixed Reality Utility Kit.
* `ProjectSettings`: the Unity project settings, including the exact Unity version in `ProjectVersion.txt`.

## How to run it on your own Quest in Unity

### What you need

* A Windows or macOS computer with Unity Hub.
* Unity version 6000.4.10f1 (the exact version listed in `ProjectSettings/ProjectVersion.txt`). Install it through Unity Hub with the Android Build Support module, which includes the Android SDK, the NDK, and OpenJDK.
* A Meta Quest 3 or 3S headset and a USB C cable that carries data.
* A Meta developer account, with developer mode turned on for the headset.

### 1. Get the project

Clone the repository to your computer. In Unity Hub choose Add, then select the project folder.

### 2. Open it in Unity

Open the project with Unity 6000.4.10f1. The first open takes a while because Unity imports every asset and restores the packages from `Packages/manifest.json`. Let it finish before you do anything else.

### 3. Turn on developer mode for the headset

In the Meta Horizon phone app, open Devices, select your headset, open Headset Settings, then Developer Mode, and turn it on. Put the headset on, connect the USB cable, and accept the Allow USB Debugging prompt inside the headset.

### 4. Set the build target

Open File, then Build Profiles (or Build Settings on older layouts). Choose the Android platform and switch to it. Make sure the scene list has `StartScene` first, then `TutorialRoom`, then `RadiationLabRoom`. The Meta template already sets the player options that Quest needs, which are IL2CPP scripting, the ARM64 architecture, and the Vulkan graphics API.

### 5. Build and run on the headset

With the headset connected and awake, choose Build And Run. Unity builds the Android package and installs it on the headset. When it launches you start in the title room, pick a movement style, walk through the tutorial, and then play the mission.

### 6. Test in the editor without a headset

You can also press Play in the Unity editor to test on the desktop. Keyboard keys stand in for the controller. In the lab, the N key submits a reading and the G key mounts the detector to the camera. On the start screen, the 1 and 2 keys pick the movement style. These editor helpers do not ship in the headset build.

## Controls

Start screen:
* Point at a button with the controller and pull the trigger to click it.
* Pick Locomotion or Teleportation, try it out in the room, then press Continue. Your choice is locked for the rest of the session.

Tutorial and mission:
* Move: with Locomotion, push the left thumbstick to walk and push the right thumbstick left or right to snap turn. With Teleportation, aim and pull the right trigger to jump to the marker.
* Grip near the detector to pick it up with either hand. Release the grip to drop it.
* Grip near a door knob to grab the door and swing it with your hand.
* Aim the detector at a barrel and press A to submit a reading. When you hold the detector in your left hand you can also press X.
* Poke the wall START button in the lab to begin a new randomized round.

## Customizing the game

Almost everything is tunable from the Unity Inspector without touching code. Open `RadiationLabRoom.unity`, select the `RadiationLabRoomSpawner` object, and look at the grouped fields. Every field has a hover tooltip. See `CUSTOMIZING.md` for a full walkthrough, including how to change room size, barrel counts, difficulty, the shelves, the audio, and the arms, plus a complete reference for every randomization and barrel placement knob.

One important rule: the scene file stores its own copy of these values, so changing a default in the C# script does not change the game. Edit the value on the component in the Inspector instead.

## Architecture

For a deeper explanation of the scene flow, the randomization pipeline, the radiation physics, and the arm rig, read `ARCHITECTURE.md`.

## Attribution

This project was scaffolded from Meta's official passthrough camera sample for Unity. The sample license is preserved in `META_SAMPLE_LICENSE.txt`. Audio credits are in `Assets/Audio/Audio Attribution.txt`.
