using System;
using System.Net;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Input;
using FAMOT.Kinematics;
using FAMOT.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FAMOT.Experiment
{
    public enum ExperimentPhase
    {
        Idle = 0,
        Calibration = 1,
        Test = 2,
        Rest = 3
    }

    /// <summary>
    /// Central experiment logic: interprets UDP commands (legacy 2-DOF and new 7-DOF), poses the target
    /// (ghost) arm, feeds the live arm when it is externally driven, evaluates target achievement, keeps
    /// trial/target counters, updates the information panel and streams per-frame data back out.
    /// Replaces the logic that used to live in UDPReceiver.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public class ExperimentController : MonoBehaviour
    {
        public static ExperimentController Instance { get; private set; }

        [Header("Networking")]
        public UdpCommandServer server;
        public UdpStreamSender streamSender;
        [Tooltip("Stream a D line every frame to the configured target (MATLAB/Python receiver).")]
        public bool streamEveryFrame = true;

        [Header("Arms")]
        [Tooltip("Arm the participant controls (dominant side).")]
        public ArmDriver liveArm;
        [Tooltip("Transparent target arm on the same side.")]
        public ArmRig ghostArm;
        [Tooltip("Optional non-dominant arm (always tracked, never targeted).")]
        public ArmDriver otherArm;
        public FingerRetargeter liveFingers;

        [Header("Joint limits (2-DOF legacy mapping, degrees)")]
        public float minFE = -70f;
        public float maxFE = 70f;
        public float minPS = -20f;
        public float maxPS = 90f;
        [Tooltip("Legacy ScriptableObject with the default limits; read on start when assigned.")]
        public MaximumAngleSO legacyLimits;

        [Header("Target achievement (built-in evaluation)")]
        [Tooltip("Evaluate success in Unity when the sender does not provide an S/F flag.")]
        public bool evaluateSuccessLocally = true;
        public float successThresholdDegrees = 8f;
        public float successDwellSeconds = 0.5f;

        [Header("UI")]
        public TMP_Text headerText;
        public TMP_Text infoText;
        public TMP_Text trialText;
        public TMP_Text targetText;
        public Image ledImage;
        public Color ledSuccess = Color.green;
        public Color ledFail = new Color(0.71f, 0.71f, 0.71f, 1f);
        public Color ledIdle = new Color(0.71f, 0.71f, 0.71f, 1f);

        // ---------------- state ----------------
        public ExperimentPhase Phase { get; private set; } = ExperimentPhase.Idle;
        public string PhaseName => Phase.ToString();
        public CalibrationPose CurrentCalibrationPose { get; private set; } = CalibrationPose.Rest;
        public int NumTargetsPerTrial { get; private set; }
        public int Trial { get; private set; }
        public int Target { get; private set; }
        public TargetStatus Status { get; private set; } = TargetStatus.None;
        public bool StatusFromSender { get; private set; }
        public UpperLimbAngles TargetAngles { get; private set; }
        public UpperLimbAngles InputAngles { get; private set; }
        /// <summary>Which DOFs the current target constrains (bit i = UpperLimbAngles index i).</summary>
        public int TargetDofMask { get; private set; }
        public bool HasTarget { get; private set; }
        /// <summary>Raw 0..100 values of the last legacy message (t1,t2,i1,i2).</summary>
        public Vector4 LastLegacyValues { get; private set; } = new Vector4(50, 50, 50, 50);
        public float LastMessageTime { get; private set; } = -1f;
        public bool SenderActive => LastMessageTime >= 0f && Time.unscaledTime - LastMessageTime < 2f;
        public float CurrentMaxError { get; private set; }
        public float DwellProgress { get; private set; }

        public const int LegacyDofMask = (1 << 4) | (1 << 5); // forearm supination + wrist extension
        public const int AllDofMask = (1 << UpperLimbAngles.Count) - 1;

        public event Action<ExperimentPhase> PhaseChanged;
        public event Action<int> TrialChanged;
        public event Action<int> TargetChanged;
        public event Action<TargetStatus> StatusChanged;
        public event Action<UdpMessage> CommandReceived;
        /// <summary>Raised when the built-in evaluator decides a target is achieved.</summary>
        public event Action TargetAchieved;
        /// <summary>Generic event marker (category, name, value, detail) for the recorder.</summary>
        public event Action<string, string, double, string> Marker;

        private float dwellTimer;
        private bool achievedThisTarget;
        private UpperLimbAngles lastLegacyTarget;
        private bool hasLegacyTarget;
        private XrTrackingService tracking;
        private string appVersion;

        private void Awake()
        {
            Instance = this;
            appVersion = Application.version;
            if (legacyLimits != null)
            {
                minFE = legacyLimits.maxAngleFlexion;
                maxFE = legacyLimits.maxAngleExtension;
                minPS = legacyLimits.maxAnglePronation;
                maxPS = legacyLimits.maxAngleSupination;
            }
        }

        private void OnEnable()
        {
            if (server == null) server = FindFirstObjectByType<UdpCommandServer>();
            if (streamSender == null) streamSender = FindFirstObjectByType<UdpStreamSender>();
            if (server != null) server.MessageReceived += OnMessage;
            tracking = XrTrackingService.Instance;
            SetHeader("FAMOT ready");
            SetInfo("Waiting for commands");
            SetLed(ledIdle);
            UpdateCounters();
            if (ghostArm != null) ghostArm.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (server != null) server.MessageReceived -= OnMessage;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (tracking == null) tracking = XrTrackingService.Instance;

            if (liveArm != null && liveArm.rig != null) InputAngles = liveArm.rig.CurrentAngles;

            EvaluateTarget();

            if (streamEveryFrame && streamSender != null)
            {
                StreamFrame();
            }
        }

        // ------------------------------------------------------------------ commands

        private void OnMessage(UdpMessage m, IPEndPoint sender)
        {
            LastMessageTime = Time.unscaledTime;
            CommandReceived?.Invoke(m);
            switch (m.type)
            {
                case UdpMessageType.CalibrationPose: HandleCalibrationPose(m.pose); break;
                case UdpMessageType.CalibrationLimits: HandleLimits(m); break;
                case UdpMessageType.LegacyTest: HandleLegacyTest(m); break;
                case UdpMessageType.Kinematics: HandleKinematics(m); break;
                case UdpMessageType.Event: HandleEvent(m); break;
                case UdpMessageType.Query:
                    server?.ReplyToLastSender(StreamFormatter.StatusLine(appVersion, true, Session.SessionManager.Instance != null && Session.SessionManager.Instance.CurrentSession != null ? Session.SessionManager.Instance.CurrentSession.sessionId : "", server.ListenPort));
                    break;
                default:
                    break;
            }
        }

        private void HandleCalibrationPose(CalibrationPose pose)
        {
            SetPhase(ExperimentPhase.Calibration);
            CurrentCalibrationPose = pose;
            HasTarget = false;
            SetHeader("Calibration Mode");
            var angles = new UpperLimbAngles();
            switch (pose)
            {
                case CalibrationPose.Rest: SetInfo("Move your hand to the rest position"); break;
                case CalibrationPose.Flexion: SetInfo("Move your hand to maximum flexion"); angles.wristExtension = minFE; break;
                case CalibrationPose.Extension: SetInfo("Move your hand to maximum extension"); angles.wristExtension = maxFE; break;
                case CalibrationPose.Pronation: SetInfo("Move your hand to maximum pronation"); angles.forearmSupination = minPS; break;
                case CalibrationPose.Supination: SetInfo("Move your hand to maximum supination"); angles.forearmSupination = maxPS; break;
            }
            // Demonstrate the pose on the live arm when it is externally driven, otherwise on the ghost arm.
            if (liveArm != null && liveArm.requestedSource == ArmInputSource.ExternalAngles)
            {
                liveArm.SetExternalAngles(angles);
                if (ghostArm != null) ghostArm.gameObject.SetActive(false);
            }
            else if (ghostArm != null)
            {
                ghostArm.gameObject.SetActive(true);
                ghostArm.ApplyAngles(angles);
            }
            TargetAngles = angles;
            Marker?.Invoke("calibration", "pose_" + pose, double.NaN, string.Empty);
        }

        private void HandleLimits(UdpMessage m)
        {
            minFE = m.minFE; maxFE = m.maxFE; minPS = m.minPS; maxPS = m.maxPS;
            NumTargetsPerTrial = Mathf.Max(0, m.numTargets);
            Trial = Mathf.Max(Trial, 1);
            Target = 0;
            UpdateCounters();
            TrialChanged?.Invoke(Trial);
            Marker?.Invoke("calibration", "limits", NumTargetsPerTrial, $"FE[{minFE},{maxFE}] PS[{minPS},{maxPS}]");
        }

        private void HandleLegacyTest(UdpMessage m)
        {
            EnterTest();
            LastLegacyValues = new Vector4(m.t1, m.t2, m.i1, m.i2);
            var target = new UpperLimbAngles
            {
                wristExtension = LegacyMapping.PiecewiseMap(m.t1, minFE, maxFE),
                forearmSupination = LegacyMapping.PiecewiseMap(m.t2, minPS, maxPS)
            };
            var input = new UpperLimbAngles
            {
                wristExtension = LegacyMapping.PiecewiseMap(m.i1, minFE, maxFE),
                forearmSupination = LegacyMapping.PiecewiseMap(m.i2, minPS, maxPS)
            };
            // Legacy senders do not announce targets explicitly: detect a new target when the target value changes.
            bool newTarget = !hasLegacyTarget || UpperLimbAngles.MaxAbsDifference(target, lastLegacyTarget) > 0.01f;
            lastLegacyTarget = target;
            hasLegacyTarget = true;
            if (newTarget) AdvanceTargetCounter();
            SetTarget(target, LegacyDofMask);
            if (liveArm != null && liveArm.requestedSource == ArmInputSource.ExternalAngles) liveArm.SetExternalAngles(input);
            ApplySenderStatus(m.status);
        }

        private void HandleKinematics(UdpMessage m)
        {
            EnterTest();
            bool newTarget = !HasTarget || UpperLimbAngles.MaxAbsDifference(m.targetAngles, TargetAngles) > 0.01f;
            if (newTarget) AdvanceTargetCounter();
            SetTarget(m.targetAngles, AllDofMask);
            if (liveArm != null && liveArm.requestedSource == ArmInputSource.ExternalAngles) liveArm.SetExternalAngles(m.inputAngles);
            ApplySenderStatus(m.status);
        }

        private void HandleEvent(UdpMessage m)
        {
            string n = (m.eventName ?? string.Empty).ToUpperInvariant();
            int v = float.IsNaN(m.eventValue) ? -1 : Mathf.RoundToInt(m.eventValue);
            switch (n)
            {
                case "TRIAL_START":
                    Trial = v >= 0 ? v : Trial + 1;
                    Target = 0;
                    UpdateCounters();
                    TrialChanged?.Invoke(Trial);
                    break;
                case "TARGET_START":
                    Target = v >= 0 ? v : Target + 1;
                    achievedThisTarget = false;
                    dwellTimer = 0f;
                    UpdateCounters();
                    TargetChanged?.Invoke(Target);
                    break;
                case "REST_START":
                    SetPhase(ExperimentPhase.Rest);
                    SetInfo("Relax");
                    break;
                case "REST_END":
                case "TEST_START":
                    EnterTest();
                    break;
                case "TRIAL_END":
                case "TARGET_END":
                default:
                    break;
            }
            Marker?.Invoke("udp_event", n, m.eventValue, m.eventDetail ?? string.Empty);
        }

        // ------------------------------------------------------------------ target logic

        /// <summary>Sets the current target and poses the ghost arm. Usable by the built-in protocol runner.</summary>
        public void SetTarget(UpperLimbAngles angles, int dofMask)
        {
            TargetAngles = angles;
            TargetDofMask = dofMask;
            HasTarget = true;
            if (ghostArm != null)
            {
                if (!ghostArm.gameObject.activeSelf) ghostArm.gameObject.SetActive(true);
                ghostArm.ApplyAngles(angles);
            }
        }

        /// <summary>Clears the target and hides the ghost arm.</summary>
        public void ClearTarget()
        {
            HasTarget = false;
            if (ghostArm != null) ghostArm.gameObject.SetActive(false);
            SetStatus(TargetStatus.None, false);
        }

        /// <summary>Advances the target counter and rolls over to the next trial when all targets are done.</summary>
        public void AdvanceTargetCounter()
        {
            Target++;
            achievedThisTarget = false;
            dwellTimer = 0f;
            if (NumTargetsPerTrial > 0 && Target > NumTargetsPerTrial)
            {
                Target = 1;
                Trial++;
                TrialChanged?.Invoke(Trial);
            }
            if (Trial == 0)
            {
                Trial = 1;
                TrialChanged?.Invoke(Trial);
            }
            UpdateCounters();
            TargetChanged?.Invoke(Target);
        }

        /// <summary>Used by the protocol runner to set counters explicitly.</summary>
        public void SetCounters(int trial, int target, int numTargets)
        {
            bool trialChanged = trial != Trial;
            bool targetChanged = target != Target;
            Trial = trial;
            Target = target;
            NumTargetsPerTrial = numTargets;
            achievedThisTarget = false;
            dwellTimer = 0f;
            UpdateCounters();
            if (trialChanged) TrialChanged?.Invoke(Trial);
            if (targetChanged) TargetChanged?.Invoke(Target);
        }

        public void EnterTest()
        {
            if (Phase != ExperimentPhase.Test)
            {
                SetPhase(ExperimentPhase.Test);
                SetHeader("Test Mode");
                SetInfo("Match the ghost hand as closely as you can");
            }
        }

        public void EnterRest(string message = "Relax")
        {
            SetPhase(ExperimentPhase.Rest);
            SetInfo(message);
        }

        public void SetPhase(ExperimentPhase p)
        {
            if (Phase == p) return;
            Phase = p;
            PhaseChanged?.Invoke(p);
            Marker?.Invoke("phase", p.ToString(), double.NaN, string.Empty);
        }

        private void ApplySenderStatus(TargetStatus s)
        {
            if (s == TargetStatus.None)
            {
                StatusFromSender = false;
                return;
            }
            StatusFromSender = true;
            SetStatus(s, true);
        }

        private void SetStatus(TargetStatus s, bool fromSender)
        {
            if (s != Status)
            {
                Status = s;
                StatusChanged?.Invoke(s);
                Marker?.Invoke("status", s.ToString(), double.NaN, fromSender ? "sender" : "local");
            }
            SetLed(s == TargetStatus.Success ? ledSuccess : s == TargetStatus.Fail ? ledFail : ledIdle);
        }

        private void EvaluateTarget()
        {
            if (!HasTarget || Phase != ExperimentPhase.Test)
            {
                CurrentMaxError = 0f;
                DwellProgress = 0f;
                return;
            }
            float maxErr = 0f;
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if ((TargetDofMask & (1 << i)) == 0) continue;
                maxErr = Mathf.Max(maxErr, Mathf.Abs(AnatomicalMath.WrapDegrees(InputAngles[i] - TargetAngles[i])));
            }
            CurrentMaxError = maxErr;

            if (!evaluateSuccessLocally || StatusFromSender) return;

            if (maxErr <= successThresholdDegrees)
            {
                dwellTimer += Time.deltaTime;
                DwellProgress = successDwellSeconds <= 0f ? 1f : Mathf.Clamp01(dwellTimer / successDwellSeconds);
                if (dwellTimer >= successDwellSeconds)
                {
                    if (!achievedThisTarget)
                    {
                        achievedThisTarget = true;
                        TargetAchieved?.Invoke();
                        Marker?.Invoke("target", "achieved", Target, $"trial={Trial} maxErr={maxErr:F1}");
                    }
                    SetStatus(TargetStatus.Success, false);
                }
            }
            else
            {
                dwellTimer = 0f;
                DwellProgress = 0f;
                if (!achievedThisTarget) SetStatus(TargetStatus.Fail, false);
            }
        }

        // ------------------------------------------------------------------ output

        private void StreamFrame()
        {
            TrackedPose head = tracking != null ? tracking.Head : TrackedPose.Invalid;
            TrackedPose gaze = tracking != null ? tracking.Gaze : TrackedPose.Invalid;
            TrackedPose lw = WristPose(ArmSide.Left);
            TrackedPose rw = WristPose(ArmSide.Right);
            string line = StreamFormatter.Frame(Time.realtimeSinceStartupAsDouble, Time.frameCount, PhaseName, Trial, Target,
                InputAngles, TargetAngles, head, gaze, lw, rw, Status);
            streamSender.Send(line);
        }

        /// <summary>World pose of the live avatar wrist for a side (identity rotation when unknown).</summary>
        public TrackedPose WristPose(ArmSide side)
        {
            ArmDriver d = ArmFor(side);
            if (d == null || d.rig == null || !d.rig.HasPose) return TrackedPose.Invalid;
            d.rig.GetJointPositions(out _, out _, out Vector3 w);
            return new TrackedPose(w, d.rig.CurrentHandBasis, true);
        }

        public ArmDriver ArmFor(ArmSide side)
        {
            if (liveArm != null && liveArm.side == side) return liveArm;
            if (otherArm != null && otherArm.side == side) return otherArm;
            return null;
        }

        /// <summary>Emits a user marker (keyboard / controller / UI).</summary>
        public void Mark(string name, string detail = "")
        {
            Marker?.Invoke("marker", name, double.NaN, detail);
        }

        // ------------------------------------------------------------------ UI helpers

        private void SetHeader(string s) { if (headerText != null) headerText.text = s; }
        private void SetInfo(string s) { if (infoText != null) infoText.text = s; }
        private void SetLed(Color c) { if (ledImage != null) ledImage.color = c; }

        private void UpdateCounters()
        {
            if (trialText != null) trialText.text = Trial > 0 ? "Trial " + Trial : "Trial -";
            if (targetText != null)
            {
                targetText.text = NumTargetsPerTrial > 0 ? $"Target {Target}/{NumTargetsPerTrial}" : Target > 0 ? "Target " + Target : "Target -";
            }
        }
    }
}
