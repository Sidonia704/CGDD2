using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.EditorTools;

public static class PhysicsMetadataRepair
{
    sealed class Change
    {
        public VoxelAssetDefinition target, source;
        public float scale;
        public VoxelPhysicalMetadata physics;
        public Mesh mesh;
        public long signature;
    }
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before repairing metadata.");
        var pending = new Dictionary<VoxelAssetDefinition, Change>();
        int instances = 0;
        double maxCOM = 0, maxCOMCorrection = 0;
        foreach (var name in new[] { "Range115Voxels", "SaturnRingVoxels" })
        {
            string oldPath = "Assets/_Starframe/Maps/" + name + "/Layouts/" + name + ".asset";
            string newPath = UniformMapBuilder.Root + "/" + name + "/Layouts/" + name + ".asset";
            var oldMap = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(oldPath);
            var newMap = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(newPath);
            if (!oldMap || !newMap || oldMap.instances.Count != newMap.instances.Count)
                throw new InvalidOperationException("Missing or mismatched maps: " + name);
            for (int i = 0; i < oldMap.instances.Count; i++)
            {
                var before = oldMap.instances[i]; var after = newMap.instances[i];
                if (before.id != after.id || !before.asset || !after.asset || after.scale != 1 || after.asset.cellSize != 1)
                    throw new InvalidOperationException("Unexpected instance mapping: " + name + " index " + i);
                instances++;
                if (pending.TryGetValue(after.asset, out var prior))
                {
                    if (prior.source != before.asset || prior.scale != before.scale)
                        throw new InvalidOperationException("Target is shared by incompatible source variants: " + after.asset.name);
                    continue;
                }
                var physics = UniformPhysicsCapture.Capture(before.asset, before.scale);
                double x = 0, y = 0, z = 0;
                foreach (var cell in before.asset.cells) { x += cell.position.x; y += cell.position.y; z += cell.position.z; }
                double size = (double)before.asset.cellSize * before.scale / before.asset.cells.Count;
                var expected = new Vector3((float)(x * size), (float)(y * size), (float)(z * size));
                maxCOM = Math.Max(maxCOM, (expected - physics.centerOfMass).magnitude);
                if (after.asset.authoredPhysics != null)
                    maxCOMCorrection = Math.Max(maxCOMCorrection, (after.asset.authoredPhysics.centerOfMass - physics.centerOfMass).magnitude);
                pending.Add(after.asset, new Change { target = after.asset, source = before.asset, scale = before.scale,
                    physics = physics, mesh = after.asset.initialLodMesh, signature = after.asset.initialLodSignature });
            }
        }
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var change in pending.Values)
            {
                change.target.authoredPhysics = change.physics;
                change.target.massPerCell = change.physics.mass / change.target.cells.Count;
                // Metadata does not change geometry or invalidate its signed assembly cache.
                if (change.target.initialLodMesh != change.mesh || change.target.initialLodSignature != change.signature)
                    throw new InvalidOperationException("Unexpected geometry change during metadata repair.");
                EditorUtility.SetDirty(change.target);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        return new { count = pending.Count, instances, maxCOM, maxCOMCorrection, geometryChanged = false };
    }
}
