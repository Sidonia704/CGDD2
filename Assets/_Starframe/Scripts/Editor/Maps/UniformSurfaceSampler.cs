// @role Rasterize native facility mesh surfaces onto a fixed one-unit grid without moving their pivot.
// @calls VoxelCell
// @tune none
// Surface-only authoring: preserves openings and thin sheets; never flood-fills mesh interiors.
using System;
using System.Collections.Generic;
using UnityEngine;
using Starframe.Construction;
using Starframe.Maps;

namespace Starframe.EditorTools
{
    public static class UniformSurfaceSampler
    {
        /// <summary>Rasterize every triangle submesh at its original pivot, after uniform scaling.
        /// The grid spacing is always one. Cap overflow throws; it never coarsens or truncates.</summary>
        public static List<VoxelCell> Build(Mesh mesh, float scale, int cap)
        {
            if (mesh == null) throw new ArgumentNullException(nameof(mesh));
            if (!SamplingFinite(scale) || scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] *= scale;
            var triangles = new List<int>();
            var surfaces = new List<int>();
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                if (mesh.GetTopology(submesh) != UnityEngine.MeshTopology.Triangles)
                    throw new ArgumentException("Only triangle submeshes can be surface sampled.", nameof(mesh));
                var indices = mesh.GetTriangles(submesh);
                triangles.AddRange(indices);
                // Native cache slots encode palette channel * ShapeCount + source hull shape.
                for (int i = 0; i < indices.Length; i += 3) surfaces.Add(submesh / VoxelHullAppearance.ShapeCount);
            }
            return Build(vertices, triangles.ToArray(), surfaces.ToArray(), cap);
        }

        /// <summary>Vector-array equivalent. Triangle surfaces contains one material index per triangle.</summary>
        public static List<VoxelCell> Build(Vector3[] vertices, int[] triangles, int[] triangleSurfaces, int cap)
        {
            if (vertices == null) throw new ArgumentNullException(nameof(vertices));
            if (triangles == null) throw new ArgumentNullException(nameof(triangles));
            if (triangles.Length == 0 || triangles.Length % 3 != 0)
                throw new ArgumentException("Expected nonempty triangle indices.", nameof(triangles));
            if (triangleSurfaces == null || triangleSurfaces.Length != triangles.Length / 3)
                throw new ArgumentException("Expected one surface index per triangle.", nameof(triangleSurfaces));
            if (cap < 1) throw new ArgumentOutOfRangeException(nameof(cap));
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = vertices[i];
                if (!SamplingFinite(v.x) || !SamplingFinite(v.y) || !SamplingFinite(v.z) ||
                    Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))) > 10000000f)
                    throw new ArgumentException("Vertex is non-finite or exceeds the supported integer grid.", nameof(vertices));
            }
            for (int i = 0; i < triangles.Length; i++)
                if (triangles[i] < 0 || triangles[i] >= vertices.Length)
                    throw new ArgumentException("Triangle index is outside the vertex array.", nameof(triangles));

            var cellTriangles = new Dictionary<Vector3Int, List<int>>();
            const float half = 0.5f, epsilon = 0.00001f;
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Vector3 a = vertices[triangles[t]], b = vertices[triangles[t + 1]], c = vertices[triangles[t + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude <= 1e-24f) continue;
                Vector3 normal = cross.normalized;
                Vector3 absolute = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
                int depth = absolute.x >= absolute.y && absolute.x >= absolute.z ? 0 : absolute.y >= absolute.z ? 1 : 2;
                int u = (depth + 1) % 3, v = (depth + 2) % 3;
                int u0 = Mathf.CeilToInt(Mathf.Min(a[u], Mathf.Min(b[u], c[u])) - half - epsilon);
                int u1 = Mathf.FloorToInt(Mathf.Max(a[u], Mathf.Max(b[u], c[u])) + half + epsilon);
                int v0 = Mathf.CeilToInt(Mathf.Min(a[v], Mathf.Min(b[v], c[v])) - half - epsilon);
                int v1 = Mathf.FloorToInt(Mathf.Max(a[v], Mathf.Max(b[v], c[v])) + half + epsilon);
                int d0 = Mathf.CeilToInt(Mathf.Min(a[depth], Mathf.Min(b[depth], c[depth])) - half - epsilon);
                int d1 = Mathf.FloorToInt(Mathf.Max(a[depth], Mathf.Max(b[depth], c[depth])) + half + epsilon);
                double slopeU = -(double)normal[u] / normal[depth];
                double slopeV = -(double)normal[v] / normal[depth];
                // At most 1.5 cells: the two projected half-extents plus this axis' half extent.
                // This expanded range also includes edge cases where a plane crosses a voxel corner.
                double radius = half * (1.0 + Math.Abs(slopeU) + Math.Abs(slopeV));
                for (int y = v0; y <= v1; y++)
                {
                    for (int x = u0; x <= u1; x++)
                    {
                        double z = a[depth] + slopeU * (x - (double)a[u]) + slopeV * (y - (double)a[v]);
                        int lo = Math.Max(d0, (int)Math.Ceiling(z - radius - epsilon));
                        int hi = Math.Min(d1, (int)Math.Floor(z + radius + epsilon));
                        for (int d = lo; d <= hi; d++)
                        {
                            Vector3Int key = Vector3Int.zero;
                            key[u] = x; key[v] = y; key[depth] = d;
                            Vector3 center = (Vector3)key;
                            var hit = SamplingTriangleBoxHit(a, b, c, normal, center, half, epsilon);
                            if (hit == SamplingBoxHit.None) continue;
                            if (hit == SamplingBoxHit.Boundary && Vector3.Dot(normal, center - a) >= 0f) continue;
                            // SAT may accept a triangle touching only an edge/corner even when
                            // its plane crosses the box interior. Such zero-area contact is not shell.
                            var patch = SamplingClipTriangleToBox(a, b, c, center, half);
                            if (patch == null || SamplingPolygonArea(patch) <= 1e-9f) continue;
                            if (!cellTriangles.TryGetValue(key, out List<int> list))
                            {
                                if (cellTriangles.Count >= cap)
                                    throw new InvalidOperationException("Uniform surface exceeds " + cap + " cells; fixed one-unit spacing was retained.");
                                list = new List<int>(2);
                                cellTriangles.Add(key, list);
                            }
                            list.Add(t);
                        }
                    }
                }
            }
            if (cellTriangles.Count == 0) throw new ArgumentException("Mesh has no nondegenerate sampled surface.", nameof(triangles));
            var result = new List<VoxelCell>(cellTriangles.Count);
            foreach (var pair in cellTriangles)
            {
                Vector3 center = (Vector3)pair.Key;
                var cell = new VoxelCell { position = pair.Key, shape = BlockShape.Cube, surface = 0,
                    quarterTurns = Vector3Int.zero, modelOffset = Vector3Int.zero };
                if (SamplingTryChooseBoundaryShape(pair.Value, triangles, vertices, center, 1f, 1,
                    out BlockShape shape, out Vector3Int turns))
                { cell.shape = shape; cell.quarterTurns = turns; }
                // Choose the material with the largest clipped local patch. Adjacent submesh
                // triangles still all contribute to shape fitting and shell connectivity.
                cell.surface = triangleSurfaces[pair.Value[0] / 3];
                bool mixed = false;
                for (int i = 1; i < pair.Value.Count; i++)
                    if (triangleSurfaces[pair.Value[i] / 3] != cell.surface) { mixed = true; break; }
                if (mixed)
                {
                    var areas = new Dictionary<int, float>();
                    foreach (int t in pair.Value)
                    {
                        int surface = triangleSurfaces[t / 3];
                        float area = SamplingPolygonArea(SamplingClipTriangleToBox(vertices[triangles[t]],
                            vertices[triangles[t + 1]], vertices[triangles[t + 2]], center, half));
                        areas.TryGetValue(surface, out float prior);
                        areas[surface] = prior + area;
                    }
                    float best = -1f;
                    foreach (var area in areas)
                        if (area.Value > best || (area.Value == best && area.Key < cell.surface))
                        { best = area.Value; cell.surface = area.Key; }
                }
                result.Add(cell);
            }
            result.Sort((left, right) => SamplingComparePositions(left.position, right.position));
            return result;
        }

        // ------------------------------------------------------------------ geometry helpers

        enum SamplingBoxHit { None, Boundary, Interior }

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
        /// Choose a coherent local Slab/Wedge fit using clipped patches and authored winding.
        /// The boundary fit permits the original sampler's 0.22-unit symmetric tolerance;
        /// opposing faces of thin members remain full cubes rather than cancelling into a cut.
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

            // Preserve the approved local fitted-sampling tolerance at fixed unit spacing.
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

