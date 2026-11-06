// Range115 authoring approximation: local clipped patches, up to0.22cell symmetric boundary fitting.
// This independent sampler intentionally permits small silhouette simplification; it is not conservative.
// Mining station / round 2: conservative component sampling.
//
// One fragment of `public static class Range115FittedSampling`; it has no scene, asset,
// material, mesh or gameplay side effects. Inputs are a component-local triangle mesh
// (already transformed and roughly centred) and the output is an in-memory list of
// Starframe.Maps.VoxelCell values.
//
// Surface sampling is conservative: every cell whose volume is touched by a triangle is
// kept, including thin sheets. Cells exactly on a triangle's boundary are assigned to the
// solid side only, so an axis-aligned box does not inflate to a 2x2x2 shell. `fillInterior`
// uses a 6-connected outside flood fill; an open shell (for example a station breach) lets
// the outside flood leak in, so it never seals the breach.
//
// Boundary shapes are deliberately limited to Cube, Slab and Wedge. The project's other
// six hull shapes were checked: Block5/Block6 are bevelled full cubes (their solid is a
// rounded cube, not a half-space cut), Block7 is a two-cell half-height slab and Block8 is
// an eight-cell cube. Using Block5/Block6 as random corner cuts would create small seam
// gaps and would not be a conservative approximation, so this sampler keeps Cube for those
// cases. Slab/Wedge are only chosen when the sampled surface is behind the cell centre and
// every clipped surface point stays on the retained side of the cut. Otherwise Cube is kept.
//
// Hard budgets fail loudly (InvalidOperationException); no cell is silently discarded.
//
// Assumptions / limits:
//  - Triangle winding is outward (the project's own hull geometry and the Unity meshes
//    inspected for this task use that convention). For closed meshes with fillInterior:true
//    a negative signed volume flips the shape-side test; open shells rely on authored winding.
//  - fillInterior is intended for closed rock geometry. It uses a dense flood grid; a very
//    large open or hollow component can exceed SamplingMaxFillGridCells and will throw.
//  - The sampler does not read UnityEngine.Mesh, does not save assets and does not run in
//    the background. The caller owns component splitting and asset creation.

using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Maps;

namespace Starframe.EditorTools
{
    public static class Range115FittedSampling
    {
        // ------------------------------------------------------------------ public API

        /// <summary>Default output cap; kept here so callers can name the same number.</summary>
        public const int SamplingDefaultMaxCells = 16000;

        /// <summary>Hard cap on triangle/AABB cell tests in one Voxelize call.</summary>
        public const int SamplingMaxTriangleCellTests = 4000000;

        /// <summary>Hard cap on the dense grid used by the fillInterior flood fill.</summary>
        public const int SamplingMaxFillGridCells = 8000000;

        /// <summary>
        /// Sample a component-local triangle mesh into hull cells.
        ///
        /// Every cell touched by a triangle is kept (thin walls included). When
        /// <paramref name="fillInterior"/> is true, cells enclosed by the surface shell are
        /// filled using a 6-connected outside flood fill; open shells stay open. Boundary
        /// cells may become Slab or Wedge only when the sampled surface is conservatively
        /// inside the retained half of the cell; otherwise they stay Cube.
        ///
        /// Throws ArgumentException / ArgumentOutOfRangeException for malformed input and
        /// InvalidOperationException when a hard budget or <paramref name="maxCells"/> is
        /// exceeded. Cells are never silently dropped.
        /// </summary>
        public static List<VoxelCell> Voxelize(Vector3[] vertices, int[] triangles, float cellSize, bool fillInterior, int maxCells = 16000)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (vertices.Length < 3) throw new ArgumentException("At least three vertices are required.", nameof(vertices));
            if (triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new ArgumentException("Triangle indices must be a non-empty multiple of three.", nameof(triangles));
            if (!SamplingFinite(cellSize) || cellSize <= 0f)
                throw new ArgumentOutOfRangeException(nameof(cellSize), "cellSize must be finite and greater than zero.");
            if (maxCells < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCells), "maxCells must be at least one.");

            // Validate vertices and find the component bounds before any grid work.
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                if (!SamplingFinite(vertex.x) || !SamplingFinite(vertex.y) || !SamplingFinite(vertex.z))
                    throw new ArgumentException("Vertex " + i + " contains a non-finite coordinate.", nameof(vertices));
                if (vertex.x < minX) minX = vertex.x;
                if (vertex.y < minY) minY = vertex.y;
                if (vertex.z < minZ) minZ = vertex.z;
                if (vertex.x > maxX) maxX = vertex.x;
                if (vertex.y > maxY) maxY = vertex.y;
                if (vertex.z > maxZ) maxZ = vertex.z;
            }

            double largestCoordinate = Math.Max(
                Math.Max(Math.Max(Math.Abs((double)minX), Math.Abs((double)maxX)), Math.Max(Math.Abs((double)minY), Math.Abs((double)maxY))),
                Math.Max(Math.Abs((double)minZ), Math.Abs((double)maxZ)));
            if (largestCoordinate / cellSize > 10000000.0)
                throw new ArgumentOutOfRangeException(nameof(cellSize),
                    "The mesh is too large for this cellSize (grid coordinates would exceed 10,000,000). Increase cellSize or split the component.");

            for (int t = 0; t < triangles.Length; t++)
            {
                int index = triangles[t];
                if (index < 0 || index >= vertices.Length)
                    throw new ArgumentException("Triangle index " + (t / 3) + " references vertex " + index + " outside the vertex array.", nameof(triangles));
            }

            // Closed rock meshes can be authored with either winding. A negative signed
            // volume means the triangle normals point inward; flip the shape-side test.
            // Open station shells keep the authored winding, which is the Unity convention.
            int orientationSign = 1;
            if (fillInterior)
            {
                double volume6 = 0.0;
                for (int t = 0; t < triangles.Length; t += 3)
                {
                    Vector3 a = vertices[triangles[t]];
                    Vector3 b = vertices[triangles[t + 1]];
                    Vector3 c = vertices[triangles[t + 2]];
                    volume6 += (double)Vector3.Dot(a, Vector3.Cross(b, c));
                }
                if (volume6 < 0.0) orientationSign = -1;
            }

            // ---------------------------------------------------------- surface rasterization
            var cellTriangles = new Dictionary<Vector3Int, List<int>>();
            int triangleCellTests = 0;
            int degenerateTriangles = 0;
            float half = cellSize * 0.5f;
            float boxEpsilon = Mathf.Max(1e-6f, half * 1e-5f);
            float indexEpsilon = 1e-5f;

            for (int t = 0; t < triangles.Length; t += 3)
            {
                int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                Vector3 a = vertices[ia], b = vertices[ib], c = vertices[ic];
                Vector3 normal = Vector3.Cross(b - a, c - a) * orientationSign;
                float area2 = normal.magnitude;
                if (area2 <= 1e-12f)
                {
                    degenerateTriangles++;
                    continue;
                }

                float loX = Mathf.Min(a.x, Mathf.Min(b.x, c.x));
                float loY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                float loZ = Mathf.Min(a.z, Mathf.Min(b.z, c.z));
                float hiX = Mathf.Max(a.x, Mathf.Max(b.x, c.x));
                float hiY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));
                float hiZ = Mathf.Max(a.z, Mathf.Max(b.z, c.z));

                // Cell i spans [(i - 0.5), (i + 0.5)] * cellSize. Include both sides of a
                // boundary here; the exact hit test below assigns boundary contacts to the
                // solid side only.
                int ix0 = Mathf.CeilToInt(loX / cellSize - 0.5f - indexEpsilon);
                int ix1 = Mathf.FloorToInt(hiX / cellSize + 0.5f + indexEpsilon);
                int iy0 = Mathf.CeilToInt(loY / cellSize - 0.5f - indexEpsilon);
                int iy1 = Mathf.FloorToInt(hiY / cellSize + 0.5f + indexEpsilon);
                int iz0 = Mathf.CeilToInt(loZ / cellSize - 0.5f - indexEpsilon);
                int iz1 = Mathf.FloorToInt(hiZ / cellSize + 0.5f + indexEpsilon);

                long nx = (long)ix1 - ix0 + 1;
                long ny = (long)iy1 - iy0 + 1;
                long nz = (long)iz1 - iz0 + 1;
                if (nx <= 0 || ny <= 0 || nz <= 0) continue;

                // Budget the AABB tests before entering the loop, so a pathological triangle
                // fails immediately instead of running for a long time.
                double estimate = (double)nx * (double)ny * (double)nz;
                if (estimate > (double)(SamplingMaxTriangleCellTests - triangleCellTests))
                    throw new InvalidOperationException(
                        "Voxelization would exceed the " + SamplingMaxTriangleCellTests.ToString() +
                        " triangle-cell test budget (triangle " + (t / 3) + " alone needs about " +
                        estimate.ToString("F0") + "). Increase cellSize or split the component. No cells were dropped.");
                triangleCellTests += (int)estimate;

                for (int z = iz0; z <= iz1; z++)
                {
                    for (int y = iy0; y <= iy1; y++)
                    {
                        for (int x = ix0; x <= ix1; x++)
                        {
                            Vector3 center = new Vector3(x * cellSize, y * cellSize, z * cellSize);
                            SamplingBoxHit hit = SamplingTriangleBoxHit(a, b, c, normal, center, half, boxEpsilon);
                            if (hit == SamplingBoxHit.None) continue;

                            // A triangle lying exactly in a cell face is a zero-thickness
                            // contact. Keep only the cell on the solid (opposite-normal) side;
                            // the empty-side cell is not part of the surface shell. A contact
                            // that only touches an edge or corner has no face area and must not
                            // thicken an axis-aligned box into a shell, so require a positive
                            // clipped patch on that solid-side cell.
                            if (hit == SamplingBoxHit.Boundary)
                            {
                                if (Vector3.Dot(normal, center - a) >= 0f) continue;
                                Vector3[] patch = SamplingClipTriangleToBox(a, b, c, center, half);
                                if (patch == null || SamplingPolygonArea(patch) <= 1e-9f * cellSize * cellSize) continue;
                            }

                            var key = new Vector3Int(x, y, z);
                            if (!cellTriangles.TryGetValue(key, out List<int> list))
                            {
                                if (cellTriangles.Count >= maxCells)
                                    throw new InvalidOperationException(
                                        "Voxelization would produce more than " + maxCells +
                                        " surface cells. Increase cellSize or split the component. No cells were dropped.");
                                list = new List<int>(4);
                                cellTriangles.Add(key, list);
                            }
                            list.Add(t);
                        }
                    }
                }
            }

            if (cellTriangles.Count == 0)
            {
                if (degenerateTriangles == triangles.Length / 3)
                    throw new ArgumentException("All triangles are degenerate; there is no surface to sample.", nameof(triangles));
                throw new InvalidOperationException("No cell intersects the supplied triangles. Check coordinates and cellSize.");
            }

            // ---------------------------------------------------------- cells + boundary shapes
            var surfaceCells = new List<VoxelCell>(cellTriangles.Count);
            foreach (KeyValuePair<Vector3Int, List<int>> pair in cellTriangles)
            {
                var cell = new VoxelCell
                {
                    position = pair.Key,
                    surface = 0,
                    shape = BlockShape.Cube,
                    quarterTurns = Vector3Int.zero,
                    modelOffset = Vector3Int.zero
                };
                Vector3 center = new Vector3(pair.Key.x * cellSize, pair.Key.y * cellSize, pair.Key.z * cellSize);
                if (SamplingTryChooseBoundaryShape(pair.Value, triangles, vertices, center, cellSize, orientationSign,
                        out BlockShape shape, out Vector3Int quarterTurns))
                {
                    cell.shape = shape;
                    cell.quarterTurns = quarterTurns;
                }
                surfaceCells.Add(cell);
            }

            // ---------------------------------------------------------- interior fill
            if (!fillInterior)
            {
                surfaceCells.Sort((left, right) => SamplingComparePositions(left.position, right.position));
                return surfaceCells;
            }

            int minGridX = int.MaxValue, minGridY = int.MaxValue, minGridZ = int.MaxValue;
            int maxGridX = int.MinValue, maxGridY = int.MinValue, maxGridZ = int.MinValue;
            foreach (VoxelCell cell in surfaceCells)
            {
                int x = cell.position.x, y = cell.position.y, z = cell.position.z;
                if (x < minGridX) minGridX = x;
                if (y < minGridY) minGridY = y;
                if (z < minGridZ) minGridZ = z;
                if (x > maxGridX) maxGridX = x;
                if (y > maxGridY) maxGridY = y;
                if (z > maxGridZ) maxGridZ = z;
            }

            // One empty cell on every side guarantees that the exterior flood always has a
            // connected boundary layer, even for a shell that touches its own bounds.
            minGridX--; minGridY--; minGridZ--;
            maxGridX++; maxGridY++; maxGridZ++;

            long sizeX = (long)maxGridX - minGridX + 1;
            long sizeY = (long)maxGridY - minGridY + 1;
            long sizeZ = (long)maxGridZ - minGridZ + 1;
            if (sizeX <= 0 || sizeY <= 0 || sizeZ <= 0)
                throw new InvalidOperationException("Invalid interior fill bounds.");
            double gridVolume = (double)sizeX * (double)sizeY * (double)sizeZ;
            if (gridVolume > (double)SamplingMaxFillGridCells)
                throw new InvalidOperationException(
                    "fillInterior would need a " + gridVolume.ToString("F0") + " cell flood grid, above the " +
                    SamplingMaxFillGridCells.ToString() + " cell budget. Increase cellSize, split the component, or pass fillInterior:false. No cells were dropped.");

            int gridSizeX = (int)sizeX, gridSizeY = (int)sizeY, gridSizeZ = (int)sizeZ;
            int gridVolumeInt = gridSizeX * gridSizeY * gridSizeZ;
            var state = new byte[gridVolumeInt]; // 0 unknown, 1 surface, 2 exterior
            foreach (VoxelCell cell in surfaceCells)
            {
                int lx = cell.position.x - minGridX;
                int ly = cell.position.y - minGridY;
                int lz = cell.position.z - minGridZ;
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

            var result = new List<VoxelCell>(surfaceCells.Count + 16);
            result.AddRange(surfaceCells);
            for (int z = 0; z < gridSizeZ; z++)
            {
                for (int y = 0; y < gridSizeY; y++)
                {
                    int row = (z * gridSizeY + y) * gridSizeX;
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        int index = row + x;
                        if (state[index] != 0) continue;
                        if ((long)result.Count >= maxCells)
                            throw new InvalidOperationException(
                                "Voxelization plus fillInterior would exceed the " + maxCells +
                                " cell cap. Increase cellSize, split the component, or pass fillInterior:false. No cells were dropped.");
                        result.Add(new VoxelCell
                        {
                            position = new Vector3Int(x + minGridX, y + minGridY, z + minGridZ),
                            surface = 0,
                            shape = BlockShape.Cube,
                            quarterTurns = Vector3Int.zero,
                            modelOffset = Vector3Int.zero
                        });
                    }
                }
            }

            result.Sort((left, right) => SamplingComparePositions(left.position, right.position));
            return result;
        }

        /// <summary>
        /// Split cells into face-connected (6-neighbour) groups. Input is not modified.
        /// Groups are ordered largest first, then by their lowest cell position; cells inside
        /// each group are ordered by position. Duplicate positions throw, because silently
        /// merging two cells would hide a data error.
        /// </summary>
        public static List<List<VoxelCell>> ConnectedGroups(List<VoxelCell> cells)
        {
            if (cells == null) throw new ArgumentNullException(nameof(cells));

            var byPosition = new Dictionary<Vector3Int, VoxelCell>(cells.Count);
            foreach (VoxelCell cell in cells)
            {
                if (cell == null) throw new ArgumentException("The cell list contains a null entry.", nameof(cells));
                if (byPosition.ContainsKey(cell.position))
                    throw new ArgumentException("Duplicate cell position " + cell.position + ".", nameof(cells));
                byPosition.Add(cell.position, cell);
            }

            var ordered = new List<Vector3Int>(byPosition.Keys);
            ordered.Sort(SamplingComparePositions);

            var visited = new HashSet<Vector3Int>();
            var groups = new List<List<VoxelCell>>();
            var queue = new Queue<Vector3Int>();
            foreach (Vector3Int start in ordered)
            {
                if (visited.Contains(start)) continue;

                var group = new List<VoxelCell>();
                visited.Add(start);
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    Vector3Int current = queue.Dequeue();
                    group.Add(byPosition[current]);
                    for (int d = 0; d < SamplingNeighborDirections.Length; d++)
                    {
                        Vector3Int neighbor = current + SamplingNeighborDirections[d];
                        if (byPosition.ContainsKey(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
                    }
                }

                group.Sort((left, right) => SamplingComparePositions(left.position, right.position));
                groups.Add(group);
            }

            groups.Sort((left, right) =>
            {
                int size = right.Count.CompareTo(left.Count);
                return size != 0 ? size : SamplingComparePositions(left[0].position, right[0].position);
            });
            return groups;
        }

        // ------------------------------------------------------------------ geometry helpers

        enum SamplingBoxHit { None, Boundary, Interior }

        static readonly Vector3Int[] SamplingNeighborDirections =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back
        };

        static bool SamplingFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static int SamplingComparePositions(Vector3Int left, Vector3Int right)
        {
            int compare = left.x.CompareTo(right.x);
            if (compare != 0) return compare;
            compare = left.y.CompareTo(right.y);
            if (compare != 0) return compare;
            return left.z.CompareTo(right.z);
        }

        /// <summary>
        /// Triangle / axis-aligned box overlap. The box is centred on <paramref name="center"/>
        /// with half-extent <paramref name="half"/>. Returns Interior for a volume overlap and
        /// Boundary for a contact that is coplanar with a face (or within epsilon of it).
        /// </summary>
        static SamplingBoxHit SamplingTriangleBoxHit(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector3 center, float half, float epsilon)
        {
            float h = half + epsilon;
            Vector3 v0 = a - center;
            Vector3 v1 = b - center;
            Vector3 v2 = c - center;

            if (Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > h || Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -h) return SamplingBoxHit.None;
            if (Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > h || Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -h) return SamplingBoxHit.None;
            if (Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > h || Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -h) return SamplingBoxHit.None;

            Vector3 e0 = v1 - v0;
            Vector3 e1 = v2 - v1;
            Vector3 e2 = v0 - v2;

            if (SamplingAxisSeparates(Vector3.Cross(e0, Vector3.right), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e0, Vector3.up), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e0, Vector3.forward), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e1, Vector3.right), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e1, Vector3.up), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e1, Vector3.forward), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e2, Vector3.right), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e2, Vector3.up), v0, v1, v2, h)) return SamplingBoxHit.None;
            if (SamplingAxisSeparates(Vector3.Cross(e2, Vector3.forward), v0, v1, v2, h)) return SamplingBoxHit.None;

            float planeDistance = Vector3.Dot(normal, v0);
            float planeRadius = half * (Mathf.Abs(normal.x) + Mathf.Abs(normal.y) + Mathf.Abs(normal.z));
            if (Mathf.Abs(planeDistance) > planeRadius + epsilon) return SamplingBoxHit.None;
            return Mathf.Abs(planeDistance) >= planeRadius - epsilon ? SamplingBoxHit.Boundary : SamplingBoxHit.Interior;
        }

        static bool SamplingAxisSeparates(Vector3 axis, Vector3 v0, Vector3 v1, Vector3 v2, float boxRadius)
        {
            float p0 = Vector3.Dot(axis, v0);
            float p1 = Vector3.Dot(axis, v1);
            float p2 = Vector3.Dot(axis, v2);
            float radius = boxRadius * (Mathf.Abs(axis.x) + Mathf.Abs(axis.y) + Mathf.Abs(axis.z));
            return Mathf.Min(p0, Mathf.Min(p1, p2)) > radius || Mathf.Max(p0, Mathf.Max(p1, p2)) < -radius;
        }

        // ------------------------------------------------------------------ boundary shapes

        struct SamplingRotation
        {
            public Quaternion rotation;
            public Vector3Int quarterTurns;
        }

        struct SamplingCutCandidate
        {
            public BlockShape shape;
            public Vector3Int quarterTurns;
            public Vector3 normal; // removed (empty) side of the half-space cut
        }

        static readonly SamplingCutCandidate[] SamplingShapeCandidates = SamplingBuildShapeCandidates();

        /// <summary>
        /// Choose Slab/Wedge only when the clipped surface is behind the cell centre and every
        /// clipped point is on the retained side of the cut. The cut plane passes through the
        /// cell centre, so the cell is at most over-filled; it never removes sampled surface.
        /// </summary>
        static bool SamplingTryChooseBoundaryShape(
            List<int> triangleIndices,
            int[] triangles,
            Vector3[] vertices,
            Vector3 center,
            float cellSize,
            int orientationSign,
            out BlockShape shape,
            out Vector3Int quarterTurns)
        {
            shape = BlockShape.Cube;
            quarterTurns = Vector3Int.zero;
            if (triangleIndices == null || triangleIndices.Count == 0) return false;

            float inverseSize = 1f / cellSize;
            double areaSum = 0.0;
            double normalX = 0.0, normalY = 0.0, normalZ = 0.0;
            double centroidX = 0.0, centroidY = 0.0, centroidZ = 0.0;

            for (int i = 0; i < triangleIndices.Count; i++)
            {
                int t = triangleIndices[i];
                Vector3 a = (vertices[triangles[t]] - center) * inverseSize;
                Vector3 b = (vertices[triangles[t + 1]] - center) * inverseSize;
                Vector3 c = (vertices[triangles[t + 2]] - center) * inverseSize;
                var patch=SamplingClipTriangleToUnitCell(a,b,c);
                if(patch==null||patch.Length<3)continue;
                for(int corner=1;corner+1<patch.Length;corner++){
                    var pa=patch[0];var pb=patch[corner];var pc=patch[corner+1];
                    Vector3 cross=Vector3.Cross(pb-pa,pc-pa);float area2=cross.magnitude;
                    if(area2<=1e-12f)continue;
                    areaSum+=area2;normalX+=cross.x*orientationSign;normalY+=cross.y*orientationSign;normalZ+=cross.z*orientationSign;
                    centroidX+=(pa.x+pb.x+pc.x)*(area2/3.0);centroidY+=(pa.y+pb.y+pc.y)*(area2/3.0);centroidZ+=(pa.z+pb.z+pc.z)*(area2/3.0);
                }
            }

            if (areaSum <= 1e-12)
            {
                shape = BlockShape.Cube;
                return false;
            }

            double normalLength = Math.Sqrt(normalX * normalX + normalY * normalY + normalZ * normalZ);
            // Require one coherent local surface direction. A concave corner, a thin two-sided
            // wall, or a fan of opposing facets must not be cut by a half-cell shape.
            if (normalLength < areaSum * 0.65) return false;

            Vector3 normal = new Vector3(
                (float)(normalX / normalLength),
                (float)(normalY / normalLength),
                (float)(normalZ / normalLength));
            Vector3 centroid = new Vector3(
                (float)(centroidX / areaSum),
                (float)(centroidY / areaSum),
                (float)(centroidZ / areaSum));
            float offset = Vector3.Dot(normal, centroid);

            // A half-cell cut at the centre can only be conservative when the sampled surface
            // is at or behind the centre. In front of it, a cut would remove real surface.
            // Behind the centre the shape merely over-fills less than a cube would, so any
            // negative offset is safe; the clipped-point test below is the hard guard.
            if (offset > 0.22f) return false;

            var polygons = new List<Vector3[]>(triangleIndices.Count);
            for (int i = 0; i < triangleIndices.Count; i++)
            {
                int t = triangleIndices[i];
                Vector3 a = (vertices[triangles[t]] - center) * inverseSize;
                Vector3 b = (vertices[triangles[t + 1]] - center) * inverseSize;
                Vector3 c = (vertices[triangles[t + 2]] - center) * inverseSize;
                Vector3[] polygon = SamplingClipTriangleToUnitCell(a, b, c);
                if (polygon != null && polygon.Length >= 3) polygons.Add(polygon);
            }
            if (polygons.Count == 0) return false;

            bool found = false;
            float bestScore = float.NegativeInfinity;
            SamplingCutCandidate best = default(SamplingCutCandidate);

            for (int candidateIndex = 0; candidateIndex < SamplingShapeCandidates.Length; candidateIndex++)
            {
                SamplingCutCandidate candidate = SamplingShapeCandidates[candidateIndex];
                float alignment = Vector3.Dot(candidate.normal, normal);
                // The cut plane must be essentially parallel to the sampled surface. An oblique
                // "diagonal" cut can contain the local surface patch while shaving a solid
                // corner elsewhere in the cell; rejecting that case is what keeps a half-cell
                // shape from hollowing a member.
                if (alignment < 0.82f) continue;

                float cutOffset = Vector3.Dot(candidate.normal, centroid);
                if (cutOffset > 0.22f) continue;

                bool valid = true;
                double squaredDistance = 0.0;
                int pointCount = 0;
                for (int polygonIndex = 0; polygonIndex < polygons.Count && valid; polygonIndex++)
                {
                    Vector3[] polygon = polygons[polygonIndex];
                    for (int pointIndex = 0; pointIndex < polygon.Length; pointIndex++)
                    {
                        float distance = Vector3.Dot(candidate.normal, polygon[pointIndex]);
                        if (distance > 0.22f)
                        {
                            valid = false;
                            break;
                        }
                        squaredDistance += distance * distance;
                        pointCount++;
                    }
                }
                if (!valid || pointCount == 0) continue;

                float rms = Mathf.Sqrt((float)(squaredDistance / pointCount));
                if (rms > 0.65f) continue;

                // Alignment is the primary fit; closeness and roughness break ties.
                float score = alignment * 10f - Mathf.Abs(cutOffset) - rms * 0.5f;
                if (candidate.shape == BlockShape.Slab) score -= 0.01f; // prefer the richer Wedge on an exact tie
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    found = true;
                }
            }

            if (!found) return false;
            shape = best.shape;
            quarterTurns = best.quarterTurns;
            return true;
        }

        static Vector3[] SamplingClipTriangleToUnitCell(Vector3 a, Vector3 b, Vector3 c)
        {
            return SamplingClipTriangleToBox(a, b, c, Vector3.zero, 0.5f);
        }

        static Vector3[] SamplingClipTriangleToBox(Vector3 a, Vector3 b, Vector3 c, Vector3 center, float half)
        {
            var polygon = new List<Vector3>(3) { a - center, b - center, c - center };
            polygon = SamplingClipAgainstPlane(polygon, Vector3.right, half);
            polygon = SamplingClipAgainstPlane(polygon, Vector3.left, half);
            polygon = SamplingClipAgainstPlane(polygon, Vector3.up, half);
            polygon = SamplingClipAgainstPlane(polygon, Vector3.down, half);
            polygon = SamplingClipAgainstPlane(polygon, Vector3.forward, half);
            polygon = SamplingClipAgainstPlane(polygon, Vector3.back, half);
            return polygon.Count >= 3 ? polygon.ToArray() : null;
        }

        static float SamplingPolygonArea(Vector3[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return 0f;
            Vector3 sum = Vector3.zero;
            for (int i = 1; i < polygon.Length - 1; i++)
                sum += Vector3.Cross(polygon[i] - polygon[0], polygon[i + 1] - polygon[0]);
            return sum.magnitude * 0.5f;
        }

        /// <summary>Keep points with dot(normal, point) &lt;= limit.</summary>
        static List<Vector3> SamplingClipAgainstPlane(List<Vector3> input, Vector3 normal, float limit)
        {
            var output = new List<Vector3>(input.Count + 2);
            if (input.Count == 0) return output;
            for (int i = 0; i < input.Count; i++)
            {
                Vector3 current = input[i];
                Vector3 previous = input[(i + input.Count - 1) % input.Count];
                float currentDistance = Vector3.Dot(normal, current) - limit;
                float previousDistance = Vector3.Dot(normal, previous) - limit;
                bool currentInside = currentDistance <= 0f;
                bool previousInside = previousDistance <= 0f;

                if (currentInside)
                {
                    if (!previousInside)
                    {
                        float denominator = previousDistance - currentDistance;
                        if (Mathf.Abs(denominator) > 1e-12f)
                            output.Add(previous + (current - previous) * (previousDistance / denominator));
                    }
                    output.Add(current);
                }
                else if (previousInside)
                {
                    float denominator = previousDistance - currentDistance;
                    if (Mathf.Abs(denominator) > 1e-12f)
                        output.Add(previous + (current - previous) * (previousDistance / denominator));
                }
            }
            return output;
        }

        // ------------------------------------------------------------------ quarter-turn candidates

        static SamplingCutCandidate[] SamplingBuildShapeCandidates()
        {
            List<SamplingRotation> rotations = SamplingBuildRotations();
            var candidates = new List<SamplingCutCandidate>(18);

            Vector3[] slabRemoved =
            {
                Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back
            };
            for (int i = 0; i < slabRemoved.Length; i++)
            {
                SamplingRotation rotation = SamplingNearestRotation(rotations, Vector3.up, slabRemoved[i]);
                candidates.Add(new SamplingCutCandidate
                {
                    shape = BlockShape.Slab,
                    quarterTurns = rotation.quarterTurns,
                    normal = slabRemoved[i]
                });
            }

            // BlockGeometry.WedgePoints is a right triangular prism: y <= z. Its removed-side
            // normal is (0, 1, -1) / sqrt(2). Rotating that normal to each diagonal direction
            // gives the 12 distinct half-cell wedge solids.
            Vector3 wedgeRemoved = new Vector3(0f, 1f, -1f).normalized;
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    for (int z = -1; z <= 1; z++)
                    {
                        int nonZero = (x != 0 ? 1 : 0) + (y != 0 ? 1 : 0) + (z != 0 ? 1 : 0);
                        if (nonZero != 2) continue;
                        Vector3 target = new Vector3(x, y, z).normalized;
                        SamplingRotation rotation = SamplingNearestRotation(rotations, wedgeRemoved, target);
                        candidates.Add(new SamplingCutCandidate
                        {
                            shape = BlockShape.Wedge,
                            quarterTurns = rotation.quarterTurns,
                            normal = target
                        });
                    }
                }
            }

            return candidates.ToArray();
        }

        static SamplingRotation SamplingNearestRotation(List<SamplingRotation> rotations, Vector3 from, Vector3 to)
        {
            float bestDot = float.NegativeInfinity;
            SamplingRotation best = default(SamplingRotation);
            for (int i = 0; i < rotations.Count; i++)
            {
                SamplingRotation candidate = rotations[i];
                float dot = Vector3.Dot(candidate.rotation * from, to);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = candidate;
                }
            }
            if (bestDot < 0.999f)
                throw new InvalidOperationException("Could not build the 90-degree hull-shape rotations.");
            return best;
        }

        static List<SamplingRotation> SamplingBuildRotations()
        {
            var rotations = new List<SamplingRotation>(24);
            for (int x = 0; x < 4; x++)
            {
                for (int y = 0; y < 4; y++)
                {
                    for (int z = 0; z < 4; z++)
                    {
                        Quaternion rotation = Quaternion.Euler(x * 90f, y * 90f, z * 90f);
                        Vector3 right = rotation * Vector3.right;
                        Vector3 up = rotation * Vector3.up;
                        Vector3 forward = rotation * Vector3.forward;

                        bool duplicate = false;
                        for (int i = 0; i < rotations.Count; i++)
                        {
                            Quaternion existing = rotations[i].rotation;
                            if (Vector3.Dot(right, existing * Vector3.right) > 0.9999f &&
                                Vector3.Dot(up, existing * Vector3.up) > 0.9999f &&
                                Vector3.Dot(forward, existing * Vector3.forward) > 0.9999f)
                            {
                                duplicate = true;
                                break;
                            }
                        }
                        if (duplicate) continue;
                        rotations.Add(new SamplingRotation
                        {
                            rotation = rotation,
                            quarterTurns = new Vector3Int(x, y, z)
                        });
                    }
                }
            }
            if (rotations.Count != 24)
                throw new InvalidOperationException("Expected 24 unique 90-degree rotations, got " + rotations.Count + ".");
            return rotations;
        }
    }
}

