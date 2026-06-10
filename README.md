#START README
#Contributors: Garrett King-Cody, Emily Christmas, Lauren Frank, Milo Schoenen

Outline
1. Introduction
2. Features
3. System Requirements
4. Tools
5. Project Contents
6. Setup and Run Project
7. Controls
8. Known Issues and Notes

1. Introduction

   VR Block Programming Educational Game

   A AR-based visual block programming educational game built in Unity and targeting Meta Quest devices.
   Users physically arrange visual programming blocks (similar to Scratch or Blockly) inside a augmented
   reality environment to control vehicles in a 3D world. The game pairs the BlocksEngine2 visual
   programming framework with the Meta XR SDK to deliver a hands-on coding experience, where learners
   drag blocks out of a floating palette, snap them into a programming canvas, and watch their logic
   drive a Speeder or a Miner craft around a placeable game zone. The application supports two crafts
   simultaneously with independent execution, integrates a tutorial flow with progressive challenges,
   and provides a built-in canvas keyboard for variable and text input. The block editor renders as a
   floating WorldSpace panel that the player can summon with a wrist-tap gesture, grab, and reposition
   anywhere in the room. The runtime is OpenXR-based and uses Meta's Interaction SDK (ISDK) for grab,
   ray, and poke interactions. Capstone project at Washburn University.


2. Features
   - AR block-programming editor displayed as a floating WorldSpace canvas
   - Drag-and-drop blocks from palette into the programming canvas
   - Multiple block categories: Events, Motion, Control, Operations, Variables, Functions
   - Multiple target crafts (Speeder + Miner) with independent Play/Stop
   - Save and load programs as XML files
   - Radial thumbstick context menu (Block Editor / Reset Vehicles / Place Zone)
   - Game zone placement with parabolic arc preview and floor-plane fallback
   - Smooth locomotion via right thumbstick (head-relative)
   - Wrist-tap summon/dismiss gesture for the block editor panel
   - Canvas-based AR keyboard for text and numeric input
   - Tutorial menu and progressive challenge system (goal zones, shoot targets)
   - Per-craft execution: each craft runs its own program independently
   - Movement scaling so 1 block step = 1 local unit on the placed environment
   - Hand tracking visuals when controllers are not held
   - Reset Vehicles action returns crafts to their captured home positions
   - ISDK grab interaction for repositioning the floating panel and the 3D environment


3. System Requirements

   Recommended Hardware Configuration:

      - Microsoft Windows 10 / Windows 11 (64-bit)
      - Processor - Intel Core i7 or AMD Ryzen 7 (or equivalent)
      - Memory - 16GB RAM (8GB minimum)
      - GPU - DirectX 11 capable; NVIDIA GTX 1660 / AMD RX 5600 or better (Editor only)
      - Storage - 30GB free disk space (Unity Editor + project + Library cache)
      - USB-C cable (for Quest Link and sideloading)

   AR Hardware:

      - Quest 3 / Quest 3S
      - Two Touch controllers (hand tracking is also supported)
      - At least 2m x 2m of clear roomscale play area


4. Tools
   - To build, deploy, and run this project you will need the following tools:
   - Unity Hub
      - Unity Editor 6000.3.10f1 (Unity 6) - exact version required
      - Android Build Support module (with OpenJDK and the Android SDK and NDK Tools)
   - Meta Quest Developer Hub (optional but recommended)
      - Used for sideloading APKs, casting the headset view, and toggling developer mode
   - Android Debug Bridge (adb)
      - Bundled with Unity at:
        C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe
   - Unity Version Control (Plastic SCM / cm command line)
      - Workspace root and Unity project root
      - Main branch: /main   Developer branch: /main/Garretts Beta Branch
   - Meta Quest device with Developer Mode enabled
      - Sign up as a developer at developer.oculus.com and verify the account
      - Enable Developer Mode from the Meta mobile app on a paired phone


5. Project Contents

   Included in the project root are the following:

   ignore.conf                Plastic SCM ignore rules
   Readme_King.txt            This file
   Assets/                    All Unity assets, scripts, scenes, prefabs and materials
   Packages/                  Unity package manifest and lockfile (Meta XR SDK, OpenXR, etc.)
   ProjectSettings/           Unity per-project settings (XR, build, graphics, input)
   Library/ (generated)       Editor cache - holds the 72k-variant shader compile cache

   Key Scenes (under Assets/BlocksEngine2/Scenes/):
      BE2MultipleTargetObjects.unity    Main scene - Speeder + Miner crafts (default)
      BE2SampleScene.unity              Single-target demo scene

   Custom XR Scripts (under Assets/Scripts/XR/):
      BE2_VRInputManager.cs             Routes VR controller raycasts into the BE2 input pipeline
      BE2_VRRenderTextureSetup.cs       Configures BE2 canvases for direct VR WorldSpace display
      BE2_VRPanel.cs                    Floating block-editor panel anchor
      BE2_VRDeleteZone.cs               Drop zone that destroys blocks released over it
      BE2ControllerMenuController.cs    Visibility toggle for the floating panel
      FloatingBE2Panel.cs               Positions the panel in front of the user when summoned
      RadialMenuController.cs           Left-thumbstick radial context menu
      VRInteractionWiring.cs            Routes radial-menu events to the relevant subsystems
      VRKeyboardManager.cs              Canvas-based VR keyboard for text input
      SmoothLocomotion.cs               Right-thumbstick head-relative movement
      GameZoneInteractable.cs           ISDK grab handler for the 3D environment
      XRGameZonePlacementManager.cs     Arc-based game zone placement
      EnvironmentBoundary.cs            Clamps craft positions to ground bounds

   Custom Challenge Scripts (under Assets/Scripts/Challenges/):
      TutorialMenuController.cs         Tutorial flow; emits OnTutorialComplete event
      ChallengeManager.cs               Spawns challenges after the tutorial completes
      ChallengeGoalZone.cs              Procedural green disc with proximity detection
      ChallengeShootTarget.cs           Shoot-target challenge variant


6. Setup and Run Project

   Set up the Workspace
      - Install Unity Hub from https://unity.com/download
      - Install Unity Editor 6000.3.10f1 with the Android Build Support module
      - Connect to the team Plastic SCM repository
         - Open the Plastic SCM client, log in, and clone the project
         - Switch to /main, or to your developer branch under /main

   Open the Project
      - Launch Unity Hub
      - Click Add and select Add project from disk
      - Browse to the workspace root (the folder containing Assets/, Packages/ and ProjectSettings/) and select it
      - Pick Unity 6000.3.10f1 as the editor version
      - On first open Unity will recompile and import - this can take 10 to 30 minutes

   Configure the Build Profile (Unity 6 uses Build Profiles, NOT Build Settings)
      - In Unity, open File - Build Profiles
      - Select the Android profile and click Switch Profile if it is not already active
      - Set Run Device to your connected Quest (click Refresh List if not visible)
      - Verify Texture Compression is set to ASTC
      - Open Edit - Project Settings - Player - Other Settings and set Active Input Handling to Old Input Manager

   Verify XR / OpenXR Settings
      - Edit - Project Settings - XR Plug-in Management - Android tab
      - Make sure OpenXR is checked
      - Under OpenXR - Interaction Profiles, confirm Oculus Touch Controller and Meta Quest Touch Pro Controller are listed
      - Under OpenXR Feature Groups, enable OpenXRCompositionLayersFeature

   Build the APK
      - File - Build Profiles - Android - click Build
      - Always overwrite the same APK path on subsequent builds

   Deploy to the Quest (without rebuilding)
      - Connect the Quest by USB-C and accept the Allow USB Debugging prompt inside the headset
      - Install the APK:
        "C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe" install -r
      - Launch the app:
        "C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe" shell am start -n com.DefaultCompany.vreducationgamelocal/com.unity3d.player.UnityPlayerGameActivity
      - On the Quest, the app appears under App Library - Unknown Sources

   Optional: Editor Play Mode via Quest Link
      - Connect the Quest by Link cable or Air Link and enable Quest Link in the headset
      - Press Play in the Unity Editor


7. Controls

   Right Controller:
      - Trigger              Click/drag blocks in the BE2 panel; confirm zone placement during arc preview
      - Grip                 Scroll the palette (grip + move up/down); grab the 3D Environment via ISDK
      - Thumbstick           Smooth locomotion (head-relative)
      - Thumbstick click     Delete the selected block

   Left Controller:
      - Thumbstick push          Open the radial context menu
      - Thumbstick slide         Highlight a menu option
      - Thumbstick release       Select the highlighted option (returns to center)

   Radial Menu Options:
      - Place Zone           Idle -> arc preview; Preview -> cancel; Spawned -> despawn
      - Block Editor         Toggle the floating BE2 programming panel
      - Reset Vehicles       Return Speeder + Miner to their home positions

   Wrist-tap gesture:
      - Bring both controllers close together and pull right trigger to summon or dismiss the panel


8. Known Issues and Notes
   - Passthrough is currently disabled. The scene is configured for it, but insightPassthroughEnabled in
     OculusProjectConfig is off; enabling it requires four scene/config changes together (see CLAUDE.md).
   - First build on a fresh machine takes about 3 hours because around 72,000 shader variants are
     compiled. Reuse the same test.apk path on every build so the shader cache stays warm; subsequent
     builds finish in 5 to 10 minutes. Never delete Library/Bee/artifacts or Library/ShaderCache.
   - Active Input Handling silently reverts to "Both" on some Editor restarts. Re-set it to "Old"
     before each Android build, or the build will fail with input system errors.
   - Editor Play mode errors without Quest Link (OVRCameraRig MissingReferenceException, GrabAndLocate
     NullRef) are expected and do not affect the Quest build.
   - Do not write custom OVRInput scripts; use the Meta ISDK building blocks (Grabbable,
     GrabInteractable, HandGrabInteractable) for grab interactions.
   - Use the Sprites/Default shader for procedural materials. Universal Render Pipeline/Lit returns null
     at runtime on Quest and produces purple materials.

#END README
