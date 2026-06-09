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

## V1 Test

1. Choose `MultiObjectDetection` from the passthrough sample scenes.
2. Press `A` to start the detection flow.
3. Point the headset at a chair.
4. Press `A` again to place barrels over current chair detections.

V1 uses on-device Unity Inference Engine / YOLO. It does not need an external inference server.
