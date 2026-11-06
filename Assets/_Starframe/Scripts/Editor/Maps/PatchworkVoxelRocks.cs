// @role Build one-unit native rock shells from an irregular faceted core, unequal shoulders and connected fault terraces.
// @calls BlockGeometry, VoxelCell
// @tune PatchDepth:int:1-3:Each fault step; four to eight steps accumulate to a broad local relief.
// @tune PatchWidth:float:0.08-0.18:Fault width as a fraction of the rock short diameter.

using System;
using System.Collections.Generic;
using System.Linq;
using Starframe.Construction;
using Starframe.Maps;
using UnityEngine;

namespace Starframe.EditorTools
{
    /// <summary>
    /// Authoring-only CSG made entirely from integer-lattice boxes and 45-degree prisms.
    /// Enumerates convex interval endpoints instead of allocating a filled rock. The final
    /// cells form the actual native collision/render shell, rather than a separate visual skin.
    /// All surface variation belongs to a few connected patches; no cell consumes random state.
    /// </summary>
    public static class PatchworkVoxelRocks
    {
        const sbyte Empty = -2, Cube = -1;
        const float Epsilon = .0001f;
        struct Plane { public Vector3Int normal; public float limit; }
        sealed class Primitive
        {
            public Vector3Int minimum, maximum;
            public readonly List<Plane> planes = new List<Plane>();
        }
        struct RandomState
        {
            public uint state;
            public float Next() { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return (state & 0x00ffffff) / 16777216f; }
            public int Range(int minimum, int maximum) { return minimum + Mathf.Min(maximum - minimum - 1, (int)(Next() * (maximum - minimum))); }
        }
        sealed class Field
        {
            public Vector3Int minimum, maximum;
            public Vector3Int[] caps;
            public readonly List<Primitive> positive = new List<Primitive>(), negative = new List<Primitive>();
            public readonly Dictionary<Vector3Int, sbyte> cache = new Dictionary<Vector3Int, sbyte>();

            public sbyte Sample(Vector3Int position)
            {
                if(caps!=null&&(position.x<minimum.x||position.y<minimum.y||position.z<minimum.z||position.x>maximum.x||position.y>maximum.y||position.z>maximum.z))return Empty;
                if (cache.TryGetValue(position, out var saved)) return saved;
                sbyte result = Empty;
                foreach (var primitive in positive)
                {
                    sbyte fragment = PrimitiveCell(primitive, position);
                    if (fragment == Empty) continue;
                    foreach (var chip in negative)
                    {
                        fragment = Subtract(fragment, position, chip);
                        if (fragment == Empty) break;
                    }
                    if (fragment == Empty) continue;
                    if (fragment == Cube) { result = Cube; break; }
                    if (result == Empty) result = fragment;
                    // Only complementary native half-cells justify a full cube. Other partial
                    // unions keep a contained main-mass wedge, with an inward <=1-cell error.
                    else if (Normals[result] == -Normals[fragment]) { result = Cube; break; }
                }
                cache.Add(position, result);
                return result;
            }
        }

        static readonly Vector3Int[] Normals = MakeNormals();
        static readonly Vector3Int[] Rotations = MakeRotations();
        static readonly Vector3[][] WedgeVertices = MakeVertices();
        static readonly Vector3Int[] Neighbors = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back };

        public static List<VoxelCell> Build(Bounds bounds, float cell, int cap, string key, out Vector3 localOffset)
        {
            if (Mathf.Abs(cell - 1f) > Epsilon) throw new ArgumentOutOfRangeException(nameof(cell), "Patchwork rocks use native one-unit cells.");
            if (cap < 1) throw new ArgumentOutOfRangeException(nameof(cap));
            var random = new RandomState { state = Seed(key) };
            if (Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) < 4f)
                return TinyRock(bounds, cap, ref random, out localOffset);
            var translation = Vector3Int.RoundToInt(bounds.center);
            Quantize(bounds,out var minimum,out var maximum);minimum-=translation;maximum-=translation;
            var envelope=Vector3Int.Max(-minimum,maximum);
            var field = new Field{minimum=minimum,maximum=maximum};
            MakeMasses(field, envelope, ref random);
            MakePatches(field, envelope, ref random);

            var candidates = new HashSet<Vector3Int>();
            foreach (var primitive in field.positive) AddBoundaryCandidates(primitive, candidates);
            foreach (var primitive in field.negative) AddBoundaryCandidates(primitive, candidates);
            var boundary = CloseBoundary(field, candidates);
            var shell = AddBacking(field, boundary);
            ConnectShell(field, shell);
            var result = new List<VoxelCell>();
            foreach (var position in shell)
            {
                sbyte shape = field.Sample(position);
                if (shape == Empty) continue;
                result.Add(new VoxelCell { position = position, shape = shape == Cube ? BlockShape.Cube : BlockShape.Wedge,
                    quarterTurns = shape == Cube ? Vector3Int.zero : Rotations[shape] });
                if (result.Count > cap) throw new InvalidOperationException("One-unit rock shell exceeds the supplied cell budget; split it into native chunks without changing its cell size.");
            }
            if (result.Count == 0) throw new InvalidOperationException("Empty patchwork rock.");
            result.Sort((a, b) => { int z = a.position.z.CompareTo(b.position.z); if (z != 0) return z; int y = a.position.y.CompareTo(b.position.y); return y != 0 ? y : a.position.x.CompareTo(b.position.x); });
            // Keep the input pivot and its integer authoring phase. Fractional source bounds
            // can differ by at most half a native cell; no instance translation is required.
            if (translation != Vector3Int.zero)
                foreach (var voxel in result) voxel.position += translation;
            localOffset = bounds.center - Centre(result);
            return result;
        }

        public static List<VoxelCell> BuildCentered(Bounds bounds, float cell, int cap, string key)
        {
            return Build(bounds, cell, cap, key, out _);
        }

        static bool IsBoundary(Field field, Vector3Int p)
        {
            sbyte shape = field.Sample(p);
            if (shape == Empty) return false;
            if (shape != Cube) return true;
            foreach (var direction in Neighbors) if (field.Sample(p + direction) != Cube) return true;
            return false;
        }

        static HashSet<Vector3Int> CloseBoundary(Field field, HashSet<Vector3Int> seeds)
        {
            var boundary = new HashSet<Vector3Int>();
            var inspected = new HashSet<Vector3Int>(seeds);
            var pending = new Queue<Vector3Int>();
            foreach (var p in seeds) if (IsBoundary(field, p)) { boundary.Add(p); pending.Enqueue(p); }
            // Primitive endpoints are seeds, not a proof of complete CSG coverage. Follow
            // the actual native boundary around every union, notch and corner until closed.
            while (pending.Count > 0)
            {
                var p = pending.Dequeue();
                for (int x = -1; x <= 1; x++) for (int y = -1; y <= 1; y++) for (int z = -1; z <= 1; z++)
                {
                    if (x == 0 && y == 0 && z == 0) continue;
                    var q = p + new Vector3Int(x, y, z);
                    if (!inspected.Add(q) || !IsBoundary(field, q)) continue;
                    boundary.Add(q); pending.Enqueue(q);
                }
            }
            return boundary;
        }

        static HashSet<Vector3Int> AddBacking(Field field, HashSet<Vector3Int> boundary)
        {
            var shell = new HashSet<Vector3Int>(boundary);
            foreach (var p in boundary)
                foreach (var direction in Neighbors)
                {
                    var q = p + direction;
                    if (field.Sample(q) != Empty) shell.Add(q);
                }
            return shell;
        }

        static List<List<Vector3Int>> Components(HashSet<Vector3Int> cells)
        {
            var remaining = new HashSet<Vector3Int>(cells);
            var result = new List<List<Vector3Int>>();
            var pending = new Queue<Vector3Int>();
            while (remaining.Count > 0)
            {
                var start = remaining.First(); remaining.Remove(start); pending.Enqueue(start);
                var component = new List<Vector3Int>();
                while (pending.Count > 0)
                {
                    var p = pending.Dequeue(); component.Add(p);
                    // Native compound occupancy requires face connectivity; corner-only
                    // contact is not accepted as a bridge between physical pieces.
                    foreach (var direction in Neighbors)
                    { var q = p + direction; if (remaining.Remove(q)) pending.Enqueue(q); }
                }
                result.Add(component);
            }
            result.Sort((a, b) => b.Count.CompareTo(a.Count));
            return result;
        }

        static void ConnectShell(Field field, HashSet<Vector3Int> shell)
        {
            var components = Components(shell);
            if (components.Count < 2) return;
            var connected = new HashSet<Vector3Int>(components[0]);
            for (int i = 1; i < components.Count; i++)
            {
                var parents = new Dictionary<Vector3Int, Vector3Int>();
                var pending = new Queue<Vector3Int>();
                var minimum = components[i][0]; var maximum = minimum;
                foreach (var p in components[i]) { minimum = Vector3Int.Min(minimum, p); maximum = Vector3Int.Max(maximum, p); }
                minimum -= Vector3Int.one * 8; maximum += Vector3Int.one * 8;
                foreach (var p in components[i]) { parents[p] = p; pending.Enqueue(p); }
                bool found = false; var end = Vector3Int.zero;
                while (pending.Count > 0 && !found)
                {
                    var p = pending.Dequeue();
                    foreach (var direction in Neighbors)
                    {
                        var q = p + direction;
                        if (q.x < minimum.x || q.x > maximum.x || q.y < minimum.y || q.y > maximum.y || q.z < minimum.z || q.z > maximum.z) continue;
                        if (parents.ContainsKey(q) || field.Sample(q) == Empty) continue;
                        parents.Add(q, p);
                        if (parents.Count > Mathf.Max(32768, components[i].Count * 8)) throw new InvalidOperationException("Native shell bridge exceeded its bounded local search at " + components[i][0]);
                        if (connected.Contains(q)) { found = true; end = q; break; }
                        pending.Enqueue(q);
                    }
                }
                if (!found) throw new InvalidOperationException("Disconnected implicit native rock fragment at " + components[i][0] + "; no true-solid face-connected bridge exists.");
                var bridge = new List<Vector3Int>();
                while (true)
                {
                    bridge.Add(end); var previous = parents[end];
                    if (previous == end) break;
                    end = previous;
                }
                foreach (var p in components[i]) connected.Add(p);
                foreach (var p in bridge)
                {
                    shell.Add(p); connected.Add(p);
                    foreach (var direction in Neighbors)
                    { var q = p + direction; if (field.Sample(q) != Empty) { shell.Add(q); connected.Add(q); } }
                }
            }
        }

        /// <summary>Exhaustive native-boundary comparison for a bounded diagnostic rock; never used by Build.</summary>
        public static object DiagnoseBoundary(Bounds bounds, string key, int maximumVolume = 4000000)
        {
            var random = new RandomState { state = Seed(key) };
            var extent = bounds.extents;
            var e = new Vector3Int(Mathf.Max(1, Mathf.FloorToInt(extent.x - .5f)), Mathf.Max(1, Mathf.FloorToInt(extent.y - .5f)), Mathf.Max(1, Mathf.FloorToInt(extent.z - .5f)));
            Quantize(bounds,out var volumeMin,out var volumeMax);var volumeSize=volumeMax-volumeMin+Vector3Int.one;long volume = ((long)volumeSize.x+2) * ((long)volumeSize.y+2) * ((long)volumeSize.z+2);
            if (volume > maximumVolume) throw new ArgumentOutOfRangeException(nameof(maximumVolume), "Diagnostic bounding volume is " + volume);
            Quantize(bounds,out var gridMin,out var gridMax);var phaseOrigin=Vector3Int.RoundToInt(bounds.center);gridMin-=phaseOrigin;gridMax-=phaseOrigin;e=Vector3Int.Max(-gridMin,gridMax);var field = new Field{minimum=gridMin,maximum=gridMax}; MakeMasses(field, e, ref random); MakePatches(field, e, ref random);
            var seeds = new HashSet<Vector3Int>();
            foreach (var p in field.positive) AddBoundaryCandidates(p, seeds);
            foreach (var p in field.negative) AddBoundaryCandidates(p, seeds);
            var closed = CloseBoundary(field, seeds); var shell = AddBacking(field, closed);
            int solid = 0, theoreticalBoundary = 0, missedSeeds = 0, missedClosure = 0;
            var missingSeedCoordinates = new List<Vector3Int>(); var missingClosureCoordinates = new List<Vector3Int>();
            for (int x = -e.x - 1; x <= e.x + 1; x++) for (int y = -e.y - 1; y <= e.y + 1; y++) for (int z = -e.z - 1; z <= e.z + 1; z++)
            {
                var p = new Vector3Int(x, y, z);
                if (field.Sample(p) != Empty) solid++;
                if (!IsBoundary(field, p)) continue;
                theoreticalBoundary++;
                if (!seeds.Contains(p)) { missedSeeds++; if (missingSeedCoordinates.Count < 40) missingSeedCoordinates.Add(p); }
                if (!closed.Contains(p)) { missedClosure++; if (missingClosureCoordinates.Count < 40) missingClosureCoordinates.Add(p); }
            }
            var components = Components(shell);
            return new { volume, solid, theoreticalBoundary, seedCandidates = seeds.Count, closedBoundary = closed.Count, backedShell = shell.Count,
                missedSeeds, missedClosure, missingSeedCoordinates, missingClosureCoordinates, faceComponentSizes = components.Select(c => c.Count).ToArray() };
        }

        public static object DiagnoseCells(Bounds bounds, string key, Vector3Int[] coordinates)
        {
            var random = new RandomState { state = Seed(key) };
            var extent = bounds.extents;
            var e = new Vector3Int(Mathf.Max(1, Mathf.FloorToInt(extent.x - .5f)), Mathf.Max(1, Mathf.FloorToInt(extent.y - .5f)), Mathf.Max(1, Mathf.FloorToInt(extent.z - .5f)));
            Quantize(bounds,out var gridMin,out var gridMax);var phaseOrigin=Vector3Int.RoundToInt(bounds.center);gridMin-=phaseOrigin;gridMax-=phaseOrigin;e=Vector3Int.Max(-gridMin,gridMax);var field = new Field{minimum=gridMin,maximum=gridMax}; MakeMasses(field, e, ref random); MakePatches(field, e, ref random);
            var seeds = new HashSet<Vector3Int>();
            foreach (var p in field.positive) AddBoundaryCandidates(p, seeds);
            foreach (var p in field.negative) AddBoundaryCandidates(p, seeds);
            var phase = Vector3Int.RoundToInt(bounds.center);
            return coordinates.Select(coordinate =>
            {
                var p = coordinate - phase;
                bool analytic = field.positive.Any(primitive => primitive.planes.All(plane => Dot(plane.normal, p) <= plane.limit + Epsilon)) &&
                    !field.negative.Any(primitive => primitive.planes.All(plane => Dot(plane.normal, p) < plane.limit - Epsilon));
                return new { coordinate, shape = (int)field.Sample(p), analyticCentreSolid = analytic, boundary = IsBoundary(field, p), seeded = seeds.Contains(p),
                    neighborShapes = Neighbors.Select(direction => (int)field.Sample(p + direction)).ToArray(),
                    positiveFragments = field.positive.Select(primitive => (int)PrimitiveCell(primitive, p)).ToArray() };
            }).ToArray();
        }

        static void MakeMasses(Field field, Vector3Int envelope, ref RandomState random)
        {
            // Every pair of axes participates in the main shape. Twelve independently
            // offset diagonal planes prevent an axis from extruding one unchanged profile.
            var coreMinimum = field.minimum; var coreMaximum = field.maximum;
            var middle=Vector3Int.RoundToInt(((Vector3)coreMinimum+(Vector3)coreMaximum)*.5f);
            field.caps=new Vector3Int[6];
            for(int axis=0;axis<3;axis++){var p=middle;p[axis]=coreMinimum[axis];field.caps[axis*2]=p;p[axis]=coreMaximum[axis];field.caps[axis*2+1]=p;}
            var core = Box(coreMinimum, coreMaximum);
            FacetAllAxes(core, .58f, 1.02f, ref random);
            for(int index=0;index<core.planes.Count;index++){var plane=core.planes[index];float support=(Mathf.Abs(plane.normal.x)+Mathf.Abs(plane.normal.y)+Mathf.Abs(plane.normal.z))*.5f;foreach(var p in field.caps)plane.limit=Mathf.Max(plane.limit,Dot(plane.normal,p)+support);core.planes[index]=plane;}
            field.positive.Add(core);

            int shoulders = random.Range(2, 5);
            int firstAxis = random.Range(0, 3);
            for (int part = 0; part < shoulders; part++)
            {
                var centre = Vector3Int.zero; var radius = Vector3Int.zero;
                int dominant = (firstAxis + part) % 3;
                int sign = random.Next() < .5f ? -1 : 1;
                for (int axis = 0; axis < 3; axis++)
                {
                    // Centres lie inside the convex core; shoulder size, secondary drift
                    // and extremity heights vary independently instead of sharing a row.
                    float drift = axis == dominant ? sign * (.39f + random.Next() * .17f) : (random.Next() - .5f) * .60f;
                    centre[axis] = Mathf.RoundToInt(envelope[axis] * drift);
                    radius[axis] = Mathf.Max(1, Mathf.RoundToInt(envelope[axis] * (.25f + random.Next() * .17f)));
                }
                var shoulder = Box(Vector3Int.Max(centre - radius, field.minimum), Vector3Int.Min(centre + radius, field.maximum));
                FacetAllAxes(shoulder, .61f, 1.08f, ref random);
                field.positive.Add(shoulder);
            }
        }

        static void FacetAllAxes(Primitive primitive, float shallow, float deep, ref RandomState random)
        {
            for (int a = 0; a < 3; a++) for (int b = a + 1; b < 3; b++)
                for (int sa = -1; sa <= 1; sa += 2) for (int sb = -1; sb <= 1; sb += 2)
                {
                    int radiusA = Mathf.Max(1, (primitive.maximum[a] - primitive.minimum[a]) / 2);
                    int radiusB = Mathf.Max(1, (primitive.maximum[b] - primitive.minimum[b]) / 2);
                    int depth = Mathf.RoundToInt(Mathf.Min(radiusA, radiusB) * Mathf.Lerp(shallow, deep, random.Next()));
                    depth = Mathf.Clamp(depth, 1, Mathf.Max(1, radiusA + radiusB - 1));
                    AddChamfer(primitive, a, sa, b, sb, depth);
                }
        }

        static void MakePatches(Field field, Vector3Int envelope, ref RandomState random)
        {
            int baseMassCount = field.positive.Count;
            float minimumDimension = Mathf.Min(envelope.x, Mathf.Min(envelope.y, envelope.z)) * 2 + 1;
            if (minimumDimension < 8) return;
            int clusters = Mathf.Clamp(3 + Mathf.FloorToInt(minimumDimension / 55), 3, 6);
            int firstAxis = random.Range(0, 3);
            // A fault is a connected set of nested cuts. Its relief accumulates through
            // several levels; it does not repeat shallow 1/2/3-cell engraved stripes.
            for (int cluster = 0; cluster < clusters; cluster++)
            {
                int axis = (firstAxis + cluster) % 3, sign = random.Next() < .5f ? -1 : 1;
                int a = (axis + 1) % 3, b = (axis + 2) % 3;
                if (random.Next() < .5f) { int swap = a; a = b; b = swap; }
                var anchor = Vector3Int.zero;
                float surface = 0; bool found = false;
                for (int attempt = 0; attempt < 16; attempt++)
                {
                    var mass = field.positive[random.Range(0, baseMassCount)];
                    anchor[a] = Mathf.RoundToInt(Mathf.Lerp(mass.minimum[a], mass.maximum[a], .24f + random.Next() * .52f));
                    anchor[b] = Mathf.RoundToInt(Mathf.Lerp(mass.minimum[b], mass.maximum[b], .24f + random.Next() * .52f));
                    if (Surface(field, baseMassCount, axis, sign, anchor, out surface)) { found = true; break; }
                }
                if (!found) continue;
                anchor[axis] = sign > 0 ? Mathf.FloorToInt(surface + Epsilon) : Mathf.CeilToInt(surface - Epsilon);
                int width = Mathf.Max(4, Mathf.RoundToInt(minimumDimension * (.08f + random.Next() * .10f)));
                int length = Mathf.Max(8, Mathf.RoundToInt((envelope[a] * 2 + 1) * (.20f + random.Next() * .25f)));
                int targetDepth = Mathf.Clamp(Mathf.RoundToInt(minimumDimension * (.08f + random.Next() * .07f)), 4, 24);
                int bands = Mathf.Clamp(Mathf.RoundToInt(targetDepth / (1.35f + random.Next() * .80f)), 4, 8);
                bands = Mathf.Min(bands, targetDepth);
                length = Mathf.Max(length, bands + targetDepth / 2);
                int travel = random.Next() < .5f ? -1 : 1;
                int used = 0, depth = 0;
                for (int band = 0; band < bands; band++)
                {
                    int remaining = length - used;
                    int remainingBands = bands - band;
                    int step = Mathf.Clamp(Mathf.RoundToInt((targetDepth - depth) / (float)remainingBands + (random.Next() - .5f)), 1, 3);
                    step = Mathf.Clamp(step, Mathf.Max(1, targetDepth - depth - (remainingBands - 1) * 3), Mathf.Min(3, targetDepth - depth - (remainingBands - 1)));
                    depth += step;
                    int bandLength = band == bands - 1 ? remaining : Mathf.Clamp(Mathf.RoundToInt(length / (float)bands * (.65f + random.Next() * .65f)), 1, remaining - remainingBands + 1);
                    int lateral = Mathf.RoundToInt((random.Next() - .5f) * width * .25f);
                    int localWidth = Mathf.Max(4, Mathf.RoundToInt(width * (.75f + random.Next() * .30f)));
                    var minimum = anchor; var maximum = anchor;
                    int begin = anchor[a] + travel * (-length / 2 + used);
                    int end = anchor[a] + travel * (length - length / 2);
                    minimum[a] = Mathf.Min(begin, end); maximum[a] = Mathf.Max(begin, end);
                    minimum[b] = anchor[b] - localWidth / 2 + lateral;
                    maximum[b] = minimum[b] + localWidth - 1;
                    int near = anchor[axis] - sign * depth;
                    // Faults never cross the protected central cell on narrow rocks.
                    // This keeps the main body rooted even when several zones meet.
                    near = sign > 0 ? Mathf.Max(near, 1) : Mathf.Min(near, -1);
                    int far = sign > 0 ? envelope[axis] : -envelope[axis];
                    minimum[axis] = Mathf.Min(near, far); maximum[axis] = Mathf.Max(near, far);
                    minimum = Vector3Int.Max(minimum, field.minimum); maximum = Vector3Int.Min(maximum, field.maximum);
                    if (minimum.x > maximum.x || minimum.y > maximum.y || minimum.z > maximum.z) { used += bandLength; continue; }
                    if(field.caps.Any(p=>p.x>=minimum.x&&p.y>=minimum.y&&p.z>=minimum.z&&p.x<=maximum.x&&p.y<=maximum.y&&p.z<=maximum.z)){used+=bandLength;continue;}
                    var patch = Box(minimum, maximum);
                    // The common far end returns diagonally to the untouched rock. Lateral
                    // edge offsets and a shallow corner cut prevent a rectangular stairwell.
                    AddChamfer(patch, axis, -sign, a, travel, depth);
                    AddChamfer(patch, a, -travel, b, random.Next() < .5f ? -1 : 1, Mathf.Max(1, localWidth / 4));
                    field.negative.Add(patch);
                    used += bandLength;
                }
            }
        }

        static List<VoxelCell> TinyRock(Bounds bounds, int cap, ref RandomState random, out Vector3 localOffset)
        {
            Quantize(bounds,out var minimum,out var maximum);var result=new List<VoxelCell>();
            for(int x=minimum.x;x<=maximum.x;x++)for(int y=minimum.y;y<=maximum.y;y++)for(int z=minimum.z;z<=maximum.z;z++)result.Add(new VoxelCell{position=new Vector3Int(x,y,z),shape=BlockShape.Cube});
            if(result.Count>cap)throw new InvalidOperationException("Tiny rock capacity.");
            localOffset = bounds.center - Centre(result);
            return result;
        }

        static void Quantize(Bounds bounds,out Vector3Int minimum,out Vector3Int maximum)
        {
            var size=Vector3Int.Max(Vector3Int.one,Vector3Int.RoundToInt(bounds.size));
            minimum=Vector3Int.RoundToInt(bounds.center-((Vector3)size-Vector3.one)*.5f);maximum=minimum+size-Vector3Int.one;
        }

        static Primitive Box(Vector3Int minimum, Vector3Int maximum)
        {
            var primitive = new Primitive { minimum = minimum, maximum = maximum };
            for (int axis = 0; axis < 3; axis++)
            {
                var n = Vector3Int.zero; n[axis] = 1;
                primitive.planes.Add(new Plane { normal = n, limit = maximum[axis] + .5f });
                primitive.planes.Add(new Plane { normal = -n, limit = -minimum[axis] + .5f });
            }
            return primitive;
        }

        static void AddChamfer(Primitive primitive, int a, int sa, int b, int sb, int depth)
        {
            var n = Vector3Int.zero; n[a] = sa; n[b] = sb;
            int outerA = sa > 0 ? primitive.maximum[a] : -primitive.minimum[a];
            int outerB = sb > 0 ? primitive.maximum[b] : -primitive.minimum[b];
            primitive.planes.Add(new Plane { normal = n, limit = outerA + outerB - depth });
        }

        static bool Surface(Field field, int count, int axis, int sign, Vector3Int anchor, out float surface)
        {
            bool found = false; surface = sign > 0 ? float.NegativeInfinity : float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (Interval(field.positive[i], axis, anchor, out var low, out var high))
                { surface = sign > 0 ? Mathf.Max(surface, high) : Mathf.Min(surface, low); found = true; }
            return found;
        }

        static bool Interval(Primitive primitive, int axis, Vector3Int anchor, out float low, out float high)
        {
            low = primitive.minimum[axis] - .5f; high = primitive.maximum[axis] + .5f;
            foreach (var plane in primitive.planes)
            {
                int coefficient = plane.normal[axis];
                float constant = Dot(plane.normal, anchor) - coefficient * anchor[axis];
                if (coefficient == 0) { if (constant > plane.limit + Epsilon) return false; }
                else if (coefficient > 0) high = Mathf.Min(high, (plane.limit - constant) / coefficient);
                else low = Mathf.Max(low, (plane.limit - constant) / coefficient);
                if (low > high + Epsilon) return false;
            }
            return true;
        }

        static void AddBoundaryCandidates(Primitive primitive, HashSet<Vector3Int> candidates)
        {
            // Three scan directions include every facet, including near-vertical cuts.
            // Only a three-cell band is visited; the interior volume is never materialized.
            for (int axis = 0; axis < 3; axis++)
            {
                int a = (axis + 1) % 3, b = (axis + 2) % 3;
                var p = Vector3Int.zero;
                for (int u = primitive.minimum[a]; u <= primitive.maximum[a]; u++)
                    for (int v = primitive.minimum[b]; v <= primitive.maximum[b]; v++)
                    {
                        p[a] = u; p[b] = v;
                        if (!Interval(primitive, axis, p, out var low, out var high)) continue;
                        int first = Mathf.CeilToInt(low - Epsilon), last = Mathf.FloorToInt(high + Epsilon);
                        for (int layer = 0; layer < 3 && first + layer <= last; layer++)
                        {
                            p[axis] = first + layer; AddCandidate(p, candidates);
                            p[axis] = last - layer; AddCandidate(p, candidates);
                        }
                    }
            }
        }

        static void AddCandidate(Vector3Int p, HashSet<Vector3Int> candidates)
        {
            candidates.Add(p);
            // Subtractive axis-aligned cuts expose a solid cell immediately OUTSIDE
            // their primitive. Include this halo so terrace floors and walls are closed.
            foreach (var direction in Neighbors) candidates.Add(p + direction);
        }

        static sbyte PrimitiveCell(Primitive primitive, Vector3Int position)
        {
            if (!Within(position, primitive)) return Empty;
            sbyte wedge = Cube;
            foreach (var plane in primitive.planes)
            {
                float centre = Dot(plane.normal, position) - plane.limit;
                float support = (Mathf.Abs(plane.normal.x) + Mathf.Abs(plane.normal.y) + Mathf.Abs(plane.normal.z)) * .5f;
                if (centre + support <= Epsilon) continue;
                if (centre > Epsilon) return Empty;
                if (Mathf.Abs(centre) > Epsilon || support < .99f) return Empty;
                int index = Array.IndexOf(Normals, plane.normal);
                if (index < 0) return Empty;
                wedge = (sbyte)index;
            }
            if (wedge == Cube) return Cube;
            foreach (var plane in primitive.planes)
                foreach (var vertex in WedgeVertices[wedge])
                    if (Dot(plane.normal, position) + Vector3.Dot(plane.normal, vertex) > plane.limit + Epsilon) return Empty;
            return wedge;
        }

        static sbyte Subtract(sbyte fragment, Vector3Int position, Primitive chip)
        {
            if (!Within(position, chip)) return fragment;
            var vertices = fragment == Cube ? BlockGeometry.Points(BlockShape.Cube) : WedgeVertices[fragment];
            foreach (var plane in chip.planes)
            {
                bool excluded = true;
                foreach (var vertex in vertices)
                    if (Dot(plane.normal, position) + Vector3.Dot(plane.normal, vertex) < plane.limit - Epsilon) { excluded = false; break; }
                if (excluded) return fragment;
            }
            if (fragment != Cube) return Empty;
            // A cube clipped by a single chip plane has a representable complement wedge.
            // Multiple-cut fragments have no matching native module and are omitted inward.
            foreach (var plane in chip.planes)
            {
                if (Mathf.Abs(Dot(plane.normal, position) - plane.limit) > Epsilon) continue;
                int index = Array.IndexOf(Normals, -plane.normal);
                if (index >= 0) return (sbyte)index;
            }
            return Empty;
        }

        static bool Within(Vector3Int p, Primitive primitive) => p.x >= primitive.minimum.x && p.x <= primitive.maximum.x && p.y >= primitive.minimum.y && p.y <= primitive.maximum.y && p.z >= primitive.minimum.z && p.z <= primitive.maximum.z;
        static float Dot(Vector3Int a, Vector3Int b) => a.x * (float)b.x + a.y * (float)b.y + a.z * (float)b.z;
        static uint Seed(string key) { uint h = 2166136261; foreach (char c in key ?? "rock") { h ^= c; h = unchecked(h * 16777619); } return h == 0 ? 1 : h; }
        static Vector3 Centre(List<VoxelCell> cells) { var minimum = cells[0].position; var maximum = minimum; foreach (var cell in cells) { minimum = Vector3Int.Min(minimum, cell.position); maximum = Vector3Int.Max(maximum, cell.position); } return ((Vector3)minimum + (Vector3)maximum) * .5f; }
        static Vector3Int[] MakeNormals()
        {
            var result = new List<Vector3Int>();
            for (int zero = 0; zero < 3; zero++) for (int a = -1; a <= 1; a += 2) for (int b = -1; b <= 1; b += 2)
            { var n = Vector3Int.zero; n[(zero + 1) % 3] = a; n[(zero + 2) % 3] = b; result.Add(n); }
            return result.ToArray();
        }
        static Vector3Int[] MakeRotations()
        {
            var result = new Vector3Int[Normals.Length];
            for (int i = 0; i < Normals.Length; i++)
            {
                bool found = false;
                for (int x = 0; x < 4 && !found; x++) for (int y = 0; y < 4 && !found; y++) for (int z = 0; z < 4; z++)
                    if (Vector3.Dot(Quaternion.Euler(x * 90, y * 90, z * 90) * new Vector3(0, 1, -1).normalized, ((Vector3)Normals[i]).normalized) > .999f)
                    { result[i] = new Vector3Int(x, y, z); found = true; break; }
                if (!found) throw new InvalidOperationException("Missing native wedge orientation.");
            }
            return result;
        }
        static Vector3[][] MakeVertices()
        {
            var result = new Vector3[Normals.Length][];
            for (int i = 0; i < result.Length; i++)
            {
                var rotation = Quaternion.Euler(Rotations[i].x * 90, Rotations[i].y * 90, Rotations[i].z * 90);
                result[i] = BlockGeometry.Points(BlockShape.Wedge).Select(v => rotation * v).ToArray();
            }
            return result;
        }
    }
}


