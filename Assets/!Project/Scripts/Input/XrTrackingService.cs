using System;
using System.Collections.Generic;
using FAMOT.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace FAMOT.Input
{
    /// <summary>
    /// Per-hand tracked data gathered from either the controller or the hand-tracking subsystem.
    /// </summary>
    public struct HandTrackingSample
    {
        /// <summary>Grip pose of the controller (world space).</summary>
        public TrackedPose controller;
        /// <summary>Wrist joint pose from hand tracking (world space).</summary>
        public TrackedPose handWrist;
        /// <summary>Palm joint pose from hand tracking (world space).</summary>
        public TrackedPose handPalm;
        public float trigger;
        public float grip;
        public bool primaryButton;
        public bool secondaryButton;
        public bool menuButton;
        public bool handTracked;
        public bool controllerTracked;
        /// <summary>Linear velocity of the controller (world, m/s), when available.</summary>
        public Vector3 controllerVelocity;
        public Vector3 controllerAngularVelocity;
    }

    /// <summary>
    /// Central reader of XR tracking data: head, eye gaze, both controllers and both tracked hands
    /// (26 joints each via XR Hands). All poses are converted to world space through the XR Origin.
    /// Other components read the latest sample from this service instead of touching the Input System.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class XrTrackingService : MonoBehaviour
    {
        public static XrTrackingService Instance { get; private set; }

        [Tooltip("XR Origin used to convert tracking-space poses to world space. Found automatically when empty.")]
        public XROrigin xrOrigin;

        [Tooltip("Subscribe to the XR Hands subsystem and expose finger joints.")]
        public bool enableHandTracking = true;

        [Tooltip("Read the OpenXR eye gaze pose (requires the Eye Gaze Interaction Profile).")]
        public bool enableEyeGaze = true;

        public static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

        // ---------- latest samples ----------
        public TrackedPose Head { get; private set; } = TrackedPose.Invalid;
        public TrackedPose Gaze { get; private set; } = TrackedPose.Invalid;
        public bool GazeTracked { get; private set; }
        public HandTrackingSample Left => left;
        public HandTrackingSample Right => right;
        private HandTrackingSample left;
        private HandTrackingSample right;

        /// <summary>World-space poses of the 26 hand joints per side (index = XRHandJointID.ToIndex()).</summary>
        public Pose[] LeftJoints => leftJoints;
        public Pose[] RightJoints => rightJoints;
        public bool[] LeftJointValid => leftJointValid;
        public bool[] RightJointValid => rightJointValid;
        private readonly Pose[] leftJoints = new Pose[JointCount];
        private readonly Pose[] rightJoints = new Pose[JointCount];
        private readonly bool[] leftJointValid = new bool[JointCount];
        private readonly bool[] rightJointValid = new bool[JointCount];

        public bool HandSubsystemRunning => handSubsystem != null && handSubsystem.running;
        public bool IsXrActive => UnityEngine.XR.XRSettings.isDeviceActive;

        /// <summary>Raised after every refresh, before other scripts' Update (execution order -100).</summary>
        public event Action Updated;

        // ---------- input actions (created in code so no asset wiring is needed) ----------
        private InputAction headPos, headRot, headTracked;
        private InputAction gazePos, gazeRot, gazeTracked;
        private InputAction lPos, lRot, lTracked, lTrigger, lGrip, lPrimary, lSecondary, lMenu, lVel, lAngVel;
        private InputAction rPos, rRot, rTracked, rTrigger, rGrip, rPrimary, rSecondary, rMenu, rVel, rAngVel;
        private readonly List<InputAction> actions = new List<InputAction>();

        private XRHandSubsystem handSubsystem;
        private readonly List<XRHandSubsystem> subsystemsBuffer = new List<XRHandSubsystem>();
        private float nextSubsystemSearch;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            if (xrOrigin == null) xrOrigin = FindFirstObjectByType<XROrigin>();
            CreateActions();
        }

        private void OnEnable()
        {
            foreach (var a in actions) a.Enable();
            TryBindHandSubsystem();
        }

        private void OnDisable()
        {
            foreach (var a in actions) a.Disable();
            UnbindHandSubsystem();
        }

        private void OnDestroy()
        {
            foreach (var a in actions) a.Dispose();
            actions.Clear();
            if (Instance == this) Instance = null;
        }

        private InputAction Make(string binding, InputActionType type = InputActionType.Value)
        {
            var a = new InputAction(type: type, binding: binding);
            actions.Add(a);
            return a;
        }

        private void CreateActions()
        {
            headPos = Make("<XRHMD>/centerEyePosition");
            headRot = Make("<XRHMD>/centerEyeRotation");
            headTracked = Make("<XRHMD>/isTracked", InputActionType.Button);

            gazePos = Make("<EyeGaze>/pose/position");
            gazeRot = Make("<EyeGaze>/pose/rotation");
            gazeTracked = Make("<EyeGaze>/pose/isTracked", InputActionType.Button);

            lPos = Make("<XRController>{LeftHand}/devicePosition");
            lRot = Make("<XRController>{LeftHand}/deviceRotation");
            lTracked = Make("<XRController>{LeftHand}/isTracked", InputActionType.Button);
            lTrigger = Make("<XRController>{LeftHand}/trigger");
            lGrip = Make("<XRController>{LeftHand}/grip");
            lPrimary = Make("<XRController>{LeftHand}/primaryButton", InputActionType.Button);
            lSecondary = Make("<XRController>{LeftHand}/secondaryButton", InputActionType.Button);
            lMenu = Make("<XRController>{LeftHand}/menu", InputActionType.Button);
            lVel = Make("<XRController>{LeftHand}/deviceVelocity");
            lAngVel = Make("<XRController>{LeftHand}/deviceAngularVelocity");

            rPos = Make("<XRController>{RightHand}/devicePosition");
            rRot = Make("<XRController>{RightHand}/deviceRotation");
            rTracked = Make("<XRController>{RightHand}/isTracked", InputActionType.Button);
            rTrigger = Make("<XRController>{RightHand}/trigger");
            rGrip = Make("<XRController>{RightHand}/grip");
            rPrimary = Make("<XRController>{RightHand}/primaryButton", InputActionType.Button);
            rSecondary = Make("<XRController>{RightHand}/secondaryButton", InputActionType.Button);
            rMenu = Make("<XRController>{RightHand}/menu", InputActionType.Button);
            rVel = Make("<XRController>{RightHand}/deviceVelocity");
            rAngVel = Make("<XRController>{RightHand}/deviceAngularVelocity");
        }

        private void Update()
        {
            Refresh();
            Updated?.Invoke();
        }

        /// <summary>Transform used to map tracking space into world space.</summary>
        public Transform TrackingSpace
        {
            get
            {
                if (xrOrigin == null) return transform;
                if (xrOrigin.CameraFloorOffsetObject != null) return xrOrigin.CameraFloorOffsetObject.transform;
                return xrOrigin.Origin != null ? xrOrigin.Origin.transform : xrOrigin.transform;
            }
        }

        public TrackedPose ToWorld(Vector3 trackingPos, Quaternion trackingRot, bool valid)
        {
            Transform ts = TrackingSpace;
            return new TrackedPose(ts.TransformPoint(trackingPos), ts.rotation * trackingRot, valid);
        }

        private static bool IsActive(InputAction a) => a != null && a.enabled && a.activeControl != null;

        private TrackedPose ReadPose(InputAction pos, InputAction rot, InputAction tracked)
        {
            bool bound = IsActive(pos) || IsActive(rot);
            if (!bound) return TrackedPose.Invalid;
            bool valid = tracked == null || !IsActive(tracked) || tracked.ReadValue<float>() > 0.5f;
            Vector3 p = pos.ReadValue<Vector3>();
            Quaternion q = rot.ReadValue<Quaternion>();
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) q = Quaternion.identity;
            return ToWorld(p, q, valid);
        }

        private void Refresh()
        {
            Head = ReadPose(headPos, headRot, headTracked);
            if (!Head.isValid && Camera.main != null && IsXrActive)
            {
                // Fall back to the camera transform (driven by the TrackedPoseDriver).
                Head = new TrackedPose(Camera.main.transform.position, Camera.main.transform.rotation, true);
            }

            if (enableEyeGaze)
            {
                TrackedPose g = ReadPose(gazePos, gazeRot, gazeTracked);
                GazeTracked = g.isValid && IsActive(gazePos);
                Gaze = GazeTracked ? g : new TrackedPose(Head.position, Head.rotation, false);
            }
            else
            {
                GazeTracked = false;
                Gaze = new TrackedPose(Head.position, Head.rotation, false);
            }

            ReadController(ref left, lPos, lRot, lTracked, lTrigger, lGrip, lPrimary, lSecondary, lMenu, lVel, lAngVel);
            ReadController(ref right, rPos, rRot, rTracked, rTrigger, rGrip, rPrimary, rSecondary, rMenu, rVel, rAngVel);

            if (enableHandTracking)
            {
                if (handSubsystem == null && Time.unscaledTime >= nextSubsystemSearch)
                {
                    nextSubsystemSearch = Time.unscaledTime + 2f;
                    TryBindHandSubsystem();
                }
                if (handSubsystem != null && handSubsystem.running)
                {
                    ReadHand(handSubsystem.leftHand, ref left, leftJoints, leftJointValid);
                    ReadHand(handSubsystem.rightHand, ref right, rightJoints, rightJointValid);
                }
                else
                {
                    left.handTracked = false;
                    right.handTracked = false;
                    left.handWrist = TrackedPose.Invalid;
                    right.handWrist = TrackedPose.Invalid;
                }
            }
        }

        private void ReadController(ref HandTrackingSample s, InputAction pos, InputAction rot, InputAction tracked,
            InputAction trigger, InputAction grip, InputAction primary, InputAction secondary, InputAction menu,
            InputAction vel, InputAction angVel)
        {
            s.controller = ReadPose(pos, rot, tracked);
            s.controllerTracked = s.controller.isValid;
            s.trigger = IsActive(trigger) ? trigger.ReadValue<float>() : 0f;
            s.grip = IsActive(grip) ? grip.ReadValue<float>() : 0f;
            s.primaryButton = IsActive(primary) && primary.ReadValue<float>() > 0.5f;
            s.secondaryButton = IsActive(secondary) && secondary.ReadValue<float>() > 0.5f;
            s.menuButton = IsActive(menu) && menu.ReadValue<float>() > 0.5f;
            Transform ts = TrackingSpace;
            s.controllerVelocity = IsActive(vel) ? ts.TransformVector(vel.ReadValue<Vector3>()) : Vector3.zero;
            s.controllerAngularVelocity = IsActive(angVel) ? ts.TransformVector(angVel.ReadValue<Vector3>()) : Vector3.zero;
        }

        private void ReadHand(XRHand hand, ref HandTrackingSample s, Pose[] joints, bool[] valid)
        {
            s.handTracked = hand.isTracked;
            Transform ts = TrackingSpace;
            if (!hand.isTracked)
            {
                s.handWrist = TrackedPose.Invalid;
                s.handPalm = TrackedPose.Invalid;
                for (int i = 0; i < valid.Length; i++) valid[i] = false;
                return;
            }
            for (int i = 0; i < JointCount; i++)
            {
                XRHandJointID id = XRHandJointIDUtility.FromIndex(i);
                XRHandJoint joint = hand.GetJoint(id);
                if (joint.TryGetPose(out Pose p))
                {
                    joints[i] = new Pose(ts.TransformPoint(p.position), ts.rotation * p.rotation);
                    valid[i] = true;
                }
                else
                {
                    valid[i] = false;
                }
            }
            int wristIdx = XRHandJointID.Wrist.ToIndex();
            int palmIdx = XRHandJointID.Palm.ToIndex();
            s.handWrist = valid[wristIdx] ? new TrackedPose(joints[wristIdx].position, joints[wristIdx].rotation, true) : TrackedPose.Invalid;
            s.handPalm = valid[palmIdx] ? new TrackedPose(joints[palmIdx].position, joints[palmIdx].rotation, true) : TrackedPose.Invalid;
        }

        private void TryBindHandSubsystem()
        {
            if (!enableHandTracking || handSubsystem != null) return;
            subsystemsBuffer.Clear();
            SubsystemManager.GetSubsystems(subsystemsBuffer);
            foreach (var s in subsystemsBuffer)
            {
                if (s.running)
                {
                    handSubsystem = s;
                    break;
                }
            }
            if (handSubsystem == null && subsystemsBuffer.Count > 0) handSubsystem = subsystemsBuffer[0];
        }

        private void UnbindHandSubsystem()
        {
            handSubsystem = null;
        }

        /// <summary>Sample for the requested side.</summary>
        public HandTrackingSample GetSample(ArmSide side) => side == ArmSide.Left ? left : right;

        public Pose[] GetJoints(ArmSide side) => side == ArmSide.Left ? leftJoints : rightJoints;
        public bool[] GetJointValidity(ArmSide side) => side == ArmSide.Left ? leftJointValid : rightJointValid;

        /// <summary>World-space pose of the XR Origin (for the session file).</summary>
        public TrackedPose OriginPose => xrOrigin != null ? TrackedPose.FromTransform(xrOrigin.transform) : TrackedPose.Invalid;
    }
}
