using System;
using System.Collections.Generic;
using System.Net;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Experiment;
using FAMOT.Input;
using FAMOT.Protocol;
using FAMOT.Session;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace FAMOT.Recording
{
    /// <summary>
    /// Writes the per-frame CSV streams of a session (kinematics, tracking, optional hand joints),
    /// the event log, the raw UDP log and calibration rows. Opens its files when the
    /// <see cref="SessionManager"/> starts a session and closes them when it ends.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class SessionRecorder : MonoBehaviour
    {
        public static SessionRecorder Instance { get; private set; }

        public ExperimentController controller;
        public UdpCommandServer server;
        public UdpStreamSender streamSender;

        [Header("Streams")]
        public bool recordKinematics = true;
        public bool recordTracking = true;
        [Tooltip("Write all 26 hand-tracking joints per hand per frame (large files).")]
        public bool recordHandJoints = false;
        public bool recordEvents = true;
        public bool recordRawUdp = true;
        [Tooltip("Also log every outbound D stream line (one per frame) to udp_raw.log.")]
        public bool logOutboundStream = false;

        public bool IsRecording => kinematics != null || tracking != null;
        public long KinematicsRows => kinematics != null ? kinematics.RowCount : 0;
        public long TrackingRows => tracking != null ? tracking.RowCount : 0;
        public long EventCount => events != null ? events.Count : 0;
        public string SessionDirectory { get; private set; } = string.Empty;

        private CsvWriter kinematics;
        private CsvWriter tracking;
        private CsvWriter handJoints;
        private CsvWriter calibration;
        private EventLogger events;
        private RawUdpLogger rawUdp;
        private TrackedPose lastOrigin = TrackedPose.Invalid;
        private readonly List<string> colsK = new List<string>(80);
        private readonly List<string> colsT = new List<string>(120);
        private readonly List<string> colsH = new List<string>(220);
        private string[] jointNames;

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (controller == null) controller = ExperimentController.Instance ?? FindFirstObjectByType<ExperimentController>();
            if (server == null) server = FindFirstObjectByType<UdpCommandServer>();
            if (streamSender == null) streamSender = FindFirstObjectByType<UdpStreamSender>();
            SessionManager sm = SessionManager.Ensure();
            sm.SessionStarted += OnSessionStarted;
            sm.SessionEnded += OnSessionEnded;
            if (sm.HasActiveSession) Open(sm);
        }

        private void OnDisable()
        {
            SessionManager sm = SessionManager.Instance;
            if (sm != null)
            {
                sm.SessionStarted -= OnSessionStarted;
                sm.SessionEnded -= OnSessionEnded;
            }
            Close();
            if (Instance == this) Instance = null;
        }

        private void OnSessionStarted(SessionInfo s) => Open(SessionManager.Instance);
        private void OnSessionEnded(SessionInfo s) => Close();

        // ------------------------------------------------------------------ open / close

        private void Open(SessionManager sm)
        {
            Close();
            if (sm == null || !sm.HasActiveSession) return;
            SessionDirectory = sm.SessionDirectory;
            try
            {
                BuildHeaders();
                if (recordKinematics) kinematics = new CsvWriter(SessionManager.FilePath(DataPaths.KinematicsCsv), colsK);
                if (recordTracking) tracking = new CsvWriter(SessionManager.FilePath(DataPaths.TrackingCsv), colsT);
                if (recordHandJoints) handJoints = new CsvWriter(SessionManager.FilePath(DataPaths.HandJointsCsv), colsH);
                if (recordEvents) events = new EventLogger(SessionManager.FilePath(DataPaths.EventsCsv));
                if (recordRawUdp) rawUdp = new RawUdpLogger(SessionManager.FilePath(DataPaths.UdpRawLog));
                calibration = new CsvWriter(SessionManager.FilePath(DataPaths.CalibrationCsv), new[]
                {
                    "utc", "unity_time", "side", "source", "shoulder_offset_x", "shoulder_offset_y", "shoulder_offset_z",
                    "arm_length", "model_scale", "wrist_offset_x", "wrist_offset_y", "wrist_offset_z",
                    "hand_basis_offset_x", "hand_basis_offset_y", "hand_basis_offset_z", "hand_basis_offset_w"
                }, 1);

                SessionInfo info = sm.CurrentSession;
                info.columnsKinematics = colsK.ToArray();
                info.columnsTracking = colsT.ToArray();
                if (server != null) info.udpListenPort = server.ListenPort;
                if (streamSender != null) info.udpStreamTarget = streamSender.TargetDescription;
                info.targetFrameRate = Application.targetFrameRate;
                XrTrackingService t = XrTrackingService.Instance;
                if (t != null && t.OriginPose.isValid) WriteOrigin(info, t.OriginPose);
                sm.UpdateSessionJson();

                Hook(true);
                events?.Log("session", "recording_started", double.NaN, SessionDirectory);
                Debug.Log($"[FAMOT] Recording to {SessionDirectory}");
            }
            catch (Exception e)
            {
                Debug.LogError("[FAMOT] Could not open session files: " + e.Message, this);
                Close();
            }
        }

        private void Close()
        {
            if (!IsRecording && events == null && rawUdp == null) return;
            Hook(false);
            events?.Log("session", "recording_stopped", KinematicsRows, string.Empty);
            kinematics?.Dispose(); kinematics = null;
            tracking?.Dispose(); tracking = null;
            handJoints?.Dispose(); handJoints = null;
            calibration?.Dispose(); calibration = null;
            events?.Dispose(); events = null;
            rawUdp?.Dispose(); rawUdp = null;
        }

        private void Hook(bool on)
        {
            if (controller != null)
            {
                if (on)
                {
                    controller.Marker += OnMarker;
                    controller.TrialChanged += OnTrial;
                    controller.TargetChanged += OnTarget;
                    if (controller.liveArm != null) controller.liveArm.Calibrated += OnCalibrated;
                    if (controller.otherArm != null) controller.otherArm.Calibrated += OnCalibrated;
                }
                else
                {
                    controller.Marker -= OnMarker;
                    controller.TrialChanged -= OnTrial;
                    controller.TargetChanged -= OnTarget;
                    if (controller.liveArm != null) controller.liveArm.Calibrated -= OnCalibrated;
                    if (controller.otherArm != null) controller.otherArm.Calibrated -= OnCalibrated;
                }
            }
            if (server != null)
            {
                if (on) server.RawReceived += OnRawIn; else server.RawReceived -= OnRawIn;
            }
            if (streamSender != null)
            {
                if (on) streamSender.Sent += OnRawOut; else streamSender.Sent -= OnRawOut;
            }
        }

        // ------------------------------------------------------------------ event sinks

        private void OnMarker(string category, string name, double value, string detail) => events?.Log(category, name, value, detail);
        private void OnTrial(int t) => events?.Log("trial", "trial", t, string.Empty);
        private void OnTarget(int t) => events?.Log("target", "target", t, controller != null ? controller.TargetAngles.ToString() : string.Empty);
        private void OnRawIn(string text, IPEndPoint ep) => rawUdp?.Log(RawUdpLogger.In, ep != null ? ep.ToString() : string.Empty, text);
        private void OnRawOut(string text)
        {
            if (logOutboundStream || !text.StartsWith("D,", StringComparison.Ordinal))
            {
                rawUdp?.Log(RawUdpLogger.Out, streamSender != null ? streamSender.TargetDescription : string.Empty, text);
            }
        }

        private void OnCalibrated(ArmCalibration c)
        {
            if (c == null) return;
            if (calibration != null)
            {
                calibration.BeginRow();
                calibration.Append(DateTime.UtcNow.ToString("o"));
                calibration.Append(Time.realtimeSinceStartupAsDouble);
                calibration.Append(c.side.ShortName());
                calibration.Append(c.sourceUsed);
                calibration.Append(c.shoulderOffsetFromHead);
                calibration.Append(c.armLength);
                calibration.Append(c.modelScale);
                calibration.Append(c.wristOffsetInHandBasis);
                calibration.Append(c.handBasisOffset);
                calibration.EndRow();
                calibration.Flush();
            }
            events?.Log("calibration", "arm_" + c.side.ShortName(), c.armLength, $"scale={c.modelScale:F3} source={c.sourceUsed}");

            SessionManager sm = SessionManager.Instance;
            if (sm != null && sm.CurrentSession != null)
            {
                SessionInfo.ArmCalibrationRecord r = c.side == ArmSide.Left ? sm.CurrentSession.leftArm : sm.CurrentSession.rightArm;
                r.calibrated = true;
                r.shoulderOffsetFromHead = c.shoulderOffsetFromHead;
                r.armLength = c.armLength;
                r.modelScale = c.modelScale;
                r.wristOffset = c.wristOffsetInHandBasis;
                r.handRotationOffset = c.handBasisOffset;
                r.calibratedUtc = c.calibratedUtc;
                sm.UpdateSessionJson();
            }
        }

        /// <summary>Public entry for other scripts to log an event.</summary>
        public void LogEvent(string category, string name, double value = double.NaN, string detail = "") => events?.Log(category, name, value, detail);

        // ------------------------------------------------------------------ headers

        private void BuildHeaders()
        {
            colsK.Clear();
            colsK.AddRange(new[] { "unity_time", "utc", "frame", "phase", "trial", "target", "status", "status_source", "live_side", "live_source", "live_tracked" });
            foreach (string nm in UpperLimbAngles.Names) colsK.Add("live_" + nm);
            foreach (string nm in UpperLimbAngles.Names) colsK.Add("target_" + nm);
            colsK.AddRange(new[] { "target_dof_mask", "max_error_deg", "dwell_progress", "ik_clamped", "other_side", "other_source" });
            foreach (string nm in UpperLimbAngles.Names) colsK.Add("other_" + nm);
            colsK.AddRange(CsvWriter.Vector3Columns("live_shoulder"));
            colsK.AddRange(CsvWriter.Vector3Columns("live_elbow"));
            colsK.AddRange(CsvWriter.Vector3Columns("live_wrist"));
            colsK.AddRange(CsvWriter.Vector3Columns("ghost_wrist"));
            colsK.AddRange(new[] { "legacy_t1", "legacy_t2", "legacy_i1", "legacy_i2" });

            colsT.Clear();
            colsT.AddRange(new[] { "unity_time", "utc", "frame", "head_valid" });
            colsT.AddRange(CsvWriter.Vector3Columns("head_pos"));
            colsT.AddRange(CsvWriter.QuaternionColumns("head_rot"));
            colsT.AddRange(new[] { "gaze_eye_tracked", "gaze_valid" });
            colsT.AddRange(CsvWriter.Vector3Columns("gaze_pos"));
            colsT.AddRange(CsvWriter.Vector3Columns("gaze_dir"));
            colsT.AddRange(new[] { "gaze_hit", "gaze_hit_kind", "gaze_hit_name" });
            colsT.AddRange(CsvWriter.Vector3Columns("gaze_hit_pos"));
            colsT.Add("gaze_hit_dist");
            foreach (string s in new[] { "L", "R" })
            {
                colsT.Add(s + "_source");
                colsT.Add(s + "_ctrl_tracked");
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_ctrl_pos"));
                colsT.AddRange(CsvWriter.QuaternionColumns(s + "_ctrl_rot"));
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_ctrl_vel"));
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_ctrl_angvel"));
                colsT.AddRange(new[] { s + "_trigger", s + "_grip", s + "_primary", s + "_secondary", s + "_menu", s + "_hand_tracked" });
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_wrist_pos"));
                colsT.AddRange(CsvWriter.QuaternionColumns(s + "_wrist_rot"));
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_palm_pos"));
                colsT.AddRange(CsvWriter.QuaternionColumns(s + "_palm_rot"));
                colsT.AddRange(CsvWriter.Vector3Columns(s + "_avatar_wrist_pos"));
                colsT.AddRange(CsvWriter.QuaternionColumns(s + "_avatar_hand_rot"));
            }

            colsH.Clear();
            colsH.AddRange(new[] { "unity_time", "frame", "side" });
            int n = XrTrackingService.JointCount;
            jointNames = new string[n];
            for (int i = 0; i < n; i++)
            {
                jointNames[i] = XRHandJointIDUtility.FromIndex(i).ToString();
                colsH.Add(jointNames[i] + "_valid");
                colsH.AddRange(CsvWriter.Vector3Columns(jointNames[i] + "_pos"));
                colsH.AddRange(CsvWriter.QuaternionColumns(jointNames[i] + "_rot"));
            }
        }

        // ------------------------------------------------------------------ per frame

        private void LateUpdate()
        {
            if (!IsRecording) return;
            double ut = Time.realtimeSinceStartupAsDouble;
            string utc = DateTime.UtcNow.ToString("o");
            int frame = Time.frameCount;
            XrTrackingService t = XrTrackingService.Instance;

            if (kinematics != null) WriteKinematics(ut, utc, frame);
            if (tracking != null) WriteTracking(ut, utc, frame, t);
            if (handJoints != null && t != null)
            {
                WriteHandJoints(ut, frame, ArmSide.Left, t);
                WriteHandJoints(ut, frame, ArmSide.Right, t);
            }
            if (t != null) CheckOrigin(t);
        }

        private void WriteKinematics(double ut, string utc, int frame)
        {
            ExperimentController c = controller;
            CsvWriter w = kinematics;
            w.BeginRow();
            w.Append(ut); w.Append(utc); w.Append(frame);
            if (c == null)
            {
                w.AppendEmpty(w.ColumnCount - 3);
                w.EndRow();
                return;
            }
            ArmDriver live = c.liveArm;
            ArmDriver other = c.otherArm;
            w.Append(c.PhaseName); w.Append(c.Trial); w.Append(c.Target);
            w.Append(c.Status.ToString()); w.Append(c.StatusFromSender ? "sender" : "local");
            w.Append(live != null ? live.side.ShortName() : string.Empty);
            w.Append(live != null ? live.ActiveSource.ToString() : string.Empty);
            w.Append(live != null && live.IsTrackedThisFrame);
            UpperLimbAngles la = live != null ? live.CurrentAngles : UpperLimbAngles.Zero;
            for (int i = 0; i < UpperLimbAngles.Count; i++) w.Append(la[i]);
            UpperLimbAngles ta = c.TargetAngles;
            for (int i = 0; i < UpperLimbAngles.Count; i++) if (c.HasTarget) w.Append(ta[i]); else w.AppendEmpty();
            w.Append(c.HasTarget ? c.TargetDofMask : 0);
            w.Append(c.CurrentMaxError);
            w.Append(c.DwellProgress);
            w.Append(live != null && live.rig != null && live.rig.CurrentPose.targetClamped);
            w.Append(other != null ? other.side.ShortName() : string.Empty);
            w.Append(other != null ? other.ActiveSource.ToString() : string.Empty);
            UpperLimbAngles oa = other != null ? other.CurrentAngles : UpperLimbAngles.Zero;
            for (int i = 0; i < UpperLimbAngles.Count; i++) if (other != null) w.Append(oa[i]); else w.AppendEmpty();
            if (live != null && live.rig != null)
            {
                live.rig.GetJointPositions(out Vector3 s, out Vector3 e, out Vector3 wr);
                w.Append(s); w.Append(e); w.Append(wr);
            }
            else w.AppendEmpty(9);
            if (c.ghostArm != null && c.ghostArm.gameObject.activeInHierarchy)
            {
                c.ghostArm.GetJointPositions(out _, out _, out Vector3 gw);
                w.Append(gw);
            }
            else w.AppendEmpty(3);
            Vector4 lv = c.LastLegacyValues;
            w.Append(lv.x); w.Append(lv.y); w.Append(lv.z); w.Append(lv.w);
            w.EndRow();
        }

        private void WriteTracking(double ut, string utc, int frame, XrTrackingService t)
        {
            CsvWriter w = tracking;
            GazeRaycaster g = GazeRaycaster.Instance;
            w.BeginRow();
            w.Append(ut); w.Append(utc); w.Append(frame);
            TrackedPose head = t != null ? t.Head : (Camera.main != null ? TrackedPose.FromTransform(Camera.main.transform) : TrackedPose.Invalid);
            w.Append(head.isValid); w.Append(head.position); w.Append(head.rotation);
            TrackedPose gaze = g != null ? g.Ray : (t != null ? t.Gaze : TrackedPose.Invalid);
            w.Append(g != null && g.UsingEyeTracking); w.Append(gaze.isValid);
            w.Append(gaze.position); w.Append(gaze.rotation * Vector3.forward);
            if (g != null)
            {
                w.Append(g.HasHit); w.Append(g.HitKind.ToString()); w.Append(g.HitName);
                if (g.HasHit) w.Append(g.HitPoint); else w.AppendEmpty(3);
                w.Append(g.HitDistance);
            }
            else
            {
                w.Append(false); w.AppendEmpty(2); w.AppendEmpty(3); w.AppendEmpty();
            }
            WriteSide(w, ArmSide.Left, t);
            WriteSide(w, ArmSide.Right, t);
            w.EndRow();
        }

        private void WriteSide(CsvWriter w, ArmSide side, XrTrackingService t)
        {
            ArmDriver d = controller != null ? controller.ArmFor(side) : null;
            w.Append(d != null ? d.ActiveSource.ToString() : string.Empty);
            if (t == null)
            {
                w.Append(false); w.AppendEmpty(3 + 4 + 3 + 3); w.AppendEmpty(5); w.Append(false);
                w.AppendEmpty(3 + 4 + 3 + 4);
            }
            else
            {
                HandTrackingSample s = t.GetSample(side);
                w.Append(s.controllerTracked);
                w.Append(s.controller.position); w.Append(s.controller.rotation);
                w.Append(s.controllerVelocity); w.Append(s.controllerAngularVelocity);
                w.Append(s.trigger); w.Append(s.grip); w.Append(s.primaryButton); w.Append(s.secondaryButton); w.Append(s.menuButton);
                w.Append(s.handTracked);
                w.Append(s.handWrist.position); w.Append(s.handWrist.rotation);
                w.Append(s.handPalm.position); w.Append(s.handPalm.rotation);
            }
            if (d != null && d.rig != null && d.rig.HasPose)
            {
                d.rig.GetJointPositions(out _, out _, out Vector3 wr);
                w.Append(wr); w.Append(d.rig.CurrentHandBasis);
            }
            else w.AppendEmpty(7);
        }

        private void WriteHandJoints(double ut, int frame, ArmSide side, XrTrackingService t)
        {
            if (!t.GetSample(side).handTracked) return;
            Pose[] joints = t.GetJoints(side);
            bool[] valid = t.GetJointValidity(side);
            CsvWriter w = handJoints;
            w.BeginRow();
            w.Append(ut); w.Append(frame); w.Append(side.ShortName());
            for (int i = 0; i < joints.Length; i++)
            {
                w.Append(valid[i]);
                if (valid[i]) { w.Append(joints[i].position); w.Append(joints[i].rotation); }
                else w.AppendEmpty(7);
            }
            w.EndRow();
        }

        private void CheckOrigin(XrTrackingService t)
        {
            TrackedPose o = t.OriginPose;
            if (!o.isValid) return;
            bool changed = !lastOrigin.isValid
                || (o.position - lastOrigin.position).sqrMagnitude > 1e-6f
                || Quaternion.Angle(o.rotation, lastOrigin.rotation) > 0.1f;
            if (!changed) return;
            lastOrigin = o;
            SessionManager sm = SessionManager.Instance;
            if (sm != null && sm.CurrentSession != null)
            {
                WriteOrigin(sm.CurrentSession, o);
                sm.UpdateSessionJson();
            }
            events?.Log("xr_origin", "pose", double.NaN, $"pos={o.position:F3} rot={o.rotation.eulerAngles:F1}");
        }

        private void WriteOrigin(SessionInfo info, TrackedPose o)
        {
            info.xrOrigin.position = o.position;
            info.xrOrigin.rotation = o.rotation;
            info.xrOrigin.utc = DateTime.UtcNow.ToString("o");
            lastOrigin = o;
        }
    }
}
