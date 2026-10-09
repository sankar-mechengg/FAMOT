using System;
using FAMOT.Avatar;
using FAMOT.Core;
using FAMOT.Input;
using FAMOT.Session;
using UnityEngine;
using JointLimits = FAMOT.Session.JointLimits;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace FAMOT.Experiment
{
    /// <summary>
    /// Wires an experiment scene at runtime from the login choices: selects the live arm side, applies the
    /// chosen input source, starts the data session (recording starts automatically) and provides the
    /// keyboard / controller shortcuts used by the experimenter:
    ///   C  – seated calibration of both arms         M – marker
    ///   P  – start / stop the built-in protocol      G – toggle gaze debug ray
    ///   Both grips + triggers held for 1 s – calibration from inside VR
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class ExperimentSceneBootstrap : MonoBehaviour
    {
        public ExperimentController controller;
        public ProtocolRunner protocolRunner;
        public GazeRaycaster gaze;

        [Header("Arms in this scene")]
        public ArmDriver rightArm;
        public ArmDriver leftArm;
        public Kinematics.ArmRig rightGhost;
        public Kinematics.ArmRig leftGhost;
        public FingerRetargeter rightFingers;
        public FingerRetargeter leftFingers;

        [Header("Scene type")]
        public VisualisationMode sceneMode = VisualisationMode.Desktop;
        [Tooltip("Hand used when the scene is started directly in the editor (no login).")]
        public ArmSide defaultHand = ArmSide.Right;
        public ArmInputSource defaultLiveSource = ArmInputSource.ExternalAngles;
        [Tooltip("Show the non-dominant arm as well (VR).")]
        public bool showOtherArm = true;

        [Header("Session")]
        public bool startSessionOnLoad = true;
        public string loginSceneName = "Scene0";

        public ArmSide LiveSide { get; private set; }
        public bool CalibrationRequested { get; private set; }

        private float chordTimer;
        private bool chordFired;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<ExperimentController>() ?? FindFirstObjectByType<ExperimentController>();
            if (protocolRunner == null) protocolRunner = FindFirstObjectByType<ProtocolRunner>();
            if (gaze == null) gaze = FindFirstObjectByType<GazeRaycaster>();

            ArmSide hand = LaunchConfig.IsSet ? LaunchConfig.Hand : defaultHand;
            ArmInputSource source = LaunchConfig.IsSet ? LaunchConfig.LiveArmSource : defaultLiveSource;
            ConfigureArms(hand, source);
        }

        private void Start()
        {
            if (startSessionOnLoad) StartSession();
        }

        /// <summary>Assigns live/ghost/other arms according to the dominant side.</summary>
        public void ConfigureArms(ArmSide hand, ArmInputSource liveSource)
        {
            LiveSide = hand;
            ArmDriver live = hand == ArmSide.Right ? rightArm : leftArm;
            ArmDriver other = hand == ArmSide.Right ? leftArm : rightArm;
            Kinematics.ArmRig ghost = hand == ArmSide.Right ? rightGhost : leftGhost;
            Kinematics.ArmRig otherGhost = hand == ArmSide.Right ? leftGhost : rightGhost;
            FingerRetargeter liveF = hand == ArmSide.Right ? rightFingers : leftFingers;

            if (live == null)
            {
                Debug.LogWarning($"[FAMOT] No {hand} arm in this scene; falling back to the available arm.", this);
                live = rightArm != null ? rightArm : leftArm;
                other = live == rightArm ? leftArm : rightArm;
                ghost = live == rightArm ? rightGhost : leftGhost;
                otherGhost = live == rightArm ? leftGhost : rightGhost;
                liveF = live == rightArm ? rightFingers : leftFingers;
                if (live != null) LiveSide = live.side;
            }

            if (live != null)
            {
                live.gameObject.SetActive(true);
                live.requestedSource = liveSource;
            }
            if (other != null)
            {
                bool show = showOtherArm && sceneMode == VisualisationMode.VR;
                other.gameObject.SetActive(show);
                other.requestedSource = ArmInputSource.Auto;
            }
            if (otherGhost != null) otherGhost.gameObject.SetActive(false);
            if (ghost != null) ghost.gameObject.SetActive(false);

            if (controller != null)
            {
                controller.liveArm = live;
                controller.otherArm = other != null && other.gameObject.activeSelf ? other : null;
                controller.ghostArm = ghost;
                controller.liveFingers = liveF;
            }
        }

        private void StartSession()
        {
            SessionManager sm = SessionManager.Ensure();
            if (sm.CurrentSubject == null)
            {
                SubjectInfo s = LaunchConfig.Subject;
                if (s == null)
                {
                    s = SubjectInfo.Create("DBUG", "EditorDebug");
                    s.handedness = defaultHand.ToString();
                    Debug.Log("[FAMOT] No login data – using a debug subject.");
                }
                sm.SetSubject(s);
            }
            string mode = LaunchConfig.IsSet ? LaunchConfig.ModeName : (sceneMode == VisualisationMode.VR ? "VR" : "Desktop");
            string source = controller != null && controller.liveArm != null ? controller.liveArm.requestedSource.ToString() : string.Empty;
            try
            {
                sm.StartSession(mode, LiveSide.ShortName(), source);
            }
            catch (Exception e)
            {
                Debug.LogError("[FAMOT] Could not start session: " + e.Message, this);
            }
            ApplySubjectLimits();
        }

        private void ApplySubjectLimits()
        {
            SessionManager sm = SessionManager.Instance;
            if (sm == null || sm.CurrentSubject == null || controller == null) return;
            JointLimits jl = sm.CurrentSubject.jointLimits;
            if (jl == null) return;
            jl.EnsureValid();
            controller.minFE = jl.maxFlexion;
            controller.maxFE = jl.maxExtension;
            controller.minPS = jl.maxPronation;
            controller.maxPS = jl.maxSupination;
            if (protocolRunner != null) protocolRunner.limits = jl.Clone();
            foreach (ArmDriver d in new[] { rightArm, leftArm })
            {
                if (d == null) continue;
                d.limitMin = jl.Min();
                d.limitMax = jl.Max();
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.cKey.wasPressedThisFrame) CalibrateArms();
                if (kb.mKey.wasPressedThisFrame) controller?.Mark("key_M");
                if (kb.pKey.wasPressedThisFrame && protocolRunner != null)
                {
                    if (protocolRunner.State == ProtocolRunner.RunnerState.Idle || protocolRunner.State == ProtocolRunner.RunnerState.Finished) protocolRunner.StartProtocol();
                    else protocolRunner.StopProtocol("key_P");
                }
                if (kb.gKey.wasPressedThisFrame && gaze != null) gaze.drawDebugRay = !gaze.drawDebugRay;
            }

            // Controller chord: both grips and both triggers held for one second.
            XrTrackingService t = XrTrackingService.Instance;
            if (t != null)
            {
                bool chord = t.Left.grip > 0.8f && t.Right.grip > 0.8f && t.Left.trigger > 0.8f && t.Right.trigger > 0.8f;
                if (chord)
                {
                    chordTimer += Time.deltaTime;
                    if (chordTimer >= 1f && !chordFired)
                    {
                        chordFired = true;
                        CalibrateArms();
                    }
                }
                else
                {
                    chordTimer = 0f;
                    chordFired = false;
                }
            }
        }

        /// <summary>Runs the seated calibration on every tracked arm.</summary>
        public void CalibrateArms()
        {
            int done = 0;
            foreach (ArmDriver d in new[] { rightArm, leftArm })
            {
                if (d == null || !d.isActiveAndEnabled) continue;
                if (d.CalibrateFromRestPose()) done++;
            }
            controller?.Mark("calibration_requested", $"calibrated_arms={done}");
            if (done == 0) Debug.LogWarning("[FAMOT] Calibration requested but no tracked hand/controller was available.");
        }

        /// <summary>Ends the session and returns to the login scene.</summary>
        public void ReturnToLogin()
        {
            SessionManager.Instance?.EndSession();
            LaunchConfig.Clear();
            SceneManager.LoadScene(loginSceneName);
        }
    }
}
