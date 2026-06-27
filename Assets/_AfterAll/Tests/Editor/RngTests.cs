using NUnit.Framework;

namespace AfterAll.Tests.Editor
{
    public sealed class RngTests
    {
        [Test]
        public void SameSeed_ProducesIdenticalSequence()
        {
            var a = new AfterAll.Generation.Rng(12345);
            var b = new AfterAll.Generation.Rng(12345);

            for (int i = 0; i < 100; i++)
            {
                Assert.AreEqual(a.Value(), b.Value(), 0.0001f, $"Mismatch at step {i}");
                Assert.AreEqual(a.Range(0, 50), b.Range(0, 50));
            }
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            var a = new AfterAll.Generation.Rng(1);
            var b = new AfterAll.Generation.Rng(2);

            bool anyDifferent = false;
            for (int i = 0; i < 20; i++)
            {
                if (System.Math.Abs(a.Value() - b.Value()) > 0.0001f)
                    anyDifferent = true;
            }

            Assert.IsTrue(anyDifferent);
        }

        [Test]
        public void Derive_IsIsolatedFromParent()
        {
            var parent = new AfterAll.Generation.Rng(99);
            float parentBefore = parent.Value();

            var child = parent.Derive(7);
            float childVal = child.Value();

            float parentAfter = parent.Value();

            Assert.That(System.Math.Abs(childVal - parentAfter), Is.GreaterThan(0.0001f));
            Assert.That(System.Math.Abs(childVal - parentBefore), Is.GreaterThan(0.0001f));
        }

        [Test]
        public void Derive_IsDeterministic()
        {
            var a = new AfterAll.Generation.Rng(42).Derive(3, 5);
            var b = new AfterAll.Generation.Rng(42).Derive(3, 5);

            for (int i = 0; i < 10; i++)
                Assert.AreEqual(a.Value(), b.Value(), 0.0001f);
        }
    }
}
