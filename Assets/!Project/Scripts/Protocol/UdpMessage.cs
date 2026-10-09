using System.Globalization;
using FAMOT.Core;

namespace FAMOT.Protocol
{
    /// <summary>Kind of a parsed inbound UDP datagram.</summary>
    public enum UdpMessageType
    {
        /// <summary>Unrecognised or malformed message. <see cref="UdpMessage.raw"/> holds the text.</summary>
        Unknown,
        /// <summary><c>C,R|F|E|P|S</c> - calibration pose command.</summary>
        CalibrationPose,
        /// <summary><c>C,N,minFE,maxFE,minPS,maxPS,numTargets</c> - 2-DOF limits.</summary>
        CalibrationLimits,
        /// <summary><c>T,t1,t2,i1,i2[,S|F]</c> - legacy 2-DOF test frame.</summary>
        LegacyTest,
        /// <summary><c>K,...</c> - 7-DOF target and input angles.</summary>
        Kinematics,
        /// <summary><c>E,NAME[,value[,detail]]</c> - event marker.</summary>
        Event,
        /// <summary><c>Q</c> - handshake / ping.</summary>
        Query
    }

    /// <summary>The calibration pose requested by a <c>C,x</c> message.</summary>
    public enum CalibrationPose
    {
        Rest,
        Flexion,
        Extension,
        Pronation,
        Supination
    }

    /// <summary>Trial outcome flag attached to T and K messages.</summary>
    public enum TargetStatus
    {
        /// <summary>No flag present.</summary>
        None,
        /// <summary>"S" - target reached.</summary>
        Success,
        /// <summary>"F" - target missed.</summary>
        Fail
    }

    /// <summary>
    /// A parsed inbound message. Plain data holder; only the fields relevant to
    /// <see cref="type"/> are meaningful, the rest keep their defaults.
    /// </summary>
    public class UdpMessage
    {
        /// <summary>Kind of message.</summary>
        public UdpMessageType type = UdpMessageType.Unknown;
        /// <summary>The datagram text (trimmed of surrounding whitespace / newlines).</summary>
        public string raw = string.Empty;

        /// <summary>CalibrationPose: requested pose.</summary>
        public CalibrationPose pose;

        /// <summary>CalibrationLimits: flexion limit in degrees (usually negative).</summary>
        public float minFE;
        /// <summary>CalibrationLimits: extension limit in degrees.</summary>
        public float maxFE;
        /// <summary>CalibrationLimits: pronation limit in degrees.</summary>
        public float minPS;
        /// <summary>CalibrationLimits: supination limit in degrees.</summary>
        public float maxPS;
        /// <summary>CalibrationLimits: number of targets per trial.</summary>
        public int numTargets;

        /// <summary>LegacyTest: flexion/extension target, 0..100 (50 = rest).</summary>
        public float t1;
        /// <summary>LegacyTest: pronation/supination target, 0..100 (50 = rest).</summary>
        public float t2;
        /// <summary>LegacyTest: flexion/extension input, 0..100 (50 = rest).</summary>
        public float i1;
        /// <summary>LegacyTest: pronation/supination input, 0..100 (50 = rest).</summary>
        public float i2;

        /// <summary>Kinematics: target angles in degrees relative to rest.</summary>
        public UpperLimbAngles targetAngles;
        /// <summary>Kinematics: input angles in degrees relative to rest.</summary>
        public UpperLimbAngles inputAngles;

        /// <summary>LegacyTest / Kinematics: optional success/fail flag.</summary>
        public TargetStatus status = TargetStatus.None;

        /// <summary>Event: marker name, e.g. TRIAL_START.</summary>
        public string eventName = string.Empty;
        /// <summary>Event: optional numeric value, NaN if absent.</summary>
        public float eventValue = float.NaN;
        /// <summary>Event: optional free text (may contain commas), empty if absent.</summary>
        public string eventDetail = string.Empty;

        /// <summary>Set when the text could not be parsed; null otherwise.</summary>
        public string parseError;

        /// <inheritdoc />
        public override string ToString()
        {
            var ci = CultureInfo.InvariantCulture;
            switch (type)
            {
                case UdpMessageType.CalibrationPose:
                    return "CalibrationPose(" + pose + ")";
                case UdpMessageType.CalibrationLimits:
                    return string.Format(ci, "CalibrationLimits(FE {0}..{1}, PS {2}..{3}, targets {4})",
                        minFE, maxFE, minPS, maxPS, numTargets);
                case UdpMessageType.LegacyTest:
                    return string.Format(ci, "LegacyTest(t {0},{1} i {2},{3} {4})", t1, t2, i1, i2, status);
                case UdpMessageType.Kinematics:
                    return "Kinematics(target [" + targetAngles + "] input [" + inputAngles + "] " + status + ")";
                case UdpMessageType.Event:
                    return string.Format(ci, "Event({0}, value {1}, detail '{2}')", eventName, eventValue, eventDetail);
                case UdpMessageType.Query:
                    return "Query";
                default:
                    return "Unknown('" + raw + "'" + (string.IsNullOrEmpty(parseError) ? "" : ", " + parseError) + ")";
            }
        }
    }
}
