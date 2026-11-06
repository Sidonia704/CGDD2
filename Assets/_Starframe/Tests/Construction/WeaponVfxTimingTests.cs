using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class WeaponVfxTimingTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown]
        public void Cleanup()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void MuzzleCoreTransitionsFromWhiteToWeaponColour()
        {
            var shader = Shader.Find("Starframe/Combat/VoxelGlow");
            if (!shader) Assert.Ignore("Combat voxel glow shader is unavailable in this Editor.");
            var host = Own(new GameObject("Muzzle VFX timing fixture"));
            var pool = host.AddComponent<CombatVfxPool>();
            pool.Initialize(shader, null, 1);
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.effectColor = new Color(.08f, .38f, .92f, 1);
            pool.EmitMuzzle(Vector3.zero, Vector3.forward, weapon);

            var effects = (IList)typeof(CombatVfxPool).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            var effect = effects[0];
            var type = effect.GetType();
            Assert.That((bool)type.GetField("transitionsColor").GetValue(effect), Is.True);
            Assert.That((Color)type.GetField("startColor").GetValue(effect), Is.EqualTo(Color.white));
            Assert.That((Color)type.GetField("endColor").GetValue(effect), Is.EqualTo(weapon.effectColor));
            float duration = (float)type.GetField("duration").GetValue(effect);
            pool.StepVisuals(duration * .5f);
            var renderer = (Renderer)type.GetField("renderer").GetValue(effect);
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            var actual = properties.GetColor("_Color");
            Assert.That(actual.r, Is.EqualTo(.54f).Within(.02f));
            Assert.That(actual.g, Is.EqualTo(.69f).Within(.02f));
            Assert.That(actual.b, Is.EqualTo(.96f).Within(.02f));
        }

        [Test]
        public void MissileTrailIgnitesAfterShortDelayAndFadesIn()
        {
            var shader = Shader.Find("Starframe/Combat/VoxelGlow");
            if (!shader) Assert.Ignore("Combat voxel glow shader is unavailable in this Editor.");
            var host = Own(new GameObject("Missile VFX timing fixture"));
            var pool = host.AddComponent<SpecialWeaponVisualPool>();
            pool.Initialize();
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.tailLength = 5;
            weapon.projectileDiameter = .6f;
            weapon.effectColor = new Color(1, .5f, .12f, 1);
            int slot = pool.AcquireMissile(weapon, Vector3.zero, Vector3.forward);
            Assert.That(slot, Is.GreaterThanOrEqualTo(0));

            pool.MoveMissile(slot, Vector3.forward * 10, Vector3.forward, 10);
            pool.StepVisuals(.11f);
            pool.MoveMissile(slot, Vector3.forward * 10, Vector3.forward, 10);
            var missile = ((IList)typeof(SpecialWeaponVisualPool).GetField("missiles", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool))[slot];
            var type = missile.GetType();
            var renderers = (MeshRenderer[])type.GetField("trailRenderers").GetValue(missile);
            foreach (var renderer in renderers)
                Assert.That(renderer.enabled, Is.False, "The ejection period must stay unlit for 0.12 seconds.");

            pool.StepVisuals(.055f);
            pool.MoveMissile(slot, Vector3.forward * 10, Vector3.forward, 10);
            int visible = 0;
            float opacity = 0;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i].enabled)
            {
                visible++;
                var properties = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(properties);
                opacity = Mathf.Max(opacity, properties.GetFloat("_Opacity"));
            }
            Assert.That(visible, Is.GreaterThan(0));
            Assert.That(opacity, Is.GreaterThan(0).And.LessThan(1), "Ignition should ramp in instead of appearing at full strength.");
        }

        [Test]
        public void BeamHasVisibleContactPointAndFadesBeforeRelease()
        {
            var shader = Shader.Find("Starframe/Combat/VoxelGlow");
            if (!shader) Assert.Ignore("Combat voxel glow shader is unavailable in this Editor.");
            var host = Own(new GameObject("Beam VFX timing fixture"));
            var pool = host.AddComponent<SpecialWeaponVisualPool>();
            pool.Initialize();
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.beamDuration = .12f;
            weapon.projectileDiameter = .6f;
            weapon.visualWidth = 1;
            weapon.visualIntensity = 5;
            weapon.effectColor = new Color(.1f, .65f, 1, 1);
            pool.ShowBeam(weapon, Vector3.one, Vector3.forward, 4, null, Vector3.zero);

            var beams = (IList)typeof(SpecialWeaponVisualPool).GetField("beams", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            var beam = beams[0];
            var type = beam.GetType();
            var contact = (Transform)type.GetField("contact").GetValue(beam);
            var contactRenderer = (Renderer)type.GetField("contactRenderer").GetValue(beam);
            Assert.That(contactRenderer.enabled, Is.True);
            Assert.That((contact.position - (Vector3.one + Vector3.forward * 4)).magnitude, Is.LessThan(.001f));

            pool.StepVisuals(.1f);
            var coreRenderer = (Renderer)type.GetField("coreRenderer").GetValue(beam);
            var properties = new MaterialPropertyBlock();
            coreRenderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_Opacity"), Is.GreaterThan(0).And.LessThan(1));
            pool.StepVisuals(.03f);
            Assert.That(contactRenderer.enabled, Is.False);
        }

        [Test] public void UnobstructedLaserBeamHasNoFalseContactGlow()
        {
            var host=Own(new GameObject("Unbounded beam VFX fixture"));var pool=host.AddComponent<SpecialWeaponVisualPool>();pool.Initialize();
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.delivery=WeaponDelivery.Beam;
            pool.ShowBeam(weapon,Vector3.zero,Vector3.forward,100000,null,Vector3.zero,false);
            var beams=(IList)typeof(SpecialWeaponVisualPool).GetField("beams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pool);
            var beam=beams[0];var type=beam.GetType();
            Assert.That(((Renderer)type.GetField("contactRenderer").GetValue(beam)).enabled,Is.False);
            Assert.That(((Renderer)type.GetField("shellRenderer").GetValue(beam)).enabled,Is.True);
            pool.StepVisuals(.02f);
            Assert.That(((Renderer)type.GetField("contactRenderer").GetValue(beam)).enabled,Is.False);
        }
        [TestCase(WeaponFamily.Gatling,5)][TestCase(WeaponFamily.RapidLaser,4)]
        [TestCase(WeaponFamily.BeamLaser,4)][TestCase(WeaponFamily.Railgun,14)]
        public void DirectMuzzleProfilesStayDirectionalAndOnlyRailUsesHeavySparks(WeaponFamily family,int count)
        {
            var host=Own(new GameObject("Weapon family muzzle fixture"));var pool=host.AddComponent<CombatVfxPool>();pool.Initialize();
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.family=family;weapon.tracerStyle=TracerStyle.Continuous;
            pool.EmitMuzzle(Vector3.zero,Vector3.forward,weapon);
            var effects=(IList)typeof(CombatVfxPool).GetField("effects",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pool);
            Assert.That(effects.Count,Is.EqualTo(count),"A continuous tracer is not automatically a railgun.");
            var core=effects[0];var type=core.GetType();var stretch=(Vector3)type.GetField("stretch").GetValue(core);
            Assert.That(stretch.z,Is.GreaterThan(stretch.x*3));
            Assert.That(((Transform)type.GetField("transform").GetValue(core)).forward,Is.EqualTo(Vector3.forward));
        }
        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
    }
}
