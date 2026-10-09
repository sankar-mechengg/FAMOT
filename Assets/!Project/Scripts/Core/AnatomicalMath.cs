using UnityEngine;

namespace FAMOT.Core
{
    /// <summary>
    /// Small, allocation-free rotation helpers used by the kinematics code.
    /// Everything here is pure and covered by EditMode tests.
    /// </summary>
    public static class AnatomicalMath
    {
        /// <summary>Wraps an angle in degrees to the half-open interval (-180, 180].</summary>
        public static float WrapDegrees(float deg)
        {
            deg %= 360f;
            if (deg > 180f) deg -= 360f;
            else if (deg <= -180f) deg += 360f;
            return deg;
        }

        /// <summary>
        /// Twist component of <paramref name="q"/> about <paramref name="axis"/>, in signed degrees (-180, 180].
        /// Uses the swing–twist decomposition q = swing * twist, where twist is a rotation about the (unrotated) axis.
        /// </summary>
        public static float TwistDegrees(Quaternion q, Vector3 axis)
        {
            axis.Normalize();
            float proj = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float w = q.w;
            if (Mathf.Abs(proj) < 1e-9f && Mathf.Abs(w) < 1e-9f) return 0f;
            float angle = 2f * Mathf.Atan2(proj, w) * Mathf.Rad2Deg;
            return WrapDegrees(angle);
        }

        /// <summary>Splits q into swing and twist so that q = swing * twist, twist being about <paramref name="axis"/>.</summary>
        public static void SwingTwist(Quaternion q, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            axis.Normalize();
            float proj = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            twist = new Quaternion(axis.x * proj, axis.y * proj, axis.z * proj, q.w);
            float mag = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (mag < 1e-9f)
            {
                twist = Quaternion.identity;
            }
            else
            {
                twist = new Quaternion(twist.x / mag, twist.y / mag, twist.z / mag, twist.w / mag);
            }
            swing = q * Quaternion.Inverse(twist);
        }

        /// <summary>Signed angle (degrees) from <paramref name="from"/> to <paramref name="to"/> measured about <paramref name="axis"/>, both projected onto the plane normal to the axis.</summary>
        public static float SignedAngleAbout(Vector3 from, Vector3 to, Vector3 axis)
        {
            axis.Normalize();
            Vector3 f = Vector3.ProjectOnPlane(from, axis);
            Vector3 t = Vector3.ProjectOnPlane(to, axis);
            if (f.sqrMagnitude < 1e-12f || t.sqrMagnitude < 1e-12f) return 0f;
            return Vector3.SignedAngle(f, t, axis);
        }

        /// <summary>
        /// Builds a right-handed orthonormal basis quaternion whose local X, Y, Z axes map onto the given world
        /// directions. <paramref name="y"/> is taken as authoritative; <paramref name="x"/> is orthogonalised against it.
        /// </summary>
        public static Quaternion BasisFromXY(Vector3 x, Vector3 y)
        {
            y.Normalize();
            x = Vector3.ProjectOnPlane(x, y).normalized;
            Vector3 z = Vector3.Cross(x, y);
            // Quaternion.LookRotation builds a frame with forward = Z and up = Y.
            return Quaternion.LookRotation(z, y);
        }

        /// <summary>Rotation that is intrinsic Y, then X, then Z (matrix form Ry * Rx * Rz). Same convention as Quaternion.Euler.</summary>
        public static Quaternion EulerYXZ(float xDeg, float yDeg, float zDeg)
        {
            return Quaternion.AngleAxis(yDeg, Vector3.up) * Quaternion.AngleAxis(xDeg, Vector3.right) * Quaternion.AngleAxis(zDeg, Vector3.forward);
        }

        /// <summary>
        /// Extracts (x, y, z) such that q == Ry(y) * Rx(x) * Rz(z), all in signed degrees.
        /// x is limited to [-90, 90]; near the gimbal lock (|x| ~ 90) z is folded into y.
        /// </summary>
        public static Vector3 ToEulerYXZ(Quaternion q)
        {
            Matrix4x4 m = Matrix4x4.Rotate(q);
            float m12 = m[1, 2];
            float x = Mathf.Asin(Mathf.Clamp(-m12, -1f, 1f)) * Mathf.Rad2Deg;
            float y, z;
            if (Mathf.Abs(m12) < 0.999999f)
            {
                y = Mathf.Atan2(m[0, 2], m[2, 2]) * Mathf.Rad2Deg;
                z = Mathf.Atan2(m[1, 0], m[1, 1]) * Mathf.Rad2Deg;
            }
            else
            {
                // Gimbal lock: choose z = 0 and solve y from the remaining terms.
                y = Mathf.Atan2(-m[2, 0], m[0, 0]) * Mathf.Rad2Deg;
                z = 0f;
            }
            return new Vector3(WrapDegrees(x), WrapDegrees(y), WrapDegrees(z));
        }

        /// <summary>
        /// Conjugates a proper rotation by a reflection across the plane with unit normal <paramref name="mirrorNormal"/>:
        /// returns M * q * M. Needed when a bone lives under a negatively scaled (mirrored) parent, because Unity's
        /// Transform.rotation is composed from quaternions and ignores the mirror, while the rendered bone is mirrored.
        /// </summary>
        public static Quaternion MirrorConjugate(Quaternion q, Vector3 mirrorNormal)
        {
            mirrorNormal.Normalize();
            Vector3 v = new Vector3(q.x, q.y, q.z);
            Vector3 mirrored = v - 2f * Vector3.Dot(v, mirrorNormal) * mirrorNormal;
            return new Quaternion(-mirrored.x, -mirrored.y, -mirrored.z, q.w);
        }

        /// <summary>Direction of the humerus for the given shoulder angles, in a body frame (x lateral*sign, y up, z forward).</summary>
        public static Vector3 HumerusDirectionBody(float flexionDeg, float abductionDeg, float lateralSign)
        {
            float f = flexionDeg * Mathf.Deg2Rad;
            float a = abductionDeg * Mathf.Deg2Rad;
            float sa = Mathf.Sin(a) * lateralSign;
            float ca = Mathf.Cos(a);
            // d = Rx(-flex) * Rz(lateralSign * abd) * (0,-1,0)
            return new Vector3(sa, -ca * Mathf.Cos(f), ca * Mathf.Sin(f));
        }

        /// <summary>Inverse of <see cref="HumerusDirectionBody"/>. Returns (flexion, abduction) in degrees.</summary>
        public static Vector2 ShoulderAnglesFromDirectionBody(Vector3 dirBody, float lateralSign)
        {
            dirBody.Normalize();
            float abd = Mathf.Asin(Mathf.Clamp(dirBody.x * lateralSign, -1f, 1f)) * Mathf.Rad2Deg;
            float flex = Mathf.Atan2(dirBody.z, -dirBody.y) * Mathf.Rad2Deg;
            return new Vector2(WrapDegrees(flex), abd);
        }
    }
}
