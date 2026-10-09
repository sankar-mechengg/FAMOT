using System.Collections.Generic;
using FAMOT.Core;
using FAMOT.Input;
using FAMOT.Kinematics;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace FAMOT.Avatar
{
    /// <summary>
    /// Poses the finger bones of the arm model. With hand tracking the XR Hands joints are retargeted onto
    /// the bones; with a controller the fingers curl according to grip and trigger values.
    ///
    /// Retargeting is offset based: for every bone we remember, at rest, the rotation that maps the bone's
    /// own frame onto a "joint-like" frame (forward = towards the child, up = dorsal). At runtime the bone
    /// receives jointRotation * offset, which keeps the model's bone roll intact.
    /// </summary>
    [DefaultExecutionOrder(-30)]
    public class FingerRetargeter : MonoBehaviour
    {
        [System.Serializable]
        public class FingerChain
        {
            public string finger;
            public Transform metacarpal;
            public Transform proximal;
            public Transform intermediate;
            public Transform distal;
            public Transform tip;
        }

        public ArmSide side = ArmSide.Right;
        public ArmRig rig;
        public ArmDriver driver;

        [Tooltip("Retarget XR Hands joints when the hand is tracked.")]
        public bool retargetFromHandTracking = true;

        [Tooltip("Curl fingers from grip/trigger when driven by a controller.")]
        public bool curlFromController = true;

        [Tooltip("Maximum curl per joint in degrees at full grip.")]
        public float maxCurlDegrees = 80f;

        [Range(0f, 1f)] public float curlSmoothing = 0.3f;

        [Header("XR Hands joint frame convention (joint local space)")]
        public Vector3 jointForwardAxis = Vector3.forward;
        public Vector3 jointDorsalAxis = Vector3.up;

        [Header("Bones (auto-found by name)")]
        public FingerChain thumb = new FingerChain { finger = "thumb" };
        public FingerChain index = new FingerChain { finger = "index" };
        public FingerChain middle = new FingerChain { finger = "middle" };
        public FingerChain ring = new FingerChain { finger = "ring" };
        public FingerChain little = new FingerChain { finger = "little" };

        private struct BoneBinding
        {
            public Transform bone;
            public XRHandJointID joint;
            public Quaternion offset;      // bone.rotation(rest) relative to joint-like frame at rest
            public Quaternion restLocal;   // local rotation at rest (for controller curl)
            public Vector3 curlAxisLocal;  // local axis for flexion curl
            public float curlWeight;       // 0..1 share of curl applied to this joint
        }

        private readonly List<BoneBinding> bindings = new List<BoneBinding>();
        private bool initialised;
        private float smoothedGrip;
        private float smoothedTrigger;
        private Vector3 restDorsalLocalInHand;

        public bool IsRetargetingThisFrame { get; private set; }

        private void Awake()
        {
            if (rig == null) rig = GetComponentInChildren<ArmRig>();
            if (driver == null) driver = GetComponent<ArmDriver>();
            if (rig != null) side = rig.side;
        }

        private void Start()
        {
            ResolveBones();
            BuildBindings();
        }

        private static Transform Find(Transform root, string name)
        {
            string key = name.Replace(" ", string.Empty).ToLowerInvariant();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.Replace(" ", string.Empty).ToLowerInvariant() == key) return t;
            }
            return null;
        }

        /// <summary>Finds bones by the NatureManufacture naming scheme: {finger}_bone, _prox, _inter, _dist, _dist_end.</summary>
        public void ResolveBones()
        {
            Transform root = rig != null ? rig.transform : transform;
            foreach (FingerChain c in new[] { thumb, index, middle, ring, little })
            {
                string f = c.finger;
                if (c.metacarpal == null) c.metacarpal = Find(root, f + "_bone");
                if (c.proximal == null) c.proximal = Find(root, f + "_prox");
                if (c.intermediate == null) c.intermediate = Find(root, f + "_inter");
                if (c.distal == null) c.distal = Find(root, f + "_dist");
                if (c.tip == null) c.tip = Find(root, f + "_dist_end");
            }
        }

        private void BuildBindings()
        {
            bindings.Clear();
            if (rig == null || rig.handBone == null) return;
            Transform hand = rig.handBone;
            Quaternion handBasis = rig.CurrentHandBasis;
            Vector3 dorsalWorld = handBasis * Vector3.forward; // basis Z = dorsal
            restDorsalLocalInHand = Quaternion.Inverse(hand.rotation) * dorsalWorld;

            Bind(thumb.proximal, XRHandJointID.ThumbMetacarpal, thumb.intermediate, 0.5f);
            Bind(thumb.intermediate, XRHandJointID.ThumbProximal, thumb.distal, 0.8f);
            Bind(thumb.distal, XRHandJointID.ThumbDistal, thumb.tip, 0.8f);

            BindFinger(index, XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal);
            BindFinger(middle, XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal);
            BindFinger(ring, XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal);
            BindFinger(little, XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal);
            initialised = true;
        }

        private void BindFinger(FingerChain c, XRHandJointID meta, XRHandJointID prox, XRHandJointID inter, XRHandJointID dist)
        {
            // Metacarpals barely move; keep them on the model's rest pose.
            Bind(c.proximal, prox, c.intermediate, 1f);
            Bind(c.intermediate, inter, c.distal, 1f);
            Bind(c.distal, dist, c.tip, 0.7f);
        }

        private void Bind(Transform bone, XRHandJointID joint, Transform child, float curlWeight)
        {
            if (bone == null) return;
            Vector3 fwd = child != null ? (child.position - bone.position) : bone.up;
            if (fwd.sqrMagnitude < 1e-8f) fwd = bone.up;
            Vector3 dorsal = rig.handBone.rotation * restDorsalLocalInHand;
            Vector3 fwdN = fwd.normalized;
            Vector3 dorsalN = Vector3.ProjectOnPlane(dorsal, fwdN).normalized;
            if (dorsalN.sqrMagnitude < 1e-6f) dorsalN = Vector3.up;
            Quaternion jointLike = Quaternion.LookRotation(fwdN, dorsalN);
            Vector3 curlAxisWorld = Vector3.Cross(fwdN, dorsalN); // rotating fwd towards -dorsal (palmar) is flexion
            bindings.Add(new BoneBinding
            {
                bone = bone,
                joint = joint,
                offset = Quaternion.Inverse(jointLike) * bone.rotation,
                restLocal = bone.localRotation,
                curlAxisLocal = Quaternion.Inverse(bone.rotation) * (-curlAxisWorld),
                curlWeight = curlWeight
            });
        }

        private void LateUpdate()
        {
            if (!initialised || rig == null) return;
            XrTrackingService t = XrTrackingService.Instance;
            IsRetargetingThisFrame = false;

            bool useHand = retargetFromHandTracking && t != null && t.GetSample(side).handTracked
                           && driver != null && driver.ActiveSource == ArmInputSource.HandTracking;
            if (useHand)
            {
                RetargetFromJoints(t);
                return;
            }

            if (curlFromController && t != null && driver != null && driver.ActiveSource == ArmInputSource.Controller)
            {
                HandTrackingSample s = t.GetSample(side);
                ApplyCurl(s.grip, s.trigger);
                return;
            }
            ApplyCurl(0f, 0f);
        }

        private void RetargetFromJoints(XrTrackingService t)
        {
            Pose[] joints = t.GetJoints(side);
            bool[] valid = t.GetJointValidity(side);
            // 'conv' maps our joint-like frame (forward = along bone, up = dorsal) onto the provider's joint axes.
            Quaternion conv = Quaternion.LookRotation(jointForwardAxis, jointDorsalAxis);
            foreach (BoneBinding b in bindings)
            {
                int idx = b.joint.ToIndex();
                if (idx < 0 || idx >= valid.Length || !valid[idx]) continue;
                Quaternion jointRot = joints[idx].rotation * Quaternion.Inverse(conv);
                b.bone.rotation = jointRot * b.offset;
            }
            IsRetargetingThisFrame = true;
        }

        private void ApplyCurl(float grip, float trigger)
        {
            float k = curlSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-Time.deltaTime / Mathf.Lerp(0.01f, 0.2f, curlSmoothing));
            smoothedGrip = Mathf.Lerp(smoothedGrip, grip, k);
            smoothedTrigger = Mathf.Lerp(smoothedTrigger, trigger, k);
            foreach (BoneBinding b in bindings)
            {
                bool isIndex = b.bone == index.proximal || b.bone == index.intermediate || b.bone == index.distal;
                bool isThumb = b.bone == thumb.proximal || b.bone == thumb.intermediate || b.bone == thumb.distal;
                float amount = isIndex ? smoothedTrigger : isThumb ? Mathf.Max(smoothedGrip, smoothedTrigger) * 0.5f : smoothedGrip;
                float deg = amount * maxCurlDegrees * b.curlWeight;
                b.bone.localRotation = b.restLocal * Quaternion.AngleAxis(deg, b.curlAxisLocal);
            }
        }
    }
}
