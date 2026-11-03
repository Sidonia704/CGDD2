using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class CoreLeadBallisticsTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly List<ProjectileWorld> worlds = new List<ProjectileWorld>();
        ModuleDatabase database;
        FlightTuning tuning;
        BattleTuning battle;

        [SetUp]
        public void SetUp()
        {
            var source = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            Assert.That(source, Is.Not.Null);
            database = Own(UnityEngine.Object.Instantiate(source));
            database.modules = new List<ModuleDefinition>(source.modules);
            var laser = Own(UnityEngine.Object.Instantiate(source.Get(BlockShape.Laser)));
            laser.weapon = Own(UnityEngine.Object.Instantiate(laser.weapon));
            laser.weapon.damage = 40;
            laser.weapon.projectileDiameter = 1.2f;
            laser.weapon.range = 1000;
            laser.weapon.penetrationDepth = 2;
            database.modules.RemoveAll(d => d.shape == BlockShape.Laser);
            database.modules.Add(laser);
            tuning = Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.database = database;
            battle = Own(ScriptableObject.CreateInstance<BattleTuning>());
            BlockModules.Configure(database);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var world in worlds) world.Clear();
            worlds.Clear();
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void TestBlueprintAimProfileUsesOneActualPhysicsMuzzleInsteadOfTheirAverage()
        {
            var owner = TestShip();
            owner.body.position = new Vector3(-30, 4, -5);
            owner.body.rotation = Quaternion.Euler(9, 23, -7);
            Physics.SyncTransforms();
            var world = NewWorld();
            var weapons = new ShipWeapons();
            weapons.Initialize(owner.runtime, world);
            weapons.Prepare();
            var profiles = new List<WeaponAimProfile>();
            weapons.GetAimProfiles(profiles);

            Assert.That(profiles.Count, Is.EqualTo(1));
            var profile = profiles[0];
            Assert.That(profile.gunCount, Is.EqualTo(3));
            var representative = owner.runtime.Parts.Find(p => p.record.id == profile.partId);
            Assert.That(representative, Is.Not.Null);
            var definition = database.Get(BlockShape.Laser);
            Vector3 expected = CylindricalHitQuery.PhysicsMatrix(representative.view.GetComponent<Collider>())
                .MultiplyPoint3x4(definition.muzzleLocalPosition);
            Assert.That(Vector3.Distance(profile.origin, expected), Is.LessThan(.0001f));
            Vector3 expectedDirection = CylindricalHitQuery.PhysicsMatrix(representative.view.GetComponent<Collider>())
                .MultiplyVector(Vector3.forward).normalized;
            Assert.That(Vector3.Angle(profile.direction, expectedDirection), Is.LessThan(.01f));

            Vector3 average = Vector3.zero;
            foreach (var part in owner.runtime.Parts)
                if (part.record.shape == BlockShape.Laser)
                    average += CylindricalHitQuery.PhysicsMatrix(part.view.GetComponent<Collider>())
                        .MultiplyPoint3x4(definition.muzzleLocalPosition);
            average /= 3;
            Assert.That(Vector3.Distance(profile.origin, average), Is.GreaterThan(1),
                "A real firing muzzle is required; the old average lay between all three parallel barrels.");
        }

        [TestCase(80f)]
        [TestCase(400f)]
        public void RepresentativeMuzzleInterceptDamagesAnOffsetMovingCore(float projectileSpeed)
        {
            database.Get(BlockShape.Laser).weapon.projectileSpeed = projectileSpeed;
            var owner = TestShip();
            owner.body.position = new Vector3(-30, 4, -10);
            var target = OffsetCoreShip();
            Vector3 start = new Vector3(8, 3, 40);
            target.body.position = start;
            target.body.rotation = Quaternion.Euler(0, 25, 0);
            Physics.SyncTransforms();
            var world = NewWorld();
            var weapons = new ShipWeapons();
            weapons.Initialize(owner.runtime, world);
            weapons.Prepare();
            var profiles = new List<WeaponAimProfile>();
            weapons.GetAimProfiles(profiles);
            Assert.That(profiles.Count, Is.EqualTo(1));
            var profile = profiles[0];
            var core = target.runtime.Parts.Find(p => p.record.shape == BlockShape.Core);
            Assert.That(Vector3.Distance(target.runtime.CorePhysicsPosition, target.body.position), Is.GreaterThan(2));

            Vector3 velocity = new Vector3(7, 2, -1);
            var estimator = new CoreVelocityEstimator();
            estimator.Reset(target.runtime.CorePhysicsPosition, 0, Vector3.zero);
            const float sampleDt = .02f;
            for (int i = 1; i <= 12; i++)
            {
                target.body.position = start + velocity * (i * sampleDt);
                Physics.SyncTransforms();
                estimator.Sample(target.runtime.CorePhysicsPosition, i * (double)sampleDt, .16f, .04f, Vector3.zero);
            }
            Assert.That(Vector3.Distance(estimator.Velocity, velocity), Is.LessThan(.03f),
                "Core position samples must converge to the actual target velocity before calculating the lead.");
            start = target.body.position;
            Assert.That(InterceptSolver.TrySolve(profile.origin, target.runtime.CorePhysicsPosition, estimator.Velocity,
                projectileSpeed, profile.weapon.range, out var intercept, out float flightTime), Is.True);
            world.Fire(owner.runtime, profile.weapon, profile.origin, (intercept - profile.origin).normalized);
            float originalHp = core.hp;
            const float dt = .005f;
            int steps = Mathf.CeilToInt((flightTime + .1f) / dt);
            for (int i = 1; i <= steps; i++)
            {
                target.body.position = start + velocity * (i * dt);
                target.runtime.BeginStep(dt);
                Physics.SyncTransforms();
                world.Step(dt);
            }

            Assert.That(core.hp, Is.EqualTo(originalHp - profile.weapon.damage).Within(.01f),
                "The real cylindrical projectile must intersect the moving core collider, not just a mathematical average ray.");
            Assert.That(target.runtime.DamageTaken, Is.EqualTo(profile.weapon.damage).Within(.01f));
        }

        [Test]
        public void CorePhysicsPositionTracksOffsetAssemblyAndRigidBodyRotation()
        {
            var ship = OffsetCoreShip();
            ship.assembly.transform.localPosition = new Vector3(-.5f, 2, .25f);
            ship.assembly.transform.localRotation = Quaternion.Euler(0, 90, 0);
            var core = ship.runtime.Parts.Find(p => p.record.shape == BlockShape.Core);
            Vector3 localCore = ship.assembly.transform.localPosition +
                ship.assembly.transform.localRotation * core.record.position;
            ship.body.position = new Vector3(10, 20, 30);
            ship.body.rotation = Quaternion.Euler(15, 65, -20);
            Physics.SyncTransforms();

            Vector3 expected = ship.body.position + ship.body.rotation * localCore;
            Assert.That(Vector3.Distance(ship.runtime.CorePhysicsPosition, expected), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(ship.runtime.CorePosition, core.view.transform.position), Is.LessThan(.0001f));
            Assert.That(Vector3.Distance(ship.runtime.CorePhysicsPosition, ship.body.position), Is.GreaterThan(2));
        }

        ShipFixture TestShip()
        {
            var snapshot = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/_Starframe/Data/RangeTargets/test.json");
            Assert.That(snapshot, Is.Not.Null);
            var ship = CreateShip(JsonUtility.FromJson<BlueprintData>(snapshot.text));
            Quaternion correction = Quaternion.Inverse(ship.runtime.CoreRotation);
            ship.assembly.transform.localRotation = correction;
            ship.assembly.transform.localPosition = -(correction * ship.runtime.CenterOfMass());
            return ship;
        }

        ShipFixture OffsetCoreShip()
        {
            var blueprint = new BlueprintData { name = "offset-core-ballistic-target" };
            blueprint.parts.Add(new BlockRecord
            {
                id = "offset-core", shape = BlockShape.Core,
                position = new Vector3(2, 1.5f, -1), rotation = Quaternion.identity
            });
            return CreateShip(blueprint);
        }

        ShipFixture CreateShip(BlueprintData blueprint)
        {
            var root = Own(new GameObject("Core lead ballistic test ship"));
            var body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var child = new GameObject("Assembly");
            child.transform.SetParent(root.transform, false);
            var assembly = child.AddComponent<ShipAssembly>();
            assembly.Configure(database);
            assembly.Import(blueprint);
            var runtime = root.AddComponent<FlightShipRuntime>();
            runtime.Initialize(assembly, blueprint, tuning, battle);
            return new ShipFixture { runtime = runtime, body = body, assembly = assembly };
        }

        ProjectileWorld NewWorld()
        {
            var world = new ProjectileWorld();
            world.Initialize(null);
            worlds.Add(world);
            return world;
        }

        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        sealed class ShipFixture
        {
            public FlightShipRuntime runtime;
            public Rigidbody body;
            public ShipAssembly assembly;
        }
    }
}
