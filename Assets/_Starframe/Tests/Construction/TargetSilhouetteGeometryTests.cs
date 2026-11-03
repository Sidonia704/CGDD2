using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Starframe.Combat;
using Starframe.Flight;

namespace Starframe.Construction.Tests
{
    public sealed class TargetSilhouetteGeometryTests
    {
        readonly List<Object> owned = new List<Object>();
        readonly List<TargetSilhouetteOutline.MaskDraw> draws = new List<TargetSilhouetteOutline.MaskDraw>();

        [TearDown] public void TearDown()
        {
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i]) Object.DestroyImmediate(owned[i]);
            owned.Clear(); draws.Clear();
        }

        [Test] public void CaptureIncludesInstancingHiddenHullAndExcludesChildEffects()
        {
            var ship = Ship();
            var part = Part(ship, Vector3.right * 3);
            part.view.GetComponent<MeshRenderer>().enabled = false;
            var childEffect = GameObject.CreatePrimitive(PrimitiveType.Cube);
            childEffect.transform.SetParent(part.view.transform, false);
            childEffect.name = "Forward ray / shield / plume must not enlarge the hull mask";
            childEffect.transform.localScale = Vector3.one * 100;
            TargetSilhouetteOutline.CaptureGeometry(ship, draws);
            Assert.That(draws.Count, Is.EqualTo(1));
            Assert.That(draws[0].Mesh, Is.SameAs(part.view.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(Vector3.Distance(draws[0].LocalMatrix.MultiplyPoint3x4(Vector3.zero), Vector3.right * 3), Is.LessThan(.0001f));
        }

        [Test] public void DamageRemovesDestroyedAndDetachedPartsWithoutLeavingGhostGeometry()
        {
            var ship = Ship();
            Part(ship, Vector3.zero);
            var destroyed = Part(ship, Vector3.right * 10);
            var detached = Part(ship, Vector3.left * 10);
            var absent = Part(ship, Vector3.up * 10);
            destroyed.hp = 0;
            detached.detached = true;
            absent.present = false;
            TargetSilhouetteOutline.CaptureGeometry(ship, draws);
            Assert.That(draws.Count, Is.EqualTo(1));
            Assert.That(draws[0].LocalMatrix.MultiplyPoint3x4(Vector3.zero), Is.EqualTo(Vector3.zero));
            TargetSilhouetteOutline.CaptureGeometry(null, draws);
            Assert.That(draws, Is.Empty, "Clearing a selected target must not retain its previous silhouette.");
        }

        [Test] public void CachedMatricesRemainLocalWhenTheWholeShipMovesAndRotates()
        {
            var ship = Ship();
            var part = Part(ship, new Vector3(3, 2, -1));
            part.view.transform.localRotation = Quaternion.Euler(0, 90, 0);
            TargetSilhouetteOutline.CaptureGeometry(ship, draws);
            var cached = draws[0].LocalMatrix;
            ship.transform.SetPositionAndRotation(new Vector3(90, 7, -40), Quaternion.Euler(15, 140, 30));
            TargetSilhouetteOutline.CaptureGeometry(ship, draws);
            for (int i = 0; i < 16; i++) Assert.That(draws[0].LocalMatrix[i], Is.EqualTo(cached[i]).Within(.0001f));
            var projected = ship.transform.localToWorldMatrix * cached;
            Assert.That(Vector3.Distance(projected.MultiplyPoint3x4(Vector3.zero), part.view.transform.position), Is.LessThan(.001f));
        }

        [Test] public void NonReadableImportedMeshesAndAllTriangleSubmeshesCanBeCapturedWithoutCpuVertexCopies()
        {
            var ship = Ship();
            var part = Part(ship, Vector3.zero);
            var mesh = Own(new Mesh { name = "Non-readable multi-material hull" });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, Vector3.forward };
            mesh.subMeshCount = 2;
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.SetTriangles(new[] { 0, 2, 3 }, 1);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            part.view.GetComponent<MeshFilter>().sharedMesh = mesh;
            Assert.That(mesh.isReadable, Is.False);
            Assert.DoesNotThrow(() => TargetSilhouetteOutline.CaptureGeometry(ship, draws));
            Assert.That(draws.Count, Is.EqualTo(2));
            Assert.That(draws[0].Submesh, Is.Zero);
            Assert.That(draws[1].Submesh, Is.EqualTo(1));
        }

        FlightShipRuntime Ship() => Own(new GameObject("Selected silhouette geometry test")).AddComponent<FlightShipRuntime>();
        ShipPartState Part(FlightShipRuntime ship, Vector3 offset)
        {
            var view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.transform.SetParent(ship.transform, false);
            view.transform.localPosition = offset;
            var part = new ShipPartState { view = view, present = true, hp = 10, maxHp = 10 };
            ship.Parts.Add(part); return part;
        }
        T Own<T>(T item) where T : Object { owned.Add(item); return item; }
    }
}
