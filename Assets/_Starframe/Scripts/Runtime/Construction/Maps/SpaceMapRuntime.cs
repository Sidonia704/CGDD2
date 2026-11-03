// @role Load staged floating scenery with destruction off by default, budgeted visual LOD and exact collision preparation.
// @calls SpaceMapDefinition, VoxelStructureRuntime, VoxelDetachedVisualFragments, SpaceMapVisuals, SpaceMapAssemblyLinks, SpaceMapPhysics
// @tune stageBuildOnStart:bool:0-1:Opt into staged startup when the caller waits for BuildIsComplete before gameplay.
// @tune instancesPerFrame:int:1-1000:Maximum scenery bodies created in a staged startup frame.
// @tune buildMillisecondsPerFrame:float:0-100:Soft CPU budget for staged startup; one body always finishes atomically.
// @tune lazyFineVisuals:bool:0-1:Use signed borrowed coarse meshes at startup and generate exact meshes near the camera or after damage.
// @tune visualLodDistance:float:0-100000:Camera distance where an authored coarse visual replaces an intact body's exact renderer.
// @tune visualCullDistance:float:0-100000:Camera distance beyond which only scenery renderers are hidden; zero disables this cull.
// @tune visualRefreshFrames:int:1-120:Frames between map-level visibility checks.
// @tune maximumFineVisualCells:int:0-131072:Keep larger intact bodies on their exterior cube mesh; zero permits all exact hull upgrades.
// @tune keepCoarseIntactVisuals:bool:0-1:With lazyFineVisuals, retain authored coarse meshes on intact scenery at every distance.
// @tune assemblyInitialMaxDepenetrationVelocity:float:0-100:Optional temporary cap for the first physics step after linked map loading; zero preserves old behaviour.
// @tune useBudgetedPhysics:bool:0-1:Use one moving bounds collider per scenery body and add exact colliders near the camera within a global budget.
// @tune detailedCollisionDistance:float:0-100000:Maximum camera distance from an asset's bounds for detailed physics selection.
// @tune activeColliderBudget:int:1-1000000:Maximum enabled scenery colliders, with one mandatory bounds proxy per live body.
// @tune physicsRefreshFrames:int:1-120:Frames between map physics distance and budget checks.
// @tune colliderPreparationPerFrame:int:1-256:Maximum cold exact collider components prepared per frame beneath a live proxy.
// @tune colliderPreparationMilliseconds:float:0-10:Soft collider preparation CPU budget; zero disables the time limit.
// @tune lightweightDamageThreshold:int:0-131072:For lazy budgeted scenery at least this large retain primitive exterior cells after damage; zero disables.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Starframe.Maps
{
    public sealed class SpaceMapRuntime : MonoBehaviour
    {
        public SpaceMapDefinition definition;
        [Tooltip("Enable only for destruction development. Gameplay scenery is indestructible by default.")]
        public bool allowDestruction;
        public bool stageBuildOnStart;
        [Min(1)] public int instancesPerFrame = 2;
        [Min(0)] public float buildMillisecondsPerFrame = 5;
        public bool lazyFineVisuals;
        public Camera visualCamera;
        [Min(0)] public float visualLodDistance = 800;
        [Min(0)] public float visualCullDistance;
        [Min(1)] public int visualRefreshFrames = 4;
        [Min(0)] public int maximumFineVisualCells;
        public bool keepCoarseIntactVisuals;
        [Min(0)] public float assemblyInitialMaxDepenetrationVelocity;
        public bool useBudgetedPhysics;
        [Min(0)] public float detailedCollisionDistance = 300;
        [Min(1)] public int activeColliderBudget = 8000;
        [Min(1)] public int physicsRefreshFrames = 12;
        [Range(1,256)] public int colliderPreparationPerFrame = 24;
        [Range(0,10)] public float colliderPreparationMilliseconds = 1.5f;
        [Min(0)] public int lightweightDamageThreshold = 128;
        public List<SpaceMapVisualLodEntry> visualLods = new List<SpaceMapVisualLodEntry>();
        readonly List<VoxelStructureRuntime> structures = new List<VoxelStructureRuntime>();
        SpaceMapVisuals visuals;
        SpaceMapAssemblyLinks assembly;
        SpaceMapPhysics physicsBudget;
        VoxelMapInstance[] pending;
        int nextInstance, frameLimit = 2;
        bool built;
        public IReadOnlyList<VoxelStructureRuntime> Structures => structures;
        public bool BuildIsPending => pending != null;
        public bool BuildIsComplete => built && pending == null;
        public string BuildError { get; private set; }
        public int BuiltInstanceCount => nextInstance;
        public int BuildTotalInstanceCount => pending?.Length ?? (built ? nextInstance : 0);
        public int ActiveAssemblyContactCount => assembly?.ActiveContactCount ?? 0;
        public int ActiveMapColliderCount => physicsBudget?.ActiveColliderCount ?? 0;
        internal void RegisterFragment(VoxelStructureRuntime fragment)
        {
            physicsBudget?.Register(fragment);
            structures.Add(fragment);
            assembly?.Observe(fragment);
            if (pending != null) assembly?.Freeze(fragment);
            assembly?.Refresh();
        }
        public static long ComputeGeometrySignature(VoxelAssetDefinition asset) => SpaceMapVisuals.ComputeGeometrySignature(asset);
        void Start()
        {
            if (!definition || built || pending != null || structures.Count > 0) return;
            try { if (stageBuildOnStart) BeginBuild(instancesPerFrame); else Build(); }
            catch (Exception error) { BuildError=error.Message; throw; }
        }
        void Update()
        {
            if (pending != null && Application.isPlaying) StepBuild(frameLimit, buildMillisecondsPerFrame);
        }
        void FixedUpdate() => assembly?.AdvanceFixedStep();
        void OnDestroy() { physicsBudget?.Dispose(); physicsBudget = null; }
        void LateUpdate()
        {
            assembly?.Tick();
            if (pending != null) return;
            if (physicsBudget != null && Time.frameCount % Mathf.Max(1, physicsRefreshFrames) == 0)
                RefreshPhysicsNow(visualCamera ? visualCamera : Camera.main, true);
            physicsBudget?.StepPreparation(colliderPreparationPerFrame, colliderPreparationMilliseconds);
            if (!lazyFineVisuals && visualCullDistance <= 0 &&
                (visualLodDistance <= 0 || visualLods == null || visualLods.Count == 0))
            {
                if (visuals != null) { visuals.Clear(); visuals = null; }
                return;
            }
            if (structures.Count == 0 || Time.frameCount % Mathf.Max(1, visualRefreshFrames) != 0) return;
            RefreshVisualsNow(visualCamera ? visualCamera : Camera.main);
        }
        public void Build(bool preview = false)
        {
            ValidateDefinition();
            Clear();
            assembly = new SpaceMapAssemblyLinks();
            if (!preview && useBudgetedPhysics) physicsBudget = new SpaceMapPhysics();
            try
            {
                foreach (var item in definition.instances) BuildOne(item, preview);
                if (!preview) FinalizePhysics();
                nextInstance = definition.instances.Count;
                built = true;
            }
            catch (Exception error) { Clear(); BuildError=error.Message; throw; }
        }
        // The caller should withhold gameplay until BuildIsComplete. Existing Build remains synchronous.
        public void BeginBuild(int maximumInstancesPerFrame = 2)
        {
            ValidateDefinition();
            Clear();
            assembly = new SpaceMapAssemblyLinks();
            if (useBudgetedPhysics) physicsBudget = new SpaceMapPhysics();
            frameLimit = Mathf.Max(1, maximumInstancesPerFrame);
            pending = definition.instances.ToArray();
            if (pending.Length == 0) { FinalizePhysics(); pending = null; built = true; }
        }
        // Public stepping permits deterministic loading checks outside Play mode.
        public int StepBuild(int maximumInstances, float milliseconds = 0)
        {
            if (pending == null || maximumInstances <= 0) return 0;
            var clock = milliseconds > 0 ? Stopwatch.StartNew() : null;
            int created = 0;
            try
            {
                while (pending != null && created < maximumInstances)
                {
                    BuildOne(pending[nextInstance], false);
                    nextInstance++; created++;
                    if (nextInstance == pending.Length)
                    { FinalizePhysics(); pending = null; built = true; break; }
                    if (clock != null && clock.Elapsed.TotalMilliseconds >= milliseconds) break;
                }
            }
            catch (Exception error) { Clear(); BuildError=error.Message; throw; }
            return created;
        }
        public void CancelBuild()
        {
            if (pending != null) Clear();
        }
        public void RefreshVisualsNow(Camera camera)
        {
            if (visuals == null) visuals = new SpaceMapVisuals();
            visuals.Refresh(structures, visualLods, camera, visualLodDistance, visualCullDistance,
                maximumFineVisualCells, keepCoarseIntactVisuals);
        }
        public void RefreshPhysicsNow(Camera camera, bool staged = false)
        {
            physicsBudget?.Refresh(structures, camera, detailedCollisionDistance, activeColliderBudget, staged);
        }
        public void RefreshAssemblyContactsNow() { assembly?.Refresh(); assembly?.Tick(); }
        public void Clear()
        {
            pending = null; built = false; nextInstance = 0;
            BuildError=null;
            visuals?.Clear();
            assembly?.Clear(); assembly = null;
            var detached = GetComponent<VoxelDetachedVisualFragments>();
            if (detached) detached.Clear();
            foreach (var structure in structures) if (structure) { structure.gameObject.SetActive(false); if (Application.isPlaying) Destroy(structure.gameObject); else DestroyImmediate(structure.gameObject); }
            structures.Clear();
            physicsBudget?.Dispose(); physicsBudget = null;
        }
        void ValidateDefinition()
        {
            if (!definition) throw new ArgumentException("Missing map definition.");
            if (!definition.Validate(out var error)) throw new ArgumentException(error);
        }
        void FinalizePhysics()
        {
            if (assembly == null) return;
            if (definition.assemblyLinks != null && definition.assemblyLinks.Count > 0)
                assembly.Install(definition, structures, transform);
            assembly.ReleaseFreeze(assemblyInitialMaxDepenetrationVelocity);
            RefreshPhysicsNow(visualCamera ? visualCamera : Camera.main);
        }
        void BuildOne(VoxelMapInstance item, bool preview)
        {
            var root = new GameObject(item.label);
            try
            {
                if (physicsBudget != null) root.layer = physicsBudget.Layer;
                root.transform.SetParent(transform, false);
                root.transform.localPosition = item.position;
                root.transform.localRotation = Quaternion.Euler(item.eulerAngles);
                root.transform.localScale = Vector3.one * item.scale;
                if (!Application.isPlaying) root.hideFlags = HideFlags.DontSaveInEditor;
                var structure = root.AddComponent<VoxelStructureRuntime>();
                structure.damageEnabled = preview || allowDestruction;
                structure.UsesLightweightDamageVisuals = physicsBudget != null && lazyFineVisuals && keepCoarseIntactVisuals && lightweightDamageThreshold > 0 && item.asset.cells.Count >= lightweightDamageThreshold;
                structure.healthMultiplier = item.healthMultiplier;
                structure.AuthoringId = item.id;
                structure.driftVelocity = item.driftVelocity;
                structure.angularVelocity = item.angularVelocity;
                structure.Initialize(item.asset, preview, true, lazyFineVisuals, physicsBudget != null);
                if (!preview) structure.PrewarmDamageVisuals();
                if (!preview) physicsBudget?.Register(structure);
                if (!preview) assembly?.Freeze(structure);
                structures.Add(structure);
            }
            catch
            {
                root.SetActive(false);
                if (Application.isPlaying) Destroy(root); else DestroyImmediate(root);
                throw;
            }
        }
    }
}
