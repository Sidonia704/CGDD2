// @role Simulate sparse-damage drifting terrain, with optional coarse startup visuals and deferred exact collision components.
// @calls VoxelAssetDefinition, VoxelGeometry, VoxelBodyHitbox, VoxelTemplateCache, VoxelHullAppearance, VoxelTerrainTopology, VoxelTerrainColliders, VoxelTerrainRenderChunks, VoxelDetachedVisualFragments, ShipSceneryCollision
// @tune healthMultiplier:float:0.01-1000:Instance multiplier for the default cell health.
// @tune driftVelocity:Vector3:-100-100:Initial world-space velocity; collisions subsequently control movement.
// @tune angularVelocity:Vector3:-180-180:Initial local angular velocity in degrees per second.
// @tune tinyFragmentMaxCells:int:0-4:Optional detached render-only fragment limit; zero preserves fully physical fragments.
// @tune tinyFragmentMaxWorldExtent:float:0-2:Maximum world-axis extent eligible for the optional render-only fragment policy.

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
        public bool removed;
        public VoxelStructureRuntime owner;
        public bool Alive => !removed;
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class VoxelStructureRuntime : MonoBehaviour
    {
        static readonly Unity.Profiling.ProfilerMarker FlushMarker = new Unity.Profiling.ProfilerMarker("Starframe.MapDamageFlush");
        public VoxelAssetDefinition asset;
        [Min(.01f)] public float healthMultiplier = 1;
        public Vector3 driftVelocity, angularVelocity;
        [NonSerialized] public string AuthoringId;
        [Range(0, 4)] public int tinyFragmentMaxCells;
        [Range(0, 2)] public float tinyFragmentMaxWorldExtent = 2;
        [NonSerialized] public bool CollectDiagnostics;

        readonly Dictionary<Vector3Int, VoxelCellState> cells = new Dictionary<Vector3Int, VoxelCellState>();
        struct Damage { public float remaining; public double expires; }
        readonly Dictionary<Vector3Int, Damage> damage = new Dictionary<Vector3Int, Damage>();
        readonly List<Vector3Int> expired = new List<Vector3Int>();
        List<Vector3Int> pendingRemoved = new List<Vector3Int>(), processingRemoved = new List<Vector3Int>();
        readonly List<Mesh> renderMeshes = new List<Mesh>();
        readonly VoxelTerrainTopology topology = new VoxelTerrainTopology();
        readonly VoxelTerrainColliders collision = new VoxelTerrainColliders();
        double timeOffset, nextCleanup, nextExpiry = double.PositiveInfinity;
        double Clock => (Application.isPlaying ? Time.timeAsDouble : Time.realtimeSinceStartupAsDouble) + timeOffset;
        VoxelTemplateCache.Template template;
        VoxelTerrainRenderChunks chunks;
        Material[] materials;
        Material fallback;
        readonly List<Material> ownedMaterials = new List<Material>();
        Mesh mesh;
        int sourceGeometryRevision;
        bool ownsMesh, previewOnly, dataOnlyPreview, lazyInitialVisual, dirty, flushing, boundsDirty, fullCollisionRebuild;
        bool deferredColliderConstruction;
        double positionSumX, positionSumY, positionSumZ;
        MeshFilter filter;
        MeshRenderer surfaceRenderer;
        Rigidbody body;
        BoxCollider boundsProxy;
        bool detailedCollisionEnabled = true;

        public IReadOnlyDictionary<Vector3Int, VoxelCellState> Cells => cells;
        public int AliveCount { get; private set; }
        public int ColliderCount => collision.LogicalCount;
        public int DetailedColliderCost => collision.LogicalCount;
        public int MaterializedColliderCount => collision.Count;
        public int PrepareDetailedCollision(int maximumBoxes) => collision.PrepareMaterialization(gameObject, maximumBoxes);
        public bool UsesBoundsCollisionProxy => boundsProxy && boundsProxy.enabled;
        public int RecentDamageCount { get { PruneDamage(); return damage.Count; } }
        public Bounds LocalBounds { get; private set; }
        public Rigidbody Body => body;
        public bool DataOnlyPreview => dataOnlyPreview;
        public bool HasLazyInitialVisual => lazyInitialVisual;
        public int SourceGeometryRevision => sourceGeometryRevision;
        public IReadOnlyList<Mesh> RenderMeshes => renderMeshes;
        public int RenderRevision { get; private set; }
        public int RenderMeshCount => renderMeshes.Count;
        public int TotalVertices { get; private set; }
        public int DetachedVisualCells { get; private set; }
        public int DetachedVisualFragmentCount { get; private set; }
        public bool UsesLightweightDamageVisuals { get; internal set; }
        public int GeometryBuildCount { get; private set; }
        public int ConnectivityPassCount { get; private set; }
        public int ColliderBuildCount { get; private set; }
        public int DamageCleanupCount { get; private set; }
        // Contractual constant, deliberately not a counter: an idle body is disabled (UpdateSchedule) and therefore
        // receives no LateUpdate/FixedUpdate, so no maintenance path may iterate cells. Only a dirty flush touches them.
        public const int ContractualIdleCellVisits = 0;
        public int IdleCellVisits => ContractualIdleCellVisits;
        public double ConnectivityMs { get; private set; }
        public double GeometryMs { get; private set; }
        public double ColliderMs { get; private set; }
        public double MassMs { get; private set; }
        public double LastFlushMilliseconds { get; private set; }
        public event Action<VoxelCellState> CellDestroyed;
        public event Action<VoxelStructureRuntime> FragmentCreated;

        void Awake() { if (asset) Initialize(asset); }
        public void Initialize(VoxelAssetDefinition definition, bool preview = false, bool buildPreviewMesh = true,
            bool lazyFineVisuals = false, bool deferDetailedColliders = false)
        {
            if (!definition) throw new ArgumentException("Missing voxel asset.");
            if (!definition.Validate(out var error)) throw new ArgumentException(error);
            Release(); ResetDiagnostics();
            asset = definition; sourceGeometryRevision = definition.GeometryRevision;
            previewOnly = preview; deferredColliderConstruction = deferDetailedColliders && !preview;
            healthMultiplier = Mathf.Max(.01f, healthMultiplier);
            dataOnlyPreview = preview && !buildPreviewMesh;
            SetupRendering();
            foreach (var cell in definition.cells)
            {
                cells.Add(cell.position, new VoxelCellState { position = cell.position, surface = cell.surface, shape = cell.shape, quarterTurns = cell.quarterTurns, modelOffset = cell.modelOffset, owner = this });
                AddCellPosition(cell.position, 1);
            }
            AliveCount = cells.Count; LocalBounds = definition.LocalBounds;
            if (deferredColliderConstruction) SetDetailedCollisionEnabled(false);
            if (dataOnlyPreview) surfaceRenderer.enabled = false;
            else
            {
                long stamp = BeginTiming();
                template = VoxelTemplateCache.Acquire(definition, lazyFineVisuals && !preview);
                GeometryMs += EndTiming(stamp);
                // The certification was made once from this template generation's authored cell copy. Seeding is
                // valid only while this instance owns exactly that cell count/revision; an unfitting copy falls back
                // to a local Reset. Fragments and subsets never call Initialize and keep MarkConnected/Resolve.
                stamp = BeginTiming();
                if (template != null && template.cellCount == cells.Count && template.revision == definition.GeometryRevision)
                    topology.Seed(template.initialConnected);
                else { topology.Reset(cells); ConnectivityPassCount++; }
                ConnectivityMs += EndTiming(stamp);
                stamp = BeginTiming();
                if (template.initialLodMesh)
                {
                    mesh = template.initialLodMesh; lazyInitialVisual = true;
                    filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0;
                }
                else
                {
                    chunks = new VoxelTerrainRenderChunks();
                    chunks.Initialize(cells, asset, materials, transform, template.renderChunks);
                    if (chunks.HasChunks) { filter.sharedMesh = null; surfaceRenderer.enabled = false; }
                    else { mesh = template.mesh; filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0; }
                }
                RefreshRenderMeshes(); GeometryBuildCount++; GeometryMs += EndTiming(stamp);
                stamp = BeginTiming(); BuildColliders(template.boxes); ColliderMs += EndTiming(stamp);
            }
            RenderRevision++;
            SetupBody(); UpdateMass(); UpdateSchedule();
        }

        void SetupRendering(VoxelStructureRuntime paletteSource = null)
        {
            filter = GetComponent<MeshFilter>(); if (!filter) filter = gameObject.AddComponent<MeshFilter>();
            surfaceRenderer = GetComponent<MeshRenderer>(); if (!surfaceRenderer) surfaceRenderer = gameObject.AddComponent<MeshRenderer>();
            var hitbox = GetComponent<VoxelBodyHitbox>(); if (!hitbox) hitbox = gameObject.AddComponent<VoxelBodyHitbox>(); hitbox.Initialize(this);
            surfaceRenderer.enabled = true;
            if (paletteSource != null)
            {
                // A live body can outlast an authored asset edit. Its fragments must keep that body's
                // material generation, with independent ownership of any runtime-created materials.
                materials = new Material[paletteSource.materials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    var source = paletteSource.materials[i];
                    if (source && (source == paletteSource.fallback || paletteSource.ownedMaterials.Contains(source)))
                    {
                        var copy = new Material(source) { name = source.name + " fragment", hideFlags = HideFlags.HideAndDontSave };
                        ownedMaterials.Add(copy); materials[i] = copy;
                    }
                    else materials[i] = source;
                }
                surfaceRenderer.sharedMaterials = materials;
                surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return;
            }
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
        public void PrewarmDamageVisuals()
        {
            if (!UsesLightweightDamageVisuals) return;
            bool borrowedCoarse = lazyInitialVisual;
            PrepareLazyDamageVisuals();
            lazyInitialVisual = borrowedCoarse;
            chunks?.PrewarmBindings(cells);
        }
        void PrepareLazyDamageVisuals()
        {
            if (template == null || template.snapshot == null || chunks != null) return;
            var warm = UsesLightweightDamageVisuals ? VoxelTemplateCache.EnsureLightweightRenderChunks(template) : VoxelTemplateCache.EnsureRenderChunks(template);
            if (warm != null)
            {
                var prepared = new VoxelTerrainRenderChunks();
                try { prepared.Initialize(cells, asset, materials, transform, warm); }
                catch { prepared.Dispose(); throw; }
                chunks = prepared;
            }
            lazyInitialVisual = false;
        }

        public float ApplyDamage(Vector3Int position, float amount)
        {
            if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 || !cells.TryGetValue(position, out var cell) || !cell.Alive) return 0;
            float health = GetHealth(position), dealt = Mathf.Min(health, amount);
            health -= dealt;
            if (health <= 0)
            {
                cell.removed = true; damage.Remove(position); AliveCount--; AddCellPosition(position, -1);
                pendingRemoved.Add(position); dirty = true;
                Vector3 center = (Vector3)position * asset.cellSize;
                float half = asset.cellSize * .5f, epsilon = asset.cellSize * .0001f;
                if (center.x - half <= LocalBounds.min.x + epsilon || center.x + half >= LocalBounds.max.x - epsilon ||
                    center.y - half <= LocalBounds.min.y + epsilon || center.y + half >= LocalBounds.max.y - epsilon ||
                    center.z - half <= LocalBounds.min.z + epsilon || center.z + half >= LocalBounds.max.z - epsilon) boundsDirty = true;
                UpdateSchedule(); CellDestroyed?.Invoke(cell);
            }
            else
            {
                double expires = Clock + Mathf.Max(.1f, asset.damageResetSeconds);
                damage[position] = new Damage { remaining = health, expires = expires };
                nextExpiry = Math.Min(nextExpiry, expires); UpdateSchedule();
            }
            return dealt;
        }
        public float GetHealth(Vector3Int position)
        {
            if (!cells.TryGetValue(position, out var cell) || !cell.Alive) return 0;
            if (damage.TryGetValue(position, out var value))
            {
                if (Clock < value.expires) return value.remaining;
                damage.Remove(position); UpdateSchedule();
            }
            return Mathf.Max(1, asset.blockHealth * healthMultiplier);
        }
        public void AdvanceDamageTime(double seconds) { if (seconds > 0) timeOffset += seconds; }
        void PruneDamage()
        {
            if (damage.Count == 0) { nextExpiry = double.PositiveInfinity; return; }
            double now = Clock;
            if (now < nextExpiry || now < nextCleanup) return;
            nextCleanup = now + .5; nextExpiry = double.PositiveInfinity; expired.Clear(); DamageCleanupCount++;
            foreach (var pair in damage)
            {
                if (now >= pair.Value.expires) expired.Add(pair.Key);
                else nextExpiry = Math.Min(nextExpiry, pair.Value.expires);
            }
            foreach (var key in expired) damage.Remove(key);
            UpdateSchedule();
        }

        public void FlushMeshChanges()
        {
            if (!dirty || flushing) return;
            using var sample = FlushMarker.Auto();
            long flushStamp = BeginTiming();
            dirty = false; flushing = true;
            var swap = processingRemoved; processingRemoved = pendingRemoved; pendingRemoved = swap; pendingRemoved.Clear();
            bool committed = false;
            List<VoxelStructureRuntime> created = null;
            try
            {
                if (dataOnlyPreview)
                {
                    LocalBounds = VoxelGeometry.Bounds(cells, asset.cellSize); boundsDirty = false; RenderRevision++;
                    committed = true;
                    return;
                }
                PrepareLazyDamageVisuals();
                lazyInitialVisual = false;
                long stamp = BeginTiming();
                bool connectedBeforeDeletion = topology.KnownConnected;
                var groups = topology.Resolve(cells, processingRemoved); ConnectivityPassCount++;
                ConnectivityMs += EndTiming(stamp);
                var velocity = body ? body.linearVelocity : Vector3.zero; var spin = body ? body.angularVelocity : Vector3.zero;
                var oldCenter = body ? body.worldCenterOfMass : transform.TransformPoint(CenterOfCells());
                created = new List<VoxelStructureRuntime>();
                bool split = !previewOnly && groups != null && groups.Count > 1;
                if (split)
                {
                    for (int i = 1; i < groups.Count; i++)
                    {
                        var group = groups[i];
                        // Only a group that was connected to this body before this deletion may become render-only;
                        // authored islands of an initially disconnected asset stay physical.
                        if (connectedBeforeDeletion && TryCreateVisualFragment(group, velocity, spin, oldCenter, i)) continue;
                        created.Add(CreatePhysicalFragment(group, velocity, spin, oldCenter, i));
                    }
                    boundsDirty = true; topology.MarkConnected();
                }
                List<Bounds> boxes = null;
                if (!split && !fullCollisionRebuild && collision.TryRemoveCells(processingRemoved, asset.cellSize, out var incrementalBoxes)) boxes = incrementalBoxes;
                chunks?.Invalidate(processingRemoved);
                RebuildGeometry(boxes); fullCollisionRebuild = false;
                UpdateMass();
                if (body && !body.isKinematic)
                {
                    // Keep the retained body's centre-of-mass point velocity continuous when the split moved its COM.
                    body.linearVelocity = velocity + Vector3.Cross(spin, body.worldCenterOfMass - oldCenter);
                    body.angularVelocity = spin;
                }
                committed = true;
            }
            catch
            {
                // A failed pass keeps its removals for a retry and forces the retry to rebuild all collision from cells,
                // so a failed flush is never left looking clean and can never leave a destroyed cell rendered/collided.
                if (processingRemoved.Count > 0) { dirty = true; fullCollisionRebuild = true; boundsDirty = true; }
                throw;
            }
            finally
            {
                if (committed) processingRemoved.Clear();
                else if (processingRemoved.Count > 0) { pendingRemoved.AddRange(processingRemoved); processingRemoved.Clear(); }
                flushing = false; LastFlushMilliseconds = EndTiming(flushStamp); UpdateSchedule();
            }
            if (created != null) foreach (var fragment in created) FragmentCreated?.Invoke(fragment);
        }

        VoxelStructureRuntime CreatePhysicalFragment(List<VoxelCellState> group, Vector3 velocity, Vector3 spin, Vector3 oldCenter, int index)
        {
            var root = new GameObject(name + " fragment");
            VoxelStructureRuntime fragment = null;
            bool committed = false;
            try
            {
                root.layer = gameObject.layer; root.transform.SetParent(transform.parent, false);
                if (root.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                root.transform.SetPositionAndRotation(body ? body.position : transform.position, body ? body.rotation : transform.rotation); root.transform.localScale = transform.localScale;
                if (!Application.isPlaying) root.hideFlags = HideFlags.DontSaveInEditor;
                fragment = root.AddComponent<VoxelStructureRuntime>();
                fragment.asset = asset; fragment.healthMultiplier = healthMultiplier; fragment.AuthoringId = AuthoringId;
                fragment.sourceGeometryRevision = sourceGeometryRevision;
                fragment.deferredColliderConstruction = deferredColliderConstruction;
                fragment.UsesLightweightDamageVisuals = UsesLightweightDamageVisuals;
                fragment.tinyFragmentMaxCells = tinyFragmentMaxCells; fragment.tinyFragmentMaxWorldExtent = tinyFragmentMaxWorldExtent;
                fragment.CollectDiagnostics = CollectDiagnostics; fragment.timeOffset = timeOffset; fragment.SetupRendering(this);
                // Stage the island inside the new runtime while the donor still owns it: ownership, damage and the
                // donor's position sums only move after the island has working chunks, colliders, body and mesh.
                foreach (var state in group) { fragment.cells.Add(state.position, state); fragment.AddCellPosition(state.position, 1); }
                fragment.AliveCount = group.Count; fragment.boundsDirty = true; fragment.topology.MarkConnected();
                if (fragment.deferredColliderConstruction)
                {
                    fragment.LocalBounds = VoxelGeometry.Bounds(fragment.cells, asset.cellSize);
                    fragment.boundsDirty = false;
                    fragment.SetDetailedCollisionEnabled(false);
                }
                if (chunks != null)
                {
                    fragment.chunks = chunks.ForkForIsland(fragment.cells, fragment.materials, root.transform);
                    if (fragment.chunks != null && template != null) fragment.template = VoxelTemplateCache.Retain(template);
                }
                fragment.RebuildGeometry(null); fragment.SetupBody(); fragment.UpdateMass();
                var center = fragment.body ? fragment.body.worldCenterOfMass : fragment.transform.TransformPoint(fragment.CenterOfCells());
                if (fragment.body)
                {
                    fragment.body.linearVelocity = velocity + Vector3.Cross(spin, center - oldCenter) + SeparationDirection(center, oldCenter, index) * asset.fragmentSeparationSpeed;
                    fragment.body.angularVelocity = spin;
                }
                foreach (var state in group)
                {
                    state.owner = fragment; cells.Remove(state.position); AddCellPosition(state.position, -1);
                    if (damage.TryGetValue(state.position, out var hit))
                    {
                        fragment.damage.Add(state.position, hit); fragment.nextExpiry = Math.Min(fragment.nextExpiry, hit.expires); damage.Remove(state.position);
                    }
                }
                AliveCount -= group.Count;
                fragment.UpdateSchedule();
                committed = true;
                var map = GetComponentInParent<SpaceMapRuntime>(); if (map) map.RegisterFragment(fragment);
                return fragment;
            }
            finally
            {
                if (!committed)
                {
                    // Drop the staged island without touching donor ownership; this releases chunk/template leases,
                    // materials and colliders, and hides it before the deferred PlayMode destroy.
                    if (fragment) fragment.DisposeResources();
                    root.SetActive(false); ReleaseObject(root);
                }
            }
        }

        bool TryCreateVisualFragment(List<VoxelCellState> group, Vector3 velocity, Vector3 spin, Vector3 oldCenter, int index)
        {
            int limit = Mathf.Clamp(tinyFragmentMaxCells, 0, 4);
            if (limit == 0 || group.Count == 0 || group.Count > limit) return false;
            var subset = new Dictionary<Vector3Int, VoxelCellState>(); Vector3 pivot = Vector3.zero;
            foreach (var state in group) { subset.Add(state.position, state); pivot += (Vector3)state.position * asset.cellSize; }
            var localBounds = VoxelGeometry.Bounds(subset, asset.cellSize);
            var matrix = PhysicsMatrix; Vector3 worldMin = Vector3.zero, worldMax = Vector3.zero; bool first = true;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? localBounds.min.x : localBounds.max.x, (i & 2) == 0 ? localBounds.min.y : localBounds.max.y, (i & 4) == 0 ? localBounds.min.z : localBounds.max.z);
                corner = matrix.MultiplyPoint3x4(corner);
                if (first) { worldMin = worldMax = corner; first = false; } else { worldMin = Vector3.Min(worldMin, corner); worldMax = Vector3.Max(worldMax, corner); }
            }
            Vector3 extent = worldMax - worldMin; float maximum = Mathf.Clamp(tinyFragmentMaxWorldExtent, 0, 2);
            if (Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)) > maximum + .00001f) return false;
            pivot /= group.Count; Vector3 center = matrix.MultiplyPoint3x4(pivot);
            var ownedMesh = VoxelGeometry.Build(subset, materials.Length, asset.cellSize, asset.hullModules);
            try
            {
                VoxelDetachedVisualFragments.GetOrCreateFor(this).AddOwnedMesh(ownedMesh, materials, matrix, pivot,
                    velocity + Vector3.Cross(spin, center - oldCenter) + SeparationDirection(center, oldCenter, index) * asset.fragmentSeparationSpeed,
                    spin, group.Count, gameObject.layer);
            }
            catch { ReleaseObject(ownedMesh); throw; }
            if (chunks != null)
            {
                var moved = new List<Vector3Int>(group.Count); foreach (var state in group) moved.Add(state.position); chunks.Invalidate(moved);
            }
            foreach (var state in group)
            {
                cells.Remove(state.position); damage.Remove(state.position); AddCellPosition(state.position, -1);
                state.removed = true; state.owner = null;
            }
            AliveCount -= group.Count; DetachedVisualCells += group.Count; DetachedVisualFragmentCount++;
            return true;
        }
        static Vector3 SeparationDirection(Vector3 center, Vector3 oldCenter, int index)
        {
            Vector3 away = center - oldCenter;
            return away.sqrMagnitude > .0001f ? away.normalized : Quaternion.Euler(index * 73, index * 127, 0) * Vector3.right;
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
        void RebuildGeometry(IReadOnlyList<Bounds> boxes)
        {
            long stamp = BeginTiming();
            chunks?.RebuildDirty(cells);
            if (chunks != null && chunks.HasChunks)
            {
                filter.sharedMesh = null; surfaceRenderer.enabled = false;
            }
            else
            {
                var rebuilt = VoxelGeometry.Build(cells, materials.Length, asset.cellSize, asset.hullModules);
                if (ownsMesh) ReleaseObject(mesh);
                mesh = rebuilt; ownsMesh = true; filter.sharedMesh = mesh; surfaceRenderer.enabled = AliveCount > 0;
                VoxelTemplateCache.Release(template); template = null;
            }
            if (boundsDirty) { LocalBounds = VoxelGeometry.Bounds(cells, asset.cellSize); boundsDirty = false; }
            RefreshBoundsProxy();
            RefreshRenderMeshes(); RenderRevision++; GeometryBuildCount++; GeometryMs += EndTiming(stamp);
            stamp = BeginTiming();
            BuildColliders(boxes ?? VoxelGeometry.CollisionBoxes(cells, asset.cellSize)); ColliderMs += EndTiming(stamp);
        }
        void RefreshRenderMeshes()
        {
            renderMeshes.Clear(); TotalVertices = 0;
            if (chunks != null && chunks.HasChunks)
            {
                foreach (var current in chunks.Meshes) if (current && current.vertexCount > 0) { renderMeshes.Add(current); TotalVertices += current.vertexCount; }
            }
            else if (mesh && mesh.vertexCount > 0) { renderMeshes.Add(mesh); TotalVertices = mesh.vertexCount; }
        }
        void AddCellPosition(Vector3Int position, int sign)
        {
            double scale = (double)asset.cellSize * sign;
            positionSumX += position.x * scale; positionSumY += position.y * scale; positionSumZ += position.z * scale;
        }
        Vector3 CenterOfCells() => AliveCount > 0
            ? new Vector3((float)(positionSumX / AliveCount), (float)(positionSumY / AliveCount), (float)(positionSumZ / AliveCount)) : Vector3.zero;
        void UpdateMass()
        {
            if (!body) return;
            long stamp = BeginTiming();
            body.mass = Mathf.Clamp(AliveCount * asset.massPerCell * Mathf.Pow(asset.cellSize, 3) * Mathf.Abs(transform.lossyScale.x * transform.lossyScale.y * transform.lossyScale.z), .01f, 100000000);
            body.centerOfMass = CenterOfCells(); body.ResetInertiaTensor(); if (AliveCount == 0) body.isKinematic = true;
            if (AliveCount > 0) Starframe.Flight.ShipSceneryCollision.UpdateSceneryMass(body);
            else Starframe.Flight.ShipSceneryCollision.RemoveScenery(body);
            MassMs += EndTiming(stamp);
        }
        void UpdateSchedule()
        {
            if (damage.Count == 0) nextExpiry = double.PositiveInfinity;
            enabled = dirty || damage.Count > 0;
        }
        long BeginTiming() => CollectDiagnostics ? Stopwatch.GetTimestamp() : 0;
        static double EndTiming(long stamp) => stamp == 0 ? 0 : (Stopwatch.GetTimestamp() - stamp) * (1000d / Stopwatch.Frequency);
        void ResetDiagnostics()
        {
            GeometryBuildCount = ConnectivityPassCount = ColliderBuildCount = DamageCleanupCount = 0;
            ConnectivityMs = GeometryMs = ColliderMs = MassMs = LastFlushMilliseconds = 0;
        }
        void LateUpdate() { FlushMeshChanges(); PruneDamage(); }
        void FixedUpdate() => FlushMeshChanges();
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
            cells.Clear(); damage.Clear(); expired.Clear(); pendingRemoved.Clear(); processingRemoved.Clear();
            AliveCount = DetachedVisualCells = DetachedVisualFragmentCount = 0; positionSumX = positionSumY = positionSumZ = 0;
            dirty = dataOnlyPreview = lazyInitialVisual = boundsDirty = flushing = fullCollisionRebuild = false;
            detailedCollisionEnabled = true; deferredColliderConstruction = false;
            nextCleanup = timeOffset = 0; nextExpiry = double.PositiveInfinity;
            ReleaseObject(fallback); fallback = null;
            foreach (var material in ownedMaterials) ReleaseObject(material); ownedMaterials.Clear(); materials = null;
        }
        void ReleaseObject(UnityEngine.Object value) { if (value) { if (Application.isPlaying && !previewOnly) Destroy(value); else DestroyImmediate(value); } }
    }
}
