// @role Render surviving hull parts with shared instanced geometry.
// @calls FlightShipRuntime

using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.Combat
{
    /// <summary>Instances repeated opaque ship meshes without merging damage, hitboxes or blueprint parts.</summary>
    [DisallowMultipleComponent]
    public sealed class ShipInstanceRenderer : MonoBehaviour
    {
        public const int MinimumPartCount = 64;
        const int InstancesPerBatch = 256;
        static readonly ProfilerMarker DrawMarker = new ProfilerMarker("Starframe.ShipInstances");

        sealed class Entry
        {
            public ShipPartState part;
            public Matrix4x4 localMatrix;
        }

        sealed class Batch
        {
            public Mesh mesh;
            public int submesh;
            public RenderParams parameters;
            public Bounds localBounds;
            public readonly List<Entry> entries = new List<Entry>(InstancesPerBatch);
            public readonly Matrix4x4[] matrices = new Matrix4x4[InstancesPerBatch];
        }

        readonly List<Batch> batches = new List<Batch>();
        readonly List<MeshRenderer> sources = new List<MeshRenderer>();
        readonly Dictionary<Material, Material> materials = new Dictionary<Material, Material>();
        Transform assemblyRoot;
        public int BatchedRendererCount => sources.Count;
        public int SubmittedBatchCount { get; private set; }
        public int VisibleInstanceCount { get; private set; }

        public void Initialize(FlightShipRuntime ship, ShipAssembly assembly)
        {
            Release();
            if (!SystemInfo.supportsInstancing || ship.Parts.Count < MinimumPartCount) return;
            assemblyRoot = assembly.transform;
            var groups = new Dictionary<(Mesh, Material, int, int, ShadowCastingMode, bool, uint), List<(MeshRenderer, Entry)>>();
            var inverse = assemblyRoot.worldToLocalMatrix;
            foreach (var part in ship.Parts)
            {
                if (!part.view) continue;
                foreach (var source in part.view.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (!source.enabled || source.HasPropertyBlock()) continue;
                    var filter = source.GetComponent<MeshFilter>();
                    var mesh = filter ? filter.sharedMesh : null;
                    var shared = source.sharedMaterials;
                    // Custom energy/particle shaders and per-renderer properties retain their normal path.
                    if (!mesh || shared.Length != mesh.subMeshCount) continue;
                    bool eligible = true;
                    foreach (var material in shared)
                        if (!material || !material.shader.name.StartsWith("Universal Render Pipeline/") || material.renderQueue > 2500)
                            eligible = false;
                    if (!eligible) continue;
                    for (int submesh = 0; submesh < shared.Length; submesh++)
                    {
                        var key = (mesh, shared[submesh], submesh, source.gameObject.layer,
                            source.shadowCastingMode, source.receiveShadows, source.renderingLayerMask);
                        if (!groups.TryGetValue(key, out var entries))
                            groups.Add(key, entries = new List<(MeshRenderer, Entry)>());
                        entries.Add((source, new Entry { part = part, localMatrix = inverse * source.transform.localToWorldMatrix }));
                    }
                }
            }

            var batched = new HashSet<MeshRenderer>();
            foreach (var pair in groups)
            {
                // All submeshes of a renderer must be submitted if its normal renderer is disabled.
                bool multiSubmesh = pair.Key.Item1.subMeshCount > 1;
                if (pair.Value.Count < 2 && !multiSubmesh) continue;
                var original = pair.Key.Item2;
                if (!materials.TryGetValue(original, out var material))
                {
                    material = new Material(original) { name = original.name + " (ship instances)", enableInstancing = true };
                    materials.Add(original, material);
                }
                Batch batch = null;
                foreach (var item in pair.Value)
                {
                    if (batch == null || batch.entries.Count == InstancesPerBatch)
                    {
                        var source = item.Item1;
                        batch = new Batch
                        {
                            mesh = pair.Key.Item1,
                            submesh = pair.Key.Item3,
                            parameters = new RenderParams(material)
                            {
                                layer = source.gameObject.layer,
                                renderingLayerMask = source.renderingLayerMask,
                                shadowCastingMode = source.shadowCastingMode,
                                receiveShadows = source.receiveShadows,
                                lightProbeUsage = source.lightProbeUsage,
                                reflectionProbeUsage = source.reflectionProbeUsage,
                                motionVectorMode = source.motionVectorGenerationMode
                            },
                            localBounds = TransformBounds(item.Item2.localMatrix, pair.Key.Item1.bounds)
                        };
                        batches.Add(batch);
                    }
                    batch.entries.Add(item.Item2);
                    batch.localBounds.Encapsulate(TransformBounds(item.Item2.localMatrix, batch.mesh.bounds));
                    batched.Add(item.Item1);
                }
            }
            sources.AddRange(batched);
            SetSourceVisibility(!isActiveAndEnabled);
        }

        void LateUpdate()
        {
            SubmittedBatchCount = VisibleInstanceCount = 0;
            if (!assemblyRoot || !assemblyRoot.gameObject.activeInHierarchy) return;
            using (DrawMarker.Auto())
            {
                Matrix4x4 root = assemblyRoot.localToWorldMatrix;
                foreach (var batch in batches)
                {
                    int count = 0;
                    foreach (var entry in batch.entries)
                        if (entry.part.present && entry.part.hp > 0 && entry.part.view && entry.part.view.activeInHierarchy)
                            batch.matrices[count++] = root * entry.localMatrix;
                    if (count == 0) continue;
                    batch.parameters.worldBounds = TransformBounds(root, batch.localBounds);
                    Graphics.RenderMeshInstanced(batch.parameters, batch.mesh, batch.submesh, batch.matrices, count);
                    SubmittedBatchCount++;
                    VisibleInstanceCount += count;
                }
            }
        }

        static Bounds TransformBounds(Matrix4x4 matrix, Bounds bounds)
        {
            Vector3 x = matrix.MultiplyVector(new Vector3(bounds.extents.x, 0, 0));
            Vector3 y = matrix.MultiplyVector(new Vector3(0, bounds.extents.y, 0));
            Vector3 z = matrix.MultiplyVector(new Vector3(0, 0, bounds.extents.z));
            return new Bounds(matrix.MultiplyPoint3x4(bounds.center), new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2f);
        }

        void SetSourceVisibility(bool visible)
        {
            foreach (var source in sources) if (source) source.enabled = visible;
        }

        void OnEnable() => SetSourceVisibility(false);
        void OnDisable() { SetSourceVisibility(true); SubmittedBatchCount = VisibleInstanceCount = 0; }
        void OnDestroy() => Release();
        void Release()
        {
            SetSourceVisibility(true);
            sources.Clear();
            batches.Clear();
            foreach (var material in materials.Values)
                if (material) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            materials.Clear();
            SubmittedBatchCount = VisibleInstanceCount = 0;
        }
    }
}
