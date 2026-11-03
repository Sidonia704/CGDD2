// @role Resolve projectile cylinders against live cells in a freely moving compound rigidbody asset.
// @calls VoxelStructureRuntime, CylindricalHitQuery, VoxelGeometry

using System.Collections.Generic;
using Starframe.Combat;
using Starframe.Construction;
using UnityEngine;

namespace Starframe.Maps
{
    public struct VoxelContact { public VoxelStructureRuntime structure; public VoxelCellState cell; public float distance; }
    public sealed class VoxelBodyHitbox : MonoBehaviour
    {
        static Mesh unitCube;
        readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        public VoxelStructureRuntime Structure { get; private set; }
        public void Initialize(VoxelStructureRuntime owner) => Structure = owner;
        public void CollectHits(Vector3 origin, Vector3 direction, float radius, float from, float to, CylindricalHitQuery geometry, List<VoxelContact> output)
        {
            if (!Structure || !Structure.asset || Structure.AliveCount == 0 || to < from) return;
            var matrix = Structure.PhysicsMatrix * Matrix4x4.Scale(Vector3.one * Structure.asset.cellSize);
            var inverse = matrix.inverse; var localOrigin = inverse.MultiplyPoint3x4(origin); var localDirection = inverse.MultiplyVector(direction);
            var scale = matrix.lossyScale;
            float localRadius = radius / Mathf.Max(.0001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
            var bounds = Structure.LocalBounds; float size = Structure.asset.cellSize;
            Vector3 min = bounds.min / size - Vector3.one * localRadius, max = bounds.max / size + Vector3.one * localRadius;
            if (!Clip(localOrigin, localDirection, min, max, ref from, ref to)) return;
            if (!unitCube) unitCube = BlockGeometry.CreateSolid(BlockShape.Cube);
            visited.Clear(); int reach = Mathf.CeilToInt(localRadius);
            Vector3Int cell = VoxelGeometry.Cell(localOrigin + localDirection * (from + .00001f));
            var step = new Vector3Int(Sign(localDirection.x), Sign(localDirection.y), Sign(localDirection.z));
            Vector3 delta = new Vector3(Delta(localDirection.x), Delta(localDirection.y), Delta(localDirection.z));
            Vector3 next = new Vector3(Next(localOrigin.x, localDirection.x, cell.x, step.x), Next(localOrigin.y, localDirection.y, cell.y, step.y), Next(localOrigin.z, localDirection.z, cell.z, step.z));
            for (int safety = 0; safety < 20000; safety++)
            {
                for (int x = -reach; x <= reach; x++) for (int y = -reach; y <= reach; y++) for (int z = -reach; z <= reach; z++)
                {
                    var candidate = cell + new Vector3Int(x, y, z);
                    if (!visited.Add(candidate) || !Structure.Cells.TryGetValue(candidate, out var state) || !state.Alive) continue;
                    if (geometry.TryHitMesh(unitCube, matrix * Matrix4x4.Translate(candidate), origin, direction, radius, from, to, out float entry))
                        output.Add(new VoxelContact { structure = Structure, cell = state, distance = entry });
                }
                float distance = Mathf.Min(next.x, Mathf.Min(next.y, next.z)); if (distance > to || float.IsInfinity(distance)) break;
                if (next.x <= distance + .000001f) { cell.x += step.x; next.x += delta.x; }
                if (next.y <= distance + .000001f) { cell.y += step.y; next.y += delta.y; }
                if (next.z <= distance + .000001f) { cell.z += step.z; next.z += delta.z; }
            }
        }
        static int Sign(float value) => value > .0000001f ? 1 : value < -.0000001f ? -1 : 0;
        static float Delta(float value) => Mathf.Abs(value) > .0000001f ? Mathf.Abs(1 / value) : float.PositiveInfinity;
        static float Next(float origin, float direction, int cell, int step) => step == 0 ? float.PositiveInfinity : (cell + step * .5f - origin) / direction;
        public static bool Clip(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max, ref float from, ref float to)
        {
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(direction[axis]) < .0000001f) { if (origin[axis] < min[axis] || origin[axis] > max[axis]) return false; continue; }
                float a = (min[axis] - origin[axis]) / direction[axis], b = (max[axis] - origin[axis]) / direction[axis];
                from = Mathf.Max(from, Mathf.Min(a, b)); to = Mathf.Min(to, Mathf.Max(a, b)); if (from > to) return false;
            }
            return true;
        }
    }
}
