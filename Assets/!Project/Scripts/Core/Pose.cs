using System;
using UnityEngine;

namespace FAMOT.Core
{
    /// <summary>A world-space position + rotation pair with validity.</summary>
    [Serializable]
    public struct TrackedPose
    {
        public Vector3 position;
        public Quaternion rotation;
        public bool isValid;

        public TrackedPose(Vector3 p, Quaternion r, bool valid = true)
        {
            position = p;
            rotation = r;
            isValid = valid;
        }

        public static TrackedPose Invalid => new TrackedPose(Vector3.zero, Quaternion.identity, false);

        public Vector3 Forward => rotation * Vector3.forward;
        public Vector3 Up => rotation * Vector3.up;
        public Vector3 Right => rotation * Vector3.right;

        public static TrackedPose FromTransform(Transform t) => new TrackedPose(t.position, t.rotation, true);
    }
}
