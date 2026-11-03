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
            Assert.That(db.modules.Count(d=>d.category==ModuleCategory.Hull),Is.EqualTo(8),"Exactly the eight ordinary hull shapes are free.");
            var seen=new HashSet<BlockShape>();
            foreach(var d in db.modules)
            {
                Assert.That(seen.Add(d.shape),Is.True,d.shape+" duplicate");
                if(d.category==ModuleCategory.Hull)Assert.That(d.cost,Is.Zero,d.shape+" ordinary hull is intentionally free.");
                else AssertPositiveFinite(d.cost,d.shape+" functional/armor cost");
                AssertPositiveFinite(d.massKg,d.shape+" mass");
                AssertPositiveFinite(d.maxHp,d.shape+" durability");AssertPositiveFinite(d.recoverySeconds,d.shape+" recovery");
                if(d.weapon){
                    AssertPositiveFinite(d.weapon.damage,d.shape+" damage");AssertPositiveFinite(d.weapon.shotsPerSecond,d.shape+" cadence");
                    AssertPositiveFinite(d.weapon.projectileSpeed,d.shape+" projectile speed");AssertPositiveFinite(d.weapon.range,d.shape+" range");
                    AssertPositiveFinite(d.weapon.sustainedPowerWatts,d.shape+" power demand");
                }
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
        [Test] public void RepackedAndRefilledPartsFillTheirReservedCellsWithoutSpareColumns()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var rail=db.Get(BlockShape.Railgun2);
            Assert.That(rail.occupiedCells.Length,Is.EqualTo(5),"A 0.82-wide rail must not reserve a second column.");
            Assert.That(rail.mesh.bounds.size.x,Is.LessThanOrEqualTo(.98f));
            Assert.That(Mathf.Abs(rail.mesh.bounds.center.x),Is.LessThan(.001f),"The recentred rail must sit on its single column.");
            Assert.That(rail.muzzleLocalPosition.x,Is.EqualTo(rail.mesh.bounds.center.x).Within(.05f));
            Assert.That(rail.muzzleLocalPosition.z,Is.GreaterThan(rail.mesh.bounds.max.z));
            var power=db.Get(BlockShape.Power2);
            Assert.That(power.occupiedCells.Length,Is.EqualTo(8));
            foreach(var axis in new[]{power.mesh.bounds.size.x,power.mesh.bounds.size.y,power.mesh.bounds.size.z})
                Assert.That(axis,Is.GreaterThanOrEqualTo(1.95f),"A 2x2x2 part must fill its box like Block8 and Shield2.");
            Assert.That(power.mesh.bounds.min.y,Is.EqualTo(-.5f).Within(.001f));
            foreach(var d in db.modules)
            {
                if((int)d.shape<10)continue;
                int cellsX=d.occupiedCells.Max(c=>c.x)+1,cellsY=d.occupiedCells.Max(c=>c.y)+1,cellsZ=d.occupiedCells.Max(c=>c.z)+1;
                var size=d.mesh.bounds.size;
                if(cellsX>1)Assert.That(size.x,Is.GreaterThan(cellsX-1),d.shape+" over-reserves X");
                if(cellsY>1)Assert.That(size.y,Is.GreaterThan(cellsY-1),d.shape+" over-reserves Y");
                if(cellsZ>1)Assert.That(size.z,Is.GreaterThan(cellsZ-1),d.shape+" over-reserves Z");
            }
        }
        [Test] public void MultiBarrelWeaponsGetOneGlowAccentPerBore()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var module=db.Get(BlockShape.Gun1);
            var host=new GameObject("Multi-barrel glow fixture");
            try
            {
                var assembly=host.AddComponent<ShipAssembly>();assembly.Configure(db);
                Assert.That(assembly.Place(BlockShape.Core,new Vector3(0,.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
                Assert.That(assembly.Place(BlockShape.Gun1,new Vector3(0,1.5f,0),Quaternion.identity),Is.True,assembly.PlacementFailure);
                var gun=host.GetComponentsInChildren<Transform>().First(t=>t.name.StartsWith("Gun1_"));
                var accents=gun.GetComponentsInChildren<MeshFilter>().Where(f=>f.name=="ModuleGlowAccent").ToArray();
                Assert.That(accents.Length,Is.EqualTo(module.weapon.muzzleOffsets.Length),"One accent per real bore, not one averaged muzzle");
                var positions=accents.Select(a=>a.transform.localPosition).ToArray();
                foreach(var offset in module.weapon.muzzleOffsets)
                {
                    var bore=module.muzzleLocalPosition+offset;
                    Assert.That(positions.Any(p=>(p-bore).sqrMagnitude<1e-6f),Is.True,"Missing accent at bore "+offset);
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(host);}
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
                Assert.That(d.weapon.delivery,Is.EqualTo(WeaponDelivery.Beam));
                Assert.That(d.weapon.range,Is.GreaterThan(d.mesh.bounds.size.z),"The energy blade must extend beyond its permanent emitter.");
                Assert.That(d.weapon.range,Is.LessThan(db.Get(BlockShape.Laser).weapon.range/10),"Blades retain a close-range role, rather than competing with projectile reach.");
            }
            AssertWeaponTierUpgrade(db.Get(BlockShape.Sword1),db.Get(BlockShape.Sword2));
            Assert.That(db.Get(BlockShape.Sword2).weapon.range,Is.GreaterThan(db.Get(BlockShape.Sword1).weapon.range));
        }
        [Test] public void CurrentApprovedLaserAndPowerBalanceHasTieredTradeoffs()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var laser=db.Get(BlockShape.Laser);var medium=db.Get(BlockShape.Laser2);var heavy=db.Get(BlockShape.Laser3);
            AssertWeaponTierUpgrade(laser,medium);AssertWeaponTierUpgrade(medium,heavy);
            Assert.That(medium.weapon.shotsPerSecond,Is.GreaterThanOrEqualTo(laser.weapon.shotsPerSecond));
            Assert.That(heavy.weapon.shotsPerSecond,Is.GreaterThanOrEqualTo(medium.weapon.shotsPerSecond));
            var power=db.Get(BlockShape.Power);var large=db.Get(BlockShape.Power2);
            Assert.That(power.generationWatts,Is.GreaterThan(laser.weapon.sustainedPowerWatts),"One generator sustains one basic laser.");
            Assert.That(power.generationWatts,Is.LessThan(2*laser.weapon.sustainedPowerWatts),"Two basic lasers draw on finite storage.");
            Assert.That(large.cost,Is.GreaterThan(power.cost));Assert.That(large.massKg,Is.GreaterThan(power.massKg));
            Assert.That(large.generationWatts,Is.GreaterThan(4*power.generationWatts));
            Assert.That(large.generationWatts,Is.LessThan(8*power.generationWatts),"Eight compact generators trade extra cost and mass for more generation.");
            Assert.That(large.storageJoules,Is.GreaterThan(power.storageJoules));
            Assert.That(large.cost,Is.LessThan(8*power.cost));Assert.That(large.massKg,Is.LessThan(8*power.massKg));
            var shield=db.Get(BlockShape.Shield);var largeShield=db.Get(BlockShape.Shield2);
            Assert.That(largeShield.shieldCapacity,Is.GreaterThan(shield.shieldCapacity));
            Assert.That(largeShield.shieldCapacity,Is.LessThan(8*shield.shieldCapacity));
            AssertPositiveFinite(shield.shieldPowerWatts,"Small shield power");
            Assert.That(largeShield.shieldPowerWatts,Is.GreaterThan(shield.shieldPowerWatts));
            Assert.That(largeShield.shieldRecoveryPerSecond,Is.GreaterThan(shield.shieldRecoveryPerSecond));
        }
        [Test] public void CurrentApprovedKineticAndRailRolesKeepPulsesReadable()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            for(int tier=0;tier<3;tier++)
            {
                var gun=db.Get(BlockShape.Gun1+tier);var laser=db.Get(tier==0?BlockShape.Laser:BlockShape.Laser2+tier-1);var w=gun.weapon;
                Assert.That(w.shotsPerSecond,Is.GreaterThan(laser.weapon.shotsPerSecond),"Kinetics own the rapid-fire role.");
                Assert.That(w.projectileSpeed,Is.GreaterThan(laser.weapon.projectileSpeed));
                Assert.That(w.range,Is.LessThan(laser.weapon.range));
                Assert.That(w.sustainedPowerWatts,Is.LessThan(laser.weapon.sustainedPowerWatts));
                Assert.That(gun.cost,Is.LessThan(laser.cost));
                if(tier>0){
                    var previous=db.Get(BlockShape.Gun1+tier-1);AssertWeaponTierUpgrade(previous,gun);
                    Assert.That(w.shotsPerSecond,Is.GreaterThan(previous.weapon.shotsPerSecond));
                    Assert.That(w.projectileSpeed,Is.GreaterThan(previous.weapon.projectileSpeed));
                }
                Assert.That(w.visualLength,Is.EqualTo(4+tier),"Gun tracer head tier "+tier);
                Assert.That(w.visualWidth,Is.EqualTo(2.2f+tier*.2f),"Gun tracer width tier "+tier);
            }
            // Keep the exact authored visual contract independently of balance tuning.
            for(int tier=0;tier<3;tier++)
            {
                var rail=db.Get(BlockShape.Railgun1+tier);var w=rail.weapon;var gun=db.Get(BlockShape.Gun1+tier).weapon;
                Assert.That(w.tracerStyle,Is.EqualTo(TracerStyle.Continuous),"Rail tracer style tier "+tier);
                Assert.That(w.tracerLingerSeconds,Is.EqualTo(.09f).Within(.0001f));
                Assert.That(w.shotsPerSecond,Is.LessThan(gun.shotsPerSecond));
                Assert.That(w.damage,Is.GreaterThan(gun.damage));Assert.That(w.projectileSpeed,Is.GreaterThan(gun.projectileSpeed));
                Assert.That(w.firstShotImmediate,Is.True,"Rail must fire on the first powered press.");
                if(tier>0){
                    var previous=db.Get(BlockShape.Railgun1+tier-1);AssertWeaponTierUpgrade(previous,rail);
                    Assert.That(w.shotsPerSecond,Is.LessThan(previous.weapon.shotsPerSecond),"Larger rail pulses trade cadence for damage.");
                    Assert.That(w.projectileSpeed,Is.GreaterThan(previous.weapon.projectileSpeed));
                    Assert.That(w.penetrationDepth,Is.GreaterThan(previous.weapon.penetrationDepth));
                }
                Assert.That(w.tailLength,Is.EqualTo(new[]{200f,300f,420f}[tier]));
                Assert.That(w.visualWidth,Is.EqualTo(8+tier));
                Assert.That(w.minimumVisualPixels,Is.GreaterThanOrEqualTo(4.5f));
            }
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
                var d=db.Get(shape);var w=d.weapon;Assert.That(w.delivery,Is.EqualTo(WeaponDelivery.GuidedMissile));AssertPositiveFinite(w.missileLockSeconds,shape+" lock time");Assert.That(w.reloadSeconds,Is.GreaterThan(w.missileLockSeconds));Assert.That(w.shotsPerSecond*w.reloadSeconds,Is.EqualTo(1).Within(.0001f),"Missile cadence must match its real reload cycle.");Assert.That(w.requireTargetLock,Is.True);Assert.That(w.missileLaunchOffsets.Length,Is.EqualTo(shape==BlockShape.Missile2?2:1));
                if(shape!=BlockShape.Missile1){
                    var previous=db.Get(shape-1);AssertWeaponTierUpgrade(previous,d);
                    Assert.That(w.reloadSeconds,Is.GreaterThan(previous.weapon.reloadSeconds));
                    Assert.That(w.missileLockSeconds,Is.GreaterThan(previous.weapon.missileLockSeconds));
                    Assert.That(w.explosionRadius,Is.GreaterThan(previous.weapon.explosionRadius));
                }
                Assert.That(w.launcherMesh,Is.Not.Null);Assert.That(w.missileMesh,Is.Not.Null);Assert.That(w.launcherMesh.vertexCount,Is.LessThan(d.mesh.vertexCount));Assert.That(w.missileMesh.bounds.center.magnitude,Is.LessThan(.001f));Assert.That(w.missileMesh.bounds.size.z,Is.GreaterThan(w.missileMesh.bounds.size.x));Assert.That(w.effectColor.r,Is.GreaterThan(w.effectColor.b));
            }
        }
        static void AssertPositiveFinite(float value,string description)
        {
            Assert.That(!float.IsNaN(value)&&!float.IsInfinity(value)&&value>0,Is.True,description+" must be finite and positive.");
        }
        static void AssertWeaponTierUpgrade(ModuleDefinition smaller,ModuleDefinition larger)
        {
            Assert.That(larger.cost,Is.GreaterThan(smaller.cost),larger.shape+" cost tier");
            Assert.That(larger.massKg,Is.GreaterThan(smaller.massKg),larger.shape+" mass tier");
            Assert.That(larger.maxHp,Is.GreaterThan(smaller.maxHp),larger.shape+" durability tier");
            Assert.That(larger.weapon.damage,Is.GreaterThan(smaller.weapon.damage),larger.shape+" damage tier");
            Assert.That(larger.weapon.sustainedPowerWatts,Is.GreaterThan(smaller.weapon.sustainedPowerWatts),larger.shape+" power tier");
            Assert.That(larger.weapon.range,Is.GreaterThanOrEqualTo(smaller.weapon.range),larger.shape+" range tier");
        }
    }
}
