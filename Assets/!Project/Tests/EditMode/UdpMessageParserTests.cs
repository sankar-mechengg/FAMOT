using System.Globalization;
using System.Threading;
using FAMOT.Core;
using FAMOT.Protocol;
using NUnit.Framework;

namespace FAMOT.Tests
{
    public class UdpMessageParserTests
    {
        private const float Tol = 1e-4f;

        private static UdpMessage Parse(string text)
        {
            Assert.IsTrue(UdpMessageParser.TryParse(text, out UdpMessage m), "expected parse success for: " + text);
            Assert.IsNotNull(m);
            return m;
        }

        private static UdpMessage ParseFail(string text)
        {
            Assert.IsFalse(UdpMessageParser.TryParse(text, out UdpMessage m), "expected parse failure for: " + text);
            Assert.IsNotNull(m);
            Assert.AreEqual(UdpMessageType.Unknown, m.type);
            Assert.IsFalse(string.IsNullOrEmpty(m.parseError));
            return m;
        }

        [TestCase("C,R", CalibrationPose.Rest)]
        [TestCase("C,F", CalibrationPose.Flexion)]
        [TestCase("C,E", CalibrationPose.Extension)]
        [TestCase("C,P", CalibrationPose.Pronation)]
        [TestCase("C,S", CalibrationPose.Supination)]
        public void CalibrationPose_Parses(string text, CalibrationPose expected)
        {
            UdpMessage m = Parse(text);
            Assert.AreEqual(UdpMessageType.CalibrationPose, m.type);
            Assert.AreEqual(expected, m.pose);
        }

        [Test]
        public void CalibrationPose_ToleratesTrailingWhitespaceAndExtraFields()
        {
            Assert.AreEqual(CalibrationPose.Flexion, Parse("C,F\r\n").pose);
            Assert.AreEqual(CalibrationPose.Rest, Parse("  C , R  \n").pose);
            Assert.AreEqual(CalibrationPose.Extension, Parse("C,E,extra,fields").pose);
        }

        [Test]
        public void CalibrationPose_UnknownSubCommandFails()
        {
            ParseFail("C,X");
            ParseFail("C");
            ParseFail("C,");
        }

        [Test]
        public void CalibrationLimits_Parses()
        {
            UdpMessage m = Parse("C,N,-70,70,-20,90,10");
            Assert.AreEqual(UdpMessageType.CalibrationLimits, m.type);
            Assert.AreEqual(-70f, m.minFE, Tol);
            Assert.AreEqual(70f, m.maxFE, Tol);
            Assert.AreEqual(-20f, m.minPS, Tol);
            Assert.AreEqual(90f, m.maxPS, Tol);
            Assert.AreEqual(10, m.numTargets);
        }

        [Test]
        public void CalibrationLimits_NumTargetsAsFloat()
        {
            UdpMessage m = Parse("C,N,-65.5,72.25,-18,88.5,10.00\n");
            Assert.AreEqual(-65.5f, m.minFE, Tol);
            Assert.AreEqual(72.25f, m.maxFE, Tol);
            Assert.AreEqual(10, m.numTargets);
        }

        [Test]
        public void CalibrationLimits_TooFewOrBadFieldsFail()
        {
            ParseFail("C,N,-70,70,-20,90");
            ParseFail("C,N,-70,70,abc,90,10");
            ParseFail("C,N,-70,70,-20,90,");
        }

        [Test]
        public void LegacyTest_Parses()
        {
            UdpMessage m = Parse("T,10,20.5,30,40");
            Assert.AreEqual(UdpMessageType.LegacyTest, m.type);
            Assert.AreEqual(10f, m.t1, Tol);
            Assert.AreEqual(20.5f, m.t2, Tol);
            Assert.AreEqual(30f, m.i1, Tol);
            Assert.AreEqual(40f, m.i2, Tol);
            Assert.AreEqual(TargetStatus.None, m.status);
        }

        [TestCase("T,50,50,50,50,S", TargetStatus.Success)]
        [TestCase("T,50,50,50,50,F", TargetStatus.Fail)]
        [TestCase("T,50,50,50,50,S\r\n", TargetStatus.Success)]
        [TestCase("T,50,50,50,50,X", TargetStatus.None)]
        [TestCase("T,50,50,50,50,", TargetStatus.None)]
        [TestCase("T,50,50,50,50,S,more,fields", TargetStatus.Success)]
        public void LegacyTest_Status(string text, TargetStatus expected)
        {
            Assert.AreEqual(expected, Parse(text).status);
        }

        [Test]
        public void LegacyTest_BadInputFails()
        {
            ParseFail("T,1,2,3");
            ParseFail("T,a,2,3,4");
            ParseFail("T,1,2,3,4x");
            ParseFail("T,1,2,,4");
            ParseFail("T,1,2,NaN,4");
        }

        [Test]
        public void Kinematics_Parses()
        {
            UdpMessage m = Parse("K,1,2,3,4,5,6,7,-1,-2,-3,-4,-5,-6,-7");
            Assert.AreEqual(UdpMessageType.Kinematics, m.type);
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                Assert.AreEqual(i + 1f, m.targetAngles[i], Tol, "target " + i);
                Assert.AreEqual(-(i + 1f), m.inputAngles[i], Tol, "input " + i);
            }
            Assert.AreEqual(TargetStatus.None, m.status);
        }

        [Test]
        public void Kinematics_StatusAndExtraFields()
        {
            Assert.AreEqual(TargetStatus.Success, Parse("K,1,2,3,4,5,6,7,1,2,3,4,5,6,7,S").status);
            Assert.AreEqual(TargetStatus.Fail, Parse("K,1,2,3,4,5,6,7,1,2,3,4,5,6,7,F\n").status);
            Assert.AreEqual(TargetStatus.Success, Parse("K,1,2,3,4,5,6,7,1,2,3,4,5,6,7,S,ignored,1.5").status);
        }

        [Test]
        public void Kinematics_TooFewOrBadFieldsFail()
        {
            ParseFail("K,1,2,3,4,5,6,7,1,2,3,4,5,6");
            ParseFail("K,1,2,3,4,5,6,7,1,2,3,4,5,6,x");
            ParseFail("K,1,2,3,oops,5,6,7,1,2,3,4,5,6,7");
        }

        [Test]
        public void Kinematics_RoundTripThroughFormatter()
        {
            var target = new UpperLimbAngles();
            var input = new UpperLimbAngles();
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                target[i] = i * 1.5f - 3.25f;
                input[i] = i * -2.25f + 0.125f;
            }

            foreach (TargetStatus status in new[] { TargetStatus.None, TargetStatus.Success, TargetStatus.Fail })
            {
                string line = UdpMessageParser.FormatKinematics(target, input, status);
                UdpMessage m = Parse(line);
                Assert.AreEqual(UdpMessageType.Kinematics, m.type);
                Assert.AreEqual(status, m.status);
                for (int i = 0; i < UpperLimbAngles.Count; i++)
                {
                    Assert.AreEqual(target[i], m.targetAngles[i], Tol);
                    Assert.AreEqual(input[i], m.inputAngles[i], Tol);
                }
            }
        }

        [Test]
        public void LegacyTest_RoundTripThroughFormatter()
        {
            string line = UdpMessageParser.FormatLegacyTest(10.5f, 20f, 30.25f, 99f, TargetStatus.Fail);
            UdpMessage m = Parse(line);
            Assert.AreEqual(UdpMessageType.LegacyTest, m.type);
            Assert.AreEqual(10.5f, m.t1, Tol);
            Assert.AreEqual(20f, m.t2, Tol);
            Assert.AreEqual(30.25f, m.i1, Tol);
            Assert.AreEqual(99f, m.i2, Tol);
            Assert.AreEqual(TargetStatus.Fail, m.status);
            Assert.AreEqual("T,50,50,50,50", UdpMessageParser.FormatLegacyTest(50, 50, 50, 50, TargetStatus.None));
        }

        [Test]
        public void Event_NameOnly()
        {
            UdpMessage m = Parse("E,TRIAL_START");
            Assert.AreEqual(UdpMessageType.Event, m.type);
            Assert.AreEqual("TRIAL_START", m.eventName);
            Assert.IsTrue(float.IsNaN(m.eventValue));
            Assert.AreEqual(string.Empty, m.eventDetail);
        }

        [Test]
        public void Event_WithValue()
        {
            UdpMessage m = Parse("E,TARGET_START,3\r\n");
            Assert.AreEqual("TARGET_START", m.eventName);
            Assert.AreEqual(3f, m.eventValue, Tol);
            Assert.AreEqual(string.Empty, m.eventDetail);
        }

        [Test]
        public void Event_DetailMayContainCommas()
        {
            UdpMessage m = Parse("E,MARK,1.5,subject moved, then paused, ok\n");
            Assert.AreEqual("MARK", m.eventName);
            Assert.AreEqual(1.5f, m.eventValue, Tol);
            Assert.AreEqual("subject moved, then paused, ok", m.eventDetail);
        }

        [Test]
        public void Event_DetailWithoutValue()
        {
            UdpMessage m = Parse("E,MARK,,just a note");
            Assert.IsTrue(float.IsNaN(m.eventValue));
            Assert.AreEqual("just a note", m.eventDetail);
        }

        [Test]
        public void Event_BadValueOrMissingNameFails()
        {
            ParseFail("E");
            ParseFail("E,");
            ParseFail("E,MARK,notanumber");
        }

        [Test]
        public void Query_Parses()
        {
            Assert.AreEqual(UdpMessageType.Query, Parse("Q").type);
            Assert.AreEqual(UdpMessageType.Query, Parse("Q\n").type);
            Assert.AreEqual(UdpMessageType.Query, Parse("Q,hello").type);
        }

        [Test]
        public void Unknown_KeepsRawText()
        {
            UdpMessage m = ParseFail("hello world\n");
            Assert.AreEqual("hello world", m.raw);
            ParseFail("Z,1,2,3");
            ParseFail("");
            ParseFail("   \r\n");
            Assert.IsFalse(UdpMessageParser.TryParse(null, out UdpMessage n));
            Assert.IsNotNull(n);
            Assert.AreEqual(UdpMessageType.Unknown, n.type);
        }

        [Test]
        public void ToString_NeverThrows()
        {
            string[] samples =
            {
                "C,R", "C,N,-70,70,-20,90,10", "T,1,2,3,4,S", "K,1,2,3,4,5,6,7,1,2,3,4,5,6,7,F",
                "E,MARK,1,x", "Q", "garbage"
            };
            foreach (string s in samples)
            {
                UdpMessageParser.TryParse(s, out UdpMessage m);
                Assert.IsFalse(string.IsNullOrEmpty(m.ToString()));
            }
        }

        [Test]
        public void Parsing_IsCultureInvariant()
        {
            CultureInfo old = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                UdpMessage m = Parse("T,1.5,2.5,3.5,4.5");
                Assert.AreEqual(1.5f, m.t1, Tol);
                Assert.AreEqual(4.5f, m.i2, Tol);

                string line = UdpMessageParser.FormatLegacyTest(1.5f, 2.5f, 3.5f, 4.5f, TargetStatus.None);
                Assert.AreEqual("T,1.5,2.5,3.5,4.5", line);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = old;
            }
        }
    }
}
