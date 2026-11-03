// @role Combine shared hull models or legacy cube surfaces and merge complete occupied cells into compound box colliders.
// @calls VoxelAssetDefinition, VoxelStructureRuntime, VoxelHullAppearance

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Starframe.Maps
{
    public static class VoxelGeometry
    {
        public static readonly Vector3Int[] Directions = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };
        static readonly Vector3[][] Corners = {
            new[]{new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,-.5f,.5f)},
            new[]{new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,-.5f)},
            new[]{new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f)},
            new[]{new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f)},
            new[]{new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,-.5f,.5f)},
            new[]{new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,-.5f,-.5f)}
        };
        public static Vector3Int Cell(Vector3 point) => new Vector3Int(Mathf.FloorToInt(point.x + .5f), Mathf.FloorToInt(point.y + .5f), Mathf.FloorToInt(point.z + .5f));
        public static Bounds Bounds(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, float size)
        {
            var bounds = new Bounds(); bool first = true;
            foreach (var pair in cells) if (pair.Value.Alive)
            { var next = new Bounds((Vector3)pair.Key * size, Vector3.one * size); if (first) { bounds = next; first = false; } else bounds.Encapsulate(next); }
            return bounds;
        }
        public static Mesh Build(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, int surfaces, float size, Starframe.Flight.ModuleDatabase hullModules = null)
        {
            if (hullModules) return VoxelHullAppearance.Build(cells, surfaces, size, hullModules);
            foreach (var cell in cells.Values) if (cell.Alive && cell.shape != Starframe.Construction.BlockShape.Cube) return VoxelHullAppearance.Build(cells, surfaces, size, null);
            return BuildExterior(cells, cells, surfaces, size, false);
        }
        // Match the reference map's authored full-cell exterior LOD after damage; gameplay cells retain their real shapes.
        internal static Mesh BuildExterior(IReadOnlyDictionary<Vector3Int, VoxelCellState> subset,
            IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, int surfaces, float size, bool hullSlots = true)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var uv = new List<Vector2>();
            var indices = new List<int>[Mathf.Max(1, surfaces)]; for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();
            foreach (var pair in subset)
            {
                var state = pair.Value; if (!state.Alive) continue; var cell = pair.Key;
                int surface = Mathf.Clamp(hullSlots ? state.surface * VoxelHullAppearance.ShapeCount + VoxelHullAppearance.ShapeIndex(state.shape) : state.surface, 0, indices.Length - 1);
                for (int face = 0; face < 6; face++)
                {
                    if (cells.TryGetValue(cell + Directions[face], out var neighbor) && neighbor.Alive) continue;
                    int start = vertices.Count;
                    for (int c = 0; c < 4; c++) { vertices.Add(((Vector3)cell + Corners[face][c]) * size); normals.Add(Directions[face]); uv.Add(new Vector2(c < 2 ? 0 : 1, c == 1 || c == 2 ? 1 : 0)); }
                    indices[surface].Add(start); indices[surface].Add(start + 1); indices[surface].Add(start + 2);
                    indices[surface].Add(start); indices[surface].Add(start + 2); indices[surface].Add(start + 3);
                }
            }
            var mesh = new Mesh { name = "Floating voxel asset surface", indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) mesh.SetTriangles(indices[i], i, false);
            mesh.RecalculateBounds(); mesh.RecalculateTangents(); return mesh;
        }
        public static List<Bounds> CollisionBoxes(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, float size)
        {
            var order = new List<Vector3Int>(); Vector3Int min = default, max = default;
            foreach (var pair in cells) if (pair.Value.Alive)
            {
                if (order.Count == 0) min = max = pair.Key;
                else { min = Vector3Int.Min(min, pair.Key); max = Vector3Int.Max(max, pair.Key); }
                order.Add(pair.Key);
            }
            var boxes = new List<Bounds>();
            if (order.Count == 0) return boxes;
            long widthCells = (long)max.x - min.x + 1, heightCells = (long)max.y - min.y + 1, depthCells = (long)max.z - min.z + 1;
            // Unique occupied cells filling their integer bounding volume prove a solid box.
            // Guard each product before multiplying: arbitrary sparse coordinates must not overflow.
            if (widthCells <= order.Count && heightCells <= order.Count && depthCells <= order.Count &&
                widthCells * heightCells <= order.Count && widthCells * heightCells * depthCells == order.Count)
            {
                var dimensions = new Vector3(widthCells, heightCells, depthCells);
                boxes.Add(new Bounds(((Vector3)min + (dimensions - Vector3.one) * .5f) * size, dimensions * size));
                return boxes;
            }
            var available = new HashSet<Vector3Int>(order);
            order.Sort((a, b) => { int y = a.y.CompareTo(b.y); if (y != 0) return y; int z = a.z.CompareTo(b.z); return z != 0 ? z : a.x.CompareTo(b.x); });
            foreach (var origin in order)
            {
                if (!available.Contains(origin)) continue;
                int width = 1, depth = 1, height = 1;
                while (available.Contains(origin + Vector3Int.right * width)) width++;
                bool full = true;
                while (full) { for (int x = 0; x < width; x++) if (!available.Contains(origin + new Vector3Int(x, 0, depth))) { full = false; break; } if (full) depth++; }
                full = true;
                while (full)
                {
                    for (int z = 0; z < depth && full; z++) for (int x = 0; x < width; x++) if (!available.Contains(origin + new Vector3Int(x, height, z))) { full = false; break; }
                    if (full) height++;
                }
                for (int y = 0; y < height; y++) for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++) available.Remove(origin + new Vector3Int(x, y, z));
                var dimensions = new Vector3(width, height, depth);
                boxes.Add(new Bounds(((Vector3)origin + (dimensions - Vector3.one) * .5f) * size, dimensions * size));
            }
            return boxes;
        }
        public static List<List<VoxelCellState>> Islands(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            var pending = new HashSet<Vector3Int>(); foreach (var pair in cells) if (pair.Value.Alive) pending.Add(pair.Key);
            var groups = new List<List<VoxelCellState>>(); var queue = new Queue<Vector3Int>();
            foreach (var pair in cells)
            {
                if (!pending.Remove(pair.Key)) continue;
                var group = new List<VoxelCellState>(); queue.Enqueue(pair.Key);
                while (queue.Count > 0) { var position = queue.Dequeue(); group.Add(cells[position]); foreach (var direction in Directions) if (pending.Remove(position + direction)) queue.Enqueue(position + direction); }
                groups.Add(group);
            }
            groups.Sort((a, b) => b.Count.CompareTo(a.Count)); return groups;
        }
    }
}
