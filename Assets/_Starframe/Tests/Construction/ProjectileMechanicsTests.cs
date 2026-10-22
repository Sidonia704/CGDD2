using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class ProjectileMechanicsTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            BlockModules.Configure(null);
            foreach (var value in owned)
            {
                var go = value as GameObject;
                var pool = go ? go.GetComponent<LaserVisualPool>() : null;
                if (!pool) continue;
                foreach (string fieldName in new[] { "sharedMesh", "sharedMaterial" })
                {
                    var field = typeof(LaserVisualPool).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                    var asset = field.GetValue(pool) as UnityEngine.Object;
                    if (asset) UnityEngine.Object.DestroyImmediate(asset);
                    field.SetValue(pool, null);
                }
            }
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [Test]
        public void LargeStepStillHitsTargetAlongEntireSegment()
        {
            var owner = CreateShip(false, new PartSpec(BlockShape.Core, 100, 0));
            var target = CreateShip(false, new PartSpec(BlockShape.Core, -1, 10), new PartSpec(BlockShape.Cube, 0, 10));
            Sync();
            var cube = Part(target, BlockShape.Cube);
            var world = NewWorld();
            world.Fire(owner.runtime, Weapon(40, 80, 100, 2, .5f), new Vector3(0, .5f, 0), Vector3.forward);
            world.Step(.2f);
            Assert.That(cube.hp, Is.EqualTo(cube.maxHp - 40).Within(.01f));
        }

        [Test]
        public void FiniteCylinderDiameterControlsHits()
        {
            var owner = CreateShip(false, new PartSpec(BlockShape.Core, 100, 0));
            var inside = CreateShip(false, new PartSpec(BlockShape.Core, -1, 10), new PartSpec(BlockShape.Cube, 0, 10));
            var outside = CreateShip(false, new PartSpec(BlockShape.Core, -1, 20), new PartSpec(BlockShape.Cube, 0, 20));
            Sync();
            var world = NewWorld();
            var weapon = Weapon(30, 80, 100, 2, 1f);
            world.Fire(owner.runtime, weapon, new Vector3(.45f, .5f, 0), Vector3.forward);
            world.Step(.2f);
            Assert.That(Part(inside, BlockShape.Cube).hp, Is.EqualTo(Part(inside, BlockShape.Cube).maxHp - 30).Within(.01f));
            world.Fire(owner.runtime, weapon, new Vector3(1.1f, .5f, 0), Vector3.forward);
            world.Step(.2f);
            Assert.That(Part(outside, BlockShape.Cube).hp, Is.EqualTo(Part(outside, BlockShape.Cube).maxHp).Within(.01f));
        }

        [Test]
        public void FlatCylinderEndDoesNotAcceptCapsuleOnlyCornerContact()
        {
            var go = Own(new GameObject("Flat cap query"));
            var box = go.AddComponent<BoxCollider>();
            box.size = Vector3.one * .2f;
            go.transform.position = new Vector3(.4f, 0, 1.2f);
            Sync();
            var query = new CylindricalHitQuery();
            Assert.That(query.TryHit(box, Vector3.zero, Vector3.forward, .5f, 0, 1, out _), Is.False);
        }

        [Test]
        public void ObliqueSweepHitsRotatedBoxOnlyWithinTheRequestedSegment()
        {
            var go = Own(new GameObject("Oblique rotated box"));
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(1.2f, 1.2f, .8f);
            Vector3 direction = new Vector3(1, 0, 1).normalized;
            go.transform.SetPositionAndRotation(direction * 5, Quaternion.Euler(0, 37, 0));
            Sync();

            var query = new CylindricalHitQuery();
            Assert.That(query.TryHit(box, Vector3.zero, direction, .5f, 0, 10, out float entry), Is.True);
            Assert.That(entry, Is.GreaterThan(0));
            Assert.That(entry, Is.LessThan(5));
            Assert.That(query.TryHit(box, Vector3.zero, direction, .5f, 0, 4, out _), Is.False);
        }

        [Test]
        public void PenetrationDamagesFirstLayerFullySecondAtHalfAndStopsBeforeThird()
        {
            var owner = CreateShip(false, new PartSpec(BlockShape.Core, 100, 0));
            var target = CreateShip(false,
                new PartSpec(BlockShape.Core, -1, 10),
                new PartSpec(BlockShape.Cube, 0, 10),
                new PartSpec(BlockShape.Cube, 0, 11),
                new PartSpec(BlockShape.Cube, 0, 12));
            Sync();
            var first = PartAt(target, 0, .5f, 10);
            var second = PartAt(target, 0, .5f, 11);
            var third = PartAt(target, 0, .5f, 12);
            var world = NewWorld();
            int confirmations=0;
            world.HitConfirmed+=source=>{Assert.That(source,Is.EqualTo(owner.runtime));confirmations++;};
            world.Fire(owner.runtime, Weapon(40, 80, 100, 2, .5f), new Vector3(0, .5f, 0), Vector3.forward);
            world.Step(.3f);
            Assert.That(first.hp, Is.EqualTo(first.maxHp - 40).Within(.01f));
            Assert.That(second.hp, Is.EqualTo(second.maxHp - 20).Within(.01f));
            Assert.That(third.hp, Is.EqualTo(third.maxHp).Within(.01f));
            Assert.That(confirmations,Is.EqualTo(1),"One penetrating bolt must confirm once across both damaged layers.");
            world.Fire(owner.runtime,Weapon(1,80,100,2,.5f),new Vector3(0,.5f,0),Vector3.forward);
            world.Step(.3f);
            Assert.That(confirmations,Is.EqualTo(2),"A reused projectile must be able to confirm its new shot.");
        }

        [Test]public void MissesEnvironmentAndZeroDamageDoNotConfirmHits()
        {
            var owner=CreateShip(false,new PartSpec(BlockShape.Core,100,0));
            CreateShip(false,new PartSpec(BlockShape.Core,-1,10),new PartSpec(BlockShape.Cube,0,10));
            var wall=Own(new GameObject("Non-damageable wall"));wall.transform.position=new Vector3(5,.5f,5);wall.AddComponent<BoxCollider>();
            Sync();var world=NewWorld();int confirmations=0;world.HitConfirmed+=source=>confirmations++;
            world.Fire(owner.runtime,Weapon(40,80,100,2,.5f),new Vector3(20,.5f,0),Vector3.forward);
            world.Fire(owner.runtime,Weapon(40,80,100,2,.5f),new Vector3(5,.5f,0),Vector3.forward);
            world.Fire(owner.runtime,Weapon(0,80,100,2,.5f),new Vector3(0,.5f,0),Vector3.forward);
            world.Step(2);
            Assert.That(confirmations,Is.Zero);
        }

        [Test]
        public void MultipleComponentsAtSameDepthAreAllHitButOnePartIdIsDamagedOnce()
        {
            var owner = CreateShip(false, new PartSpec(BlockShape.Core, 100, 0));
            var target = CreateShip(false,
                new PartSpec(BlockShape.Core, 0, 10, 2.5f),
                new PartSpec(BlockShape.Cube, 0, 10, 1.5f),
                new PartSpec(BlockShape.Cube, 0, 10),
                new PartSpec(BlockShape.Cube, 1, 10));
            Sync();
            var support = PartAt(target, 0, 1.5f, 10);
            var left = PartAt(target, 0, .5f, 10);
            var right = PartAt(target, 1, .5f, 10);
            // A second collider reports the same component again; hitParts must deduplicate by state/ID.
            var secondCollider = left.view.AddComponent<BoxCollider>();
            secondCollider.size = Vector3.one;
            var world = NewWorld();
            world.Fire(owner.runtime, Weapon(40, 80, 100, 2, 1f), new Vector3(.5f, .5f, 0), Vector3.forward);
            Sync();
            world.Step(.2f);
            Assert.That(support.hp, Is.EqualTo(support.maxHp).Within(.01f));
            Assert.That(left.hp, Is.EqualTo(left.maxHp - 40).Within(.01f));
            Assert.That(right.hp, Is.EqualTo(right.maxHp - 40).Within(.01f));
            Assert.That(target.runtime.DamageTaken, Is.EqualTo(80).Within(.01f));
        }

        [Test]
        public void ProjectileNeverDamagesOwnerAndOwnHullBlocksWeaponFire()
        {
            var owner = CreateShip(true, true,
                new PartSpec(BlockShape.Core, 0, 0),
                new PartSpec(BlockShape.Laser, 0, 1),
                new PartSpec(BlockShape.Cube, 0, 3));
            Sync();
            var world = NewWorld();
            var gun = Part(owner, BlockShape.Laser);
            float hp = gun.hp;
            world.Fire(owner.runtime, Weapon(40, 80, 100, 2, .5f), new Vector3(0, .5f, -2), Vector3.forward);
            world.Step(.1f);
            Assert.That(gun.hp, Is.EqualTo(hp).Within(.001f));

            var weapons = new ShipWeapons();
            weapons.Initialize(owner.runtime, world);
            weapons.FireRequested = true;
            Assert.That(weapons.Prepare(), Is.Zero);
            Assert.That(weapons.BlockedGuns, Is.EqualTo(1));
            weapons.Execute(1, 1);
            Assert.That(weapons.ShotsFired, Is.Zero);
        }

        [Test]
        public void FinitePowerReducesFiveShotPerSecondRateWithoutChangingProjectileDamage()
        {
            var owner = CreateShip(true,
                new PartSpec(BlockShape.Core, 0, 0),
                new PartSpec(BlockShape.Laser, 0, 1));
            var target = CreateShip(false, new PartSpec(BlockShape.Core, -1, 10), new PartSpec(BlockShape.Cube, 0, 10));
            Sync();
            var gunDef = owner.definitions[BlockShape.Laser];
            gunDef.weapon.damage = 40;
            gunDef.weapon.shotsPerSecond = 5;
            gunDef.weapon.energyPerShot = 10;
            gunDef.generationWatts = 25;
            owner.runtime.Recalculate();
            var weapons = new ShipWeapons();
            var world = NewWorld();
            weapons.Initialize(owner.runtime, world);
            weapons.FireRequested = true;
            Assert.That(weapons.Prepare(), Is.EqualTo(50).Within(.01f));
            float ratio = owner.runtime.Power.Allocate(.2f, 50);
            Assert.That(ratio, Is.EqualTo(.5f).Within(.01f));
            weapons.Execute(.2f, ratio);
            weapons.Prepare();
            ratio = owner.runtime.Power.Allocate(.2f, 50);
            weapons.Execute(.2f, ratio);
            Assert.That(weapons.ShotsFired, Is.EqualTo(1));
            world.Step(.2f);
            Assert.That(Part(target, BlockShape.Cube).hp, Is.EqualTo(Part(target, BlockShape.Cube).maxHp - 40).Within(.01f));
        }

        [Test]
        public void DestroyedWeaponStopsFiringAndRestoredWeaponCanFireAgain()
        {
            var owner = CreateShip(true, new PartSpec(BlockShape.Core, 0, 0), new PartSpec(BlockShape.Laser, 0, 1));
            Sync();
            var gun = Part(owner, BlockShape.Laser);
            var weapons = new ShipWeapons();
            var world = NewWorld();
            weapons.Initialize(owner.runtime, world);
            weapons.FireRequested = true;
            weapons.Prepare();
            weapons.Execute(.2f, 1);
            Assert.That(weapons.ShotsFired, Is.EqualTo(1));
            owner.runtime.ApplyDamage(gun.record.id, gun.maxHp);
            weapons.Prepare();
            weapons.Execute(1, 1);
            Assert.That(weapons.ActiveGuns, Is.Zero);
            Assert.That(weapons.ShotsFired, Is.EqualTo(1));
            owner.runtime.SetPartPresent(gun.record.id, true);
            weapons.Prepare();
            weapons.Execute(.2f, 1);
            Assert.That(weapons.ShotsFired, Is.EqualTo(2));
        }

        [Test]
        public void ReleasedVisualHandleCannotMoveReusedPoolSlot()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (!shader) Assert.Ignore("Sprites/Default shader is unavailable in this Editor.");
            var poolObject = Own(new GameObject("Laser visual pool"));
            var pool = poolObject.AddComponent<LaserVisualPool>();
            pool.Initialize(shader);
            int oldHandle = pool.Acquire(Vector3.zero, Vector3.forward, 1, 1, 5);
            pool.Release(oldHandle);
            int newHandle = pool.Acquire(new Vector3(2, 3, 4), Vector3.right, 1, 1, 0);
            Assert.That(newHandle, Is.Not.EqualTo(oldHandle));
            pool.Move(newHandle,new Vector3(4,3,4),Vector3.right);
            pool.Move(oldHandle, new Vector3(100, 100, 100), Vector3.up);
            var visuals = (System.Collections.IList)typeof(LaserVisualPool).GetField("visuals", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
            var visual = visuals[0];
            var transform = (Transform)visual.GetType().GetField("transform", BindingFlags.Instance | BindingFlags.Public).GetValue(visual);
            Assert.That((transform.position - new Vector3(3, 3, 4)).sqrMagnitude, Is.LessThan(.0001f));
        }

        [TestCase(0f,10f,1000f)][TestCase(.1f,1000f,1000f)][TestCase(10f,1f,1000f)][TestCase(100f,10f,0f)]
        public void LaserBodyAndTailStayAheadOfLaunchAndMovingMuzzle(float distance,float bodyLength,float tailLength)
        {
            var pool=Own(new GameObject("Muzzle clipped laser pool")).AddComponent<LaserVisualPool>();pool.Initialize(Shader.Find("Starframe/Combat/LaserProjectile"));
            var muzzle=Own(new GameObject("Moving muzzle")).transform;var origin=new Vector3(2,3,4);var direction=new Vector3(1,.4f,-.8f).normalized;muzzle.position=origin;
            int handle=pool.Acquire(origin,direction,2,bodyLength,tailLength,muzzle);
            muzzle.position=origin+direction*distance*.5f;pool.Move(handle,origin+direction*distance,direction);
            var filter=pool.GetComponentInChildren<MeshFilter>();var renderer=filter.GetComponent<MeshRenderer>();
            Assert.That(renderer.enabled,Is.EqualTo(distance>0));
            foreach(var vertex in filter.sharedMesh.vertices){var point=filter.transform.TransformPoint(vertex);Assert.That(Vector3.Dot(point-muzzle.position,direction),Is.GreaterThanOrEqualTo(-.0001f));Assert.That(Vector3.Dot(point-origin,direction),Is.LessThanOrEqualTo(distance+.0001f));}
        }

        ShipFixture CreateShip(bool armed, params PartSpec[] parts) => CreateShip(armed, false, parts);

        ShipFixture CreateShip(bool armed, bool allowLegacyBlockedLayout, params PartSpec[] parts)
        {
            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.database = database;
            var defs = new Dictionary<BlockShape, ModuleDefinition>();
            foreach (var shape in new[] { BlockShape.Core, BlockShape.Cube, BlockShape.Laser })
            {
                var def = Own(ScriptableObject.CreateInstance<ModuleDefinition>());
                def.shape = shape;
                def.category = shape == BlockShape.Core ? ModuleCategory.Core : shape == BlockShape.Laser ? ModuleCategory.Weapon : ModuleCategory.Hull;
                def.massKg = 10;
                def.maxHp = 100;
                def.recoverySeconds = 1;
                def.occupiedCells = shape == BlockShape.Laser
                    ? new[] { Vector3Int.zero, Vector3Int.forward }
                    : new[] { Vector3Int.zero };
                if (shape == BlockShape.Laser)
                {
                    def.muzzleLocalPosition = Vector3.zero;
                    if (armed) def.weapon = Weapon(40, 80, 100, 2, .5f);
                }
                defs.Add(shape, def);
            }
            database.modules = new List<ModuleDefinition>(defs.Values);
            BlockModules.Configure(database);
            var go = Own(new GameObject("Projectile test ship"));
            var assembly = go.AddComponent<ShipAssembly>();
            typeof(ShipAssembly).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(assembly, null);
            assembly.Configure(database);
            if (allowLegacyBlockedLayout)
            {
                var legacy = new BlueprintData { name = "legacy-blocked-projectile-test" };
                foreach (var part in parts)
                    legacy.parts.Add(new BlockRecord
                    {
                        id = "legacy-" + part.shape + "-" + part.x + "-" + part.z,
                        shape = part.shape,
                        position = new Vector3(part.x, part.y, part.z),
                        cell = new Vector3Int(part.x, Mathf.RoundToInt(part.y - .5f), part.z),
                        rotation = Quaternion.identity
                    });
                assembly.Import(legacy);
            }
            else foreach (var part in parts)
            {
                var shape = part.shape;
                Assert.That(assembly.Place(shape, new Vector3(part.x, part.y, part.z), Quaternion.identity), Is.True,
                    "Could not place " + shape + " at " + part.x + "," + part.z);
            }
            var blueprint = assembly.Export("projectile-test");
            var runtime = go.AddComponent<FlightShipRuntime>();
            runtime.Initialize(assembly, blueprint, tuning, Own(ScriptableObject.CreateInstance<BattleTuning>()));
            return new ShipFixture(runtime, defs);
        }

        WeaponDefinition Weapon(float damage, float speed, float range, float depth, float diameter)
        {
            var weapon = Own(ScriptableObject.CreateInstance<WeaponDefinition>());
            weapon.damage = damage;
            weapon.projectileSpeed = speed;
            weapon.range = range;
            weapon.penetrationDepth = depth;
            weapon.projectileDiameter = diameter;
            weapon.secondLayerDamageMultiplier = .5f;
            weapon.shotsPerSecond = 5;
            return weapon;
        }

        ProjectileWorld NewWorld() { var world = new ProjectileWorld(); world.Initialize(null); return world; }
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        void Sync() => Physics.SyncTransforms();
        static ShipPartState Part(ShipFixture fixture, BlockShape shape) => fixture.runtime.Parts.Find(p => p.record.shape == shape);
        static List<ShipPartState> Parts(ShipFixture fixture, BlockShape shape) => fixture.runtime.Parts.FindAll(p => p.record.shape == shape);
        static ShipPartState PartAt(ShipFixture fixture, float x, float y, float z) =>
            fixture.runtime.Parts.Find(p => p.record.shape == BlockShape.Cube &&
                Mathf.Abs(p.record.position.x - x) < .001f && Mathf.Abs(p.record.position.y - y) < .001f && Mathf.Abs(p.record.position.z - z) < .001f);

        readonly struct PartSpec
        {
            public readonly BlockShape shape;
            public readonly int x, z;
            public readonly float y;
            public PartSpec(BlockShape s, int px, int pz, float py = .5f) { shape = s; x = px; z = pz; y = py; }
        }
        sealed class ShipFixture
        {
            public readonly FlightShipRuntime runtime;
            public readonly Dictionary<BlockShape, ModuleDefinition> definitions;
            public ShipFixture(FlightShipRuntime r, Dictionary<BlockShape, ModuleDefinition> d) { runtime = r; definitions = d; }
        }
    }
}
