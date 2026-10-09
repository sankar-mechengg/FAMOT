using System;
using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Avatar
{
    /// <summary>
    /// Result of the seated calibration pose for one arm: how the tracked device relates to the anatomical
    /// hand frame, where the shoulder sits relative to the head and how long the arm is.
    /// </summary>
    [Serializable]
    public class ArmCalibration
    {
        public ArmSide side;
        public bool isCalibrated;

        /// <summary>Rotation offset so that deviceRotation * handBasisOffset yields the anatomical hand basis (X flexion axis, Y to fingers, Z dorsal).</summary>
        public Quaternion handBasisOffset = Quaternion.identity;

        /// <summary>Wrist position relative to the device pose, expressed in the anatomical hand basis (metres). Y is along the fingers, so the wrist lies at negative Y for a controller held in the palm.</summary>
        public Vector3 wristOffsetInHandBasis = Vector3.zero;

        /// <summary>Shoulder position relative to the head, in the head's yaw-only frame (x lateral*side sign ... stored raw as right/up/forward).</summary>
        public Vector3 shoulderOffsetFromHead = new Vector3(0.17f, -0.22f, -0.06f);

        /// <summary>Shoulder-to-wrist distance measured in the calibration pose (metres).</summary>
        public float armLength = 0.6f;

        /// <summary>Uniform scale to apply to the arm model so its arm length matches the user's.</summary>
        public float modelScale = 1f;

        public string calibratedUtc = string.Empty;

        /// <summary>Which input produced the calibration (Controller / HandTracking).</summary>
        public string sourceUsed = string.Empty;

        public static ArmCalibration Default(ArmSide side)
        {
            var c = new ArmCalibration { side = side, isCalibrated = false };
            c.shoulderOffsetFromHead = new Vector3(0.17f * side.LateralSign(), -0.22f, -0.06f);
            return c;
        }

        public ArmCalibration Clone()
        {
            return (ArmCalibration)MemberwiseClone();
        }
    }
}
