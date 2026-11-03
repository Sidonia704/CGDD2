// @role Generate deterministic voxel stamps and rotated multi-cell hull pieces, and ray-pick live scenery cells.
// @calls VoxelAssetDefinition, VoxelStructureRuntime, VoxelGeometry, VoxelBodyHitbox, BlockModules

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    public enum VoxelStampShape { Box, Asteroid, Ring }
    public static class VoxelAuthoring
    {
        public static List<VoxelCell> Stamp(VoxelStampShape shape, Vector3Int size, Vector3Int center, int seed, float roughness, bool shell, int surface)
        {
            size = new Vector3Int(Mathf.Clamp(size.x, 1, 64), Mathf.Clamp(size.y, 1, 64), Mathf.Clamp(size.z, 1, 64));
            var output = new List<VoxelCell>(); var random = new System.Random(seed);
            Vector3 radii = (Vector3)size * .5f;
            for (int x = 0; x < size.x; x++) for (int y = 0; y < size.y; y++) for (int z = 0; z < size.z; z++)
            {
                Vector3 p = new Vector3(x - (size.x - 1) * .5f, y - (size.y - 1) * .5f, z - (size.z - 1) * .5f);
                Vector3 n = new Vector3(p.x / radii.x, p.y / radii.y, p.z / radii.z);
                float noise = (float)random.NextDouble(); bool include;
                if (shape == VoxelStampShape.Asteroid)
                {
                    float threshold = 1 - Mathf.Clamp01(roughness) * .38f * noise;
                    include = n.magnitude <= threshold && (!shell || n.magnitude >= Mathf.Max(0, threshold - 2f / Mathf.Min(size.x, Mathf.Min(size.y, size.z))));
                }
                else if (shape == VoxelStampShape.Ring)
                {
                    float radius = new Vector2(n.x, n.z).magnitude;
                    include = radius <= 1 && radius >= .60f;
                }
                else include = !shell || x == 0 || y == 0 || z == 0 || x == size.x - 1 || y == size.y - 1 || z == size.z - 1;
                if (include)
                {
                    if (output.Count >= VoxelAssetDefinition.MaximumCells) throw new ArgumentException("生成数量超过资产上限，请减小尺寸。");
                    output.Add(new VoxelCell { position = new Vector3Int(x - size.x / 2, y - size.y / 2, z - size.z / 2) + center, surface = surface });
                }
            }
            return output;
        }

        public static List<VoxelCell> HullPiece(Starframe.Construction.BlockShape shape, Vector3Int anchor, Vector3Int turns, int surface, Starframe.Flight.ModuleDatabase database = null)
        {
            var definition = database ? database.Get(shape) : null;
            var offsets = definition && definition.occupiedCells != null && definition.occupiedCells.Length > 0 ? definition.occupiedCells : Starframe.Construction.BlockModules.LocalCells(shape);
            var rotation = Quaternion.Euler((Vector3)turns * 90);
            var result = new List<VoxelCell>();
            foreach (var offset in offsets) result.Add(new VoxelCell { position = anchor + Vector3Int.RoundToInt(rotation * (Vector3)offset), shape = shape, quarterTurns = turns, surface = surface, modelOffset = offset });
            return result;
        }
        public static bool Pick(VoxelStructureRuntime structure, Ray ray, out Vector3Int result, out Vector3Int normal, out float distance)
        {
            result = normal = Vector3Int.zero; distance = float.PositiveInfinity;
            if (!structure || !structure.asset || structure.AliveCount == 0) return false;
            float size = structure.asset.cellSize;
            Matrix4x4 matrix = structure.transform.localToWorldMatrix * Matrix4x4.Scale(Vector3.one * size);
            Matrix4x4 inverse = matrix.inverse;
            Vector3 origin = inverse.MultiplyPoint3x4(ray.origin), direction = inverse.MultiplyVector(ray.direction);
            var bounds = structure.LocalBounds; Vector3 min = bounds.min / size, max = bounds.max / size;
            float from = 0, to = 1000000;
            if (!VoxelBodyHitbox.Clip(origin, direction, min, max, ref from, ref to)) return false;
            var cell = VoxelGeometry.Cell(origin + direction * (from + .0001f));
            int safety = 0;
            while (from <= to + .0001f && safety++ < 20000)
            {
                if (structure.Cells.TryGetValue(cell, out var state) && state.Alive)
                {
                    result = cell; distance = from;
                    Vector3 offset = origin + direction * from - (Vector3)cell;
                    int axis = Mathf.Abs(offset.x) > Mathf.Abs(offset.y) ? (Mathf.Abs(offset.x) > Mathf.Abs(offset.z) ? 0 : 2) : (Mathf.Abs(offset.y) > Mathf.Abs(offset.z) ? 1 : 2);
                    normal[axis] = offset[axis] >= 0 ? 1 : -1; return true;
                }
                int nextAxis = -1; float next = float.PositiveInfinity;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (Mathf.Abs(direction[axis]) < .0000001f) continue;
                    float t = (cell[axis] + (direction[axis] > 0 ? .5f : -.5f) - origin[axis]) / direction[axis];
                    if (t < next) { next = t; nextAxis = axis; }
                }
                if (nextAxis < 0 || next > to) break;
                from = Mathf.Max(from, next); cell[nextAxis] += direction[nextAxis] > 0 ? 1 : -1;
            }
            return false;
        }
    }
}
