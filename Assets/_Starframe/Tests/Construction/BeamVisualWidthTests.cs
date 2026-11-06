using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class BeamVisualWidthTests
    {
        readonly List<Object> owned=new List<Object>();
        [TearDown]public void Cleanup(){for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();}
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
        static object Beam(SpecialWeaponVisualPool pool)=>((IList)typeof(SpecialWeaponVisualPool).GetField("beams",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pool))[0];
        static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name).GetValue(value);
        [TestCase("Sword1Weapon")][TestCase("Sword2Weapon")]
        public void HitAndMissWidthsStayAnchoredToTheOriginalShaftAcrossReuse(string asset)
        {
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/"+asset+".asset");
            var source=Own(new GameObject("Beam source")).transform;
            var pool=Own(new GameObject("Beam width fixture")).AddComponent<SpecialWeaponVisualPool>();pool.Initialize();
            float collision=weapon.projectileDiameter;
            Mesh original=weapon.beamMesh;
            float baseline=collision*weapon.visualWidth*Mathf.Max(original.bounds.size.x,original.bounds.size.y);
            foreach(bool hit in new[]{false,true,false,true})
            {
                pool.ShowBeam(weapon,Vector3.zero,Vector3.forward,hit?500:100000,source,Vector3.zero,hit);
                var beam=Beam(pool);var shell=Field<Transform>(beam,"shell");var core=Field<Transform>(beam,"core");
                Assert.That(shell.localScale.x,Is.EqualTo(baseline*(hit?1.95f:1.3f)).Within(.0001f));
                Assert.That(core.localScale.x/shell.localScale.x,Is.EqualTo(.32f).Within(.0001f));
                Assert.That(Field<Renderer>(beam,"contactRenderer").enabled,Is.EqualTo(hit));
                pool.StepVisuals(.01f);
                Assert.That(shell.localScale.x,Is.EqualTo(baseline*(hit?1.95f:1.3f)).Within(.0001f));
            }
            Assert.That(weapon.beamMesh,Is.SameAs(original));Assert.That(weapon.projectileDiameter,Is.EqualTo(collision));
            var final=Beam(pool);pool.StepVisuals(1);Assert.That(Field<Renderer>(final,"shellRenderer").enabled,Is.False);
        }
        [Test]public void UnobstructedLongShaftHasTheSameCrossSectionAtItsMuzzleAndFarEnd()
        {
            var weapon=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Sword2Weapon.asset");
            var pool=Own(new GameObject("Constant long shaft")).AddComponent<SpecialWeaponVisualPool>();pool.Initialize();
            pool.ShowBeam(weapon,Vector3.zero,Vector3.forward,100000,null,Vector3.zero,false);
            var beam=Beam(pool);var shell=Field<Transform>(beam,"shell");var mesh=Field<MeshFilter>(beam,"shellFilter").sharedMesh;
            float near=0,far=0;
            foreach(var vertex in mesh.vertices){var world=shell.TransformPoint(vertex);if(world.z<1)near=Mathf.Max(near,Mathf.Abs(world.x));else far=Mathf.Max(far,Mathf.Abs(world.x));}
            Assert.That(near,Is.GreaterThan(0));Assert.That(far,Is.EqualTo(near).Within(.0001f));
            Assert.That(Field<Renderer>(beam,"contactRenderer").enabled,Is.False);
        }
        [Test]public void GenericShortBeamRetainsItsAuthoredShapeAndWidth()
        {
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.delivery=WeaponDelivery.Beam;weapon.projectileDiameter=1;weapon.visualWidth=2;
            weapon.beamMesh=AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Starframe/Settings/Modules/Sword1Weapon.asset").beamMesh;
            var pool=Own(new GameObject("Legacy shaped beam")).AddComponent<SpecialWeaponVisualPool>();pool.Initialize();
            pool.ShowBeam(weapon,Vector3.zero,Vector3.forward,10,null,Vector3.zero,true);
            var beam=Beam(pool);Assert.That(Field<MeshFilter>(beam,"shellFilter").sharedMesh,Is.SameAs(weapon.beamMesh));
            Assert.That(Field<Transform>(beam,"shell").localScale.x,Is.EqualTo(2));
        }
    }
}
