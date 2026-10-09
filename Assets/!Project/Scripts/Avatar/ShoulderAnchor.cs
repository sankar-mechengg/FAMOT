using FAMOT.Core;
using FAMOT.Input;
using UnityEngine;

namespace FAMOT.Avatar
{
    /// <summary>
    /// Places the shoulder anchor of one arm. In VR the anchor follows the headset with a yaw-only rotation
    /// and a calibrated offset, so the virtual shoulder stays attached to the user's torso. In desktop mode
    /// the anchor is simply left where the scene author placed it.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class ShoulderAnchor : MonoBehaviour
    {
        public ArmSide side = ArmSide.Right;

        [Tooltip("Follow the headset. Leave off for desktop scenes.")]
        public bool followHead;

        [Tooltip("Offset from the head centre in the head's yaw-only frame: x right, y up, z forward (metres).")]
        public Vector3 offsetFromHead = new Vector3(0.17f, -0.22f, -0.06f);

        [Tooltip("Blend factor per second for position; 0 = snap.")]
        public float positionSmoothing = 12f;

        [Tooltip("Blend factor per second for yaw; 0 = snap.")]
        public float yawSmoothing = 6f;

        [Tooltip("Uniform scale applied to the arm so it matches the user's arm length.")]
        public float modelScale = 1f;

        private bool hasTarget;
        private Vector3 smoothedPos;
        private float smoothedYaw;

        public XrTrackingService Tracking => XrTrackingService.Instance;

        /// <summary>Yaw-only rotation of the head (world).</summary>
        public static Quaternion YawOnly(Quaternion q)
        {
            Vector3 f = q * Vector3.forward;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-6f)
            {
                f = q * Vector3.up;
                f.y = 0f;
                if (f.sqrMagnitude < 1e-6f) return Quaternion.identity;
            }
            return Quaternion.LookRotation(f.normalized, Vector3.up);
        }

        private void OnEnable()
        {
            hasTarget = false;
            ApplyScale();
        }

        private void Update()
        {
            ApplyScale();
            if (!followHead) return;
            XrTrackingService t = Tracking;
            if (t == null || !t.Head.isValid) return;

            Quaternion yaw = YawOnly(t.Head.rotation);
            Vector3 targetPos = t.Head.position + yaw * offsetFromHead;
            float targetYaw = yaw.eulerAngles.y;

            if (!hasTarget || positionSmoothing <= 0f)
            {
                smoothedPos = targetPos;
                smoothedYaw = targetYaw;
                hasTarget = true;
            }
            else
            {
                float kp = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
                float ky = yawSmoothing <= 0f ? 1f : 1f - Mathf.Exp(-yawSmoothing * Time.deltaTime);
                smoothedPos = Vector3.Lerp(smoothedPos, targetPos, kp);
                smoothedYaw = Mathf.LerpAngle(smoothedYaw, targetYaw, ky);
            }
            transform.SetPositionAndRotation(smoothedPos, Quaternion.Euler(0f, smoothedYaw, 0f));
        }

        private void ApplyScale()
        {
            float s = Mathf.Clamp(modelScale, 0.5f, 1.6f);
            if (Mathf.Abs(transform.localScale.x - s) > 1e-4f) transform.localScale = new Vector3(s, s, s);
        }

        /// <summary>Applies a calibration result to this anchor.</summary>
        public void ApplyCalibration(ArmCalibration c)
        {
            if (c == null) return;
            offsetFromHead = c.shoulderOffsetFromHead;
            modelScale = c.modelScale;
            hasTarget = false;
            ApplyScale();
        }
    }
}
