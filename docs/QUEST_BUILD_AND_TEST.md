# Quest Build And Test Guide

## Build To Quest 3/3S

1. Open `c:\Work\SimJam-Project` in Unity Hub.
2. Use the Unity version in `ProjectSettings/ProjectVersion.txt`.
3. Let Unity finish importing and compiling.
4. Plug in the Quest 3/3S with USB.
5. In the headset, accept `Allow USB debugging` if prompted.
6. In Unity, open `File > Build Profiles` or `File > Build Settings`.
7. Select `Android`.
8. Set `Run Device` to the Quest.
9. Click `Build And Run`.

The generated APK is local build output and is intentionally not committed.

## Launch V2

1. When the app opens in the headset, choose `SimJam Scenes`.
2. Choose `RandomRoomBarrels`.
3. Accept Scene / Spatial Data permission if prompted.
4. If room data loads, the app spawns 3-6 barrels on the room floor.

## V2 Controls

- `A`: clear and reshuffle barrel positions, orientations, and counts.
- `B`: clear barrels.
- `X`: randomize count values only.
- Right thumbstick up: make current and future barrels bigger.
- Right thumbstick down: make current and future barrels smaller.

## If It Says Room Data Is Loading

The v2 scene is local to the headset. It uses Meta MRUK / Scene API to read saved Quest room data. No external server is involved.

If it stays on a room-data message for more than about 20 seconds:

1. Quit the app.
2. On the Quest, open `Settings`.
3. Check app permissions for `passthroughcamera`.
4. Allow Spatial Data / Scene permission.
5. Go to `Settings > Physical Space` or `Environment Setup > Space Setup`.
6. Complete and save Space Setup.
7. Reopen the app and choose `SimJam Scenes > RandomRoomBarrels`.

The app now shows clearer room-load messages, including permission failures, missing Quest room scans, poor lighting, and MRUK load failures.

## Launch Full VR

1. When the app opens in the headset, choose `SimJam Scenes`.
2. Choose `BasicVRRoom`.
3. The scene creates a 20 ft x 20 ft office-style VR room and immediately spawns a randomized scenario.

`BasicVRRoom` does not need passthrough, room scanning, Spatial Data permission, or an external server. It is a plain VR scene for testing randomized barrel placement, visibility, walkability, and locomotion.

## Full VR Controls

- `A`: regenerate the whole randomized scenario.
- `B`: clear the current scenario.
- `X`: randomize count values only.
- Left thumbstick: smooth movement.
- Right thumbstick left/right: snap turn.
- Right trigger: teleport to the visible floor target.

Full-VR barrels are fixed and non-grabbable. The scenario generator may place fewer than the requested random count if a high-count layout would hide barrels or block walkable paths.

## Launch Radiation Lab (identiFINDER training scene)

1. When the app opens in the headset, choose `SimJam Scenes`.
2. Choose `RadiationLabRoom`.
3. You spawn in the home room. The identiFINDER detector sits on a lit pedestal nearby.
4. Exactly ONE barrel in the lab room hides a radioactive source each run. There is no
   visual difference — find it with the detector readings, geiger clicks, and haptics.

`RadiationLabRoom` is the radiation-training variant of `BasicVRRoom`. The original
`BasicVRRoom` scene is unchanged and still available.

## Radiation Lab Controls

- Grip near the identiFINDER: pick it up (either hand). Release grip: drop it.
- Grip near a door knob: grab the door, then swing it open or shut with your hand.
  Bring it near the frame and it latches closed with a click.
- The detector screen shows live CPS, uSv/h, and a bargraph. Geiger click rate and
  controller vibration scale with the reading; vibration stays OFF until the signal is
  strong (close + roughly aimed), so distance alone gives no hint.
- Readings obey inverse-square falloff and shielding: a barrel (or wall/door/table)
  between you and the source noticeably weakens the reading.
- `A`: regenerate the whole randomized scenario (new layout + new hidden source).
- `B`: clear the current scenario.
- `X`: re-roll radiation values and move the hidden source to a different barrel.
- `Y`: toggle debug count labels (works only in the Editor or Development builds;
  release builds never show them).
- Left thumbstick: smooth movement. Right thumbstick left/right: snap turn.
  Right trigger: teleport to the visible floor target.

Editor desktop test (no headset): enter Play Mode in `RadiationLabRoom` and press `G`
to mount the detector to the camera; move the Scene/Game camera around to watch the
readings react.

## V1 Test

1. Choose `MultiObjectDetection` from the passthrough sample scenes.
2. Press `A` to start the detection flow.
3. Point the headset at a chair.
4. Press `A` again to place barrels over current chair detections.

V1 uses on-device Unity Inference Engine / YOLO. It does not need an external inference server.
