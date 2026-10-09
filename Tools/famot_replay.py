#!/usr/bin/env python3
"""FAMOT session replay / analysis tool.

Reads the CSV / JSON / log files that the FAMOT Unity app writes for one
recorded session and produces publication-style figures plus a numeric summary.

Expected on-disk layout (under ``StreamingAssets/FAMOT_Data/``)::

    FAMOT_Data/
      subjects.csv
      {SubjectID}_{SubjectName}/
        subject.json
        {yyyy-MM-dd_HHmmss}_{Mode}_{Hand}/      e.g. 2026-10-09_193000_VR_R
          session.json  calibration.csv  kinematics.csv  tracking.csv
          hand_joints.csv (optional)  events.csv  udp_raw.log

Usage::

    python famot_replay.py <session_dir> [--out DIR] [--no-show] [--dpi 150]
                           [--joints ShF,ShA,...]
    python famot_replay.py <FAMOT_Data root | subject folder> --list

Outputs (in ``--out``, default ``<session_dir>/analysis``): ``joint_angles.png``,
``target_error.png``, ``gaze.png``, ``hand_paths.png``, ``summary.txt``,
``summary.json``.  A missing input file only skips the figures that need it.

Only Python >= 3.9, pandas, numpy and matplotlib are required.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import re
import sys
import warnings
from collections import Counter, OrderedDict
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Sequence, Tuple

import numpy as np
import pandas as pd

# --------------------------------------------------------------------------
# Constants
# --------------------------------------------------------------------------

#: (short name, column suffix) in the bit order used by ``target_dof_mask``.
JOINTS: Tuple[Tuple[str, str], ...] = (
    ("ShF", "shoulder_flexion"),
    ("ShA", "shoulder_abduction"),
    ("ShR", "shoulder_rotation"),
    ("ElF", "elbow_flexion"),
    ("FaS", "forearm_supination"),
    ("WrE", "wrist_extension"),
    ("WrR", "wrist_radial"),
)

JOINT_LABELS: Dict[str, str] = {
    "ShF": "Shoulder flexion",
    "ShA": "Shoulder abduction",
    "ShR": "Shoulder rotation",
    "ElF": "Elbow flexion",
    "FaS": "Forearm supination",
    "WrE": "Wrist extension",
    "WrR": "Wrist radial dev.",
}

#: Colour-blind friendly colours (Okabe-Ito) for the gaze categories.
GAZE_COLORS: Dict[str, str] = {
    "LiveArm": "#0072B2",
    "GhostArm": "#E69F00",
    "UI": "#CC79A7",
    "Environment": "#009E73",
    "Other": "#8C8C8C",
    "NoHit": "#D9D9D9",
    "Invalid": "#F0E442",
}
_FALLBACK_COLORS = ["#56B4E9", "#D55E00", "#7F3C8D", "#11A579", "#3969AC", "#F2B701"]

SESSION_FOLDER_RE = re.compile(r"^(\d{4}-\d{2}-\d{2})_(\d{6})_([^_]+)_([^_]+)$")

_NUM = r"[-+]?\d+(?:\.\d+)?"
_THRESH_RE = re.compile(
    r"(?i)(?:thresh\w*|toleran\w*)\s*(?:\(\s*(?:deg\w*|°)\s*\))?\s*[=:]?\s*(" + _NUM + ")"
)
_THRESH_KEY_RE = re.compile(r"(?i)thresh|toleran")


# --------------------------------------------------------------------------
# Small helpers (paths, logging, JSON)
# --------------------------------------------------------------------------

def warn(msg: str) -> None:
    """Print a warning to stderr."""
    print(f"[famot_replay] WARNING: {msg}", file=sys.stderr)


def info(msg: str) -> None:
    """Print an informational line to stdout."""
    print(f"[famot_replay] {msg}")


def normalize_path(p: str) -> Path:
    """Return a ``Path`` for ``p`` accepting both ``/`` and ``\\`` separators.

    Surrounding quotes and ``~`` are handled.  On POSIX systems backslashes are
    converted to forward slashes so Windows-style paths still work.
    """
    s = str(p).strip().strip('"').strip("'")
    s = s.replace("\\", "/")
    return Path(os.path.normpath(os.path.expanduser(s)))


def to_jsonable(obj: Any) -> Any:
    """Recursively convert numpy / pandas values into JSON-safe Python values.

    Non-finite floats become ``None``.
    """
    if isinstance(obj, dict):
        return {str(k): to_jsonable(v) for k, v in obj.items()}
    if isinstance(obj, (list, tuple, set)):
        return [to_jsonable(v) for v in obj]
    if isinstance(obj, np.ndarray):
        return [to_jsonable(v) for v in obj.tolist()]
    if isinstance(obj, (bool, np.bool_)):
        return bool(obj)
    if isinstance(obj, (int, np.integer)):
        return int(obj)
    if isinstance(obj, (float, np.floating)):
        f = float(obj)
        return f if math.isfinite(f) else None
    if isinstance(obj, pd.Timestamp):
        return None if pd.isna(obj) else obj.isoformat()
    if obj is None or isinstance(obj, str):
        return obj
    try:
        if pd.isna(obj):
            return None
    except (TypeError, ValueError):
        pass
    return str(obj)


def _r(x: Any, nd: int = 4) -> Optional[float]:
    """Round ``x`` to ``nd`` digits; return ``None`` for NaN / non-numeric."""
    try:
        f = float(x)
    except (TypeError, ValueError):
        return None
    return round(f, nd) if math.isfinite(f) else None


def _nanmean(a: np.ndarray) -> Optional[float]:
    """Mean ignoring NaN; ``None`` if there are no finite values."""
    a = np.asarray(a, dtype=float)
    a = a[np.isfinite(a)]
    return float(a.mean()) if a.size else None


def _fmt_label(v: Any) -> str:
    """Format a trial / target id: ``2.0`` -> ``"2"``; NaN -> ``""``."""
    if v is None:
        return ""
    try:
        if pd.isna(v):
            return ""
    except (TypeError, ValueError):
        pass
    if isinstance(v, (float, np.floating)):
        return str(int(v)) if float(v).is_integer() else str(v)
    if isinstance(v, (int, np.integer)):
        return str(int(v))
    return str(v).strip()


# --------------------------------------------------------------------------
# Robust DataFrame access
# --------------------------------------------------------------------------

def read_csv_safe(path: Path, label: Optional[str] = None, as_text: bool = False) -> Optional[pd.DataFrame]:
    """Read a FAMOT CSV, returning ``None`` (with a warning) if unusable.

    Empty fields become NaN; the literal strings ``None``/``NA`` are kept as
    text so that e.g. a gaze kind named ``None`` is not silently dropped.
    """
    label = label or path.name
    if not path.is_file():
        warn(f"{label}: file not found ({path}); related output will be skipped.")
        return None
    try:
        df = pd.read_csv(
            path,
            encoding="utf-8-sig",
            keep_default_na=False,
            na_values=[""],
            low_memory=False,
            on_bad_lines="skip",
            dtype=str if as_text else None,
        )
    except pd.errors.EmptyDataError:
        warn(f"{label}: file is empty.")
        return None
    except Exception as exc:  # noqa: BLE001 - any parse problem must not crash
        warn(f"{label}: could not be read ({exc}).")
        return None
    df.columns = [str(c).strip() for c in df.columns]
    if df.empty:
        warn(f"{label}: no data rows.")
        return None
    return df


def num(df: Optional[pd.DataFrame], name: str) -> pd.Series:
    """Return column ``name`` as float Series (all-NaN if missing / bad)."""
    if df is None:
        return pd.Series(dtype=float)
    if name not in df.columns:
        return pd.Series(np.nan, index=df.index, dtype=float)
    return pd.to_numeric(df[name], errors="coerce").astype(float)


def txt(df: Optional[pd.DataFrame], name: str) -> pd.Series:
    """Return column ``name`` as stripped strings ("" for missing / NaN)."""
    if df is None:
        return pd.Series(dtype=str)
    if name not in df.columns:
        return pd.Series("", index=df.index, dtype=object)
    return df[name].map(_fmt_label).astype(object)


def flag(df: Optional[pd.DataFrame], name: str) -> Optional[np.ndarray]:
    """Return a boolean array (value > 0) for a 1/0 column, or ``None``."""
    if df is None or name not in df.columns:
        return None
    return (num(df, name).fillna(0.0).to_numpy() > 0)


def time_of(df: pd.DataFrame) -> np.ndarray:
    """Time axis (seconds) for a frame table.

    Uses ``unity_time``; falls back to ``frame`` and finally the row index.
    """
    for col in ("unity_time", "frame"):
        if col in df.columns:
            t = num(df, col).to_numpy()
            if np.isfinite(t).any():
                return t
    return np.arange(len(df), dtype=float)


def has_any(df: Optional[pd.DataFrame], cols: Iterable[str]) -> bool:
    """True if any of ``cols`` exists in ``df`` and has a finite value."""
    if df is None:
        return False
    return any(c in df.columns and num(df, c).notna().any() for c in cols)


def load_json(path: Path) -> Optional[dict]:
    """Load a JSON file into a dict; ``None`` (with warning) on failure."""
    if not path.is_file():
        return None
    try:
        with open(path, "r", encoding="utf-8-sig") as fh:
            data = json.load(fh)
        return data if isinstance(data, dict) else {"_value": data}
    except Exception as exc:  # noqa: BLE001
        warn(f"{path.name}: could not be parsed ({exc}).")
        return None


def _norm_key(k: str) -> str:
    return re.sub(r"[^a-z0-9]", "", str(k).lower())


def pick(d: Optional[dict], keys: Sequence[str], depth: int = 2) -> Any:
    """Find the first value in ``d`` whose key matches one of ``keys``.

    Matching ignores case, underscores and punctuation; nested dicts are
    searched up to ``depth`` levels.  Returns ``None`` if nothing matches.
    """
    if not isinstance(d, dict):
        return None
    wanted = [_norm_key(k) for k in keys]
    lookup = {_norm_key(k): v for k, v in d.items()}
    for w in wanted:
        if w in lookup and lookup[w] not in (None, ""):
            return lookup[w]
    if depth > 0:
        for v in d.values():
            if isinstance(v, dict):
                r = pick(v, keys, depth - 1)
                if r is not None:
                    return r
    return None


def frame_dt(t: np.ndarray) -> np.ndarray:
    """Per-sample duration: forward difference, last = median, gaps clipped.

    Gaps larger than 5x the median (e.g. app paused) are clipped so they do not
    dominate time-weighted shares.
    """
    t = np.asarray(t, dtype=float)
    n = len(t)
    if n == 0:
        return np.zeros(0)
    if n == 1:
        return np.zeros(1)
    d = np.diff(t)
    good = d[np.isfinite(d) & (d > 0)]
    med = float(np.median(good)) if good.size else 0.0
    dt = np.empty(n)
    dt[:-1] = d
    dt[-1] = med
    dt[~np.isfinite(dt) | (dt < 0)] = med
    if med > 0:
        dt = np.minimum(dt, 5 * med)
    return dt


def estimate_fps(t: np.ndarray) -> Optional[float]:
    """Frame-rate estimate: ``median(1/dt)`` over positive finite ``dt``."""
    t = np.asarray(t, dtype=float)
    if t.size < 2:
        return None
    d = np.diff(t)
    d = d[np.isfinite(d) & (d > 0)]
    if not d.size:
        return None
    return float(np.median(1.0 / d))


def runs_of(labels: Sequence[Any], t: np.ndarray) -> List[Tuple[Any, float, float]]:
    """Collapse a label sequence into ``(label, t_start, t_end)`` runs.

    ``t_end`` of a run is the time of the first sample of the next run (or the
    last sample time plus one frame for the final run).
    """
    n = len(labels)
    if n == 0:
        return []
    dt = frame_dt(t)
    out: List[Tuple[Any, float, float]] = []
    start = 0
    for i in range(1, n + 1):
        if i == n or labels[i] != labels[start]:
            t0 = float(t[start])
            t1 = float(t[i]) if i < n else float(t[n - 1] + dt[n - 1])
            if np.isfinite(t0) and np.isfinite(t1):
                out.append((labels[start], t0, max(t1, t0)))
            start = i
    return out


# --------------------------------------------------------------------------
# Session discovery / --list
# --------------------------------------------------------------------------

def is_session_dir(p: Path) -> bool:
    """True if ``p`` looks like a session folder."""
    return p.is_dir() and any(
        (p / f).is_file() for f in ("session.json", "kinematics.csv", "tracking.csv", "events.csv")
    )


def parse_session_folder_name(name: str) -> Dict[str, Optional[str]]:
    """Split ``2026-10-09_193000_VR_R`` into date / time / mode / hand."""
    m = SESSION_FOLDER_RE.match(name)
    if not m:
        return {"date": None, "time": None, "mode": None, "hand": None}
    return {"date": m.group(1), "time": m.group(2), "mode": m.group(3), "hand": m.group(4)}


def _parse_time(v: Any) -> Optional[pd.Timestamp]:
    if v in (None, ""):
        return None
    try:
        ts = pd.to_datetime(v, utc=True, errors="coerce")
    except Exception:  # noqa: BLE001
        return None
    return None if pd.isna(ts) else ts


def session_info(session_dir: Path, meta: Optional[dict]) -> Dict[str, Any]:
    """Collect start / end / mode / hand from ``session.json`` + folder name."""
    parsed = parse_session_folder_name(session_dir.name)
    start = _parse_time(pick(meta, ["start_utc", "started_utc", "session_start_utc", "start_time",
                                    "start", "started", "session_start", "begin_utc", "created_utc"]))
    end = _parse_time(pick(meta, ["end_utc", "ended_utc", "session_end_utc", "end_time",
                                  "end", "ended", "session_end", "stop_utc"]))
    if start is None and parsed["date"]:
        start = _parse_time(f"{parsed['date']} {parsed['time'][:2]}:{parsed['time'][2:4]}:{parsed['time'][4:]}")
    mode = pick(meta, ["mode", "session_mode", "run_mode"]) or parsed["mode"]
    hand = pick(meta, ["hand", "hand_side", "tracked_hand", "side", "arm"]) or parsed["hand"]
    dur = None
    if start is not None and end is not None:
        dur = (end - start).total_seconds()
    return {
        "folder": session_dir.name,
        "mode": None if mode is None else str(mode),
        "hand": None if hand is None else str(hand),
        "start_utc": None if start is None else start.isoformat(),
        "end_utc": None if end is None else end.isoformat(),
        "duration_from_json_s": dur,
    }


def find_sessions(subject_dir: Path) -> List[Path]:
    """Session sub-folders of a subject folder, sorted by name."""
    try:
        return sorted(p for p in subject_dir.iterdir() if p.is_dir() and is_session_dir(p))
    except OSError:
        return []


def find_subject_dirs(root: Path) -> List[Path]:
    """Subject folders below a FAMOT_Data root, sorted by name."""
    try:
        subs = [p for p in root.iterdir() if p.is_dir()]
    except OSError:
        return []
    return sorted(p for p in subs if (p / "subject.json").is_file() or find_sessions(p))


def list_data(path: Path) -> int:
    """Implement ``--list``: print subjects and sessions found under ``path``."""
    if not path.is_dir():
        print(f"Not a directory: {path}", file=sys.stderr)
        return 2
    if is_session_dir(path):
        print(f"{path} is a session folder itself:")
        _print_session_line(path, indent="  ")
        return 0

    subject_dirs = find_subject_dirs(path)
    if not subject_dirs and find_sessions(path):
        subject_dirs = [path]  # path is a subject folder
        root = path.parent
    else:
        root = path

    print(f"Data folder: {path}")
    csv = read_csv_safe(root / "subjects.csv", "subjects.csv", as_text=True) if (root / "subjects.csv").is_file() else None
    if csv is not None:
        print(f"\nsubjects.csv ({len(csv)} subjects)")
        for _, row in csv.iterrows():
            print("  {id:<10} {name:<24} hand={hand:<6} sessions={cnt:<4} last={last}".format(
                id=_fmt_label(row.get("subject_id")), name=_fmt_label(row.get("subject_name")),
                hand=_fmt_label(row.get("handedness")) or "-", cnt=_fmt_label(row.get("session_count")) or "?",
                last=_fmt_label(row.get("last_session_utc")) or "-"))
    if not subject_dirs:
        print("\nNo subject folders with sessions found.")
        return 0
    for sd in subject_dirs:
        sj = load_json(sd / "subject.json")
        sessions = find_sessions(sd)
        extra = ""
        if sj:
            sid = pick(sj, ["subject_id", "id"])
            sname = pick(sj, ["subject_name", "name"])
            hnd = pick(sj, ["handedness", "dominant_hand"])
            extra = f"  [id={sid} name={sname} handedness={hnd}]"
        print(f"\nSubject folder: {sd.name}  ({len(sessions)} sessions){extra}")
        for s in sessions:
            _print_session_line(s, indent="  ")
    return 0


def _print_session_line(session_dir: Path, indent: str = "") -> None:
    meta = load_json(session_dir / "session.json")
    si = session_info(session_dir, meta)
    files = "".join(
        flag_char for f, flag_char in (("kinematics.csv", "K"), ("tracking.csv", "T"), ("hand_joints.csv", "H"),
                                       ("events.csv", "E"), ("calibration.csv", "C"), ("udp_raw.log", "U"))
        if (session_dir / f).is_file()
    )
    dur = si["duration_from_json_s"]
    dur_s = f"{dur:.1f}s" if dur is not None else "-"
    print(f"{indent}{session_dir.name}  mode={si['mode'] or '-'} hand={si['hand'] or '-'} "
          f"start={si['start_utc'] or '-'} end={si['end_utc'] or '-'} dur={dur_s} files={files or '-'}")


# --------------------------------------------------------------------------
# Data loading
# --------------------------------------------------------------------------

def load_session(session_dir: Path) -> Dict[str, Any]:
    """Load every file of a session into a dict (missing ones are ``None``)."""
    data: Dict[str, Any] = {"dir": session_dir}
    data["meta"] = load_json(session_dir / "session.json")
    if data["meta"] is None and not (session_dir / "session.json").is_file():
        warn("session.json not found; metadata will come from the folder name only.")
    for key in ("kinematics", "tracking", "events", "calibration"):
        data[key] = read_csv_safe(session_dir / f"{key}.csv")
    hj = session_dir / "hand_joints.csv"
    data["hand_joints"] = read_csv_safe(hj) if hj.is_file() else None  # optional: no warning
    data["udp"] = parse_udp_log(session_dir / "udp_raw.log")
    return data


def parse_udp_log(path: Path) -> Optional[Dict[str, Any]]:
    """Parse ``udp_raw.log`` (utc, unity_time, direction, endpoint, message).

    The first character of the message is the command type.  Returns counts
    per type and per (direction, type), or ``None`` if the file is missing.
    """
    if not path.is_file():
        warn("udp_raw.log: file not found; UDP statistics skipped.")
        return None
    by_type: Counter = Counter()
    by_dir_type: Counter = Counter()
    by_dir: Counter = Counter()
    n = bad = 0
    try:
        with open(path, "r", encoding="utf-8", errors="replace") as fh:
            for line in fh:
                line = line.rstrip("\r\n")
                if not line.strip() or line.lstrip().startswith("#"):
                    continue
                parts = line.split("\t", 4)
                if len(parts) < 5 or not parts[4].strip():
                    bad += 1
                    continue
                n += 1
                direction = parts[2].strip() or "?"
                ctype = parts[4].lstrip()[0]
                by_type[ctype] += 1
                by_dir[direction] += 1
                by_dir_type[f"{direction}:{ctype}"] += 1
    except OSError as exc:
        warn(f"udp_raw.log: could not be read ({exc}).")
        return None
    return {
        "n_messages": n,
        "n_malformed_lines": bad,
        "by_type": dict(sorted(by_type.items())),
        "by_direction": dict(sorted(by_dir.items())),
        "by_direction_type": dict(sorted(by_dir_type.items())),
    }


# --------------------------------------------------------------------------
# Kinematic derived quantities
# --------------------------------------------------------------------------

def parse_joint_filter(spec: Optional[str]) -> List[str]:
    """Turn ``"ShF,ElF"`` into a validated list of short names (file order).

    Unknown names are reported and ignored.  ``None`` / empty means all joints.
    """
    all_short = [s for s, _ in JOINTS]
    if not spec:
        return all_short
    wanted = set()
    by_lower = {s.lower(): s for s in all_short}
    by_lower.update({full.lower(): s for s, full in JOINTS})
    for tok in re.split(r"[,\s;]+", spec.strip()):
        if not tok:
            continue
        key = by_lower.get(tok.lower())
        if key is None:
            warn(f"unknown joint '{tok}' ignored (valid: {', '.join(all_short)}).")
        else:
            wanted.add(key)
    return [s for s in all_short if s in wanted]


def joint_columns(prefix: str) -> List[str]:
    """Column names ``{prefix}_{joint}`` for the 7 joints."""
    return [f"{prefix}_{full}" for _, full in JOINTS]


def target_valid_matrix(kin: pd.DataFrame) -> np.ndarray:
    """Boolean ``(n, 7)`` matrix: target is active for joint ``i`` in row ``r``.

    Active means bit ``i`` of ``target_dof_mask`` is set and the target angle is
    not NaN.  If ``target_dof_mask`` is absent every non-NaN target counts.
    """
    n = len(kin)
    tcols = joint_columns("target")
    out = np.zeros((n, len(JOINTS)), dtype=bool)
    if "target_dof_mask" in kin.columns:
        m = num(kin, "target_dof_mask").to_numpy()
        m = np.where(np.isfinite(m), m, 0).astype(np.int64)
        for i in range(len(JOINTS)):
            out[:, i] = ((m >> i) & 1) == 1
    else:
        out[:] = True
    for i, c in enumerate(tcols):
        out[:, i] &= num(kin, c).notna().to_numpy() if c in kin.columns else False
    return out


def joint_abs_error(kin: pd.DataFrame, valid: np.ndarray) -> np.ndarray:
    """``|live - target|`` per joint, NaN where the target is inactive."""
    err = np.full((len(kin), len(JOINTS)), np.nan)
    for i, (lc, tc) in enumerate(zip(joint_columns("live"), joint_columns("target"))):
        if lc in kin.columns and tc in kin.columns:
            e = np.abs(num(kin, lc).to_numpy() - num(kin, tc).to_numpy())
            err[:, i] = np.where(valid[:, i], e, np.nan)
    return err


def detect_threshold(events: Optional[pd.DataFrame]) -> Optional[Dict[str, Any]]:
    """Find a success threshold (deg) mentioned in event text.

    Looks at ``detail`` first, then ``name``/``value`` of events whose name
    mentions "thresh"/"toleran".  Returns the most frequent value or ``None``.
    """
    if events is None or events.empty:
        return None
    details, names, values = txt(events, "detail"), txt(events, "name"), txt(events, "value")
    found: List[Tuple[float, str]] = []
    for d, nm, v in zip(details, names, values):
        for m in _THRESH_RE.finditer(d):
            found.append((float(m.group(1)), "detail"))
        if _THRESH_KEY_RE.search(nm):
            m2 = _THRESH_RE.search(nm)
            if m2:
                found.append((float(m2.group(1)), "name"))
            else:
                try:
                    found.append((float(v), "value"))
                except ValueError:
                    pass
    if not found:
        return None
    cnt = Counter(round(f, 6) for f, _ in found)
    value = cnt.most_common(1)[0][0]
    src = next(s for f, s in found if round(f, 6) == value)
    return {"value_deg": float(value), "source": src, "n_mentions": int(cnt[value])}


def compute_segments(kin: pd.DataFrame, t: np.ndarray, err: np.ndarray) -> List[Dict[str, Any]]:
    """Split kinematics into consecutive (trial, target) segments with stats.

    Rows with both ``trial`` and ``target`` empty are ignored.  For each
    segment: time to achieve (first ``Success`` row minus segment start), final
    error, mean error, mean absolute error per joint.
    """
    n = len(kin)
    if n == 0:
        return []
    trial, target = txt(kin, "trial").to_numpy(), txt(kin, "target").to_numpy()
    phase = txt(kin, "phase").to_numpy()
    status_ok = (txt(kin, "status").str.lower() == "success").to_numpy()
    maxerr = num(kin, "max_error_deg").to_numpy()
    dwell = num(kin, "dwell_progress").to_numpy()
    keys = np.char.add(np.char.add(trial.astype(str), "|"), target.astype(str))

    segs: List[Dict[str, Any]] = []
    a = 0
    for i in range(1, n + 1):
        if i < n and keys[i] == keys[a]:
            continue
        b = i  # segment rows [a, b)
        if not (trial[a] == "" and target[a] == ""):
            sl = slice(a, b)
            t0 = float(t[a])
            ok_idx = np.flatnonzero(status_ok[sl])
            ttl = float(t[a + ok_idx[0]] - t0) if ok_idx.size else None
            e_seg = maxerr[sl]
            fin = e_seg[np.isfinite(e_seg)]
            ok_err = float(maxerr[a + ok_idx[0]]) if ok_idx.size and np.isfinite(maxerr[a + ok_idx[0]]) else None
            d_seg = dwell[sl]
            d_fin = d_seg[np.isfinite(d_seg)]
            segs.append({
                "trial": trial[a], "target": target[a], "phase": phase[a],
                "start_time_s": _r(t0), "end_time_s": _r(t[b - 1]),
                "duration_s": _r(t[b - 1] - t0), "n_frames": int(b - a),
                "achieved": bool(ok_idx.size),
                "time_to_achieve_s": _r(ttl),
                "error_at_success_deg": _r(ok_err),
                "final_error_deg": _r(fin[-1]) if fin.size else None,
                "mean_error_deg": _r(fin.mean()) if fin.size else None,
                "max_dwell_progress": _r(d_fin.max()) if d_fin.size else None,
                "mean_abs_error_deg": {s: _r(_nanmean(err[sl, j])) for j, (s, _) in enumerate(JOINTS)},
            })
        a = b
    return segs


def compute_trials(kin: pd.DataFrame, t: np.ndarray, err: np.ndarray,
                   segs: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
    """Aggregate segments per trial (mean errors computed from raw rows)."""
    trial = txt(kin, "trial").to_numpy()
    order: "OrderedDict[str, List[Dict[str, Any]]]" = OrderedDict()
    for s in segs:
        if s["trial"] == "":
            continue
        order.setdefault(s["trial"], []).append(s)
    out = []
    for tr, ss in order.items():
        rows = trial == tr
        ttl = [s["time_to_achieve_s"] for s in ss if s["time_to_achieve_s"] is not None]
        fin = [s["final_error_deg"] for s in ss if s["final_error_deg"] is not None]
        start = min(s["start_time_s"] for s in ss if s["start_time_s"] is not None) if ss else None
        end = max(s["end_time_s"] for s in ss if s["end_time_s"] is not None) if ss else None
        out.append({
            "trial": tr, "start_time_s": start, "end_time_s": end,
            "duration_s": _r(end - start) if start is not None and end is not None else None,
            "n_target_segments": len(ss),
            "n_targets_achieved": sum(1 for s in ss if s["achieved"]),
            "mean_time_to_achieve_s": _r(np.mean(ttl)) if ttl else None,
            "mean_final_error_deg": _r(np.mean(fin)) if fin else None,
            "mean_abs_error_deg": {s_: _r(_nanmean(err[rows, j])) for j, (s_, _) in enumerate(JOINTS)},
        })
    return out


# --------------------------------------------------------------------------
# Summary computation
# --------------------------------------------------------------------------

def gaze_kinds(trk: pd.DataFrame) -> np.ndarray:
    """Per-frame gaze category: hit kind, ``NoHit`` or ``Invalid``."""
    kind = txt(trk, "gaze_hit_kind").to_numpy().astype(object)
    kind = np.where(kind == "", "NoHit", kind)
    hit = flag(trk, "gaze_hit")
    if hit is not None:
        kind = np.where(~hit & (kind == "NoHit"), "NoHit", kind)
    valid = flag(trk, "gaze_valid")
    if valid is not None:
        kind = np.where(~valid, "Invalid", kind)
    return kind


def gaze_summary(trk: Optional[pd.DataFrame]) -> Optional[Dict[str, Any]]:
    """Time-weighted gaze share per kind and eye-tracking availability."""
    if trk is None or "gaze_hit_kind" not in trk.columns and "gaze_eye_tracked" not in trk.columns:
        return None
    t = time_of(trk)
    dt = frame_dt(t)
    out: Dict[str, Any] = {}
    if "gaze_hit_kind" in trk.columns or "gaze_valid" in trk.columns:
        kinds = gaze_kinds(trk)
        total = float(dt.sum()) or float(len(kinds))
        weights = dt if dt.sum() > 0 else np.ones(len(kinds))
        share = {}
        for k in sorted(set(kinds.tolist())):
            share[k] = _r(float(weights[kinds == k].sum()) / total)
        out["share_by_kind"] = share
        out["seconds_by_kind"] = {k: _r(float(dt[kinds == k].sum())) for k in share}
        out["frames_by_kind"] = {k: int((kinds == k).sum()) for k in share}
    eye = flag(trk, "gaze_eye_tracked")
    out["eye_tracked_fraction"] = _r(float(eye.mean())) if eye is not None and len(eye) else None
    gv = flag(trk, "gaze_valid")
    out["gaze_valid_fraction"] = _r(float(gv.mean())) if gv is not None and len(gv) else None
    return out


def hand_tracking_summary(trk: Optional[pd.DataFrame], hand_joints: Optional[pd.DataFrame]) -> Optional[Dict[str, Any]]:
    """Share of frames with tracked hands / controllers per side."""
    if trk is None:
        return None
    out: Dict[str, Any] = {"n_frames": int(len(trk)), "per_side": {}}
    any_hand = np.zeros(len(trk), dtype=bool)
    any_ctrl = np.zeros(len(trk), dtype=bool)
    seen_hand = seen_ctrl = False
    for side in ("L", "R"):
        hf, cf = flag(trk, f"{side}_hand_tracked"), flag(trk, f"{side}_ctrl_tracked")
        entry: Dict[str, Any] = {
            "hand_tracked_share": _r(float(hf.mean())) if hf is not None else None,
            "controller_tracked_share": _r(float(cf.mean())) if cf is not None else None,
        }
        src = f"{side}_source"
        if src in trk.columns:
            counts = Counter(v for v in txt(trk, src) if v != "")
            n = sum(counts.values())
            entry["source_share"] = {k: _r(v / n) for k, v in sorted(counts.items())} if n else {}
        if hf is not None:
            any_hand |= hf
            seen_hand = True
        if cf is not None:
            any_ctrl |= cf
            seen_ctrl = True
        out["per_side"][side] = entry
    out["any_hand_tracked_share"] = _r(float(any_hand.mean())) if seen_hand and len(any_hand) else None
    out["any_controller_tracked_share"] = _r(float(any_ctrl.mean())) if seen_ctrl and len(any_ctrl) else None
    out["hand_joints_rows"] = int(len(hand_joints)) if hand_joints is not None else None
    return out


def build_summary(data: Dict[str, Any]) -> Dict[str, Any]:
    """Compute the full numeric summary for a loaded session."""
    sdir: Path = data["dir"]
    kin, trk, ev, cal = data["kinematics"], data["tracking"], data["events"], data["calibration"]
    summary: Dict[str, Any] = {
        "session_dir": str(sdir),
        "session": session_info(sdir, data["meta"]),
        "session_json": data["meta"],
        "files": {
            name: (None if df is None else int(len(df)))
            for name, df in (("kinematics.csv", kin), ("tracking.csv", trk), ("events.csv", ev),
                             ("calibration.csv", cal), ("hand_joints.csv", data["hand_joints"]))
        },
    }

    # Durations / frame rates -------------------------------------------------
    fps: Dict[str, Optional[float]] = {}
    span: Dict[str, Optional[float]] = {}
    for name, df in (("kinematics", kin), ("tracking", trk)):
        if df is None:
            continue
        t = time_of(df)
        fin = t[np.isfinite(t)]
        span[name] = _r(fin.max() - fin.min()) if fin.size else None
        fps[name] = _r(estimate_fps(t), 2)
    durations = [v for v in span.values() if v is not None]
    summary["duration_s"] = max(durations) if durations else summary["session"]["duration_from_json_s"]
    summary["duration_by_file_s"] = span
    summary["frame_rate_hz_median"] = fps

    # Kinematics --------------------------------------------------------------
    if kin is not None:
        t = time_of(kin)
        valid = target_valid_matrix(kin)
        err = joint_abs_error(kin, valid)
        segs = compute_segments(kin, t, err)
        status = txt(kin, "status")
        dt = frame_dt(t)
        phase = txt(kin, "phase").to_numpy()
        phase_dur = {p: _r(float(dt[phase == p].sum())) for p in OrderedDict.fromkeys(phase.tolist()) if p != ""}
        lt = flag(kin, "live_tracked")
        ik = flag(kin, "ik_clamped")
        summary["kinematics"] = {
            "n_frames": int(len(kin)),
            "status_counts": dict(Counter(s for s in status if s != "")),
            "phase_durations_s": phase_dur,
            "live_tracked_share": _r(float(lt.mean())) if lt is not None else None,
            "ik_clamped_share": _r(float(ik.mean())) if ik is not None else None,
            "mean_abs_error_deg_per_joint": {s: _r(_nanmean(err[:, j])) for j, (s, _) in enumerate(JOINTS)},
            "n_target_segments": len(segs),
            "n_targets_achieved": sum(1 for s in segs if s["achieved"]),
            "success_threshold": detect_threshold(ev),
        }
        summary["trials"] = compute_trials(kin, t, err, segs)
        summary["targets"] = segs
    else:
        summary["kinematics"] = None
        summary["trials"] = []
        summary["targets"] = []

    summary["gaze"] = gaze_summary(trk)
    summary["hand_tracking"] = hand_tracking_summary(trk, data["hand_joints"])
    summary["calibration"] = None if cal is None else {
        "n_rows": int(len(cal)), "columns": list(cal.columns),
        "rows": json.loads(cal.head(50).to_json(orient="records")),
    }
    summary["udp_commands"] = data["udp"]
    if ev is not None:
        cats = txt(ev, "category")
        summary["events"] = {
            "n_events": int(len(ev)),
            "by_category": dict(sorted(Counter(c for c in cats if c != "").items())),
        }
    else:
        summary["events"] = None
    return summary


def _pct(x: Optional[float]) -> str:
    return "n/a" if x is None else f"{100 * x:.1f}%"


def _f(x: Optional[float], nd: int = 2) -> str:
    return "n/a" if x is None else f"{x:.{nd}f}"


def render_summary_text(s: Dict[str, Any]) -> str:
    """Human-readable version of the summary dict."""
    L: List[str] = []
    ses = s["session"]
    L += ["FAMOT SESSION SUMMARY", "=" * 60,
          f"Session folder : {ses['folder']}",
          f"Mode / hand    : {ses['mode'] or '-'} / {ses['hand'] or '-'}",
          f"Start (UTC)    : {ses['start_utc'] or '-'}",
          f"End (UTC)      : {ses['end_utc'] or '-'}",
          f"Duration       : {_f(s['duration_s'], 1)} s (recorded data span)"]
    fr = s["frame_rate_hz_median"]
    L.append("Frame rate     : " + (", ".join(f"{k} {_f(v, 1)} Hz" for k, v in fr.items()) or "n/a") + " (median 1/dt)")
    L.append("Rows per file  : " + ", ".join(f"{k}={'missing' if v is None else v}" for k, v in s["files"].items()))

    meta = s.get("session_json")
    if meta:
        L += ["", "session.json", "-" * 60]
        for k, v in meta.items():
            vs = json.dumps(to_jsonable(v), ensure_ascii=False) if isinstance(v, (dict, list)) else str(v)
            L.append(f"  {k}: {vs if len(vs) < 160 else vs[:157] + '...'}")

    k = s.get("kinematics")
    if k:
        L += ["", "Kinematics", "-" * 60,
              f"Frames: {k['n_frames']}   live tracked: {_pct(k['live_tracked_share'])}   IK clamped: {_pct(k['ik_clamped_share'])}",
              f"Target segments: {k['n_target_segments']} (achieved: {k['n_targets_achieved']})"]
        thr = k.get("success_threshold")
        L.append("Success threshold from events: " + (f"{thr['value_deg']} deg ({thr['source']})" if thr else "not found"))
        if k["status_counts"]:
            L.append("Status frames: " + ", ".join(f"{a}={b}" for a, b in k["status_counts"].items()))
        if k["phase_durations_s"]:
            L.append("Phase time (s): " + ", ".join(f"{a}={_f(b, 1)}" for a, b in k["phase_durations_s"].items()))
        L.append("Mean |live - target| per joint (deg, active targets only): "
                 + ", ".join(f"{a}={_f(b, 1)}" for a, b in k["mean_abs_error_deg_per_joint"].items()))

    if s.get("trials"):
        L += ["", "Per-trial statistics", "-" * 60,
              f"{'trial':>6} {'start':>8} {'dur(s)':>8} {'targets':>8} {'achieved':>9} {'mean TTA(s)':>12} {'mean final err':>15}"]
        for tr in s["trials"]:
            L.append(f"{tr['trial']:>6} {_f(tr['start_time_s'], 1):>8} {_f(tr['duration_s'], 1):>8} "
                     f"{tr['n_target_segments']:>8} {tr['n_targets_achieved']:>9} "
                     f"{_f(tr['mean_time_to_achieve_s']):>12} {_f(tr['mean_final_error_deg']):>15}")
    if s.get("targets"):
        L += ["", "Per-target statistics (time to achieve = first Success after target start)", "-" * 60,
              f"{'trial':>6} {'target':>7} {'start':>8} {'dur(s)':>8} {'achv':>5} {'TTA(s)':>8} {'final err':>10} {'mean err':>9}  mean |err| per joint"]
        for g in s["targets"]:
            je = " ".join(f"{a}={_f(b, 1)}" for a, b in g["mean_abs_error_deg"].items() if b is not None)
            L.append(f"{g['trial']:>6} {g['target']:>7} {_f(g['start_time_s'], 1):>8} {_f(g['duration_s'], 1):>8} "
                     f"{'yes' if g['achieved'] else 'no':>5} {_f(g['time_to_achieve_s']):>8} "
                     f"{_f(g['final_error_deg']):>10} {_f(g['mean_error_deg']):>9}  {je}")

    g = s.get("gaze")
    if g:
        L += ["", "Gaze", "-" * 60,
              f"Eye tracked frames: {_pct(g.get('eye_tracked_fraction'))}   valid gaze frames: {_pct(g.get('gaze_valid_fraction'))}"]
        for kind, share in (g.get("share_by_kind") or {}).items():
            L.append(f"  {kind:<12} {_pct(share):>7}  ({_f(g['seconds_by_kind'][kind], 1)} s)")

    h = s.get("hand_tracking")
    if h:
        L += ["", "Hand / controller tracking", "-" * 60]
        for side, e in h["per_side"].items():
            src = ", ".join(f"{a}={_pct(b)}" for a, b in (e.get("source_share") or {}).items())
            L.append(f"  {side}: hand tracked {_pct(e['hand_tracked_share'])}, controller tracked "
                     f"{_pct(e['controller_tracked_share'])}" + (f"; source: {src}" if src else ""))
        L.append(f"  Any hand tracked: {_pct(h['any_hand_tracked_share'])}   any controller tracked: "
                 f"{_pct(h['any_controller_tracked_share'])}")
        if h.get("hand_joints_rows") is not None:
            L.append(f"  hand_joints.csv rows: {h['hand_joints_rows']}")

    c = s.get("calibration")
    if c:
        L += ["", "Calibration", "-" * 60, f"Rows: {c['n_rows']}   columns: {', '.join(c['columns'])}"]
    u = s.get("udp_commands")
    if u:
        L += ["", "UDP commands (first char of message)", "-" * 60,
              f"Messages: {u['n_messages']}   malformed lines: {u['n_malformed_lines']}",
              "By type: " + (", ".join(f"{a}={b}" for a, b in u["by_type"].items()) or "none"),
              "By direction: " + (", ".join(f"{a}={b}" for a, b in u["by_direction"].items()) or "none"),
              "By direction:type: " + (", ".join(f"{a}={b}" for a, b in u["by_direction_type"].items()) or "none")]
    e = s.get("events")
    if e:
        L += ["", "Events", "-" * 60, f"Total {e['n_events']}: "
              + ", ".join(f"{a}={b}" for a, b in e["by_category"].items())]
    return "\n".join(L) + "\n"


# --------------------------------------------------------------------------
# Plotting
# --------------------------------------------------------------------------

def _pyplot():
    """Import and return ``matplotlib.pyplot`` lazily (backend set by caller)."""
    import matplotlib.pyplot as plt  # noqa: WPS433
    return plt


def _event_times(ev: Optional[pd.DataFrame], category: str) -> List[Tuple[float, str]]:
    """(time, name) of events whose category equals ``category`` (any case)."""
    if ev is None or ev.empty:
        return []
    t = num(ev, "unity_time").to_numpy()
    cat = txt(ev, "category").str.lower().to_numpy()
    nm = txt(ev, "name").to_numpy()
    val = txt(ev, "value").to_numpy()
    return [(float(a), str(v) if v != "" else str(b))
            for a, b, v, c in zip(t, nm, val, cat) if c == category and np.isfinite(a)]


def _shade_success(ax, kin: pd.DataFrame, t: np.ndarray) -> bool:
    """Shade spans where ``status == Success``; return True if any drawn."""
    ok = (txt(kin, "status").str.lower() == "success").to_numpy()
    if not ok.any():
        return False
    for lab, t0, t1 in runs_of(ok.tolist(), t):
        if lab:
            ax.axvspan(t0, t1, color="#009E73", alpha=0.18, lw=0)
    return True


def _mark_events(ax, ev: Optional[pd.DataFrame], label_trials: bool = False) -> Tuple[bool, bool]:
    """Draw vertical lines for trial and target events."""
    trials = _event_times(ev, "trial")
    targets = _event_times(ev, "target")
    for tt, _ in targets:
        ax.axvline(tt, color="#7f7f7f", lw=0.6, ls=":", alpha=0.6, zorder=1)
    for tt, nm in trials:
        ax.axvline(tt, color="#222222", lw=1.0, alpha=0.7, zorder=1)
        if label_trials and len(trials) <= 40:
            ax.text(tt, 1.0, nm, transform=ax.get_xaxis_transform(), rotation=90, fontsize=6,
                    va="bottom", ha="right", color="#222222")
    return bool(trials), bool(targets)


def plot_joint_angles(data: Dict[str, Any], joints: List[str], title: str):
    """Figure 1: live vs target joint angles over time. Returns Figure or None."""
    kin = data["kinematics"]
    if kin is None or not has_any(kin, joint_columns("live")):
        warn("joint_angles: kinematics.csv missing or has no live joint columns; skipped.")
        return None
    plt = _pyplot()
    from matplotlib.lines import Line2D
    from matplotlib.patches import Patch

    t = time_of(kin)
    valid = target_valid_matrix(kin)
    names = dict(JOINTS)
    idx = {s: i for i, (s, _) in enumerate(JOINTS)}
    fig, axes = plt.subplots(len(joints), 1, sharex=True, squeeze=False,
                             figsize=(13, max(3.0, 1.9 * len(joints) + 1.0)), constrained_layout=True)
    axes = axes[:, 0]
    any_success = any_trial = any_target = False
    for ax, short in zip(axes, joints):
        i = idx[short]
        live = num(kin, f"live_{names[short]}").to_numpy()
        tgt = num(kin, f"target_{names[short]}").to_numpy()
        tgt = np.where(valid[:, i], tgt, np.nan)
        any_success |= _shade_success(ax, kin, t)
        tr, tg = _mark_events(ax, data["events"], label_trials=(short == joints[0]))
        any_trial |= tr
        any_target |= tg
        ax.plot(t, live, color="#0072B2", lw=1.2)
        ax.plot(t, tgt, color="#D55E00", lw=1.4, ls="--")
        ax.set_ylabel(f"{short}\n(deg)")
        ax.set_title(JOINT_LABELS[short], fontsize=9, loc="left", pad=2)
        ax.grid(alpha=0.25)
    axes[-1].set_xlabel("unity_time (s)")
    handles = [Line2D([0], [0], color="#0072B2", lw=1.5, label="live"),
               Line2D([0], [0], color="#D55E00", lw=1.5, ls="--", label="target (active DOF)")]
    if any_success:
        handles.append(Patch(color="#009E73", alpha=0.3, label="status = Success"))
    if any_trial:
        handles.append(Line2D([0], [0], color="#222222", lw=1.0, label="trial event"))
    if any_target:
        handles.append(Line2D([0], [0], color="#7f7f7f", lw=1.0, ls=":", label="target event"))
    fig.legend(handles=handles, loc="outside upper right", fontsize=8, ncol=len(handles), frameon=False)
    fig.suptitle(f"Joint angles - {title}", fontsize=11, x=0.01, ha="left")
    return fig


def plot_target_error(data: Dict[str, Any], title: str):
    """Figure 2: max error with threshold line + dwell progress. Returns Figure or None."""
    kin = data["kinematics"]
    if kin is None or not has_any(kin, ["max_error_deg", "dwell_progress"]):
        warn("target_error: kinematics.csv missing max_error_deg / dwell_progress; skipped.")
        return None
    plt = _pyplot()
    from matplotlib.lines import Line2D

    t = time_of(kin)
    fig, ax = plt.subplots(figsize=(13, 4.5), constrained_layout=True)
    _shade_success(ax, kin, t)
    _mark_events(ax, data["events"])
    handles = []
    if "max_error_deg" in kin.columns:
        ax.plot(t, num(kin, "max_error_deg").to_numpy(), color="#0072B2", lw=1.2)
        handles.append(Line2D([0], [0], color="#0072B2", label="max_error_deg"))
    ax.set_xlabel("unity_time (s)")
    ax.set_ylabel("max joint error (deg)", color="#0072B2")
    ax.grid(alpha=0.25)
    ax.set_ylim(bottom=0)
    thr = detect_threshold(data["events"])
    if thr:
        ax.axhline(thr["value_deg"], color="#D55E00", ls="--", lw=1.3)
        handles.append(Line2D([0], [0], color="#D55E00", ls="--", label=f"success threshold {thr['value_deg']:g} deg"))
    if "dwell_progress" in kin.columns:
        ax2 = ax.twinx()
        ax2.plot(t, num(kin, "dwell_progress").to_numpy(), color="#E69F00", lw=1.0, alpha=0.9)
        ax2.set_ylabel("dwell progress", color="#B87800")
        ax2.set_ylim(-0.02, 1.05)
        handles.append(Line2D([0], [0], color="#E69F00", label="dwell_progress"))
    if handles:
        ax.legend(handles=handles, loc="upper right", fontsize=8, framealpha=0.9)
    ax.set_title(f"Target error - {title}", fontsize=11, loc="left")
    return fig


def plot_gaze(data: Dict[str, Any], title: str):
    """Figure 3: gaze category strip, eye-tracking strip and time-share bars."""
    trk = data["tracking"]
    if trk is None or not ("gaze_hit_kind" in trk.columns or "gaze_eye_tracked" in trk.columns):
        warn("gaze: tracking.csv missing gaze columns; skipped.")
        return None
    plt = _pyplot()
    from matplotlib.patches import Patch

    t = time_of(trk)
    kinds = gaze_kinds(trk)
    uniq = sorted(set(kinds.tolist()), key=lambda k: (k not in GAZE_COLORS, k))
    colors = {}
    fb = iter(_FALLBACK_COLORS * 4)
    for k in uniq:
        colors[k] = GAZE_COLORS.get(k) or next(fb)
    eye = flag(trk, "gaze_eye_tracked")

    nrows = 2 + (1 if eye is not None else 0)
    ratios = [1.0] + ([0.45] if eye is not None else []) + [3.0]
    fig = plt.figure(figsize=(12, 7), constrained_layout=True)
    gs = fig.add_gridspec(nrows, 1, height_ratios=ratios)
    ax_strip = fig.add_subplot(gs[0])
    for k in uniq:
        spans = [(a, b - a) for lab, a, b in runs_of(kinds.tolist(), t) if lab == k]
        if spans:
            ax_strip.broken_barh(spans, (0, 1), facecolors=colors[k], lw=0)
    fin_t = t[np.isfinite(t)]
    if fin_t.size:
        ax_strip.set_xlim(fin_t.min(), fin_t.max())
    ax_strip.set_ylim(0, 1)
    ax_strip.set_yticks([])
    ax_strip.set_title(f"Gaze hit kind over time - {title}", fontsize=11, loc="left")
    ax_strip.legend(handles=[Patch(color=colors[k], label=k) for k in uniq], loc="lower right",
                    bbox_to_anchor=(1.0, 1.0), ncol=len(uniq), fontsize=8, frameon=False)
    last_time_ax = ax_strip
    row = 1
    if eye is not None:
        ax_eye = fig.add_subplot(gs[row], sharex=ax_strip)
        for val, col in ((True, "#009E73"), (False, "#D55E00")):
            spans = [(a, b - a) for lab, a, b in runs_of(eye.tolist(), t) if lab == val]
            if spans:
                ax_eye.broken_barh(spans, (0, 1), facecolors=col, lw=0)
        ax_eye.set_ylim(0, 1)
        ax_eye.set_yticks([])
        ax_eye.set_xlabel("unity_time (s)")
        ax_eye.set_ylabel("eye tracked\n(green yes,\nred no)", rotation=0, ha="right", va="center", fontsize=7)
        last_time_ax = ax_eye
        row += 1
    else:
        ax_strip.set_xlabel("unity_time (s)")
    del last_time_ax

    ax_bar = fig.add_subplot(gs[row])
    gs_sum = gaze_summary(trk) or {}
    share = gs_sum.get("share_by_kind") or {}
    if share:
        order = sorted(share, key=lambda k: -(share[k] or 0))
        vals = [100 * (share[k] or 0) for k in order]
        bars = ax_bar.bar(order, vals, color=[colors.get(k, "#888888") for k in order])
        for b, v in zip(bars, vals):
            ax_bar.text(b.get_x() + b.get_width() / 2, v, f"{v:.1f}%", ha="center", va="bottom", fontsize=8)
        ax_bar.set_ylabel("time share (%)")
        ax_bar.set_ylim(0, max(vals + [1]) * 1.15)
    ef = gs_sum.get("eye_tracked_fraction")
    ax_bar.set_title("Time share per gaze kind" + (f"   |   eye tracked in {100 * ef:.1f}% of frames" if ef is not None else ""),
                     fontsize=10, loc="left")
    ax_bar.grid(axis="y", alpha=0.25)
    return fig


def _xyz(df: Optional[pd.DataFrame], prefix: str, ok: Optional[np.ndarray] = None,
         drop_zero: bool = False) -> Optional[np.ndarray]:
    """Return ``(n, 3)`` positions of ``{prefix}_x/y/z`` or ``None`` if unusable."""
    if df is None:
        return None
    cols = [f"{prefix}_{a}" for a in "xyz"]
    if not all(c in df.columns for c in cols):
        return None
    p = np.column_stack([num(df, c).to_numpy() for c in cols])
    if ok is not None:
        p = np.where(ok[:, None], p, np.nan)
    if drop_zero:
        p = np.where((np.abs(p).sum(axis=1) == 0)[:, None], np.nan, p)
    if not np.isfinite(p).all(axis=1).any():
        return None
    return p


def plot_hand_paths(data: Dict[str, Any], title: str):
    """Figure 4: top (x-z) and side (z-y) views of wrist / controller paths."""
    kin, trk = data["kinematics"], data["tracking"]
    series: List[Tuple[str, np.ndarray, str, str]] = []  # label, xyz, colour, linestyle
    k_live = _xyz(kin, "live_wrist")
    k_ghost = _xyz(kin, "ghost_wrist")
    if k_live is not None:
        series.append(("live wrist (kinematics)", k_live, "#0072B2", "-"))
    if k_ghost is not None:
        series.append(("ghost wrist (target)", k_ghost, "#D55E00", "--"))
    side_colors = {"L": ("#009E73", "#56B4E9"), "R": ("#CC79A7", "#E69F00")}
    for side in ("L", "R"):
        if trk is None:
            break
        cf, hf = flag(trk, f"{side}_ctrl_tracked"), flag(trk, f"{side}_hand_tracked")
        p = _xyz(trk, f"{side}_ctrl_pos", ok=cf, drop_zero=True) if cf is not None else None
        if p is not None:
            series.append((f"{side} controller (tracked)", p, side_colors[side][0], "-"))
        q = _xyz(trk, f"{side}_wrist_pos", ok=hf, drop_zero=True) if hf is not None else None
        if q is not None:
            series.append((f"{side} hand wrist (tracked)", q, side_colors[side][1], ":"))
    if not series:
        warn("hand_paths: no usable wrist / controller position columns; skipped.")
        return None
    plt = _pyplot()
    fig, (ax_top, ax_side) = plt.subplots(1, 2, figsize=(13, 6), constrained_layout=True)
    for label, p, col, ls in series:
        good = np.isfinite(p).all(axis=1)
        if not good.any():
            continue
        q = p[good]
        for ax, (ia, ib) in ((ax_top, (0, 2)), (ax_side, (2, 1))):
            ax.plot(q[:, ia], q[:, ib], color=col, ls=ls, lw=1.0, alpha=0.85, label=label)
            ax.plot(q[0, ia], q[0, ib], "o", color=col, ms=6)
            ax.plot(q[-1, ia], q[-1, ib], "X", color=col, ms=7)
    ax_top.set_xlabel("x (m)")
    ax_top.set_ylabel("z (m)")
    ax_top.set_title("Top view (x-z)", fontsize=10, loc="left")
    ax_side.set_xlabel("z (m)")
    ax_side.set_ylabel("y (m)")
    ax_side.set_title("Side view (z-y)", fontsize=10, loc="left")
    for ax in (ax_top, ax_side):
        ax.set_aspect("equal", adjustable="datalim")
        ax.grid(alpha=0.25)
    ax_top.legend(fontsize=7, loc="best", framealpha=0.9, title="o start, X end", title_fontsize=7)
    fig.suptitle(f"Hand / wrist paths - {title}", fontsize=11)
    return fig


# --------------------------------------------------------------------------
# Orchestration / CLI
# --------------------------------------------------------------------------

def run_analysis(session_dir: Path, out_dir: Path, joints: List[str], dpi: int, show: bool) -> Dict[str, Any]:
    """Load a session, write all figures + summaries, optionally show figures.

    Returns the summary dict.
    """
    data = load_session(session_dir)
    if all(data[k] is None for k in ("kinematics", "tracking", "events", "calibration")):
        raise RuntimeError(f"No readable CSV data found in {session_dir}")
    out_dir.mkdir(parents=True, exist_ok=True)
    title = session_dir.name
    plt = _pyplot()

    figures: List[Tuple[str, Any]] = []
    for fname, builder in (
        ("joint_angles.png", lambda: plot_joint_angles(data, joints, title)),
        ("target_error.png", lambda: plot_target_error(data, title)),
        ("gaze.png", lambda: plot_gaze(data, title)),
        ("hand_paths.png", lambda: plot_hand_paths(data, title)),
    ):
        try:
            fig = builder()
        except Exception as exc:  # noqa: BLE001 - one bad figure must not stop the rest
            warn(f"{fname}: failed ({type(exc).__name__}: {exc}); skipped.")
            fig = None
        if fig is None:
            continue
        path = out_dir / fname
        fig.savefig(path, dpi=dpi)
        info(f"wrote {path}")
        figures.append((fname, fig))

    summary = build_summary(data)
    (out_dir / "summary.json").write_text(
        json.dumps(to_jsonable(summary), indent=2, ensure_ascii=False), encoding="utf-8")
    text = render_summary_text(summary)
    (out_dir / "summary.txt").write_text(text, encoding="utf-8")
    info(f"wrote {out_dir / 'summary.txt'}")
    info(f"wrote {out_dir / 'summary.json'}")

    if show and figures:
        try:
            plt.show()
        except Exception as exc:  # noqa: BLE001
            warn(f"could not show figures ({exc}).")
    plt.close("all")
    return summary


def build_parser() -> argparse.ArgumentParser:
    """Create the command-line parser."""
    p = argparse.ArgumentParser(
        prog="famot_replay.py",
        description="Analyse one FAMOT session folder (figures + summary), or list available data with --list.")
    p.add_argument("path", help="session folder (or FAMOT_Data root / subject folder with --list)")
    p.add_argument("--out", default=None, help="output folder (default: <session_dir>/analysis)")
    p.add_argument("--no-show", action="store_true", help="do not open figure windows (just save PNGs)")
    p.add_argument("--dpi", type=int, default=150, help="PNG resolution (default 150)")
    p.add_argument("--joints", default=None,
                   help="comma separated subset of joints to plot: " + ",".join(s for s, _ in JOINTS))
    p.add_argument("--list", action="store_true",
                   help="list subjects and sessions under a FAMOT_Data root or subject folder, then exit")
    return p


def main(argv: Optional[Sequence[str]] = None) -> int:
    """Command-line entry point. Returns the process exit code."""
    args = build_parser().parse_args(argv)
    path = normalize_path(args.path)
    if args.list:
        return list_data(path)
    if not path.is_dir():
        print(f"error: not a directory: {path}", file=sys.stderr)
        return 2
    if not is_session_dir(path):
        print(f"error: {path} does not look like a session folder (no session.json / kinematics.csv ...).\n"
              f"Use --list on the FAMOT_Data root or a subject folder to find sessions.", file=sys.stderr)
        return 2
    joints = parse_joint_filter(args.joints)
    if not joints:
        print("error: --joints did not contain any valid joint name.", file=sys.stderr)
        return 2
    import matplotlib
    if args.no_show:
        matplotlib.use("Agg")
    out_dir = normalize_path(args.out) if args.out else path / "analysis"
    try:
        summary = run_analysis(path, out_dir, joints, max(args.dpi, 20), show=not args.no_show)
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1
    print()
    print(render_summary_text(summary))
    return 0


if __name__ == "__main__":
    warnings.filterwarnings("ignore", category=RuntimeWarning)
    sys.exit(main())
