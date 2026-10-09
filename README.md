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
8. [Upper-Limb Kinematics (7 DOF)](#upper-limb-kinematics-7-dof)
9. [Input Sources and Calibration](#input-sources-and-calibration)
10. [UDP Communication Protocol](#udp-communication-protocol)
11. [Recorded Data](#recorded-data)
12. [MATLAB / Python Integration](#matlab--python-integration)
13. [Keyboard and Controller Shortcuts](#keyboard-and-controller-shortcuts)
14. [Editor Setup Tool and Tests](#editor-setup-tool-and-tests)
15. [Customization](#customization)
16. [Authors & Affiliations](#authors--affiliations)
17. [License](#license)

---

## Overview

FAMOT (Framework for Arm Movement Tracking) is an open-source Unity application designed to support research in **target achievement control (TAC)** for evaluating upper limb motor functions in the virtual rehabilitation domain. It provides a **direct-mapped interface** where a virtual arm mirrors the user's real arm in real time across **7 degrees of freedom** (shoulder 3, elbow 1, forearm 1, wrist 2), leveraging the **rubber hand illusion** principle to create proprioceptive feedback and a sense of ownership over the virtual limb.

FAMOT is built for:

- **Physiotherapists** conducting rehabilitation sessions
- **Researchers** collecting motor function data and running experiments
- **Clinicians** evaluating upper limb impairments (stroke, traumatic brain injury, neurological disorders)

---

## Key Features

- **Direct-Mapped Interface** — one-to-one mapping between the user's real arm and an anatomically identical virtual arm
- **Full upper-limb kinematics** — analytic inverse kinematics from a tracked hand (controller or hand tracking) and forward kinematics from externally supplied joint angles, both expressed in the same anatomical 7-DOF convention
- **Dual Visualization Modes** — Desktop 3D and Immersive VR (OpenXR); one VR scene supports left- and right-handed participants and shows both arms
- **Device-Agnostic Input** — VR controllers, OpenXR hand tracking (26 joints per hand, fingers retargeted onto the model), or external sensors (IMU, EMG, motion capture, datagloves) via UDP
- **Eye tracking** — OpenXR eye-gaze pose, gaze raycast and gaze target classification (live arm / ghost arm / environment)
- **Seated calibration** — one gesture measures shoulder offset, arm length and the device-to-hand orientation; the avatar is scaled to the participant
- **MATLAB / Python Integration** — legacy 2-DOF protocol kept unchanged, new 7-DOF, event and query messages, plus an outbound per-frame data stream
- **Built-in protocol runner** — random targets inside the calibrated range of motion with dwell-time success when no external sender is present
- **Per-subject data recording** — every frame of kinematics, tracking and gaze, plus events, calibration and raw UDP, under `StreamingAssets/FAMOT_Data/<subject>/<session>/`
- **Subject management** — subject registry with reusable IDs and optional demographics; sessions accumulate per subject
- **Experimenter panel** — desktop mirror overlay with live joint angles, gaze target, recording and UDP status
- **Analysis tooling** — Python replay script that plots joint angles, target error, gaze share and hand paths and writes a session summary

---

## Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                        External Software (MATLAB / Python)                   │
│   C,*  T,*  K,*  E,*  Q  ──────────────────────▶  UDP :8400 (commands)        │
│   ◀──────────────────────────────  UDP :8401 (per-frame "D" stream, "S" reply)│
└──────────────────────────────────────────────────────────────────────────────┘
                                    │
┌───────────────────────────────────▼──────────────────────────────────────────┐
│  FAMOT (Unity 6)                                                             │
│                                                                              │
│  Scene0 (login)  ──▶  Scene1R / Scene1L (desktop)   or   Scene1_VR (OpenXR)  │
│                                                                              │
│  FAMOT Managers                                                              │
│   • UdpCommandServer / UdpStreamSender      • XrTrackingService (head, gaze, │
│   • ExperimentController (phases, targets,    controllers, XR Hands joints)  │
│     counters, LED, success evaluation)      • GazeRaycaster                  │
│   • ProtocolRunner (built-in targets)       • SessionRecorder (CSV streams)  │
│   • ExperimentSceneBootstrap (login → scene, session start, shortcuts)       │
│                                                                              │
│  ArmAnchor_R / ArmAnchor_L  (ShoulderAnchor + ArmDriver + FingerRetargeter)  │
│   └ Male_*_Hand (ArmRig = analytic FK/IK on the bones, gaze colliders)       │
│   └ Male_*_Hand_Ghost (ArmRig driven by target angles)                       │
│                                                                              │
│  SessionManager (persistent) ── DataPaths / SubjectRegistry / session.json   │
└──────────────────────────────────────────────────────────────────────────────┘
```

---

## System Requirements

| Requirement | Details |
|---|---|
| **Unity Version** | Unity 6 (`6000.5.10f1`) |
| **Render Pipeline** | Universal Render Pipeline (URP) 17.2 |
| **XR** | OpenXR 1.18, XR Interaction Toolkit 3.6, XR Hands 1.9, Input System 1.20 |
| **Platform** | Windows standalone (PC VR); Android/Quest build structure present |
| **VR Headsets** | Any OpenXR headset. Eye tracking and hand tracking tested design target: Meta Quest Pro over Link/Air Link (enable eye and hand tracking in the Meta PC app). Quest 3/3S: hands yes, gaze falls back to head ray |
| **External Software** | MATLAB R2020b+ (included scripts) or any UDP-capable application; Python 3.9+ for the analysis script |

---

## Getting Started

1. Install **Unity 6000.5.10f1** via Unity Hub and open the project folder.
2. Open `Assets/!Project/Scenes/Scene0.unity` and press **Play**, or build a Windows player.
3. On the login screen:
   - choose **New subject** or an existing subject from the *Existing Subject* dropdown
   - enter / confirm the **Subject Name**; the 4-character **Subject ID** is generated and reused for returning subjects
   - select **Hand Preference**, **Visualization Mode** and **Input Source** (UDP, controller, hand tracking or auto)
   - optionally adjust the wrist/forearm limits and fill in age, sex, affected side and notes
4. Click **Enter FAMOT**. Recording starts as soon as the experiment scene loads.
5. In VR, sit upright, let both arms hang relaxed with thumbs forward and hold **both grips + both triggers for one second** (or press **C** on the keyboard) to calibrate.
6. Start your MATLAB/Python sender (see below) or press **P** to run the built-in protocol.

> **Clean project checkout?** Run **FAMOT ▸ Setup ▸ Configure Everything** once in the Editor. It wires the scenes, enables the OpenXR hand-tracking and eye-gaze features and sets the build scene list. The command is idempotent.

---

## Project Structure

```
FAMOT/
├── Assets/!Project/
│   ├── Scenes/
│   │   ├── Scene0.unity            # Login / subject registration
│   │   ├── Scene1R.unity           # Right hand — desktop 3D
│   │   ├── Scene1L.unity           # Left hand — desktop 3D
│   │   └── Scene1_VR.unity         # Immersive VR, both arms, handedness chosen at login
│   ├── Scripts/                    # assembly FAMOT.Runtime
│   │   ├── Core/        ArmSide, UpperLimbAngles, AnatomicalMath, TrackedPose
│   │   ├── Kinematics/  ArmRestPose, ArmSolver (FK/IK/decompose), ArmRig (bones)
│   │   ├── Avatar/      ArmDriver, ArmCalibration, ShoulderAnchor, FingerRetargeter, ArmGazeColliders
│   │   ├── Input/       XrTrackingService, GazeRaycaster
│   │   ├── Protocol/    UdpMessage(Parser), UdpCommandServer, UdpStreamSender, StreamFormatter, LegacyMapping
│   │   ├── Session/     DataPaths, SubjectInfo, SubjectRegistry, SessionInfo, SessionManager, JointLimits, LaunchConfig
│   │   ├── Recording/   CsvWriter, EventLogger, RawUdpLogger, SessionRecorder
│   │   ├── Experiment/  ExperimentController, ProtocolRunner, ExperimentSceneBootstrap
│   │   ├── UI/          LoginController, ExperimenterPanel
│   │   ├── Editor/      FamotSetupTools (FAMOT ▸ Setup menu)
│   │   ├── UDPSender.m, UDPSender_Test.m, UDPSender_7DOF.m, UDPReceiver_Stream.m
│   │   └── (legacy) UDPReceiver.cs, SetTarget.cs, CameraController.cs, HomePosition.cs, UtilityLoader.cs
│   ├── Tests/EditMode/             # NUnit tests (assembly FAMOT.Tests.EditMode)
│   └── Media/Prefabs/              # Male_Right_Hand, Male_Left_Hand (+ Ghost variants)
├── Assets/StreamingAssets/FAMOT_Data/   # recorded data (git-ignored)
├── Tools/famot_replay.py           # analysis / replay script
└── README.md
```

---

## Application Workflow

### Login (Scene0)
Subject selection, hand, visualisation mode, input source and limits. The subject is written to `FAMOT_Data/subjects.csv` and `FAMOT_Data/<ID>_<Name>/subject.json`.

### Experiment scene
1. `ExperimentSceneBootstrap` selects the live arm (dominant side), the ghost arm and, in VR, the other arm; it starts a session folder and recording.
2. **Calibration mode** — `C,R/F/E/P/S` commands show the requested pose. When the live arm is externally driven the pose is shown on it, otherwise on the ghost arm as a demonstration.
3. **Test mode** — targets arrive by UDP (`T` or `K`) or from the built-in protocol runner. The ghost arm shows the target; the live arm follows the participant (tracking) or the sender's input values. The LED turns green on success (sender `S` flag, or the built-in evaluator: every targeted joint within the threshold for the dwell time).
4. **Return / Home** closes the session (writes `endUtc`) and returns to the login scene.

---

## Upper-Limb Kinematics (7 DOF)

All joint angles are **signed degrees relative to the calibrated rest pose** (0 = rest) and are independent of side:

| # | Name | + direction |
|---|---|---|
| 0 | `shoulder_flexion` | forward elevation |
| 1 | `shoulder_abduction` | away from the trunk |
| 2 | `shoulder_rotation` | external (lateral) rotation |
| 3 | `elbow_flexion` | flexion |
| 4 | `forearm_supination` | palm up (negative = pronation) |
| 5 | `wrist_extension` | dorsiflexion (negative = flexion) |
| 6 | `wrist_radial` | radial deviation, towards the thumb |

`ArmSolver` is a pure analytic solver:

- **Forward kinematics**: humerus direction from flexion/abduction, axial twist, elbow bend about the plane normal, forearm twist (split between the elbow and forearm twist bones), wrist flexion and deviation in a forearm-fixed anatomical basis.
- **Inverse kinematics**: two-bone IK from the shoulder anchor to the tracked wrist, elbow placed on the solution circle by a hint (below/outside the shoulder-wrist line, swung by hand roll), humeral rotation chosen so the elbow axis matches the arm plane, forearm/wrist angles extracted from the tracked hand basis (Y-X-Z Euler in the forearm frame). The result is run through forward kinematics so the rendered pose and the logged angles always agree.
- **Decomposition** recovers the 7 angles from arbitrary bone rotations (used for logging and tests).

The rest pose is captured once from the model (palm down, fingers forward) in the shoulder anchor's frame, so moving or scaling the anchor moves the kinematic definition with it.

---

## Input Sources and Calibration

`ArmDriver.requestedSource`:

| Source | Behaviour |
|---|---|
| `Auto` | hand tracking when the hand is visible ▸ controller ▸ external angles |
| `Controller` | grip pose of the XR controller; fingers curl with grip/trigger |
| `HandTracking` | XR Hands wrist joint; all 26 joints retargeted to the model's finger bones |
| `ExternalAngles` | forward kinematics from UDP (`T`/`K`) or the protocol runner |

**Seated calibration** (`C` key, both grips + triggers, or `ArmDriver.CalibrateFromRestPose()`): the participant sits upright with the arm hanging relaxed, thumb forward. From the head pose and the tracked device FAMOT derives the device→hand orientation offset, the wrist offset, the shoulder position relative to the head (yaw frame), the arm length and the model scale. Results are stored in `calibration.csv` and `session.json` and applied to the `ShoulderAnchor`, which then follows the headset with a yaw-only rotation.

---

## UDP Communication Protocol

FAMOT listens on **UDP 8400** (configurable on `UdpCommandServer`). Messages are UTF-8, comma separated, `.` decimal point. Unknown or malformed messages are logged and ignored.

### Inbound messages

| Message | Description |
|---|---|
| `C,R` `C,F` `C,E` `C,P` `C,S` | Calibration poses: rest, max flexion, max extension, max pronation, max supination |
| `C,N,minFE,maxFE,minPS,maxPS,numTargets` | 2-DOF limits (degrees) and targets per trial |
| `T,t1,t2,i1,i2[,S\|F]` | Legacy 2-DOF test: target and input in 0–100 (50 = rest), piecewise-mapped to the limits (see below); optional success flag |
| `K,t0..t6,i0..i6[,S\|F]` | 7-DOF test: 7 target + 7 input angles in degrees relative to rest, joint order as in the table above |
| `E,NAME[,value[,detail]]` | Event marker. Recognised names: `TRIAL_START`, `TRIAL_END`, `TARGET_START`, `TARGET_END`, `REST_START`, `REST_END`, `TEST_START`, `MARK`. Any name is logged to `events.csv`; `detail` may contain commas |
| `Q` | Query; FAMOT replies to the sender with `S,<version>,<recording 0/1>,<sessionId>,<port>` |

Legacy piecewise mapping (unchanged): input 0 → `min`, 50 → 0°, 100 → `max`.

### Outbound stream (UDP 8401, one line per frame)

```
D,unity_time,frame,phase,trial,target,
  live_ShF,live_ShA,live_ShR,live_ElF,live_FaS,live_WrE,live_WrR,
  target_ShF,...,target_WrR,
  head_px,head_py,head_pz,head_qx,head_qy,head_qz,head_qw,
  gaze_px,gaze_py,gaze_pz,gaze_dx,gaze_dy,gaze_dz,
  lwrist_valid,lwrist_px,py,pz,qx,qy,qz,qw,
  rwrist_valid,rwrist_px,py,pz,qx,qy,qz,qw,
  status            (S | F | N)
```

Target address and rate are set on `UdpStreamSender` (default `127.0.0.1:8401`, every frame).

---

## Recorded Data

Root: `Assets/StreamingAssets/FAMOT_Data/` (in builds: `<Player>_Data/StreamingAssets/FAMOT_Data/`; on Android the persistent data path is used). Positions are in Unity **world** metres; the XR Origin pose is stored in `session.json` and re-logged in `events.csv` whenever it changes, so tracking space can be reconstructed offline.

```
FAMOT_Data/
  subjects.csv                               subject index
  {ID}_{Name}/
    subject.json                             demographics, handedness, joint limits, session count
    {yyyy-MM-dd_HHmmss}_{Desktop|VR}_{L|R}/
      session.json       device, versions, input source, UDP ports, calibration per arm, XR origin, CSV headers
      calibration.csv    one row per calibration (side, source, shoulder offset, arm length, model scale, offsets)
      kinematics.csv     per frame: phase, trial, target, status, live/target/other-arm 7-DOF angles, DOF mask,
                         max error, dwell progress, joint positions, ghost wrist, legacy 0–100 values
      tracking.csv       per frame: head pose, gaze origin/direction, eye-tracked flag, gaze hit (kind, name,
                         point, distance), per side: controller pose/velocity/buttons, hand-tracked wrist and palm,
                         avatar wrist pose
      hand_joints.csv    optional (SessionRecorder.recordHandJoints): 26 joints × (valid, position, quaternion) per hand
      events.csv         unity_time, utc, frame, category, name, value, detail (phases, trials, targets, status,
                         markers, calibration, UDP events, protocol runner)
      udp_raw.log        every inbound datagram (and outbound non-frame replies) with timestamps
```

All rows carry `unity_time` (seconds), `utc` (ISO 8601) and `frame`, so the streams can be joined.

---

## MATLAB / Python Integration

Scripts in `Assets/!Project/Scripts/`:

| Script | Purpose |
|---|---|
| `UDPSender.m` | Original calibration + 2-DOF test workflow (`C,*`, `T,*`) |
| `UDPSender_Test.m` | Mapping verification sweeps |
| `UDPSender_7DOF.m` | 7-DOF protocol with `K` targets and `E` trial/target markers |
| `UDPReceiver_Stream.m` | Receives the outbound `D` stream and plots live vs target wrist extension |

Python example:

```python
import socket, time
sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
to = ("127.0.0.1", 8400)
sock.sendto(b"E,TRIAL_START,1", to)
sock.sendto(b"K,20,10,0,45,30,-10,5,18,9,0,40,25,-8,4,F", to)   # 7 target + 7 input angles
time.sleep(0.05)
```

### Analysis

```bash
pip install -r Tools/requirements.txt
python Tools/famot_replay.py "Assets/StreamingAssets/FAMOT_Data/AB12_Subject/2026-10-09_193000_VR_R"
python Tools/famot_replay.py Assets/StreamingAssets/FAMOT_Data --list
```

Produces `joint_angles.png`, `target_error.png`, `gaze.png`, `hand_paths.png`, `summary.txt/json` in the session's `analysis/` folder.

---

## Keyboard and Controller Shortcuts

| Key / gesture | Action |
|---|---|
| `C` or both grips + both triggers for 1 s | Seated calibration of all tracked arms |
| `M` | Write a marker to `events.csv` |
| `P` | Start / stop the built-in protocol runner |
| `G` | Toggle the gaze debug ray |
| `F1` | Hide / show the experimenter panel |
| `W A S D`, arrows, `Q E`, `R F`, scroll | Desktop fly camera (unchanged) |

---

## Editor Setup Tool and Tests

- **FAMOT ▸ Setup ▸ Configure Everything** — configures the login scene, both desktop scenes and the VR scene, renames the old `Scene1R_VR` to `Scene1_VR`, enables OpenXR hand tracking / eye gaze and sets the build scenes. Individual commands exist for the open scene.
- **Window ▸ General ▸ Test Runner ▸ EditMode** — 160+ tests covering the rotation helpers, the FK/IK solver (round trips, mirror symmetry, reach clamping), the UDP parser, the legacy mapping, the stream formatter, the CSV writer and the subject registry.

---

## Customization

- **Joint limits**: per subject on the login screen (stored in `subject.json`), via `C,N` for the 2-DOF values, or `JointLimits` for all 7 joints (used by the protocol runner and optional IK clamping on `ArmDriver`).
- **Success criterion**: `ExperimentController.successThresholdDegrees` / `successDwellSeconds`; a sender's `S/F` flag always overrides.
- **Protocol runner**: trials, targets per trial, active joints, range fraction, timeouts and rest durations on `ProtocolRunner`.
- **Recording**: toggle streams and the 26-joint hand log on `SessionRecorder`.
- **New input devices**: either send angles over UDP (`K`) or add an `ArmInputSource` branch in `ArmDriver`.
- **New message types**: extend `UdpMessageParser` and handle them in `ExperimentController.OnMessage`.

---

## Authors & Affiliations

| Author | Affiliation | Contact |
|---|---|---|
| **B. Sankar**\* | Department of Mechanical Engineering, Indian Institute of Science (IISc), Bangalore | [sankarb@iisc.ac.in](mailto:sankarb@iisc.ac.in) |
| **Manikandan Shenbagam**\* | Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi | [bmz228039@cbme.iitd.ac.in](mailto:bmz228039@cbme.iitd.ac.in) |
| **Biswarup Mukherjee** | Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi | [bmukherjee@iitd.ac.in](mailto:bmukherjee@iitd.ac.in) |

\* Equal contribution

### Lab Websites

- [B. Sankar — IISc](https://sankar.studio/)
- [RISE Lab — IIT Delhi](https://sites.google.com/view/riselabiitd/)

---

## License

This project is open-source. Please see the [LICENSE](LICENSE) file for details, or contact the authors for licensing information.

---

## Acknowledgments

This work was carried out at the Indian Institute of Science (IISc), Bangalore and the Centre for Biomedical Engineering (CBME), Indian Institute of Technology (IIT) Delhi.
