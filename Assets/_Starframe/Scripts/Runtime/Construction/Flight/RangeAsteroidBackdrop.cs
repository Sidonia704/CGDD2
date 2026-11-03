// @role Draw a fixed, decorative continuation of the range's asteroid belt using bounded, camera-culled mesh instances and no physics.
// @tune backdropEnabled:bool:0-1:Show or hide the decorative belts without changing their deterministic positions.
// @tune seed:int:0-2147483647:Stable placement seed for all three world-anchored belts.
// @tune nearFadeStart:float:0-10000:Camera distance within which an approached decorative rock is invisible.
// @tune nearFadeEnd:float:0-10000:Camera distance beyond which an approached decorative rock is fully visible.
// @tune farFadeStart:float:0-50000:Camera distance where horizon rocks begin fading out.
// @tune farFadeEnd:float:0-50000:Camera distance where horizon rocks disappear.

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
        const int MaximumRocks = 4800;
        const int MaximumDrawBatches = 180;
        const int MaximumVisibleRocks = 3000;
        const float DetailedLodDistance = 7000f;
        const float BeltHeight = -1330f;
        const float MapLongitudinalEdge = 1754f;
        const float MapClearance = 35f;
        const float VoxelRockBoundRadius = 1.75f;

        [SerializeField] Camera targetCamera;
        [SerializeField] Mesh detailedRockA, detailedRockB, distantRockA, distantRockB;
        [SerializeField] Material rockMaterial;
        [SerializeField] int seed = 261107;
        [SerializeField, Min(0)] float nearFadeStart = 80f;
        [SerializeField, Min(0)] float nearFadeEnd = 350f;
        [SerializeField, Min(0)] float farFadeStart = 17000f;
        [SerializeField, Min(0)] float farFadeEnd = 25000f;
        public bool backdropEnabled = true;

        sealed class Batch
        {
            public Matrix4x4[] matrices;
            public Bounds bounds;
            public int shape;
            public float distanceSquared;
        }

        readonly List<Batch> batches = new List<Batch>(160);
        readonly List<Batch> candidates = new List<Batch>(200);
        readonly Plane[] frustum = new Plane[6];
        Material instanceMaterial;
        MaterialPropertyBlock properties;
        RenderParams parameters;
        bool built;

        public int totalRockCount { get; private set; }
        public int visibleBatchCount { get; private set; }
        public int visibleRockCount { get; private set; }
        public int estimatedTriangles { get; private set; }

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

        void Start() => BuildOnce();

        void BuildOnce()
        {
            if (built) return;
            built = true;
            batches.Clear();
            totalRockCount = 0;
            if (!targetCamera || !rockMaterial || !detailedRockA || !detailedRockB || !distantRockA || !distantRockB)
                return;

            if (instanceMaterial) Destroy(instanceMaterial);
            instanceMaterial = new Material(rockMaterial)
            {
                name = rockMaterial.name + " (backdrop instances)",
                enableInstancing = true,
                hideFlags = HideFlags.DontSave
            };
            properties = new MaterialPropertyBlock();
            float nearStart = Mathf.Max(0, nearFadeStart);
            float nearEnd = Mathf.Max(nearStart + 1f, nearFadeEnd);
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
                int centerDistance = Mathf.Abs(segmentStart + SegmentLength / 2);
                int centralCount = centerDistance <= 2000 ? 200 : centerDistance <= 4000 ? 128 : 48;
                AddSegment(segmentStart, 0, centralCount, new System.Random(unchecked(segmentSeed + 31)));
                AddSegment(segmentStart, -1, 64, new System.Random(unchecked(segmentSeed + 47)));
                AddSegment(segmentStart, 1, 64, new System.Random(unchecked(segmentSeed + 59)));
            }
        }

        void AddSegment(int segmentStart, int band, int desired, System.Random random)
        {
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
            if (first.Count > 0) batches.Add(new Batch { matrices = first.ToArray(), bounds = firstBounds, shape = 0 });
            if (second.Count > 0) batches.Add(new Batch { matrices = second.ToArray(), bounds = secondBounds, shape = 1 });
        }

        void LateUpdate()
        {
            visibleBatchCount = visibleRockCount = estimatedTriangles = 0;
            if (!backdropEnabled || !isActiveAndEnabled) return;
            if (!built) BuildOnce();
            if (!targetCamera || !targetCamera.isActiveAndEnabled || !instanceMaterial ||
                (targetCamera.cullingMask & (1 << gameObject.layer)) == 0) return;

            GeometryUtility.CalculateFrustumPlanes(targetCamera, frustum);
            candidates.Clear();
            var cameraPosition = targetCamera.transform.position;
            float far = Mathf.Min(targetCamera.farClipPlane, Mathf.Max(farFadeEnd, farFadeStart + 1f));
            float farSquared = far * far;
            foreach (var batch in batches)
            {
                batch.distanceSquared = batch.bounds.SqrDistance(cameraPosition);
                if (batch.distanceSquared > farSquared || !GeometryUtility.TestPlanesAABB(frustum, batch.bounds)) continue;
                candidates.Add(batch);
            }
            candidates.Sort((a, b) => a.distanceSquared.CompareTo(b.distanceSquared));
            foreach (var batch in candidates)
            {
                if (visibleBatchCount >= MaximumDrawBatches) break;
                if (visibleRockCount + batch.matrices.Length > MaximumVisibleRocks) continue;
                Mesh mesh = batch.distanceSquared < DetailedLodDistance * DetailedLodDistance
                    ? (batch.shape == 0 ? detailedRockA : detailedRockB)
                    : (batch.shape == 0 ? distantRockA : distantRockB);
                parameters.worldBounds = batch.bounds;
                Graphics.RenderMeshInstanced(parameters, mesh, 0, batch.matrices, batch.matrices.Length);
                visibleBatchCount++;
                visibleRockCount += batch.matrices.Length;
                estimatedTriangles += (int)(mesh.GetIndexCount(0) / 3) * batch.matrices.Length;
            }
        }

        void OnDestroy()
        {
            if (instanceMaterial) Destroy(instanceMaterial);
        }

        void OnDisable()
        {
            visibleBatchCount = visibleRockCount = estimatedTriangles = 0;
        }
    }
}
