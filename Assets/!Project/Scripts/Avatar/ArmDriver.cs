using System;
using FAMOT.Core;
using FAMOT.Input;
using FAMOT.Kinematics;
using UnityEngine;

namespace FAMOT.Avatar
{
    /// <summary>Where the pose of an arm comes from.</summary>
    public enum ArmInputSource
    {
        /// <summary>Hand tracking when the hand is visible, otherwise the controller, otherwise external angles.</summary>
        Auto = 0,
        Controller = 1,
        HandTracking = 2,
        /// <summary>Angles pushed from outside (UDP, protocol runner). Forward kinematics.</summary>
        ExternalAngles = 3,
        /// <summary>Hold the last pose.</summary>
        None = 4
    }

    /// <summary>
    /// Drives one <see cref="ArmRig"/> from the selected input source: inverse kinematics from a tracked
    /// controller or hand, or forward kinematics from externally supplied anatomical angles.
    /// Also owns the seated calibration for that arm.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class ArmDriver : MonoBehaviour
    {
        public ArmSide side = ArmSide.Right;
        public ArmRig rig;
        public ShoulderAnchor shoulderAnchor;

        [Header("Source")]
        public ArmInputSource requestedSource = ArmInputSource.Auto;

        [Header("Tracking → hand mapping")]
        [Tooltip("Offset from the controller grip position to the wrist, along the hand's long axis (metres, positive = towards the wrist).")]
        public float controllerWristDistance = 0.07f;

        [Tooltip("Default device-to-hand-basis offsets used before calibration (controller grip pose convention).")]
        public Vector3 defaultControllerBasisEuler = new Vector3(0f, 0f, 0f);

        [Range(0f, 1f)]
        [Tooltip("0 = raw tracking, 1 = very smooth. Applied to the wrist target and hand orientation.")]
        public float smoothing = 0.15f;

        [Range(0f, 1f)]
        [Tooltip("How much the hand roll swings the elbow in or out.")]
        public float elbowRollInfluence = 0.5f;

        [Header("Joint limits (degrees, relative to rest)")]
        public bool applyJointLimits = false;
        public UpperLimbAngles limitMin = new UpperLimbAngles { shoulderFlexion = -90, shoulderAbduction = -60, shoulderRotation = -100, elbowFlexion = -40, forearmSupination = -120, wristExtension = -90, wristRadial = -45 };
        public UpperLimbAngles limitMax = new UpperLimbAngles { shoulderFlexion = 170, shoulderAbduction = 170, shoulderRotation = 100, elbowFlexion = 150, forearmSupination = 120, wristExtension = 90, wristRadial = 45 };

        [Header("Calibration")]
        public ArmCalibration calibration;

        /// <summary>Angles to use when the resolved source is ExternalAngles.</summary>
        [NonSerialized] public UpperLimbAngles externalAngles;
        [NonSerialized] public bool hasExternalAngles;

        public ArmInputSource ActiveSource { get; private set; } = ArmInputSource.None;
        public bool IsTrackedThisFrame { get; private set; }
        public Vector3 WristTargetWorld { get; private set; }
        public Quaternion HandBasisWorld { get; private set; } = Quaternion.identity;
        public TrackedPose DevicePose { get; private set; } = TrackedPose.Invalid;
        public UpperLimbAngles CurrentAngles => rig != null ? rig.CurrentAngles : UpperLimbAngles.Zero;

        public event Action<ArmCalibration> Calibrated;

        private bool hasSmoothed;
        private Vector3 smoothedWrist;
        private Quaternion smoothedBasis = Quaternion.identity;

        private void Reset()
        {
            rig = GetComponentInChildren<ArmRig>();
            shoulderAnchor = GetComponent<ShoulderAnchor>();
        }

        private void Awake()
        {
            if (rig == null) rig = GetComponentInChildren<ArmRig>();
            if (shoulderAnchor == null) shoulderAnchor = GetComponent<ShoulderAnchor>();
            if (calibration == null || calibration.side != side) calibration = ArmCalibration.Default(side);
            if (rig != null) side = rig.side;
        }

        private void Update()
        {
            if (rig == null || !rig.HasRestPose) return;
            XrTrackingService t = XrTrackingService.Instance;
            ActiveSource = ResolveSource(t);
            IsTrackedThisFrame = false;

            switch (ActiveSource)
            {
                case ArmInputSource.Controller:
                case ArmInputSource.HandTracking:
                    DriveFromTracking(t, ActiveSource == ArmInputSource.HandTracking);
                    break;
                case ArmInputSource.ExternalAngles:
                    rig.ApplyAngles(applyJointLimits ? externalAngles.Clamped(limitMin, limitMax) : externalAngles);
                    break;
                default:
                    break;
            }
        }

        private ArmInputSource ResolveSource(XrTrackingService t)
        {
            bool handOk = t != null && t.GetSample(side).handTracked && t.GetSample(side).handWrist.isValid;
            bool ctrlOk = t != null && t.GetSample(side).controllerTracked;
            switch (requestedSource)
            {
                case ArmInputSource.Controller: return ctrlOk ? ArmInputSource.Controller : ArmInputSource.None;
                case ArmInputSource.HandTracking: return handOk ? ArmInputSource.HandTracking : ArmInputSource.None;
                case ArmInputSource.ExternalAngles: return hasExternalAngles ? ArmInputSource.ExternalAngles : ArmInputSource.None;
                case ArmInputSource.None: return ArmInputSource.None;
                default:
                    if (handOk) return ArmInputSource.HandTracking;
                    if (ctrlOk) return ArmInputSource.Controller;
                    if (hasExternalAngles) return ArmInputSource.ExternalAngles;
                    return ArmInputSource.None;
            }
        }

        private void DriveFromTracking(XrTrackingService t, bool useHand)
        {
            HandTrackingSample s = t.GetSample(side);
            TrackedPose device = useHand ? s.handWrist : s.controller;
            DevicePose = device;
            if (!device.isValid) return;

            Quaternion basisOffset = calibration != null && calibration.isCalibrated
                ? calibration.handBasisOffset
                : DefaultBasisOffset(useHand);
            Quaternion basis = device.rotation * basisOffset;
            Vector3 wristOffset = calibration != null && calibration.isCalibrated
                ? calibration.wristOffsetInHandBasis
                : (useHand ? Vector3.zero : new Vector3(0f, -controllerWristDistance, 0f));
            Vector3 wrist = device.position + basis * wristOffset;

            if (!hasSmoothed || smoothing <= 0f)
            {
                smoothedWrist = wrist;
                smoothedBasis = basis;
                hasSmoothed = true;
            }
            else
            {
                // Frame-rate independent exponential smoothing; "smoothing" maps to a time constant of up to ~0.15 s.
                float tau = Mathf.Lerp(0.0f, 0.15f, smoothing);
                float k = tau <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / tau);
                smoothedWrist = Vector3.Lerp(smoothedWrist, wrist, k);
                smoothedBasis = Quaternion.Slerp(smoothedBasis, basis, k);
            }

            WristTargetWorld = smoothedWrist;
            HandBasisWorld = smoothedBasis;
            IsTrackedThisFrame = true;
            rig.SolveIK(smoothedWrist, smoothedBasis, null,
                applyJointLimits ? limitMin : (UpperLimbAngles?)null,
                applyJointLimits ? limitMax : (UpperLimbAngles?)null,
                elbowRollInfluence);
        }

        /// <summary>
        /// Device-to-anatomical-basis offset used before calibration.
        /// Hand tracking (XR Hands wrist joint): +Z along the fingers, +Y out of the back of the hand.
        /// Controller grip pose (OpenXR via Unity): -Z roughly along the fingers for a relaxed grip, +Y towards the thumb side
        /// of the handle; the default below maps that approximately and is refined by calibration.
        /// </summary>
        private Quaternion DefaultBasisOffset(bool useHand)
        {
            if (useHand)
            {
                // Joint frame (X right, Y dorsal, Z fingers) → basis (X flexion axis, Y fingers, Z dorsal), expressed in joint space:
                // basis.Y = joint.Z, basis.Z = joint.Y, basis.X = Y x Z = -joint.X.
                return AnatomicalMath.BasisFromXY(Vector3.left, Vector3.forward);
            }
            // Controller: fingers wrap the handle; the grip pose +Z points along the handle towards the wrist.
            // basis.Y (fingers) = -grip.Z (approx.), basis.Z (dorsal) = grip.X * side sign.
            float ls = side.LateralSign();
            Vector3 fingers = Vector3.back;
            Vector3 dorsal = Vector3.right * ls;
            Vector3 flex = Vector3.Cross(fingers, dorsal);
            return AnatomicalMath.BasisFromXY(flex, fingers) * Quaternion.Euler(defaultControllerBasisEuler);
        }

        /// <summary>
        /// Seated calibration: call while the user sits upright with the arm hanging relaxed at the side,
        /// palm facing the thigh and thumb forward. Uses the currently active tracked device.
        /// Returns false when no device is tracked.
        /// </summary>
        public bool CalibrateFromRestPose()
        {
            XrTrackingService t = XrTrackingService.Instance;
            if (t == null || rig == null || !rig.HasRestPose || !t.Head.isValid) return false;
            HandTrackingSample s = t.GetSample(side);
            bool useHand = s.handTracked && s.handWrist.isValid;
            TrackedPose device = useHand ? s.handWrist : s.controller;
            if (!device.isValid) return false;

            float ls = side.LateralSign();
            Quaternion yaw = ShoulderAnchor.YawOnly(t.Head.rotation);
            Vector3 fwd = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;
            Vector3 up = Vector3.up;
            Vector3 lateral = right * ls;

            // Expected anatomical hand basis in the hanging pose: fingers down, dorsal lateral.
            Vector3 fingers = -up;
            Vector3 dorsal = lateral;
            Vector3 flex = Vector3.Cross(fingers, dorsal);
            Quaternion expectedBasis = AnatomicalMath.BasisFromXY(flex, fingers);

            var c = new ArmCalibration
            {
                side = side,
                isCalibrated = true,
                handBasisOffset = Quaternion.Inverse(device.rotation) * expectedBasis,
                wristOffsetInHandBasis = useHand ? Vector3.zero : new Vector3(0f, -controllerWristDistance, 0f),
                sourceUsed = useHand ? "HandTracking" : "Controller",
                calibratedUtc = DateTime.UtcNow.ToString("o")
            };

            Vector3 wrist = device.position + expectedBasis * c.wristOffsetInHandBasis;
            Vector3 rel = wrist - t.Head.position;
            float relRight = Vector3.Dot(rel, right);
            float relFwd = Vector3.Dot(rel, fwd);
            float relUp = Vector3.Dot(rel, up);

            float eyeToShoulderDrop = 0.22f;
            c.shoulderOffsetFromHead = new Vector3(relRight, -eyeToShoulderDrop, relFwd);
            c.armLength = Mathf.Clamp(-eyeToShoulderDrop - relUp, 0.35f, 0.95f);
            float modelLength = rig.ModelArmLength;
            c.modelScale = modelLength > 0.05f ? Mathf.Clamp(c.armLength / modelLength, 0.6f, 1.5f) : 1f;

            calibration = c;
            if (shoulderAnchor != null) shoulderAnchor.ApplyCalibration(c);
            hasSmoothed = false;
            Calibrated?.Invoke(c);
            return true;
        }

        /// <summary>Applies a previously stored calibration (e.g. from the session file).</summary>
        public void ApplyCalibration(ArmCalibration c)
        {
            if (c == null) return;
            calibration = c.Clone();
            calibration.side = side;
            if (shoulderAnchor != null) shoulderAnchor.ApplyCalibration(calibration);
            hasSmoothed = false;
        }

        /// <summary>Convenience: pushes angles for forward-kinematics driving.</summary>
        public void SetExternalAngles(UpperLimbAngles angles)
        {
            externalAngles = angles;
            hasExternalAngles = true;
        }

        public void ClearExternalAngles()
        {
            hasExternalAngles = false;
        }
    }
}
