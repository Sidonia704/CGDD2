// Mining station / round 2: solid-cell packing and enclosed-air verification.
//
// One fragment of `public static partial class MiningRound2`; it does not touch scenes,
// assets, materials or Sampling.cs. It only mutates the supplied in-memory cell list.
//
// PackLargeBlocks turns every complete, disjoint 2x2x2 group of plain Cube cells into the
// project's Block8 hull shape. Block8 is a real 2x2x2 hull (BlockModules.LocalCells returns
// eight cells), and VoxelModelPartition expects the per-cell modelOffset in that local
// zero-based cell space. Therefore all eight VoxelCells stay in the list, each keeps its
// position/surface, gets shape=Block8, quarterTurns=zero and modelOffset=position-anchor.
// The anchor is the minimum corner of the group. No cell is removed and no anchor-only cell
// is left behind.
//
// The greedy is deterministic: candidates are all valid 2x2x2 Cube groups, sorted by
// (covered exterior faces desc, exposed face directions asc, anchor lexicographic). That
// prefers interior and flat/planar groups and never consumes Slab/Wedge cells. Accepted
// groups are disjoint. The return value is the number of packed groups.
//
// EnclosedAirCells is a read-only acceptance check: it expands the occupied bounds by one
// cell of padding, floods exterior air through 6-neighbours, and returns the number of
// unvisited air cells. A framework with a through-hole leaks and reports zero; a sealed
// cavity reports its interior air count. It never fills or mutates the input.
//
// Hard budget: EnclosedAirCells throws InvalidOperationException when the one-cell-padded
// flood grid would exceed SolidPackingMaxAirGridCells (8,000,000). There is no silent
// truncation.
//
// Risk (not fixed by this file): Voxelize(fillInterior:true) relies on the triangle shell
// enclosing a region. An open tank/deck shell with a gap lets the exterior flood enter and
// can therefore yield no fill (or a partial fill) even though the part is meant to be solid.
// That is a construction/call-site decision: do not use fillInterior:true for an open shell
// as a substitute for a closed solid mesh. This packer does not change Sampling.cs.

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Maps;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        // ------------------------------------------------------------------ public API

        /// <summary>Hard cap for the padded flood grid used by <see cref="EnclosedAirCells"/>.</summary>
        public const int SolidPackingMaxAirGridCells = 8000000;

        /// <summary>
        /// Pack complete 2x2x2 Cube groups into Block8 in place. Every one of the eight cells
        /// remains present; only shape, quarterTurns and modelOffset change. Groups containing
        /// Slab/Wedge or an incomplete cube are untouched. Returns the number of groups packed.
        /// </summary>
        public static int PackLargeBlocks(List<VoxelCell> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Count == 0) return 0;

            var byPosition = new Dictionary<Vector3Int, VoxelCell>(cells.Count);
            var cubePositions = new HashSet<Vector3Int>();
            var occupied = new HashSet<Vector3Int>();
            foreach (VoxelCell cell in cells)
            {
                if (cell == null) throw new ArgumentException("The cell list contains a null entry.", nameof(cells));
                if (byPosition.ContainsKey(cell.position))
                    throw new ArgumentException("Duplicate cell position " + cell.position + ".", nameof(cells));
                byPosition.Add(cell.position, cell);
                occupied.Add(cell.position);

                // A single-cell Cube must not carry a partition offset. Cubes are rotationally
                // symmetric, so an existing quarterTurns value is harmless and will be reset.
                if (cell.shape == BlockShape.Cube && cell.modelOffset == Vector3Int.zero)
                    cubePositions.Add(cell.position);
            }

            if (cubePositions.Count < 8) return 0;

            var candidates = new List<SolidPackingCandidate>(cubePositions.Count);
            foreach (Vector3Int anchor in cubePositions)
            {
                if (!SolidPackingHasFullBlock8(cubePositions, anchor)) continue;
                SolidPackingScoreCandidate(occupied, anchor, out int coveredFaces, out int exposedDirections);
                candidates.Add(new SolidPackingCandidate
                {
                    anchor = anchor,
                    coveredFaces = coveredFaces,
                    exposedDirections = exposedDirections,
                    score = coveredFaces * 100 - exposedDirections * 10
                });
            }
            if (candidates.Count == 0) return 0;

            var packed = new HashSet<Vector3Int>();
            int groups = 0;

            // Pass 1: a fixed 2-cell lattice phase gives disjoint 2x2x2 groups. Pick the phase
            // with the most groups (ties: more covered faces = more interior, then lower phase)
            // so an ordinary solid box tiles completely instead of one greedy interior group
            // blocking every neighbour.
            int bestPhase = 0;
            int bestCount = -1;
            long bestCovered = long.MinValue;
            for (int phase = 0; phase < 8; phase++)
            {
                int phaseX = phase & 1;
                int phaseY = (phase >> 1) & 1;
                int phaseZ = (phase >> 2) & 1;
                int count = 0;
                long covered = 0;
                foreach (SolidPackingCandidate candidate in candidates)
                {
                    if ((candidate.anchor.x & 1) != phaseX ||
                        (candidate.anchor.y & 1) != phaseY ||
                        (candidate.anchor.z & 1) != phaseZ) continue;
                    count++;
                    covered += candidate.coveredFaces;
                }

                if (count > bestCount || (count == bestCount && covered > bestCovered))
                {
                    bestPhase = phase;
                    bestCount = count;
                    bestCovered = covered;
                }
            }

            if (bestCount > 0)
            {
                int phaseX = bestPhase & 1;
                int phaseY = (bestPhase >> 1) & 1;
                int phaseZ = (bestPhase >> 2) & 1;
                foreach (SolidPackingCandidate candidate in candidates)
                {
                    if ((candidate.anchor.x & 1) != phaseX ||
                        (candidate.anchor.y & 1) != phaseY ||
                        (candidate.anchor.z & 1) != phaseZ) continue;
                    SolidPackingPackGroup(byPosition, packed, candidate.anchor);
                    groups++;
                }
            }

            // Pass 2: fill remaining gaps with the best interior/flat candidates. Packing only
            // removes cells, so one sorted pass over the original candidate list is enough.
            candidates.Sort((left, right) =>
            {
                int score = right.score.CompareTo(left.score);
                return score != 0 ? score : SolidPackingComparePositions(left.anchor, right.anchor);
            });
            foreach (SolidPackingCandidate candidate in candidates)
            {
                if (SolidPackingOverlapsPacked(packed, candidate.anchor)) continue;
                SolidPackingPackGroup(byPosition, packed, candidate.anchor);
                groups++;
            }
            return groups;
        }

        /// <summary>
        /// Count air cells enclosed by the supplied occupied cells. Bounds are expanded by one
        /// cell of padding and exterior air is flooded through 6-neighbours; unvisited air is
        /// the enclosed count. The input is not changed. Throws when the padded grid would
        /// exceed <see cref="SolidPackingMaxAirGridCells"/>.
        /// </summary>
        public static int EnclosedAirCells(List<VoxelCell> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));
            if (cells.Count == 0) return 0;

            var occupied = new HashSet<Vector3Int>();
            foreach (VoxelCell cell in cells)
            {
                if (cell == null) throw new ArgumentException("The cell list contains a null entry.", nameof(cells));
                if (!occupied.Add(cell.position))
                    throw new ArgumentException("Duplicate cell position " + cell.position + ".", nameof(cells));
            }

            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
            foreach (Vector3Int position in occupied)
            {
                if (position.x < minX) minX = position.x;
                if (position.y < minY) minY = position.y;
                if (position.z < minZ) minZ = position.z;
                if (position.x > maxX) maxX = position.x;
                if (position.y > maxY) maxY = position.y;
                if (position.z > maxZ) maxZ = position.z;
            }

            // One cell of padding on every side, exactly as the acceptance check describes.
            minX--; minY--; minZ--;
            maxX++; maxY++; maxZ++;

            long sizeX = (long)maxX - minX + 1;
            long sizeY = (long)maxY - minY + 1;
            long sizeZ = (long)maxZ - minZ + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0) return 0;

            double gridVolume = (double)sizeX * (double)sizeY * (double)sizeZ;
            if (gridVolume > (double)SolidPackingMaxAirGridCells)
                throw new InvalidOperationException(
                    "EnclosedAirCells would need a " + gridVolume.ToString("F0") + " cell flood grid, above the " +
                    SolidPackingMaxAirGridCells.ToString() + " cell budget. Split the component or use a larger cell size. No cells were changed.");

            int gridSizeX = (int)sizeX, gridSizeY = (int)sizeY, gridSizeZ = (int)sizeZ;
            int gridVolumeInt = gridSizeX * gridSizeY * gridSizeZ;
            var state = new byte[gridVolumeInt]; // 0 unknown air, 1 occupied, 2 exterior
            foreach (Vector3Int position in occupied)
            {
                int lx = position.x - minX;
                int ly = position.y - minY;
                int lz = position.z - minZ;
                state[(lz * gridSizeY + ly) * gridSizeX + lx] = 1;
            }

            int[] queue = new int[gridVolumeInt];
            int head = 0, tail = 0;
            for (int z = 0; z < gridSizeZ; z++)
            {
                bool zEdge = z == 0 || z == gridSizeZ - 1;
                for (int y = 0; y < gridSizeY; y++)
                {
                    bool yEdge = y == 0 || y == gridSizeY - 1;
                    int row = (z * gridSizeY + y) * gridSizeX;
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        if (!zEdge && !yEdge && x != 0 && x != gridSizeX - 1) continue;
                        int index = row + x;
                        if (state[index] != 0) continue;
                        state[index] = 2;
                        queue[tail++] = index;
                    }
                }
            }

            int strideY = gridSizeX;
            int strideZ = gridSizeX * gridSizeY;
            while (head < tail)
            {
                int index = queue[head++];
                int z = index / strideZ;
                int remainder = index - z * strideZ;
                int y = remainder / strideY;
                int x = remainder - y * strideY;

                if (x > 0)
                {
                    int neighbor = index - 1;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
                if (x + 1 < gridSizeX)
                {
                    int neighbor = index + 1;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
                if (y > 0)
                {
                    int neighbor = index - strideY;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
                if (y + 1 < gridSizeY)
                {
                    int neighbor = index + strideY;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
                if (z > 0)
                {
                    int neighbor = index - strideZ;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
                if (z + 1 < gridSizeZ)
                {
                    int neighbor = index + strideZ;
                    if (state[neighbor] == 0) { state[neighbor] = 2; queue[tail++] = neighbor; }
                }
            }

            int enclosed = 0;
            for (int i = 0; i < state.Length; i++)
                if (state[i] == 0) enclosed++;
            return enclosed;
        }

        // ------------------------------------------------------------------ greedy helpers

        struct SolidPackingCandidate
        {
            public Vector3Int anchor;
            public int coveredFaces;
            public int exposedDirections;
            public int score;
        }

        static readonly Vector3Int[] SolidPackingDirections =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back
        };

        static bool SolidPackingHasFullBlock8(HashSet<Vector3Int> cubePositions, Vector3Int anchor)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        if (!cubePositions.Contains(new Vector3Int(anchor.x + dx, anchor.y + dy, anchor.z + dz)))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// coveredFaces counts exterior faces of the 2x2x2 group that touch another occupied
        /// cell (higher = more interior). exposedDirections counts the world directions that
        /// still see air (lower = flatter / more plane-like).
        /// </summary>
        static void SolidPackingScoreCandidate(HashSet<Vector3Int> occupied, Vector3Int anchor, out int coveredFaces, out int exposedDirections)
        {
            coveredFaces = 0;
            int exposedMask = 0;
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        for (int face = 0; face < SolidPackingDirections.Length; face++)
                        {
                            Vector3Int direction = SolidPackingDirections[face];
                            int nx = dx + direction.x;
                            int ny = dy + direction.y;
                            int nz = dz + direction.z;
                            if (nx >= 0 && nx <= 1 && ny >= 0 && ny <= 1 && nz >= 0 && nz <= 1)
                                continue; // internal face of the 2x2x2 group

                            var neighbor = new Vector3Int(anchor.x + nx, anchor.y + ny, anchor.z + nz);
                            if (occupied.Contains(neighbor)) coveredFaces++;
                            else exposedMask |= 1 << face;
                        }
                    }
                }
            }

            exposedDirections = 0;
            while (exposedMask != 0)
            {
                if ((exposedMask & 1) != 0) exposedDirections++;
                exposedMask >>= 1;
            }
        }

        static bool SolidPackingOverlapsPacked(HashSet<Vector3Int> packed, Vector3Int anchor)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        if (packed.Contains(new Vector3Int(anchor.x + dx, anchor.y + dy, anchor.z + dz)))
                            return true;
                    }
                }
            }
            return false;
        }

        static void SolidPackingPackGroup(Dictionary<Vector3Int, VoxelCell> byPosition, HashSet<Vector3Int> packed, Vector3Int anchor)
        {
            for (int dx = 0; dx <= 1; dx++)
            {
                for (int dy = 0; dy <= 1; dy++)
                {
                    for (int dz = 0; dz <= 1; dz++)
                    {
                        var offset = new Vector3Int(dx, dy, dz);
                        Vector3Int position = anchor + offset;
                        VoxelCell cell = byPosition[position];
                        cell.shape = BlockShape.Block8;
                        cell.quarterTurns = Vector3Int.zero; // required by the large-model partition
                        cell.modelOffset = offset;             // position - anchor
                        packed.Add(position);
                    }
                }
            }
        }

        static int SolidPackingComparePositions(Vector3Int left, Vector3Int right)
        {
            int compare = left.x.CompareTo(right.x);
            if (compare != 0) return compare;
            compare = left.y.CompareTo(right.y);
            if (compare != 0) return compare;
            return left.z.CompareTo(right.z);
        }
    }
}
