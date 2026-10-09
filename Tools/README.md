# FAMOT offline analysis tools

`famot_replay.py` analyses one recorded FAMOT session (the folders the Unity app writes under
`StreamingAssets/FAMOT_Data/`) and produces figures plus a numeric summary. It needs Python 3.9+
and only pandas, numpy and matplotlib.

```
pip install -r requirements.txt
```

## Usage

```
# analyse one session (figures are saved to <session_dir>/analysis and shown)
python famot_replay.py "StreamingAssets/FAMOT_Data/001_Alice/2026-10-09_193000_VR_R"

# headless, custom output folder / resolution, only some joints
python famot_replay.py <session_dir> --out results --no-show --dpi 200 --joints ShF,ElF,WrE

# list subjects and sessions (FAMOT_Data root or a subject folder), then exit
python famot_replay.py "StreamingAssets/FAMOT_Data" --list
```

Both `/` and `\` path separators are accepted.

| Option | Meaning |
| --- | --- |
| `--out DIR` | Output folder (default `<session_dir>/analysis`) |
| `--no-show` | Only save files, do not open figure windows |
| `--dpi N` | PNG resolution (default 150) |
| `--joints A,B` | Joints to plot in `joint_angles.png`: `ShF ShA ShR ElF FaS WrE WrR` |
| `--list` | Print subjects / sessions found (mode, hand, start/end from `session.json`) and exit |

## Outputs

| File | Content |
| --- | --- |
| `joint_angles.png` | Live angle (solid) vs. target angle (dashed, only where the joint's bit in `target_dof_mask` is set), green spans where `status == Success`, vertical lines for trial (solid) and target (dotted) events |
| `target_error.png` | `max_error_deg` over time, success-threshold line if an event `detail` mentions a threshold/tolerance, `dwell_progress` on a twin axis |
| `gaze.png` | Categorical strip of `gaze_hit_kind` over time, eye-tracking availability strip, time share per kind |
| `hand_paths.png` | Top (x-z) and side (z-y) views of live/ghost wrist and tracked controller / hand wrist paths (o = start, X = end) |
| `summary.txt`, `summary.json` | Session metadata, duration, median frame rate, per-trial and per-target statistics (time to achieve = first `Success` after target start, final error, mean abs error per joint), gaze share per kind, calibration rows, UDP command counts (first character of the message in `udp_raw.log`), hand / controller tracking share |

Gaze shares are time-weighted; frames without a hit are reported as `NoHit` and frames with
`gaze_valid == 0` as `Invalid`.

## Robustness

A missing or empty input file only skips the figures / summary sections that need it (a warning is
printed). Empty CSV fields are read as NaN, and unexpected or missing columns never raise: they are
treated as all-NaN.
