// @role Store scenery instances, authored physical assembly contacts, drift settings and future encounter spawn positions.
// @calls VoxelAssetDefinition, SpaceMapRuntime, SpaceMapAssemblyLinks
// @tune size:Vector3:1-100000:Full map dimensions in metres.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Starframe.Maps
{
    [Serializable]
    public sealed class VoxelMapInstance
    {
        public string id = Guid.NewGuid().ToString("N");
        public string label = "Scenery";
        public VoxelAssetDefinition asset;
        public Vector3 position, eulerAngles;
        [Range(.25f, 100)] public float scale = 1;
        [Min(.01f)] public float healthMultiplier = 1;
        public Vector3 driftVelocity, angularVelocity;
        public Matrix4x4 Matrix => Matrix4x4.TRS(position, Quaternion.Euler(eulerAngles), Vector3.one * scale);
        public VoxelMapInstance Copy() => new VoxelMapInstance { label = label, asset = asset, position = position,
            eulerAngles = eulerAngles, scale = scale, healthMultiplier = healthMultiplier, driftVelocity = driftVelocity, angularVelocity = angularVelocity };
    }

    [Serializable]
    public sealed class VoxelAssemblyLink
    {
        public string instanceIdA, instanceIdB;
        public Vector3Int anchorCellA, anchorCellB;
        // Map-local world position in the saved layout, after its one global placement transform.
        public Vector3 worldAnchor;
        public bool createFixedJoint = true;
        public bool ignoreMutualCollision = true;
    }

    [CreateAssetMenu(menuName = "Starframe/Maps/Space map")]
    public sealed class SpaceMapDefinition : ScriptableObject
    {
        public Vector3 size = Vector3.one * 3000;
        public Vector3 playerSpawn = new Vector3(0, 150, -250);
        public Vector3 playerEulerAngles;
        public List<Vector3> enemySpawns = new List<Vector3> { new Vector3(0, 200, 500) };
        public List<VoxelMapInstance> instances = new List<VoxelMapInstance>();
        public List<VoxelAssemblyLink> assemblyLinks = new List<VoxelAssemblyLink>();
        public bool Validate(out string error)
        {
            if (!Finite(size) || size.x <= 0 || size.y <= 0 || size.z <= 0 || !Finite(playerSpawn) || !Finite(playerEulerAngles))
            { error = "地图尺寸或出生点无效。"; return false; }
            var ids = new HashSet<string>();
            var byId = new Dictionary<string, VoxelMapInstance>();
            foreach (var point in enemySpawns) if (!Finite(point)) { error = "敌方出生点无效。"; return false; }
            foreach (var item in instances)
            {
                if (item == null || !item.asset || string.IsNullOrEmpty(item.id) || !ids.Add(item.id) || !Finite(item.position) ||
                    !Finite(item.eulerAngles) || !Finite(item.driftVelocity) || !Finite(item.angularVelocity) || !Finite(item.scale) || item.scale < .25f || item.scale > 100 ||
                    !Finite(item.healthMultiplier) || item.healthMultiplier <= 0)
                { error = "地图中存在缺失资产、重复标识或无效变换。"; return false; }
                if (!item.asset.Validate(out error)) return false;
                byId.Add(item.id, item);
            }
            if (assemblyLinks != null) foreach (var link in assemblyLinks)
            {
                if (link == null || link.instanceIdA == link.instanceIdB || !Finite(link.worldAnchor) ||
                    !byId.TryGetValue(link.instanceIdA ?? "", out var a) ||
                    !byId.TryGetValue(link.instanceIdB ?? "", out var b) ||
                    !HasCell(a.asset, link.anchorCellA) || !HasCell(b.asset, link.anchorCellB) ||
                    (!link.createFixedJoint && !link.ignoreMutualCollision))
                { error = "地图物理连接引用了缺失物件或锚点格。"; return false; }
            }
            error = null; return true;
        }
        static bool HasCell(VoxelAssetDefinition asset, Vector3Int position)
        {
            foreach (var cell in asset.cells) if (cell.position == position) return true;
            return false;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
    }
}
