using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;

namespace Starframe.Construction.Tests
{
    public sealed class TargetMarkerGraphicTests
    {
        [Test] public void DiscoveredTriangleTipMatchesTheSelectableCoreProjectionAndPointsDown()
        {
            using (var vertices = new UnityEngine.UI.VertexHelper())
            {
                var projection = new Vector2(273, -82);
                typeof(TargetAimGraphic).GetMethod("DrawDetectionMarker", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { vertices, projection, 1f });
                Assert.That(vertices.currentVertCount, Is.EqualTo(3));
                var vertex = new UnityEngine.UIVertex();
                vertices.PopulateUIVertex(ref vertex, 0);
                Assert.That((Vector2)vertex.position, Is.EqualTo(projection));
                for (int i = 0; i < 3; i++)
                {
                    vertices.PopulateUIVertex(ref vertex, i);
                    Assert.That(vertex.color, Is.EqualTo((Color32)TargetAimGraphic.EnemyTint));
                    if (i > 0) Assert.That(vertex.position.y, Is.GreaterThan(projection.y));
                }
            }
        }

        [Test] public void LeadMarkersUseFourDifferentShapesWhileKeepingOneEnemyColor()
        {
            var counts = new System.Collections.Generic.HashSet<int>();
            for (int shape = 0; shape < 4; shape++) using (var vertices = new UnityEngine.UI.VertexHelper())
            {
                typeof(TargetAimGraphic).GetMethod("DrawRefinedLead", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { vertices, Vector2.zero, 11f, TargetAimGraphic.EnemyTint, shape, 1f });
                counts.Add(vertices.currentVertCount);
                var vertex = new UnityEngine.UIVertex();
                for (int i = 0; i < vertices.currentVertCount; i++)
                {
                    vertices.PopulateUIVertex(ref vertex, i);
                    Assert.That(vertex.color, Is.EqualTo((Color32)TargetAimGraphic.EnemyTint));
                }
            }
            Assert.That(counts.Count, Is.EqualTo(4), "Different projectile-speed profiles remain distinguishable without color coding.");
        }
    }
}
