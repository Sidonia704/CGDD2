using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class ExpandedComponentAssetTests
    {
        const string Root="Assets/_Starframe/";
        [Test] public void ReplacingDefinitionInClonedDatabaseKeepsProductionAssetsIsolated()
        {
            var production=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var original=production.Get(BlockShape.Laser);
            float sourceSpeed=original.weapon.projectileSpeed;
            var copy=UnityEngine.Object.Instantiate(production);
            var replacement=UnityEngine.Object.Instantiate(original);
            var isolatedWeapon=UnityEngine.Object.Instantiate(original.weapon);
            try
            {
                copy.modules=new List<ModuleDefinition>(production.modules);
                Assert.That(copy.Get(BlockShape.Laser),Is.SameAs(original),"Prime the cloned database cache before replacing its list entry.");
                replacement.weapon=isolatedWeapon;
                int slot=copy.modules.FindIndex(d=>d.shape==BlockShape.Laser);
                copy.modules[slot]=replacement;
                Assert.That(copy.modules.Count,Is.EqualTo(production.modules.Count),"This exercises equal-count replacement.");
                Assert.That(copy.Get(BlockShape.Laser),Is.SameAs(replacement),"A stale cache must not return the production module.");
                copy.Get(BlockShape.Laser).weapon.projectileSpeed=sourceSpeed+321;
                Assert.That(production.Get(BlockShape.Laser),Is.SameAs(original));
                Assert.That(production.Get(BlockShape.Laser).weapon.projectileSpeed,Is.EqualTo(sourceSpeed));
                Assert.That(copy.Get(BlockShape.Laser).weapon.projectileSpeed,Is.EqualTo(sourceSpeed+321));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(copy);
                UnityEngine.Object.DestroyImmediate(replacement);
                UnityEngine.Object.DestroyImmediate(isolatedWeapon);
            }
        }
        [Test] public void EveryShapeHasCompleteProductionAssetsAndAlignedOccupancy()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            Assert.That(db.modules.Count,Is.EqualTo(Enum.GetValues(typeof(BlockShape)).Length));
            var seen=new HashSet<BlockShape>();
            foreach(var d in db.modules)
            {
                Assert.That(seen.Add(d.shape),Is.True,d.shape+" duplicate");
                Assert.That(d.mesh,Is.Not.Null,d.shape+" mesh");Assert.That(d.material,Is.Not.Null,d.shape+" material");
                Assert.That(d.icon,Is.Not.Null,d.shape+" icon");Assert.That(d.selectedIcon,Is.Not.Null,d.shape+" selected icon");
                Assert.That(d.mesh.vertexCount,Is.GreaterThan(30),d.shape+" geometry");
                if((int)d.shape<10)continue;
                var bounds=d.mesh.bounds;
                Assert.That(bounds.min.y,Is.EqualTo(-.5f).Within(.001f),d.shape+" mounting plane");
                foreach(var vertex in d.mesh.vertices)
                {
                    bool inCell=false;
                    foreach(var occupied in d.occupiedCells)
                        if(vertex.x>=occupied.x-.501f&&vertex.x<=occupied.x+.501f&&
                           vertex.y>=occupied.y-.501f&&vertex.y<=occupied.y+.501f&&
                           vertex.z>=occupied.z-.501f&&vertex.z<=occupied.z+.501f){inCell=true;break;}
                    Assert.That(inCell,Is.True,d.shape+" vertex lies outside reserved body cells: "+vertex);
                }
            }
        }
        [Test] public void WeaponsExposeRealBarrelMuzzlesAndIndependentEnergyBlades()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            Assert.That(db.Get(BlockShape.Gun1).weapon.muzzleOffsets.Length,Is.EqualTo(2));
            Assert.That(db.Get(BlockShape.Gun2).weapon.muzzleOffsets.Length,Is.EqualTo(4));
            Assert.That(db.Get(BlockShape.Gun3).weapon.muzzleOffsets.Length,Is.EqualTo(6));
            foreach(var shape in new[]{BlockShape.Sword1,BlockShape.Sword2})
            {
                var d=db.Get(shape);Assert.That(d.weapon.beamMesh,Is.Not.Null);
                Assert.That(d.weapon.beamMesh.bounds.min.z,Is.EqualTo(0).Within(.001f));
                Assert.That(d.weapon.beamMesh.bounds.max.z,Is.EqualTo(1).Within(.001f));
                Assert.That(d.mesh.bounds.size.z,Is.GreaterThan(shape==BlockShape.Sword1?2.5f:3.5f),"The supplied triangular body is a permanent physical component.");
                Assert.That(d.muzzleLocalPosition.z,Is.GreaterThan(d.mesh.bounds.max.z),"The independent energy blade emits from the physical tip.");
                Assert.That(d.weapon.range,Is.EqualTo(40));
            }
        }
        [Test] public void ExpansionPreservesOriginalLaserAndPowerBalance()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            string weaponPath=AssetDatabase.GetAssetPath(db.Get(BlockShape.Laser).weapon);
            string before="Documentation/ComponentLibrary-2026-10-25/Before/"+Path.GetFileName(weaponPath);
            Assert.That(File.ReadAllBytes(weaponPath).SequenceEqual(File.ReadAllBytes(before)),Is.True,"The original serialized weapon asset must remain unchanged.");
            // Other physics fixtures can temporarily tune the shared in-memory asset. This check
            // deliberately verifies the production asset on disk, then reads a synchronous reimport.
            AssetDatabase.ImportAsset(weaponPath,ImportAssetOptions.ForceSynchronousImport|ImportAssetOptions.ForceUpdate);
            var old=db.Get(BlockShape.Laser);Assert.That(old.massKg,Is.EqualTo(600));Assert.That(old.weapon.damage,Is.EqualTo(25));Assert.That(old.weapon.shotsPerSecond,Is.EqualTo(12));Assert.That(old.weapon.projectileSpeed,Is.EqualTo(1400));Assert.That(old.weapon.sustainedPowerWatts,Is.EqualTo(50));
            Assert.That(db.Get(BlockShape.Power).generationWatts,Is.EqualTo(60));Assert.That(db.Get(BlockShape.Power).storageJoules,Is.EqualTo(500));
            Assert.That(db.Get(BlockShape.Power2).generationWatts,Is.GreaterThan(8*db.Get(BlockShape.Power).generationWatts));
            Assert.That(db.Get(BlockShape.Shield2).shieldCapacity,Is.LessThan(8*db.Get(BlockShape.Shield).shieldCapacity));
        }
        [Test] public void ArmorIsPassiveDurabilityAndMissilesHaveSeparateLoadedAndFlyingMeshes()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            foreach(var shape in new[]{BlockShape.Armor1,BlockShape.Armor2,BlockShape.Armor3,BlockShape.Armor4})
            {
                var d=db.Get(shape);Assert.That(d.category,Is.EqualTo(ModuleCategory.Shield));Assert.That(d.shieldCapacity,Is.Zero);Assert.That(d.shieldSurfaceMesh,Is.Null);Assert.That(d.maxHp/d.occupiedCells.Length,Is.GreaterThan(2*db.Get(BlockShape.Cube).maxHp));
            }
            foreach(var shape in new[]{BlockShape.Missile1,BlockShape.Missile2,BlockShape.Missile3})
            {
                var d=db.Get(shape);var w=d.weapon;Assert.That(w.delivery,Is.EqualTo(WeaponDelivery.GuidedMissile));Assert.That(w.missileLockSeconds,Is.EqualTo(2));Assert.That(w.reloadSeconds,Is.GreaterThan(2));Assert.That(w.requireTargetLock,Is.True);Assert.That(w.missileLaunchOffsets.Length,Is.EqualTo(shape==BlockShape.Missile2?2:1));
                Assert.That(w.launcherMesh,Is.Not.Null);Assert.That(w.missileMesh,Is.Not.Null);Assert.That(w.launcherMesh.vertexCount,Is.LessThan(d.mesh.vertexCount));Assert.That(w.missileMesh.bounds.center.magnitude,Is.LessThan(.001f));Assert.That(w.missileMesh.bounds.size.z,Is.GreaterThan(w.missileMesh.bounds.size.x));Assert.That(w.effectColor.r,Is.GreaterThan(w.effectColor.b));
            }
        }
    }
}
