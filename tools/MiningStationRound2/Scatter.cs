// Mining station / round 2: ONE local density patch, in memory only.
//
// v02 shape: three dense irregular knots (~19 members each) with hollows between them, ~10% of
// the pieces floating free, and members seated on three Y bands so the band reads layered rather
// than uniform. Everything lives inside an ~890 x 180 x 800 m sample region beside the map centre,
// leaving a 330 m empty sphere around the station. Deterministic from `seed` (own xorshift PRNG,
// stable ids); every instance gets its own scale, Euler rotation, drift and spin.
//
// No AssetDatabase save, no .asset/.meta/.unity write, no material, no light, no scene/prefab,
// no gameplay or AI code, no runtime default change: the caller only receives the in-memory map.
// Composition: this file is one fragment of `public static partial class MiningRound2`.

using System;
using System.Collections.Generic;
using Starframe.Maps;
using UnityEngine;

namespace Starframe.MiningRound2
{
    public static partial class MiningRound2
    {
        // ------------------------------------------------------------------ public API

        /// <summary>Last patch report: requested/placed counts, region, scales, measured min gap.</summary>
        public static string ScatterLastReport { get; private set; } = string.Empty;

        /// <summary>
        /// Build the density patch and return it; the map is never saved. Three knots of 12-20
        /// members, three Y layers inside the 180 m band, 8 m conservative clearance, 10% free strays.
        /// Fails loudly (exception) on bad input; reports, via <see cref="ScatterLastReport"/> plus a
        /// console warning, any instance that could not be seated inside the clearance rules.
        /// </summary>
        public static SpaceMapDefinition CreateDensityPatch(
            List<VoxelAssetDefinition> rocks,
            List<VoxelAssetDefinition> debris,
            int seed = 261101,
            int rockCount = 44,
            int debrisCount = 12)
        {
            if (rockCount < 0 || debrisCount < 0)
                throw new ArgumentOutOfRangeException(nameof(rockCount), "Instance counts must not be negative.");
            int requested = rockCount + debrisCount;
            if (requested > ScatterMaximumInstances)
                throw new ArgumentOutOfRangeException(nameof(rockCount), "A density patch is limited to " + ScatterMaximumInstances + " instances; requested " + requested + ".");

            int skippedAssets = 0;
            var rockPool = ScatterCollect(rocks, ref skippedAssets);
            var debrisPool = ScatterCollect(debris, ref skippedAssets);
            if (rockCount > 0 && rockPool.Count == 0)
                throw new ArgumentException("No usable rock asset was supplied (null, empty or failing VoxelAssetDefinition.Validate).", nameof(rocks));
            if (debrisCount > 0 && debrisPool.Count == 0)
                throw new ArgumentException("No usable debris asset was supplied (null, empty or failing VoxelAssetDefinition.Validate).", nameof(debris));

            var rng = new ScatterRng(seed);
            var items = new List<ScatterItem>(requested);
            var usedScales = new HashSet<int>();
            var usedEulers = new HashSet<long>();
            ScatterPlan(items, rockPool, rockCount, false, ref rng, usedScales, usedEulers);
            ScatterPlan(items, debrisPool, debrisCount, true, ref rng, usedScales, usedEulers);
            ScatterMarkStragglers(items, ref rng);

            var clusters = ScatterBuildClusters(Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(requested / 6f)), 2, 8), ref rng);
            var pockets = ScatterBuildPockets(clusters, ref rng);
            ScatterAssignClusters(items, clusters.Length, ref rng);
            items.Sort(ScatterCompareSize); // biggest first: the large pieces pick their spot before the small fill in.

            var spheres = new List<ScatterSphere>(items.Count);
            var entries = new List<ScatterEntry>(items.Count);
            var misses = new List<string>();
            var clusterAssigned = new int[clusters.Length];
            int latticeUses = 0;
            int stragglers = 0;

            for (int index = 0; index < items.Count; index++)
            {
                var item = items[index];
                int clusterIndex = item.cluster >= 0 && item.cluster < clusters.Length ? item.cluster : -1;
                Vector3 baseDirection = clusterIndex >= 0 ? clusters[clusterIndex].drift : rng.UnitVector();
                Vector3 drift = ScatterDrift(baseDirection, ref rng);
                Vector3 spin = ScatterSpin(ref rng);

                Vector3 position = Vector3.zero;
                bool seated = false;

                if (!item.straggler && clusterIndex >= 0)
                {
                    var cluster = clusters[clusterIndex];
                    int rank = clusterAssigned[clusterIndex];
                    // Biggest member holds the middle band; the rest cycle middle/upper/lower, so a
                    // knot reads as a dense core plus a three-layer fringe.
                    int layerIndex = (rank % ScatterLayerCount + 1) % ScatterLayerCount;
                    for (int attempt = 0; attempt < ScatterClusterAttempts; attempt++)
                    {
                        Vector3 candidate = ScatterSampleCluster(cluster, layerIndex, item.radius, ref rng, rank == 0 && attempt == 0);
                        if (!ScatterFits(candidate, item.radius, spheres, pockets)) continue;
                        position = candidate; seated = true; break;
                    }
                }
                if (!seated)
                {
                    // Bounded uniform retry: stragglers, and cluster candidates that hit a wall or a pocket.
                    for (int attempt = 0; attempt < ScatterUniformAttempts; attempt++)
                    {
                        Vector3 candidate = ScatterSampleUniform(item.radius, ref rng);
                        if (!ScatterFits(candidate, item.radius, spheres, pockets)) continue;
                        position = candidate; seated = true; break;
                    }
                }
                if (!seated)
                {
                    // Last deterministic resort: coarse lattice scan, so packing never loops forever.
                    seated = ScatterLatticePlace(item.radius, spheres, pockets, out position);
                    if (seated) latticeUses++;
                }
                if (!seated)
                {
                    misses.Add(item.asset.name + " r=" + item.radius.ToString("F1") + (item.straggler ? " straggler" : " cluster " + clusterIndex));
                    continue;
                }

                if (item.straggler) stragglers++;
                if (clusterIndex >= 0) clusterAssigned[clusterIndex]++;
                spheres.Add(new ScatterSphere { center = position, radius = item.radius });
                entries.Add(new ScatterEntry
                {
                    asset = item.asset,
                    position = position,
                    euler = item.euler,
                    drift = drift,
                    spin = spin,
                    scale = item.scale,
                    longest = item.longest,
                    tier = item.tier
                });
            }

            var map = ScriptableObject.CreateInstance<SpaceMapDefinition>();
            map.name = "Mining density patch " + seed;
            map.size = ScatterMapExtent;
            map.instances = new List<VoxelMapInstance>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                map.instances.Add(new VoxelMapInstance
                {
                    // Deterministic id: the default Guid would change on every run.
                    id = "msp" + unchecked((uint)seed).ToString("x8") + "-" + i.ToString("D2"),
                    label = entry.asset.name + " " + (i + 1).ToString("D2"),
                    asset = entry.asset,
                    position = entry.position,
                    eulerAngles = entry.euler,
                    scale = entry.scale,
                    healthMultiplier = 1f,
                    driftVelocity = entry.drift,
                    angularVelocity = entry.spin
                });
            }
            // Player/enemy spawns and map size stay at their authored defaults; this file only scatters.

            if (!map.Validate(out string error))
                throw new InvalidOperationException("Density patch failed map validation: " + error);

            ScatterLastReport = ScatterReport(map, seed, rockCount, debrisCount, skippedAssets, clusterAssigned,
                pockets.Length, latticeUses, entries, misses, requested, stragglers);
            if (misses.Count > 0)
                Debug.LogWarning("[MiningRound2] Density patch could not seat " + misses.Count + " of " + requested + " instances. " + ScatterLastReport);
            return map;
        }

        /// <summary>
        /// Conservative minimum gap between instance bounding spheres (negative = overlap).
        /// Spheres are centred on the instance origin and use the farthest local AABB corner, so the
        /// value stays valid for any rotation and for the authored spin. +Infinity when under two
        /// usable instances remain. Cost is O(n^2); the patch is small, whole maps are not.
        /// </summary>
        public static float PatchMinimumGap(SpaceMapDefinition map)
        {
            if (!map || map.instances == null) return float.PositiveInfinity;
            var radiusCache = new Dictionary<VoxelAssetDefinition, float>();
            float minimum = float.PositiveInfinity;
            var instances = map.instances;
            for (int a = 0; a < instances.Count; a++)
            {
                var instance = instances[a];
                if (!ScatterMeasurable(instance)) continue;
                float radius = ScatterCachedRadius(instance.asset, instance.scale, radiusCache);
                for (int b = a + 1; b < instances.Count; b++)
                {
                    var other = instances[b];
                    if (!ScatterMeasurable(other)) continue;
                    float gap = (instance.position - other.position).magnitude - radius - ScatterCachedRadius(other.asset, other.scale, radiusCache);
                    if (gap < minimum) minimum = gap;
                }
            }
            return minimum;
        }

        // ------------------------------------------------------------------ tuning / limits

        public const int ScatterMaximumInstances = 512;

        /// <summary>Adaptive uniform scale band: smaller assets grow, larger assets shrink.</summary>
        public const float ScatterMinimumScale = .45f;
        public const float ScatterMaximumScale = 2f;

        /// <summary>Conservative sphere-to-sphere clearance kept between neighbours, in metres.</summary>
        public const float ScatterClearance = 8f;

        /// <summary>Empty central airspace radius (station is ~500 long, so 330 leaves ~80 m around it).</summary>
        public const float ScatterVoidRadius = 330f;

        const int ScatterClusterAttempts = 48;
        const int ScatterUniformAttempts = 24;
        const int ScatterLatticeBudget = 4096;
        const int ScatterPocketCount = 2;
        const float ScatterLatticeMinStep = 10f;

        // Y layering: members sit on one of three bands. A member never exceeds +/-88 m, so the
        // cluster spheres always stay inside the 180 m tall region.
        const float ScatterLayerNominal = 56f;
        const float ScatterMaximumLayerHeight = 88f;
        const int ScatterLayerCount = 3;

        static readonly Vector3 ScatterMapExtent = new Vector3(2500f, 2500f, 2500f);
        static readonly Vector3 ScatterRegionCenter = new Vector3(790f, 0f, 0f);
        static readonly Vector3 ScatterRegionHalfSize = new Vector3(445f, 90f, 400f); // ~890 x 180 x 800
        static readonly Vector3 ScatterVoidCenter = Vector3.zero;

        // Deliberately uneven knot anchors (x, z): three lumps around a clear middle hollow,
        // not a grid.
        static readonly Vector3[] ScatterClusterAnchors =
        {
            new Vector3(520f, 0f, -230f),
            new Vector3(1140f, 0f, -150f),
            new Vector3(830f, 0f, 250f),
            new Vector3(1160f, 0f, 280f),
            new Vector3(430f, 0f, 60f),
            new Vector3(920f, 0f, -70f),
            new Vector3(700f, 0f, 330f),
            new Vector3(430f, 0f, -370f),
            new Vector3(1080f, 0f, 60f)
        };

        static Vector3 ScatterRegionMin => ScatterRegionCenter - ScatterRegionHalfSize;
        static Vector3 ScatterRegionMax => ScatterRegionCenter + ScatterRegionHalfSize;

        // ------------------------------------------------------------------ types

        enum ScatterTier { Small, Medium, Large, Hero }

        struct ScatterRng
        {
            uint state;
            public ScatterRng(int seed)
            {
                state = unchecked((uint)seed);
                if (state == 0u) state = 0x9E3779B9u; // xorshift32 must not start at zero.
            }
            public uint NextBits()
            {
                uint x = state;
                x ^= x << 13; x ^= x >> 17; x ^= x << 5;
                state = x;
                return x;
            }
            public float Next() => (NextBits() >> 8) * (1f / 16777216f); // [0, 1)
            public int NextInt(int exclusiveMax) => exclusiveMax <= 1 ? 0 : (int)(NextBits() % (uint)exclusiveMax);
            public float Range(float minimum, float maximum) => minimum + (maximum - minimum) * Next();
            public float Signed() => Next() * 2f - 1f;
            public bool Chance(float probability) => Next() < probability;
            public Vector3 UnitVector()
            {
                for (int guard = 0; guard < 8; guard++)
                {
                    var candidate = new Vector3(Signed(), Signed(), Signed());
                    float lengthSqr = candidate.sqrMagnitude;
                    if (lengthSqr > 1e-4f && lengthSqr <= 1f) return candidate / Mathf.Sqrt(lengthSqr);
                }
                return Vector3.up; // deterministic, bounded; practically unreachable.
            }
        }

        struct ScatterAssetInfo
        {
            public VoxelAssetDefinition asset;
            public float longest; // asset-unit longest extent
            public float radius;  // asset-unit conservative sphere radius around the origin
        }

        struct ScatterItem
        {
            public VoxelAssetDefinition asset;
            public Vector3 euler;
            public float scale;
            public float radius;   // world metres, conservative
            public float longest;  // world metres, longest extent
            public ScatterTier tier;
            public bool straggler;
            public int cluster;
            public int order;
        }

        struct ScatterCluster
        {
            public Vector3 center;        // knot centre on the XZ plane
            public Vector3 radii;         // x/z footprint only; y is driven by the layers
            public Quaternion rotation;   // yaw / slight tilt of the knot footprint
            public Vector3 drift;
            public Vector3[] lobes;       // horizontal sub-centres: a knot is a chain of lumps
            public float[] layerHeights;  // 3 absolute Y bands: {upper, middle, lower}
        }

        struct ScatterPocket
        {
            public Vector3 center;
            public float radius;
        }

        struct ScatterSphere
        {
            public Vector3 center;
            public float radius;
        }

        struct ScatterEntry
        {
            public VoxelAssetDefinition asset;
            public Vector3 position, euler, drift, spin;
            public float scale, longest;
            public ScatterTier tier;
        }

        // ------------------------------------------------------------------ inputs

        static List<ScatterAssetInfo> ScatterCollect(List<VoxelAssetDefinition> source, ref int skipped)
        {
            var result = new List<ScatterAssetInfo>();
            if (source == null) return result;
            foreach (var asset in source)
            {
                if (!asset || asset.cells == null || asset.cells.Count == 0 || !asset.Validate(out _))
                {
                    skipped++;
                    continue;
                }
                ScatterMeasure(asset.LocalBounds, out float longest, out float radius);
                result.Add(new ScatterAssetInfo { asset = asset, longest = longest, radius = radius });
            }
            // Sorted by size so tier picks can stay anchored on the real large models.
            result.Sort((a, b) =>
            {
                int order = b.longest.CompareTo(a.longest);
                return order != 0 ? order : string.CompareOrdinal(a.asset.name, b.asset.name);
            });
            return result;
        }

        static void ScatterMeasure(Bounds bounds, out float longest, out float radius)
        {
            Vector3 center = bounds.center, extents = bounds.extents;
            // Farthest corner of the local AABB from the instance origin: rotation invariant.
            float x = Mathf.Max(Mathf.Abs(center.x + extents.x), Mathf.Abs(center.x - extents.x));
            float y = Mathf.Max(Mathf.Abs(center.y + extents.y), Mathf.Abs(center.y - extents.y));
            float z = Mathf.Max(Mathf.Abs(center.z + extents.z), Mathf.Abs(center.z - extents.z));
            radius = Mathf.Sqrt(x * x + y * y + z * z);
            longest = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) * 2f;
        }

        // ------------------------------------------------------------------ planning

        static void ScatterPlan(List<ScatterItem> items, List<ScatterAssetInfo> pool, int count, bool debris,
            ref ScatterRng rng, HashSet<int> usedScales, HashSet<long> usedEulers)
        {
            if (count <= 0) return;
            var tiers = ScatterTierPlan(count, !debris, ref rng);
            for (int i = 0; i < count; i++)
            {
                var tier = tiers[i];
                var info = ScatterPickAsset(pool, tier, ref rng);
                float target = ScatterTargetLength(tier, !debris, ref rng);
                float scale = ScatterFitScale(info.longest, target, ref rng);
                // Every instance must keep a distinct scale, including the clamped extremes.
                for (int guard = 0; guard < 32; guard++)
                {
                    if (usedScales.Add(Mathf.RoundToInt(scale * 10000f))) break;
                    scale = Mathf.Clamp(scale + (guard % 2 == 0 ? .0007f : -.0011f), ScatterMinimumScale, ScatterMaximumScale);
                }
                Vector3 euler = ScatterUniqueEuler(ref rng, usedEulers);
                float radius = Mathf.Max(.5f, info.radius * scale);
                items.Add(new ScatterItem
                {
                    asset = info.asset,
                    euler = euler,
                    scale = scale,
                    radius = radius,
                    longest = info.longest * scale,
                    tier = tier,
                    straggler = false,
                    cluster = -1,
                    order = items.Count
                });
            }
        }

        // Size hierarchy: a few heroes, some large, many medium, most small.
        static ScatterTier[] ScatterTierPlan(int count, bool rock, ref ScatterRng rng)
        {
            var plan = new ScatterTier[Mathf.Max(0, count)];
            if (plan.Length == 0) return plan;
            int hero, large, medium;
            if (rock)
            {
                hero = count >= 12 ? Mathf.Clamp(Mathf.RoundToInt(count * .05f), 1, 3) : 0;
                large = Mathf.Max(1, Mathf.RoundToInt(count * .14f));
                medium = Mathf.Max(0, Mathf.RoundToInt(count * .33f));
            }
            else
            {
                hero = 0;
                large = count >= 10 ? 1 : 0;
                medium = Mathf.Max(0, Mathf.RoundToInt(count * .35f));
            }
            int overflow = hero + large + medium - count;
            while (overflow > 0)
            {
                if (medium > 0) { medium--; overflow--; continue; }
                if (large > 0) { large--; overflow--; continue; }
                if (hero > 0) { hero--; overflow--; continue; }
                break;
            }
            int index = 0;
            for (int i = 0; i < hero; i++) plan[index++] = ScatterTier.Hero;
            for (int i = 0; i < large; i++) plan[index++] = ScatterTier.Large;
            for (int i = 0; i < medium; i++) plan[index++] = ScatterTier.Medium;
            while (index < plan.Length) plan[index++] = ScatterTier.Small;
            ScatterShuffle(plan, ref rng);
            return plan;
        }

        // The pool is sorted large to small: bigger tiers draw from the bigger models.
        static ScatterAssetInfo ScatterPickAsset(List<ScatterAssetInfo> pool, ScatterTier tier, ref ScatterRng rng)
        {
            int count = pool.Count;
            int start = 0, end = count;
            switch (tier)
            {
                case ScatterTier.Hero: end = Mathf.Max(1, Mathf.CeilToInt(count * .34f)); break;
                case ScatterTier.Large: end = Mathf.Max(1, Mathf.CeilToInt(count * .62f)); break;
                case ScatterTier.Medium: start = Mathf.FloorToInt(count * .2f); break;
                default: start = Mathf.FloorToInt(count * .42f); break;
            }
            if (start >= end) { start = 0; end = count; }
            return pool[start + rng.NextInt(end - start)];
        }

        static float ScatterTargetLength(ScatterTier tier, bool rock, ref ScatterRng rng)
        {
            switch (tier)
            {
                case ScatterTier.Hero: return rock ? rng.Range(58f, 76f) : rng.Range(30f, 40f);
                case ScatterTier.Large: return rock ? rng.Range(42f, 60f) : rng.Range(24f, 34f);
                case ScatterTier.Medium: return rock ? rng.Range(27f, 42f) : rng.Range(15f, 25f);
                default: return rock ? rng.Range(15f, 27f) : rng.Range(8f, 16f);
            }
        }

        // Uniform scale derived from the asset bounds; the band edges stay jittered so clamped
        // assets do not all collapse onto exactly 0.45 or 2.0.
        static float ScatterFitScale(float prototypeLongest, float targetLongest, ref ScatterRng rng)
        {
            float raw = targetLongest / Mathf.Max(prototypeLongest, .01f);
            float scale;
            if (raw >= ScatterMaximumScale) scale = ScatterMaximumScale - rng.Range(0f, .09f);
            else if (raw <= ScatterMinimumScale) scale = ScatterMinimumScale + rng.Range(0f, .09f);
            else scale = raw * (1f + rng.Signed() * .018f);
            return Mathf.Clamp(scale, ScatterMinimumScale, ScatterMaximumScale);
        }

        static Vector3 ScatterUniqueEuler(ref ScatterRng rng, HashSet<long> used)
        {
            Vector3 euler = Vector3.zero;
            for (int attempt = 0; attempt < 12; attempt++)
            {
                euler = new Vector3(rng.Range(0f, 360f), rng.Range(0f, 360f), rng.Range(0f, 360f));
                if (used.Add(ScatterEulerKey(euler))) return euler;
            }
            // Practically unreachable: walk the yaw deterministically until the key is free.
            for (int guard = 0; guard < 256; guard++)
            {
                euler.y = Mathf.Repeat(euler.y + .37f, 360f);
                if (used.Add(ScatterEulerKey(euler))) return euler;
            }
            return euler;
        }

        static long ScatterEulerKey(Vector3 euler)
        {
            long x = Mathf.RoundToInt(Mathf.Repeat(euler.x, 360f) * 20f) % 7200;
            long y = Mathf.RoundToInt(Mathf.Repeat(euler.y, 360f) * 20f) % 7200;
            long z = Mathf.RoundToInt(Mathf.Repeat(euler.z, 360f) * 20f) % 7200;
            return (x * 7200L + y) * 7200L + z;
        }

        static Vector3 ScatterDrift(Vector3 direction, ref ScatterRng rng)
        {
            Vector3 mixed = direction + rng.UnitVector() * .4f;
            Vector3 result = mixed.sqrMagnitude > .01f ? mixed.normalized : (direction.sqrMagnitude > .01f ? direction.normalized : Vector3.right);
            return result * rng.Range(.1f, .4f); // slow: a short sample cannot close the 8 m clearance quickly.
        }

        static Vector3 ScatterSpin(ref ScatterRng rng) => rng.UnitVector() * rng.Range(.03f, .1f);

        // ------------------------------------------------------------------ clusters / gaps

        static ScatterCluster[] ScatterBuildClusters(int count, ref ScatterRng rng)
        {
            count = Mathf.Clamp(count, 1, ScatterClusterAnchors.Length);
            var clusters = new ScatterCluster[count];
            for (int i = 0; i < count; i++)
            {
                // Fixed uneven anchors (jittered): three knots with real holes between them,
                // instead of a 3 x 3 spread that reads as an even field.
                Vector3 anchor = ScatterClusterAnchors[i];
                var center = new Vector3(anchor.x + rng.Range(-30f, 30f), 0f, anchor.z + rng.Range(-30f, 30f));
                var radii = new Vector3(rng.Range(118f, 142f), 0f, rng.Range(118f, 142f));
                var rotation = Quaternion.Euler(rng.Range(-8f, 8f), rng.Range(0f, 360f), rng.Range(-8f, 8f));

                // Drifting away from the station keeps the central airspace clear over a longer sample.
                Vector3 outward = center - ScatterVoidCenter;
                outward = outward.sqrMagnitude > 1f ? outward.normalized : Vector3.right;
                Vector3 drift = rng.UnitVector();
                if (Vector3.Dot(drift, outward) < 0f) drift = (drift + outward * .9f).normalized;

                // Two overlapping lumps along a random yaw: an irregular knot, not a round bead.
                var lobes = new Vector3[]
                {
                    new Vector3(rng.Range(-.55f, -.3f) * radii.x, 0f, rng.Range(-.35f, .35f) * radii.z),
                    new Vector3(rng.Range(.3f, .55f) * radii.x, 0f, rng.Range(-.35f, .35f) * radii.z)
                };

                // Three Y bands per knot; each knot gets its own heights, so bands interleave.
                float upper = rng.Range(50f, 62f);
                var layers = new[]
                {
                    upper + rng.Range(-6f, 6f),
                    rng.Range(-9f, 9f),
                    -(upper + rng.Range(-6f, 6f))
                };

                clusters[i] = new ScatterCluster
                {
                    center = center, radii = radii, rotation = rotation, drift = drift, lobes = lobes, layerHeights = layers
                };
            }
            return clusters;
        }

        // Cavities sit in the gaps between knots and bite their inner edges, so the band keeps
        // visible hollows without ever hollowing a knot core out.
        static ScatterPocket[] ScatterBuildPockets(ScatterCluster[] clusters, ref ScatterRng rng)
        {
            var pockets = new ScatterPocket[ScatterPocketCount];
            for (int i = 0; i < pockets.Length; i++)
            {
                int firstIndex = Mathf.Clamp(i, 0, clusters.Length - 1);
                int secondIndex = Mathf.Clamp(i + 1, 0, clusters.Length - 1);
                Vector3 direction = clusters[secondIndex].center - clusters[firstIndex].center;
                direction.y = 0f;
                if (firstIndex == secondIndex || direction.sqrMagnitude < 1f) direction = new Vector3(150f, 0f, 0f);
                Vector3 side = Vector3.Cross(Vector3.up, direction.normalized);
                pockets[i] = new ScatterPocket
                {
                    center = clusters[firstIndex].center + direction * .5f
                             + side * rng.Range(-26f, 26f)
                             + new Vector3(rng.Range(-12f, 12f), 0f, rng.Range(-12f, 12f)),
                    radius = rng.Range(64f, 78f)
                };
            }
            return pockets;
        }

        static void ScatterAssignClusters(List<ScatterItem> items, int clusterCount, ref ScatterRng rng)
        {
            if (items.Count == 0 || clusterCount <= 0) return;
            var slots = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++) slots.Add(i % clusterCount);
            ScatterShuffle(slots, ref rng);
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                item.cluster = slots[i];
                items[i] = item;
            }
        }

        // Exactly ~10% of the patch floats free between the knots, so the belt never reads as
        // three isolated islands (and never as a uniform field).
        static void ScatterMarkStragglers(List<ScatterItem> items, ref ScatterRng rng)
        {
            if (items.Count == 0) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(items.Count * .1f), 1, items.Count / 2);
            var order = new List<int>(items.Count);
            for (int i = 0; i < items.Count; i++) order.Add(i);
            ScatterShuffle(order, ref rng);
            for (int i = 0; i < count; i++)
            {
                var item = items[order[i]];
                item.straggler = true;
                items[order[i]] = item;
            }
        }

        // Strong core bias keeps a dense middle and a thin edge; Y comes from the knot's layer
        // band, pulled toward 0 for the largest pieces so no sphere leaves the 180 m region.
        static Vector3 ScatterSampleCluster(ScatterCluster cluster, int layerIndex, float radius, ref ScatterRng rng, bool core)
        {
            Vector3 direction = rng.UnitVector();
            float reach = Mathf.Pow(rng.Next(), 1.5f);
            Vector3 lobe = !core && cluster.lobes != null && cluster.lobes.Length > 0
                ? cluster.lobes[rng.NextInt(cluster.lobes.Length)]
                : Vector3.zero;
            Vector3 horizontal = lobe + new Vector3(direction.x * cluster.radii.x, 0f, direction.z * cluster.radii.z) * (core ? .12f : reach);
            Vector3 rotated = cluster.rotation * horizontal;
            Vector3 position = cluster.center;
            position.x += rotated.x;
            position.z += rotated.z;

            float allowance = Mathf.Max(0f, ScatterMaximumLayerHeight - radius);
            float band = cluster.layerHeights != null && cluster.layerHeights.Length == ScatterLayerCount
                ? cluster.layerHeights[Mathf.Clamp(layerIndex, 0, ScatterLayerCount - 1)]
                : 0f;
            float pull = Mathf.Min(1f, allowance / ScatterLayerNominal); // big pieces stay nearer the middle
            position.y = Mathf.Clamp(band * pull + rng.Range(-4f, 4f), -allowance, allowance);
            return position;
        }

        static Vector3 ScatterSampleUniform(float radius, ref ScatterRng rng)
        {
            Vector3 minimum = ScatterRegionMin + Vector3.one * radius;
            Vector3 maximum = ScatterRegionMax - Vector3.one * radius;
            return new Vector3(
                minimum.x > maximum.x ? ScatterRegionCenter.x : rng.Range(minimum.x, maximum.x),
                minimum.y > maximum.y ? ScatterRegionCenter.y : rng.Range(minimum.y, maximum.y),
                minimum.z > maximum.z ? ScatterRegionCenter.z : rng.Range(minimum.z, maximum.z));
        }

        // ------------------------------------------------------------------ seating test

        static bool ScatterFits(Vector3 position, float radius, List<ScatterSphere> placed, ScatterPocket[] pockets)
        {
            Vector3 minimum = ScatterRegionMin + Vector3.one * radius;
            Vector3 maximum = ScatterRegionMax - Vector3.one * radius;
            if (minimum.x > maximum.x || minimum.y > maximum.y || minimum.z > maximum.z) return false; // too large for the band
            if (position.x < minimum.x || position.x > maximum.x || position.y < minimum.y ||
                position.y > maximum.y || position.z < minimum.z || position.z > maximum.z) return false;

            float voidDistance = ScatterVoidRadius + radius;
            if ((position - ScatterVoidCenter).sqrMagnitude < voidDistance * voidDistance) return false;

            for (int i = 0; i < pockets.Length; i++)
            {
                var pocket = pockets[i];
                float limit = pocket.radius + radius;
                if ((position - pocket.center).sqrMagnitude < limit * limit) return false;
            }
            for (int i = 0; i < placed.Count; i++)
            {
                var other = placed[i];
                float limit = other.radius + radius + ScatterClearance;
                if ((position - other.center).sqrMagnitude < limit * limit) return false;
            }
            return true;
        }

        // Deterministic coarse scan used only when random seating fails: bounded work, no open loop.
        static bool ScatterLatticePlace(float radius, List<ScatterSphere> placed, ScatterPocket[] pockets, out Vector3 position)
        {
            position = default;
            Vector3 minimum = ScatterRegionMin + Vector3.one * radius;
            Vector3 maximum = ScatterRegionMax - Vector3.one * radius;
            if (minimum.x > maximum.x || minimum.y > maximum.y || minimum.z > maximum.z) return false;

            float step = Mathf.Max(ScatterLatticeMinStep, radius * .55f);
            int nx = 1, ny = 1, nz = 1;
            for (int guard = 0; guard < 24; guard++)
            {
                nx = Mathf.FloorToInt((maximum.x - minimum.x) / step) + 1;
                ny = Mathf.FloorToInt((maximum.y - minimum.y) / step) + 1;
                nz = Mathf.FloorToInt((maximum.z - minimum.z) / step) + 1;
                if (nx * ny * nz <= ScatterLatticeBudget) break;
                step *= 1.35f;
            }

            float offsetX = (maximum.x - minimum.x - (nx - 1) * step) * .5f;
            float offsetY = (maximum.y - minimum.y - (ny - 1) * step) * .5f;
            float offsetZ = (maximum.z - minimum.z - (nz - 1) * step) * .5f;
            float best = float.NegativeInfinity;
            bool found = false;
            for (int ix = 0; ix < nx; ix++)
                for (int iy = 0; iy < ny; iy++)
                    for (int iz = 0; iz < nz; iz++)
                    {
                        var candidate = new Vector3(minimum.x + offsetX + ix * step, minimum.y + offsetY + iy * step, minimum.z + offsetZ + iz * step);
                        if (!ScatterFits(candidate, radius, placed, pockets)) continue;
                        float clearance = float.PositiveInfinity;
                        for (int i = 0; i < placed.Count; i++)
                        {
                            var other = placed[i];
                            float gap = (candidate - other.center).magnitude - other.radius - radius;
                            if (gap < clearance) clearance = gap;
                        }
                        if (clearance <= best) continue;
                        best = clearance; position = candidate; found = true;
                    }
            return found;
        }

        // ------------------------------------------------------------------ misc helpers

        static int ScatterCompareSize(ScatterItem a, ScatterItem b)
        {
            int order = b.radius.CompareTo(a.radius);
            return order != 0 ? order : a.order.CompareTo(b.order);
        }

        static void ScatterShuffle<T>(T[] values, ref ScatterRng rng)
        {
            for (int i = values.Length - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                T swap = values[i]; values[i] = values[j]; values[j] = swap;
            }
        }

        static void ScatterShuffle(List<int> values, ref ScatterRng rng)
        {
            for (int i = values.Count - 1; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                int swap = values[i]; values[i] = values[j]; values[j] = swap;
            }
        }

        static bool ScatterMeasurable(VoxelMapInstance instance) => instance != null && instance.asset;

        static float ScatterCachedRadius(VoxelAssetDefinition asset, float scale, Dictionary<VoxelAssetDefinition, float> cache)
        {
            if (cache.TryGetValue(asset, out float radius)) return radius * scale;
            ScatterMeasure(asset.LocalBounds, out _, out radius);
            cache[asset] = radius;
            return radius * scale;
        }

        // ------------------------------------------------------------------ report

        static string ScatterReport(SpaceMapDefinition map, int seed, int rockCount, int debrisCount, int skippedAssets,
            int[] clusterMembers, int pocketCount, int latticeUses, List<ScatterEntry> entries, List<string> misses, int requested, int stragglers)
        {
            float minScale = float.PositiveInfinity, maxScale = 0f;
            float minLongest = float.PositiveInfinity, maxLongest = 0f;
            float minDrift = float.PositiveInfinity, maxDrift = 0f;
            float minSpin = float.PositiveInfinity, maxSpin = 0f;
            int small = 0, medium = 0, large = 0, hero = 0;
            foreach (var entry in entries)
            {
                minScale = Mathf.Min(minScale, entry.scale); maxScale = Mathf.Max(maxScale, entry.scale);
                minLongest = Mathf.Min(minLongest, entry.longest); maxLongest = Mathf.Max(maxLongest, entry.longest);
                float drift = entry.drift.magnitude, spin = entry.spin.magnitude;
                minDrift = Mathf.Min(minDrift, drift); maxDrift = Mathf.Max(maxDrift, drift);
                minSpin = Mathf.Min(minSpin, spin); maxSpin = Mathf.Max(maxSpin, spin);
                switch (entry.tier)
                {
                    case ScatterTier.Hero: hero++; break;
                    case ScatterTier.Large: large++; break;
                    case ScatterTier.Medium: medium++; break;
                    default: small++; break;
                }
            }
            float gap = PatchMinimumGap(map);
            string gapText = float.IsInfinity(gap) ? (entries.Count == 0 ? "n/a" : "+inf") : gap.ToString("F2");

            var parts = new List<string>
            {
                (misses.Count > 0 ? "SHORTFALL " : string.Empty) +
                "seed=" + seed + " rocks=" + rockCount + " debris=" + debrisCount + " requested=" + requested + " placed=" + entries.Count,
                "misses=" + misses.Count,
                "region=" + (ScatterRegionHalfSize.x * 2f).ToString("F0") + "x" + (ScatterRegionHalfSize.y * 2f).ToString("F0") + "x" + (ScatterRegionHalfSize.z * 2f).ToString("F0") +
                "@(" + ScatterRegionCenter.x.ToString("F0") + "," + ScatterRegionCenter.y.ToString("F0") + "," + ScatterRegionCenter.z.ToString("F0") + ")" +
                " voidR=" + ScatterVoidRadius.ToString("F0"),
                "clusters=" + clusterMembers.Length + " members=" + string.Join("/", clusterMembers) +
                " pockets=" + pocketCount + " stragglers=" + stragglers + " yLayers=" + ScatterLayerCount + " lattice=" + latticeUses,
                "tiers=s" + small + "/m" + medium + "/l" + large + "/h" + hero,
                "scale=" + ScatterRange(minScale, maxScale, "F2", entries.Count),
                "worldLongest=" + ScatterRange(minLongest, maxLongest, "F1", entries.Count),
                "drift=" + ScatterRange(minDrift, maxDrift, "F2", entries.Count) + " spinDeg/s=" + ScatterRange(minSpin, maxSpin, "F3", entries.Count),
                "clearance=" + ScatterClearance.ToString("F1") + " minGap=" + gapText,
                "skippedAssets=" + skippedAssets
            };
            if (misses.Count > 0)
            {
                int shown = Mathf.Min(3, misses.Count);
                var samples = new List<string>(shown);
                for (int i = 0; i < shown; i++) samples.Add(misses[i]);
                parts.Add("firstMisses=" + string.Join(", ", samples));
            }
            return string.Join(" ", parts);
        }

        static string ScatterRange(float minimum, float maximum, string format, int count)
            => count == 0 ? "n/a" : minimum.ToString(format) + ".." + maximum.ToString(format);
    }
}
