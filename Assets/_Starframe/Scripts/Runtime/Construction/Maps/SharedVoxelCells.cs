// @role Keep one compact immutable occupancy generation and bounded owner-aware lookup states.
// @calls VoxelAssetDefinition, VoxelStructureRuntime

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;

namespace Starframe.Maps
{
    // One packed generation per reusable asset; no per-instance cell dictionary or state objects.
    internal sealed class SharedVoxelCells : IReadOnlyDictionary<Vector3Int, VoxelCellState>
    {
        struct Record
        {
            public Vector3Int position, modelOffset;
            public int surface;
            public uint shapeAndTurns;
            public Record(VoxelCell cell)
            {
                position = cell.position; modelOffset = cell.modelOffset; surface = cell.surface;
                shapeAndTurns = (uint)cell.shape | ((uint)(cell.quarterTurns.x & 3) << 16) |
                    ((uint)(cell.quarterTurns.y & 3) << 18) | ((uint)(cell.quarterTurns.z & 3) << 20);
            }
            public VoxelCellState State(VoxelStructureRuntime owner) => new VoxelCellState {
                position = position, surface = surface, modelOffset = modelOffset, owner = owner,
                shape = (BlockShape)(shapeAndTurns & 65535),
                quarterTurns = new Vector3Int((int)(shapeAndTurns >> 16) & 3,
                    (int)(shapeAndTurns >> 18) & 3, (int)(shapeAndTurns >> 20) & 3) };
        }
        readonly Record[] records;
        readonly Dictionary<Vector3Int, int> index;
        readonly int start, count;
        readonly SharedVoxelCells root;
        public readonly Bounds bounds;
        public readonly Vector3 centerOfCells;
        public int Count => count;
        public SharedVoxelCells(VoxelAssetDefinition asset)
        {
            records = new Record[asset.cells.Count]; index = new Dictionary<Vector3Int, int>(records.Length);
            count = records.Length; root = this;
            double x = 0, y = 0, z = 0;
            var extent = new Bounds(Vector3.zero, Vector3.one * asset.cellSize);
            for (int i = 0; i < records.Length; i++)
            {
                var cell = asset.cells[i]; records[i] = new Record(cell); index.Add(cell.position, i);
                x += cell.position.x; y += cell.position.y; z += cell.position.z;
                var box = new Bounds((Vector3)cell.position * asset.cellSize, Vector3.one * asset.cellSize);
                if (i == 0) extent = box; else extent.Encapsulate(box);
            }
            bounds = extent;
            centerOfCells = count > 0 ? new Vector3((float)(x / count), (float)(y / count), (float)(z / count)) * asset.cellSize : Vector3.zero;
        }
        SharedVoxelCells(SharedVoxelCells source, int offset, int length)
        { root = source.root; records = root.records; index = root.index; start = offset; count = length; }
        public SharedVoxelCells Slice(int offset, int length) => new SharedVoxelCells(root, offset, length);
        public bool ContainsKey(Vector3Int key) => index.TryGetValue(key, out int i) && i >= start && i < start + count;
        internal bool TryState(Vector3Int key, VoxelStructureRuntime owner, out VoxelCellState value)
        {
            if (index.TryGetValue(key, out int i) && i >= start && i < start + count)
            { value = records[i].State(owner); return true; }
            value = null; return false;
        }
        public bool TryGetValue(Vector3Int key, out VoxelCellState value) => TryState(key, null, out value);
        public VoxelCellState this[Vector3Int key] => TryGetValue(key, out var state) ? state : throw new KeyNotFoundException();
        public IEnumerable<Vector3Int> Keys { get { for (int i = start; i < start + count; i++) yield return records[i].position; } }
        public IEnumerable<VoxelCellState> Values { get { for (int i = start; i < start + count; i++) yield return records[i].State(null); } }
        public IEnumerator<KeyValuePair<Vector3Int, VoxelCellState>> GetEnumerator()
        { for (int i = start; i < start + count; i++) yield return new KeyValuePair<Vector3Int, VoxelCellState>(records[i].position, records[i].State(null)); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal sealed class OwnedVoxelCells : IReadOnlyDictionary<Vector3Int, VoxelCellState>
    {
        const int WorkingSetLimit = 256;
        readonly SharedVoxelCells source;
        readonly VoxelStructureRuntime owner;
        readonly Dictionary<Vector3Int, VoxelCellState> working = new Dictionary<Vector3Int, VoxelCellState>();
        public OwnedVoxelCells(SharedVoxelCells cells, VoxelStructureRuntime body) { source = cells; owner = body; }
        public int Count => source.Count;
        public int CachedCount => working.Count;
        public bool ContainsKey(Vector3Int key) => source.ContainsKey(key);
        public bool TryGetValue(Vector3Int key, out VoxelCellState value)
        {
            if (working.TryGetValue(key, out value)) return true;
            if (!source.TryState(key, owner, out value)) return false;
            // Cells are immutable. Existing anchor references remain valid after cache eviction.
            if (working.Count >= WorkingSetLimit) working.Clear();
            working.Add(key, value); return true;
        }
        public VoxelCellState this[Vector3Int key] => TryGetValue(key, out var value) ? value : throw new KeyNotFoundException();
        public IEnumerable<Vector3Int> Keys => source.Keys;
        public IEnumerable<VoxelCellState> Values { get { foreach (var key in source.Keys) yield return this[key]; } }
        public IEnumerator<KeyValuePair<Vector3Int, VoxelCellState>> GetEnumerator()
        { foreach (var key in source.Keys) yield return new KeyValuePair<Vector3Int, VoxelCellState>(key, this[key]); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
