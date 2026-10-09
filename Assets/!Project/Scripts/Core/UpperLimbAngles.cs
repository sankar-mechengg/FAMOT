using System;
using UnityEngine;

namespace FAMOT.Core
{
    /// <summary>
    /// Seven anatomical degrees of freedom of one upper limb, in signed degrees.
    /// Zero means "same as the calibrated rest pose" for every component.
    ///
    /// Sign conventions (independent of side):
    ///   shoulderFlexion   + forward elevation, - extension
    ///   shoulderAbduction + away from the trunk (lateral), - adduction
    ///   shoulderRotation  + external (lateral) rotation, - internal
    ///   elbowFlexion      + flexion
    ///   forearmSupination + supination (palm up), - pronation
    ///   wristExtension    + extension (dorsiflexion), - flexion
    ///   wristRadial       + radial deviation (towards thumb), - ulnar
    /// </summary>
    [Serializable]
    public struct UpperLimbAngles
    {
        public const int Count = 7;

        public float shoulderFlexion;
        public float shoulderAbduction;
        public float shoulderRotation;
        public float elbowFlexion;
        public float forearmSupination;
        public float wristExtension;
        public float wristRadial;

        public static readonly string[] Names =
        {
            "shoulder_flexion", "shoulder_abduction", "shoulder_rotation",
            "elbow_flexion", "forearm_supination", "wrist_extension", "wrist_radial"
        };

        public static readonly string[] ShortNames = { "ShF", "ShA", "ShR", "ElF", "FaS", "WrE", "WrR" };

        public static UpperLimbAngles Zero => default;

        public float this[int i]
        {
            get
            {
                switch (i)
                {
                    case 0: return shoulderFlexion;
                    case 1: return shoulderAbduction;
                    case 2: return shoulderRotation;
                    case 3: return elbowFlexion;
                    case 4: return forearmSupination;
                    case 5: return wristExtension;
                    case 6: return wristRadial;
                    default: throw new IndexOutOfRangeException();
                }
            }
            set
            {
                switch (i)
                {
                    case 0: shoulderFlexion = value; break;
                    case 1: shoulderAbduction = value; break;
                    case 2: shoulderRotation = value; break;
                    case 3: elbowFlexion = value; break;
                    case 4: forearmSupination = value; break;
                    case 5: wristExtension = value; break;
                    case 6: wristRadial = value; break;
                    default: throw new IndexOutOfRangeException();
                }
            }
        }

        public float[] ToArray()
        {
            var a = new float[Count];
            for (int i = 0; i < Count; i++) a[i] = this[i];
            return a;
        }

        public static UpperLimbAngles FromArray(float[] values, int offset = 0)
        {
            var r = new UpperLimbAngles();
            for (int i = 0; i < Count; i++) r[i] = values[offset + i];
            return r;
        }

        public static UpperLimbAngles operator +(UpperLimbAngles a, UpperLimbAngles b)
        {
            var r = new UpperLimbAngles();
            for (int i = 0; i < Count; i++) r[i] = a[i] + b[i];
            return r;
        }

        public static UpperLimbAngles operator -(UpperLimbAngles a, UpperLimbAngles b)
        {
            var r = new UpperLimbAngles();
            for (int i = 0; i < Count; i++) r[i] = AnatomicalMath.WrapDegrees(a[i] - b[i]);
            return r;
        }

        public static UpperLimbAngles Lerp(UpperLimbAngles a, UpperLimbAngles b, float t)
        {
            var r = new UpperLimbAngles();
            for (int i = 0; i < Count; i++) r[i] = Mathf.LerpAngle(a[i], b[i], t);
            return r;
        }

        /// <summary>Largest absolute per-joint difference, in degrees.</summary>
        public static float MaxAbsDifference(UpperLimbAngles a, UpperLimbAngles b)
        {
            float m = 0f;
            for (int i = 0; i < Count; i++) m = Mathf.Max(m, Mathf.Abs(AnatomicalMath.WrapDegrees(a[i] - b[i])));
            return m;
        }

        /// <summary>Root-mean-square difference across the seven joints, in degrees.</summary>
        public static float RmsDifference(UpperLimbAngles a, UpperLimbAngles b)
        {
            float s = 0f;
            for (int i = 0; i < Count; i++)
            {
                float d = AnatomicalMath.WrapDegrees(a[i] - b[i]);
                s += d * d;
            }
            return Mathf.Sqrt(s / Count);
        }

        public UpperLimbAngles Clamped(UpperLimbAngles min, UpperLimbAngles max)
        {
            var r = new UpperLimbAngles();
            for (int i = 0; i < Count; i++) r[i] = Mathf.Clamp(this[i], min[i], max[i]);
            return r;
        }

        public override string ToString()
        {
            return $"ShF {shoulderFlexion:F1} ShA {shoulderAbduction:F1} ShR {shoulderRotation:F1} " +
                   $"ElF {elbowFlexion:F1} FaS {forearmSupination:F1} WrE {wristExtension:F1} WrR {wristRadial:F1}";
        }
    }
}
