// @role Certify unchanged scenery connectivity locally and enumerate exact islands only when a deletion disconnects them.
// @calls VoxelGeometry, VoxelStructureRuntime

using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    /// <summary>
    /// Per-body scratch storage. Resolve requires every cell removed since Reset/Resolve in removedPositions.
    /// A null result means that all surviving cells are still connected; a non-null result contains every island.
    /// No ship mounting or visual-surface rules participate: scenery uses complete-cell face adjacency.
    /// </summary>
    public sealed class VoxelTerrainTopology
    {
        readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        readonly HashSet<Vector3Int> boundary = new HashSet<Vector3Int>();
        readonly List<Vector3Int> queue = new List<Vector3Int>();
        public bool KnownConnected { get; private set; }

        /// <summary>Classify a new body, including assets which already contain multiple islands.</summary>
        public bool Reset(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            visited.Clear(); boundary.Clear(); queue.Clear();
            int alive = 0; Vector3Int first = default;
            foreach (var pair in cells)
                if (pair.Value.Alive) { if (alive++ == 0) first = pair.Key; }
            if (alive <= 1) return KnownConnected = true;
            visited.Add(first); queue.Add(first);
            for (int head = 0; head < queue.Count && visited.Count < alive; head++)
                AddNeighbors(cells, queue[head], false);
            return KnownConnected = visited.Count == alive;
        }

        /// <summary>Only call after ownership has been reduced to one exact component returned by Resolve.</summary>
        public void MarkConnected() => KnownConnected = true;

        public List<List<VoxelCellState>> Resolve(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells,
            IReadOnlyList<Vector3Int> removedPositions)
        {
            visited.Clear(); boundary.Clear(); queue.Clear();
            if (!KnownConnected || removedPositions == null || removedPositions.Count == 0)
                return FullFallback(cells);
            foreach (var removed in removedPositions)
            {
                // An incomplete/misordered removal submission cannot establish the boundary theorem.
                if (cells.TryGetValue(removed, out var removedState) && removedState.Alive)
                    return FullFallback(cells);
                foreach (var direction in VoxelGeometry.Directions)
                {
                    var neighbor = removed + direction;
                    if (cells.TryGetValue(neighbor, out var state) && state.Alive) boundary.Add(neighbor);
                }
            }
            // In an originally connected graph, every remaining component touches the removed set.
            if (boundary.Count <= 1) return null;
            var first = FirstBoundary(); visited.Add(first); boundary.Remove(first); queue.Add(first);
            for (int head = 0; head < queue.Count; head++)
            {
                AddNeighbors(cells, queue[head], true);
                if (boundary.Count == 0) return null; // Certificate: every boundary seed reaches this component.
            }

            var groups = new List<List<VoxelCellState>> { CopyGroup(cells) };
            while (boundary.Count > 0)
            {
                first = FirstBoundary(); boundary.Remove(first);
                if (!visited.Add(first)) continue;
                queue.Clear(); queue.Add(first);
                for (int head = 0; head < queue.Count; head++) AddNeighbors(cells, queue[head], true);
                groups.Add(CopyGroup(cells));
            }
            groups.Sort((a, b) => b.Count.CompareTo(a.Count));
            KnownConnected = false; // Caller must transfer ownership before using MarkConnected.
            return groups;
        }

        void AddNeighbors(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells, Vector3Int position, bool consumeBoundary)
        {
            foreach (var direction in VoxelGeometry.Directions)
            {
                var neighbor = position + direction;
                if (!cells.TryGetValue(neighbor, out var state) || !state.Alive || !visited.Add(neighbor)) continue;
                queue.Add(neighbor);
                if (consumeBoundary) boundary.Remove(neighbor);
            }
        }
        Vector3Int FirstBoundary() { foreach (var position in boundary) return position; return default; }
        List<VoxelCellState> CopyGroup(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            var group = new List<VoxelCellState>(queue.Count);
            foreach (var position in queue) group.Add(cells[position]);
            return group;
        }
        List<List<VoxelCellState>> FullFallback(IReadOnlyDictionary<Vector3Int, VoxelCellState> cells)
        {
            var groups = VoxelGeometry.Islands(cells); KnownConnected = groups.Count <= 1;
            return KnownConnected ? null : groups;
        }
    }
}
