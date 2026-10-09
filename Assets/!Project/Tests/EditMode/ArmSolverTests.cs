using System;
using FAMOT.Core;
using FAMOT.Kinematics;
using NUnit.Framework;
using UnityEngine;

namespace FAMOT.Tests
{
    /// <summary>Edit-mode tests for <see cref="ArmSolver"/> and <see cref="ArmRestPose"/>.</summary>
    public class ArmSolverTests
    {
        private const float PosTol = 1e-4f;
        private const float DirTol = 1e-4f;

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Synthetic arm: body axes = world axes, humerus mostly down (slightly lateral and forward),
        /// forearm pointing forward, all bone rotations identity. Left arm is the x-mirrored right arm.
        /// </summary>
        private static ArmRestPose MakeArm(ArmSide side)
        {
            float m = side == ArmSide.Right ? 1f : -1f;
            var rest = new ArmRestPose
            {
                side = side,
                shoulderPos = new Vector3(0.2f * m, 1.3f, 0f),
                elbowPos = new Vector3(0.25f * m, 1.05f, 0.1f),
                wristPos = new Vector3(0.25f * m, 1.05f, 0.37f),
                handTipPos = new Vector3(0.25f * m, 1.05f, 0.45f),
                upperArmRot = Quaternion.identity,
                upperArmTwistRot = Quaternion.identity,
                forearmRot = Quaternion.identity,
                forearmTwistRot = Quaternion.identity,
                handRot = Quaternion.identity
            };
            rest.Finalize();
            return rest;
        }

        private static float AngleBetween(Quaternion a, Quaternion b)
        {
            Quaternion d = Quaternion.Inverse(a) * b;
            float mag = Mathf.Sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
            return 2f * Mathf.Atan2(mag, Mathf.Abs(d.w)) * Mathf.Rad2Deg;
        }

        private static void AssertVec(Vector3 expected, Vector3 actual, float tol, string msg = "")
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tol), "x: " + actual + " vs " + expected + " " + msg);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tol), "y: " + actual + " vs " + expected + " " + msg);
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tol), "z: " + actual + " vs " + expected + " " + msg);
        }

        private static void AssertAnglesClose(UpperLimbAngles expected, UpperLimbAngles actual, float tolDeg, string msg = "")
        {
            for (int i = 0; i < UpperLimbAngles.Count; i++)
            {
                float diff = AnatomicalMath.WrapDegrees(actual[i] - expected[i]);
                Assert.That(diff, Is.EqualTo(0f).Within(tolDeg),
                    $"{UpperLimbAngles.Names[i]}: expected {expected[i]:F3}, got {actual[i]:F3}. {msg}\nexpected [{expected}]\nactual   [{actual}]");
            }
        }

        private static float Uniform(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        private static UpperLimbAngles RandomAngles(System.Random rng)
        {
            return new UpperLimbAngles
            {
                shoulderFlexion = Uniform(rng, -30f, 80f),
                shoulderAbduction = Uniform(rng, -20f, 60f),
                shoulderRotation = Uniform(rng, -40f, 40f),
                elbowFlexion = Uniform(rng, -10f, 90f),
                forearmSupination = Uniform(rng, -60f, 60f),
                wristExtension = Uniform(rng, -50f, 50f),
                wristRadial = Uniform(rng, -20f, 20f)
            };
        }

        private static UpperLimbAngles Only(int index, float value)
        {
            var a = new UpperLimbAngles();
            a[index] = value;
            return a;
        }

        private static readonly ArmSide[] Sides = { ArmSide.Right, ArmSide.Left };

        // ------------------------------------------------------------------ Finalize

        [Test]
        public void Finalize_DerivedValues_RightArm()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);

            Assert.That(rest.isFinalized, Is.True);
            Assert.That(rest.upperArmLength, Is.EqualTo(0.27f).Within(0.01f));
            Assert.That(rest.upperArmLength, Is.EqualTo(Mathf.Sqrt(0.05f * 0.05f + 0.25f * 0.25f + 0.1f * 0.1f)).Within(PosTol));
            Assert.That(rest.forearmLength, Is.EqualTo(0.27f).Within(PosTol));
            Assert.That(rest.handLength, Is.EqualTo(0.08f).Within(PosTol));
            Assert.That(rest.ArmLength, Is.EqualTo(rest.upperArmLength + rest.forearmLength).Within(PosTol));

            Assert.That(rest.elbowRestDeg, Is.GreaterThan(0f));
            Assert.That(rest.elbowRestDeg, Is.EqualTo(Vector3.Angle(rest.humerusDir, rest.forearmDir)).Within(0.01f));

            AssertVec(Vector3.forward, rest.forearmDir, DirTol);
            AssertVec((rest.elbowPos - rest.shoulderPos).normalized, rest.humerusDir, DirTol);
        }

        [Test]
        public void Finalize_ForearmBasis_Axes_RightArm()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            // Y = long axis, Z = dorsal = world up, X = Y x Z = -right.
            AssertVec(rest.forearmDir, rest.forearmBasis * Vector3.up, DirTol);
            AssertVec(Vector3.up, rest.forearmBasis * Vector3.forward, DirTol);
            AssertVec(Vector3.left, rest.forearmBasis * Vector3.right, DirTol);
        }

        [Test]
        public void Finalize_ForearmBasis_Axes_LeftArm()
        {
            // The forearm points forward on both sides, so the basis is identical for both arms.
            ArmRestPose rest = MakeArm(ArmSide.Left);
            AssertVec(rest.forearmDir, rest.forearmBasis * Vector3.up, DirTol);
            AssertVec(Vector3.up, rest.forearmBasis * Vector3.forward, DirTol);
            AssertVec(Vector3.left, rest.forearmBasis * Vector3.right, DirTol);
        }

        [Test]
        public void Finalize_BodyAxes_AreOrthonormal_WorldAligned()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            AssertVec(Vector3.forward, rest.bodyForward, DirTol);
            AssertVec(Vector3.up, rest.bodyUp, DirTol);
            AssertVec(Vector3.right, rest.bodyRight, DirTol);
            AssertVec(Vector3.right, rest.BodyLateral, DirTol);
            AssertVec(Vector3.left, MakeArm(ArmSide.Left).BodyLateral, DirTol);
        }

        [Test]
        public void Finalize_ElbowAxis_IsUnit_AndPerpendicularToBothSegments()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Assert.That(rest.elbowAxis.magnitude, Is.EqualTo(1f).Within(DirTol));
                Assert.That(Vector3.Dot(rest.elbowAxis, rest.humerusDir), Is.EqualTo(0f).Within(DirTol));
                Assert.That(Vector3.Dot(rest.elbowAxis, rest.forearmDir), Is.EqualTo(0f).Within(DirTol));
            }
        }

        [Test]
        public void Finalize_ShoulderRestAngles_ReproduceHumerusDirection()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 body = AnatomicalMath.HumerusDirectionBody(rest.shoulderRestAngles.x, rest.shoulderRestAngles.y, rest.LateralSign);
                AssertVec(rest.humerusDir, rest.BodyToWorld(body), DirTol);
                // Humerus is slightly forward and slightly lateral for both arms: positive flexion and abduction.
                Assert.That(rest.shoulderRestAngles.x, Is.GreaterThan(0f));
                Assert.That(rest.shoulderRestAngles.y, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void Finalize_LeftArm_IsMirrorOfRightArm()
        {
            ArmRestPose r = MakeArm(ArmSide.Right);
            ArmRestPose l = MakeArm(ArmSide.Left);
            Assert.That(l.upperArmLength, Is.EqualTo(r.upperArmLength).Within(PosTol));
            Assert.That(l.forearmLength, Is.EqualTo(r.forearmLength).Within(PosTol));
            Assert.That(l.elbowRestDeg, Is.EqualTo(r.elbowRestDeg).Within(0.01f));
            Assert.That(l.shoulderRestAngles.x, Is.EqualTo(r.shoulderRestAngles.x).Within(0.01f));
            Assert.That(l.shoulderRestAngles.y, Is.EqualTo(r.shoulderRestAngles.y).Within(0.01f));
            AssertVec(new Vector3(-r.humerusDir.x, r.humerusDir.y, r.humerusDir.z), l.humerusDir, DirTol);
        }

        // ------------------------------------------------------------------ Forward: rest reproduction

        [Test]
        public void Forward_Zero_ReproducesRestPose()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, UpperLimbAngles.Zero);

                AssertVec(rest.shoulderPos, r.shoulderPos, PosTol, side.ToString());
                AssertVec(rest.elbowPos, r.elbowPos, PosTol, side.ToString());
                AssertVec(rest.wristPos, r.wristPos, PosTol, side.ToString());
                Assert.That(AngleBetween(rest.handRot, r.handRot), Is.LessThan(0.01f), side.ToString());
                Assert.That(AngleBetween(rest.upperArmRot, r.upperArmRot), Is.LessThan(0.01f), side.ToString());
                Assert.That(AngleBetween(rest.forearmRot, r.forearmRot), Is.LessThan(0.01f), side.ToString());
                Assert.That(AngleBetween(rest.forearmBasis, r.forearmBasis), Is.LessThan(0.01f), side.ToString());
                Assert.That(r.targetClamped, Is.False);
                for (int i = 0; i < UpperLimbAngles.Count; i++)
                    Assert.That(r.angles[i], Is.EqualTo(0f), UpperLimbAngles.Names[i]);
            }
        }

        [Test]
        public void Decompose_RestPose_GivesZeroAngles()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                UpperLimbAngles a = ArmSolver.Decompose(rest, rest.upperArmRot, rest.forearmRot, rest.handRot);
                AssertAnglesClose(UpperLimbAngles.Zero, a, 0.05f, side.ToString());
            }
        }

        [Test]
        public void Forward_SegmentLengthsArePreserved()
        {
            var rng = new System.Random(11);
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                for (int i = 0; i < 30; i++)
                {
                    ArmPoseResult r = ArmSolver.Forward(rest, RandomAngles(rng));
                    Assert.That(Vector3.Distance(r.shoulderPos, r.elbowPos), Is.EqualTo(rest.upperArmLength).Within(PosTol));
                    Assert.That(Vector3.Distance(r.elbowPos, r.wristPos), Is.EqualTo(rest.forearmLength).Within(PosTol));
                }
            }
        }

        // ------------------------------------------------------------------ Forward: elbow

        [Test]
        public void Forward_ElbowFlexion_MovesWristUp_KeepsForearmLength()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, Only(3, +30f));

                // Elbow itself does not move (no shoulder change).
                AssertVec(rest.elbowPos, r.elbowPos, PosTol, side.ToString());
                Assert.That(r.wristPos.y, Is.GreaterThan(rest.wristPos.y + 0.05f), side.ToString());
                Assert.That(Vector3.Distance(r.wristPos, r.elbowPos), Is.EqualTo(rest.forearmLength).Within(PosTol), side.ToString());
                // The bend angle between segments grows by exactly the flexion.
                float bend = Vector3.Angle(r.elbowPos - r.shoulderPos, r.wristPos - r.elbowPos);
                Assert.That(bend, Is.EqualTo(rest.elbowRestDeg + 30f).Within(0.05f), side.ToString());
            }
        }

        [Test]
        public void Forward_NegativeElbowFlexion_MovesWristDown()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, Only(3, -20f));
                Assert.That(r.wristPos.y, Is.LessThan(rest.wristPos.y - 0.03f), side.ToString());
                Assert.That(Vector3.Distance(r.wristPos, r.elbowPos), Is.EqualTo(rest.forearmLength).Within(PosTol), side.ToString());
                float bend = Vector3.Angle(r.elbowPos - r.shoulderPos, r.wristPos - r.elbowPos);
                Assert.That(bend, Is.EqualTo(rest.elbowRestDeg - 20f).Within(0.05f), side.ToString());
            }
        }

        // ------------------------------------------------------------------ Forward: shoulder

        [Test]
        public void Forward_ShoulderFlexion_RaisesElbowForwardAndUp()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, Only(0, +45f));
                Assert.That(r.elbowPos.z, Is.GreaterThan(rest.elbowPos.z + 0.05f), side.ToString());
                Assert.That(r.elbowPos.y, Is.GreaterThan(rest.elbowPos.y + 0.03f), side.ToString());
                Assert.That(Vector3.Distance(r.elbowPos, r.shoulderPos), Is.EqualTo(rest.upperArmLength).Within(PosTol), side.ToString());
            }
        }

        [Test]
        public void Forward_ShoulderExtension_MovesElbowBackward()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, Only(0, -30f));
                Assert.That(r.elbowPos.z, Is.LessThan(rest.elbowPos.z - 0.05f), side.ToString());
            }
        }

        [Test]
        public void Forward_ShoulderAbduction_MovesElbowLaterally()
        {
            ArmRestPose right = MakeArm(ArmSide.Right);
            ArmRestPose left = MakeArm(ArmSide.Left);
            ArmPoseResult rr = ArmSolver.Forward(right, Only(1, +30f));
            ArmPoseResult lr = ArmSolver.Forward(left, Only(1, +30f));

            // Right arm: +x is lateral; left arm: -x is lateral.
            Assert.That(rr.elbowPos.x, Is.GreaterThan(right.elbowPos.x + 0.05f));
            Assert.That(lr.elbowPos.x, Is.LessThan(left.elbowPos.x - 0.05f));
            // Mirror symmetry of the whole configuration.
            AssertVec(new Vector3(-rr.elbowPos.x, rr.elbowPos.y, rr.elbowPos.z), lr.elbowPos, PosTol);
            AssertVec(new Vector3(-rr.wristPos.x, rr.wristPos.y, rr.wristPos.z), lr.wristPos, PosTol);
        }

        [Test]
        public void Forward_ShoulderAdduction_MovesElbowMedially()
        {
            ArmRestPose right = MakeArm(ArmSide.Right);
            ArmRestPose left = MakeArm(ArmSide.Left);
            Assert.That(ArmSolver.Forward(right, Only(1, -10f)).elbowPos.x, Is.LessThan(right.elbowPos.x - 0.01f));
            Assert.That(ArmSolver.Forward(left, Only(1, -10f)).elbowPos.x, Is.GreaterThan(left.elbowPos.x + 0.01f));
        }

        [Test]
        public void Forward_SameAnglesOnBothSides_GiveMirroredGeometry()
        {
            // Same anatomical angles on both sides => x-mirrored geometry.
            var rng = new System.Random(3);
            ArmRestPose right = MakeArm(ArmSide.Right);
            ArmRestPose left = MakeArm(ArmSide.Left);
            for (int i = 0; i < 30; i++)
            {
                UpperLimbAngles a = RandomAngles(rng);
                ArmPoseResult rr = ArmSolver.Forward(right, a);
                ArmPoseResult lr = ArmSolver.Forward(left, a);
                AssertVec(new Vector3(-rr.elbowPos.x, rr.elbowPos.y, rr.elbowPos.z), lr.elbowPos, PosTol);
                AssertVec(new Vector3(-rr.wristPos.x, rr.wristPos.y, rr.wristPos.z), lr.wristPos, PosTol);
                // Hand axes mirror too (x negated).
                Vector3 ry = rr.handBasis * Vector3.up, ly = lr.handBasis * Vector3.up;
                Vector3 rz = rr.handBasis * Vector3.forward, lz = lr.handBasis * Vector3.forward;
                AssertVec(new Vector3(-ry.x, ry.y, ry.z), ly, DirTol);
                AssertVec(new Vector3(-rz.x, rz.y, rz.z), lz, DirTol);
            }
        }

        // ------------------------------------------------------------------ Forward: forearm supination

        [Test]
        public void Forward_Supination90_DorsalAxisMatchesAngleAxisConvention()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                float ls = rest.LateralSign;
                ArmPoseResult r = ArmSolver.Forward(rest, Only(4, +90f));

                // Convention from the solver: psRaw = -lateralSign * supination, applied as AngleAxis(psRaw, basis Y).
                Quaternion expectedBasis = rest.forearmBasis * Quaternion.AngleAxis(-ls * 90f, Vector3.up);
                Vector3 expectedDorsal = expectedBasis * Vector3.forward;
                Vector3 dorsal = r.handBasis * Vector3.forward;
                AssertVec(expectedDorsal, dorsal, DirTol, side.ToString());

                // The long axis (forward) is unchanged by supination.
                AssertVec(Vector3.forward, r.handBasis * Vector3.up, DirTol, side.ToString());
            }
        }

        [Test]
        public void Forward_Supination90_RightArmDorsalFacesLaterally_LeftIsMirrored()
        {
            // Right forearm pointing forward, palm down: 90 deg of supination turns the thumb up and the palm to
            // face medially, so the back of the hand faces laterally (+x for the right arm, -x for the left arm).
            ArmPoseResult r = ArmSolver.Forward(MakeArm(ArmSide.Right), Only(4, +90f));
            ArmPoseResult l = ArmSolver.Forward(MakeArm(ArmSide.Left), Only(4, +90f));
            Vector3 rd = r.handBasis * Vector3.forward;
            Vector3 ld = l.handBasis * Vector3.forward;

            AssertVec(Vector3.right, rd, DirTol);
            AssertVec(Vector3.left, ld, DirTol);
            AssertVec(new Vector3(-rd.x, rd.y, rd.z), ld, DirTol);
        }

        [Test]
        public void Forward_Supination180_DorsalFacesDown_AndPronationIsOpposite()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 d180 = ArmSolver.Forward(rest, Only(4, 180f)).handBasis * Vector3.forward;
                AssertVec(Vector3.down, d180, DirTol, side.ToString());

                // Pronation is the mirror image of supination about the vertical.
                Vector3 sup = ArmSolver.Forward(rest, Only(4, +40f)).handBasis * Vector3.forward;
                Vector3 pro = ArmSolver.Forward(rest, Only(4, -40f)).handBasis * Vector3.forward;
                AssertVec(new Vector3(-sup.x, sup.y, sup.z), pro, DirTol, side.ToString());
            }
        }

        [Test]
        public void Forward_Supination_SplitsRotationBetweenForearmBoneAndHandBasis()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            ArmPoseResult r = ArmSolver.Forward(rest, Only(4, +60f));
            // forearmRot carries elbowTwistShare of the total rotation about the long axis.
            float expected = 60f * ArmSolver.elbowTwistShare;
            Quaternion rel = r.forearmRot * Quaternion.Inverse(rest.forearmRot);
            Assert.That(Mathf.Abs(AnatomicalMath.TwistDegrees(rel, Vector3.forward)), Is.EqualTo(expected).Within(0.05f));
        }

        // ------------------------------------------------------------------ Forward: wrist

        [Test]
        public void Forward_WristExtension_TiltsHandAxisUp()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 y0 = ArmSolver.Forward(rest, UpperLimbAngles.Zero).handBasis * Vector3.up;
                Vector3 yExt = ArmSolver.Forward(rest, Only(5, +40f)).handBasis * Vector3.up;
                Vector3 yFlex = ArmSolver.Forward(rest, Only(5, -40f)).handBasis * Vector3.up;

                Assert.That(y0.y, Is.EqualTo(0f).Within(DirTol), side.ToString());
                Assert.That(yExt.y, Is.GreaterThan(0.5f), side.ToString());
                Assert.That(yExt.y, Is.EqualTo(Mathf.Sin(40f * Mathf.Deg2Rad)).Within(DirTol), side.ToString());
                Assert.That(yFlex.y, Is.LessThan(-0.5f), side.ToString());
                // Pure extension stays in the sagittal plane: no sideways drift.
                Assert.That(yExt.x, Is.EqualTo(0f).Within(DirTol), side.ToString());
            }
        }

        [Test]
        public void Forward_WristRadialDeviation_MovesHandAxisMedially_MirrorSymmetric()
        {
            ArmRestPose right = MakeArm(ArmSide.Right);
            ArmRestPose left = MakeArm(ArmSide.Left);
            Vector3 ry = ArmSolver.Forward(right, Only(6, +20f)).handBasis * Vector3.up;
            Vector3 ly = ArmSolver.Forward(left, Only(6, +20f)).handBasis * Vector3.up;

            // Palm-down hand: the thumb is on the medial side, so radial deviation swings the fingers medially
            // (towards -x for the right arm, +x for the left arm).
            float expectedX = Mathf.Sin(20f * Mathf.Deg2Rad);
            Assert.That(ry.x, Is.EqualTo(-expectedX).Within(DirTol));
            Assert.That(ly.x, Is.EqualTo(+expectedX).Within(DirTol));
            AssertVec(new Vector3(-ry.x, ry.y, ry.z), ly, DirTol);

            // Ulnar deviation is the opposite.
            Vector3 ru = ArmSolver.Forward(right, Only(6, -20f)).handBasis * Vector3.up;
            Assert.That(ru.x, Is.EqualTo(+expectedX).Within(DirTol));
        }

        [Test]
        public void Forward_WristRadial_MatchesRudRawConvention()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                float ls = rest.LateralSign;
                ArmPoseResult r = ArmSolver.Forward(rest, Only(6, +20f));
                // rudRaw = -lateralSign * radial, rotation about basis Z (Vector3.forward in the basis frame).
                Quaternion expected = rest.forearmBasis * Quaternion.AngleAxis(-ls * 20f, Vector3.forward);
                Assert.That(AngleBetween(expected, r.handBasis), Is.LessThan(0.01f), side.ToString());
            }
        }

        // ------------------------------------------------------------------ Decompose(Forward(x)) == x

        [Test]
        public void Decompose_OfForward_RecoversAngles_RandomSamples_BothArms()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                var rng = new System.Random(side == ArmSide.Right ? 20240 : 20241);
                for (int i = 0; i < 200; i++)
                {
                    UpperLimbAngles a = RandomAngles(rng);
                    ArmPoseResult r = ArmSolver.Forward(rest, a);
                    UpperLimbAngles d = ArmSolver.Decompose(rest, r.upperArmRot, r.forearmRot, r.handRot);
                    AssertAnglesClose(a, d, 0.5f, $"{side} sample {i}");
                }
            }
        }

        [Test]
        public void Decompose_OfForward_RecoversAngles_SingleJointSweeps()
        {
            float[] mins = { -30f, -20f, -40f, -10f, -60f, -50f, -20f };
            float[] maxs = { 80f, 60f, 40f, 90f, 60f, 50f, 20f };
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                for (int j = 0; j < UpperLimbAngles.Count; j++)
                {
                    for (int s = 0; s <= 8; s++)
                    {
                        float v = Mathf.Lerp(mins[j], maxs[j], s / 8f);
                        UpperLimbAngles a = Only(j, v);
                        ArmPoseResult r = ArmSolver.Forward(rest, a);
                        UpperLimbAngles d = ArmSolver.Decompose(rest, r.upperArmRot, r.forearmRot, r.handRot);
                        AssertAnglesClose(a, d, 0.5f, $"{side} joint {UpperLimbAngles.Names[j]} = {v}");
                    }
                }
            }
        }

        // ------------------------------------------------------------------ Inverse -> Forward consistency

        [Test]
        public void Inverse_OfForward_RecoversPose_RandomSamples_BothArms()
        {
            int tested = 0;
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                var rng = new System.Random(side == ArmSide.Right ? 777 : 778);
                for (int i = 0; i < 200; i++)
                {
                    UpperLimbAngles a = RandomAngles(rng);
                    if (a.elbowFlexion + rest.elbowRestDeg < 8f) continue; // arm plane undefined when straight
                    ArmPoseResult r = ArmSolver.Forward(rest, a);

                    float prevTwist = 0f;
                    ArmPoseResult s = ArmSolver.Inverse(rest, r.wristPos, r.handBasis, r.elbowPos, ref prevTwist);
                    string msg = $"{side} sample {i}";

                    Assert.That(s.targetClamped, Is.False, msg);
                    Assert.That(Vector3.Distance(s.wristPos, r.wristPos), Is.LessThan(0.002f), msg);
                    Assert.That(Vector3.Distance(s.elbowPos, r.elbowPos), Is.LessThan(0.002f), msg);
                    AssertAnglesClose(a, s.angles, 1f, msg);
                    tested++;
                }
            }
            Assert.That(tested, Is.GreaterThan(300), "too many samples skipped");
        }

        [Test]
        public void Inverse_OfRest_ReturnsZeroAngles()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                ArmPoseResult r = ArmSolver.Forward(rest, UpperLimbAngles.Zero);
                float prevTwist = 0f;
                ArmPoseResult s = ArmSolver.Inverse(rest, r.wristPos, r.handBasis, r.elbowPos, ref prevTwist);
                AssertAnglesClose(UpperLimbAngles.Zero, s.angles, 0.5f, side.ToString());
                AssertVec(rest.wristPos, s.wristPos, 0.002f, side.ToString());
                AssertVec(rest.elbowPos, s.elbowPos, 0.002f, side.ToString());
            }
        }

        [Test]
        public void Inverse_WithDefaultElbowHint_ReachesTargetWristPosition()
        {
            var rng = new System.Random(4242);
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                for (int i = 0; i < 50; i++)
                {
                    UpperLimbAngles a = RandomAngles(rng);
                    ArmPoseResult r = ArmSolver.Forward(rest, a);
                    Vector3 hint = ArmSolver.DefaultElbowHint(rest, r.wristPos, r.handBasis);
                    float prevTwist = 0f;
                    ArmPoseResult s = ArmSolver.Inverse(rest, r.wristPos, r.handBasis, hint, ref prevTwist);
                    Assert.That(s.targetClamped, Is.False, $"{side} sample {i}");
                    Assert.That(Vector3.Distance(s.wristPos, r.wristPos), Is.LessThan(0.002f), $"{side} sample {i}");
                    Assert.That(Vector3.Distance(s.shoulderPos, rest.shoulderPos), Is.LessThan(PosTol), $"{side} sample {i}");
                }
            }
        }

        // ------------------------------------------------------------------ Reach clamping

        [Test]
        public void Inverse_TargetBeyondReach_IsClampedOntoReachSphere()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 target = rest.shoulderPos + Vector3.forward * 2f; // 2 m away
                Vector3 hint = ArmSolver.DefaultElbowHint(rest, target, rest.forearmBasis);
                float prevTwist = 0f;
                ArmPoseResult r = ArmSolver.Inverse(rest, target, rest.forearmBasis, hint, ref prevTwist);

                Assert.That(r.targetClamped, Is.True, side.ToString());
                float reach = 0.999f * (rest.upperArmLength + rest.forearmLength);
                Assert.That(Vector3.Distance(r.wristPos, rest.shoulderPos), Is.EqualTo(reach).Within(0.001f), side.ToString());
                // Clamped wrist lies on the line towards the target.
                Vector3 dir = (r.wristPos - rest.shoulderPos).normalized;
                AssertVec(Vector3.forward, dir, 0.01f, side.ToString());
            }
        }

        [Test]
        public void Inverse_TargetWithinReach_IsNotClamped()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            Vector3 target = rest.shoulderPos + new Vector3(0f, -0.1f, 0.35f);
            float prevTwist = 0f;
            ArmPoseResult r = ArmSolver.Inverse(rest, target, rest.forearmBasis,
                ArmSolver.DefaultElbowHint(rest, target, rest.forearmBasis), ref prevTwist);
            Assert.That(r.targetClamped, Is.False);
            AssertVec(target, r.wristPos, 0.002f);
        }

        [Test]
        public void Inverse_TargetTooClose_IsClamped()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            Vector3 target = rest.shoulderPos + new Vector3(0.001f, 0f, 0f);
            float prevTwist = 0f;
            ArmPoseResult r = ArmSolver.Inverse(rest, target, rest.forearmBasis,
                rest.shoulderPos + Vector3.down * 0.2f, ref prevTwist);
            Assert.That(r.targetClamped, Is.True);
            Assert.That(float.IsNaN(r.wristPos.x) || float.IsNaN(r.wristPos.y) || float.IsNaN(r.wristPos.z), Is.False);
        }

        [Test]
        public void Inverse_JointLimits_ClampResultAngles()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            ArmPoseResult r = ArmSolver.Forward(rest, Only(0, 70f));
            var min = new UpperLimbAngles
            {
                shoulderFlexion = -30f, shoulderAbduction = -90f, shoulderRotation = -90f, elbowFlexion = -90f,
                forearmSupination = -90f, wristExtension = -90f, wristRadial = -90f
            };
            var max = new UpperLimbAngles
            {
                shoulderFlexion = 40f, shoulderAbduction = 90f, shoulderRotation = 90f, elbowFlexion = 90f,
                forearmSupination = 90f, wristExtension = 90f, wristRadial = 90f
            };
            float prevTwist = 0f;
            ArmPoseResult s = ArmSolver.Inverse(rest, r.wristPos, r.handBasis, r.elbowPos, ref prevTwist, min, max);
            Assert.That(s.angles.shoulderFlexion, Is.EqualTo(40f).Within(0.5f));
        }

        // ------------------------------------------------------------------ DefaultElbowHint

        [Test]
        public void DefaultElbowHint_ForForwardTarget_IsBelowShoulder()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 target = rest.shoulderPos + Vector3.forward * 0.45f;
                Vector3 hint = ArmSolver.DefaultElbowHint(rest, target, rest.forearmBasis);

                Assert.That(hint.y, Is.LessThan(rest.shoulderPos.y), side.ToString());
                Assert.That(Vector3.Distance(hint, rest.shoulderPos), Is.EqualTo(rest.upperArmLength).Within(PosTol), side.ToString());
                // Also below the shoulder-wrist line, and leaning outwards (lateral side).
                Assert.That(hint.y, Is.LessThan(target.y), side.ToString());
                float lateralOffset = (hint.x - rest.shoulderPos.x) * rest.LateralSign;
                Assert.That(lateralOffset, Is.GreaterThan(0f), side.ToString());
            }
        }

        [Test]
        public void DefaultElbowHint_IsPerpendicularToShoulderWristAxis()
        {
            foreach (ArmSide side in Sides)
            {
                ArmRestPose rest = MakeArm(side);
                Vector3 target = rest.shoulderPos + new Vector3(0.1f, -0.1f, 0.4f);
                Vector3 hint = ArmSolver.DefaultElbowHint(rest, target, rest.forearmBasis);
                Vector3 axis = (target - rest.shoulderPos).normalized;
                Assert.That(Vector3.Dot((hint - rest.shoulderPos).normalized, axis), Is.EqualTo(0f).Within(1e-3f), side.ToString());
            }
        }

        [Test]
        public void DefaultElbowHint_HandRoll_RotatesHintAboutShoulderWristAxis()
        {
            // With the hand rolled (dorsal facing down) the hint rotates about the shoulder-wrist axis by
            // rollInfluence * roll; with rollInfluence = 0 the hint is independent of the hand.
            ArmRestPose rest = MakeArm(ArmSide.Right);
            Vector3 target = rest.shoulderPos + Vector3.forward * 0.45f;
            Quaternion palmUp = rest.forearmBasis * Quaternion.AngleAxis(-120f, Vector3.up);

            Vector3 neutral = ArmSolver.DefaultElbowHint(rest, target, rest.forearmBasis);
            Vector3 noRoll = ArmSolver.DefaultElbowHint(rest, target, palmUp, 0f);
            AssertVec(neutral, noRoll, 1e-4f);

            Vector3 rolled = ArmSolver.DefaultElbowHint(rest, target, palmUp, 0.5f);
            Assert.That(Vector3.Distance(rolled, neutral), Is.GreaterThan(0.01f));
            Assert.That(Vector3.Distance(rolled, rest.shoulderPos), Is.EqualTo(rest.upperArmLength).Within(PosTol));
        }

        // ------------------------------------------------------------------ ArmRestPose.Transformed

        [Test]
        public void Transformed_Translation_ShiftsPositionsAndKeepsDerivedValues()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            Vector3 t = new Vector3(1f, 2f, 3f);
            ArmRestPose moved = rest.Transformed(t, Quaternion.identity);
            Assert.That(moved.isFinalized, Is.True);
            AssertVec(rest.shoulderPos + t, moved.shoulderPos, PosTol);
            AssertVec(rest.wristPos + t, moved.wristPos, PosTol);
            Assert.That(moved.upperArmLength, Is.EqualTo(rest.upperArmLength).Within(PosTol));
            Assert.That(moved.elbowRestDeg, Is.EqualTo(rest.elbowRestDeg).Within(0.01f));
            Assert.That(moved.shoulderRestAngles.x, Is.EqualTo(rest.shoulderRestAngles.x).Within(0.01f));
        }

        // ------------------------------------------------------------------ misc

        [Test]
        public void Forward_ElbowFlexion_AndShoulderRotationDoNotChangeElbowPosition()
        {
            ArmRestPose rest = MakeArm(ArmSide.Right);
            ArmPoseResult a = ArmSolver.Forward(rest, Only(3, 40f));
            ArmPoseResult b = ArmSolver.Forward(rest, Only(2, 30f));
            AssertVec(rest.elbowPos, a.elbowPos, PosTol);
            // Humeral rotation is a twist about the humerus: elbow stays put, but the forearm swings round it.
            AssertVec(rest.elbowPos, b.elbowPos, PosTol);
            Assert.That(Vector3.Distance(b.wristPos, rest.wristPos), Is.GreaterThan(0.02f));
        }
    }

    /// <summary>Edit-mode tests for <see cref="UpperLimbAngles"/>.</summary>
    public class UpperLimbAnglesTests
    {
        private static UpperLimbAngles Sample()
        {
            return new UpperLimbAngles
            {
                shoulderFlexion = 1f, shoulderAbduction = 2f, shoulderRotation = 3f, elbowFlexion = 4f,
                forearmSupination = 5f, wristExtension = 6f, wristRadial = 7f
            };
        }

        [Test]
        public void Indexer_GetMatchesFields()
        {
            UpperLimbAngles a = Sample();
            Assert.That(a[0], Is.EqualTo(a.shoulderFlexion));
            Assert.That(a[1], Is.EqualTo(a.shoulderAbduction));
            Assert.That(a[2], Is.EqualTo(a.shoulderRotation));
            Assert.That(a[3], Is.EqualTo(a.elbowFlexion));
            Assert.That(a[4], Is.EqualTo(a.forearmSupination));
            Assert.That(a[5], Is.EqualTo(a.wristExtension));
            Assert.That(a[6], Is.EqualTo(a.wristRadial));
        }

        [Test]
        public void Indexer_SetWritesFields()
        {
            var a = new UpperLimbAngles();
            for (int i = 0; i < UpperLimbAngles.Count; i++) a[i] = 10f * (i + 1);
            Assert.That(a.shoulderFlexion, Is.EqualTo(10f));
            Assert.That(a.shoulderAbduction, Is.EqualTo(20f));
            Assert.That(a.shoulderRotation, Is.EqualTo(30f));
            Assert.That(a.elbowFlexion, Is.EqualTo(40f));
            Assert.That(a.forearmSupination, Is.EqualTo(50f));
            Assert.That(a.wristExtension, Is.EqualTo(60f));
            Assert.That(a.wristRadial, Is.EqualTo(70f));
        }

        [Test]
        public void Indexer_OutOfRange_Throws()
        {
            UpperLimbAngles a = Sample();
            Assert.Throws<IndexOutOfRangeException>(() => { float _ = a[-1]; });
            Assert.Throws<IndexOutOfRangeException>(() => { float _ = a[UpperLimbAngles.Count]; });
            Assert.Throws<IndexOutOfRangeException>(() =>
            {
                var b = new UpperLimbAngles();
                b[UpperLimbAngles.Count] = 1f;
            });
        }

        [Test]
        public void NamesArrays_HaveOneEntryPerJoint()
        {
            Assert.That(UpperLimbAngles.Count, Is.EqualTo(7));
            Assert.That(UpperLimbAngles.Names.Length, Is.EqualTo(UpperLimbAngles.Count));
            Assert.That(UpperLimbAngles.ShortNames.Length, Is.EqualTo(UpperLimbAngles.Count));
        }

        [Test]
        public void ToArray_FromArray_RoundTrip()
        {
            UpperLimbAngles a = Sample();
            float[] arr = a.ToArray();
            Assert.That(arr, Is.EqualTo(new[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f }));
            UpperLimbAngles b = UpperLimbAngles.FromArray(arr);
            for (int i = 0; i < UpperLimbAngles.Count; i++) Assert.That(b[i], Is.EqualTo(a[i]));
        }

        [Test]
        public void FromArray_HonoursOffset()
        {
            float[] data = { 99f, 99f, 1f, 2f, 3f, 4f, 5f, 6f, 7f, 99f };
            UpperLimbAngles b = UpperLimbAngles.FromArray(data, 2);
            for (int i = 0; i < UpperLimbAngles.Count; i++) Assert.That(b[i], Is.EqualTo(i + 1f));
        }

        [Test]
        public void Zero_IsAllZeros()
        {
            UpperLimbAngles z = UpperLimbAngles.Zero;
            for (int i = 0; i < UpperLimbAngles.Count; i++) Assert.That(z[i], Is.EqualTo(0f));
        }

        [Test]
        public void MaxAbsDifference_WrapsAcrossPlusMinus180()
        {
            var a = new UpperLimbAngles { shoulderFlexion = 179f };
            var b = new UpperLimbAngles { shoulderFlexion = -179f };
            Assert.That(UpperLimbAngles.MaxAbsDifference(a, b), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(UpperLimbAngles.MaxAbsDifference(b, a), Is.EqualTo(2f).Within(1e-4f));
        }

        [Test]
        public void MaxAbsDifference_PicksLargestJoint()
        {
            UpperLimbAngles a = Sample();
            UpperLimbAngles b = a;
            b.elbowFlexion += 12f;
            b.wristRadial -= 5f;
            Assert.That(UpperLimbAngles.MaxAbsDifference(a, b), Is.EqualTo(12f).Within(1e-4f));
            Assert.That(UpperLimbAngles.MaxAbsDifference(a, a), Is.EqualTo(0f));
        }

        [Test]
        public void RmsDifference_IsRootMeanSquare()
        {
            var a = new UpperLimbAngles();
            var b = new UpperLimbAngles { shoulderFlexion = 7f };
            Assert.That(UpperLimbAngles.RmsDifference(a, b), Is.EqualTo(Mathf.Sqrt(49f / 7f)).Within(1e-4f));
        }

        [Test]
        public void Subtraction_WrapsEachComponent()
        {
            var a = new UpperLimbAngles { shoulderRotation = 170f };
            var b = new UpperLimbAngles { shoulderRotation = -170f };
            Assert.That((a - b).shoulderRotation, Is.EqualTo(-20f).Within(1e-4f));
            UpperLimbAngles sum = Sample() + Sample();
            Assert.That(sum.wristRadial, Is.EqualTo(14f).Within(1e-4f));
        }

        [Test]
        public void Lerp_InterpolatesComponentwise()
        {
            var a = new UpperLimbAngles();
            var b = new UpperLimbAngles { elbowFlexion = 80f };
            Assert.That(UpperLimbAngles.Lerp(a, b, 0.25f).elbowFlexion, Is.EqualTo(20f).Within(1e-3f));
            Assert.That(UpperLimbAngles.Lerp(a, b, 0f).elbowFlexion, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(UpperLimbAngles.Lerp(a, b, 1f).elbowFlexion, Is.EqualTo(80f).Within(1e-3f));
        }

        [Test]
        public void Clamped_ClampsEachComponentToItsRange()
        {
            var min = new UpperLimbAngles
            {
                shoulderFlexion = -10f, shoulderAbduction = -10f, shoulderRotation = -10f, elbowFlexion = -10f,
                forearmSupination = -10f, wristExtension = -10f, wristRadial = -10f
            };
            var max = new UpperLimbAngles
            {
                shoulderFlexion = 10f, shoulderAbduction = 10f, shoulderRotation = 10f, elbowFlexion = 10f,
                forearmSupination = 10f, wristExtension = 10f, wristRadial = 10f
            };
            var v = new UpperLimbAngles
            {
                shoulderFlexion = 50f, shoulderAbduction = -50f, shoulderRotation = 5f, elbowFlexion = 10f,
                forearmSupination = -10f, wristExtension = 11f, wristRadial = -11f
            };
            UpperLimbAngles c = v.Clamped(min, max);
            Assert.That(c.shoulderFlexion, Is.EqualTo(10f));
            Assert.That(c.shoulderAbduction, Is.EqualTo(-10f));
            Assert.That(c.shoulderRotation, Is.EqualTo(5f));
            Assert.That(c.elbowFlexion, Is.EqualTo(10f));
            Assert.That(c.forearmSupination, Is.EqualTo(-10f));
            Assert.That(c.wristExtension, Is.EqualTo(10f));
            Assert.That(c.wristRadial, Is.EqualTo(-10f));
        }
    }
}
