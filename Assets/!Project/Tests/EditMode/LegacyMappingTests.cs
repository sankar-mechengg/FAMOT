using FAMOT.Protocol;
using NUnit.Framework;

namespace FAMOT.Tests
{
    public class LegacyMappingTests
    {
        private const float Tol = 1e-3f;

        [Test]
        public void Piecewise_EndpointsAndRest()
        {
            Assert.AreEqual(-70f, LegacyMapping.PiecewiseMap(0f, -70f, 70f), Tol);
            Assert.AreEqual(0f, LegacyMapping.PiecewiseMap(50f, -70f, 70f), Tol);
            Assert.AreEqual(70f, LegacyMapping.PiecewiseMap(100f, -70f, 70f), Tol);
        }

        [Test]
        public void Piecewise_AsymmetricRangesStillRestAt50()
        {
            Assert.AreEqual(-20f, LegacyMapping.PiecewiseMap(0f, -20f, 90f), Tol);
            Assert.AreEqual(0f, LegacyMapping.PiecewiseMap(50f, -20f, 90f), Tol);
            Assert.AreEqual(90f, LegacyMapping.PiecewiseMap(100f, -20f, 90f), Tol);
            Assert.AreEqual(-10f, LegacyMapping.PiecewiseMap(25f, -20f, 90f), Tol);
            Assert.AreEqual(45f, LegacyMapping.PiecewiseMap(75f, -20f, 90f), Tol);
        }

        [Test]
        public void Piecewise_ClampsOutOfRangeInput()
        {
            Assert.AreEqual(-70f, LegacyMapping.PiecewiseMap(-10f, -70f, 70f), Tol);
            Assert.AreEqual(70f, LegacyMapping.PiecewiseMap(150f, -70f, 70f), Tol);
        }

        [Test]
        public void Inverse_Endpoints()
        {
            Assert.AreEqual(0f, LegacyMapping.InversePiecewiseMap(-70f, -70f, 70f), Tol);
            Assert.AreEqual(50f, LegacyMapping.InversePiecewiseMap(0f, -70f, 70f), Tol);
            Assert.AreEqual(100f, LegacyMapping.InversePiecewiseMap(70f, -70f, 70f), Tol);
        }

        [Test]
        public void Inverse_RoundTrip()
        {
            float[][] limits = { new[] { -70f, 70f }, new[] { -20f, 90f }, new[] { -45.5f, 33.25f } };
            foreach (float[] l in limits)
            {
                for (float v = 0f; v <= 100f; v += 2.5f)
                {
                    float angle = LegacyMapping.PiecewiseMap(v, l[0], l[1]);
                    float back = LegacyMapping.InversePiecewiseMap(angle, l[0], l[1]);
                    Assert.AreEqual(v, back, 1e-2f, "v=" + v + " limits " + l[0] + "," + l[1]);
                }
            }
        }

        [Test]
        public void Inverse_ClampsBeyondLimits()
        {
            Assert.AreEqual(0f, LegacyMapping.InversePiecewiseMap(-200f, -70f, 70f), Tol);
            Assert.AreEqual(100f, LegacyMapping.InversePiecewiseMap(200f, -70f, 70f), Tol);
        }

        [Test]
        public void Inverse_DegenerateLimitsGiveRest()
        {
            Assert.AreEqual(50f, LegacyMapping.InversePiecewiseMap(-10f, 0f, 70f), Tol);
            Assert.AreEqual(50f, LegacyMapping.InversePiecewiseMap(10f, -70f, 0f), Tol);
        }
    }
}
