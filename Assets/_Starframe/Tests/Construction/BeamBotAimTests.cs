using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Combat;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Tests
{
    public sealed class BeamBotAimTests
    {
        readonly List<WeaponDefinition> owned=new List<WeaponDefinition>();
        [TearDown]public void Cleanup(){foreach(var weapon in owned)Object.DestroyImmediate(weapon);owned.Clear();}

        [Test]public void UnboundedBeamAimsAtFastCrossingTargetsCurrentPositionBeyondLegacyRange()
        {
            var beam=Weapon(WeaponDelivery.Beam);beam.range=45;beam.unboundedBeam=true;beam.projectileSpeed=1400;
            Vector3 target=Vector3.forward*1000;
            var profile=new WeaponAimProfile{weapon=beam,origin=Vector3.zero,direction=Vector3.forward};
            Assert.That(RangeBotPilot.TryFiringSolution(profile,target,Vector3.right*5000,5,out Vector3 point),Is.True);
            Assert.That(point,Is.EqualTo(target),"An instantaneous beam cannot lead a target by projectile travel time.");
            beam.projectileSpeed=0;
            Assert.That(RangeBotPilot.TryFiringSolution(profile,target,Vector3.right*5000,5,out point),Is.True,
                "Beam aiming does not depend on the unused projectile speed.");
        }

        [Test]public void ProjectileStillRequiresFiniteSpeedLeadForCrossingTargets()
        {
            var projectile=Weapon(WeaponDelivery.Projectile);projectile.range=2000;projectile.projectileSpeed=1400;
            Vector3 target=Vector3.forward*1000,velocity=Vector3.right*500;
            var profile=new WeaponAimProfile{weapon=projectile,origin=Vector3.zero,direction=Vector3.forward};
            Assert.That(RangeBotPilot.TryFiringSolution(profile,target,velocity,180,out Vector3 point),Is.False,
                "A barrel aimed directly at the current position is not aligned with the intercept.");
            Assert.That(point.x,Is.GreaterThan(300));Assert.That(point.z,Is.EqualTo(target.z));
            profile.direction=point.normalized;
            Assert.That(RangeBotPilot.TryFiringSolution(profile,target,velocity,5,out Vector3 aligned),Is.True);
            Assert.That(aligned,Is.EqualTo(point));
        }

        [Test]public void GenericBoundedBeamRetainsItsAuthoredRangeLimitWithoutLead()
        {
            var beam=Weapon(WeaponDelivery.Beam);beam.range=45;beam.unboundedBeam=false;
            var profile=new WeaponAimProfile{weapon=beam,origin=Vector3.zero,direction=Vector3.forward};
            Assert.That(RangeBotPilot.TryFiringSolution(profile,Vector3.forward*46,Vector3.right*5000,5,out _),Is.False);
            Assert.That(RangeBotPilot.TryFiringSolution(profile,Vector3.forward*45,Vector3.right*5000,5,out Vector3 point),Is.True);
            Assert.That(point,Is.EqualTo(Vector3.forward*45));
        }

        WeaponDefinition Weapon(WeaponDelivery delivery)
        {
            var weapon=ScriptableObject.CreateInstance<WeaponDefinition>();weapon.delivery=delivery;
            owned.Add(weapon);return weapon;
        }
    }
}
