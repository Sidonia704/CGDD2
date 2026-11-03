using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class DebrisPhysicsBudgetTests
    {
        GameObject root;BattleTuning rules;FlightShipRuntime observer;DebrisWorld debris;DebrisCluster island;
        [SetUp]public void SetUp()
        {
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
            rules=ScriptableObject.CreateInstance<BattleTuning>();root=new GameObject("Wreck physics budget fixture");
            var original=new GameObject("Living observer");original.transform.SetParent(root.transform,false);
            var assembly=original.AddComponent<ShipAssembly>();assembly.Configure(tuning.database);
            var data=new BlueprintData{name="budget observer",parts=new List<BlockRecord>()};
            for(int x=0;x<5;x++)data.parts.Add(new BlockRecord{id="p"+x,shape=x==0?BlockShape.Core:BlockShape.Cube,position=new Vector3(x-3,.5f,0),rotation=Quaternion.identity});
            assembly.Import(data);observer=original.AddComponent<FlightShipRuntime>();observer.Initialize(assembly,data,tuning,rules);
            debris=root.AddComponent<DebrisWorld>();debris.Initialize(rules,null);
            observer.PartsDetached+=parts=>debris.Spawn(parts,tuning,Vector3.forward*9,observer.CorePosition,observer);
            observer.ApplyDamage("p1",10000);Physics.SyncTransforms();
            Assert.That(observer.IsAlive,Is.True);Assert.That(debris.Clusters.Count,Is.EqualTo(1));island=debris.Clusters[0];
            debris.SetPhysicsObservers(new[]{new RangeCombatWorld.Actor{ship=observer}});
        }
        [TearDown]public void TearDown(){if(root)Object.DestroyImmediate(root);if(rules)Object.DestroyImmediate(rules);BlockModules.Configure(null);}

        [Test]public void DistantBudgetOnlyChangesCollisionModeAndInterpolationThenRestoresNearAnyShip()
        {
            var body=island.Body;body.position=Vector3.right*2000;Physics.SyncTransforms();
            float mass=body.mass,sleep=body.sleepThreshold;var velocity=body.linearVelocity;var spin=body.angularVelocity;
            var colliders=island.GetComponentsInChildren<Collider>();int partCount=debris.PartCount;
            debris.Step(0);
            Assert.That(body.collisionDetectionMode,Is.EqualTo(CollisionDetectionMode.Discrete));Assert.That(body.interpolation,Is.EqualTo(RigidbodyInterpolation.None));
            Assert.That(debris.FarPhysicsClusterCount,Is.EqualTo(1));
            Assert.That(body.mass,Is.EqualTo(mass));Assert.That(body.linearVelocity,Is.EqualTo(velocity));Assert.That(body.angularVelocity,Is.EqualTo(spin));
            Assert.That(body.sleepThreshold,Is.EqualTo(sleep));Assert.That(body.isKinematic,Is.False);Assert.That(body.useGravity,Is.False);
            Assert.That(debris.PartCount,Is.EqualTo(partCount));foreach(var collider in colliders)Assert.That(collider&&collider.enabled,Is.True);
            observer.transform.position=Vector3.right*1970;Physics.SyncTransforms();debris.Step(.5f);
            Assert.That(body.collisionDetectionMode,Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));Assert.That(body.interpolation,Is.EqualTo(RigidbodyInterpolation.Interpolate));
            Assert.That(debris.FarPhysicsClusterCount,Is.Zero);Assert.That(body.linearVelocity,Is.EqualTo(velocity));Assert.That(body.mass,Is.EqualTo(mass));
            foreach(var collider in colliders)Assert.That(collider&&collider.enabled,Is.True);
        }
        [Test]public void HysteresisAvoidsSwitchingInTheMiddleBandAndObserverRemovalRestoresFineMode()
        {
            island.Body.position=Vector3.right*2000;Physics.SyncTransforms();debris.Step(0);
            observer.transform.position=Vector3.right*1500;Physics.SyncTransforms();debris.Step(.5f);
            Assert.That(debris.FarPhysicsClusterCount,Is.EqualTo(1),"A distant island stays low-detail within the hysteresis band.");
            debris.SetPhysicsObservers(null);
            Assert.That(island.Body.collisionDetectionMode,Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));Assert.That(island.Body.interpolation,Is.EqualTo(RigidbodyInterpolation.Interpolate));
            Assert.That(debris.FarPhysicsClusterCount,Is.Zero);
        }
        [Test]public void FastApproachRestoresBeforeNextHalfSecondEvaluationCanReachTheIsland()
        {
            island.Body.position=Vector3.right*2000;Physics.SyncTransforms();debris.Step(0);
            var fast=observer.gameObject.AddComponent<Rigidbody>();fast.isKinematic=false;fast.useGravity=false;fast.linearVelocity=Vector3.right*1000;
            observer.transform.position=Vector3.right*1550;Physics.SyncTransforms();debris.SetPhysicsObservers(new[]{new RangeCombatWorld.Actor{ship=observer,body=fast}});debris.Step(0);
            Assert.That(debris.FarPhysicsClusterCount,Is.Zero,"Closing-speed guard expands the 350m wake boundary.");
            Assert.That(island.Body.collisionDetectionMode,Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));
        }
    }
}
