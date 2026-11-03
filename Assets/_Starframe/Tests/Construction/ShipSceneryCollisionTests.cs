using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starframe.Tests
{
    // Actual native contacts in an isolated physics scene; no default-scene simulation or saved asset changes.
    public sealed class ShipSceneryCollisionTests
    {
        const float Step = .02f;
        readonly List<VoxelAssetDefinition> sources = new List<VoxelAssetDefinition>();
        readonly List<ShipSceneryCollision> contacts = new List<ShipSceneryCollision>();
        Scene previous, staging, preview;
        bool openedStaging;
        GameObject root;
        PhysicsScene physics;

        [SetUp] public void Setup()
        {
            previous = SceneManager.GetActiveScene();
            preview = EditorSceneManager.NewPreviewScene(); physics = preview.GetPhysicsScene();
            const string path = "Assets/_Starframe/Tests/Construction/Fixtures/TerrainTestStaging.unity";
            staging = SceneManager.GetSceneByPath(path);
            openedStaging = !staging.IsValid() || !staging.isLoaded;
            if (openedStaging) staging = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(staging);
            root = new GameObject("Isolated finite-mass contact fixture");
            SceneManager.MoveGameObjectToScene(root, preview);
            Assert.That(physics, Is.Not.EqualTo(Physics.defaultPhysicsScene));
        }
        [TearDown] public void Cleanup()
        {
            foreach (var contact in contacts) contact.Dispose(); contacts.Clear();
            if (root) Object.DestroyImmediate(root);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            if (preview.IsValid() && preview.isLoaded) EditorSceneManager.ClosePreviewScene(preview);
            if (openedStaging && staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
            foreach (var source in sources) if (source) Object.DestroyImmediate(source); sources.Clear();
        }
        GameObject Make(string name, Vector3 position)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, preview);
            go.transform.SetParent(root.transform, false); go.transform.localPosition = position; return go;
        }
        VoxelStructureRuntime Scenery(float density, Vector3 position, params Vector3Int[] cells)
        {
            var source = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); sources.Add(source);
            source.massPerCell = density; source.fragmentSeparationSpeed = 0;
            if (cells.Length == 0) cells = new[] { Vector3Int.zero };
            foreach (var cell in cells) source.cells.Add(new VoxelCell { position = cell });
            var result = Make("Scenery", position).AddComponent<VoxelStructureRuntime>(); result.Initialize(source);
            result.Body.interpolation = RigidbodyInterpolation.None; return result;
        }
        Rigidbody Ship(Vector3 position, Vector3 size, out ShipSceneryCollision contact)
        {
            var go = Make("Kinematic finite-mass ship", position);
            var body = go.AddComponent<Rigidbody>(); body.isKinematic = true; body.useGravity = false; body.mass = 1000;
            go.AddComponent<BoxCollider>().size = size;
            contact = new ShipSceneryCollision(); contact.Initialize(body); contacts.Add(contact); return body;
        }
        void Move(Rigidbody ship, ShipSceneryCollision contact, Vector3 position, Quaternion rotation)
        {
            contact.BeginStep(); contact.SetPose(position, rotation);
            ship.MovePosition(position); ship.MoveRotation(rotation); physics.Simulate(Step);
            physics.Simulate(Step); // Contact generation can observe a kinematic target on the following step.
        }
        [Test] public void NativeTranslationGivesHeavyStructureFarLessMotionThanLightFragment()
        {
            var heavy = Scenery(10000000, Vector3.zero);
            var light = Scenery(100, Vector3.forward * 10);
            var control = Scenery(100, Vector3.forward * 20);
            var heavyShip = Ship(new Vector3(-1.2f, 0, 0), Vector3.one, out var heavyContact);
            var lightShip = Ship(new Vector3(-1.2f, 0, 10), Vector3.one, out var lightContact);
            var controlShip = Ship(new Vector3(-1.2f, 0, 20), Vector3.one, out var controlContact);
            controlContact.Dispose();
            physics.Simulate(Step);
            heavyContact.BeginStep(); lightContact.BeginStep();
            heavyContact.SetPose(new Vector3(-.8f, 0, 0), Quaternion.identity);
            lightContact.SetPose(new Vector3(-.8f, 0, 10), Quaternion.identity);
            heavyShip.MovePosition(new Vector3(-.8f, 0, 0)); lightShip.MovePosition(new Vector3(-.8f, 0, 10));
            controlShip.MovePosition(new Vector3(-.8f, 0, 20));
            physics.Simulate(Step);
            physics.Simulate(Step);
            Assert.That(control.Body.linearVelocity.x, Is.GreaterThan(.1f), "Unmodified native contact must move the positive control.");
            Assert.That(light.Body.linearVelocity.x, Is.GreaterThanOrEqualTo(control.Body.linearVelocity.x * .8f),
                "Light fragments must retain at least 80% of the original native contact response.");
            Assert.That(heavy.Body.linearVelocity.magnitude, Is.LessThan(light.Body.linearVelocity.magnitude * .01f),
                "The heavy structure must move at least 100 times less than the light fragment.");
            Assert.That(heavy.Body.linearVelocity.magnitude, Is.LessThan(.01f), "A 10,000:1 mass ratio cannot inherit the kinematic ship speed.");
            Assert.That(heavy.Body.isKinematic || heavy.Body.useGravity, Is.False);
            Assert.That(light.Body.isKinematic || light.Body.useGravity, Is.False);
        }
        [Test] public void NativeRotationAndMultipleShipCollidersShareFiniteMomentum()
        {
            var heavy = Scenery(10000000, new Vector3(1.7f, 0, 1));
            var ship = Ship(Vector3.zero, new Vector3(4, 1, 1), out var contact);
            // Coincident compound contacts must not each get an independent ship-sized impulse budget.
            for (int i = 0; i < 8; i++)
            {
                var child = Make("Additional compound contact", Vector3.zero); child.transform.SetParent(ship.transform, false);
                child.AddComponent<BoxCollider>().size = new Vector3(4, 1, 1);
            }
            contact.Initialize(ship); physics.Simulate(Step);
            Move(ship, contact, ship.position, Quaternion.Euler(0, -30, 0));
            float angularSpeed = 30 * Mathf.Deg2Rad / Step;
            float momentumBound = ship.mass * angularSpeed * 3;
            Assert.That(heavy.Body.linearVelocity.magnitude, Is.GreaterThan(0), "Rotation must exercise actual native contact response.");
            Assert.That(heavy.Body.mass * heavy.Body.linearVelocity.magnitude, Is.LessThan(momentumBound * 1.5f),
                "All compound contacts together must remain within one finite ship momentum budget.");
            Assert.That(heavy.Body.linearVelocity.magnitude, Is.LessThan(.02f));
        }
        [Test] public void SweptImpulseIsNotAppliedAgainByNativeOverlapContacts()
        {
            var target = Scenery(100, Vector3.zero);
            var ship = Ship(new Vector3(-1.2f, 0, 0), Vector3.one, out var contact); physics.Simulate(Step);
            contact.BeginStep();
            contact.ApplySweptImpulse(target.Body, Vector3.right * 10, Vector3.zero, Vector3.left);
            contact.SetPose(new Vector3(-.8f, 0, 0), Quaternion.identity);
            ship.MovePosition(new Vector3(-.8f, 0, 0)); physics.Simulate(Step);
            physics.Simulate(Step);
            float expected = 10 * ship.mass / (ship.mass + target.Body.mass);
            Assert.That(target.Body.linearVelocity.x, Is.EqualTo(expected).Within(.01f));
        }
        [Test] public void SeveredPiecesKeepScaledMassIndependentPhysicsAndDestructibility()
        {
            var source = Scenery(100, Vector3.zero, Vector3Int.zero, Vector3Int.right, Vector3Int.right * 2);
            source.DisposeResources();
            source.transform.localScale = new Vector3(2, 3, 4);
            source.Initialize(sources[0]);
            Assert.That(source.Body.mass, Is.EqualTo(7200).Within(.01f));
            source.ApplyDamage(Vector3Int.right, 1000); source.FlushMeshChanges();
            var pieces = root.GetComponentsInChildren<VoxelStructureRuntime>();
            Assert.That(pieces.Length, Is.EqualTo(2));
            Assert.That(pieces.Sum(p => p.Body.mass), Is.EqualTo(4800).Within(.01f));
            foreach (var piece in pieces)
            {
                Assert.That(piece.Body.isKinematic || piece.Body.useGravity, Is.False);
                Assert.That(piece.ColliderCount, Is.GreaterThan(0));
                var remaining = piece.Cells.First(c => c.Value.Alive).Key;
                Assert.That(piece.ApplyDamage(remaining, 1000), Is.EqualTo(100));
                piece.FlushMeshChanges(); Assert.That(piece.AliveCount, Is.Zero);
            }
        }
    }
}
