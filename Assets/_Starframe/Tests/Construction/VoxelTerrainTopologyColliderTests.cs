using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Starframe.Maps;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Starframe.Tests
{
    public sealed class VoxelTerrainTopologyColliderTests
    {
        readonly List<GameObject> owners = new List<GameObject>();
        [TearDown] public void Cleanup() { foreach (var owner in owners) if (owner) Object.DestroyImmediate(owner); owners.Clear(); }
        GameObject Owner() { var owner = new GameObject("Terrain helper fixture"); owners.Add(owner); return owner; }
        static void Check(bool condition, string message) => Assert.That(condition, Is.True, message);
        static Dictionary<Vector3Int, VoxelCellState> Cells(IEnumerable<Vector3Int> positions) =>
            positions.ToDictionary(p => p, p => new VoxelCellState { position = p });
        static IEnumerable<Vector3Int> Grid(int width, int height, int depth, Vector3Int offset)
        {
            for (int y = 0; y < height; y++) for (int z = 0; z < depth; z++) for (int x = 0; x < width; x++)
                yield return offset + new Vector3Int(x, y, z);
        }
        static string Canonical(List<List<VoxelCellState>> groups) => string.Join("|", groups.Select(group =>
            string.Join(";", group.Select(cell => cell.position).OrderBy(p => p.x).ThenBy(p => p.y).ThenBy(p => p.z).Select(p => p.ToString())))
            .OrderBy(group => group));
        static List<List<VoxelCellState>> CheckResolve(VoxelTerrainTopology topology,
            Dictionary<Vector3Int, VoxelCellState> cells, IReadOnlyList<Vector3Int> removed)
        {
            var oracle = VoxelGeometry.Islands(cells); var result = topology.Resolve(cells, removed);
            if (oracle.Count <= 1) Check(result == null, "A connected survivor must return the null certificate.");
            else Check(result != null && Canonical(result) == Canonical(oracle), "Island membership differs from the full oracle.");
            return oracle;
        }

        [Test] public void UnknownAndInitiallyDisconnectedBodiesUseTheFullOracle()
        {
            var cells = Cells(new[] { new Vector3Int(-4, 0, 0), new Vector3Int(-3, 0, 0), new Vector3Int(4, 0, 0), new Vector3Int(5, 0, 0) });
            var topology = new VoxelTerrainTopology();
            Check(!topology.Reset(cells), "Initial disconnected assets must not acquire the connected invariant.");
            var removed = new[] { new Vector3Int(-4, 0, 0) }; cells[removed[0]].removed = true;
            CheckResolve(topology, cells, removed);
            CheckResolve(new VoxelTerrainTopology(), cells, removed);
        }
        [Test] public void FinalBoundaryHandlesParallelTwoCellBridgeAndLongAlternateRoute()
        {
            var bridge = Cells(Grid(7, 2, 1, new Vector3Int(-3, 0, 0)));
            var topology = new VoxelTerrainTopology(); Check(topology.Reset(bridge), "Bridge fixture starts connected.");
            var cuts = new[] { Vector3Int.zero, Vector3Int.up };
            foreach (var p in cuts) bridge[p].removed = true;
            Check(CheckResolve(topology, bridge, cuts).Count == 2, "Both bridge cells removed together must produce two islands.");
            var ring = Cells(Grid(21, 5, 1, new Vector3Int(-10, -2, -1)).Where(p => Math.Abs(p.x) == 10 || Math.Abs(p.y) == 2));
            Check(topology.Reset(ring), "Ring fixture starts connected.");
            cuts = new[] { new Vector3Int(0, -2, -1) }; ring[cuts[0]].removed = true;
            Check(CheckResolve(topology, ring, cuts).Count == 1, "A distant bypass must not be mistaken for a local split.");
        }
        [Test] public void EverySmallOccupancyAndSingleDeletionAgreesWithIslandsOracle()
        {
            var positions = Grid(3, 2, 2, new Vector3Int(-2, -1, -2)).ToArray();
            for (int mask = 1; mask < (1 << positions.Length); mask++)
            {
                var cells = Cells(positions.Where((p, index) => (mask & (1 << index)) != 0));
                foreach (var p in cells.Keys.ToArray())
                {
                    var topology = new VoxelTerrainTopology(); topology.Reset(cells); cells[p].removed = true;
                    CheckResolve(topology, cells, new[] { p }); cells[p].removed = false;
                }
            }
        }
        [Test] public void RandomTwoHundredDeletionsAndOwnershipTransfersAgreeWithOracle()
        {
            for (int seed = 0; seed < 4; seed++)
            {
                var random = new System.Random(26107 + seed);
                var cells = Cells(Grid(10, 10, 10, new Vector3Int(-7, -6, -5)));
                var topology = new VoxelTerrainTopology(); topology.Reset(cells);
                for (int deleted = 0; deleted < 200;)
                {
                    var alive = cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key).OrderBy(p => random.Next()).ToArray();
                    int count = Math.Min(Math.Min(random.Next(1, 5), 200 - deleted), alive.Length);
                    Check(count > 0, "The main component should survive all 200 deletions.");
                    var removed = alive.Take(count).ToArray(); foreach (var p in removed) cells[p].removed = true;
                    var oracle = CheckResolve(topology, cells, removed); deleted += count;
                    if (oracle.Count > 1)
                    {
                        var keep = new HashSet<Vector3Int>(oracle[0].Select(cell => cell.position));
                        foreach (var p in cells.Keys.ToArray()) if (cells[p].Alive && !keep.Contains(p)) cells.Remove(p);
                        topology.MarkConnected();
                    }
                }
            }
        }

        static void CheckCoverage(IReadOnlyList<Bounds> boxes, Dictionary<Vector3Int, VoxelCellState> cells, float size)
        {
            var covered = new HashSet<Vector3Int>();
            foreach (var box in boxes)
            {
                int width = Mathf.RoundToInt(box.size.x / size), height = Mathf.RoundToInt(box.size.y / size), depth = Mathf.RoundToInt(box.size.z / size);
                Check(width > 0 && height > 0 && depth > 0, "Every collision box has positive integer volume.");
                var origin = new Vector3Int(Mathf.RoundToInt(box.center.x / size - (width - 1) * .5f),
                    Mathf.RoundToInt(box.center.y / size - (height - 1) * .5f), Mathf.RoundToInt(box.center.z / size - (depth - 1) * .5f));
                foreach (var p in Grid(width, height, depth, origin))
                {
                    Check(cells.TryGetValue(p, out var state) && state.Alive, "A collision box fills an empty or destroyed cell: " + p);
                    Check(covered.Add(p), "Collision boxes overlap inside a cell: " + p);
                }
            }
            Check(covered.SetEquals(cells.Where(pair => pair.Value.Alive).Select(pair => pair.Key)), "Every live cell is covered exactly once.");
        }
        [Test] public void LocalSlabsHaveExactCoverageAndRetainEveryUnchangedComponent()
        {
            foreach (float size in new[] { .25f, 1f, 10f, .3f, 1.7f })
            {
                var cells = Cells(Grid(5, 3, 2, new Vector3Int(-4, -1, -2)).Concat(new[] { new Vector3Int(9, 0, 0) }));
                var helper = new VoxelTerrainColliders(); var owner = Owner();
                helper.Synchronize(owner, VoxelGeometry.CollisionBoxes(cells, size), true);
                var old = helper.Active.ToDictionary(c => new Bounds(c.center, c.size), c => c);
                var removed = new[] { new Vector3Int(-2, 0, -1) }; cells[removed[0]].removed = true;
                Check(helper.TryRemoveCells(removed, size, out var boxes), "Valid generated integer boxes should support a local cut.");
                CheckCoverage(boxes, cells, size); helper.Synchronize(owner, boxes, true);
                foreach (var collider in helper.Active)
                    if (old.TryGetValue(new Bounds(collider.center, collider.size), out var previous))
                        Check(ReferenceEquals(collider, previous), "An unchanged box must retain its BoxCollider instance.");
                var same = helper.Active.ToArray(); helper.Synchronize(owner, boxes, true);
                Check(helper.Active.All(c => same.Contains(c)), "Identical synchronization must not churn components.");
                helper.Clear(); Check(helper.Count == 0 && owner.GetComponents<BoxCollider>().All(c => !c.enabled), "Clear disables active and pooled colliders.");
            }
        }
        [Test] public void RecompactBudgetRejectsWithoutMutationAndResetsAfterFullSynchronization()
        {
            var cells = Cells(Grid(130, 1, 1, new Vector3Int(-65, 0, 0))); var owner = Owner(); var helper = new VoxelTerrainColliders();
            helper.Synchronize(owner, VoxelGeometry.CollisionBoxes(cells, 1), true);
            for (int i = 0; i < 64; i++)
            {
                var removed = new[] { new Vector3Int(i - 65, 0, 0) }; cells[removed[0]].removed = true;
                Check(helper.TryRemoveCells(removed, 1, out var local), "The first 64 endpoint removals stay within budget."); helper.Synchronize(owner, local, true);
            }
            var before = helper.Active.ToArray(); var bounds = before.Select(c => new Bounds(c.center, c.size)).ToArray();
            var cut = new[] { new Vector3Int(-1, 0, 0) }; cells[cut[0]].removed = true;
            Check(!helper.TryRemoveCells(cut, 1, out var rejected) && rejected == null, "The 65th removal requests a full recompact.");
            Check(helper.Active.SequenceEqual(before) && helper.Active.Select(c => new Bounds(c.center, c.size)).SequenceEqual(bounds) && helper.Active.All(c => c.enabled), "A rejected local update changes no components.");
            helper.Synchronize(owner, VoxelGeometry.CollisionBoxes(cells, 1), true);
            cut = new[] { Vector3Int.zero }; cells[cut[0]].removed = true;
            Check(helper.TryRemoveCells(cut, 1, out var resumed), "A full compact must reset the local budget."); CheckCoverage(resumed, cells, 1);
        }
        [Test] public void TwoHundredInteriorHolesRecompactWithoutCoverageLossOrUnboundedComponentChurn()
        {
            var random = new System.Random(26107); var cells = Cells(Grid(10, 10, 10, new Vector3Int(-5, -5, -5)));
            var order = cells.Keys.OrderBy(p => random.Next()).Take(200).ToArray();
            var helper = new VoxelTerrainColliders(); var owner = Owner(); helper.Synchronize(owner, VoxelGeometry.CollisionBoxes(cells, 1), true);
            int compact = 0, local = 0, peak = helper.Count, localAfterCompact = 0;
            foreach (var p in order)
            {
                cells[p].removed = true;
                if (helper.TryRemoveCells(new[] { p }, 1, out var boxes)) { local++; if (compact > 0) localAfterCompact++; }
                else { compact++; boxes = VoxelGeometry.CollisionBoxes(cells, 1); }
                CheckCoverage(boxes, cells, 1); helper.Synchronize(owner, boxes, true); peak = Math.Max(peak, helper.Count);
            }
            Check(local + compact == 200 && compact > 0 && localAfterCompact > 0, "Long-running holes must recompact and then resume local edits.");
            Check(owner.GetComponents<BoxCollider>().Length == peak, "Component pooling should allocate only to the peak active count.");
        }
        [Test] public void InvalidBaselineAndMissingCellFallBackWithoutComponentChanges()
        {
            var helper = new VoxelTerrainColliders(); var owner = Owner();
            helper.Synchronize(owner, new[] { new Bounds(new Vector3(.1f, 0, 0), Vector3.one) }, true);
            var collider = helper.Active[0]; var before = new Bounds(collider.center, collider.size);
            Check(!helper.TryRemoveCells(new[] { Vector3Int.zero }, 1, out var result), "A non-grid baseline must fall back.");
            Check(result == null && ReferenceEquals(collider, helper.Active[0]) && before.Equals(new Bounds(collider.center, collider.size)) && collider.enabled, "Invalid baselines leave components untouched.");
            helper.Synchronize(owner, new[] { new Bounds(Vector3.zero, Vector3.one) }, true);
            Check(!helper.TryRemoveCells(new[] { new Vector3Int(8, 0, 0) }, 1, out result), "An absent cell must fall back, not remove another cell.");
        }
    }
}
