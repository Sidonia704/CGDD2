using System.Collections.Generic;
using NUnit.Framework;
using Starframe.Flight;
using UnityEngine;

namespace Starframe.Construction.Tests
{
    public sealed class ShipCollisionSweepTests
    {
        readonly List<GameObject> objects = new List<GameObject>();
        Rigidbody body;
        ShipCollisionSweep sweep;

        GameObject Make(string name)
        {
            var value = new GameObject(name); objects.Add(value); return value;
        }

        [SetUp] public void SetUp()
        {
            body = Make("Compound ship").AddComponent<Rigidbody>(); body.isKinematic = true;
            foreach (float x in new[] { 0f, 1f, 2f, 3f, 4f })
            {
                var cell = Make("Body cell"); cell.transform.SetParent(body.transform, false);
                cell.transform.localPosition = Vector3.right * x; cell.AddComponent<BoxCollider>();
            }
            Physics.SyncTransforms(); sweep = new ShipCollisionSweep(); sweep.Initialize(body);
        }

        [TearDown] public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) if (objects[i]) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        [Test] public void ClearSpaceSkipsTheCompoundSweepDespiteItsOwnColliders()
        {
            var trigger = Make("Nonphysical reference").AddComponent<BoxCollider>();
            trigger.isTrigger = true; trigger.transform.position = Vector3.right * 6;
            Physics.SyncTransforms();
            Assert.That(sweep.MayHit(Vector3.right * 5), Is.False);
        }

        [Test] public void DetectsObstaclesAtTheFarEndOfTravelAndAfterTeleportOrRotation()
        {
            var wall = Make("Static wall").AddComponent<BoxCollider>();
            for (int i = 0; i < 4; i++)
            {
                body.position = new Vector3(40 * i, 5, 20);
                body.rotation = Quaternion.Euler(0, i * 90, 0);
                wall.transform.position = body.position + body.rotation * (Vector3.right * 8);
                Physics.SyncTransforms();
                var travel = body.rotation * (Vector3.right * 5);
                Assert.That(body.SweepTest(travel.normalized, out _, travel.magnitude, QueryTriggerInteraction.Ignore), Is.True);
                Assert.That(sweep.MayHit(travel), Is.True);
            }
        }

        [Test] public void BufferGrowthDoesNotLoseAnObstacleInADenseScene()
        {
            for (int i = 0; i < 80; i++)
            {
                var cell = Make("Extra compound cell"); cell.transform.SetParent(body.transform, false);
                cell.AddComponent<BoxCollider>();
            }
            var obstacle = Make("Other rigid body"); obstacle.transform.position = Vector3.right * 6;
            obstacle.AddComponent<Rigidbody>().isKinematic = true; obstacle.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.That(sweep.MayHit(Vector3.right * 3), Is.True);
        }
    }
}
