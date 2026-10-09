using System;
using FAMOT.Core;
using FAMOT.Session;
using UnityEngine;
using JointLimits = FAMOT.Session.JointLimits;

namespace FAMOT.Experiment
{
    /// <summary>
    /// Built-in target achievement protocol used when no external sender (MATLAB/Python) is driving the
    /// experiment: random targets inside the configured range of motion, a fixed number of targets per
    /// trial and trials per session, dwell-time success and rest periods. Everything is routed through
    /// <see cref="ExperimentController"/> so recording and UI behave exactly as with UDP control.
    /// </summary>
    public class ProtocolRunner : MonoBehaviour
    {
        public enum RunnerState { Idle, WaitingForTracking, Countdown, Target, Hold, Rest, Finished }

        public ExperimentController controller;

        [Header("Start conditions")]
        [Tooltip("Start automatically when no UDP sender has been heard for 'senderSilenceSeconds'.")]
        public bool autoStartWhenNoSender = false;
        public float senderSilenceSeconds = 5f;
        public float countdownSeconds = 3f;

        [Header("Protocol")]
        public int trials = 3;
        public int targetsPerTrial = 10;
        [Tooltip("Which joints get random targets. Order: ShF ShA ShR ElF FaS WrE WrR.")]
        public bool[] activeDofs = { false, false, false, false, true, true, false };
        [Tooltip("Random targets are drawn inside [min, max] of the subject's joint limits, scaled by this factor.")]
        [Range(0.1f, 1f)] public float rangeFraction = 0.8f;
        public float targetTimeoutSeconds = 15f;
        public float holdAfterSuccessSeconds = 1f;
        public float restBetweenTargetsSeconds = 2f;
        public float restBetweenTrialsSeconds = 8f;
        public int randomSeed = 0;

        [Header("Limits used for target generation")]
        public JointLimits limits = new JointLimits();

        public RunnerState State { get; private set; } = RunnerState.Idle;
        public int CurrentTrial { get; private set; }
        public int CurrentTarget { get; private set; }
        public float StateTime => Time.time - stateStart;
        public int SeedUsed { get; private set; }

        public event Action<RunnerState> StateChanged;

        private float stateStart;
        private System.Random rng;
        private bool achieved;
        private float silenceStart = -1f;

        private void Awake()
        {
            if (controller == null) controller = GetComponent<ExperimentController>();
            if (controller == null) controller = ExperimentController.Instance;
        }

        private void OnEnable()
        {
            if (controller != null) controller.TargetAchieved += OnAchieved;
        }

        private void OnDisable()
        {
            if (controller != null) controller.TargetAchieved -= OnAchieved;
        }

        private void OnAchieved()
        {
            if (State == RunnerState.Target) achieved = true;
        }

        private void Update()
        {
            if (controller == null) return;

            if (State == RunnerState.Idle)
            {
                if (!autoStartWhenNoSender) return;
                if (controller.SenderActive)
                {
                    silenceStart = -1f;
                    return;
                }
                if (silenceStart < 0f) silenceStart = Time.time;
                if (Time.time - silenceStart >= senderSilenceSeconds) StartProtocol();
                return;
            }

            // An external sender takes priority at any time.
            if (controller.SenderActive && State != RunnerState.Finished)
            {
                StopProtocol("external sender active");
                return;
            }

            switch (State)
            {
                case RunnerState.WaitingForTracking:
                    if (controller.liveArm == null || controller.liveArm.IsTrackedThisFrame || controller.liveArm.ActiveSource == Avatar.ArmInputSource.ExternalAngles)
                    {
                        SetState(RunnerState.Countdown);
                    }
                    break;
                case RunnerState.Countdown:
                    controller.EnterRest($"Get ready… {Mathf.CeilToInt(countdownSeconds - StateTime)}");
                    if (StateTime >= countdownSeconds) NextTarget();
                    break;
                case RunnerState.Target:
                    if (achieved)
                    {
                        SetState(RunnerState.Hold);
                    }
                    else if (StateTime >= targetTimeoutSeconds)
                    {
                        controller.Mark("target_timeout", $"trial={CurrentTrial} target={CurrentTarget}");
                        SetState(RunnerState.Rest);
                        controller.ClearTarget();
                        controller.EnterRest("Time out – relax");
                    }
                    break;
                case RunnerState.Hold:
                    if (StateTime >= holdAfterSuccessSeconds)
                    {
                        controller.ClearTarget();
                        bool trialDone = CurrentTarget >= targetsPerTrial;
                        controller.EnterRest(trialDone ? "Trial complete – relax" : "Relax");
                        SetState(RunnerState.Rest);
                    }
                    break;
                case RunnerState.Rest:
                    {
                        bool trialDone = CurrentTarget >= targetsPerTrial;
                        float restTime = trialDone ? restBetweenTrialsSeconds : restBetweenTargetsSeconds;
                        if (StateTime >= restTime)
                        {
                            if (trialDone && CurrentTrial >= trials)
                            {
                                Finish();
                            }
                            else
                            {
                                NextTarget();
                            }
                        }
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>Starts (or restarts) the protocol from trial 1.</summary>
        public void StartProtocol()
        {
            SeedUsed = randomSeed != 0 ? randomSeed : Environment.TickCount;
            rng = new System.Random(SeedUsed);
            CurrentTrial = 0;
            CurrentTarget = 0;
            achieved = false;
            controller.SetCounters(0, 0, targetsPerTrial);
            controller.Mark("protocol_start", $"trials={trials} targetsPerTrial={targetsPerTrial} seed={SeedUsed} dofs={DofString()}");
            SetState(RunnerState.WaitingForTracking);
        }

        public void StopProtocol(string reason = "stopped")
        {
            if (State == RunnerState.Idle) return;
            controller.Mark("protocol_stop", reason);
            controller.ClearTarget();
            SetState(RunnerState.Idle);
            silenceStart = -1f;
        }

        private void Finish()
        {
            controller.ClearTarget();
            controller.EnterRest("Protocol finished – thank you");
            controller.Mark("protocol_finished", $"trials={CurrentTrial}");
            SetState(RunnerState.Finished);
        }

        private void NextTarget()
        {
            if (CurrentTrial == 0 || CurrentTarget >= targetsPerTrial)
            {
                CurrentTrial++;
                CurrentTarget = 0;
            }
            CurrentTarget++;
            achieved = false;
            UpperLimbAngles min = limits.Min();
            UpperLimbAngles max = limits.Max();
            var target = new UpperLimbAngles();
            int mask = 0;
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if (i < activeDofs.Length && activeDofs[i])
                {
                    float lo = min[i] * rangeFraction;
                    float hi = max[i] * rangeFraction;
                    target[i] = Mathf.Round(Mathf.Lerp(lo, hi, (float)rng.NextDouble()));
                    mask |= 1 << i;
                }
            }
            controller.SetCounters(CurrentTrial, CurrentTarget, targetsPerTrial);
            controller.EnterTest();
            controller.SetTarget(target, mask);
            controller.Mark("target_set", $"trial={CurrentTrial} target={CurrentTarget} angles={target}");
            SetState(RunnerState.Target);
        }

        private void SetState(RunnerState s)
        {
            State = s;
            stateStart = Time.time;
            StateChanged?.Invoke(s);
        }

        private string DofString()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if (i < activeDofs.Length && activeDofs[i])
                {
                    if (sb.Length > 0) sb.Append('|');
                    sb.Append(UpperLimbAngles.ShortNames[i]);
                }
            }
            return sb.ToString();
        }
    }
}
