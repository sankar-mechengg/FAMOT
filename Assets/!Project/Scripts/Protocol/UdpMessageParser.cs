using System;
using System.Globalization;
using System.Text;
using FAMOT.Core;

namespace FAMOT.Protocol
{
    /// <summary>
    /// Parses and formats the comma separated UDP protocol. All parsing is culture invariant,
    /// tolerant to surrounding whitespace / trailing newlines and to extra trailing fields,
    /// and never throws. Not thread safe for the Format* helpers (shared StringBuilder);
    /// call them from one thread (normally the Unity main thread).
    /// </summary>
    public static class UdpMessageParser
    {
        private static readonly char[] TrimChars = { ' ', '\t', '\r', '\n', '\0' };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly StringBuilder Sb = new StringBuilder(256);

        private const int KinematicsMinFields = 1 + 2 * UpperLimbAngles.Count; // K + 7 target + 7 input

        /// <summary>
        /// Parses one datagram. Never throws. Always returns a non-null <paramref name="message"/>.
        /// Returns true only for a recognised, well formed message. For anything else (unrecognised
        /// tag, missing fields, malformed numbers) it returns false and the message has
        /// <see cref="UdpMessageType.Unknown"/>, the trimmed text in <see cref="UdpMessage.raw"/> and
        /// a description in <see cref="UdpMessage.parseError"/>.
        /// </summary>
        public static bool TryParse(string text, out UdpMessage message)
        {
            message = new UdpMessage();
            if (text == null)
            {
                message.parseError = "null text";
                return false;
            }

            try
            {
                string trimmed = text.Trim(TrimChars);
                message.raw = trimmed;
                string error = ParseInto(trimmed, message);
                if (error != null)
                {
                    message.type = UdpMessageType.Unknown;
                    message.parseError = error;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                message.type = UdpMessageType.Unknown;
                message.parseError = "exception: " + ex.Message;
                return false;
            }
        }

        // Returns null on success, otherwise an error description.
        private static string ParseInto(string text, UdpMessage m)
        {
            if (text.Length == 0)
                return "empty message";

            string[] f = text.Split(',');
            for (int i = 0; i < f.Length; i++)
                f[i] = f[i].Trim(TrimChars);

            if (f[0].Length != 1)
                return "unrecognised message tag '" + f[0] + "'";

            switch (char.ToUpperInvariant(f[0][0]))
            {
                case 'Q':
                    m.type = UdpMessageType.Query;
                    return null;
                case 'C':
                    return ParseCalibration(f, m);
                case 'T':
                    return ParseLegacyTest(f, m);
                case 'K':
                    return ParseKinematics(f, m);
                case 'E':
                    return ParseEvent(text, f, m);
                default:
                    return "unrecognised message tag '" + f[0] + "'";
            }
        }

        private static string ParseCalibration(string[] f, UdpMessage m)
        {
            if (f.Length < 2 || f[1].Length != 1)
                return "C message needs a one letter sub-command";

            switch (char.ToUpperInvariant(f[1][0]))
            {
                case 'R': m.type = UdpMessageType.CalibrationPose; m.pose = CalibrationPose.Rest; return null;
                case 'F': m.type = UdpMessageType.CalibrationPose; m.pose = CalibrationPose.Flexion; return null;
                case 'E': m.type = UdpMessageType.CalibrationPose; m.pose = CalibrationPose.Extension; return null;
                case 'P': m.type = UdpMessageType.CalibrationPose; m.pose = CalibrationPose.Pronation; return null;
                case 'S': m.type = UdpMessageType.CalibrationPose; m.pose = CalibrationPose.Supination; return null;
                case 'N':
                {
                    if (f.Length < 7)
                        return "C,N needs minFE,maxFE,minPS,maxPS,numTargets (got " + (f.Length - 2) + " values)";
                    if (!TryNum(f[2], out float minFE)) return BadNumber("minFE", f[2]);
                    if (!TryNum(f[3], out float maxFE)) return BadNumber("maxFE", f[3]);
                    if (!TryNum(f[4], out float minPS)) return BadNumber("minPS", f[4]);
                    if (!TryNum(f[5], out float maxPS)) return BadNumber("maxPS", f[5]);
                    if (!TryNum(f[6], out float n)) return BadNumber("numTargets", f[6]);
                    m.type = UdpMessageType.CalibrationLimits;
                    m.minFE = minFE;
                    m.maxFE = maxFE;
                    m.minPS = minPS;
                    m.maxPS = maxPS;
                    m.numTargets = (int)Math.Round(n); // "10.00" -> 10
                    return null;
                }
                default:
                    return "unknown calibration sub-command '" + f[1] + "'";
            }
        }

        private static string ParseLegacyTest(string[] f, UdpMessage m)
        {
            if (f.Length < 5)
                return "T needs t1,t2,i1,i2 (got " + (f.Length - 1) + " values)";
            if (!TryNum(f[1], out float t1)) return BadNumber("t1", f[1]);
            if (!TryNum(f[2], out float t2)) return BadNumber("t2", f[2]);
            if (!TryNum(f[3], out float i1)) return BadNumber("i1", f[3]);
            if (!TryNum(f[4], out float i2)) return BadNumber("i2", f[4]);
            m.type = UdpMessageType.LegacyTest;
            m.t1 = t1;
            m.t2 = t2;
            m.i1 = i1;
            m.i2 = i2;
            m.status = ParseStatus(f, 5);
            return null;
        }

        private static string ParseKinematics(string[] f, UdpMessage m)
        {
            if (f.Length < KinematicsMinFields)
                return "K needs 14 angle values (got " + (f.Length - 1) + ")";

            var target = new UpperLimbAngles();
            var input = new UpperLimbAngles();
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if (!TryNum(f[1 + i], out float tv))
                    return BadNumber("target " + UpperLimbAngles.Names[i], f[1 + i]);
                target[i] = tv;
            }
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                if (!TryNum(f[1 + UpperLimbAngles.Count + i], out float iv))
                    return BadNumber("input " + UpperLimbAngles.Names[i], f[1 + UpperLimbAngles.Count + i]);
                input[i] = iv;
            }

            m.type = UdpMessageType.Kinematics;
            m.targetAngles = target;
            m.inputAngles = input;
            m.status = ParseStatus(f, KinematicsMinFields);
            return null;
        }

        private static string ParseEvent(string text, string[] f, UdpMessage m)
        {
            if (f.Length < 2 || f[1].Length == 0)
                return "E needs an event name";

            float value = float.NaN;
            if (f.Length > 2 && f[2].Length > 0)
            {
                if (!TryNum(f[2], out value)) return BadNumber("event value", f[2]);
            }

            // Detail = everything after the third comma (may itself contain commas).
            string detail = string.Empty;
            int idx = -1;
            for (int c = 0; c < 3; c++)
            {
                idx = text.IndexOf(',', idx + 1);
                if (idx < 0) break;
            }
            if (idx >= 0)
                detail = text.Substring(idx + 1).Trim(TrimChars);

            m.type = UdpMessageType.Event;
            m.eventName = f[1];
            m.eventValue = value;
            m.eventDetail = detail;
            return null;
        }

        private static TargetStatus ParseStatus(string[] f, int index)
        {
            if (f.Length <= index || f[index].Length != 1)
                return TargetStatus.None;
            switch (char.ToUpperInvariant(f[index][0]))
            {
                case 'S': return TargetStatus.Success;
                case 'F': return TargetStatus.Fail;
                default: return TargetStatus.None;
            }
        }

        private static bool TryNum(string s, out float value)
        {
            if (float.TryParse(s, NumberStyles.Float, Inv, out value) && !float.IsNaN(value) && !float.IsInfinity(value))
                return true;
            value = 0f;
            return false;
        }

        private static string BadNumber(string field, string text)
        {
            return "invalid number for " + field + ": '" + text + "'";
        }

        // ---------------------------------------------------------------- formatting

        /// <summary>
        /// Builds a <c>K,...</c> message (round-trip precision, invariant culture).
        /// Status <see cref="TargetStatus.None"/> omits the trailing flag.
        /// </summary>
        public static string FormatKinematics(UpperLimbAngles target, UpperLimbAngles input, TargetStatus status)
        {
            lock (Sb)
            {
                Sb.Length = 0;
                Sb.Append('K');
                for (int i = 0; i < UpperLimbAngles.Count; i++)
                    Sb.Append(',').Append(target[i].ToString("R", Inv));
                for (int i = 0; i < UpperLimbAngles.Count; i++)
                    Sb.Append(',').Append(input[i].ToString("R", Inv));
                AppendStatus(Sb, status);
                return Sb.ToString();
            }
        }

        /// <summary>
        /// Builds a legacy <c>T,t1,t2,i1,i2[,S|F]</c> message (invariant culture).
        /// </summary>
        public static string FormatLegacyTest(float t1, float t2, float i1, float i2, TargetStatus status)
        {
            lock (Sb)
            {
                Sb.Length = 0;
                Sb.Append("T,")
                  .Append(t1.ToString("R", Inv)).Append(',')
                  .Append(t2.ToString("R", Inv)).Append(',')
                  .Append(i1.ToString("R", Inv)).Append(',')
                  .Append(i2.ToString("R", Inv));
                AppendStatus(Sb, status);
                return Sb.ToString();
            }
        }

        private static void AppendStatus(StringBuilder sb, TargetStatus status)
        {
            if (status == TargetStatus.Success) sb.Append(",S");
            else if (status == TargetStatus.Fail) sb.Append(",F");
        }
    }
}
