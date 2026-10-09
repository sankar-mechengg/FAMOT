using System;
using FAMOT.Core;

namespace FAMOT.Session
{
    /// <summary>
    /// Per-subject range-of-motion limits. Serialized with <c>UnityEngine.JsonUtility</c> as part of
    /// <see cref="SubjectInfo"/>, so it is a flat class with public fields only.
    /// </summary>
    [Serializable]
    public class JointLimits
    {
        /// <summary>Default lower bound per <see cref="UpperLimbAngles"/> component, in degrees.</summary>
        public static readonly float[] DefaultMinAngles = { -60f, -30f, -90f, 0f, -90f, -80f, -30f };

        /// <summary>Default upper bound per <see cref="UpperLimbAngles"/> component, in degrees.</summary>
        public static readonly float[] DefaultMaxAngles = { 180f, 180f, 90f, 150f, 90f, 80f, 30f };

        /// <summary>Legacy wrist limit: maximum wrist flexion (negative), in degrees.</summary>
        public float maxFlexion = -70f;

        /// <summary>Legacy wrist limit: maximum wrist extension (positive), in degrees.</summary>
        public float maxExtension = 70f;

        /// <summary>Legacy forearm limit: maximum pronation (negative), in degrees.</summary>
        public float maxPronation = -20f;

        /// <summary>Legacy forearm limit: maximum supination (positive), in degrees.</summary>
        public float maxSupination = 90f;

        /// <summary>
        /// Lower bound per joint, indexed like <see cref="UpperLimbAngles"/> (see <see cref="UpperLimbAngles.Names"/>).
        /// </summary>
        public float[] minAngles = (float[])DefaultMinAngles.Clone();

        /// <summary>
        /// Upper bound per joint, indexed like <see cref="UpperLimbAngles"/> (see <see cref="UpperLimbAngles.Names"/>).
        /// </summary>
        public float[] maxAngles = (float[])DefaultMaxAngles.Clone();

        /// <summary>
        /// Repairs arrays that are missing or have the wrong length (e.g. after loading an old or hand-edited
        /// JSON file). Missing entries are filled from the defaults; min/max are swapped if inverted.
        /// </summary>
        public void EnsureValid()
        {
            minAngles = Repair(minAngles, DefaultMinAngles);
            maxAngles = Repair(maxAngles, DefaultMaxAngles);
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if (minAngles[i] > maxAngles[i])
                {
                    float t = minAngles[i];
                    minAngles[i] = maxAngles[i];
                    maxAngles[i] = t;
                }
            }
        }

        /// <summary>Lower bounds as an <see cref="UpperLimbAngles"/> value.</summary>
        public UpperLimbAngles Min()
        {
            EnsureValid();
            return UpperLimbAngles.FromArray(minAngles);
        }

        /// <summary>Upper bounds as an <see cref="UpperLimbAngles"/> value.</summary>
        public UpperLimbAngles Max()
        {
            EnsureValid();
            return UpperLimbAngles.FromArray(maxAngles);
        }

        /// <summary>Sets the per-joint bounds from two <see cref="UpperLimbAngles"/> values.</summary>
        public void SetRange(UpperLimbAngles min, UpperLimbAngles max)
        {
            EnsureValid();
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                minAngles[i] = Math.Min(min[i], max[i]);
                maxAngles[i] = Math.Max(min[i], max[i]);
            }
        }

        /// <summary>Returns a deep copy.</summary>
        public JointLimits Clone()
        {
            var c = (JointLimits)MemberwiseClone();
            c.minAngles = minAngles != null ? (float[])minAngles.Clone() : null;
            c.maxAngles = maxAngles != null ? (float[])maxAngles.Clone() : null;
            c.EnsureValid();
            return c;
        }

        private static float[] Repair(float[] values, float[] defaults)
        {
            if (values != null && values.Length == UpperLimbAngles.Count)
            {
                return values;
            }

            var r = (float[])defaults.Clone();
            if (values != null)
            {
                int n = Math.Min(values.Length, r.Length);
                for (int i = 0; i < n; i++)
                {
                    r[i] = values[i];
                }
            }
            return r;
        }
    }
}
