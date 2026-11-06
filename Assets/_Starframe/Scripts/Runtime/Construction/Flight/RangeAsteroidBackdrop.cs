// @role Draw a fixed, decorative continuation of the range's asteroid belt using bounded, camera-culled mesh instances and no physics.
// @tune backdropEnabled:bool:0-1:Show or hide the decorative belts without changing their deterministic positions.
// @tune seed:int:0-2147483647:Stable placement seed for all three world-anchored belts.
// @tune farFadeStart:float:0-50000:Camera distance where horizon rocks begin blending toward the distant atmosphere.
// @tune farFadeEnd:float:0-50000:Camera distance where horizon rocks reach their distant atmosphere colour.
// @tune maximumVisibleTriangles:int:1-20000000:Diagnostic triangle target; reaching it never removes permanent decorative rocks.
// @tune densityMultiplier:int:1-8:Legacy procedural-rock density only; native uniform belts use fixed stratified world slots.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Flight
{
    /// <summary>Visual scenery only. The generated matrices are never parented to the player or moved after creation.</summary>
    [DisallowMultipleComponent]
    public sealed class RangeAsteroidBackdrop : MonoBehaviour
    {
        const int HalfLength = 25000;
        const int SegmentLength = 2000;
        const int MaximumRocks = 16000;
        const int UniformGridSide = 9;
        const int MaximumDrawBatches = 180;
        const float DetailedLodDistance = 7000f;
        const int RegionCount = 9;
        const int SizeFamilyCount = 3;
        const int QueueCount = RegionCount * SizeFamilyCount;
        const int MaximumInstancesPerGroup = 1023;
        // Object-to-world plus world-to-object instance data can limit URP draws to 511.
        const int MaximumInstancesPerDraw = 511;
        const float BeltHeight = -1330f;
        const float MapLongitudinalEdge = 1754f;
        const float MapClearance = 35f;
        const float VoxelRockBoundRadius = 1.75f;

        [SerializeField] Camera targetCamera;
        [SerializeField] Mesh detailedRockA, detailedRockB, distantRockA, distantRockB;
        [SerializeField] Material rockMaterial;
        [SerializeField] Mesh[] uniformRockVariants = Array.Empty<Mesh>();
        [SerializeField] Mesh[] uniformRockMidLods = Array.Empty<Mesh>();
        [SerializeField] Mesh[] uniformRockFarLods = Array.Empty<Mesh>();
        [SerializeField] float[] uniformRockDiameters = Array.Empty<float>();
        [SerializeField] int seed = 261107;
        [SerializeField, Min(0)] float nearFadeStart = 80f;
        [SerializeField, Min(0)] float nearFadeEnd = 350f;
        [SerializeField, Min(0)] float farFadeStart = 17000f;
        [SerializeField, Min(0)] float farFadeEnd = 25000f;
        [SerializeField, Min(1)] int maximumVisibleTriangles = 4000000;
        [SerializeField, Range(1,8)] int densityMultiplier = 1;
        public bool backdropEnabled = true;

        sealed class Batch
        {
            public Matrix4x4[] matrices;
            public float[] radii;
            public bool[] selected;
            public int[] lastLods;
            public Bounds bounds;
            public int index;
            public int shape;
            public int band;
            public int segmentStart;
            public RenderGroup[] renderGroups;
        }

        // Draws merge compatible instances across longitudinal segments. This leaves enough
        // draw slots for every native shape, belt and distance LOD (16 * 3 * 3 = 144).
        sealed class RenderGroup
        {
            public Mesh mesh;
            public int shape;
            public int band;
            public int lod;
            public int triangles;
            public Matrix4x4[] submittedMatrices = new Matrix4x4[MaximumInstancesPerGroup];
            public int[] submittedSourceIndices = new int[MaximumInstancesPerGroup];
            public int[] submittedInstanceIndices = new int[MaximumInstancesPerGroup];
            public int submittedCount;
            public Bounds submittedBounds;
            public int sourceCapacity;

            public void ReserveSource(int count)
            {
                sourceCapacity += count;
                if (sourceCapacity <= submittedMatrices.Length) return;
                int capacity = Mathf.NextPowerOfTwo(sourceCapacity);
                Array.Resize(ref submittedMatrices, capacity);
                Array.Resize(ref submittedSourceIndices, capacity);
                Array.Resize(ref submittedInstanceIndices, capacity);
            }
        }

        readonly List<Batch> batches = new List<Batch>(1200);
        readonly List<RenderGroup> renderGroups = new List<RenderGroup>(144);
        readonly Dictionary<int, RenderGroup> groupLookup = new Dictionary<int, RenderGroup>(144);
        readonly int[] visibleByRegion = new int[RegionCount];
        readonly int[] visibleByRegionAndSize = new int[QueueCount];
        readonly int[] visibleByLod = new int[3];
        readonly IReadOnlyList<int> visibleRegionView;
        readonly IReadOnlyList<int> visibleSizeView;
        readonly IReadOnlyList<int> visibleLodView;
        readonly Dictionary<Mesh, int> triangleCounts = new Dictionary<Mesh, int>();
        readonly Plane[] frustum = new Plane[6];
        Material instanceMaterial;
        MaterialPropertyBlock properties;
        RenderParams parameters;
        bool built;
        bool useUniformRocks;
        bool useUniformLods;

        public int totalRockCount { get; private set; }
        public int visibleBatchCount { get; private set; }
        public int visibleRockCount { get; private set; }
        public int estimatedTriangles { get; private set; }
        /// <summary>Submitted rocks: central, negative-X, positive-X; each has 0-7, 7-14, and 14+ km bins.</summary>
        public IReadOnlyList<int> VisibleByRegion => visibleRegionView;
        /// <summary>Region * 3 + size family: small <=65m, medium <149m, large >=149m.</summary>
        public IReadOnlyList<int> VisibleByRegionAndSize => visibleSizeView;
        /// <summary>Submitted native, mid and far instances, using their actual render mesh.</summary>
        public IReadOnlyList<int> VisibleByLod => visibleLodView;
        public int ConsideredCount { get; private set; }
        public int StableRockCount => totalRockCount;
        public int LodSwitchCount { get; private set; }
        public long TotalLodSwitchCount { get; private set; }
        public int ResidentTriangles { get; private set; }
        public bool OverBudget => estimatedTriangles > Mathf.Max(1, maximumVisibleTriangles);
        public bool ResidentOverBudget => ResidentTriangles > Mathf.Max(1, maximumVisibleTriangles);
        public bool OverDrawBudget => visibleBatchCount > MaximumDrawBatches;

        public RangeAsteroidBackdrop()
        {
            visibleRegionView = Array.AsReadOnly(visibleByRegion);
            visibleSizeView = Array.AsReadOnly(visibleByRegionAndSize);
            visibleLodView = Array.AsReadOnly(visibleByLod);
        }

        public void Configure(Camera camera, Mesh highA, Mesh highB, Mesh lowA, Mesh lowB, Material material)
        {
            targetCamera = camera;
            detailedRockA = highA; detailedRockB = highB;
            distantRockA = lowA; distantRockB = lowB;
            rockMaterial = material;
            nearFadeStart = 80f;
            nearFadeEnd = 350f;
            built = false;
            batches.Clear();
        }

        public void ConfigureUniformRocks(Mesh[] variants, float[] diameters)
        {
            if (variants == null || diameters == null || variants.Length == 0 || variants.Length != diameters.Length)
                throw new ArgumentException("Supply equally sized, nonempty rock meshes and diameter arrays.");
            for (int i = 0; i < variants.Length; i++)
                if (!variants[i] || variants[i].subMeshCount != 1 || !(diameters[i] > 0) ||
                    float.IsInfinity(diameters[i]) || float.IsNaN(diameters[i]))
                    throw new ArgumentException("Each uniform rock needs a one-submesh mesh and a finite positive diameter.");
            uniformRockVariants = (Mesh[])variants.Clone();
            uniformRockDiameters = (float[])diameters.Clone();
            built = false;
            batches.Clear();
        }

        /// <summary>LODs preserve native mesh indexing, local coordinates and unit instance scale.</summary>
        public void ConfigureUniformLods(Mesh[] mid, Mesh[] far)
        {
            if (!ValidLods(mid) || !ValidLods(far))
                throw new ArgumentException("Supply one-submesh mid/far meshes matching every configured native rock index.");
            uniformRockMidLods = (Mesh[])mid.Clone();
            uniformRockFarLods = (Mesh[])far.Clone();
            built = false;
            batches.Clear();
        }

        bool ValidLods(Mesh[] meshes)
        {
            if (meshes == null || uniformRockVariants == null || uniformRockVariants.Length == 0 ||
                meshes.Length != uniformRockVariants.Length) return false;
            for (int i = 0; i < meshes.Length; i++)
                if (!meshes[i] || meshes[i].subMeshCount != 1) return false;
            return true;
        }

        bool HasUniformRocks()
        {
            if (uniformRockVariants == null || uniformRockDiameters == null || uniformRockVariants.Length == 0 ||
                uniformRockVariants.Length != uniformRockDiameters.Length) return false;
            for (int i = 0; i < uniformRockVariants.Length; i++)
                if (!uniformRockVariants[i] || uniformRockVariants[i].subMeshCount != 1 ||
                    !(uniformRockDiameters[i] > 0) || float.IsInfinity(uniformRockDiameters[i]) ||
                    float.IsNaN(uniformRockDiameters[i])) return false;
            return true;
        }

        void Start() => BuildOnce();

        void BuildOnce()
        {
            if (built) return;
            built = true;
            batches.Clear();
            totalRockCount = 0;
            TotalLodSwitchCount = 0;
            ResidentTriangles = 0;
            triangleCounts.Clear();
            renderGroups.Clear();
            groupLookup.Clear();
            useUniformRocks = HasUniformRocks();
            useUniformLods = useUniformRocks && ValidLods(uniformRockMidLods) && ValidLods(uniformRockFarLods);
            if (!targetCamera || !rockMaterial || (!useUniformRocks &&
                (!detailedRockA || !detailedRockB || !distantRockA || !distantRockB)))
                return;

            if (instanceMaterial) Destroy(instanceMaterial);
            instanceMaterial = new Material(rockMaterial)
            {
                name = rockMaterial.name + " (backdrop instances)",
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            properties = new MaterialPropertyBlock();
            // Retain serialized legacy fields, but approaching scenery must not hide it.
            float nearStart = -2f;
            float nearEnd = -1f;
            float farStart = Mathf.Max(nearEnd + 1f, farFadeStart);
            float farEnd = Mathf.Max(farStart + 1f, farFadeEnd);
            properties.SetFloat("_NearFadeStart", nearStart);
            properties.SetFloat("_NearFadeEnd", nearEnd);
            properties.SetFloat("_FarFadeStart", farStart);
            properties.SetFloat("_FarFadeEnd", farEnd);
            parameters = new RenderParams(instanceMaterial)
            {
                camera = targetCamera,
                layer = gameObject.layer,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                matProps = properties
            };

            // Three parallel bands. The middle one joins the authored map's Z ends with
            // a small safety clearance; side bands remain >1.6 km from its X edge.
            for (int segmentStart = -HalfLength; segmentStart < HalfLength; segmentStart += SegmentLength)
            {
                int segmentIndex = (segmentStart + HalfLength) / SegmentLength;
                int segmentSeed = unchecked(seed * 7919 + segmentIndex * 104729);
                if (useUniformRocks)
                {
                    AddUniformSegment(segmentStart, 0, new System.Random(unchecked(segmentSeed + 31)));
                    AddUniformSegment(segmentStart, -1, new System.Random(unchecked(segmentSeed + 47)));
                    AddUniformSegment(segmentStart, 1, new System.Random(unchecked(segmentSeed + 59)));
                }
                else
                {
                    int centerDistance = Mathf.Abs(segmentStart + SegmentLength / 2);
                    int centralCount = centerDistance <= 2000 ? 200 : centerDistance <= 4000 ? 128 : 48;
                    AddSegment(segmentStart, 0, centralCount, new System.Random(unchecked(segmentSeed + 31)));
                    AddSegment(segmentStart, -1, 64, new System.Random(unchecked(segmentSeed + 47)));
                    AddSegment(segmentStart, 1, 64, new System.Random(unchecked(segmentSeed + 59)));
                }
            }
        }

        void AddSegment(int segmentStart, int band, int desired, System.Random random)
        {
            desired*=Mathf.Clamp(densityMultiplier,1,8);
            var first = new List<Matrix4x4>(desired);
            var second = new List<Matrix4x4>(desired);
            Bounds firstBounds = default, secondBounds = default;
            for (int i = 0; i < desired && totalRockCount < MaximumRocks; i++)
            {
                float z = segmentStart + 15f + (float)random.NextDouble() * (SegmentLength - 30f);
                if (band == 0 && Mathf.Abs(z) < MapLongitudinalEdge + MapClearance) continue;
                float x = band == 0
                    ? ((float)random.NextDouble() * 2f - 1f) * 1450f
                    : band * (3200f + (float)random.NextDouble() * 2700f);
                float y = BeltHeight + ((float)random.NextDouble() * 2f - 1f) * 185f;
                float horizon = Mathf.Max(Mathf.Abs(z), Mathf.Abs(x));
                float diameter = horizon > 12000f
                    ? Mathf.Lerp(45f, 310f, Mathf.Pow((float)random.NextDouble(), 1.45f))
                    : Mathf.Lerp(24f, 150f, Mathf.Pow((float)random.NextDouble(), 1.35f));
                if (band == 0)
                {
                    // Small edge fragments let the new belt meet the map visually without
                    // allowing a large render-only boulder to enter its occupied AABB.
                    float edgeDistance = Mathf.Abs(z) - MapLongitudinalEdge;
                    if (edgeDistance < MapClearance + 700f)
                        diameter = Mathf.Min(diameter, Mathf.Lerp(18f, 150f,
                            Mathf.Clamp01((edgeDistance - MapClearance) / 700f)));
                }
                float radius = diameter * .5f;
                var scale = new Vector3(radius * Mathf.Lerp(.72f, 1.35f, (float)random.NextDouble()),
                    radius * Mathf.Lerp(.60f, 1.17f, (float)random.NextDouble()),
                    radius * Mathf.Lerp(.74f, 1.34f, (float)random.NextDouble()));
                var position = new Vector3(x, y, z);
                var rotation = Quaternion.Euler((float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                var matrix = Matrix4x4.TRS(position, rotation, scale);
                // The voxel mesh fits within a [-1,1] cube, whose rotated sphere radius is sqrt(3).
                float boundRadius = Mathf.Max(scale.x, scale.y, scale.z) * VoxelRockBoundRadius;
                if (band == 0 && Mathf.Abs(z) - boundRadius < MapLongitudinalEdge + MapClearance) continue;
                var rockBounds = new Bounds(position, Vector3.one * (boundRadius * 2f));
                if (random.Next(2) == 0)
                {
                    first.Add(matrix);
                    if (first.Count == 1) firstBounds = rockBounds; else firstBounds.Encapsulate(rockBounds);
                }
                else
                {
                    second.Add(matrix);
                    if (second.Count == 1) secondBounds = rockBounds; else secondBounds.Encapsulate(rockBounds);
                }
                totalRockCount++;
            }
            if (first.Count > 0) AddBatch(first, firstBounds, 0, band, segmentStart);
            if (second.Count > 0) AddBatch(second, secondBounds, 1, band, segmentStart);
        }

        void AddUniformSegment(int segmentStart, int band, System.Random random)
        {
            var matrices = new List<Matrix4x4>[uniformRockVariants.Length];
            var bounds = new Bounds[uniformRockVariants.Length];
            int segmentIndex = (segmentStart + HalfLength) / SegmentLength;
            // Five segments contain 368 slots instead of 320: exactly 15% denser.
            int segmentPhase = segmentIndex % 5;
            int slots = segmentPhase == 2 || segmentPhase == 4 ? 73 : 74;
            int rankOffset = random.Next(slots);
            int gridOffset = random.Next(UniformGridSide * UniformGridSide);
            // Coprime permutations distribute sizes and occupied cells across a fixed
            // world grid. Positions never depend on frame, view direction or budget.
            for (int slot = 0; slot < slots; slot++)
            {
                float rank = ((slot * 17 + rankOffset) % slots + .5f) / slots;
                float diameter = (rank < .03f ? 24f : rank < .10f ? 35f : rank < .25f ? 50f
                    : rank < .45f ? 72f : rank < .70f ? 104f : rank < .90f ? 149f
                    : rank < .98f ? 215f : 310f) * .7f;
                int gridSlot = (slot * 37 + gridOffset) % (UniformGridSide * UniformGridSide);
                float jitterX = ((float)random.NextDouble() * 2f - 1f) * .42f;
                float jitterZ = ((float)random.NextDouble() * 2f - 1f) * .42f;
                float z = segmentStart + (gridSlot / UniformGridSide + .5f + jitterZ) * (SegmentLength / (float)UniformGridSide);
                float across = (gridSlot % UniformGridSide + .5f + jitterX) / UniformGridSide;
                float x = band == 0 ? Mathf.Lerp(-1400f, 1400f, across)
                    : band * Mathf.Lerp(3200f, 5400f, across);
                float y = BeltHeight + ((float)random.NextDouble() * 2f - 1f) * 260f;
                int shape = random.Next(2);
                var rotation = Quaternion.Euler((float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f, (float)random.NextDouble() * 360f);
                // Consume every slot's random values before clearance rejection, so a
                // rejected map-overlap slot cannot move any other permanent world ID.
                if (band == 0)
                {
                    float edgeDistance = Mathf.Abs(z) - MapLongitudinalEdge;
                    if (edgeDistance < MapClearance) continue;
                    if (edgeDistance < MapClearance + 700f)
                        diameter = Mathf.Min(diameter, Mathf.Lerp(18f, 150f,
                            Mathf.Clamp01((edgeDistance - MapClearance) / 700f)) * .7f);
                }
                int variant = NearestUniformVariant(diameter, shape);
                Mesh mesh = uniformRockVariants[variant];
                float boundRadius = mesh.bounds.extents.magnitude + mesh.bounds.center.magnitude;
                // The central belt's conservative longitudinal slab includes the entire
                // map AABB and 35m clearance; no decorative sphere may enter that slab.
                if (band == 0 && Mathf.Abs(z) - boundRadius < MapLongitudinalEdge + MapClearance) continue;
                var position = new Vector3(x, y, z);
                var matrix = Matrix4x4.TRS(position, rotation, Vector3.one);
                var rockBounds = new Bounds(position, Vector3.one * (boundRadius * 2f));
                if (matrices[variant] == null)
                {
                    matrices[variant] = new List<Matrix4x4>();
                    bounds[variant] = rockBounds;
                }
                else bounds[variant].Encapsulate(rockBounds);
                matrices[variant].Add(matrix);
                totalRockCount++;
            }
            for (int variant = 0; variant < matrices.Length; variant++)
                if (matrices[variant] != null && matrices[variant].Count > 0)
                    AddBatch(matrices[variant], bounds[variant], variant, band, segmentStart);
        }

        void AddBatch(List<Matrix4x4> matrices, Bounds bounds, int shape, int band, int segmentStart)
        {
            var batch = new Batch
            {
                matrices = matrices.ToArray(),
                radii = new float[matrices.Count],
                selected = new bool[matrices.Count],
                lastLods = new int[matrices.Count],
                bounds = bounds,
                index = batches.Count,
                shape = shape,
                band = band,
                segmentStart = segmentStart,
                renderGroups = new RenderGroup[3]
            };
            for (int i = 0; i < batch.matrices.Length; i++)
            {
                Matrix4x4 matrix = batch.matrices[i];
                batch.lastLods[i] = -1;
                batch.radii[i] = useUniformRocks
                    ? uniformRockVariants[shape].bounds.extents.magnitude + uniformRockVariants[shape].bounds.center.magnitude
                    : Mathf.Max(matrix.GetColumn(0).magnitude, Mathf.Max(matrix.GetColumn(1).magnitude,
                        matrix.GetColumn(2).magnitude)) * VoxelRockBoundRadius;
            }
            int shapeCount = useUniformRocks ? uniformRockVariants.Length : 2;
            for (int lod = 0; lod < 3; lod++)
            {
                // Scenes without a complete authored LOD set retain their native geometry.
                int actualLod = useUniformRocks ? (useUniformLods ? lod : 0) : Mathf.Min(lod, 1);
                int key = ((band + 1) * shapeCount + shape) * 3 + actualLod;
                if (!groupLookup.TryGetValue(key, out RenderGroup group))
                {
                    Mesh mesh = useUniformRocks
                        ? actualLod == 0 ? uniformRockVariants[shape]
                        : actualLod == 1 ? uniformRockMidLods[shape] : uniformRockFarLods[shape]
                        : actualLod == 0 ? (shape == 0 ? detailedRockA : detailedRockB)
                        : (shape == 0 ? distantRockA : distantRockB);
                    CacheTriangles(mesh);
                    group = new RenderGroup { mesh = mesh, shape = shape, band = band,
                        lod = actualLod, triangles = triangleCounts[mesh] };
                    groupLookup.Add(key, group);
                    renderGroups.Add(group);
                }
                batch.renderGroups[lod] = group;
                // Reserve at build time only, including legacy scenes with higher density.
                if (lod == 0 || group != batch.renderGroups[lod - 1]) group.ReserveSource(matrices.Count);
                if (useUniformRocks)
                {
                    float lodRadius = group.mesh.bounds.extents.magnitude + group.mesh.bounds.center.magnitude;
                    for (int i = 0; i < batch.radii.Length; i++)
                        batch.radii[i] = Mathf.Max(batch.radii[i], lodRadius);
                }
            }
            batches.Add(batch);
        }

        void CacheTriangles(Mesh mesh)
        {
            if (!triangleCounts.ContainsKey(mesh)) triangleCounts.Add(mesh, (int)(mesh.GetIndexCount(0) / 3));
        }

        int NearestUniformVariant(float diameter, int shape)
        {
            int best = -1;
            float distance = float.PositiveInfinity;
            for (int i = shape; i < uniformRockVariants.Length; i += 2)
            {
                float candidate = Mathf.Abs(Mathf.Log(uniformRockDiameters[i] / Mathf.Max(1f, diameter)));
                if (candidate < distance) { best = i; distance = candidate; }
            }
            return best >= 0 ? best : 0;
        }

        void LateUpdate()
        {
            ResetDiagnostics();
            foreach (var group in renderGroups) group.submittedCount = 0;
            if (!backdropEnabled || !isActiveAndEnabled) return;
            if (!built) BuildOnce();
            if (!targetCamera || !targetCamera.isActiveAndEnabled || !instanceMaterial ||
                (targetCamera.cullingMask & (1 << gameObject.layer)) == 0) return;

            GeometryUtility.CalculateFrustumPlanes(targetCamera, frustum);
            Vector3 cameraPosition = targetCamera.transform.position;
            ResidentTriangles = 0;
            foreach (var batch in batches)
            {
                for (int instance = 0; instance < batch.matrices.Length; instance++)
                {
                    ConsideredCount++;
                    batch.selected[instance] = false;
                    Matrix4x4 matrix = batch.matrices[instance];
                    Vector3 position = matrix.GetColumn(3);
                    float distance = Vector3.Distance(position, cameraPosition);
                    int previousLod = batch.lastLods[instance];
                    int lod = StableLod(previousLod, distance);
                    batch.lastLods[instance] = lod;
                    if (previousLod >= 0 && previousLod != lod)
                    {
                        LodSwitchCount++;
                        TotalLodSwitchCount++;
                    }
                    RenderGroup group = batch.renderGroups[lod];
                    ResidentTriangles += group.triangles;
                    // Visibility never affects identity, LOD state or another rock's LOD.
                    // The frustum includes the camera's real far plane. There is no near
                    // fade, budget rejection, screen-space quota or distance reranking.
                    float radius = batch.radii[instance];
                    if (!SphereInFrustum(position, radius)) continue;
                    int submitted = group.submittedCount++;
                    // Every source slot has build-time reserved submission capacity.
                    group.submittedMatrices[submitted] = matrix;
                    group.submittedSourceIndices[submitted] = batch.index;
                    group.submittedInstanceIndices[submitted] = instance;
                    batch.selected[instance] = true;
                    var rockBounds = new Bounds(position, Vector3.one * (radius * 2f));
                    if (submitted == 0) group.submittedBounds = rockBounds;
                    else group.submittedBounds.Encapsulate(rockBounds);
                    if (submitted % MaximumInstancesPerDraw == 0) visibleBatchCount++;
                    int bandRegion = batch.band == 0 ? 0 : batch.band < 0 ? 1 : 2;
                    int distanceRegion = distance < DetailedLodDistance ? 0 : distance < DetailedLodDistance * 2f ? 1 : 2;
                    int region = bandRegion * 3 + distanceRegion;
                    float diameter = useUniformRocks ? uniformRockDiameters[batch.shape] : radius * 2f;
                    int family = diameter <= 65f ? 0 : diameter < 149f ? 1 : 2;
                    visibleByRegion[region]++;
                    visibleByRegionAndSize[region * SizeFamilyCount + family]++;
                    visibleByLod[group.lod]++;
                    visibleRockCount++;
                    estimatedTriangles += group.triangles;
                }
            }
            foreach (var group in renderGroups)
            {
                if (group.submittedCount == 0) continue;
                parameters.worldBounds = group.submittedBounds;
                for (int start = 0; start < group.submittedCount; start += MaximumInstancesPerDraw)
                    Graphics.RenderMeshInstanced(parameters, group.mesh, 0, group.submittedMatrices,
                        Mathf.Min(MaximumInstancesPerDraw, group.submittedCount - start), start);
            }
        }

        static int StableLod(int previous, float distance)
        {
            if (previous < 0) return distance < 3500f ? 0 : distance < 14000f ? 1 : 2;
            if (previous == 0)
                return distance > 16000f ? 2 : distance > 4200f ? 1 : 0;
            if (previous == 1)
                return distance < 3200f ? 0 : distance > 16000f ? 2 : 1;
            return distance < 3200f ? 0 : distance < 12000f ? 1 : 2;
        }

        void ResetDiagnostics()
        {
            visibleBatchCount = visibleRockCount = estimatedTriangles = 0;
            ConsideredCount = 0;
            LodSwitchCount = 0;
            Array.Clear(visibleByRegion, 0, visibleByRegion.Length);
            Array.Clear(visibleByRegionAndSize, 0, visibleByRegionAndSize.Length);
            Array.Clear(visibleByLod, 0, visibleByLod.Length);
        }

        bool SphereInFrustum(Vector3 position, float radius)
        {
            for (int plane = 0; plane < frustum.Length; plane++)
                if (frustum[plane].GetDistanceToPoint(position) < -radius) return false;
            return true;
        }

        void OnDestroy()
        {
            if (instanceMaterial) Destroy(instanceMaterial);
        }

        void OnDisable()
        {
            ResetDiagnostics();
            foreach (var group in renderGroups) group.submittedCount = 0;
        }
    }
}

