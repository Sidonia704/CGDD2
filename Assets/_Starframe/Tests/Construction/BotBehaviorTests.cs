using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class BotBehaviorTests
    {
        [Test]
        public void AllSevenRolesHaveFiniteUsefulRangesAndDifferentFlightStyles()
        {
            var profile = ScriptableObject.CreateInstance<BotBehaviorProfile>();
            try
            {
                foreach (BotCombatRole role in Enum.GetValues(typeof(BotCombatRole)))
                {
                    profile.ConfigureRole(role);
                    Assert.That(profile.preferredRange, Is.InRange(200, 1500));
                    Assert.That(profile.attackSpeedFraction, Is.InRange(.1f, 1));
                    Assert.That(profile.cruiseSpeedFraction, Is.GreaterThanOrEqualTo(profile.attackSpeedFraction));
                    Assert.That(profile.attackPass, Is.EqualTo(role == BotCombatRole.Interceptor || role == BotCombatRole.Assault));
                }
                profile.ConfigureRole(BotCombatRole.Interceptor);
                float interceptSpeed = profile.attackSpeedFraction, interceptRange = profile.preferredRange;
                profile.ConfigureRole(BotCombatRole.Capital);
                Assert.That(profile.attackSpeedFraction, Is.LessThan(interceptSpeed * .5f));
                Assert.That(profile.preferredRange, Is.GreaterThan(interceptRange * 2));
                Assert.That(profile.attackPass, Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(profile); }
        }

        [Test]
        public void AttackVelocityHoldsDistanceAndMatchesMovingTargetsWithoutAnAutomaticEscape()
        {
            Vector3 target = Vector3.right * 60;
            var matching = RangeBotPilot.AttackVelocity(Vector3.forward, target, 500, 500, 80, Vector3.right, 0);
            Assert.That(matching, Is.EqualTo(target));
            var approaching = RangeBotPilot.AttackVelocity(Vector3.forward, target, 900, 500, 80, Vector3.right, 0);
            Assert.That(approaching.z, Is.EqualTo(80));
            var tooClose = RangeBotPilot.AttackVelocity(Vector3.forward, target, 100, 500, 80, Vector3.right, 0);
            Assert.That(tooClose.z, Is.EqualTo(-36));
            Assert.That(tooClose.x, Is.EqualTo(60));
        }

        [Test]
        public void VelocityResponseIsContinuousAndIndependentOfPhysicsStepSubdivision()
        {
            Vector3 start = Vector3.forward * 100, goal = Vector3.right * 100;
            var whole = RangeBotPilot.BlendVelocity(start, goal, .5f, .1f);
            var half = RangeBotPilot.BlendVelocity(start, goal, .5f, .05f);
            half = RangeBotPilot.BlendVelocity(half, goal, .5f, .05f);
            Assert.That(Vector3.Distance(whole, half), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(start, whole), Is.LessThan(Vector3.Distance(start, goal) * .25f));
            Assert.That(RangeBotPilot.BlendVelocity(start, goal, .5f, 0), Is.EqualTo(start));
        }

        [Test]
        public void SteeringCanReverseWithoutAZeroVectorOrInstantHalfTurn()
        {
            Vector3 direction = Vector3.forward;
            float previous = 180;
            for (int i = 0; i < 180; i++)
            {
                direction = RangeBotPilot.BlendDirection(direction, Vector3.back, .2f, .02f);
                Assert.That(direction.magnitude, Is.EqualTo(1).Within(.0001f));
                float remaining = Vector3.Angle(direction, Vector3.back);
                Assert.That(remaining, Is.LessThanOrEqualTo(previous + .001f));
                if (i == 0) Assert.That(remaining, Is.GreaterThan(150));
                previous = remaining;
            }
            Assert.That(previous, Is.LessThan(.1f));
        }

        [Test]
        public void AimErrorIsSlowPersistentAndDifferentBetweenPilots()
        {
            var a = RangeBotPilot.PersistentAimError(10, 1, .5f);
            var b = RangeBotPilot.PersistentAimError(10.02, 1, .5f);
            var other = RangeBotPilot.PersistentAimError(10, 2, .5f);
            Assert.That(Vector2.Distance(a, b), Is.LessThan(.02f));
            Assert.That(Vector2.Distance(a, other), Is.GreaterThan(.1f));
            Assert.That(RangeBotPilot.PersistentAimError(10, 1, 0), Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void WeaponWindowsAllowChargeAndGuidedAcquisitionInsteadOfResettingTooEarly()
        {
            var weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                weapon.activationMode = WeaponActivationMode.ChargeRelease; weapon.chargeSeconds = 2;
                Assert.That(RangeBotPilot.EffectiveBurstSeconds(.3f, weapon), Is.GreaterThan(2));
                weapon.activationMode = WeaponActivationMode.Hold;
                weapon.delivery = WeaponDelivery.GuidedMissile; weapon.missileLockSeconds = 1.4f;
                Assert.That(RangeBotPilot.EffectiveBurstSeconds(.3f, weapon), Is.GreaterThan(1.4f));
                weapon.delivery = WeaponDelivery.Projectile;
                Assert.That(RangeBotPilot.EffectiveBurstSeconds(.3f, weapon), Is.EqualTo(.3f));
            }
            finally { UnityEngine.Object.DestroyImmediate(weapon); }
        }

        [Test]
        public void TerrainBlocksTheFiringLaneButAnOffsetObstacleDoesNot()
        {
            int layer = LayerMask.NameToLayer("BudgetedScenery");
            Assert.That(layer, Is.GreaterThanOrEqualTo(0));
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.layer = layer;
            obstacle.transform.position = new Vector3(5000, 5000, 5050);
            var pilot = new RangeBotPilot();
            var method = typeof(RangeBotPilot).GetMethod("ClearTerrainLane", BindingFlags.Instance | BindingFlags.NonPublic);
            Vector3 origin = new Vector3(5000, 5000, 5000), destination = origin + Vector3.forward * 100;
            try
            {
                Physics.SyncTransforms();
                Assert.That((bool)method.Invoke(pilot, new object[] { origin, destination, .5f }), Is.False);
                obstacle.transform.position += Vector3.right * 10;
                Physics.SyncTransforms();
                Assert.That((bool)method.Invoke(pilot, new object[] { origin, destination, .5f }), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(obstacle); }
        }
    }
}
