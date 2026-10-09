using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Kinematics
{
    /// <summary>
    /// World-space rotations and joint positions that fully describe one arm configuration.
    /// Produced by <see cref="ArmSolver"/> and consumed by <see cref="ArmRig"/>.
    /// </summary>
    public struct ArmPoseResult
    {
        public Vector3 shoulderPos;
        public Vector3 elbowPos;
        public Vector3 wristPos;
        public Quaternion upperArmRot;
        public Quaternion upperArmTwistRot;
        public Quaternion forearmRot;
        public Quaternion forearmTwistRot;
        public Quaternion handRot;
        /// <summary>Anatomical hand basis (X flexion axis, Y long axis, Z dorsal) in world space.</summary>
        public Quaternion handBasis;
        public Quaternion forearmBasis;
        /// <summary>Angles relative to the rest pose that produce this configuration.</summary>
        public UpperLimbAngles angles;
        /// <summary>True when the IK target was beyond reach and the wrist was clamped onto the reach sphere.</summary>
        public bool targetClamped;
    }

    /// <summary>
    /// Pure analytic solver for one upper limb (shoulder 3 DOF, elbow 1 DOF, forearm 1 DOF, wrist 2 DOF).
    ///
    /// Forward kinematics turns anatomical angles into bone rotations; inverse kinematics turns a wrist
    /// target (position + hand basis) into the same angle set and then runs forward kinematics, so what
    /// is rendered and what is logged always agree.
    /// </summary>
    public static class ArmSolver
    {
        /// <summary>Fraction of forearm pronation/supination applied at the elbow bone; the rest goes to the forearm twist bone.</summary>
        public static float elbowTwistShare = 0.35f;

        // ------------------------------------------------------------------------------------------------
        // Forward kinematics
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Forward kinematics. <paramref name="rest"/> must be finalized and already expressed at the
        /// current shoulder anchor. <paramref name="relative"/> are angles relative to the rest pose.
        /// </summary>
        public static ArmPoseResult Forward(ArmRestPose rest, UpperLimbAngles relative)
        {
            float ls = rest.LateralSign;

            // --- shoulder ---
            float flexAbs = rest.shoulderRestAngles.x + relative.shoulderFlexion;
            float abdAbs = rest.shoulderRestAngles.y + relative.shoulderAbduction;
            Vector3 humerusDir = rest.BodyToWorld(AnatomicalMath.HumerusDirectionBody(flexAbs, abdAbs, ls));
            Quaternion swing = Quaternion.FromToRotation(rest.humerusDir, humerusDir);
            float twistRaw = -ls * relative.shoulderRotation; // + external rotation
            Quaternion twist = Quaternion.AngleAxis(twistRaw, rest.humerusDir);
            Quaternion shoulderDelta = swing * twist;

            Quaternion upperArmRot = shoulderDelta * rest.upperArmRot;
            Vector3 elbowAxis = upperArmRot * rest.elbowAxisLocal;

            // --- elbow ---
            float elbowAbs = rest.elbowRestDeg + relative.elbowFlexion;
            Quaternion elbowBend = Quaternion.AngleAxis(elbowAbs - rest.elbowRestDeg, elbowAxis);
            Vector3 forearmDir = elbowBend * (shoulderDelta * rest.forearmDir);
            Quaternion basisNoTwist = elbowBend * shoulderDelta * rest.forearmBasis;

            // --- forearm pronation / supination ---
            float psRaw = -ls * relative.forearmSupination; // rotation about the long axis (basis Y)
            Quaternion psLocal = Quaternion.AngleAxis(psRaw, Vector3.up);
            Quaternion forearmBasis = basisNoTwist * psLocal;
            Quaternion forearmRot = basisNoTwist * Quaternion.AngleAxis(psRaw * elbowTwistShare, Vector3.up) * rest.forearmInBasis;
            Quaternion forearmTwistRot = forearmBasis * rest.forearmTwistInBasis;

            // --- wrist ---
            float rudRaw = -ls * relative.wristRadial;
            Quaternion wristLocal = Quaternion.AngleAxis(relative.wristExtension, Vector3.right) * Quaternion.AngleAxis(rudRaw, Vector3.forward);
            Quaternion handBasis = forearmBasis * wristLocal;
            Quaternion handRot = handBasis * rest.handInBasis;

            // --- positions ---
            Vector3 shoulderPos = rest.shoulderPos;
            Vector3 elbowPos = shoulderPos + humerusDir * rest.upperArmLength;
            Vector3 wristPos = elbowPos + forearmDir * rest.forearmLength;

            return new ArmPoseResult
            {
                shoulderPos = shoulderPos,
                elbowPos = elbowPos,
                wristPos = wristPos,
                upperArmRot = upperArmRot,
                upperArmTwistRot = upperArmRot * rest.upperArmTwistInUpperArm,
                forearmRot = forearmRot,
                forearmTwistRot = forearmTwistRot,
                handRot = handRot,
                handBasis = handBasis,
                forearmBasis = forearmBasis,
                angles = relative,
                targetClamped = false
            };
        }

        // ------------------------------------------------------------------------------------------------
        // Inverse kinematics
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Solves the arm so that the wrist reaches <paramref name="wristTarget"/> and the hand adopts
        /// <paramref name="handBasisTarget"/> (anatomical basis: X flexion axis, Y along the hand towards the
        /// fingers, Z dorsal normal). <paramref name="elbowHint"/> is a world point the elbow should lean
        /// towards. <paramref name="previousTwist"/> keeps the humeral rotation stable when the elbow is
        /// nearly straight and the plane normal is ill-defined.
        /// </summary>
        public static ArmPoseResult Inverse(ArmRestPose rest, Vector3 wristTarget, Quaternion handBasisTarget,
            Vector3 elbowHint, ref float previousTwist, UpperLimbAngles? jointLimitsMin = null, UpperLimbAngles? jointLimitsMax = null)
        {
            float ls = rest.LateralSign;
            float l1 = rest.upperArmLength;
            float l2 = rest.forearmLength;
            Vector3 shoulder = rest.shoulderPos;

            Vector3 toTarget = wristTarget - shoulder;
            float dist = toTarget.magnitude;
            bool clamped = false;
            float maxReach = (l1 + l2) * 0.999f;
            float minReach = Mathf.Abs(l1 - l2) * 1.001f + 1e-4f;
            if (dist > maxReach)
            {
                dist = maxReach;
                clamped = true;
            }
            else if (dist < minReach)
            {
                dist = minReach;
                clamped = true;
            }
            if (dist < 1e-5f) dist = 1e-5f;
            Vector3 axis = toTarget.sqrMagnitude > 1e-10f ? toTarget.normalized : rest.humerusDir;
            Vector3 wrist = shoulder + axis * dist;

            // Elbow placement on the circle of solutions.
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float r2 = l1 * l1 - a * a;
            float r = r2 > 0f ? Mathf.Sqrt(r2) : 0f;
            Vector3 hintDir = Vector3.ProjectOnPlane(elbowHint - shoulder, axis);
            if (hintDir.sqrMagnitude < 1e-8f)
            {
                hintDir = Vector3.ProjectOnPlane(-rest.bodyUp, axis);
                if (hintDir.sqrMagnitude < 1e-8f) hintDir = Vector3.ProjectOnPlane(rest.BodyLateral, axis);
            }
            hintDir.Normalize();
            Vector3 elbow = shoulder + axis * a + hintDir * r;

            Vector3 humerusDir = (elbow - shoulder).normalized;
            Vector3 forearmDir = (wrist - elbow).normalized;

            // --- shoulder flexion / abduction from the humerus direction ---
            Vector2 shAngles = AnatomicalMath.ShoulderAnglesFromDirectionBody(rest.WorldToBody(humerusDir), ls);
            Quaternion swing = Quaternion.FromToRotation(rest.humerusDir, humerusDir);

            // --- humeral rotation so that the elbow axis matches the arm-plane normal ---
            Vector3 planeNormal = Vector3.Cross(humerusDir, forearmDir);
            float twistRaw;
            if (planeNormal.magnitude > 0.08f)
            {
                Vector3 axisAfterSwing = swing * rest.elbowAxis;
                twistRaw = AnatomicalMath.SignedAngleAbout(axisAfterSwing, planeNormal.normalized, humerusDir);
                previousTwist = twistRaw;
            }
            else
            {
                twistRaw = previousTwist;
            }

            // --- elbow flexion ---
            Quaternion twist = Quaternion.AngleAxis(twistRaw, rest.humerusDir);
            Quaternion shoulderDelta = swing * twist;
            Quaternion upperArmRot = shoulderDelta * rest.upperArmRot;
            Vector3 elbowAxis = upperArmRot * rest.elbowAxisLocal;
            float elbowAbs = AnatomicalMath.SignedAngleAbout(humerusDir, forearmDir, elbowAxis);

            // --- forearm & wrist from the hand basis ---
            Quaternion elbowBend = Quaternion.AngleAxis(elbowAbs - rest.elbowRestDeg, elbowAxis);
            Quaternion basisNoTwist = elbowBend * shoulderDelta * rest.forearmBasis;
            Quaternion rel = Quaternion.Inverse(basisNoTwist) * handBasisTarget;
            Vector3 e = AnatomicalMath.ToEulerYXZ(rel); // x = extension, y = psRaw, z = rudRaw

            var angles = new UpperLimbAngles
            {
                shoulderFlexion = AnatomicalMath.WrapDegrees(shAngles.x - rest.shoulderRestAngles.x),
                shoulderAbduction = AnatomicalMath.WrapDegrees(shAngles.y - rest.shoulderRestAngles.y),
                shoulderRotation = -ls * twistRaw,
                elbowFlexion = AnatomicalMath.WrapDegrees(elbowAbs - rest.elbowRestDeg),
                forearmSupination = -ls * e.y,
                wristExtension = e.x,
                wristRadial = -ls * e.z
            };

            if (jointLimitsMin.HasValue && jointLimitsMax.HasValue)
            {
                angles = angles.Clamped(jointLimitsMin.Value, jointLimitsMax.Value);
            }

            var result = Forward(rest, angles);
            result.targetClamped = clamped;
            return result;
        }

        /// <summary>
        /// Decomposes an arbitrary set of bone rotations (e.g. after animation) into anatomical angles.
        /// Only the upper arm, forearm and hand rotations are needed.
        /// </summary>
        public static UpperLimbAngles Decompose(ArmRestPose rest, Quaternion upperArmRot, Quaternion forearmRot, Quaternion handRot)
        {
            float ls = rest.LateralSign;
            Quaternion shoulderDelta = upperArmRot * Quaternion.Inverse(rest.upperArmRot);
            Vector3 humerusDir = shoulderDelta * rest.humerusDir;
            Vector2 shAngles = AnatomicalMath.ShoulderAnglesFromDirectionBody(rest.WorldToBody(humerusDir), ls);
            Quaternion swing = Quaternion.FromToRotation(rest.humerusDir, humerusDir);
            Quaternion twistQ = Quaternion.Inverse(swing) * shoulderDelta;
            float twistRaw = AnatomicalMath.TwistDegrees(twistQ, rest.humerusDir);

            Vector3 elbowAxis = upperArmRot * rest.elbowAxisLocal;
            Vector3 forearmDir = forearmRot * Quaternion.Inverse(rest.forearmRot) * rest.forearmDir;
            float elbowAbs = AnatomicalMath.SignedAngleAbout(humerusDir, forearmDir, elbowAxis);

            Quaternion elbowBend = Quaternion.AngleAxis(elbowAbs - rest.elbowRestDeg, elbowAxis);
            Quaternion basisNoTwist = elbowBend * shoulderDelta * rest.forearmBasis;
            Quaternion handBasis = handRot * Quaternion.Inverse(rest.handInBasis);
            Vector3 e = AnatomicalMath.ToEulerYXZ(Quaternion.Inverse(basisNoTwist) * handBasis);

            return new UpperLimbAngles
            {
                shoulderFlexion = AnatomicalMath.WrapDegrees(shAngles.x - rest.shoulderRestAngles.x),
                shoulderAbduction = AnatomicalMath.WrapDegrees(shAngles.y - rest.shoulderRestAngles.y),
                shoulderRotation = -ls * twistRaw,
                elbowFlexion = AnatomicalMath.WrapDegrees(elbowAbs - rest.elbowRestDeg),
                forearmSupination = -ls * e.y,
                wristExtension = e.x,
                wristRadial = -ls * e.z
            };
        }

        /// <summary>
        /// Default elbow hint used in VR: below and slightly outside the shoulder-wrist line, rotated by part
        /// of the hand's roll so that turning the palm up swings the elbow inwards, as a real arm does.
        /// </summary>
        public static Vector3 DefaultElbowHint(ArmRestPose rest, Vector3 wristTarget, Quaternion handBasisTarget, float rollInfluence = 0.5f)
        {
            Vector3 shoulder = rest.shoulderPos;
            Vector3 axis = (wristTarget - shoulder).normalized;
            if (axis.sqrMagnitude < 1e-8f) axis = rest.humerusDir;
            Vector3 baseDir = (-rest.bodyUp * 1.0f + rest.BodyLateral * 0.45f - rest.bodyForward * 0.25f).normalized;
            Vector3 hint = Vector3.ProjectOnPlane(baseDir, axis);
            if (hint.sqrMagnitude < 1e-6f) hint = Vector3.ProjectOnPlane(rest.BodyLateral, axis);
            hint.Normalize();

            // Hand roll about the shoulder-wrist axis relative to the "dorsal up" neutral.
            Vector3 dorsal = handBasisTarget * Vector3.forward;
            Vector3 neutralDorsal = Vector3.ProjectOnPlane(rest.bodyUp, axis);
            float roll = 0f;
            if (neutralDorsal.sqrMagnitude > 1e-6f)
            {
                roll = AnatomicalMath.SignedAngleAbout(neutralDorsal.normalized, dorsal, axis);
            }
            hint = Quaternion.AngleAxis(roll * rollInfluence, axis) * hint;
            return shoulder + hint * rest.upperArmLength;
        }
    }
}
