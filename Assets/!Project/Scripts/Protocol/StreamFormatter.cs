using System.Globalization;
using System.Text;
using FAMOT.Core;
using UnityEngine;

namespace FAMOT.Protocol
{
    /// <summary>
    /// Builds the outbound text lines streamed to MATLAB/Python. All numbers use the invariant
    /// culture. Uses a shared StringBuilder, so call from the main thread only.
    /// </summary>
    public static class StreamFormatter
    {
        /// <summary>Number of comma separated fields in a <c>D</c> frame line.</summary>
        public const int FrameFieldCount = 50;

        /// <summary>Comma separated column names of a <c>D</c> frame line (<see cref="FrameFieldCount"/> columns).</summary>
        public const string FrameHeaderCsv =
            "tag,unity_time,frame,phase,trial,target," +
            "live_shoulder_flexion,live_shoulder_abduction,live_shoulder_rotation,live_elbow_flexion,live_forearm_supination,live_wrist_extension,live_wrist_radial," +
            "target_shoulder_flexion,target_shoulder_abduction,target_shoulder_rotation,target_elbow_flexion,target_forearm_supination,target_wrist_extension,target_wrist_radial," +
            "head_px,head_py,head_pz,head_qx,head_qy,head_qz,head_qw," +
            "gaze_px,gaze_py,gaze_pz,gaze_dx,gaze_dy,gaze_dz," +
            "lwrist_valid,lwrist_px,lwrist_py,lwrist_pz,lwrist_qx,lwrist_qy,lwrist_qz,lwrist_qw," +
            "rwrist_valid,rwrist_px,rwrist_py,rwrist_pz,rwrist_qx,rwrist_qy,rwrist_qz,rwrist_qw," +
            "status";

        /// <summary>Documentation of the <c>D</c> line and the status line, for the README.</summary>
        public const string FrameHeaderDoc =
            "Per-frame line (UDP, UTF-8, comma separated, '.' decimal point, 50 fields):\n" +
            "  " + FrameHeaderCsv + "\n" +
            "tag         always 'D'\n" +
            "unity_time  Unity time in seconds (3 decimals)\n" +
            "frame       Unity frame count\n" +
            "phase       experiment phase name (commas replaced by '_')\n" +
            "trial,target  1-based indices, 0 when not applicable\n" +
            "live_*      7 live joint angles in degrees relative to rest (2 decimals), UpperLimbAngles order\n" +
            "target_*    7 target joint angles in degrees relative to rest (2 decimals)\n" +
            "head_*      head world position (m) and rotation quaternion (3 decimals)\n" +
            "gaze_*      gaze origin (m) and unit direction = rotation * forward (3 decimals)\n" +
            "lwrist_*, rwrist_*  valid flag (0/1), world position (m) and quaternion (3 decimals)\n" +
            "status      S = target success, F = fail, N = none\n" +
            "Status line (reply to 'Q'): S,<version>,<recording 0/1>,<sessionId>,<port>";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly StringBuilder Sb = new StringBuilder(768);

        /// <summary>
        /// Builds one <c>D,...</c> frame line, see <see cref="FrameHeaderDoc"/> for the column order.
        /// </summary>
        public static string Frame(double unityTime, int frame, string phase, int trial, int target,
            UpperLimbAngles live, UpperLimbAngles targetAngles,
            TrackedPose head, TrackedPose gaze, TrackedPose leftWrist, TrackedPose rightWrist,
            TargetStatus status)
        {
            lock (Sb)
            {
                StringBuilder sb = Sb;
                sb.Length = 0;
                sb.Append('D');
                sb.Append(',').Append(unityTime.ToString("0.000", Inv));
                sb.Append(',').Append(frame.ToString(Inv));
                sb.Append(',').Append(Sanitize(phase));
                sb.Append(',').Append(trial.ToString(Inv));
                sb.Append(',').Append(target.ToString(Inv));

                for (int i = 0; i < UpperLimbAngles.Count; i++)
                    sb.Append(',').Append(live[i].ToString("F2", Inv));
                for (int i = 0; i < UpperLimbAngles.Count; i++)
                    sb.Append(',').Append(targetAngles[i].ToString("F2", Inv));

                AppendPosRot(sb, head.position, head.rotation);

                Vector3 dir = gaze.rotation * Vector3.forward;
                AppendVec3(sb, gaze.position);
                AppendVec3(sb, dir);

                AppendWrist(sb, leftWrist);
                AppendWrist(sb, rightWrist);

                sb.Append(',');
                switch (status)
                {
                    case TargetStatus.Success: sb.Append('S'); break;
                    case TargetStatus.Fail: sb.Append('F'); break;
                    default: sb.Append('N'); break;
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// Builds the reply to a <c>Q</c> handshake: <c>S,&lt;version&gt;,&lt;recording 0/1&gt;,&lt;sessionId&gt;,&lt;port&gt;</c>.
        /// </summary>
        public static string StatusLine(string appVersion, bool recording, string sessionId, int listenPort)
        {
            return "S," + Sanitize(appVersion) + "," + (recording ? "1" : "0") + "," + Sanitize(sessionId) + "," +
                   listenPort.ToString(Inv);
        }

        private static void AppendWrist(StringBuilder sb, TrackedPose p)
        {
            sb.Append(',').Append(p.isValid ? '1' : '0');
            AppendPosRot(sb, p.position, p.rotation);
        }

        private static void AppendPosRot(StringBuilder sb, Vector3 pos, Quaternion rot)
        {
            AppendVec3(sb, pos);
            sb.Append(',').Append(rot.x.ToString("F3", Inv));
            sb.Append(',').Append(rot.y.ToString("F3", Inv));
            sb.Append(',').Append(rot.z.ToString("F3", Inv));
            sb.Append(',').Append(rot.w.ToString("F3", Inv));
        }

        private static void AppendVec3(StringBuilder sb, Vector3 v)
        {
            sb.Append(',').Append(v.x.ToString("F3", Inv));
            sb.Append(',').Append(v.y.ToString("F3", Inv));
            sb.Append(',').Append(v.z.ToString("F3", Inv));
        }

        // Keeps free text from breaking the comma separated layout.
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace(',', '_').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
