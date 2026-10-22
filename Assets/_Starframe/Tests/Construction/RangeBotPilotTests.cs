using System;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Tests
{
    public sealed class RangeBotPilotTests
    {
        WeaponDefinition weapon;
        bool hadMode;
        int savedMode;

        [SetUp] public void SetUp()
        {
            hadMode = PlayerPrefs.HasKey(RangePracticeSettings.PreferenceKey);
            savedMode = PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey);
            weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.projectileSpeed = 160; weapon.range = 800; weapon.projectileDiameter = 1;
        }

        [TearDown] public void TearDown()
        {
            if (hadMode) PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, savedMode);
            else PlayerPrefs.DeleteKey(RangePracticeSettings.PreferenceKey);
            PlayerPrefs.Save();
            UnityEngine.Object.DestroyImmediate(weapon);
        }

        [Test] public void PracticeModePersistsWithoutReplacingBlueprintRosterPreferences()
        {
            foreach (RangeBotMode mode in Enum.GetValues(typeof(RangeBotMode)))
            {
                RangePracticeSettings.Mode = mode;
                Assert.That(RangePracticeSettings.Mode, Is.EqualTo(mode));
                Assert.That(PlayerPrefs.GetInt(RangePracticeSettings.PreferenceKey), Is.EqualTo((int)mode));
            }
            PlayerPrefs.SetInt(RangePracticeSettings.PreferenceKey, 999);
            Assert.That(RangePracticeSettings.Mode, Is.EqualTo(RangeBotMode.Combat));
            Assert.Throws<ArgumentOutOfRangeException>(() => RangePracticeSettings.Mode = (RangeBotMode)(-1));
        }

        [Test] public void FiringRequiresAnAchievableInterceptAndActualBarrelAlignment()
        {
            Vector3 origin = new Vector3(3, 2, -1), core = new Vector3(8, 7, 160), velocity = new Vector3(45, 0, 0);
            Assert.That(InterceptSolver.TrySolve(origin, core, velocity, weapon.projectileSpeed, weapon.range,
                out var lead, out float flightTime), Is.True);
            var profile = new WeaponAimProfile { weapon = weapon, origin = origin, direction = (lead - origin).normalized };
            Assert.That(RangeBotPilot.TryFiringSolution(profile, core, velocity, 2, out var aim), Is.True);
            Assert.That(Vector3.Distance(aim, core + velocity * flightTime), Is.LessThan(.001f));
            profile.direction = (core - origin).normalized;
            Assert.That(RangeBotPilot.TryFiringSolution(profile, core, velocity, 2, out _), Is.False,
                "Pointing at the current target position must not count as leading a fast crossing core.");
            profile.direction = Vector3.forward;
            Assert.That(RangeBotPilot.TryFiringSolution(profile, new Vector3(0, 0, 900), Vector3.zero, 2, out _), Is.False);
            Assert.That(RangeBotPilot.TryFiringSolution(profile, new Vector3(0, 0, 100), Vector3.forward * 200, 2, out _), Is.False);
        }

        [Test] public void LongRangeFireToleranceShrinksToTheTargetInsteadOfUsingABroadRandomCone()
        {
            var profile = new WeaponAimProfile { weapon = weapon, origin = Vector3.zero,
                direction = Quaternion.Euler(0, .7f, 0) * Vector3.forward };
            Assert.That(RangeBotPilot.TryFiringSolution(profile, Vector3.forward * 20, Vector3.zero, 2, out _), Is.True);
            Assert.That(RangeBotPilot.TryFiringSolution(profile, Vector3.forward * 700, Vector3.zero, 2, out _), Is.False);
        }

        [Test] public void DistantFireRejectsAVisibleGeometricMissAndAcceptsASubDegreeHit()
        {
            weapon.range = 3000;
            var profile = new WeaponAimProfile { weapon = weapon, origin = Vector3.zero,
                direction = Quaternion.Euler(0, .05f, 0) * Vector3.forward };
            Assert.That(RangeBotPilot.TryFiringSolution(profile, Vector3.forward * 2400, Vector3.zero, 2, out _), Is.False,
                "A 0.05 degree error misses this core by over two metres at 2400 m.");
            profile.direction = Quaternion.Euler(0, .01f, 0) * Vector3.forward;
            Assert.That(RangeBotPilot.TryFiringSolution(profile, Vector3.forward * 2400, Vector3.zero, 2, out _), Is.True);
            profile.direction = Vector3.zero;
            Assert.That(RangeBotPilot.TryFiringSolution(profile, Vector3.forward * 2400, Vector3.zero, 2, out _), Is.False);
        }

        [Test] public void CombatFireCanCoverTheHullWithoutRequiringExactCoreAlignment()
        {
            var profile=new WeaponAimProfile{weapon=weapon,origin=Vector3.zero,
                direction=Quaternion.Euler(0,1,0)*Vector3.forward};
            Assert.That(RangeBotPilot.TryFiringSolution(profile,Vector3.forward*200,Vector3.zero,3.5f,out _),Is.False);
            Assert.That(RangeBotPilot.TryFiringSolution(profile,Vector3.forward*200,Vector3.zero,3.5f,out _,4),Is.True);
            profile.direction=Quaternion.Euler(0,6,0)*Vector3.forward;
            Assert.That(RangeBotPilot.TryFiringSolution(profile,Vector3.forward*200,Vector3.zero,3.5f,out _,4),Is.False,
                "A more willing trigger still respects the real barrel direction and attack cone.");
        }

        [Test] public void MovementUsesOnlySurvivingDirectionalEnginesAndLeavesUnavailableAxesToSharedBraking()
        {
            var performance = new ShipPerformance { accelerationPositive = new Vector3(0, 0, 12), accelerationNegative = Vector3.zero };
            var positive = RangeBotPilot.MovementInput(Quaternion.identity, new Vector3(30, 15, 80), Vector3.zero, performance);
            Assert.That(positive.x, Is.Zero); Assert.That(positive.y, Is.Zero); Assert.That(positive.z, Is.EqualTo(1));
            var braking = RangeBotPilot.MovementInput(Quaternion.identity, Vector3.back * 20, Vector3.forward * 50, performance);
            Assert.That(braking, Is.EqualTo(Vector3.zero));
            var turned = RangeBotPilot.MovementInput(Quaternion.Euler(0, 90, 0), Vector3.right * 60, Vector3.zero, performance);
            Assert.That(turned.z, Is.GreaterThan(.99f));
            performance.accelerationPositive = Vector3.zero;
            Assert.That(RangeBotPilot.MovementInput(Quaternion.identity, Vector3.one * 100, Vector3.zero, performance), Is.EqualTo(Vector3.zero));
        }

        [Test] public void AlignedTrackingKeepsTheCrossingSightlineRateThroughTheSharedStickResponse()
        {
            var tuning = ScriptableObject.CreateInstance<FlightTuning>();
            try
            {
                tuning.aimInputDeadZone = .16f;
                tuning.aimResponseExponent = 2.2f;
                var rotation = Quaternion.Euler(23, 124, 41);
                var direction = rotation * Vector3.forward;
                var trackingRate = new Vector3(-11, 19, 0);
                var input = RangeBotPilot.AimInput(rotation, direction, direction, trackingRate, 80, tuning, trackingRate);
                float radius = input.magnitude;
                var steering = input.normalized * Mathf.Pow((radius - tuning.aimInputDeadZone)
                    / (1 - tuning.aimInputDeadZone), tuning.aimResponseExponent);
                Assert.That(steering.x * 80, Is.EqualTo(trackingRate.y).Within(.001f));
                Assert.That(-steering.y * 80, Is.EqualTo(trackingRate.x).Within(.001f),
                    "An aligned bot must keep turning with the sightline, without braking away from its firing solution.");
                Assert.That(RangeBotPilot.AimInput(rotation, direction, direction, Vector3.zero, 80, tuning),
                    Is.EqualTo(Vector2.zero));
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }

        [TestCase(.01f, 80)]
        [TestCase(-.01f, 80)]
        [TestCase(.01f, .002f)]
        [TestCase(180, 80)]
        public void FineAimAndLowAuthorityHalfTurnsProduceFiniteBoundedSteering(float degrees, float angularRate)
        {
            var tuning = ScriptableObject.CreateInstance<FlightTuning>();
            try
            {
                var desired = Quaternion.Euler(0, degrees, 0) * Vector3.forward;
                var input = RangeBotPilot.AimInput(Quaternion.identity, Vector3.forward, desired,
                    Vector3.zero, angularRate, tuning);
                Assert.That(float.IsNaN(input.x) || float.IsInfinity(input.x)
                    || float.IsNaN(input.y) || float.IsInfinity(input.y), Is.False);
                Assert.That(input.magnitude, Is.InRange(tuning.aimInputDeadZone + .00001f, 1.00001f));
                if (Mathf.Abs(degrees) < 1)
                    Assert.That(input.x * Mathf.Sign(degrees), Is.GreaterThan(tuning.aimInputDeadZone),
                        "Distant micro-corrections must survive the angle calculation and stick dead zone.");
            }
            finally { UnityEngine.Object.DestroyImmediate(tuning); }
        }

        [Test] public void BoundarySteeringReturnsInwardBeforeStoppingDistanceAndProtectsAllThreeFaces()
        {
            var outward = new Vector3(70, -40, 50);
            var constrained = RangeBotPilot.ConstrainToArena(new Vector3(480, -475, 482), outward, outward, 500, 10, 8);
            Assert.That(constrained.x, Is.LessThan(0));
            Assert.That(constrained.y, Is.GreaterThan(0));
            Assert.That(constrained.z, Is.LessThan(0));
            var fast = RangeBotPilot.ConstrainToArena(new Vector3(200, 0, 0), Vector3.right * 100, Vector3.right * 100, 500, 5, 5);
            Assert.That(fast.x, Is.LessThan(0), "A fast ship must brake well before the boundary is visually close.");
            var centre = RangeBotPilot.ConstrainToArena(Vector3.zero, Vector3.forward * 30, Vector3.zero, 500, 5, 8);
            Assert.That(centre, Is.EqualTo(Vector3.forward * 30));
            var slowRequest = new Vector3(10, -10, 10);
            var corner = RangeBotPilot.ConstrainToArena(new Vector3(480, -475, 482), slowRequest,
                new Vector3(120, -120, 120), 500, 10, 8);
            Assert.That(corner.magnitude, Is.LessThanOrEqualTo(slowRequest.magnitude + .001f),
                "Avoiding several faces must not inflate the speed request and waste boost power.");
            Assert.That(corner.x, Is.LessThan(0)); Assert.That(corner.y, Is.GreaterThan(0)); Assert.That(corner.z, Is.LessThan(0));
        }
    }
}
