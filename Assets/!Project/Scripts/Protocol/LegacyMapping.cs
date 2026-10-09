using UnityEngine;

namespace FAMOT.Protocol
{
    /// <summary>
    /// Mapping between the legacy 0..100 scale of <c>T</c> messages and joint angles in degrees.
    /// 50 is always the rest position (0 degrees), independent of asymmetric limits.
    /// </summary>
    public static class LegacyMapping
    {
        /// <summary>
        /// Piecewise linear map. Input 0..50 maps to <paramref name="outputMin"/>..0,
        /// input 50..100 maps to 0..<paramref name="outputMax"/>. Input outside 0..100 is clamped.
        /// This guarantees input 50 always maps to 0 degrees (rest).
        /// </summary>
        /// <param name="input0to100">Legacy value, 0..100 (50 = rest).</param>
        /// <param name="outputMin">Angle at input 0 (e.g. maximum flexion, negative).</param>
        /// <param name="outputMax">Angle at input 100 (e.g. maximum extension, positive).</param>
        public static float PiecewiseMap(float input0to100, float outputMin, float outputMax)
        {
            if (input0to100 <= 50f)
                return Mathf.Lerp(outputMin, 0f, input0to100 / 50f);
            else
                return Mathf.Lerp(0f, outputMax, (input0to100 - 50f) / 50f);
        }

        /// <summary>
        /// Inverse of <see cref="PiecewiseMap"/>: angle in degrees to the legacy 0..100 scale.
        /// Angles beyond the limits are clamped to 0 or 100. If a limit is degenerate
        /// (outputMin &gt;= 0 or outputMax &lt;= 0) the corresponding half maps to 50.
        /// </summary>
        /// <param name="angle">Angle in degrees relative to rest.</param>
        /// <param name="outputMin">Angle at input 0 (negative).</param>
        /// <param name="outputMax">Angle at input 100 (positive).</param>
        public static float InversePiecewiseMap(float angle, float outputMin, float outputMax)
        {
            float result;
            if (angle <= 0f)
            {
                if (outputMin >= 0f)
                    return 50f;
                result = 50f * (1f - angle / outputMin);
            }
            else
            {
                if (outputMax <= 0f)
                    return 50f;
                result = 50f + 50f * (angle / outputMax);
            }
            return Mathf.Clamp(result, 0f, 100f);
        }
    }
}
