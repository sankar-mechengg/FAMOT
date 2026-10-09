using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Input
{
    /// <summary>What a gaze hit belongs to.</summary>
    public enum GazeTargetKind
    {
        Other = 0,
        LiveArm = 1,
        GhostArm = 2,
        UI = 3,
        Environment = 4
    }

    /// <summary>Tag a collider hierarchy so gaze hits can be classified.</summary>
    public class GazeTarget : MonoBehaviour
    {
        public GazeTargetKind kind = GazeTargetKind.Other;
        public string label;

        public string Label => string.IsNullOrEmpty(label) ? name : label;
    }

    /// <summary>
    /// Casts the eye-gaze ray (or the head ray when eye tracking is unavailable) every frame and
    /// records what it hits. Results are read by the recorder and the experimenter panel.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class GazeRaycaster : MonoBehaviour
    {
        public static GazeRaycaster Instance { get; private set; }

        public LayerMask layers = ~0;
        public float maxDistance = 10f;
        [Tooltip("Use the head forward ray when no eye tracker is available (desktop or Quest 3).")]
        public bool fallbackToHead = true;
        [Tooltip("In desktop mode (no XR) use the main camera as the head.")]
        public bool useMainCameraWithoutXr = true;
        public bool drawDebugRay;

        public TrackedPose Ray { get; private set; } = TrackedPose.Invalid;
        public bool UsingEyeTracking { get; private set; }
        public bool HasHit { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public float HitDistance { get; private set; }
        public string HitName { get; private set; } = string.Empty;
        public GazeTargetKind HitKind { get; private set; } = GazeTargetKind.Other;
        public Collider HitCollider { get; private set; }

        private readonly RaycastHit[] hits = new RaycastHit[8];

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            XrTrackingService t = XrTrackingService.Instance;
            TrackedPose ray = TrackedPose.Invalid;
            UsingEyeTracking = false;
            if (t != null && t.GazeTracked)
            {
                ray = t.Gaze;
                UsingEyeTracking = true;
            }
            else if (fallbackToHead && t != null && t.Head.isValid)
            {
                ray = t.Head;
            }
            else if (useMainCameraWithoutXr && Camera.main != null)
            {
                ray = TrackedPose.FromTransform(Camera.main.transform);
            }
            Ray = ray;
            HasHit = false;
            HitCollider = null;
            HitName = string.Empty;
            HitKind = GazeTargetKind.Other;
            HitDistance = 0f;
            if (!ray.isValid) return;

            Vector3 dir = ray.rotation * Vector3.forward;
            int n = Physics.RaycastNonAlloc(ray.position, dir, hits, maxDistance, layers, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].distance < best)
                {
                    best = hits[i].distance;
                    bestIdx = i;
                }
            }
            if (bestIdx >= 0)
            {
                RaycastHit h = hits[bestIdx];
                HasHit = true;
                HitPoint = h.point;
                HitDistance = h.distance;
                HitCollider = h.collider;
                GazeTarget tag = h.collider.GetComponentInParent<GazeTarget>();
                if (tag != null)
                {
                    HitKind = tag.kind;
                    HitName = tag.Label;
                }
                else
                {
                    HitKind = GazeTargetKind.Environment;
                    HitName = h.collider.name;
                }
            }
            if (drawDebugRay)
            {
                Debug.DrawRay(ray.position, dir * (HasHit ? HitDistance : maxDistance), UsingEyeTracking ? Color.cyan : Color.gray);
            }
        }
    }
}
