using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;
namespace Starframe.Construction.Tests
{
    public sealed class ContinuousRailTracerTests
    {
        readonly List<Object> owned=new List<Object>();ProjectileWorld world;
        [TearDown]public void Cleanup()
        {
            world?.Clear();BlockModules.Configure(null);
            foreach(var go in owned.OfType<GameObject>()){
                var pool=go?go.GetComponent<LaserVisualPool>():null;if(!pool)continue;
                var field=typeof(LaserVisualPool).GetField("sharedMaterial",BindingFlags.Instance|BindingFlags.NonPublic);
                var material=field.GetValue(pool) as Material;if(material)Object.DestroyImmediate(material);field.SetValue(pool,null);
            }
            for(int i=owned.Count-1;i>=0;i--)if(owned[i])Object.DestroyImmediate(owned[i]);owned.Clear();
        }
        [Test]public void ContinuousTracerHasSolidCoverageBetweenTailAndHead()
        {
            var pool=Pool();
            int handle=pool.Acquire(Vector3.zero,Vector3.forward,.32f,140,420,null,default,10,11,new Color(1,.66f,.12f),0,TracerStyle.Continuous,.09f);
            pool.Move(handle,Vector3.forward*400,Vector3.forward);
            var filter=pool.GetComponentInChildren<MeshFilter>();var query=new CylindricalHitQuery();
            foreach(float z in new[]{40f,100f,200f,300f,380f})
                Assert.That(query.TryHitMesh(filter.sharedMesh,filter.transform.localToWorldMatrix,new Vector3(-10,0,z),Vector3.right,.001f,0,20,out _),Is.True,"A continuous rail beam must not contain the gaps of the voxel tracer.");
            Assert.That(pool.GetComponentsInChildren<MeshRenderer>().Count(r=>r.enabled),Is.EqualTo(2),"Gold outer beam and bright inner core.");
        }
        [Test]public void OneStepFourteenThousandSpeedHitRetainsVisibleTraceThenFadesAndRecycles()
        {
            var owner=Ship(new Vector3(-40,5100,0));var target=Ship(new Vector3(0,5100,100));
            var pool=Pool();world=new ProjectileWorld();world.Initialize(pool);
            var weapon=Own(ScriptableObject.CreateInstance<WeaponDefinition>());weapon.projectileSpeed=14000;weapon.projectileDiameter=.32f;
            weapon.damage=40;weapon.penetrationDepth=1;weapon.range=2000;weapon.visualLength=140;weapon.tailLength=420;
            weapon.tracerStyle=TracerStyle.Continuous;weapon.tracerLingerSeconds=.09f;weapon.visualWidth=10;
            Physics.SyncTransforms();world.Fire(owner,weapon,new Vector3(0,5100.5f,0),Vector3.forward);world.Step(.02f);
            Assert.That(target.DamageTaken,Is.EqualTo(40).Within(.001f));Assert.That(world.ActiveCount,Is.Zero,"The real swept shot has already resolved.");
            Assert.That(pool.ActiveVisualCount,Is.EqualTo(1));Assert.That(pool.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled),Is.True,"A same-step hit must still be visible.");
            pool.StepVisuals(.04f);
            var properties=new MaterialPropertyBlock();pool.GetComponentInChildren<MeshRenderer>().GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_Opacity"),Is.GreaterThan(0).And.LessThan(1));
            pool.StepVisuals(.06f);Assert.That(pool.ActiveVisualCount,Is.Zero);Assert.That(pool.GetComponentsInChildren<MeshRenderer>().Any(r=>r.enabled),Is.False);
        }
        [Test]public void GatlingWorldLengthCapIsAppliedAfterGlobalEffectScaling()
        {
            var pool=Own(new GameObject("Scaled Gatling tracer pool")).AddComponent<LaserVisualPool>();
            pool.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"),3);
            float cap=.7f*2700/24;
            int handle=pool.Acquire(Vector3.zero,Vector3.forward,.7f,10,160,null,default,2.6f,6.5f,Color.yellow,2.2f,TracerStyle.Continuous,0,cap);
            pool.Move(handle,Vector3.forward*1000,Vector3.forward);
            var filter=pool.GetComponentInChildren<MeshFilter>();
            Assert.That(filter.transform.localScale.y,Is.EqualTo(cap).Within(.001f));
            Assert.That(filter.transform.localScale.y,Is.LessThan(2700/24f),"Full-speed shots must keep a visible gap.");
        }
        [Test]public void GrowingBeamClipsAtMovingMuzzleAndImpactFadeFreezesBothEndpoints()
        {
            var pool=Pool();var muzzle=Own(new GameObject("Rail muzzle")).transform;
            int handle=pool.Acquire(Vector3.zero,Vector3.forward,.32f,140,420,muzzle,default,10,11,null,0,TracerStyle.Continuous,.09f);
            muzzle.position=Vector3.forward*1.6f;pool.Move(handle,Vector3.forward*2,Vector3.forward);
            var filter=pool.GetComponentInChildren<MeshFilter>();
            foreach(var vertex in filter.sharedMesh.vertices){float z=filter.transform.TransformPoint(vertex).z;Assert.That(z,Is.InRange(1.5999f,2.0001f));}
            Vector3 before=filter.transform.position;Vector3 scale=filter.transform.localScale;pool.Release(handle);
            muzzle.position=Vector3.forward*100;pool.Move(handle,Vector3.one*999,Vector3.up);Object.DestroyImmediate(muzzle.gameObject);pool.StepVisuals(.02f);
            Assert.That(Vector3.Distance(filter.transform.position,before),Is.LessThan(.00001f));
            Assert.That(Vector3.Distance(filter.transform.localScale,scale),Is.LessThan(.00001f),"Fading is opacity only; destroyed muzzles must not drag the trace.");
        }
        LaserVisualPool Pool(){var pool=Own(new GameObject("Continuous rail pool")).AddComponent<LaserVisualPool>();pool.Initialize(Shader.Find("Starframe/Combat/VoxelGlow"));return pool;}
        FlightShipRuntime Ship(Vector3 position)
        {
            var db=Own(ScriptableObject.CreateInstance<ModuleDatabase>());var d=Own(ScriptableObject.CreateInstance<ModuleDefinition>());d.shape=BlockShape.Core;d.maxHp=1000;db.modules.Add(d);
            var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=db;
            var go=Own(new GameObject("Rail hit fixture"));go.transform.position=position;var body=go.AddComponent<Rigidbody>();body.isKinematic=true;body.useGravity=false;
            var assembly=go.AddComponent<ShipAssembly>();assembly.Configure(db);Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True);
            var runtime=go.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,assembly.Export("Rail hit fixture"),tuning);return runtime;
        }
        T Own<T>(T value)where T:Object{owned.Add(value);return value;}
    }
}
