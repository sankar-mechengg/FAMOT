using System.Globalization;
using System.Threading;
using FAMOT.Core;
using FAMOT.Protocol;
using NUnit.Framework;
using UnityEngine;

namespace FAMOT.Tests
{
    public class StreamFormatterTests
    {
        private static UpperLimbAngles Angles(float start, float step)
        {
            var a = new UpperLimbAngles();
            for (int i = 0; i < UpperLimbAngles.Count; i++)
                a[i] = start + step * i;
            return a;
        }

        private static string SampleFrame(string phase = "TRIAL", TargetStatus status = TargetStatus.Success)
        {
            return StreamFormatter.Frame(
                12.3456, 789, phase, 3, 4,
                Angles(1.234f, 1.5f), Angles(-5.678f, 0.25f),
                new TrackedPose(new Vector3(0.1234f, 1.5f, -0.25f), Quaternion.identity, true),
                new TrackedPose(new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f), true),
                new TrackedPose(new Vector3(-0.2f, 1.1f, 0.3f), Quaternion.identity, true),
                TrackedPose.Invalid,
                status);
        }

        [Test]
        public void Frame_FieldCountMatchesDocumentation()
        {
            string[] doc = StreamFormatter.FrameHeaderCsv.Split(',');
            string[] fields = SampleFrame().Split(',');
            Assert.AreEqual(StreamFormatter.FrameFieldCount, doc.Length, "header column count");
            Assert.AreEqual(doc.Length, fields.Length, "frame field count");
            StringAssert.Contains(StreamFormatter.FrameHeaderCsv, StreamFormatter.FrameHeaderDoc);
        }

        [Test]
        public void Frame_ColumnsAreWhereDocumented()
        {
            string[] f = SampleFrame().Split(',');
            Assert.AreEqual("D", f[0]);
            Assert.AreEqual("12.346", f[1]);
            Assert.AreEqual("789", f[2]);
            Assert.AreEqual("TRIAL", f[3]);
            Assert.AreEqual("3", f[4]);
            Assert.AreEqual("4", f[5]);
            Assert.AreEqual("1.23", f[6]);   // live[0], 2 decimals
            Assert.AreEqual("-5.68", f[13]); // target[0], 2 decimals
            Assert.AreEqual("0.123", f[20]); // head px
            Assert.AreEqual("1.000", f[26]); // head qw
            Assert.AreEqual("1.000", f[27 + 3]); // gaze dx = +1 for yaw 90
            Assert.AreEqual("0.000", f[27 + 5].Replace("-", ""));
            Assert.AreEqual("1", f[33]);     // left valid
            Assert.AreEqual("0", f[41]);     // right invalid
            Assert.AreEqual("S", f[49]);
        }

        [Test]
        public void Frame_StatusFlags()
        {
            Assert.IsTrue(SampleFrame("P", TargetStatus.Success).EndsWith(",S"));
            Assert.IsTrue(SampleFrame("P", TargetStatus.Fail).EndsWith(",F"));
            Assert.IsTrue(SampleFrame("P", TargetStatus.None).EndsWith(",N"));
        }

        [Test]
        public void Frame_PhaseWithCommaDoesNotBreakLayout()
        {
            string[] f = SampleFrame("phase,with,commas").Split(',');
            Assert.AreEqual(StreamFormatter.FrameFieldCount, f.Length);
            Assert.AreEqual("phase_with_commas", f[3]);
            Assert.AreEqual(StreamFormatter.FrameFieldCount, SampleFrame(null).Split(',').Length);
        }

        [Test]
        public void Frame_UsesInvariantCulture()
        {
            CultureInfo old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string line = SampleFrame();
                string[] f = line.Split(',');
                Assert.AreEqual(StreamFormatter.FrameFieldCount, f.Length, "de-DE would add commas if the culture leaked");
                Assert.AreEqual("12.346", f[1]);
                Assert.AreEqual("1.23", f[6]);
                Assert.AreEqual("0.123", f[20]);

                string status = StreamFormatter.StatusLine("1.0.2", true, "S01", 8400);
                Assert.AreEqual("S,1.0.2,1,S01,8400", status);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [Test]
        public void StatusLine_Format()
        {
            Assert.AreEqual("S,0.9,0,,8400", StreamFormatter.StatusLine("0.9", false, null, 8400));
            Assert.AreEqual("S,1.0,1,a_b,9000", StreamFormatter.StatusLine("1.0", true, "a,b", 9000));
        }
    }
}
