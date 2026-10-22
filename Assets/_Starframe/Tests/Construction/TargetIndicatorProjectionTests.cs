using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;

namespace Starframe.Tests
{
    public sealed class TargetIndicatorProjectionTests
    {
        static readonly Rect TestRect = new Rect(-960f, -540f, 1920f, 1080f);

        GameObject cameraObject;
        Camera camera;

        [SetUp]
        public void SetUp()
        {
            cameraObject = new GameObject("TargetIndicatorProjectionTestCamera");
            camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
        }

        [TearDown]
        public void TearDown()
        {
            if (cameraObject) Object.DestroyImmediate(cameraObject);
        }

        [Test]
        public void InViewTargetDoesNotRequestEdgeIndicator()
        {
            bool shown = TargetIndicatorProjection.TryEdge(camera, new Vector3(0f, 0f, 10f),
                TestRect, 40f, out _, out _);

            Assert.That(shown, Is.False);
        }

        [Test]
        public void RearRightTargetKeepsRightwardEdgeDirection()
        {
            bool shown = TargetIndicatorProjection.TryEdge(camera, new Vector3(2f, 0f, -10f),
                TestRect, 40f, out Vector2 edge, out Vector2 direction);

            Assert.That(shown, Is.True);
            Assert.That(direction.x, Is.GreaterThan(0f));
            Assert.That(edge.x, Is.EqualTo(TestRect.xMax - 40f).Within(.01f));
        }

        [Test]
        public void DirectlyBehindTargetPointsDownWithoutInvalidCoordinates()
        {
            bool shown = TargetIndicatorProjection.TryEdge(camera, new Vector3(0f, 0f, -10f),
                TestRect, 40f, out Vector2 edge, out Vector2 direction);

            Assert.That(shown, Is.True);
            Assert.That(direction.x, Is.EqualTo(0f).Within(.001f));
            Assert.That(direction.y, Is.LessThan(0f));
            Assert.That(edge.y, Is.EqualTo(TestRect.yMin + 40f).Within(.01f));
            Assert.That(float.IsNaN(edge.x) || float.IsInfinity(edge.x), Is.False);
        }

        [Test]
        public void OffscreenTargetsAboveAndBelowPointToCorrespondingEdges()
        {
            bool above = TargetIndicatorProjection.TryEdge(camera, new Vector3(0f, 10f, 5f),
                TestRect, 40f, out Vector2 upperEdge, out Vector2 upperDirection);
            bool below = TargetIndicatorProjection.TryEdge(camera, new Vector3(0f, -10f, 5f),
                TestRect, 40f, out Vector2 lowerEdge, out Vector2 lowerDirection);

            Assert.That(above, Is.True);
            Assert.That(below, Is.True);
            Assert.That(upperDirection.y, Is.GreaterThan(0f));
            Assert.That(lowerDirection.y, Is.LessThan(0f));
            Assert.That(upperEdge.y, Is.EqualTo(TestRect.yMax - 40f).Within(.01f));
            Assert.That(lowerEdge.y, Is.EqualTo(TestRect.yMin + 40f).Within(.01f));
        }
    }
}
