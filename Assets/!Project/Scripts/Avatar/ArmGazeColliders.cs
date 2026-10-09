using FAMOT.Core;
using FAMOT.Input;
using FAMOT.Kinematics;
using UnityEngine;

namespace FAMOT.Avatar
{
    /// <summary>
    /// Adds trigger capsule colliders along the upper arm, forearm and hand of an <see cref="ArmRig"/>
    /// so that the gaze ray can tell whether the participant looks at the live or the ghost arm.
    /// </summary>
    public class ArmGazeColliders : MonoBehaviour
    {
        public ArmRig rig;
        public GazeTargetKind kind = GazeTargetKind.LiveArm;
        public float upperArmRadius = 0.05f;
        public float forearmRadius = 0.04f;
        public float handRadius = 0.045f;
        public float handLength = 0.18f;
        [Tooltip("Layer for the generated colliders (-1 keeps the bone's layer).")]
        public int layer = -1;

        private void Start()
        {
            if (rig == null) rig = GetComponentInChildren<ArmRig>();
            if (rig == null || !rig.IsBound) return;
            var tag = GetComponent<GazeTarget>();
            if (tag == null) tag = gameObject.AddComponent<GazeTarget>();
            tag.kind = kind;
            if (string.IsNullOrEmpty(tag.label)) tag.label = kind == GazeTargetKind.GhostArm ? "ghost_arm_" + rig.side.ShortName() : "live_arm_" + rig.side.ShortName();

            Build(rig.upperArmBone, rig.forearmBone.position, upperArmRadius);
            Build(rig.forearmBone, rig.handBone.position, forearmRadius);
            Vector3 handEnd = rig.handTipBone != null
                ? rig.handBone.position + (rig.handTipBone.position - rig.handBone.position).normalized * handLength
                : rig.handBone.position + (rig.handBone.position - rig.forearmBone.position).normalized * handLength;
            Build(rig.handBone, handEnd, handRadius);
        }

        private void Build(Transform bone, Vector3 endWorld, float radius)
        {
            if (bone == null) return;
            var col = bone.GetComponent<CapsuleCollider>();
            if (col == null) col = bone.gameObject.AddComponent<CapsuleCollider>();
            col.isTrigger = true;
            Vector3 endLocal = bone.InverseTransformPoint(endWorld);
            float len = endLocal.magnitude;
            // Pick the local axis closest to the segment direction.
            Vector3 a = endLocal.normalized;
            int axis = Mathf.Abs(a.y) >= Mathf.Abs(a.x) && Mathf.Abs(a.y) >= Mathf.Abs(a.z) ? 1 : Mathf.Abs(a.x) >= Mathf.Abs(a.z) ? 0 : 2;
            col.direction = axis;
            col.center = endLocal * 0.5f;
            Vector3 ls = bone.lossyScale;
            float scale = Mathf.Max(1e-4f, Mathf.Abs(axis == 0 ? ls.x : axis == 1 ? ls.y : ls.z));
            float radialScale = Mathf.Max(1e-4f, Mathf.Abs(axis == 0 ? ls.y : ls.x));
            col.height = len + 2f * radius / scale;
            col.radius = radius / radialScale;
            if (layer >= 0) bone.gameObject.layer = layer;
        }
    }
}
