using System;
using UnityEngine;

namespace FAMOT.Session
{
    /// <summary>
    /// Metadata for one recording session, written as <c>session.json</c> in the session folder.
    /// Flat [Serializable] class with public fields so that <c>UnityEngine.JsonUtility</c> can round-trip it.
    /// </summary>
    [Serializable]
    public class SessionInfo
    {
        /// <summary>Calibration state of one arm at the time it was recorded.</summary>
        [Serializable]
        public class ArmCalibrationRecord
        {
            /// <summary>"L" or "R".</summary>
            public string side = "";

            /// <summary>True when this arm has been calibrated in this session.</summary>
            public bool calibrated;

            /// <summary>Shoulder position relative to the head, in head space, metres.</summary>
            public Vector3 shoulderOffsetFromHead;

            /// <summary>Measured arm length (shoulder to wrist), metres.</summary>
            public float armLength;

            /// <summary>Uniform scale applied to the avatar arm model.</summary>
            public float modelScale = 1f;

            /// <summary>Wrist offset from the tracked hand/controller, metres.</summary>
            public Vector3 wristOffset;

            /// <summary>Rotation offset between the tracked hand/controller and the model hand.</summary>
            public Quaternion handRotationOffset = Quaternion.identity;

            /// <summary>Calibration time, UTC, ISO 8601 ("o"); empty when not calibrated.</summary>
            public string calibratedUtc = "";
        }

        /// <summary>A world-space pose snapshot with a timestamp.</summary>
        [Serializable]
        public class PoseRecord
        {
            /// <summary>World position, metres.</summary>
            public Vector3 position;

            /// <summary>World rotation.</summary>
            public Quaternion rotation = Quaternion.identity;

            /// <summary>Snapshot time, UTC, ISO 8601 ("o"); empty when never captured.</summary>
            public string utc = "";
        }

        /// <summary>Session identifier; equal to the session folder name.</summary>
        public string sessionId = "";

        /// <summary>Id of the subject this session belongs to.</summary>
        public string subjectId = "";

        /// <summary>Name of the subject at session start.</summary>
        public string subjectName = "";

        /// <summary>"Desktop" or "VR".</summary>
        public string mode = "";

        /// <summary>Active hand: "L" or "R".</summary>
        public string hand = "";

        /// <summary>Session start, UTC, ISO 8601 ("o").</summary>
        public string startUtc = "";

        /// <summary>Session end, UTC, ISO 8601 ("o"); empty while running or if the app crashed.</summary>
        public string endUtc = "";

        /// <summary><c>Application.unityVersion</c>.</summary>
        public string unityVersion = "";

        /// <summary><c>Application.version</c>.</summary>
        public string appVersion = "";

        /// <summary><c>Application.platform</c> as text.</summary>
        public string platform = "";

        /// <summary><c>SystemInfo.deviceModel</c>.</summary>
        public string deviceModel = "";

        /// <summary>Loaded XR display/runtime name (<c>XRSettings.loadedDeviceName</c>) or "None".</summary>
        public string xrRuntimeName = "";

        /// <summary>Name of the head-mounted device reported by the XR input subsystem, if any.</summary>
        public string xrDeviceName = "";

        /// <summary>Description of the input source driving the avatar (e.g. "UDP", "XRHands", "Controllers").</summary>
        public string inputSource = "";

        /// <summary>Local UDP port listened on, or 0 when unused.</summary>
        public int udpListenPort;

        /// <summary>UDP stream target "host:port", or empty when unused.</summary>
        public string udpStreamTarget = "";

        /// <summary>Target frame rate at session start (<c>Application.targetFrameRate</c>; -1 = platform default).</summary>
        public float targetFrameRate;

        /// <summary>Free-text notes.</summary>
        public string notes = "";

        /// <summary>Left arm calibration snapshot.</summary>
        public ArmCalibrationRecord leftArm = new ArmCalibrationRecord { side = "L" };

        /// <summary>Right arm calibration snapshot.</summary>
        public ArmCalibrationRecord rightArm = new ArmCalibrationRecord { side = "R" };

        /// <summary>Pose of the XR origin (rig) at calibration / session start.</summary>
        public PoseRecord xrOrigin = new PoseRecord();

        /// <summary>Column names of <c>kinematics.csv</c>; filled in by the recorder.</summary>
        public string[] columnsKinematics = new string[0];

        /// <summary>Column names of <c>tracking.csv</c>; filled in by the recorder.</summary>
        public string[] columnsTracking = new string[0];
    }
}
