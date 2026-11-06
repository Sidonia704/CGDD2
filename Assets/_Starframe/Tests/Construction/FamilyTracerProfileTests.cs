using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class FamilyTracerProfileTests
    {
        GameObject root;
        LaserVisualPool pool;
        static readonly Color Blue = new Color(.08f, .65f, 1f, 1f);
        static readonly Color Gold = new Color(1f, .62f, .055f, 1f);

        [SetUp] public void SetUp()
        {
            root = new GameObject("Family tracer profile fixture");
            pool = root.AddComponent<LaserVisualPool>();
            pool.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"));
        }
        [TearDown] public void TearDown()
        {
            if (pool)
            {
                var field = typeof(LaserVisualPool).GetField("sharedMaterial", BindingFlags.Instance | BindingFlags.NonPublic);
                var material = field.GetValue(pool) as Material;
                if (material) Object.DestroyImmediate(material);
                field.SetValue(pool, null);
            }
            if (root) Object.DestroyImmediate(root);
        }

        [TestCase(WeaponFamily.Gatling)] [TestCase(WeaponFamily.Railgun)]
        public void KineticFamiliesRenderOnlyTheOriginalBrightCoreAtEightyPercentWidth(WeaponFamily family)
        {
            int handle = Acquire(family, Gold);
            pool.Move(handle, Vector3.forward * 40, Vector3.forward);
            var primary = Primary(); var properties = Properties(primary);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled), Is.EqualTo(1));
            Assert.That(primary.transform.localScale.x, Is.EqualTo(2f * .24f * .8f).Within(.00001f));
            Assert.That(primary.transform.localScale.y, Is.EqualTo(40).Within(.00001f), "Changing cross-section must not shorten the physical trace.");
            bool gatling=family==WeaponFamily.Gatling;
            AssertColor(properties.GetColor("_Color"), gatling?Gold:Color.Lerp(Gold, Color.white, .82f));
            AssertColor(properties.GetColor("_HeadColor"), gatling?Gold:Color.white);
            Assert.That(properties.GetFloat("_Intensity"), Is.EqualTo(10 * (gatling?1:1.25f)).Within(.00001f));
            Assert.That(properties.GetFloat("_Opacity"), Is.EqualTo(1));
        }

        [Test] public void RapidLaserKeepsBlueAndOuterWidthWhileItsCoreBecomesExactlyOnePointFiveTimesWider()
        {
            int original = Acquire(WeaponFamily.Generic, Blue);
            pool.Move(original, Vector3.forward * 40, Vector3.forward);
            var primary = Primary(); var core = Core();
            float originalOuterWidth = primary.transform.lossyScale.x, originalCoreWidth = core.transform.lossyScale.x;
            var oldCore = Properties(core);
            pool.Release(original);
            int updated = Acquire(WeaponFamily.RapidLaser, Blue);
            pool.Move(updated, Vector3.forward * 40, Vector3.forward);
            Assert.That(Primary(), Is.SameAs(primary), "Reuse the existing visual slot.");
            Assert.That(primary.transform.lossyScale.x, Is.EqualTo(originalOuterWidth).Within(.00001f));
            Assert.That(core.transform.lossyScale.x, Is.EqualTo(originalCoreWidth * 1.5f).Within(.00001f));
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled), Is.EqualTo(2));
            AssertColor(Properties(primary).GetColor("_Color"), Blue);
            AssertColor(Properties(core).GetColor("_Color"), oldCore.GetColor("_Color"));
            Assert.That(Properties(primary).GetFloat("_Opacity"), Is.EqualTo(.65f).Within(.00001f));
            Assert.That(Properties(core).GetFloat("_Opacity"), Is.EqualTo(1));
            Assert.That(Properties(core).GetFloat("_Intensity"), Is.EqualTo(oldCore.GetFloat("_Intensity")));
        }

        [TestCase(WeaponFamily.Gatling)] [TestCase(WeaponFamily.RapidLaser)]
        public void LingerPreservesProfileAndEndpointsThenRecyclesBothLayers(WeaponFamily family)
        {
            int handle = Acquire(family, family == WeaponFamily.Gatling ? Gold : Blue, .2f);
            pool.Move(handle, Vector3.forward * 40, Vector3.forward);
            var primary = Primary(); Vector3 position = primary.transform.position, scale = primary.transform.localScale;
            pool.Release(handle); pool.Move(handle, Vector3.one * 999, Vector3.right); pool.StepVisuals(.1f);
            Assert.That(Vector3.Distance(primary.transform.position, position), Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(primary.transform.localScale, scale), Is.LessThan(.00001f));
            Assert.That(Properties(primary).GetFloat("_Opacity"), Is.EqualTo(family == WeaponFamily.RapidLaser ? .325f : .5f).Within(.00001f));
            if (family == WeaponFamily.RapidLaser) Assert.That(Properties(Core()).GetFloat("_Opacity"), Is.EqualTo(.5f).Within(.00001f));
            else Assert.That(root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled), Is.EqualTo(1));
            pool.StepVisuals(.11f);
            Assert.That(pool.ActiveVisualCount, Is.Zero);
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Any(r => r.enabled), Is.False);
        }

        [Test] public void ReuseAndClearCannotLeakLaserOuterLayersOrKineticColorsIntoAnotherFamily()
        {
            int handle = Acquire(WeaponFamily.RapidLaser, Blue); pool.Move(handle, Vector3.forward * 40, Vector3.forward);
            var primary = Primary(); var core = Core(); pool.Release(handle);
            foreach (var family in new[] { WeaponFamily.Gatling, WeaponFamily.Railgun })
            {
                handle = Acquire(family, Gold); pool.Move(handle, Vector3.forward * 40, Vector3.forward);
                Assert.That(Primary(), Is.SameAs(primary)); Assert.That(core.enabled, Is.False);
                Assert.That(primary.transform.localScale.x, Is.EqualTo(.384f).Within(.00001f));
                pool.Release(handle);
            }
            handle = Acquire(WeaponFamily.RapidLaser, Blue); pool.Move(handle, Vector3.forward * 40, Vector3.forward);
            pool.Clear(); Assert.That(primary.enabled || core.enabled, Is.False);
            // Omitting the optional family must restore the legacy generic profile on a recycled slot.
            handle = pool.Acquire(Vector3.zero, Vector3.forward, 1, 30, 20, null, default, 2, 10, Blue, 0, TracerStyle.Continuous);
            pool.Move(handle, Vector3.forward * 40, Vector3.forward);
            Assert.That(primary.transform.localScale.x, Is.EqualTo(2).Within(.00001f));
            Assert.That(core.transform.lossyScale.x, Is.EqualTo(.48f).Within(.00001f));
            Assert.That(primary.enabled && core.enabled, Is.True);
            Assert.That(Properties(primary).GetFloat("_Opacity"), Is.EqualTo(1));
            AssertColor(Properties(primary).GetColor("_Color"), Blue);
        }

        [Test] public void KineticCoreProfileRetainsThePostScaleWorldLengthCap()
        {
            float cap = .7f * 2700 / 24;
            int handle = pool.Acquire(Vector3.zero, Vector3.forward, 1, 30, 200, null, default, 2, 10, Gold,
                0, TracerStyle.Continuous, 0, cap, WeaponFamily.Gatling);
            pool.Move(handle, Vector3.forward * 1000, Vector3.forward);
            Assert.That(Primary().transform.localScale.y, Is.EqualTo(cap).Within(.00001f));
            Assert.That(root.GetComponentsInChildren<MeshRenderer>().Count(r => r.enabled), Is.EqualTo(1));
        }

        int Acquire(WeaponFamily family, Color color, float linger = 0) => pool.Acquire(Vector3.zero, Vector3.forward, 1, 30, 20,
            null, default, 2, 10, color, 0, TracerStyle.Continuous, linger, 0, family);
        MeshRenderer Primary() => root.GetComponentsInChildren<MeshRenderer>().Single(r => r.transform.parent == root.transform);
        MeshRenderer Core() => root.GetComponentsInChildren<MeshRenderer>().Single(r => r.transform.parent != root.transform);
        static MaterialPropertyBlock Properties(MeshRenderer renderer)
        {
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block;
        }
        static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(.00001f)); Assert.That(actual.g, Is.EqualTo(expected.g).Within(.00001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(.00001f)); Assert.That(actual.a, Is.EqualTo(expected.a).Within(.00001f));
        }
    }
}
