using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Starframe.Tests
{
    public sealed class SpaceMapStudioPreviewTests
    {
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        EditorWindow window;
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        object Call(string name, params object[] args) => window.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(window, args);
        T Field<T>(string name) => (T)window.GetType().GetField(name, Private).GetValue(window);
        VoxelAssetDefinition Source(int length)
        {
            var value = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); value.hideFlags = HideFlags.HideAndDontSave;
            value.name = "Preview lifecycle fixture " + length;
            for (int i = 0; i < length; i++) value.cells.Add(new VoxelCell { position = Vector3Int.right * i });
            owned.Add(value); return value;
        }
        void OpenWindow()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Starframe.EditorTools.SpaceMapStudioWindow")).FirstOrDefault(t => t != null);
            Assert.That(type, Is.Not.Null);
            window = ScriptableObject.CreateInstance(type) as EditorWindow; window.Show(); Call("CreateGUI");
        }
        VoxelStructureRuntime[] PreviewBodies() => Field<GameObject>("previewRoot").GetComponentsInChildren<VoxelStructureRuntime>();
        [TearDown] public void Cleanup()
        {
            if (window) { Call("DisposePreview"); window.Close(); UnityEngine.Object.DestroyImmediate(window); }
            foreach (var value in owned) if (value) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear();
        }

        [Test] public void SignedSurfaceAppearsInAssetAndMapPreviewAndVoxelEditsInvalidateIt()
        {
            OpenWindow(); var source = Source(2);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            owned.Add(mesh); source.initialLodMesh = mesh;
            source.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(source);
            Assert.That(source.HasValidInitialLod, Is.True);
            Call("OpenAsset", source); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            var map = ScriptableObject.CreateInstance<SpaceMapDefinition>(); owned.Add(map);
            map.instances.Add(new VoxelMapInstance { asset = source });
            Call("OpenMap", map); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
            source.cells.Add(new VoxelCell { position = Vector3Int.up }); source.NotifyChanged();
            Assert.That(source.HasValidInitialLod, Is.False);
            Call("OpenAsset", source); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh, Is.Not.SameAs(mesh));
        }

        [Test] public void SignedCubeSurfaceUsesLazyTemplateAndExactFallbackRetainsMaterialSlots()
        {
            var source = Source(2);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            owned.Add(mesh); source.initialLodMesh = mesh;
            source.initialLodSignature = SpaceMapRuntime.ComputeGeometrySignature(source);
            var template = VoxelTemplateCache.Acquire(source, true);
            try
            {
                Assert.That(template.initialLodMesh, Is.SameAs(mesh));
                Assert.That(VoxelTemplateCache.EnsureExactMesh(template).subMeshCount, Is.EqualTo(1));
            }
            finally { VoxelTemplateCache.Release(template); }
            mesh.subMeshCount = 2;
            Assert.That(source.HasValidInitialLod, Is.False);
        }

        [Test] public void AcquiringAfterNativeMeshWasDestroyedBuildsAUsableReplacement()
        {
            var source = Source(3); var old = VoxelTemplateCache.Acquire(source);
            UnityEngine.Object.DestroyImmediate(old.mesh);
            var replacement = VoxelTemplateCache.Acquire(source);
            Assert.That((bool)replacement.mesh, Is.True); Assert.That(replacement.mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(replacement, Is.Not.SameAs(old));
            VoxelTemplateCache.Release(old); Assert.That((bool)replacement.mesh, Is.True);
            VoxelTemplateCache.Release(replacement); Assert.That((bool)replacement.mesh, Is.False);
        }

        [UnityTest] public IEnumerator ManagedCacheMeshSurvivesUnusedResourceUnloadingUntilItsLastOwnerReleasesIt()
        {
            var template = VoxelTemplateCache.Acquire(Source(2));
            yield return Resources.UnloadUnusedAssets();
            Assert.That((bool)template.mesh, Is.True);
            VoxelTemplateCache.Release(template); Assert.That((bool)template.mesh, Is.False);
        }

        [Test] public void RepeatedAssetMapSwitchesReleasePreviewOwnershipAndKeepAllMeshesVisible()
        {
            OpenWindow(); var a = Source(2); var b = Source(5); var c = Source(1);
            var map = ScriptableObject.CreateInstance<SpaceMapDefinition>(); owned.Add(map);
            for (int i = 0; i < 12; i++) map.instances.Add(new VoxelMapInstance { asset = i % 2 == 0 ? a : b, position = Vector3.right * i * 8 });
            string original = JsonUtility.ToJson(map);
            Mesh previous = null;
            for (int cycle = 0; cycle < 8; cycle++)
            {
                foreach (var source in new[] { a, b, c })
                {
                    Call("OpenAsset", source); Call("RebuildPreview");
                    if (!ReferenceEquals(previous, null)) Assert.That((bool)previous, Is.False, "The previous isolated preview still owns a mesh.");
                    var bodies = PreviewBodies(); Assert.That(bodies.Length, Is.EqualTo(1)); Assert.That(bodies[0].AliveCount, Is.EqualTo(source.cells.Count));
                    previous = bodies[0].GetComponent<MeshFilter>().sharedMesh;
                    Assert.That((bool)previous, Is.True); Assert.That(previous.vertexCount, Is.GreaterThan(0));
                    Assert.That(bodies[0].Body, Is.Null);
                }
                Call("OpenMap", map); Call("RebuildPreview");
                Assert.That((bool)previous, Is.False);
                var parts = PreviewBodies(); Assert.That(parts.Length, Is.EqualTo(12));
                Assert.That(parts.All(x => x.GetComponent<MeshFilter>().sharedMesh && x.AliveCount > 0), Is.True);
                Assert.That(parts[0].GetComponent<MeshFilter>().sharedMesh, Is.SameAs(parts[2].GetComponent<MeshFilter>().sharedMesh));
                previous = parts[0].GetComponent<MeshFilter>().sharedMesh;
            }
            Call("DisposePreview"); Assert.That((bool)previous, Is.False);
            foreach (var source in new[] { a, b, c })
            {
                var lease = VoxelTemplateCache.Acquire(source); Assert.That(lease.users, Is.EqualTo(1), "An editor preview leaked a cache owner."); VoxelTemplateCache.Release(lease);
            }
            Assert.That(JsonUtility.ToJson(map), Is.EqualTo(original));
        }



        [Test] public void ConnectivityWarningsCacheByGeometryRevisionAndCountMapAssetsAndInstances()
        {
            var diagnosticsType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Starframe.EditorTools.VoxelConnectivityDiagnostics", false))
                .FirstOrDefault(type => type != null);
            Assert.That(diagnosticsType, Is.Not.Null);
            var assetSummaryMethod = diagnosticsType.GetMethod("ForAsset", BindingFlags.Static | BindingFlags.NonPublic);
            var mapSummaryMethod = diagnosticsType.GetMethod("ForMap", BindingFlags.Static | BindingFlags.NonPublic);
            var disconnected = Source(3);
            disconnected.cells[2].position = Vector3Int.right * 10;
            disconnected.NotifyChanged();

            object first = assetSummaryMethod.Invoke(null, new object[] { disconnected });
            object cached = assetSummaryMethod.Invoke(null, new object[] { disconnected });
            Assert.That(cached, Is.SameAs(first), "A repaint query in the same geometry generation should reuse its BFS result.");
            Assert.That((int)first.GetType().GetField("componentCount").GetValue(first), Is.EqualTo(2));

            var connected = Source(2);
            var map = ScriptableObject.CreateInstance<SpaceMapDefinition>(); owned.Add(map);
            map.instances.Add(new VoxelMapInstance { asset = disconnected });
            map.instances.Add(new VoxelMapInstance { asset = disconnected });
            map.instances.Add(new VoxelMapInstance { asset = connected });
            object mapSummary = mapSummaryMethod.Invoke(null, new object[] { map });
            Assert.That((int)mapSummary.GetType().GetField("disconnectedAssetCount").GetValue(mapSummary), Is.EqualTo(1));
            Assert.That((int)mapSummary.GetType().GetField("disconnectedInstanceCount").GetValue(mapSummary), Is.EqualTo(2));
            Assert.That((int)mapSummary.GetType().GetField("possibleRigidBodyCount").GetValue(mapSummary), Is.EqualTo(4));

            disconnected.cells[2].position = Vector3Int.right * 2;
            disconnected.NotifyChanged();
            object refreshed = assetSummaryMethod.Invoke(null, new object[] { disconnected });
            Assert.That(refreshed, Is.Not.SameAs(first), "A geometry revision change must invalidate the cached BFS result.");
            Assert.That((int)refreshed.GetType().GetField("componentCount").GetValue(refreshed), Is.EqualTo(1));
            mapSummary = mapSummaryMethod.Invoke(null, new object[] { map });
            Assert.That((int)mapSummary.GetType().GetField("disconnectedAssetCount").GetValue(mapSummary), Is.Zero);
            Assert.That((int)mapSummary.GetType().GetField("disconnectedInstanceCount").GetValue(mapSummary), Is.Zero);
            Assert.That((int)mapSummary.GetType().GetField("possibleRigidBodyCount").GetValue(mapSummary), Is.Zero);

            var emptySummary = assetSummaryMethod.Invoke(null, new object[] { Source(0) });
            Assert.That((int)emptySummary.GetType().GetField("componentCount").GetValue(emptySummary), Is.Zero);
            var emptyMap = ScriptableObject.CreateInstance<SpaceMapDefinition>(); owned.Add(emptyMap);
            var emptyMapSummary = mapSummaryMethod.Invoke(null, new object[] { emptyMap });
            Assert.That((int)emptyMapSummary.GetType().GetField("disconnectedAssetCount").GetValue(emptyMapSummary), Is.Zero);
            Assert.That((int)emptyMapSummary.GetType().GetField("disconnectedInstanceCount").GetValue(emptyMapSummary), Is.Zero);
            Assert.That((int)emptyMapSummary.GetType().GetField("possibleRigidBodyCount").GetValue(emptyMapSummary), Is.Zero);
        }

        [Test] public void PreviewCanRecoverAfterAPlayTransitionDestroysItsNativeResources()
        {
            OpenWindow(); var source = Source(3); Call("OpenAsset", source); Call("RebuildPreview");
            var mesh = PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh;
            Call("OnPlayModeChanged", PlayModeStateChange.ExitingEditMode); Assert.That((bool)mesh, Is.False);
            Call("OnPlayModeChanged", PlayModeStateChange.EnteredPlayMode); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(0));
            Call("OnPlayModeChanged", PlayModeStateChange.ExitingPlayMode);
            Call("OnPlayModeChanged", PlayModeStateChange.EnteredEditMode); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].AliveCount, Is.EqualTo(3));
        }

        [Test] public void ClosingAndReopeningWindowDoesNotReuseADeadMesh()
        {
            OpenWindow(); var source = Source(4); Call("OpenAsset", source); Call("RebuildPreview");
            var mesh = PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh;
            window.Close(); UnityEngine.Object.DestroyImmediate(window); window = null;
            Assert.That((bool)mesh, Is.False);
            OpenWindow(); Call("OpenAsset", source); Call("RebuildPreview");
            Assert.That(PreviewBodies()[0].AliveCount, Is.EqualTo(4)); Assert.That((bool)PreviewBodies()[0].GetComponent<MeshFilter>().sharedMesh, Is.True);
        }
    }
}
