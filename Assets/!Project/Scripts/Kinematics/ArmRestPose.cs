using System;
using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Kinematics
{
    /// <summary>
    /// Everything the solver needs to know about an arm in its neutral (rest) configuration.
    /// Captured once from the bone transforms (see <see cref="ArmRig"/>) and then used as pure data,
    /// so the solver can be unit tested without a scene.
    ///
    /// Assumed rest pose: forearm resting roughly horizontal, palm down, fingers pointing along
    /// the body frame's forward axis. The dorsal (back of hand) normal then points along body up.
    /// </summary>
    [Serializable]
    public class ArmRestPose
    {
        public ArmSide side;

        /// <summary>Body frame axes (world) at capture time.</summary>
        public Vector3 bodyForward = Vector3.forward;
        public Vector3 bodyUp = Vector3.up;
        public Vector3 bodyRight = Vector3.right;

        /// <summary>World positions of the joint centres at rest.</summary>
        public Vector3 shoulderPos;
        public Vector3 elbowPos;
        public Vector3 wristPos;
        public Vector3 handTipPos;

        /// <summary>World rotations of the driven bones at rest.</summary>
        public Quaternion upperArmRot = Quaternion.identity;
        public Quaternion upperArmTwistRot = Quaternion.identity;
        public Quaternion forearmRot = Quaternion.identity;
        public Quaternion forearmTwistRot = Quaternion.identity;
        public Quaternion handRot = Quaternion.identity;

        // ---- derived quantities (filled by Finalize) ----
        [NonSerialized] public Vector3 humerusDir;
        [NonSerialized] public Vector3 forearmDir;
        [NonSerialized] public Vector3 elbowAxis;
        [NonSerialized] public float elbowRestDeg;
        [NonSerialized] public float upperArmLength;
        [NonSerialized] public float forearmLength;
        [NonSerialized] public float handLength;
        /// <summary>Anatomical basis of the forearm/hand at rest: X = flexion axis, Y = long axis, Z = dorsal normal.</summary>
        [NonSerialized] public Quaternion forearmBasis = Quaternion.identity;
        /// <summary>Bone rotations expressed in the forearm basis / parent bone frames.</summary>
        [NonSerialized] public Quaternion handInBasis = Quaternion.identity;
        [NonSerialized] public Quaternion forearmInBasis = Quaternion.identity;
        [NonSerialized] public Quaternion forearmTwistInBasis = Quaternion.identity;
        [NonSerialized] public Quaternion upperArmTwistInUpperArm = Quaternion.identity;
        /// <summary>Elbow axis expressed in the upper-arm bone's local frame.</summary>
        [NonSerialized] public Vector3 elbowAxisLocal;
        [NonSerialized] public Vector2 shoulderRestAngles;
        [NonSerialized] public bool isFinalized;

        public float LateralSign => side.LateralSign();
        public Vector3 BodyLateral => bodyRight * LateralSign;
        public float ArmLength => upperArmLength + forearmLength;

        /// <summary>Computes the derived quantities. Must be called after the public fields are set.</summary>
        public void Finalize()
        {
            bodyForward.Normalize();
            bodyUp = Vector3.ProjectOnPlane(bodyUp, bodyForward).normalized;
            bodyRight = Vector3.Cross(bodyUp, bodyForward).normalized;

            humerusDir = (elbowPos - shoulderPos).normalized;
            forearmDir = (wristPos - elbowPos).normalized;
            upperArmLength = Vector3.Distance(shoulderPos, elbowPos);
            forearmLength = Vector3.Distance(elbowPos, wristPos);
            handLength = Vector3.Distance(wristPos, handTipPos);

            // Elbow flexion axis: normal of the plane containing both segments at rest. If the rest pose is
            // nearly straight the plane is undefined, so fall back to an axis that makes positive flexion lift
            // the hand towards the dorsal side (upwards for a forward-pointing arm).
            Vector3 n = Vector3.Cross(humerusDir, forearmDir);
            float restAngle = Vector3.Angle(humerusDir, forearmDir);
            if (n.magnitude > 0.05f && restAngle > 3f)
            {
                elbowAxis = n.normalized;
            }
            else
            {
                elbowAxis = Vector3.Cross(humerusDir, bodyUp);
                if (elbowAxis.sqrMagnitude < 1e-6f) elbowAxis = -BodyLateral;
                elbowAxis.Normalize();
            }
            elbowRestDeg = AnatomicalMath.SignedAngleAbout(humerusDir, forearmDir, elbowAxis);
            elbowAxisLocal = Quaternion.Inverse(upperArmRot) * elbowAxis;

            // Forearm / hand anatomical basis. X: flexion axis chosen so that +rotation = wrist extension
            // (fingers lift toward the dorsal side). With the long axis Y pointing to the fingers and the
            // dorsal normal Z, X = Y x Z gives a right-handed frame (X, Y, Z).
            Vector3 longAxis = forearmDir;
            Vector3 dorsal = Vector3.ProjectOnPlane(bodyUp, longAxis).normalized;
            if (dorsal.sqrMagnitude < 1e-6f) dorsal = Vector3.ProjectOnPlane(-bodyForward, longAxis).normalized;
            Vector3 flexAxis = Vector3.Cross(longAxis, dorsal).normalized;
            forearmBasis = AnatomicalMath.BasisFromXY(flexAxis, longAxis);

            Quaternion invB = Quaternion.Inverse(forearmBasis);
            handInBasis = invB * handRot;
            forearmInBasis = invB * forearmRot;
            forearmTwistInBasis = invB * forearmTwistRot;
            upperArmTwistInUpperArm = Quaternion.Inverse(upperArmRot) * upperArmTwistRot;

            shoulderRestAngles = AnatomicalMath.ShoulderAnglesFromDirectionBody(WorldToBody(humerusDir), LateralSign);
            isFinalized = true;
        }

        public Vector3 WorldToBody(Vector3 v) => new Vector3(Vector3.Dot(v, bodyRight), Vector3.Dot(v, bodyUp), Vector3.Dot(v, bodyForward));
        public Vector3 BodyToWorld(Vector3 v) => bodyRight * v.x + bodyUp * v.y + bodyForward * v.z;

        /// <summary>
        /// Returns a copy of this rest pose after a rigid transform (rotate about the origin, then translate)
        /// has been applied to every world quantity, optionally with a uniform scale about the shoulder.
        /// Used when the arm root (shoulder anchor) moves, e.g. following the headset.
        /// </summary>
        public ArmRestPose Transformed(Vector3 translation, Quaternion rotation, float scale = 1f)
        {
            Vector3 S(Vector3 p) => rotation * (shoulderPos + (p - shoulderPos) * scale) + translation;
            var r = new ArmRestPose
            {
                side = side,
                bodyForward = rotation * bodyForward,
                bodyUp = rotation * bodyUp,
                bodyRight = rotation * bodyRight,
                shoulderPos = S(shoulderPos),
                elbowPos = S(elbowPos),
                wristPos = S(wristPos),
                handTipPos = S(handTipPos),
                upperArmRot = rotation * upperArmRot,
                upperArmTwistRot = rotation * upperArmTwistRot,
                forearmRot = rotation * forearmRot,
                forearmTwistRot = rotation * forearmTwistRot,
                handRot = rotation * handRot
            };
            r.Finalize();
            return r;
        }
    }
}
