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
            var removed = new[] { new Vector3Int(-4, 0, 0) }; cells.Remove(removed[0]);
            CheckResolve(topology, cells, removed);
            CheckResolve(new VoxelTerrainTopology(), cells, removed);
        }
        [Test] public void FinalBoundaryHandlesParallelTwoCellBridgeAndLongAlternateRoute()
        {
            var bridge = Cells(Grid(7, 2, 1, new Vector3Int(-3, 0, 0)));
            var topology = new VoxelTerrainTopology(); Check(topology.Reset(bridge), "Bridge fixture starts connected.");
            var cuts = new[] { Vector3Int.zero, Vector3Int.up };
            foreach (var p in cuts) bridge.Remove(p);
            Check(CheckResolve(topology, bridge, cuts).Count == 2, "Both bridge cells removed together must produce two islands.");
            var ring = Cells(Grid(21, 5, 1, new Vector3Int(-10, -2, -1)).Where(p => Math.Abs(p.x) == 10 || Math.Abs(p.y) == 2));
            Check(topology.Reset(ring), "Ring fixture starts connected.");
            cuts = new[] { new Vector3Int(0, -2, -1) }; ring.Remove(cuts[0]);
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
                    var topology = new VoxelTerrainTopology(); topology.Reset(cells); var saved=cells[p]; cells.Remove(p);
                    CheckResolve(topology, cells, new[] { p }); cells[p]=saved;
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
                    var removed = alive.Take(count).ToArray(); foreach (var p in removed) cells.Remove(p);
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




    }
}
