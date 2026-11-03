using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Starframe.Maps;
using UnityEngine;

namespace Starframe.EditorTools
{
    internal static class VoxelConnectivityDiagnostics
    {
        internal sealed class AssetSummary
        {
            public int revision;
            public int cellCount;
            public int componentCount;
        }

        internal struct MapSummary
        {
            public int disconnectedAssetCount;
            public int disconnectedInstanceCount;
            public int possibleRigidBodyCount;
        }

        sealed class CacheEntry
        {
            public int revision;
            public AssetSummary summary;
        }

        static readonly ConditionalWeakTable<VoxelAssetDefinition, CacheEntry> cache = new ConditionalWeakTable<VoxelAssetDefinition, CacheEntry>();

        internal static AssetSummary ForAsset(VoxelAssetDefinition asset)
        {
            if (!asset) return new AssetSummary();
            int revision = asset.GeometryRevision;
            if (cache.TryGetValue(asset, out var entry) && entry.revision == revision) return entry.summary;

            var remaining = new HashSet<Vector3Int>();
            if (asset.cells != null)
                foreach (var cell in asset.cells) if (cell != null) remaining.Add(cell.position);

            int components = 0;
            var queue = new Queue<Vector3Int>();
            while (remaining.Count > 0)
            {
                components++;
                var first = First(remaining);
                remaining.Remove(first); queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var position = queue.Dequeue();
                    foreach (var direction in VoxelGeometry.Directions)
                    {
                        var neighbor = position + direction;
                        if (remaining.Remove(neighbor)) queue.Enqueue(neighbor);
                    }
                }
            }

            var summary = new AssetSummary { revision = revision, cellCount = asset.cells?.Count ?? 0, componentCount = components };
            cache.Remove(asset);
            cache.Add(asset, new CacheEntry { revision = revision, summary = summary });
            return summary;
        }

        internal static MapSummary ForMap(SpaceMapDefinition map)
        {
            var summary = new MapSummary();
            if (!map || map.instances == null) return summary;
            var disconnectedAssets = new HashSet<VoxelAssetDefinition>();
            foreach (var instance in map.instances)
            {
                if (instance == null || !instance.asset) continue;
                var assetSummary = ForAsset(instance.asset);
                if (assetSummary.componentCount <= 1) continue;
                disconnectedAssets.Add(instance.asset);
                summary.disconnectedInstanceCount++;
                summary.possibleRigidBodyCount += assetSummary.componentCount;
            }
            summary.disconnectedAssetCount = disconnectedAssets.Count;
            return summary;
        }

        static Vector3Int First(HashSet<Vector3Int> positions)
        {
            foreach (var position in positions) return position;
            return default;
        }
    }
}
