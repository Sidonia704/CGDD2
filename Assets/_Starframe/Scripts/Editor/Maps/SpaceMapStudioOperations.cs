using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Maps;
using Starframe.Construction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starframe.EditorTools
{
    public sealed partial class SpaceMapStudioWindow
    {
        void PaintCells(Vector3Int center, int operation)
        {
            if (!asset) return;
            var positions = new HashSet<Vector3Int>(asset.cells.Select(c => c.position));
            var start = center - new Vector3Int(brushSize.x / 2, brushSize.y / 2, brushSize.z / 2);
            int changed = 0;
            ChangeAsset(operation == 0 ? "放置地图体块" : operation == 1 ? "删除地图体块" : "涂绘地图材质", () =>
            {
                var targets = new HashSet<Vector3Int>();
                for (int x = 0; x < brushSize.x; x++) for (int y = 0; y < brushSize.y; y++) for (int z = 0; z < brushSize.z; z++) targets.Add(start + new Vector3Int(x, y, z));
                if (operation == 0)
                {
                    foreach (var anchor in targets) changed += AddHullPiece(asset.cells, positions, anchor, brushShape, placementTurns, surface, asset.hullModules);
                }
                else if (operation == 1) changed = asset.cells.RemoveAll(c => targets.Contains(c.position));
                else foreach (var cell in asset.cells) if (targets.Contains(cell.position) && cell.surface != surface) { cell.surface = surface; changed++; }
            });
            SetStatus($"已修改 {changed} 格。资产共 {asset.cells.Count:N0} 格。Ctrl+Z 可撤销。" );
        }
        void ApplyStamp(bool erase, bool replace)
        {
            if (!asset) return;
            Safe(() =>
            {
                var stamp = VoxelAuthoring.Stamp(stampShape, stampSize, stampCenter, stampSeed, roughness, shell, surface);
                var next = replace ? new List<VoxelCell>() : asset.cells.Select(cell => cell.Copy()).ToList();
                var positions = new HashSet<Vector3Int>(next.Select(cell => cell.position));
                if (erase) { var remove = new HashSet<Vector3Int>(stamp.Select(cell => cell.position)); next.RemoveAll(cell => remove.Contains(cell.position)); }
                else
                {
                    var turns = new Vector3Int(0, (brushTurns % 4 + 4) % 4, 0);
                    foreach (var anchor in stamp) AddHullPiece(next, positions, anchor.position, brushShape, turns, surface, asset.hullModules);
                }
                ChangeAsset(erase ? "挖空体块资产" : "生成体块造型", () => { asset.cells = next; });
                frameRequested = true; SetStatus($"造型已应用，共 {asset.cells.Count:N0} 格。保存后可在地图中反复使用。");
            });
        }
        public static int AddHullPiece(List<VoxelCell> output, HashSet<Vector3Int> occupied, Vector3Int anchor, BlockShape shape, Vector3Int turns, int color, Starframe.Flight.ModuleDatabase database = null)
        {
            var definition = database ? database.Get(shape) : null;
            var offsets = definition && definition.occupiedCells != null && definition.occupiedCells.Length > 0 ? definition.occupiedCells : BlockModules.LocalCells(shape);
            if (output.Count + offsets.Length > VoxelAssetDefinition.MaximumCells) return 0;
            var rotation = Quaternion.Euler((Vector3)turns * 90);
            foreach (var offset in offsets) if (occupied.Contains(anchor + Vector3Int.RoundToInt(rotation * (Vector3)offset))) return 0;
            foreach (var offset in offsets)
            {
                var position = anchor + Vector3Int.RoundToInt(rotation * (Vector3)offset);
                occupied.Add(position); output.Add(new VoxelCell { position = position, surface = color, shape = shape, quarterTurns = turns, modelOffset = offset });
            }
            return offsets.Length;
        }
        void CenterAsset()
        {
            if (!asset || asset.cells.Count == 0) return;
            Vector3Int offset = Vector3Int.RoundToInt(asset.LocalBounds.center / asset.cellSize);
            ChangeAsset("居中体块资产", () => { foreach (var cell in asset.cells) cell.position -= offset; }); frameRequested = true;
        }
        void AddPaletteMaterial()
        {
            if (!asset) return; SpaceMapStudioContent.EnsureFolders();
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { enableInstancing = true };
            material.SetColor("_BaseColor", paletteColor); material.SetFloat("_Smoothness", .18f);
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(ContentRoot + "/Materials/" + asset.name + "_Surface.mat"));
            ChangeAsset("添加资产材质", () => { var list = asset.surfaces.ToList(); list.Add(material); asset.surfaces = list.ToArray(); surface = list.Count - 1; }); BuildInspector();
        }
        void PlaceInstance(Vector3 position)
        {
            if (!map || !asset || asset.cells.Count == 0) { SetStatus("请先从左侧选择一份非空的体块资产。"); return; }
            ChangeMap("摆放地图资产", () => { map.instances.Add(new VoxelMapInstance { label = asset.name, asset = asset, position = position }); selectedInstance = map.instances.Count - 1; });
            BuildInspector(); SetStatus("已摆放 " + asset.name + "。在选择模式拖动，右侧可精确输入 XYZ 和旋转。");
        }
        void MoveInstance(VoxelMapInstance item, Vector3 position, Vector3 euler, float scale)
        {
            ChangeMap("移动地图对象", () => { item.position = position; item.eulerAngles = euler; item.scale = scale; }, false);
            foreach (var previewItem in previewItems) if (previewItem.index < map.instances.Count && map.instances[previewItem.index] == item)
            { previewItem.runtime.transform.localPosition = position; previewItem.runtime.transform.localRotation = Quaternion.Euler(euler); previewItem.runtime.transform.localScale = Vector3.one * scale; }
            renderDirty = true;
        }
        void DuplicateInstance()
        {
            if (!map || selectedInstance < 0 || selectedInstance >= map.instances.Count) return;
            var copy = map.instances[selectedInstance].Copy(); copy.position += Vector3.right * snap; copy.label += " Copy";
            ChangeMap("复制地图对象", () => { map.instances.Add(copy); selectedInstance = map.instances.Count - 1; }); BuildInspector();
        }
        void DeleteInstance()
        {
            if (!map || selectedInstance < 0 || selectedInstance >= map.instances.Count) return;
            ChangeMap("删除地图对象", () => { map.instances.RemoveAt(selectedInstance); selectedInstance = -1; }); BuildInspector();
        }
        void Scatter()
        {
            if (!map || !asset || asset.cells.Count == 0) { SetStatus("请先选择要散布的资产。"); return; }
            var random = new System.Random(scatterSeed); var add = new List<VoxelMapInstance>();
            float min = Mathf.Min(scatterScaleMin, scatterScaleMax), max = Mathf.Max(scatterScaleMin, scatterScaleMax);
            for (int i = 0; i < Mathf.Clamp(scatterCount, 1, 200); i++)
            {
                float scale = Mathf.Lerp(min, max, (float)random.NextDouble());
                float radius = (asset.LocalBounds.extents.magnitude + asset.LocalBounds.center.magnitude) * scale;
                var half = map.size * .5f;
                if (radius >= Mathf.Min(half.x, Mathf.Min(half.z, half.y * .5f))) { SetStatus("资产缩放过大，无法完全放在地图下半区。请减小缩放。"); return; }
                float Range(float a, float b) => Mathf.Lerp(a, b, (float)random.NextDouble());
                add.Add(new VoxelMapInstance { asset = asset, label = asset.name + " " + (i + 1), scale = scale,
                    position = new Vector3(Range(-half.x + radius, half.x - radius), Range(-half.y + radius, -radius), Range(-half.z + radius, half.z - radius)),
                    eulerAngles = new Vector3(Range(-30, 30), Range(0, 360), Range(-30, 30)) });
            }
            ChangeMap("散布小行星带资产", () => map.instances.AddRange(add)); selectedInstance = -1; frameRequested = true;
            BuildInspector(); SetStatus($"新增 {add.Count} 个资产实例，均在 Y<0 的下半区。可单独修改每一个对象。");
        }
        void ExportAssetPrefab()
        {
            if (!CanExport()) return;
            if (!asset || asset.cells.Count == 0) { SetStatus("资产为空。请先添加一些体块。"); return; }
            SaveCurrent(); SpaceMapStudioContent.EnsureFolders();
            string path = EditorUtility.SaveFilePanelInProject("导出可破坏资产 Prefab", asset.name, "prefab", "Prefab 在运行时加载共享体块数据", ContentRoot + "/Prefabs");
            if (string.IsNullOrEmpty(path)) return;
            var scene = EditorSceneManager.NewPreviewScene(); var root = new GameObject(asset.name);
            try
            {
                SceneManager.MoveGameObjectToScene(root, scene); root.AddComponent<VoxelStructureRuntime>().asset = asset;
                PrefabUtility.SaveAsPrefabAsset(root, path); SetStatus("已导出 " + path);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        void ExportMapScene()
        {
            if (!CanExport()) return;
            if (!map) return; if (!map.Validate(out string error)) { SetStatus(error); return; }
            SaveCurrent(); SpaceMapStudioContent.EnsureFolders();
            string path = EditorUtility.SaveFilePanelInProject("导出地图场景", map.name, "unity", "导出环境场景；后续关卡脚本可使用出生点数据", ContentRoot + "/Scenes");
            if (string.IsNullOrEmpty(path)) return;
            ExportScene(map, path); SetStatus("已导出环境场景。未加入游戏首页或出击流程：" + path);
        }
        void OpenPhysicsSandbox()
        {
            if (!CanExport()) return;
            if (!map) return;
            if (!map.Validate(out var error)) { SetStatus(error); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SaveCurrent(); SpaceMapStudioContent.EnsureFolders();
            string path = ContentRoot + "/Scenes/StudioPhysicsPreview.unity";
            ExportScene(map, path, true); EditorSceneManager.OpenScene(path);
            SetStatus("已打开独立物理试玩场景。按 Unity Play 后可自由飞行、观察碰撞和射击破坏；用 Stop 返回编辑。");
        }
        public static void ExportScene(SpaceMapDefinition definition, string path, bool sandbox = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先停止 Unity Play，再导出地图场景。");
            var previous = SceneManager.GetActiveScene(); var sky = RenderSettings.skybox;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Space Map"); root.AddComponent<SpaceMapRuntime>().definition = definition;
                var camera = new GameObject("Map overview camera", typeof(Camera)); camera.tag = "MainCamera";
                camera.transform.position = new Vector3(0, definition.size.y * .22f, -definition.size.z * .65f);
                camera.transform.rotation = Quaternion.LookRotation(new Vector3(0, -definition.size.y * .12f, 0) - camera.transform.position, Vector3.up);
                camera.GetComponent<Camera>().farClipPlane = Mathf.Max(5000, definition.size.magnitude * 2); camera.GetComponent<Camera>().fieldOfView = 50;
                if (sandbox) camera.AddComponent<MapSandboxCamera>();
                var sun = new GameObject("Star", typeof(Light)); sun.GetComponent<Light>().type = LightType.Directional;
                sun.GetComponent<Light>().intensity = 1.2f; sun.transform.rotation = Quaternion.Euler(45, -30, 0);
                RenderSettings.skybox = sky; RenderSettings.ambientLight = new Color(.2f, .23f, .28f);
                EditorSceneManager.SaveScene(scene, path);
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (previous.IsValid()) SceneManager.SetActiveScene(previous); }
        }
        bool CanExport()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            SetStatus("请先停止 Unity Play，再导出或打开物理试玩场景。"); return false;
        }
    }
}
