// @role Cache exact scenery box partitions, materialize their components on demand, and subtract destroyed cells without filling holes.
// @calls VoxelGeometry, VoxelStructureRuntime

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    /// <summary>One helper belongs to one body. Only synchronize exact, disjoint occupied-cell box descriptions.</summary>
    public sealed class VoxelTerrainColliders
    {
        List<BoxCollider> active = new List<BoxCollider>(), next = new List<BoxCollider>();
        readonly List<BoxCollider> pool = new List<BoxCollider>();
        readonly List<Bounds> baseline = new List<Bounds>(), missing = new List<Bounds>();
        readonly Dictionary<Bounds, BoxCollider> unchanged = new Dictionary<Bounds, BoxCollider>();
        readonly HashSet<BoxCollider> retained = new HashSet<BoxCollider>();
        readonly HashSet<Vector3Int> removed = new HashSet<Vector3Int>();
        GameObject bodyOwner;
        List<Bounds> pendingLocal;
        int removalsSinceCompact, fullCompactBoxCount, pendingLocalRemovals;
        const int MaximumLocalRemovals = 64;
        public int Count => active.Count;
        public int LogicalCount => baseline.Count;
        public IReadOnlyList<BoxCollider> Active => active;

        // Cold budgeted maps retain the exact partition without allocating a Unity component for every box.
        public void RecordDeferred(GameObject owner, IReadOnlyList<Bounds> boxes)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (bodyOwner && bodyOwner != owner) throw new InvalidOperationException("A collider helper belongs to one body.");
            if (active.Count != 0) throw new InvalidOperationException("Materialized colliders require Synchronize.");
            if (pool.Count != 0) { Synchronize(owner, boxes, false); return; }
            bodyOwner = owner;
            bool localUpdate = ReferenceEquals(boxes, pendingLocal);
            baseline.Clear(); foreach (var box in boxes) baseline.Add(box);
            if (localUpdate) removalsSinceCompact += pendingLocalRemovals;
            else { removalsSinceCompact = 0; fullCompactBoxCount = boxes.Count; }
            pendingLocal = null; pendingLocalRemovals = 0;
        }

        public void Materialize(GameObject owner, bool enabled)
        {
            PrepareMaterialization(owner, int.MaxValue);
            SetEnabled(enabled);
        }

        // Create cold components in bounded batches, disabled beneath the live broadphase proxy.
        // The caller switches to the complete exact hull only after every box is ready.
        public int PrepareMaterialization(GameObject owner, int maximumBoxes)
        {
            if (!owner || bodyOwner != owner) throw new InvalidOperationException("Collider owner changed before materialization.");
            int created = 0;
            while (active.Count < baseline.Count && created < maximumBoxes)
            {
                BoxCollider collider = null;
                while (pool.Count > 0 && !collider)
                { int last = pool.Count - 1; collider = pool[last]; pool.RemoveAt(last); }
                if (!collider) collider = owner.AddComponent<BoxCollider>();
                var box = baseline[active.Count];
                collider.enabled = false; collider.center = box.center; collider.size = box.size;
                active.Add(collider); created++;
            }
            return created;
        }

        public void Synchronize(GameObject owner, IReadOnlyList<Bounds> boxes, bool enabled)
        {
            if (!owner) throw new ArgumentNullException(nameof(owner));
            if (boxes == null) throw new ArgumentNullException(nameof(boxes));
            if (bodyOwner && bodyOwner != owner) throw new InvalidOperationException("A collider helper belongs to one body.");
            bodyOwner = owner;
            bool localUpdate = ReferenceEquals(boxes, pendingLocal);
            next.Clear(); missing.Clear(); unchanged.Clear(); retained.Clear();
            foreach (var collider in active)
                if (collider) unchanged[new Bounds(collider.center, collider.size)] = collider;
            // Match every unchanged box before recycling anything: exact survivors retain component identity.
            foreach (var box in boxes)
            {
                if (unchanged.TryGetValue(box, out var collider) && collider && collider.gameObject == owner)
                {
                    unchanged.Remove(box); retained.Add(collider); next.Add(collider);
                    collider.enabled = enabled;
                }
                else missing.Add(box);
            }
            foreach (var collider in active)
                if (collider && !retained.Contains(collider)) { collider.enabled = false; pool.Add(collider); }
            foreach (var box in missing)
            {
                BoxCollider collider = null;
                while (pool.Count > 0 && !collider)
                {
                    int last = pool.Count - 1; var candidate = pool[last]; pool.RemoveAt(last);
                    if (candidate && candidate.gameObject == owner) collider = candidate;
                }
                if (!collider) collider = owner.AddComponent<BoxCollider>();
                collider.enabled = false; collider.center = box.center; collider.size = box.size;
                collider.enabled = enabled; next.Add(collider);
            }
            var old = active; active = next; next = old; next.Clear();
            baseline.Clear(); foreach (var box in boxes) baseline.Add(box);
            if (localUpdate) removalsSinceCompact += pendingLocalRemovals;
            else { removalsSinceCompact = 0; fullCompactBoxCount = boxes.Count; }
            pendingLocal = null; pendingLocalRemovals = 0;
            unchanged.Clear(); retained.Clear(); missing.Clear();
        }

        /// <summary>
        /// Cut newly removed cells from the last synchronized integer box partition.
        /// Returns false without changing components when no valid local baseline exists or a conservative
        /// recompact budget is reached; caller uses full CollisionBoxes. Pass the returned list directly to
        /// Synchronize so local updates cannot accidentally reset that budget. Untouched boxes remain exact.
        /// </summary>
        public bool TryRemoveCells(IReadOnlyList<Vector3Int> removedPositions, float cellSize, out List<Bounds> boxes)
        {
            boxes = null;
            if (removedPositions == null || removedPositions.Count == 0 ||
                removedPositions.Count > MaximumLocalRemovals - removalsSinceCompact ||
                !Finite(cellSize) || cellSize <= 0 || baseline.Count == 0) return false;
            var partition = new List<CellBox>(baseline.Count + removedPositions.Count * 5);
            foreach (var bounds in baseline)
            {
                if (!TryDecode(bounds, cellSize, out var box)) return false;
                partition.Add(box);
            }
            removed.Clear();
            foreach (var position in removedPositions)
            {
                if (!removed.Add(position)) continue;
                int index = -1;
                for (int i = 0; i < partition.Count; i++) if (partition[i].Contains(position)) { index = i; break; }
                if (index < 0) return false;
                var source = partition[index]; partition.RemoveAt(index);
                // Ordered slabs partition B\{p}: X exterior, then Y exterior in p's X plane,
                // then Z exterior in p's X/Y line. No slab interiors overlap.
                Add(partition, source.min, new Vector3Int(position.x - 1, source.max.y, source.max.z));
                Add(partition, new Vector3Int(position.x + 1, source.min.y, source.min.z), source.max);
                Add(partition, new Vector3Int(position.x, source.min.y, source.min.z), new Vector3Int(position.x, position.y - 1, source.max.z));
                Add(partition, new Vector3Int(position.x, position.y + 1, source.min.z), new Vector3Int(position.x, source.max.y, source.max.z));
                Add(partition, new Vector3Int(position.x, position.y, source.min.z), new Vector3Int(position.x, position.y, position.z - 1));
                Add(partition, new Vector3Int(position.x, position.y, position.z + 1), new Vector3Int(position.x, position.y, source.max.z));
            }
            if (partition.Count > Math.Max(128L, fullCompactBoxCount * 2L)) return false;
            boxes = new List<Bounds>(partition.Count);
            foreach (var box in partition) boxes.Add(box.ToBounds(cellSize));
            pendingLocal = boxes; pendingLocalRemovals = removed.Count;
            return true;
        }

        public void SetEnabled(bool enabled) { foreach (var collider in active) if (collider) collider.enabled = enabled; }
        /// <summary>Disable and forget every component; optionally destroy through the owner's normal lifetime policy.</summary>
        public void Clear(Action<UnityEngine.Object> release = null)
        {
            foreach (var collider in active) if (collider) { collider.enabled = false; release?.Invoke(collider); }
            foreach (var collider in pool) if (collider) { collider.enabled = false; release?.Invoke(collider); }
            active.Clear(); next.Clear(); pool.Clear(); baseline.Clear(); missing.Clear(); unchanged.Clear(); retained.Clear(); removed.Clear(); bodyOwner = null;
            pendingLocal = null; removalsSinceCompact = fullCompactBoxCount = pendingLocalRemovals = 0;
        }

        struct CellBox
        {
            public Vector3Int min, max;
            public bool Contains(Vector3Int p) => p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y && p.z >= min.z && p.z <= max.z;
            public Bounds ToBounds(float size)
            {
                var dimensions = new Vector3(max.x - min.x + 1, max.y - min.y + 1, max.z - min.z + 1);
                return new Bounds(((Vector3)min + (dimensions - Vector3.one) * .5f) * size, dimensions * size);
            }
        }
        static void Add(List<CellBox> boxes, Vector3Int min, Vector3Int max)
        {
            if (min.x <= max.x && min.y <= max.y && min.z <= max.z) boxes.Add(new CellBox { min = min, max = max });
        }
        static bool TryDecode(Bounds bounds, float size, out CellBox box)
        {
            box = default;
            if (!Axis(bounds.center.x, bounds.size.x, size, out int minX, out int maxX) ||
                !Axis(bounds.center.y, bounds.size.y, size, out int minY, out int maxY) ||
                !Axis(bounds.center.z, bounds.size.z, size, out int minZ, out int maxZ)) return false;
            box = new CellBox { min = new Vector3Int(minX, minY, minZ), max = new Vector3Int(maxX, maxY, maxZ) };
            // Reject non-grid boxes rather than rounding a hole closed. Bounds uses exact component equality.
            return box.ToBounds(size).Equals(bounds);
        }
        static bool Axis(float center, float length, float size, out int min, out int max)
        {
            min = max = 0;
            if (!Finite(center) || !Finite(length) || length <= 0) return false;
            double count = Math.Round((double)length / size);
            if (count < 1 || count > 200001) return false;
            double origin = Math.Round((double)center / size - (count - 1) * .5);
            double end = origin + count - 1;
            if (origin < -100000 || end > 100000) return false;
            min = (int)origin; max = (int)end; return true;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
