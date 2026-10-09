using System;
using System.Collections.Generic;
using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Kinematics
{
    /// <summary>
    /// Binds one arm model (shoulder → upper arm → forearm → hand bones) to the analytic <see cref="ArmSolver"/>.
    ///
    /// The rest pose is captured once in the local space of <see cref="anchor"/>, the shoulder anchor, so that
    /// moving or scaling the anchor (e.g. following the headset) moves the whole kinematic definition with it.
    /// The anchor's axes define the body frame: forward = where the fingers point at rest, up = cranial.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    public class ArmRig : MonoBehaviour
    {
        [Header("Identity")]
        public ArmSide side = ArmSide.Right;

        [Tooltip("Shoulder anchor whose axes form the body frame. Defaults to the parent transform.")]
        public Transform anchor;

        [Header("Bones (auto-found by name when empty)")]
        public Transform upperArmBone;
        public Transform upperArmTwistBone;
        public Transform forearmBone;
        public Transform forearmTwistBone;
        public Transform handBone;
        [Tooltip("Reference point used to measure hand length (e.g. middle finger base).")]
        public Transform handTipBone;

        [Header("Bone name hints")]
        public string[] upperArmNames = { "arm_up_deform", "upperarm", "upper_arm" };
        public string[] upperArmTwistNames = { "arm_down_deform" };
        public string[] forearmNames = { "forearm_up_deform", "forearm" };
        public string[] forearmTwistNames = { "forearm_ down_deform", "forearm_down_deform" };
        public string[] handNames = { "hand_joint", "hand", "wrist" };
        public string[] handTipNames = { "middle_prox", "middle_bone" };

        [Header("Behaviour")]
        [Tooltip("Capture the rest pose automatically in Awake. Disable if another component sets the pose first.")]
        public bool captureRestOnAwake = true;

        [Tooltip("Keep the solved pose applied every LateUpdate (prevents Animator/other scripts from overriding).")]
        public bool reapplyInLateUpdate = true;

        [Tooltip("Draw joints and axes in the Scene view.")]
        public bool drawGizmos = true;

        // Rest pose expressed in anchor-local space.
        [SerializeField, HideInInspector] private ArmRestPose restLocal;
        private bool hasRest;
        private float previousTwist;
        private ArmPoseResult currentPose;
        private UpperLimbAngles currentAngles;
        private bool hasPose;
        private bool isMirrored;
        private Transform mirrorTransform;
        private Vector3 mirrorLocalAxis = Vector3.right;

        /// <summary>Most recent solved pose (world space).</summary>
        public ArmPoseResult CurrentPose => currentPose;

        /// <summary>Most recent anatomical angles relative to the rest pose.</summary>
        public UpperLimbAngles CurrentAngles => currentAngles;

        /// <summary>True when the bones live under a negatively scaled parent (mirrored mesh, e.g. the left arm prefab).</summary>
        public bool IsMirrored => isMirrored;

        public bool HasRestPose => hasRest;
        public bool HasPose => hasPose;

        /// <summary>Rest pose in anchor-local space (unit scale).</summary>
        public ArmRestPose RestLocal => restLocal;

        /// <summary>Length of the model's arm (shoulder to wrist) at unit scale, in metres.</summary>
        public float ModelArmLength => hasRest ? restLocal.ArmLength : 0f;

        public Transform Anchor => anchor != null ? anchor : transform.parent != null ? transform.parent : transform;

        private void Awake()
        {
            if (anchor == null) anchor = transform.parent != null ? transform.parent : transform;
            ResolveBones();
            if (captureRestOnAwake) CaptureRestPose();
        }

        private void LateUpdate()
        {
            if (reapplyInLateUpdate && hasPose) ApplyPose(currentPose);
        }

        /// <summary>Finds the bones by name below this transform when they are not assigned.</summary>
        public void ResolveBones()
        {
            if (upperArmBone == null) upperArmBone = FindBone(upperArmNames);
            if (upperArmTwistBone == null) upperArmTwistBone = FindBone(upperArmTwistNames);
            if (forearmBone == null) forearmBone = FindBone(forearmNames);
            if (forearmTwistBone == null) forearmTwistBone = FindBone(forearmTwistNames);
            if (handBone == null) handBone = FindBone(handNames);
            if (handTipBone == null) handTipBone = FindBone(handTipNames);
            DetectMirror();
        }

        /// <summary>
        /// Detects a negatively scaled ancestor between the hand bone and the anchor (mirrored model, e.g. the
        /// left arm prefab). Informational only: world-space rotation deltas behave correctly either way.
        /// </summary>
        private void DetectMirror()
        {
            isMirrored = false;
            mirrorTransform = null;
            if (handBone == null) return;
            Transform stop = Anchor;
            for (Transform t = handBone; t != null && t != stop.parent; t = t.parent)
            {
                Vector3 ls = t.localScale;
                if (ls.x < 0f || ls.y < 0f || ls.z < 0f)
                {
                    isMirrored = !isMirrored;
                    mirrorTransform = t;
                    mirrorLocalAxis = ls.x < 0f ? Vector3.right : ls.y < 0f ? Vector3.up : Vector3.forward;
                }
                if (t == stop) break;
            }
            if (!isMirrored && handBone.localToWorldMatrix.determinant < 0f)
            {
                isMirrored = true;
                mirrorTransform = Anchor;
                mirrorLocalAxis = Vector3.right;
            }
        }

        private Transform FindBone(string[] names)
        {
            var all = GetComponentsInChildren<Transform>(true);
            foreach (string n in names)
            {
                string key = Normalize(n);
                foreach (Transform t in all)
                {
                    if (Normalize(t.name) == key) return t;
                }
            }
            return null;
        }

        private static string Normalize(string s) => s.Replace(" ", string.Empty).Trim().ToLowerInvariant();

        /// <summary>True when all mandatory bones are bound.</summary>
        public bool IsBound => upperArmBone != null && forearmBone != null && handBone != null;

        /// <summary>
        /// Records the current bone configuration as the rest pose. Call with the model in its neutral pose
        /// (palm down, fingers pointing along the anchor's forward axis).
        /// </summary>
        public void CaptureRestPose()
        {
            if (!IsBound)
            {
                Debug.LogError($"[ArmRig:{name}] bones not bound; cannot capture rest pose.", this);
                return;
            }
            Transform a = Anchor;
            Quaternion invA = Quaternion.Inverse(a.rotation);
            Vector3 tip = handTipBone != null ? handTipBone.position : handBone.position + (handBone.position - forearmBone.position).normalized * 0.08f;

            restLocal = new ArmRestPose
            {
                side = side,
                bodyForward = Vector3.forward,
                bodyUp = Vector3.up,
                bodyRight = Vector3.right,
                shoulderPos = a.InverseTransformPoint(upperArmBone.position),
                elbowPos = a.InverseTransformPoint(forearmBone.position),
                wristPos = a.InverseTransformPoint(handBone.position),
                handTipPos = a.InverseTransformPoint(tip),
                upperArmRot = invA * upperArmBone.rotation,
                upperArmTwistRot = invA * (upperArmTwistBone != null ? upperArmTwistBone.rotation : upperArmBone.rotation),
                forearmRot = invA * forearmBone.rotation,
                forearmTwistRot = invA * (forearmTwistBone != null ? forearmTwistBone.rotation : forearmBone.rotation),
                handRot = invA * handBone.rotation
            };
            restLocal.Finalize();
            hasRest = true;
            previousTwist = 0f;
            currentAngles = UpperLimbAngles.Zero;
            currentPose = ArmSolver.Forward(BuildWorldRest(), currentAngles);
            hasPose = true;

            float fwdDot = Vector3.Dot(restLocal.forearmDir, Vector3.forward);
            if (fwdDot < 0.5f)
            {
                Debug.LogWarning($"[ArmRig:{name}] rest forearm direction deviates from the anchor forward axis (dot={fwdDot:F2}). Check the anchor orientation.", this);
            }
        }

        /// <summary>Rest pose re-expressed in world space at the anchor's current position, rotation and scale.</summary>
        public ArmRestPose BuildWorldRest()
        {
            Transform a = Anchor;
            Quaternion r = a.rotation;
            var w = new ArmRestPose
            {
                side = side,
                bodyForward = r * Vector3.forward,
                bodyUp = r * Vector3.up,
                bodyRight = r * Vector3.right,
                shoulderPos = a.TransformPoint(restLocal.shoulderPos),
                elbowPos = a.TransformPoint(restLocal.elbowPos),
                wristPos = a.TransformPoint(restLocal.wristPos),
                handTipPos = a.TransformPoint(restLocal.handTipPos),
                upperArmRot = r * restLocal.upperArmRot,
                upperArmTwistRot = r * restLocal.upperArmTwistRot,
                forearmRot = r * restLocal.forearmRot,
                forearmTwistRot = r * restLocal.forearmTwistRot,
                handRot = r * restLocal.handRot
            };
            w.Finalize();
            return w;
        }

        /// <summary>Forward kinematics: poses the arm from angles relative to rest.</summary>
        public void ApplyAngles(UpperLimbAngles relative)
        {
            if (!hasRest) return;
            currentPose = ArmSolver.Forward(BuildWorldRest(), relative);
            currentAngles = relative;
            hasPose = true;
            ApplyPose(currentPose);
        }

        /// <summary>
        /// Inverse kinematics: reaches <paramref name="wristWorld"/> with the hand oriented by
        /// <paramref name="handBasisWorld"/> (X flexion axis, Y towards the fingers, Z dorsal).
        /// </summary>
        public ArmPoseResult SolveIK(Vector3 wristWorld, Quaternion handBasisWorld, Vector3? elbowHintWorld = null,
            UpperLimbAngles? limitsMin = null, UpperLimbAngles? limitsMax = null, float elbowRollInfluence = 0.5f)
        {
            if (!hasRest) return default;
            ArmRestPose rest = BuildWorldRest();
            Vector3 hint = elbowHintWorld ?? ArmSolver.DefaultElbowHint(rest, wristWorld, handBasisWorld, elbowRollInfluence);
            currentPose = ArmSolver.Inverse(rest, wristWorld, handBasisWorld, hint, ref previousTwist, limitsMin, limitsMax);
            currentAngles = currentPose.angles;
            hasPose = true;
            ApplyPose(currentPose);
            return currentPose;
        }

        /// <summary>Writes world rotations to the bones. Positions follow from the hierarchy.</summary>
        public void ApplyPose(in ArmPoseResult pose)
        {
            if (!IsBound) return;
            // World-space deltas apply correctly even under a mirrored (negative-scale) parent: Unity's rotation
            // setter keeps delta * rotation consistent with the rendered bone, so no conjugation is needed.
            upperArmBone.rotation = pose.upperArmRot;
            if (upperArmTwistBone != null) upperArmTwistBone.rotation = pose.upperArmTwistRot;
            forearmBone.rotation = pose.forearmRot;
            if (forearmTwistBone != null) forearmTwistBone.rotation = pose.forearmTwistRot;
            handBone.rotation = pose.handRot;
        }

        /// <summary>Returns the arm to its rest pose.</summary>
        public void ResetToRest()
        {
            if (!hasRest) return;
            ApplyAngles(UpperLimbAngles.Zero);
        }

        /// <summary>Re-derives the anatomical angles from the bones' current rotations (e.g. after an animation).</summary>
        public UpperLimbAngles DecomposeCurrentBones()
        {
            if (!hasRest) return UpperLimbAngles.Zero;
            return ArmSolver.Decompose(BuildWorldRest(), upperArmBone.rotation, forearmBone.rotation, handBone.rotation);
        }

        /// <summary>World positions of shoulder, elbow, wrist as currently posed by the bones.</summary>
        public void GetJointPositions(out Vector3 shoulder, out Vector3 elbow, out Vector3 wrist)
        {
            shoulder = upperArmBone != null ? upperArmBone.position : transform.position;
            elbow = forearmBone != null ? forearmBone.position : shoulder;
            wrist = handBone != null ? handBone.position : elbow;
        }

        /// <summary>World-space anatomical hand basis of the current pose (identity if no pose).</summary>
        public Quaternion CurrentHandBasis => hasPose ? currentPose.handBasis : Quaternion.identity;

        private void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !IsBound) return;
            GetJointPositions(out var s, out var e, out var w);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(s, e);
            Gizmos.DrawLine(e, w);
            Gizmos.DrawSphere(s, 0.015f);
            Gizmos.DrawSphere(e, 0.012f);
            Gizmos.DrawSphere(w, 0.01f);
            if (hasPose)
            {
                Quaternion b = currentPose.handBasis;
                Gizmos.color = Color.red;
                Gizmos.DrawLine(w, w + b * Vector3.right * 0.05f);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(w, w + b * Vector3.up * 0.08f);
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(w, w + b * Vector3.forward * 0.05f);
            }
            Transform a = Anchor;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a.position, a.position + a.forward * 0.1f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(a.position, a.position + a.up * 0.1f);
        }
    }
}
