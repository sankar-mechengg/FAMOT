using System.Text;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Experiment;
using FAMOT.Input;
using FAMOT.Protocol;
using FAMOT.Recording;
using FAMOT.Session;
using TMPro;
using UnityEngine;

namespace FAMOT.UI
{
    /// <summary>
    /// Text panel for the experimenter, shown on the desktop mirror window (Screen Space Overlay canvases are
    /// not rendered inside the headset). Lists session, recording, protocol, tracking, calibration, joint
    /// angles, gaze and UDP status.
    /// </summary>
    public class ExperimenterPanel : MonoBehaviour
    {
        public TMP_Text text;
        public TMP_Text recIndicator;
        public float refreshInterval = 0.1f;
        public bool showAngles = true;
        public bool visible = true;
        public KeyCode toggleKey = KeyCode.F1;

        private float nextRefresh;
        private readonly StringBuilder sb = new StringBuilder(2048);

        private void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
            {
                visible = !visible;
                if (text != null) text.gameObject.SetActive(visible);
            }
            if (!visible) return;
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            ExperimentController c = ExperimentController.Instance;
            SessionRecorder rec = SessionRecorder.Instance;
            SessionManager sm = SessionManager.Instance;
            XrTrackingService t = XrTrackingService.Instance;
            GazeRaycaster g = GazeRaycaster.Instance;
            ProtocolRunner pr = c != null ? c.GetComponent<ProtocolRunner>() : null;
            UdpCommandServer srv = c != null ? c.server : null;

            sb.Clear();
            sb.Append("<b>FAMOT</b> ").Append(Application.version).Append("   ");
            if (sm != null && sm.CurrentSubject != null) sb.Append(sm.CurrentSubject.subjectId).Append(' ').Append(sm.CurrentSubject.subjectName);
            sb.Append('\n');
            if (sm != null && sm.CurrentSession != null) sb.Append("session ").Append(sm.CurrentSession.sessionId).Append('\n');

            bool recording = rec != null && rec.IsRecording;
            sb.Append(recording ? "<color=#ff4040>● REC</color> " : "<color=#888888>○ not recording</color> ");
            if (rec != null) sb.Append("kin ").Append(rec.KinematicsRows).Append("  trk ").Append(rec.TrackingRows).Append("  ev ").Append(rec.EventCount);
            sb.Append('\n');
            if (recIndicator != null)
            {
                recIndicator.text = recording ? "● REC" : "";
                recIndicator.color = recording && Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f ? Color.red : new Color(1f, 0f, 0f, 0.35f);
            }

            if (c != null)
            {
                sb.Append("<b>").Append(c.PhaseName).Append("</b>  trial ").Append(c.Trial).Append("  target ").Append(c.Target);
                if (c.NumTargetsPerTrial > 0) sb.Append('/').Append(c.NumTargetsPerTrial);
                sb.Append("  status ").Append(c.Status).Append(c.StatusFromSender ? " (sender)" : " (local)");
                if (c.HasTarget) sb.Append("  err ").Append(c.CurrentMaxError.ToString("F1")).Append("°  dwell ").Append((c.DwellProgress * 100f).ToString("F0")).Append('%');
                sb.Append('\n');
                sb.Append("UDP ").Append(srv != null ? (srv.IsListening ? "listening :" + srv.ListenPort : "stopped") : "n/a");
                if (srv != null) sb.Append("  rx ").Append(srv.ReceivedCount).Append("  errors ").Append(srv.ParseErrorCount);
                sb.Append("  sender ").Append(c.SenderActive ? "active" : "silent");
                if (c.streamSender != null) sb.Append("  tx→").Append(c.streamSender.TargetDescription).Append(' ').Append(c.streamSender.SentCount);
                sb.Append('\n');
                if (pr != null) sb.Append("protocol ").Append(pr.State).Append(pr.State != ProtocolRunner.RunnerState.Idle ? $" t{pr.CurrentTrial}/{pr.trials} #{pr.CurrentTarget}/{pr.targetsPerTrial}" : "").Append('\n');

                AppendArm(c.liveArm, "live");
                AppendArm(c.otherArm, "other");
                if (showAngles)
                {
                    sb.Append("<mspace=0.6em>      ");
                    foreach (string n in UpperLimbAngles.ShortNames) sb.Append(n.PadLeft(7));
                    sb.Append('\n');
                    if (c.liveArm != null) AppendAngles("live ", c.liveArm.CurrentAngles);
                    if (c.HasTarget) AppendAngles("tgt  ", c.TargetAngles);
                    if (c.otherArm != null) AppendAngles("other", c.otherArm.CurrentAngles);
                    sb.Append("</mspace>");
                }
            }

            if (t != null)
            {
                sb.Append("head ").Append(t.Head.isValid ? "ok" : "--");
                sb.Append("  eye ").Append(t.GazeTracked ? "tracked" : "--");
                sb.Append("  hands ").Append(t.HandSubsystemRunning ? "subsystem on" : "off");
                sb.Append("  L:").Append(t.Left.handTracked ? "hand" : t.Left.controllerTracked ? "ctrl" : "--");
                sb.Append("  R:").Append(t.Right.handTracked ? "hand" : t.Right.controllerTracked ? "ctrl" : "--");
                sb.Append('\n');
            }
            if (g != null)
            {
                sb.Append("gaze ").Append(g.UsingEyeTracking ? "eye" : "head").Append(" → ");
                sb.Append(g.HasHit ? g.HitKind + " " + g.HitName + " " + g.HitDistance.ToString("F2") + "m" : "nothing");
                sb.Append('\n');
            }
            sb.Append("<size=80%>C calibrate  M marker  P protocol  G gaze ray  F1 hide</size>");

            if (text != null) text.text = sb.ToString();
        }

        private void AppendArm(ArmDriver d, string label)
        {
            if (d == null) return;
            sb.Append(label).Append(' ').Append(d.side.ShortName()).Append(": ").Append(d.ActiveSource);
            sb.Append(d.IsTrackedThisFrame ? " tracked" : "");
            if (d.calibration != null && d.calibration.isCalibrated)
            {
                sb.Append("  cal ").Append(d.calibration.armLength.ToString("F2")).Append("m ×").Append(d.calibration.modelScale.ToString("F2"));
            }
            else sb.Append("  <color=#ffb040>not calibrated</color>");
            sb.Append('\n');
        }

        private void AppendAngles(string label, UpperLimbAngles a)
        {
            sb.Append(label);
            for (int i = 0; i < UpperLimbAngles.Count; i++) sb.Append(a[i].ToString("F0").PadLeft(7));
            sb.Append('\n');
        }
    }
}
