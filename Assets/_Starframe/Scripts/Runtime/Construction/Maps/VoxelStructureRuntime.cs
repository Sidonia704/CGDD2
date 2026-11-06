// @role Simulate immutable drifting scenery with signed surface visuals and deferred collision components.
// @calls VoxelAssetDefinition, VoxelGeometry, VoxelBodyHitbox, VoxelTemplateCache, VoxelHullAppearance, VoxelTerrainColliders, VoxelTerrainRenderChunks, ShipSceneryCollision
// @tune driftVelocity:Vector3:-100-100:Initial world-space velocity; collisions subsequently control movement.
// @tune angularVelocity:Vector3:-180-180:Initial local angular velocity in degrees per second.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Starframe.Maps
{
    public sealed class VoxelCellState
    {
        public Vector3Int position;
        public int surface;
        public Starframe.Construction.BlockShape shape;
        public Vector3Int quarterTurns;
        public Vector3Int modelOffset;
        public Quaternion Rotation => Quaternion.Euler(quarterTurns.x * 90f, quarterTurns.y * 90f, quarterTurns.z * 90f);
        
        public VoxelStructureRuntime owner;
        public bool Alive => true;
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class VoxelStructureRuntime : MonoBehaviour
    {
        public VoxelAssetDefinition asset;
        public Vector3 driftVelocity, angularVelocity;
        [NonSerialized] public string AuthoringId;
        [NonSerialized] public bool CollectDiagnostics;

        IReadOnlyDictionary<Vector3Int, VoxelCellState> cells = EmptyCells;
        static readonly IReadOnlyDictionary<Vector3Int, VoxelCellState> EmptyCells = new Dictionary<Vector3Int, VoxelCellState>();
        readonly List<Mesh> renderMeshes = new List<Mesh>();
        readonly VoxelTerrainColliders collision = new VoxelTerrainColliders();
        VoxelTemplateCache.Template template;
        VoxelTerrainRenderChunks chunks;
        Material[] materials;
        Material fallback;
        readonly List<Material> ownedMaterials = new List<Material>();
        Mesh mesh;
        int sourceGeometryRevision;
        bool ownsMesh, previewOnly, dataOnlyPreview, lazyInitialVisual;
        bool deferredColliderConstruction;
        Vector3 centerOfCells;
        MeshFilter filter;
        MeshRenderer surfaceRenderer;
        Rigidbody body;
        BoxCollider boundsProxy;
        bool detailedCollisionEnabled = true;

        public IReadOnlyDictionary<Vector3Int, VoxelCellState> Cells => cells;
        public int CachedCellStateCount => cells is OwnedVoxelCells owned ? owned.CachedCount : 0;
        public int AliveCount { get; private set; }
        public int ColliderCount => collision.LogicalCount;
        public int DetailedColliderCost => collision.LogicalCount;
        public int MaterializedColliderCount => collision.Count;
        public int PrepareDetailedCollision(int maximumBoxes) => collision.PrepareMaterialization(gameObject, maximumBoxes);
        public bool UsesBoundsCollisionProxy => boundsProxy && boundsProxy.enabled;
        public Bounds LocalBounds { get; private set; }
        public Rigidbody Body => body;
        public bool DataOnlyPreview => dataOnlyPreview;
        public bool HasLazyInitialVisual => lazyInitialVisual;
        public int SourceGeometryRevision => sourceGeometryRevision;
        public IReadOnlyList<Mesh> RenderMeshes => renderMeshes;
        public int RenderRevision { get; private set; }
        public int RenderMeshCount => renderMeshes.Count;
        public int TotalVertices { get; private set; }
        public int GeometryBuildCount { get; private set; }
        public int ColliderBuildCount { get; private set; }
        // Immutable scenery has no per-frame cell maintenance.
        public const int ContractualIdleCellVisits = 0;
        public int IdleCellVisits => ContractualIdleCellVisits;
        public double GeometryMs { get; private set; }
        public double ColliderMs { get; private set; }
        public double MassMs { get; private set; }

        void Awake() { if (asset) Initialize(asset); }
        public void Initialize(VoxelAssetDefinition definition, bool preview = false, bool buildPreviewMesh = true,
            bool lazyFineVisuals = false, bool deferDetailedColliders = false)
        {
            if (!definition) throw new ArgumentException("Missing voxel asset.");
            Release(); ResetDiagnostics();
            asset = definition; sourceGeometryRevision = definition.GeometryRevision;
            previewOnly = preview; deferredColliderConstruction = deferDetailedColliders && !preview;
            dataOnlyPreview = preview && !buildPreviewMesh;
            SetupRendering();
            long initializationStamp = BeginTiming();
            template = VoxelTemplateCache.Acquire(definition, lazyFineVisuals && !preview, dataOnlyPreview);
            GeometryMs += EndTiming(initializationStamp);
            cells = new OwnedVoxelCells(template.snapshot, this);
            centerOfCells = template.snapshot.centerOfCells;
            AliveCount = template.cellCount; LocalBounds = template.snapshot.bounds;
            if (deferredColliderConstruction) SetDetailedCollisionEnabled(false);
            if (dataOnlyPreview) surfaceRenderer.enabled = false;
            else
            {
                long stamp = BeginTiming();
                if (template.initialLodMesh)
                {
                    mesh = template.initialLodMesh; lazyInitialVisual = true;
                    filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0;
                }
                else
                {
                    chunks = new VoxelTerrainRenderChunks();
                    chunks.Initialize(template.snapshot, asset, materials, transform, template.renderChunks);
                    if (chunks.HasChunks) { filter.sharedMesh = null; surfaceRenderer.enabled = false; }
                    else { mesh = template.mesh; filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0; }
                }
                RefreshRenderMeshes(); GeometryBuildCount++; GeometryMs += EndTiming(stamp);
                stamp = BeginTiming(); BuildColliders(template.boxes); ColliderMs += EndTiming(stamp);
            }
            RenderRevision++;
            SetupBody(); UpdateMass(); enabled = false;
        }

        void SetupRendering()
        {
            filter = GetComponent<MeshFilter>(); if (!filter) filter = gameObject.AddComponent<MeshFilter>();
            surfaceRenderer = GetComponent<MeshRenderer>(); if (!surfaceRenderer) surfaceRenderer = gameObject.AddComponent<MeshRenderer>();
            var hitbox = GetComponent<VoxelBodyHitbox>(); if (!hitbox) hitbox = gameObject.AddComponent<VoxelBodyHitbox>(); hitbox.Initialize(this);
            surfaceRenderer.enabled = true;
            if (asset.UsesHullModels)
            {
                materials = VoxelHullAppearance.Materials(asset, ownedMaterials);
                surfaceRenderer.sharedMaterials = materials;
                surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return;
            }
            int count = Mathf.Max(1, asset.surfaces?.Length ?? 0); materials = new Material[count];
            for (int i = 0; i < count; i++)
            {
                var material = asset.surfaces != null && i < asset.surfaces.Length ? asset.surfaces[i] : null;
                if (!material)
                {
                    if (!fallback)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        fallback = new Material(shader) { name = "Voxel scenery fallback", color = new Color(.58f, .6f, .64f) };
                    }
                    material = fallback;
                }
                materials[i] = material;
            }
            surfaceRenderer.sharedMaterials = materials;
            surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void SetupBody()
        {
            if (previewOnly) return;
            body = GetComponent<Rigidbody>(); if (!body) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = false; body.detectCollisions = true; body.useGravity = false; body.linearDamping = 0; body.angularDamping = 0;
            body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearVelocity = driftVelocity; body.angularVelocity = transform.TransformDirection(angularVelocity * Mathf.Deg2Rad);
        }
        void BuildColliders(IReadOnlyList<Bounds> boxes)
        {
            if (deferredColliderConstruction && !detailedCollisionEnabled && collision.Count == 0)
                collision.RecordDeferred(gameObject, boxes);
            else collision.Synchronize(gameObject, boxes, !previewOnly && detailedCollisionEnabled && AliveCount > 0);
            RefreshBoundsProxy(); ColliderBuildCount++;
        }
        // A map can keep one physical broadphase box while its exact occupied cells remain authoritative for shots.
        public void SetDetailedCollisionEnabled(bool detailed)
        {
            if (previewOnly || !asset) return;
            if (detailed == detailedCollisionEnabled && (detailed || boundsProxy)) return;
            if (!detailed && !boundsProxy)
            {
                boundsProxy = gameObject.AddComponent<BoxCollider>();
                boundsProxy.enabled = false;
            }
            detailedCollisionEnabled = detailed;
            if (detailed)
            {
                collision.Materialize(gameObject, AliveCount > 0);
                if (boundsProxy) boundsProxy.enabled = false;
            }
            else
            {
                RefreshBoundsProxy();
                collision.SetEnabled(false);
            }
        }
        void RefreshBoundsProxy()
        {
            if (!boundsProxy) return;
            boundsProxy.center = LocalBounds.center;
            boundsProxy.size = LocalBounds.size;
            boundsProxy.enabled = !detailedCollisionEnabled && AliveCount > 0;
        }
        // Promotes one intact coarse instance to the shared exact mesh without touching its body, cells or colliders.
        public void EnsureExactVisuals()
        {
            if (!lazyInitialVisual || template == null) return;
            mesh = VoxelTemplateCache.EnsureExactMesh(template);
            filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0;
            lazyInitialVisual = false;
            RefreshRenderMeshes(); RenderRevision++;
        }
        public Matrix4x4 PhysicsMatrix
        {
            get
            {
                if (!body) return transform.localToWorldMatrix;
                // PhysX poses can contain tiny quaternion norm drift between interpolation updates.
                return Matrix4x4.TRS(body.position, body.rotation.normalized, Vector3.one) * Matrix4x4.TRS(transform.position, transform.rotation.normalized, Vector3.one).inverse * transform.localToWorldMatrix;
            }
        }
        public Matrix4x4 CellMatrix(Vector3Int position) => PhysicsMatrix * Matrix4x4.TRS((Vector3)position * asset.cellSize, Quaternion.identity, Vector3.one * asset.cellSize);
        void RefreshRenderMeshes()
        {
            renderMeshes.Clear(); TotalVertices = 0;
            if (chunks != null && chunks.HasChunks)
            {
                foreach (var current in chunks.Meshes) if (current && current.vertexCount > 0) { renderMeshes.Add(current); TotalVertices += current.vertexCount; }
            }
            else if (mesh && mesh.vertexCount > 0) { renderMeshes.Add(mesh); TotalVertices = mesh.vertexCount; }
        }
        void UpdateMass()
        {
            if (!body) return;
            long stamp = BeginTiming();
            var authored = asset.authoredPhysics;
            if (authored != null && authored.IsValid)
            {
                float scale = Mathf.Abs(transform.lossyScale.x);
                float volume = Mathf.Abs(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z);
                body.mass = Mathf.Clamp(authored.mass * volume, .01f, 100000000);
                body.centerOfMass = authored.centerOfMass;
                body.inertiaTensorRotation = authored.inertiaTensorRotation.normalized;
                // Tensor already contains the source mass; retain clamp effects when applying a later instance scale.
                body.inertiaTensor = authored.inertiaTensor * (body.mass / authored.mass) * scale * scale;
            }
            else
            {
                body.mass = Mathf.Clamp(AliveCount * asset.massPerCell * Mathf.Pow(asset.cellSize, 3) * Mathf.Abs(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z), .01f, 100000000);
                body.centerOfMass = centerOfCells; body.ResetInertiaTensor();
            }
            if (AliveCount == 0) body.isKinematic = true;
            if (AliveCount > 0) Starframe.Flight.ShipSceneryCollision.UpdateSceneryMass(body);
            else Starframe.Flight.ShipSceneryCollision.RemoveScenery(body);
            MassMs += EndTiming(stamp);
        }
        long BeginTiming() => CollectDiagnostics ? Stopwatch.GetTimestamp() : 0;
        static double EndTiming(long stamp) => stamp == 0 ? 0 : (Stopwatch.GetTimestamp() - stamp) * (1000d / Stopwatch.Frequency);
        void ResetDiagnostics()
        {
            GeometryBuildCount = ColliderBuildCount = 0;
            GeometryMs = ColliderMs = MassMs = 0;
        }
        public void DisposeResources() => Release();
        void OnDestroy() => Release(false);
        void Release(bool removeColliders = true)
        {
            Starframe.Flight.ShipSceneryCollision.RemoveScenery(body);
            enabled = false;
            collision.Clear(removeColliders ? ReleaseObject : (Action<UnityEngine.Object>)null);
            if (boundsProxy) { boundsProxy.enabled = false; if (removeColliders) ReleaseObject(boundsProxy); boundsProxy = null; }
            chunks?.Dispose(); chunks = null;
            if (filter) filter.sharedMesh = null;
            if (ownsMesh) ReleaseObject(mesh); mesh = null; ownsMesh = false;
            VoxelTemplateCache.Release(template); template = null;
            if (renderMeshes.Count > 0) RenderRevision++;
            renderMeshes.Clear(); TotalVertices = 0;
            cells = EmptyCells;
            AliveCount = 0; centerOfCells = Vector3.zero;
            dataOnlyPreview = lazyInitialVisual = false;
            detailedCollisionEnabled = true; deferredColliderConstruction = false;
            ReleaseObject(fallback); fallback = null;
            foreach (var material in ownedMaterials) ReleaseObject(material); ownedMaterials.Clear(); materials = null;
        }
        void ReleaseObject(UnityEngine.Object value) { if (value) { if (Application.isPlaying && !previewOnly) Destroy(value); else DestroyImmediate(value); } }
    }
}
