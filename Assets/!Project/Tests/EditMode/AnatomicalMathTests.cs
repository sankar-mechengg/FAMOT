using System;
using FAMOT.Core;
using NUnit.Framework;
using UnityEngine;

namespace FAMOT.Tests
{
    /// <summary>Edit-mode tests for <see cref="AnatomicalMath"/>.</summary>
    public class AnatomicalMathTests
    {
        private const float Tol = 1e-4f;

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Rotation angle (degrees) between two rotations. Computed from the vector part of a^-1 * b, which is
        /// well conditioned for tiny angles (unlike acos(dot), which Quaternion.Angle uses).
        /// </summary>
        private static float AngleBetween(Quaternion a, Quaternion b)
        {
            Quaternion d = Quaternion.Inverse(a) * b;
            float mag = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
            return 2f * Mathf.Atan2(mag, Mathf.Abs(d.w)) * Mathf.Rad2Deg;
        }

        private static Quaternion RandomRotation(System.Random rng)
        {
            while (true)
            {
                var q = new Quaternion(
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1),
                    (float)(rng.NextDouble() * 2 - 1));
                float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
                if (m < 0.1f) continue;
                return new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
            }
        }

        private static Vector3 RandomVector(System.Random rng)
        {
            return new Vector3(
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1),
                (float)(rng.NextDouble() * 2 - 1));
        }

        private static Vector3 RandomUnit(System.Random rng)
        {
            while (true)
            {
                Vector3 v = RandomVector(rng);
                if (v.magnitude > 0.1f) return v.normalized;
            }
        }

        private static void AssertVec(Vector3 expected, Vector3 actual, float tol = Tol)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tol), "x of " + actual + " vs " + expected);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tol), "y of " + actual + " vs " + expected);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tol), "z of " + actual + " vs " + expected);
        }

        private static Vector3 Reflect(Vector3 v, Vector3 unitNormal)
        {
            return v - 2f * Vector3.Dot(v, unitNormal) * unitNormal;
        }

        // ------------------------------------------------------------------ WrapDegrees

        [TestCase(0f, 0f)]
        [TestCase(360f, 0f)]
        [TestCase(181f, -179f)]
        [TestCase(-180f, 180f)]
        [TestCase(180f, 180f)]
        [TestCase(540f, 180f)]
        [TestCase(-181f, 179f)]
        [TestCase(-360f, 0f)]
        [TestCase(720f + 45f, 45f)]
        [TestCase(-90f, -90f)]
        public void WrapDegrees_MapsToHalfOpenInterval(float input, float expected)
        {
            Assert.That(AnatomicalMath.WrapDegrees(input), Is.EqualTo(expected).Within(Tol));
        }

        [Test]
        public void WrapDegrees_AlwaysWithinRange()
        {
            for (float d = -1000f; d <= 1000f; d += 7.3f)
            {
                float w = AnatomicalMath.WrapDegrees(d);
                Assert.That(w, Is.GreaterThan(-180f - Tol).And.LessThanOrEqualTo(180f + Tol), "input " + d);
                // Must be congruent to the input modulo 360.
                float diff = Mathf.Repeat(d - w + 180f, 360f) - 180f;
                Assert.That(diff, Is.EqualTo(0f).Within(1e-3f), "input " + d);
            }
        }

        // ------------------------------------------------------------------ TwistDegrees

        [TestCase(30f)]
        [TestCase(-30f)]
        [TestCase(90f)]
        [TestCase(-120f)]
        [TestCase(0f)]
        public void TwistDegrees_PureTwistAboutAxis_ReturnsAngle(float angle)
        {
            Vector3 axis = new Vector3(0.3f, 0.8f, -0.5f).normalized;
            Quaternion q = Quaternion.AngleAxis(angle, axis);
            Assert.That(AnatomicalMath.TwistDegrees(q, axis), Is.EqualTo(angle).Within(1e-3f));
        }

        [Test]
        public void TwistDegrees_AxisIsNormalisedInternally()
        {
            Quaternion q = Quaternion.AngleAxis(30f, Vector3.up);
            Assert.That(AnatomicalMath.TwistDegrees(q, new Vector3(0f, 5f, 0f)), Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void TwistDegrees_RotationAboutPerpendicularAxis_IsZero()
        {
            Quaternion q = Quaternion.AngleAxis(30f, Vector3.right);
            Assert.That(AnatomicalMath.TwistDegrees(q, Vector3.up), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(AnatomicalMath.TwistDegrees(q, Vector3.forward), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void TwistDegrees_SignFollowsAxisDirection()
        {
            Quaternion q = Quaternion.AngleAxis(40f, Vector3.up);
            Assert.That(AnatomicalMath.TwistDegrees(q, Vector3.up), Is.EqualTo(40f).Within(1e-3f));
            Assert.That(AnatomicalMath.TwistDegrees(q, Vector3.down), Is.EqualTo(-40f).Within(1e-3f));
        }

        [Test]
        public void TwistDegrees_WrapsLargeAngles()
        {
            Quaternion q = Quaternion.AngleAxis(200f, Vector3.up);
            Assert.That(AnatomicalMath.TwistDegrees(q, Vector3.up), Is.EqualTo(-160f).Within(1e-2f));
        }

        [Test]
        public void TwistDegrees_SwingFollowedByTwist_RecoversTwist()
        {
            // q = swing * twist, twist about the axis, swing about a perpendicular axis.
            Vector3 axis = Vector3.up;
            Quaternion q = Quaternion.AngleAxis(50f, Vector3.right) * Quaternion.AngleAxis(25f, axis);
            Assert.That(AnatomicalMath.TwistDegrees(q, axis), Is.EqualTo(25f).Within(1e-3f));
        }

        // ------------------------------------------------------------------ SwingTwist

        [Test]
        public void SwingTwist_Recomposes_ForRandomQuaternions()
        {
            var rng = new System.Random(1234);
            for (int i = 0; i < 100; i++)
            {
                Quaternion q = RandomRotation(rng);
                Vector3 axis = RandomUnit(rng);
                AnatomicalMath.SwingTwist(q, axis, out Quaternion swing, out Quaternion twist);

                Quaternion recomposed = swing * twist;
                // q and -q are the same rotation; compare via the rotation angle between them.
                Assert.That(AngleBetween(q, recomposed), Is.LessThan(1e-2f), "sample " + i);
                Assert.That(Mathf.Abs(Quaternion.Dot(q, recomposed)), Is.EqualTo(1f).Within(Tol), "sample " + i);
            }
        }

        [Test]
        public void SwingTwist_TwistIsAboutAxis_AndSwingHasNoTwist()
        {
            var rng = new System.Random(99);
            for (int i = 0; i < 100; i++)
            {
                Quaternion q = RandomRotation(rng);
                Vector3 axis = RandomUnit(rng);
                AnatomicalMath.SwingTwist(q, axis, out Quaternion swing, out Quaternion twist);

                // The twist's vector part is parallel to the axis.
                Vector3 tv = new Vector3(twist.x, twist.y, twist.z);
                Assert.That(Vector3.Cross(tv, axis).magnitude, Is.EqualTo(0f).Within(Tol), "sample " + i);
                // The twist is a unit quaternion.
                Assert.That(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w,
                    Is.EqualTo(1f).Within(Tol), "sample " + i);
                // Applying the twist leaves the axis fixed.
                AssertVec(axis, twist * axis, 1e-3f);
                // The swing carries no twist about the axis.
                Assert.That(AnatomicalMath.TwistDegrees(swing, axis), Is.EqualTo(0f).Within(1e-2f), "sample " + i);
            }
        }

        [Test]
        public void SwingTwist_TwistAngleMatchesTwistDegrees()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < 50; i++)
            {
                Quaternion q = RandomRotation(rng);
                Vector3 axis = RandomUnit(rng);
                AnatomicalMath.SwingTwist(q, axis, out _, out Quaternion twist);
                float expected = AnatomicalMath.TwistDegrees(q, axis);
                float actual = AnatomicalMath.TwistDegrees(twist, axis);
                Assert.That(AnatomicalMath.WrapDegrees(actual - expected), Is.EqualTo(0f).Within(1e-2f), "sample " + i);
            }
        }

        [Test]
        public void SwingTwist_PureTwist_HasIdentitySwing()
        {
            Quaternion q = Quaternion.AngleAxis(70f, Vector3.forward);
            AnatomicalMath.SwingTwist(q, Vector3.forward, out Quaternion swing, out Quaternion twist);
            Assert.That(AngleBetween(Quaternion.identity, swing), Is.LessThan(1e-2f));
            Assert.That(AngleBetween(q, twist), Is.LessThan(1e-2f));
        }

        // ------------------------------------------------------------------ SignedAngleAbout

        [Test]
        public void SignedAngleAbout_BasicSigns()
        {
            Assert.That(AnatomicalMath.SignedAngleAbout(Vector3.right, Vector3.up, Vector3.forward), Is.EqualTo(90f).Within(1e-3f));
            Assert.That(AnatomicalMath.SignedAngleAbout(Vector3.up, Vector3.right, Vector3.forward), Is.EqualTo(-90f).Within(1e-3f));
            Assert.That(AnatomicalMath.SignedAngleAbout(Vector3.right, Vector3.right, Vector3.forward), Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void SignedAngleAbout_FlippingAxisFlipsSign()
        {
            float a = AnatomicalMath.SignedAngleAbout(Vector3.right, Vector3.up, Vector3.forward);
            float b = AnatomicalMath.SignedAngleAbout(Vector3.right, Vector3.up, Vector3.back);
            Assert.That(b, Is.EqualTo(-a).Within(1e-3f));
        }

        [Test]
        public void SignedAngleAbout_IgnoresComponentsAlongAxis()
        {
            float a = AnatomicalMath.SignedAngleAbout(new Vector3(1f, 0f, 5f), new Vector3(0f, 1f, -3f), Vector3.forward);
            Assert.That(a, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void SignedAngleAbout_AxisIsNormalisedInternally()
        {
            float a = AnatomicalMath.SignedAngleAbout(Vector3.right, Vector3.up, new Vector3(0f, 0f, 10f));
            Assert.That(a, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void SignedAngleAbout_VectorParallelToAxis_ReturnsZero()
        {
            Assert.That(AnatomicalMath.SignedAngleAbout(Vector3.forward, Vector3.up, Vector3.forward), Is.EqualTo(0f));
            Assert.That(AnatomicalMath.SignedAngleAbout(Vector3.up, Vector3.forward, Vector3.forward), Is.EqualTo(0f));
        }

        [Test]
        public void SignedAngleAbout_MatchesAngleAxisRotation()
        {
            var rng = new System.Random(5);
            for (int i = 0; i < 50; i++)
            {
                Vector3 axis = RandomUnit(rng);
                Vector3 from = Vector3.ProjectOnPlane(RandomVector(rng), axis);
                if (from.magnitude < 0.1f) continue;
                float angle = (float)(rng.NextDouble() * 340 - 170);
                Vector3 to = Quaternion.AngleAxis(angle, axis) * from;
                Assert.That(AnatomicalMath.SignedAngleAbout(from, to, axis), Is.EqualTo(angle).Within(0.1f), "sample " + i);
            }
        }

        // ------------------------------------------------------------------ BasisFromXY

        [Test]
        public void BasisFromXY_MapsAxesOntoGivenDirections()
        {
            var rng = new System.Random(21);
            for (int i = 0; i < 50; i++)
            {
                Vector3 y = RandomUnit(rng);
                Vector3 x = Vector3.ProjectOnPlane(RandomVector(rng), y);
                if (x.magnitude < 0.1f) continue;
                x.Normalize();

                Quaternion q = AnatomicalMath.BasisFromXY(x, y);
                AssertVec(x, q * Vector3.right, 1e-3f);
                AssertVec(y, q * Vector3.up, 1e-3f);
                AssertVec(Vector3.Cross(x, y), q * Vector3.forward, 1e-3f);
            }
        }

        [Test]
        public void BasisFromXY_KnownCase_NegativeRightAndForward()
        {
            // The configuration used by the right forearm at rest: X = -right, Y = forward  =>  Z = up.
            Quaternion q = AnatomicalMath.BasisFromXY(Vector3.left, Vector3.forward);
            AssertVec(Vector3.left, q * Vector3.right);
            AssertVec(Vector3.forward, q * Vector3.up);
            AssertVec(Vector3.up, q * Vector3.forward);
        }

        [Test]
        public void BasisFromXY_OrthogonalisesNonPerpendicularX()
        {
            // y = up (authoritative); x has a component along y which must be removed.
            Quaternion q = AnatomicalMath.BasisFromXY(new Vector3(1f, 1f, 0f), Vector3.up);
            AssertVec(Vector3.right, q * Vector3.right);
            AssertVec(Vector3.up, q * Vector3.up);
            AssertVec(Vector3.Cross(Vector3.right, Vector3.up), q * Vector3.forward);
        }

        [Test]
        public void BasisFromXY_YIsAuthoritative_AndResultIsProperRotation()
        {
            Vector3 y = new Vector3(0.2f, 0.9f, -0.3f);
            Vector3 x = new Vector3(0.7f, 0.1f, 0.6f); // not perpendicular to y
            Quaternion q = AnatomicalMath.BasisFromXY(x, y);
            AssertVec(y.normalized, q * Vector3.up, 1e-3f);
            // Rotation axes stay orthonormal and right-handed.
            Vector3 qx = q * Vector3.right, qy = q * Vector3.up, qz = q * Vector3.forward;
            Assert.That(Vector3.Dot(qx, qy), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(Vector3.Dot(qx, qz), Is.EqualTo(0f).Within(1e-3f));
            AssertVec(qz, Vector3.Cross(qx, qy), 1e-3f);
            // x was only orthogonalised, so it keeps the part of x perpendicular to y.
            AssertVec(Vector3.ProjectOnPlane(x, y).normalized, qx, 1e-3f);
        }

        // ------------------------------------------------------------------ EulerYXZ / ToEulerYXZ

        [Test]
        public void EulerYXZ_MatchesUnityEuler()
        {
            // Quaternion.Euler is documented as Z, then X, then Y (extrinsic), i.e. Ry * Rx * Rz.
            Quaternion a = AnatomicalMath.EulerYXZ(20f, -40f, 65f);
            Quaternion b = Quaternion.Euler(20f, -40f, 65f);
            Assert.That(AngleBetween(a, b), Is.LessThan(0.01f));
        }

        [Test]
        public void ToEulerYXZ_RoundTrip_OverGrid()
        {
            float[] xs = { -80f, -45f, -10f, 0f, 10f, 45f, 80f };
            float[] yzs = { -170f, -120f, -90f, -30f, 0f, 30f, 90f, 120f, 170f };
            foreach (float x in xs)
            foreach (float y in yzs)
            foreach (float z in yzs)
            {
                Quaternion q = AnatomicalMath.EulerYXZ(x, y, z);
                Vector3 e = AnatomicalMath.ToEulerYXZ(q);
                Quaternion q2 = AnatomicalMath.EulerYXZ(e.x, e.y, e.z);
                Assert.That(AngleBetween(q, q2), Is.LessThan(0.01f), $"x={x} y={y} z={z} -> {e}");
                Assert.That(e.x, Is.InRange(-90f, 90f), $"x={x} y={y} z={z}");
            }
        }

        [Test]
        public void ToEulerYXZ_ReturnsOriginalAngles_WithinSafeRange()
        {
            // Inside the unambiguous range the angles themselves are recovered, not just an equivalent rotation.
            float[] xs = { -60f, 0f, 35f };
            float[] yzs = { -150f, -50f, 0f, 25f, 140f };
            foreach (float x in xs)
            foreach (float y in yzs)
            foreach (float z in yzs)
            {
                Vector3 e = AnatomicalMath.ToEulerYXZ(AnatomicalMath.EulerYXZ(x, y, z));
                Assert.That(e.x, Is.EqualTo(x).Within(1e-2f), $"x={x} y={y} z={z}");
                Assert.That(AnatomicalMath.WrapDegrees(e.y - y), Is.EqualTo(0f).Within(1e-2f), $"x={x} y={y} z={z}");
                Assert.That(AnatomicalMath.WrapDegrees(e.z - z), Is.EqualTo(0f).Within(1e-2f), $"x={x} y={y} z={z}");
            }
        }

        [Test]
        public void ToEulerYXZ_GimbalLock_StillRepresentsSameRotation()
        {
            foreach (float xDeg in new[] { 90f, -90f })
            {
                Quaternion q = AnatomicalMath.EulerYXZ(xDeg, 30f, 20f);
                Vector3 e = AnatomicalMath.ToEulerYXZ(q);
                Quaternion q2 = AnatomicalMath.EulerYXZ(e.x, e.y, e.z);
                Assert.That(AngleBetween(q, q2), Is.LessThan(0.1f), "x=" + xDeg);
            }
        }

        // ------------------------------------------------------------------ MirrorConjugate

        [Test]
        public void MirrorConjugate_ActsAsConjugationByReflection()
        {
            var rng = new System.Random(314);
            Vector3[] normals =
            {
                Vector3.right, Vector3.up, Vector3.forward,
                new Vector3(1f, 2f, -3f).normalized,
                new Vector3(-0.2f, 0.5f, 0.1f).normalized
            };
            foreach (Vector3 n in normals)
            {
                for (int i = 0; i < 20; i++)
                {
                    Quaternion q = RandomRotation(rng);
                    Vector3 v = RandomVector(rng);
                    Quaternion qm = AnatomicalMath.MirrorConjugate(q, n);

                    Vector3 expected = Reflect(q * Reflect(v, n), n);
                    AssertVec(expected, qm * v, 1e-4f);
                    // Result must remain a unit quaternion.
                    Assert.That(qm.x * qm.x + qm.y * qm.y + qm.z * qm.z + qm.w * qm.w, Is.EqualTo(1f).Within(1e-4f));
                }
            }
        }

        [Test]
        public void MirrorConjugate_NormalIsNormalisedInternally()
        {
            Quaternion q = Quaternion.Euler(10f, 50f, -20f);
            Quaternion a = AnatomicalMath.MirrorConjugate(q, new Vector3(0f, 0f, 1f));
            Quaternion b = AnatomicalMath.MirrorConjugate(q, new Vector3(0f, 0f, 7f));
            Assert.That(AngleBetween(a, b), Is.LessThan(1e-3f));
        }

        [Test]
        public void MirrorConjugate_IsAnInvolution_AndKeepsIdentity()
        {
            Vector3 n = new Vector3(0.3f, -0.4f, 0.8f).normalized;
            Quaternion q = Quaternion.Euler(33f, -71f, 12f);
            Quaternion twice = AnatomicalMath.MirrorConjugate(AnatomicalMath.MirrorConjugate(q, n), n);
            Assert.That(AngleBetween(q, twice), Is.LessThan(1e-2f));
            Assert.That(AngleBetween(Quaternion.identity, AnatomicalMath.MirrorConjugate(Quaternion.identity, n)), Is.LessThan(1e-3f));
        }

        [Test]
        public void MirrorConjugate_AcrossXNormal_MatchesDefinitionOnConcreteVector()
        {
            Quaternion rx = Quaternion.AngleAxis(30f, Vector3.right);
            Quaternion ry = Quaternion.AngleAxis(30f, Vector3.up);
            Quaternion rxm = AnatomicalMath.MirrorConjugate(rx, Vector3.right);
            Quaternion rym = AnatomicalMath.MirrorConjugate(ry, Vector3.right);
            Vector3 v = new Vector3(0.2f, 0.5f, 0.9f);
            AssertVec(Reflect(rx * Reflect(v, Vector3.right), Vector3.right), rxm * v);
            AssertVec(Reflect(ry * Reflect(v, Vector3.right), Vector3.right), rym * v);
            // Reflecting across the YZ plane reverses the sense of a rotation about Y (but not about X).
            AssertVec(Quaternion.AngleAxis(-30f, Vector3.up) * v, rym * v);
            AssertVec(rx * v, rxm * v);
        }

        // ------------------------------------------------------------------ Humerus direction <-> shoulder angles

        [Test]
        public void HumerusDirectionBody_SpecificCases()
        {
            AssertVec(new Vector3(0f, -1f, 0f), AnatomicalMath.HumerusDirectionBody(0f, 0f, 1f));
            AssertVec(new Vector3(0f, -1f, 0f), AnatomicalMath.HumerusDirectionBody(0f, 0f, -1f));
            AssertVec(new Vector3(0f, 0f, 1f), AnatomicalMath.HumerusDirectionBody(90f, 0f, 1f));
            AssertVec(new Vector3(0f, 0f, 1f), AnatomicalMath.HumerusDirectionBody(90f, 0f, -1f));
            AssertVec(new Vector3(1f, 0f, 0f), AnatomicalMath.HumerusDirectionBody(0f, 90f, 1f));
            AssertVec(new Vector3(-1f, 0f, 0f), AnatomicalMath.HumerusDirectionBody(0f, 90f, -1f));
        }

        [Test]
        public void HumerusDirectionBody_IsUnitLength()
        {
            for (float f = -60f; f <= 150f; f += 30f)
            for (float a = -40f; a <= 80f; a += 20f)
            {
                Assert.That(AnatomicalMath.HumerusDirectionBody(f, a, 1f).magnitude, Is.EqualTo(1f).Within(Tol));
                Assert.That(AnatomicalMath.HumerusDirectionBody(f, a, -1f).magnitude, Is.EqualTo(1f).Within(Tol));
            }
        }

        [Test]
        public void HumerusDirectionBody_FlexionRaisesArmForward_ExtensionMovesBackward()
        {
            Assert.That(AnatomicalMath.HumerusDirectionBody(45f, 0f, 1f).z, Is.GreaterThan(0.5f));
            Assert.That(AnatomicalMath.HumerusDirectionBody(-30f, 0f, 1f).z, Is.LessThan(-0.3f));
            // Full overhead flexion points up.
            Assert.That(AnatomicalMath.HumerusDirectionBody(180f, 0f, 1f).y, Is.EqualTo(1f).Within(Tol));
        }

        [Test]
        public void HumerusDirection_ShoulderAngles_RoundTrip_BothSides()
        {
            foreach (float ls in new[] { 1f, -1f })
            {
                for (float flex = -60f; flex <= 150f; flex += 15f)
                for (float abd = -40f; abd <= 80f; abd += 10f)
                {
                    Vector3 d = AnatomicalMath.HumerusDirectionBody(flex, abd, ls);
                    Vector2 back = AnatomicalMath.ShoulderAnglesFromDirectionBody(d, ls);
                    Assert.That(back.x, Is.EqualTo(flex).Within(1e-2f), $"flex={flex} abd={abd} ls={ls}");
                    Assert.That(back.y, Is.EqualTo(abd).Within(1e-2f), $"flex={flex} abd={abd} ls={ls}");
                }
            }
        }

        [Test]
        public void ShoulderAnglesFromDirectionBody_NormalisesInput()
        {
            Vector3 d = AnatomicalMath.HumerusDirectionBody(40f, 25f, 1f) * 3.7f;
            Vector2 a = AnatomicalMath.ShoulderAnglesFromDirectionBody(d, 1f);
            Assert.That(a.x, Is.EqualTo(40f).Within(1e-2f));
            Assert.That(a.y, Is.EqualTo(25f).Within(1e-2f));
        }

        [Test]
        public void ShoulderAnglesFromDirectionBody_SpecificCases()
        {
            Vector2 down = AnatomicalMath.ShoulderAnglesFromDirectionBody(Vector3.down, 1f);
            Assert.That(down.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(down.y, Is.EqualTo(0f).Within(1e-3f));

            Vector2 fwd = AnatomicalMath.ShoulderAnglesFromDirectionBody(Vector3.forward, 1f);
            Assert.That(fwd.x, Is.EqualTo(90f).Within(1e-3f));

            // Pointing out to the right: abduction +90 for the right arm, -90 for the left arm.
            Assert.That(AnatomicalMath.ShoulderAnglesFromDirectionBody(Vector3.right, 1f).y, Is.EqualTo(90f).Within(1e-3f));
            Assert.That(AnatomicalMath.ShoulderAnglesFromDirectionBody(Vector3.right, -1f).y, Is.EqualTo(-90f).Within(1e-3f));
        }
    }
}
