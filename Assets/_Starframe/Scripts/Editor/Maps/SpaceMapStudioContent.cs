using System.Collections.Generic;
using System.Linq;
using Starframe.Maps;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    public static class SpaceMapStudioContent
    {
        public const string Root = SpaceMapStudioWindow.ContentRoot;
        public static Starframe.Flight.ModuleDatabase HullDatabase => AssetDatabase.LoadAssetAtPath<Starframe.Flight.ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        public static void EnsureFolders()
        {
            foreach (var folder in new[] { Root, Root + "/Assets", Root + "/Layouts", Root + "/Materials", Root + "/Prefabs", Root + "/Scenes" })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                int slash = folder.LastIndexOf('/'); AssetDatabase.CreateFolder(folder.Substring(0, slash), folder.Substring(slash + 1));
            }
        }
        public static Material[] DefaultSurfaces()
        {
            EnsureFolders(); return new[] { Material("Rock", new Color(.48f, .51f, .55f)), Material("RockLight", new Color(.60f, .62f, .65f)), Material("Ore", new Color(.67f, .55f, .35f)), Material("Station", new Color(.76f, .78f, .80f)) };
        }
        static Material Material(string name, Color color)
        {
            string path = Root + "/Materials/" + name + ".mat"; var value = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (value) return value;
            value = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, enableInstancing = true };
            value.SetColor("_BaseColor", color); value.SetFloat("_Smoothness", name == "Station" ? .36f : .15f);
            AssetDatabase.CreateAsset(value, path); return value;
        }
        [MenuItem("Starframe/开发工具/创建地图工作室示例资源", false, 101)]
        public static void InstallExamples()
        {
            EnsureFolders(); var materials = DefaultSurfaces();
            var asteroid = Asset("Asteroid", "小行星", materials, VoxelAuthoring.Stamp(VoxelStampShape.Asteroid, new Vector3Int(18, 12, 16), Vector3Int.zero, 42, .5f, false, 0));
            var rock = Asset("Rock", "石块", materials, VoxelAuthoring.Stamp(VoxelStampShape.Asteroid, new Vector3Int(7, 5, 6), Vector3Int.zero, 27, .7f, false, 1));
            Asset("Fragment", "碎片", materials, VoxelAuthoring.Stamp(VoxelStampShape.Asteroid, new Vector3Int(3, 2, 4), Vector3Int.zero, 9, .5f, false, 0));
            var cells = new Dictionary<Vector3Int, VoxelCell>();
            void Box(Vector3Int center, Vector3Int size) { foreach (var cell in VoxelAuthoring.Stamp(VoxelStampShape.Box, size, center, 0, 0, false, 3)) cells[cell.position] = cell; }
            Box(new Vector3Int(0, -2, 0), new Vector3Int(36, 2, 24));
            Box(new Vector3Int(-13, 1, -8), new Vector3Int(4, 8, 4)); Box(new Vector3Int(13, 1, -8), new Vector3Int(4, 8, 4));
            Box(new Vector3Int(-13, 1, 8), new Vector3Int(4, 8, 4)); Box(new Vector3Int(13, 1, 8), new Vector3Int(4, 8, 4));
            Box(new Vector3Int(0, 5, -8), new Vector3Int(30, 2, 3)); Box(new Vector3Int(0, 5, 8), new Vector3Int(30, 2, 3));
            var station = Asset("MiningPlatform", "采矿基地", materials, cells.Values.ToList());
            const string path = Root + "/Layouts/MapWorkshopDemo.asset";
            if (!AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path))
            {
                var map = ScriptableObject.CreateInstance<SpaceMapDefinition>();
                map.instances.Add(new VoxelMapInstance { label = "Mining platform", asset = station, position = new Vector3(0, -180, 0), scale = 3 });
                var random = new System.Random(42);
                for (int i = 0; i < 20; i++)
                {
                    float angle = i * Mathf.PI * 2 / 20, radius = 220 + (float)random.NextDouble() * 260;
                    map.instances.Add(new VoxelMapInstance { asset = i % 3 == 0 ? rock : asteroid, label = "Asteroid " + (i + 1),
                        position = new Vector3(Mathf.Cos(angle) * radius, -160 - (float)random.NextDouble() * 170, Mathf.Sin(angle) * radius),
                        eulerAngles = new Vector3(0, (float)random.NextDouble() * 360, 0), scale = 1.5f + (float)random.NextDouble() * 2.5f });
                }
                AssetDatabase.CreateAsset(map, path);
            }
            AssetDatabase.SaveAssets();
        }
        static VoxelAssetDefinition Asset(string name, string category, Material[] materials, List<VoxelCell> cells)
        {
            string path = Root + "/Assets/" + name + ".asset"; var value = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
            if (value) return value;
            value = ScriptableObject.CreateInstance<VoxelAssetDefinition>(); value.name = name; value.category = category; value.hullModules = HullDatabase; value.surfaces = materials; value.cells = cells;
            AssetDatabase.CreateAsset(value, path); return value;
        }
    }
}
