# FAMOT — Framework for Arm Movement Tracking

**An Open-Source Virtual Reality Application for Target Achievement Control (TAC) Evaluation of Upper Limb Motor Functions**

---

## Citation

If you use FAMOT in your research, please cite the following paper:

> B. Sankar\*, Manikandan Shenbagam\*, and Biswarup Mukherjee, "FAMOT: An Open-Source Virtual Reality Application for Target Achievement Control Evaluation of Upper Limb Motor Functions," *Proceedings of the International Society for Virtual Rehabilitation (ISVR) 2025*, Chicago, USA.

```bibtex
@inproceedings{sankar2025famot,
  title     = {{FAMOT}: An Open-Source Virtual Reality Application for Target Achievement Control Evaluation of Upper Limb Motor Functions},
  author    = {Sankar, B. and Shenbagam, Manikandan and Mukherjee, Biswarup},
  booktitle = {Proceedings of the International Society for Virtual Rehabilitation (ISVR)},
  year      = {2025},
  address   = {Chicago, USA}
}
```

---

## Table of Contents

1. [Overview](#overview)
2. [Key Features](#key-features)
3. [Architecture](#architecture)
4. [System Requirements](#system-requirements)
5. [Getting Started](#getting-started)
6. [Project Structure](#project-structure)
7. [Application Workflow](#application-workflow)
8. [UDP Communication Protocol](#udp-communication-protocol)
9. [MATLAB Integration](#matlab-integration)
10. [Degrees of Freedom](#degrees-of-freedom)
11. [Keyboard Controls (Desktop Mode)](#keyboard-controls-desktop-mode)
12. [Customization](#customization)
13. [Authors & Affiliations](#authors--affiliations)
14. [License](#license)

---

## Overview

FAMOT (Framework for Arm Movement Tracking) is an open-source Unity application designed to support research in **target achievement control (TAC)** for evaluating upper limb motor functions in the virtual rehabilitation domain. It provides a **direct-mapped interface** where a virtual hand mirrors the user's real hand movements in real-time across **7 degrees of freedom** (wrist, elbow, and shoulder joints), leveraging the **rubber hand illusion** principle to create proprioceptive feedback and a sense of ownership over the virtual limb.

FAMOT is built for:

- **Physiotherapists** conducting rehabilitation sessions
- **Researchers** collecting motor function data and running experiments
- **Clinicians** evaluating upper limb impairments (stroke, traumatic brain injury, neurological disorders)

Unlike existing virtual rehabilitation systems that rely on indirect-mapped interfaces (cursors, points, abstract objects), FAMOT directly maps the user's physical hand to an anatomically identical virtual hand model, providing a more intuitive and engaging experience.

---

## Key Features

- **Direct-Mapped Interface** — One-to-one mapping between the user's real hand and a virtual hand model, enhancing proprioceptive feedback
- **Dual Visualization Modes** — Desktop 3D and Immersive VR (via OpenXR-compatible headsets)
- **7 Degrees of Freedom** — Wrist flexion/extension, elbow pronation/supination, and shoulder joint control
- **Device-Agnostic Input** — Compatible with IMU, motion capture, EMG, datagloves, hand controllers, and other input devices via UDP
- **MATLAB/Python Integration** — Receives commands and streams data over UDP protocol, enabling seamless integration with MATLAB, Python, and other software
- **Calibration & Test Modes** — Built-in calibration workflow to establish baseline ranges, followed by target achievement testing
- **Left & Right Hand Support** — Separate scenes for left-handed and right-handed users
- **Configurable Joint Limits** — Adjustable maximum angles for flexion, extension, pronation, and supination via the login screen or ScriptableObject
- **Visual Feedback** — Ghost hand overlay as the target, LED indicator for success/failure status, trial and target counters
- **Subject Management** — Auto-generated subject IDs with per-subject data folders under `StreamingAssets/`
- **Modular Architecture** — Easily extensible for custom rehabilitation games and experiments

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                     External Software                           │
│          (MATLAB / Python / Custom Application)                 │
│                                                                 │
│   Sends calibration commands and test data via UDP protocol     │
└──────────────────────────┬──────────────────────────────────────┘
                           │ UDP (default port 8400)
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                      FAMOT (Unity Application)                  │
│                                                                 │
│  ┌──────────────┐    ┌──────────────────┐   ┌───────────────┐  │
│  │  Scene0       │    │  Scene1R / 1L    │   │  Scene1R_VR   │  │
│  │  (Login UI)   │───▶│  (Desktop 3D)    │   │  (Immersive)  │  │
│  │               │    │                  │   │               │  │
│  │ - Subject ID  │    │ - UDPReceiver    │   │ - OpenXR      │  │
│  │ - Hand pref   │    │ - Virtual Hand   │   │ - XR Origin   │  │
│  │ - Viz type    │    │ - Ghost Hand     │   │ - VR Hands    │  │
│  │ - Max angles  │    │ - Camera Control │   │               │  │
│  └──────────────┘    └──────────────────┘   └───────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐   │
│  │  Core Components                                         │   │
│  │  - UDPReceiver: Threaded UDP listener + message parser   │   │
│  │  - MaximumAngleSO: ScriptableObject for joint limits     │   │
│  │  - PiecewiseMap: 0–100 input → degree angle mapping      │   │
│  │  - CameraController: Desktop fly camera                  │   │
│  │  - HomePosition: Camera reset + scene navigation         │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                 │
│  Input Devices: IMU, EMG, Motion Capture, Datagloves, etc.     │
│  (connected via external software that sends UDP messages)      │
└─────────────────────────────────────────────────────────────────┘
```

---

## System Requirements

| Requirement | Details |
|---|---|
| **Unity Version** | Unity 6 (`6000.2.15f1`) |
| **Render Pipeline** | Universal Render Pipeline (URP) 17.2.0 |
| **XR Runtime** | OpenXR 1.16.1 (for VR mode) |
| **XR Toolkit** | XR Interaction Toolkit 3.2.2 |
| **Platform** | Windows (Standalone); Android XR support structure present |
| **VR Headsets** | Any OpenXR-compatible headset (Meta Quest, HTC Vive, Valve Index, etc.) |
| **External Software** | MATLAB R2020b+ (for included sender scripts) or any UDP-capable application |

---

## Getting Started

### 1. Clone the Repository

```bash
git clone <repository-url>
```

### 2. Open in Unity

1. Install **Unity 6** (version `6000.2.15f1` or compatible) via Unity Hub
2. Open the project folder in Unity Hub
3. Wait for package resolution and asset import to complete

### 3. Run the Application

1. Open `Assets/!Project/Scenes/Scene0.unity` (the Login scene)
2. Press **Play** in the Unity Editor, or build a standalone executable
3. On the login screen:
   - Enter a **Subject Name** (default: `RISE_Lab`)
   - A **Subject ID** is auto-generated (4-character alphanumeric)
   - Select **Hand Preference**: Right or Left
   - Select **Visualization Type**: Desktop Screen or VR
   - Optionally adjust **Maximum Angles** for flexion, extension, pronation, and supination
4. Click **Enter** to proceed to the experiment scene

### 4. Connect External Software

Start your MATLAB or Python script to send UDP messages to `127.0.0.1:8400` (or the configured port). See [UDP Communication Protocol](#udp-communication-protocol) and [MATLAB Integration](#matlab-integration) below.

---

## Project Structure

```
FAMOT_MuscleActivationToolbox_Mani_IITD/
├── Assets/
│   ├── !Project/                          # FAMOT application code and assets
│   │   ├── Scenes/
│   │   │   ├── Scene0.unity               # Login / subject registration screen
│   │   │   ├── Scene1R.unity              # Right hand — Desktop 3D mode
│   │   │   ├── Scene1L.unity              # Left hand — Desktop 3D mode
│   │   │   ├── Scene1R_VR.unity           # Right hand — Immersive VR mode
│   │   │   ├── Scene1R_Test.unity         # Test/debug scene (not in build)
│   │   │   └── Scene2_Vertical.unity      # Experimental vertical layout (not in build)
│   │   ├── Scripts/
│   │   │   ├── UDPReceiver.cs             # Core: threaded UDP listener + hand control
│   │   │   ├── EnterFamot.cs              # Login UI logic + scene routing
│   │   │   ├── MaximumAngleSO.cs          # ScriptableObject for joint angle limits
│   │   │   ├── UtilityLoader.cs           # Welcome message + exit handler
│   │   │   ├── CameraController.cs        # Desktop fly camera (WASD + mouse)
│   │   │   ├── HomePosition.cs            # Camera reset + return to login
│   │   │   ├── SetTarget.cs               # Debug: random target generation (Space key)
│   │   │   ├── TabNavigation.cs           # Tab key focus cycling on login UI
│   │   │   ├── UDPSender.m                # MATLAB: full calibration + test sender
│   │   │   └── UDPSender_Test.m           # MATLAB: mapping verification + sweep tests
│   │   ├── Art/                           # Materials, shaders, icons
│   │   │   ├── Materials/                 # Skin, wall, floor, ghost skin materials
│   │   │   ├── Shaders/                   # WireframeShader, DottedLine shader
│   │   │   └── Icons/                     # FAMOT and institutional logos
│   │   └── Media/
│   │       ├── Prefabs/                   # Male_Right_Hand, Male_Left_Hand, Ghost variants
│   │       └── Images/                    # UI textures and PBR skin textures
│   ├── Resources/
│   │   └── MaximumAnglesSettings.asset    # Default joint angle limits instance
│   ├── StreamingAssets/                   # Per-subject data folders (created at runtime)
│   ├── Settings/                          # URP pipeline assets (Low/Medium/High quality)
│   ├── XR/                                # OpenXR loader configuration
│   ├── Samples/XR Interaction Toolkit/    # XRI 3.0.7 starter assets
│   ├── TextMesh Pro/                      # TMP resources and examples
│   └── [Third-party assets]/              # Mini UI, NatureManufacture VR Hands,
│                                          # Old Table&Chair, ColorSkies, etc.
├── Packages/
│   ├── manifest.json                      # Direct package dependencies
│   └── packages-lock.json                 # Resolved dependency tree
├── ProjectSettings/                       # Unity project configuration
├── .gitignore
└── README.md                              # This file
```

---

## Application Workflow

### Login (Scene0)

1. The user enters their name and selects hand preference (Right/Left) and visualization type (Desktop/VR)
2. A unique 4-character alphanumeric subject ID is auto-generated
3. Maximum joint angles can be adjusted (defaults: Flexion -70°, Extension 70°, Pronation -20°, Supination 90°)
4. On submission, a subject folder is created under `StreamingAssets/{subjectID}_{subjectName}/`
5. The appropriate experiment scene is loaded based on hand and visualization selections

### Calibration Mode

The external software (e.g., MATLAB) sends calibration commands via UDP to guide the user through establishing their range of motion:

1. **Rest (R)** — Hand returns to neutral position
2. **Flexion (F)** — Virtual hand shows maximum flexion pose
3. **Extension (E)** — Virtual hand shows maximum extension pose
4. **Pronation (P)** — Virtual hand shows maximum pronation pose
5. **Supination (S)** — Virtual hand shows maximum supination pose
6. **Set Parameters (N)** — Sends numeric angle limits and target count to the application

### Test Mode

The external software sends continuous test messages containing:
- **Target values** (t1, t2) — Where the ghost hand should be positioned
- **Input values** (i1, i2) — Where the user's actual hand is positioned (from sensor data)
- **Status flag** (S/F) — Success or Fail indicator for visual LED feedback

The user attempts to match their hand (solid virtual hand) to the target (transparent ghost hand).

---

## UDP Communication Protocol

FAMOT listens for UDP datagrams on a configurable port (default: **8400**). Messages are UTF-8 encoded, comma-separated strings.

### Calibration Messages

| Message | Description |
|---|---|
| `C,R` | Set hand to **Rest** position |
| `C,F` | Show **Maximum Flexion** position |
| `C,E` | Show **Maximum Extension** position |
| `C,P` | Show **Maximum Pronation** position |
| `C,S` | Show **Maximum Supination** position |
| `C,N,minFE,maxFE,minPS,maxPS,numTargets` | Set angle ranges and number of targets |

**Example:** `C,N,-70.00,70.00,-20.00,90.00,10` sets flexion to -70°, extension to 70°, pronation to -20°, supination to 90°, with 10 targets.

### Test Messages

| Message | Description |
|---|---|
| `T,t1,t2,i1,i2` | Target (t1,t2) and input (i1,i2) values, range 0–100 |
| `T,t1,t2,i1,i2,S` | Same as above with **Success** LED indicator (green) |
| `T,t1,t2,i1,i2,F` | Same as above with **Fail** LED indicator (grey) |

### Value Mapping (Piecewise Linear)

Input values (0–100) are mapped to joint angles using a piecewise linear function:

| Input Range | Mapping |
|---|---|
| 0 → 50 | `outputMin` → 0° (rest) |
| 50 → 100 | 0° (rest) → `outputMax` |

- **Input = 0** → Maximum flexion / pronation
- **Input = 50** → Rest position (0°)
- **Input = 100** → Maximum extension / supination

This ensures that input = 50 always corresponds to the rest position regardless of asymmetric joint ranges.

---

## MATLAB Integration

Two MATLAB scripts are included in `Assets/!Project/Scripts/`:

### `UDPSender.m` — Full Experiment Sender

Runs a complete calibration-then-test workflow:

1. Sends each calibration pose (R, F, E, P, S) for 3 seconds at 10 Hz
2. Sends `C,N` message with angle ranges and target count
3. Loops through random targets, simulating IMU input converging toward each target
4. Includes rest periods between targets

```matlab
% Quick start — run in MATLAB:
run('UDPSender.m')
```

### `UDPSender_Test.m` — Mapping Verification

Diagnostic script for verifying the piecewise mapping:

1. Prints a mapping table showing input → angle conversions
2. Sweeps FE from 0–100 with PS fixed (pure flexion/extension test)
3. Sweeps PS from 0–100 with FE fixed (pure pronation/supination test)
4. Holds specific positions for 3 seconds each for visual verification

```matlab
% Quick start — run in MATLAB:
run('UDPSender_Test.m')
```

### Custom Integration (Python Example)

```python
import socket
import time

sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
target = ("127.0.0.1", 8400)

# Send calibration rest command
sock.sendto(b"C,R", target)
time.sleep(3)

# Set angle ranges: FE [-70, 70], PS [-20, 90], 10 targets
sock.sendto(b"C,N,-70.00,70.00,-20.00,90.00,10", target)
time.sleep(0.5)

# Send a test message: target at (75, 25), input at (60, 30)
sock.sendto(b"T,75.00,25.00,60.00,30.00,F", target)
```

---

## Degrees of Freedom

FAMOT tracks and visualizes **7 degrees of freedom** for the upper limb:

| Joint | DOF | Axis in Unity | Input Channel |
|---|---|---|---|
| **Wrist** | Flexion / Extension | Local X rotation | FE (t1/i1) |
| **Elbow** | Pronation / Supination | Local Y rotation (negated) | PS (t2/i2) |
| **Shoulder** | Additional DOFs | Supported via architecture | Extensible |

### Default Joint Angle Limits

| Parameter | Default Value | Description |
|---|---|---|
| Max Flexion | -70° | Wrist flexion limit |
| Max Extension | 70° | Wrist extension limit |
| Max Pronation | -20° | Elbow pronation limit |
| Max Supination | 90° | Elbow supination limit |

These can be modified on the login screen or by editing the `MaximumAnglesSettings` ScriptableObject asset in `Assets/Resources/`.

---

## Keyboard Controls (Desktop Mode)

### Camera Movement

| Key | Action |
|---|---|
| `W` / `A` / `S` / `D` | Move camera (local space) |
| Arrow Keys | Move camera (world space) |
| `Left Shift` | Hold for fast movement |
| `R` / `F` | Zoom in / out (dolly) |
| `Q` / `E` | Rotate around world Y axis |
| `Z` / `C` | Rotate around local Y axis |
| Mouse Scroll | Zoom toward mouse cursor (raycast-based) |

### UI Navigation

| Key | Action |
|---|---|
| `Tab` | Cycle focus between login form fields |
| `Enter` | Submit (when button is focused) |

### Debug

| Key | Action |
|---|---|
| `Space` | Set random target angles (in test scenes with `SetTarget` component) |

---

## Customization

### Adding New Input Devices

FAMOT is device-agnostic by design. To integrate a new input device:

1. Write a sender application (in MATLAB, Python, C++, etc.) that reads your device data
2. Normalize your sensor readings to the 0–100 range
3. Send UDP messages following the [protocol specification](#udp-communication-protocol)

### Modifying Joint Limits

- **At runtime**: Adjust values on the login screen before entering the experiment
- **Via UDP**: Send a `C,N,minFE,maxFE,minPS,maxPS,numTargets` message
- **In the Editor**: Edit `Assets/Resources/MaximumAnglesSettings.asset` or create a new one via `Create > FAMOT > Maximum Angles Settings`

### Extending the Application

- Add new scenes for different rehabilitation tasks
- Create additional ScriptableObjects for experiment configurations
- Extend `UDPReceiver.cs` to handle new message types
- Add finger-level DOFs by extending the hand prefab hierarchy

---

## Authors & Affiliations

| Author | Affiliation | Contact |
|---|---|---|
| **B. Sankar**\* | Department of Mechanical Engineering, Indian Institute of Science (IISc), Bangalore | [sankarb@iisc.ac.in](mailto:sankarb@iisc.ac.in) |
| **Manikandan Shenbagam**\* | Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi | [bmz228039@cbme.iitd.ac.in](mailto:bmz228039@cbme.iitd.ac.in) |
| **Biswarup Mukherjee** | Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi | [bmukherjee@iitd.ac.in](mailto:bmukherjee@iitd.ac.in) |

\* Equal contribution

### Lab Websites

- [B. Sankar — IISc](https://sankar-mechengg.github.io/home)
- [RISE Lab — IIT Delhi](https://sites.google.com/view/riselabiitd/)

---

## License

This project is open-source. Please see the [LICENSE](LICENSE) file for details, or contact the authors for licensing information.

---

## Acknowledgments

This work was carried out at the Indian Institute of Science (IISc), Bangalore and the Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi.
