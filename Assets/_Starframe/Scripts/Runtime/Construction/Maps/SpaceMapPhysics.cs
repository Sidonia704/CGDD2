// @role Budget active map colliders by camera distance and isolate their mutual contacts while retaining exterior collisions and exact voxel shot data.
// @calls VoxelStructureRuntime

using System.Collections.Generic;
using System;
using UnityEngine;

namespace Starframe.Maps
{
    internal sealed class SpaceMapPhysics
    {
        const string DedicatedLayer = "BudgetedScenery";
        static int users, sharedLayer;
        static bool previousSelfCollision;
        struct Candidate
        {
            public VoxelStructureRuntime body;
            public float distance;
        }
        readonly List<Candidate> candidates = new List<Candidate>();
        readonly HashSet<VoxelStructureRuntime> selected = new HashSet<VoxelStructureRuntime>();
        readonly List<VoxelStructureRuntime> pending = new List<VoxelStructureRuntime>();
        static readonly Unity.Profiling.ProfilerMarker PreparationMarker = new Unity.Profiling.ProfilerMarker("Starframe.MapColliderPreparation");
        public int ActiveColliderCount { get; private set; }
        public int Layer { get; }

        public SpaceMapPhysics()
        {
            Layer = LayerMask.NameToLayer(DedicatedLayer);
            if (Layer < 0) throw new InvalidOperationException("Missing BudgetedScenery physics layer.");
            if (users == 0)
            {
                sharedLayer = Layer;
                previousSelfCollision = Physics.GetIgnoreLayerCollision(Layer, Layer);
                Physics.IgnoreLayerCollision(Layer, Layer, true);
            }
            users++;
        }
        public void Dispose()
        {
            if (users <= 0) return;
            users--;
            if (users == 0) Physics.IgnoreLayerCollision(sharedLayer, sharedLayer, previousSelfCollision);
        }

        public void Register(VoxelStructureRuntime structure)
        {
            if (!structure || !structure.Body) return;
            structure.SetDetailedCollisionEnabled(false);
        }
        public void Refresh(IReadOnlyList<VoxelStructureRuntime> structures, Camera camera, float detailDistance, int colliderBudget, bool staged = false)
        {
            candidates.Clear();
            int baseCount = 0;
            foreach (var body in structures)
            {
                if (!body || !body.Body || body.AliveCount == 0) continue;
                baseCount++; // Every physical body keeps one moving bounds proxy for projectiles and pushes.
                if (!camera || detailDistance <= 0 || body.DetailedColliderCost <= 1) continue;
                var world = WorldBounds(body.PhysicsMatrix, body.LocalBounds);
                float distance = Vector3.Distance(camera.transform.position, world.ClosestPoint(camera.transform.position));
                // Keep already detailed bodies until they leave a wider band, avoiding repeated
                // thousands-of-colliders switches when the camera hovers at the boundary.
                if (distance <= detailDistance * (selected.Contains(body) ? 1.2f : 1f))
                    candidates.Add(new Candidate { body = body, distance = distance * (selected.Contains(body) ? .85f : 1f) });
            }
            candidates.Sort((a, b) => a.distance.CompareTo(b.distance));
            int active = baseCount;
            int limit = Mathf.Max(baseCount, colliderBudget);
            selected.Clear();
            foreach (var candidate in candidates)
            {
                int extra = candidate.body.DetailedColliderCost - 1;
                if (active + extra > limit) continue;
                selected.Add(candidate.body); active += extra;
            }
            pending.Clear();
            foreach (var body in structures)
                if (body && body.Body && body.AliveCount > 0)
                {
                    if (!selected.Contains(body)) body.SetDetailedCollisionEnabled(false);
                    else if (!staged) body.SetDetailedCollisionEnabled(true);
                }
            if (staged) foreach (var candidate in candidates)
                if (selected.Contains(candidate.body) && candidate.body.UsesBoundsCollisionProxy) pending.Add(candidate.body);
            ActiveColliderCount = active;
        }
        public void StepPreparation(int maximumBoxes, float milliseconds)
        {
            using var sample = PreparationMarker.Auto();
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int boxes = Math.Max(1, maximumBoxes);
            while (pending.Count > 0 && boxes > 0)
            {
                var body = pending[0];
                if (!body || body.AliveCount == 0 || !selected.Contains(body)) { pending.RemoveAt(0); continue; }
                boxes -= body.PrepareDetailedCollision(Math.Min(4, boxes));
                if (body.MaterializedColliderCount >= body.DetailedColliderCost)
                { body.SetDetailedCollisionEnabled(true); pending.RemoveAt(0); break; }
                if (milliseconds > 0 && (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= milliseconds) break;
            }
        }
        static Bounds WorldBounds(Matrix4x4 matrix, Bounds local)
        {
            var center = matrix.MultiplyPoint3x4(local.center);
            var extents = local.extents;
            var x = matrix.MultiplyVector(new Vector3(extents.x, 0, 0));
            var y = matrix.MultiplyVector(new Vector3(0, extents.y, 0));
            var z = matrix.MultiplyVector(new Vector3(0, 0, extents.z));
            return new Bounds(center, new Vector3(
                Mathf.Abs(x.x) + Mathf.Abs(y.x) + Mathf.Abs(z.x),
                Mathf.Abs(x.y) + Mathf.Abs(y.y) + Mathf.Abs(z.y),
                Mathf.Abs(x.z) + Mathf.Abs(y.z) + Mathf.Abs(z.z)) * 2);
        }
    }
}
