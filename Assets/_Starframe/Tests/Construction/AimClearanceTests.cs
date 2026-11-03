using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using Starframe.Combat;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class AimClearanceTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            BlockModules.Configure(null);
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        [TestCase(0f, 10f, 0f, 0f, 100f, true)] // stationary
        [TestCase(0f, 10f, 0f, 2f, 100f, true)] // lateral motion
        [TestCase(0f, 10f, -2f, 0f, 100f, true)] // approaching
        [TestCase(0f, 10f, 2f, 0f, 100f, true)] // receding, still slower than the projectile
        [TestCase(0f, 10f, 100f, 0f, 100f, false)] // cannot catch a faster target
        [TestCase(0f, 10f, 0f, 0f, 9f, false)] // beyond range
        public void InterceptSolverReturnsOnlyReachableIntercepts(float targetX, float targetZ, float targetVelocityZ,
            float targetVelocityX, float range, bool expected)
        {
            Vector3 origin = Vector3.zero;
            Vector3 target = new Vector3(targetX, 0, targetZ);
            Vector3 velocity = new Vector3(targetVelocityX, 0, targetVelocityZ);
            bool solved = InterceptSolver.TrySolve(origin, target, velocity, 20, range, out Vector3 point, out float time);
            Assert.That(solved, Is.EqualTo(expected));
            if (!solved) return;

            Assert.That(time, Is.GreaterThanOrEqualTo(0));
            Assert.That(point, Is.EqualTo(target + velocity * time));
            Assert.That(Vector3.Distance(origin, point), Is.EqualTo(20 * time).Within(.001f));
        }

        [Test]
        public void FasterProjectilesProduceEarlierAndCloserInterceptPoints()
        {
            Vector3 target = new Vector3(0, 0, 20);
            Vector3 velocity = new Vector3(3, 0, 0);
            Assert.That(InterceptSolver.TrySolve(Vector3.zero, target, velocity, 20, 100, out Vector3 slowPoint, out float slowTime), Is.True);
            Assert.That(InterceptSolver.TrySolve(Vector3.zero, target, velocity, 40, 100, out Vector3 fastPoint, out float fastTime), Is.True);

            Assert.That(fastTime, Is.LessThan(slowTime));
            Assert.That(fastPoint.x, Is.LessThan(slowPoint.x));
            Assert.That(Vector3.Distance(Vector3.zero, slowPoint), Is.EqualTo(20 * slowTime).Within(.001f));
            Assert.That(Vector3.Distance(Vector3.zero, fastPoint), Is.EqualTo(40 * fastTime).Within(.001f));
        }

        [TestCase(50f)][TestCase(500f)][TestCase(900f)]
        public void AligningReticleWithLeadHitsMovingTargetDespiteThirdPersonCameraOffset(float speed)
        {
            var camera=Own(new GameObject("Third person aim projection regression")).AddComponent<Camera>();camera.enabled=false;
            camera.pixelRect=new Rect(0,0,1920,1080);camera.fieldOfView=65;camera.transform.position=new Vector3(0,7,-18);
            var muzzle=new Vector3(3,.8f,1.52f);var target=new Vector3(8,5,35);var velocity=new Vector3(7,2,-1);
            Assert.That(InterceptSolver.TrySolve(muzzle,target,velocity,speed,800,out var intercept,out float time),Is.True);
            var direction=(intercept-muzzle).normalized;
            var leadScreen=AimProjection.ScreenPoint(camera,direction);var aimedDirection=camera.ScreenPointToRay(leadScreen).direction;
            Assert.That(Vector3.Distance(muzzle+aimedDirection*(speed*time),target+velocity*time),Is.LessThan(.001f),"Aiming at the rendered pip must produce a real intercept from the gun's muzzle.");
            var oldDirection=camera.ScreenPointToRay(camera.WorldToScreenPoint(intercept)).direction;
            Assert.That(Vector3.Distance(muzzle+oldDirection*(speed*time),intercept),Is.GreaterThan(1),"Projecting the world intercept directly introduces third person parallax.");
            camera.transform.position+=new Vector3(12,-3,-20);var zoomed=AimProjection.ScreenPoint(camera,direction);
            Assert.That(Vector3.Distance(leadScreen,zoomed),Is.LessThan(.01f),"Camera distance and translation must not change the required firing direction.");
        }

        [Test]
        public void AimProfilesMergeGunsByWeaponDefinitionAndKeepDifferentSpeedsSeparate()
        {
            var sourceDb = Asset<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var sourceLaser = sourceDb.Get(BlockShape.Laser);
            var sourceCube = sourceDb.Get(BlockShape.Cube);
            var sourceCore = sourceDb.Get(BlockShape.Core);
            var laser = Own(UnityEngine.Object.Instantiate(sourceLaser));
            var cube = Own(UnityEngine.Object.Instantiate(sourceCube));
            var core = Own(UnityEngine.Object.Instantiate(sourceCore));
            var rapid = Own(UnityEngine.Object.Instantiate(sourceLaser.weapon));
            rapid.projectileSpeed = 80;
            var alternate = Own(UnityEngine.Object.Instantiate(sourceLaser.weapon));
            alternate.projectileSpeed = 140;
            laser.weapon = rapid;
            var alternateGun = Own(UnityEngine.Object.Instantiate(sourceLaser)); alternateGun.shape = BlockShape.Gun1; alternateGun.weapon = alternate;

            var database = Own(ScriptableObject.CreateInstance<ModuleDatabase>());
            database.modules = new List<ModuleDefinition> { core, cube, laser, alternateGun };
            BlockModules.Configure(database);
            var tuning = Own(ScriptableObject.CreateInstance<FlightTuning>());
            tuning.database = database;
            var battle = Own(ScriptableObject.CreateInstance<BattleTuning>());
            var root = Own(new GameObject("Aim profile assembly"));
            var assembly = root.AddComponent<ShipAssembly>();
            assembly.Configure(database);
            var blueprint = new BlueprintData
            {
                name = "two-weapon-profiles",
                parts = new List<BlockRecord> { Record(BlockShape.Core,0,0) }
            };
            for(int x=-4;x<=4;x++)if(x!=0)blueprint.parts.Add(Record(BlockShape.Cube,x,0));
            blueprint.parts.Add(Record(BlockShape.Laser,-4,1,0));
            blueprint.parts.Add(Record(BlockShape.Laser,-2,1,0));
            blueprint.parts.Add(Record(BlockShape.Gun1,0,1,0));
            blueprint.parts.Add(Record(BlockShape.Gun1,2,1,0));
            blueprint.parts.Add(Record(BlockShape.Gun1,4,1,0));
            assembly.Import(blueprint);
            var runtime = root.AddComponent<FlightShipRuntime>();
            runtime.Initialize(assembly, blueprint, tuning, battle);
            var weapons = new ShipWeapons();
            weapons.Initialize(runtime, new ProjectileWorld());

            var profiles = new List<WeaponAimProfile>();
            weapons.GetAimProfiles(profiles);
            Assert.That(profiles.Count, Is.EqualTo(2));
            var laserProfile = profiles.Find(p => p.weapon == rapid);
            var cubeProfile = profiles.Find(p => p.weapon == alternate);
            Assert.That(laserProfile.gunCount, Is.EqualTo(2));
            var representative=runtime.Parts.Find(p=>p.record.id==laserProfile.partId);
            Assert.That(representative,Is.Not.Null);
            Assert.That(Vector3.Distance(laserProfile.origin,representative.view.transform.TransformPoint(laser.muzzleLocalPosition)),Is.LessThan(.001f));
            Assert.That(cubeProfile.gunCount, Is.EqualTo(3));
            Assert.That(laserProfile.weapon.projectileSpeed, Is.EqualTo(80));
            Assert.That(cubeProfile.weapon.projectileSpeed, Is.EqualTo(140));
        }

        [Test]
        public void RealMeshClearanceAllowsSideTangencyButBlocksTheForwardPath()
        {
            var laser = Asset<ModuleDefinition>("Assets/_Starframe/Settings/Modules/Laser.asset");
            var mesh = Asset<Mesh>("Assets/_Starframe/Art/Models/BeveledCube.asset");
            if (!mesh) mesh = Own(BlockGeometry.CreateSolid(BlockShape.Cube));

            var gun = new BlockRecord { shape = BlockShape.Laser, position = new Vector3(0, .5f, 0), rotation = Quaternion.identity };
            var clearance = new WeaponClearance();
            var sideCube = new BlockRecord { shape = BlockShape.Cube, position = new Vector3(-1, .5f, 4), rotation = Quaternion.identity };
            var forwardCube = new BlockRecord { shape = BlockShape.Cube, position = new Vector3(0, .5f, 4), rotation = Quaternion.identity };

            Assert.That(clearance.IsBlockedBy(gun, laser, sideCube, mesh), Is.False,
                "The cube only touches the beam's side boundary; mesh triangles should not be treated as an AABB.");
            Assert.That(clearance.IsBlockedBy(gun, laser, forwardCube, mesh), Is.True);
        }

        [Test]
        public void CanPlaceChecksNewGunAndExistingGunClearanceAgainstRealAssets()
        {
            var database = Asset<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var root = Own(new GameObject("Aim clearance assembly"));
            var assembly = root.AddComponent<ShipAssembly>();
            assembly.Configure(database);

            var support = new BlueprintData { name = "physically-supported-clearance", parts = new List<BlockRecord> { Record(BlockShape.Core,0,0) } };
            for(int z=1;z<=4;z++)support.parts.Add(Record(BlockShape.Cube,0,z));
            support.parts.Add(Record(BlockShape.Cube,0,1,4));
            assembly.Import(support);
            Assert.That(assembly.CanPlace(BlockShape.Laser,new Vector3(0,1.5f,0),Quaternion.identity),Is.False,
                "A physically supported candidate gun may not point through an existing hull part.");
            Assert.That(assembly.PlacementFailure,Does.Contain("炮口"),"The candidate must reach the muzzle-clearance check, rather than fail attachment or occupancy.");

            support.parts.RemoveAt(support.parts.Count-1);
            for(int z=0;z<=4;z++)support.parts.Add(Record(BlockShape.Cube,-1,z));
            support.parts.Add(Record(BlockShape.Laser,0,1,0));
            assembly.Import(support);
            Assert.That(assembly.CanPlace(BlockShape.Cube,new Vector3(0,1.5f,4),Quaternion.identity),Is.False,
                "A physically supported newly placed part may not block an existing gun's muzzle path.");
            Assert.That(assembly.PlacementFailure,Does.Contain("开火路径"));
            Assert.That(assembly.CanPlace(BlockShape.Cube,new Vector3(-1,1.5f,4),Quaternion.identity),Is.True,
                "A supported part beside the actual beam remains placeable.");
        }

        [TestCase(1,0,0)][TestCase(-1,0,0)][TestCase(0,1,0)][TestCase(0,-1,0)][TestCase(0,0,1)][TestCase(0,0,-1)]
        public void FullCellLaserColliderPreservesOccupancyOnEveryBottomMountingAxis(int x,int y,int z)
        {
            var database=Asset<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var root=Own(new GameObject("Laser placement offset regression"));var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
            var bounds=assembly.Collision(BlockShape.Laser).bounds;
            Assert.That(bounds.center.x,Is.EqualTo(0).Within(.000001f));
            Assert.That(bounds.min.x,Is.EqualTo(-.5f).Within(.000001f));
            Assert.That(bounds.size,Is.EqualTo(new Vector3(1,1,3)));
            var rotation=BlockPlacement.FaceRotation(new Vector3(x,y,z),0);var position=new Vector3(0,.5f,0);
            Assert.That(assembly.Place(BlockShape.Cube,position-rotation*Vector3.up,Quaternion.identity),Is.True);
            Assert.That(assembly.CanPlace(BlockShape.Laser,position,rotation),Is.True,assembly.PlacementFailure);
            var gun=new BlockRecord{shape=BlockShape.Laser,position=position,rotation=rotation};
            var side=new BlockRecord{shape=BlockShape.Cube,position=position-rotation*Vector3.right,rotation=Quaternion.identity};
            Assert.That(BlockConnectivity.Touches(gun,side),Is.False,"Complete integer collision cells do not turn the weapon's sides into mounting faces.");
        }

        [Test]
        public void SupportedWeaponAllowsWideHitVolumeWithoutBlockingTheMuzzle()
        {
            var source=Asset<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var database=Own(UnityEngine.Object.Instantiate(source));database.modules=new List<ModuleDefinition>(source.modules);
            var laser=Own(UnityEngine.Object.Instantiate(source.Get(BlockShape.Laser)));laser.weapon=Own(UnityEngine.Object.Instantiate(laser.weapon));
            laser.weapon.projectileDiameter=2;laser.muzzleClearanceDiameter=.25f;
            database.modules.RemoveAll(m=>m.shape==BlockShape.Laser);database.modules.Add(laser);
            var root=Own(new GameObject("Test blueprint side mount regression"));var assembly=root.AddComponent<ShipAssembly>();assembly.Configure(database);
            var fixture=new BlueprintData{name="wide-hit-volume-clearance",parts=new List<BlockRecord>{Record(BlockShape.Core,0,0)}};
            for(int z=0;z<=4;z++)fixture.parts.Add(Record(BlockShape.Cube,1,z));
            fixture.parts.Add(Record(BlockShape.Cube,1,1,4));assembly.Import(fixture);
            var position=new Vector3(0,1.5f,0);var rotation=Quaternion.identity;
            Assert.That(assembly.CanPlace(BlockShape.Laser,position,rotation),Is.True,assembly.PlacementFailure);
            Assert.That(assembly.Place(BlockShape.Laser,position,rotation),Is.True);
            var blueprint=assembly.Export("clear-side-mount");var tuning=Own(ScriptableObject.CreateInstance<FlightTuning>());tuning.database=database;
            var runtime=root.AddComponent<FlightShipRuntime>();runtime.Initialize(assembly,blueprint,tuning);
            var gun=runtime.Parts.Find(p=>p.record.shape==BlockShape.Laser&&(p.record.position-position).sqrMagnitude<.00001f);
            var muzzle=position+rotation*laser.muzzleLocalPosition;var direction=rotation*Vector3.forward;var world=new ProjectileWorld();
            Assert.That(world.OwnPathBlocked(runtime,gun,muzzle,direction,laser.muzzleClearanceDiameter),Is.False);
            Assert.That(world.OwnPathBlocked(runtime,gun,muzzle,direction,laser.weapon.projectileDiameter),Is.True,"A wider damage volume must not set the physical muzzle clearance.");
            var cube=Own(BlockGeometry.CreateSolid(BlockShape.Cube));var obstacle=new BlockRecord{shape=BlockShape.Cube,position=muzzle+direction*3};
            Assert.That(new WeaponClearance().IsBlockedBy(gun.record,laser,obstacle,cube),Is.True,"A real obstruction directly ahead must still block placement.");
        }

        [Test]
        public void MultiBarrelPlacementChecksEveryBoreNotOnlyTheAveragedMuzzle()
        {
            var db = Asset<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
            var module = db.Get(BlockShape.Gun1);
            Assert.That(module.weapon.muzzleOffsets.Length, Is.EqualTo(2), "Gun1 is the two-barrel reference.");
            var gun = new BlockRecord { shape = BlockShape.Gun1, position = Vector3.zero, rotation = Quaternion.identity };
            var solid = Own(BlockGeometry.CreateSolid(BlockShape.Cube));
            var probe = Own(Scaled(solid, .04f));
            var clearance = new WeaponClearance();
            foreach (var offset in module.weapon.muzzleOffsets)
            {
                var bore = module.muzzleLocalPosition + offset;
                var obstacle = new BlockRecord { shape = BlockShape.Cube, position = bore + Vector3.forward * 3f };
                Assert.That(clearance.IsBlockedBy(gun, module, obstacle, probe), Is.True,
                    "Each individual bore must be checked, not only the average muzzle at " + module.muzzleLocalPosition);
            }
            var between = new BlockRecord { shape = BlockShape.Cube, position = module.muzzleLocalPosition + Vector3.forward * 3f };
            Assert.That(clearance.IsBlockedBy(gun, module, between, probe), Is.False,
                "A small block between the bores must not be reported as blocking a barrel");
        }

        static Mesh Scaled(Mesh source, float scale)
        {
            var mesh = UnityEngine.Object.Instantiate(source);
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] *= scale;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            return mesh;
        }

        T Asset<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);
        T Own<T>(T value) where T : UnityEngine.Object { owned.Add(value); return value; }
        static BlockRecord Record(BlockShape shape,int x,int z)=>Record(shape,x,0,z);
        static BlockRecord Record(BlockShape shape, int x, int y, int z) => new BlockRecord
        {
            id = shape + "-" + x + "-" + y + "-" + z,
            shape = shape,
            position = new Vector3(x, y+.5f, z),
            cell = new Vector3Int(x, y, z),
            rotation = Quaternion.identity
        };
    }
}
