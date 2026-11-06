using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Starframe.Maps;
using Starframe.Construction;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Starframe.EditorTools
{
    public sealed partial class SpaceMapStudioWindow : EditorWindow
    {
        public const string ContentRoot = "Assets/_Starframe/Maps";
        const int MaximumUniformEditableCells = 2000000;
        const string UiRoot = "Assets/_Starframe/Scripts/Editor/Maps/UI/";
        [SerializeField] VoxelAssetDefinition asset;
        [SerializeField] SpaceMapDefinition map;
        [SerializeField] bool mapMode;
        [SerializeField] bool authoredSurfacePreview = true;
        [SerializeField] int tool, surface, selectedInstance = -1;
        [SerializeField] float planeHeight = -180, snap = 5;
        [SerializeField] Vector3Int brushSize = Vector3Int.one, stampSize = new Vector3Int(12, 8, 10), stampCenter;
        [SerializeField] VoxelStampShape stampShape = VoxelStampShape.Asteroid;
        [SerializeField] int stampSeed = 1701, scatterSeed = 42, scatterCount = 24;
        [SerializeField] float roughness = .35f, scatterScaleMin = 1, scatterScaleMax = 4;
        [SerializeField] bool shell;
        [SerializeField] Color paletteColor = new Color(.53f, .56f, .60f);
        [SerializeField] List<ScriptableObject> editedDefinitions = new List<ScriptableObject>();
        [SerializeField] BlockShape brushShape;
        [SerializeField] int brushTurns;
        [SerializeField] float brushRepeatDelay = .65f, brushRepeatInterval = .18f;
        Vector3Int placementTurns;
        bool rebuildingUi, rebuildRequested = true;
        Vector3Int selectedCell;
        Label status, info;
        VisualElement library, toolbar, viewportTools;
        ScrollView details;
        IMGUIContainer viewport;
        readonly List<VoxelAssetDefinition> libraryAssets = new List<VoxelAssetDefinition>();

        [MenuItem("Starframe/开发工具/体块地图工作室", false, 100)]
        public static void Open()
        {
            var window = GetWindow<SpaceMapStudioWindow>(); window.titleContent = new GUIContent("体块地图工作室");
            window.minSize = new Vector2(1040, 650); window.Show();
        }
        [OnOpenAsset]
        static bool OpenDefinition(int instanceId, int line)
        {
            var value = EditorUtility.InstanceIDToObject(instanceId);
            if (value is VoxelAssetDefinition voxel) { Open(); GetWindow<SpaceMapStudioWindow>().OpenAsset(voxel); return true; }
            if (value is SpaceMapDefinition level) { Open(); GetWindow<SpaceMapStudioWindow>().OpenMap(level); return true; }
            return false;
        }
        void OnEnable()
        {
            if (brushRepeatDelay < .25f) brushRepeatDelay = .65f;
            if (brushRepeatInterval < .1f) brushRepeatInterval = .18f;
            Undo.undoRedoPerformed += OnUndo; EditorApplication.projectChanged += OnProjectChanged;
            AssemblyReloadEvents.beforeAssemblyReload += DisposePreview; rebuildRequested = true;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }
        void OnDisable()
        {
            EndStroke();
            Undo.undoRedoPerformed -= OnUndo; EditorApplication.projectChanged -= OnProjectChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= DisposePreview; DisposePreview();
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }
        void OnPlayModeChanged(PlayModeStateChange state)
        {
            EndStroke();
            DisposePreview(); rebuildRequested = true; RefreshLibrary(); Repaint();
        }
        void OnUndo() { if (asset) asset.NotifyChanged(); MarkUnsaved(); rebuildRequested = true; BuildInspector(); RefreshLibrary(); Repaint(); }
        void OnProjectChanged() { if (!rebuildingUi) { RefreshLibrary(); rebuildRequested = true; Repaint(); } }
        public void CreateGUI()
        {
            var template = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UiRoot + "SpaceMapStudio.uxml");
            if (!template) { rootVisualElement.Add(new Label("地图工具 UI 正在导入，请重新打开窗口。")); return; }
            rootVisualElement.Clear(); template.CloneTree(rootVisualElement);
            toolbar = rootVisualElement.Q("toolbar"); library = rootVisualElement.Q("library");
            details = rootVisualElement.Q<ScrollView>("details"); viewport = rootVisualElement.Q<IMGUIContainer>("viewport");
            viewportTools = rootVisualElement.Q("viewportTools"); status = rootVisualElement.Q<Label>("status"); info = rootVisualElement.Q<Label>("viewportInfo");
            viewport.onGUIHandler = DrawViewport; viewport.focusable = true;
            BuildToolbar(); RefreshLibrary(); BuildInspector(); rebuildRequested = true;
            SetStatus("先制作可复用体块资产，再切换地图布局摆放。所有编辑都保存在独立地图数据中。");
        }
        public void OpenAsset(VoxelAssetDefinition value)
        {
            EndStroke();
            asset = value; mapMode = false; tool = 0; selectedInstance = -1; surface = 0; selectedCell = Vector3Int.zero;
            frameRequested = true; RefreshUi();
        }
        public void OpenMap(SpaceMapDefinition value)
        {
            EndStroke();
            map = value; mapMode = true; tool = 0; selectedInstance = -1; frameRequested = true; RefreshUi();
        }
        void RefreshUi() { BuildToolbar(); RefreshLibrary(); BuildInspector(); MarkUnsaved(); rebuildRequested = true; Repaint(); }
        void BuildToolbar()
        {
            if (toolbar == null) return; toolbar.Clear();
            Button( toolbar, "体块资产", () => { EndStroke(); mapMode = false; tool = 0; frameRequested = true; RefreshUi(); }, !mapMode);
            Button( toolbar, "地图布局", () => { EndStroke(); mapMode = true; tool = 0; frameRequested = true; RefreshUi(); }, mapMode);
            Button(toolbar, "新建", NewDefinition); Button(toolbar, "保存", SaveCurrent); Button(toolbar, "另存为", SaveAs);
            Button(toolbar, "撤销", Undo.PerformUndo); Button(toolbar, "重做", Undo.PerformRedo);
            Button(toolbar, "示例资源", () => { SpaceMapStudioContent.InstallExamples(); RefreshLibrary(); });
            Button(toolbar, "定位文件", () => { Selection.activeObject = mapMode ? (UnityEngine.Object)map : asset; });
            viewportTools.Clear();
            string[] names = mapMode ? new[] { "选择/移动", "摆放资产" } : new[] { "放置", "删除", "涂材质" };
            for (int i = 0; i < names.Length; i++) { int value = i; Button(viewportTools, names[i], () => { EndStroke(); tool = value; BuildToolbar(); BuildInspector(); }, tool == i); }
            Button(viewportTools, "聚焦 F", FrameView); Button(viewportTools, "俯视", () => { orbit = new Vector2(90, 0); Repaint(); });
            Button(viewportTools, "等轴", () => { orbit = new Vector2(30, -35); Repaint(); });
            var authoredToggle = new Toggle("体素合并缓存") { value = authoredSurfacePreview };
            authoredToggle.RegisterValueChangedCallback(e => { authoredSurfacePreview = e.newValue; rebuildRequested = true; Repaint(); });
            viewportTools.Add(authoredToggle);
            Button(viewportTools, "重置预览", () => { rebuildRequested = true; Repaint(); });
        }
        void RefreshLibrary()
        {
            if (library == null) return; rebuildingUi = true;
            try
            {
                library.Clear(); library.Add(Heading("资产库"));
                library.Add(Hint(mapMode ? "选择一个资产，然后用“摆放资产”在视窗中放置。" : "可复用的岩石、碎片、空间站等体块资产。"));
                libraryAssets.Clear();
                foreach (var guid in AssetDatabase.FindAssets("t:VoxelAssetDefinition"))
                { var value = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetDatabase.GUIDToAssetPath(guid)); if (value) libraryAssets.Add(value); }
                libraryAssets.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
                var search = new ToolbarSearchField(); library.Add(search); var list = new ScrollView(); list.AddToClassList("library-scroll"); library.Add(list);
                Action<string> populate = text =>
                {
                    list.Clear();
                    foreach (var value in libraryAssets)
                    {
                        if (!string.IsNullOrWhiteSpace(text) && (value.name + value.category).IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var button = Button(list, value.name + "\n" + value.cells.Count + " 格 · " + value.category, () =>
                        {
                            if (mapMode) { asset = value; surface = 0; RefreshLibrary(); BuildInspector(); } else OpenAsset(value);
                        }, value == asset);
                        button.AddToClassList("library-item");
                        if (value == asset) button.AddToClassList("selected");
                    }
                };
                search.RegisterValueChangedCallback(e => populate(e.newValue)); populate("");
                if (mapMode)
                {
                    library.Add(Heading("当前地图"));
                    var field = new ObjectField { objectType = typeof(SpaceMapDefinition), allowSceneObjects = false, value = map };
                    field.RegisterValueChangedCallback(e => OpenMap(e.newValue as SpaceMapDefinition)); library.Add(field);
                    Button(library, "导出地图场景", ExportMapScene).SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
                    Button(library, "打开物理试玩场景", OpenPhysicsSandbox).SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
                }
                else
                {
                    var field = new ObjectField { objectType = typeof(VoxelAssetDefinition), allowSceneObjects = false, value = asset };
                    field.RegisterValueChangedCallback(e => OpenAsset(e.newValue as VoxelAssetDefinition)); library.Add(field);
                    Button(library, "导出资产 Prefab", ExportAssetPrefab).SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
                }
            }
            finally { rebuildingUi = false; }
        }
        void BuildInspector()
        {
            if (details == null) return; details.Clear();
            if (mapMode) BuildMapInspector(); else BuildAssetInspector();
        }
        void BuildAuthoredSurfaceInspector()
        {
            details.Add(Section("体素合并缓存"));
            var field = new ObjectField("合并 Mesh") { objectType = typeof(Mesh), allowSceneObjects = false, value = asset.initialLodMesh };
            field.SetEnabled(false); details.Add(field);
            bool valid = VoxelAssemblyBaker.IsVoxelAssemblyCache(asset);
            string cacheInfo = valid ? "体块缓存符合合并工具的命名及签名约定；修改体块后会自动失效。" :
                asset.initialLodMesh && asset.initialLodMesh.name != asset.name + " Voxel Assembly" ? "旧外部显示缓存，点击“从体块重建显示”以替换。" :
                "当前没有匹配的体块缓存；视窗直接显示可编辑体块。";
            details.Add(new HelpBox(cacheInfo, valid ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning));
            if (asset.initialLodMesh) details.Add(Hint($"{asset.initialLodMesh.vertexCount:N0} 顶点 · {VoxelAssemblyBaker.TriangleCount(asset.initialLodMesh):N0} 三角形"));
            details.Add(Hint("场景由现有体素体块实际拼装，保留各块形状与方向。缓存只合并显示，不使用外加模型外壳；关闭视窗的“体素合并缓存”可直接检查体块。"));
            Button(details, "从体块重建显示", () => Safe(() =>
            {
                string sourcePath = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(sourcePath)) { SetStatus("请先保存体块资产，再重建显示。"); return; }
                string path = AssetDatabase.GetAssetPath(asset.initialLodMesh);
                if (!asset.initialLodMesh || asset.initialLodMesh.name != asset.name + " Voxel Assembly" ||
                    string.IsNullOrEmpty(path) || !string.Equals(Path.GetExtension(path), ".asset", StringComparison.OrdinalIgnoreCase) ||
                    AssetDatabase.IsSubAsset(asset.initialLodMesh) || AssetDatabase.LoadMainAssetAtPath(path) != asset.initialLodMesh)
                    path = AssetDatabase.GenerateUniqueAssetPath(Path.ChangeExtension(sourcePath, null) + "_VoxelAssembly.asset");
                var mesh = VoxelAssemblyBaker.Bake(asset, path);
                rebuildRequested = true; BuildInspector(); MarkUnsaved(); Repaint();
                SetStatus($"已从 {asset.cells.Count:N0} 个实际体块重建显示：{VoxelAssemblyBaker.TriangleCount(mesh):N0} 三角形。");
            })).SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
        }
        void BuildAssetInspector()
        {
            details.Add(Heading(asset ? asset.name : "体块资产"));
            if (!asset) { details.Add(Hint("新建资产，或从左侧资产库打开一个资产。")); return; }
            BuildAuthoredSurfaceInspector();
            StringField(details, "分类", asset.category, v => ChangeAsset("修改资产分类", () => asset.category = v, false));
            if (IsUniformVoxelAsset(asset))
            {
                var unit = new FloatField("格子尺寸 / 米") { value = 1 }; unit.SetEnabled(false); details.Add(unit);
                details.Add(Hint("统一体素资产固定为 1 米格。需要不同大小时，请选择或重建对应尺寸的资产；不会通过缩放拉伸体素。"));
            }
            else Float(details, "格子尺寸", asset.cellSize, .25f, 10, v => ChangeAsset("修改格子尺寸", () => asset.cellSize = v));
            Float(details, "体积质量密度", asset.massPerCell, .01f, 1000000, v => ChangeAsset("修改资产质量密度", () => asset.massPerCell = v, false));
            details.Add(Section("笔刷"));
            Float(details, "长按等待/秒", brushRepeatDelay, .25f, 2, v => brushRepeatDelay = v);
            Float(details, "连续间隔/秒", brushRepeatInterval, .1f, 1, v => brushRepeatInterval = v);
            details.Add(Hint("单击立即操作一格；等待长按阈值后才连续操作。松开或切换窗口立即停止，整笔可一次撤销。"));
            if (asset.hullModules)
            {
                var choices = Row(details);
                string[] labels = { "正方体", "斜面体", "半高方块", "四棱锥", "船体 5", "船体 6", "双格薄板", "八格船体" };
                for (int i = 0; i < VoxelHullAppearance.Shapes.Length; i++)
                {
                    var hullShape = VoxelHullAppearance.Shapes[i]; var definition = asset.hullModules.Get(hullShape);
                    var button = Button(choices, labels[i], () => { brushShape = hullShape; BuildInspector(); Repaint(); }, brushShape == hullShape);
                    button.AddToClassList("shape-choice");
                    if (definition && definition.icon)
                    {
                        var image = new Image { sprite = definition.icon, scaleMode = ScaleMode.ScaleToFit }; image.style.height = 34; image.style.width = 55;
                        button.Insert(0, image);
                    }
                }
                Button(details, "旋转 R · " + ((brushTurns % 4 + 4) % 4 * 90) + "°", () => { brushTurns = (brushTurns + 1) % 4; BuildInspector(); Repaint(); });
                details.Add(Hint("底面贴附光标格子面，R 绕此面旋转。双格薄板占 2 格，八格船体占 2×2×2；各格保留原模型切片，可单独涂色和编辑删除。"));
            }
            VectorInt(details, "笔刷尺寸", brushSize, v => brushSize = ClampSize(v, 12));
            if (asset.hullModules)
            {
                details.Add(Section("船体外观"));
                var modules = new ObjectField("共用模型/材质库") { objectType = typeof(Starframe.Flight.ModuleDatabase), value = asset.hullModules, allowSceneObjects = false };
                modules.SetEnabled(false); details.Add(modules);
                var tint = new ColorField("基础色") { value = asset.baseColor, showAlpha = false }; tint.RegisterValueChangedCallback(e => ChangeAsset("修改地图基础色", () => asset.baseColor = e.newValue)); details.Add(tint);
                details.Add(Hint("白色表示原始船体材质。基础色与颜色槽叠加着色，保留塑料高光、硬边倒角和接缝。不会修改飞船材质。"));
            }
            var options = new List<string>(); for (int i = 0; i < asset.SurfaceCount; i++) options.Add(asset.hullModules ? "颜色槽 " + (i + 1) : i + " · " + (i < asset.surfaces.Length && asset.surfaces[i] ? asset.surfaces[i].name : "默认岩石"));
            surface = Mathf.Clamp(surface, 0, options.Count - 1);
            var palette = new PopupField<string>(asset.hullModules ? "当前颜色槽" : "材质槽", options, surface); palette.RegisterValueChangedCallback(e => surface = options.IndexOf(e.newValue)); details.Add(palette);
            if (asset.hullModules)
            {
                if (asset.surfaceColors == null) asset.surfaceColors = new List<Color> { Color.white };
                for (int i = 0; i < asset.surfaceColors.Count; i++)
                {
                    int index = i; var slot = new ColorField("颜色槽 " + (i + 1)) { value = asset.surfaceColors[i], showAlpha = false };
                    slot.RegisterValueChangedCallback(e => ChangeAsset("修改地图颜色槽", () => asset.surfaceColors[index] = e.newValue)); details.Add(slot);
                }
                Button(details, "添加颜色槽", () => { ChangeAsset("添加地图颜色槽", () => asset.surfaceColors.Add(Color.white)); BuildInspector(); });
            }
            else
            {
            for (int i = 0; i < asset.surfaces.Length; i++)
            {
                int index = i; var field = new ObjectField("材质 " + i) { objectType = typeof(Material), value = asset.surfaces[i], allowSceneObjects = false };
                field.RegisterValueChangedCallback(e => ChangeAsset("替换地图材质", () => asset.surfaces[index] = e.newValue as Material)); details.Add(field);
            }
            var color = new ColorField("新材质颜色") { value = paletteColor }; color.RegisterValueChangedCallback(e => paletteColor = e.newValue); details.Add(color);
            Button(details, "添加独立颜色材质", AddPaletteMaterial);
            }
            details.Add(Section("批量造型"));
            var shape = new EnumField("形状", stampShape); shape.RegisterValueChangedCallback(e => stampShape = (VoxelStampShape)e.newValue); details.Add(shape);
            VectorInt(details, "尺寸", stampSize, v => stampSize = ClampSize(v, 64));
            VectorInt(details, "中心格", stampCenter, v => stampCenter = v);
            Integer(details, "随机种子", stampSeed, v => stampSeed = v);
            Float(details, "岩石粗糙度", roughness, 0, 1, v => roughness = v);
            var hollow = new Toggle("空心外壳") { value = shell }; hollow.RegisterValueChangedCallback(e => shell = e.newValue); details.Add(hollow);
            var row = Row(details); Button(row, "叠加", () => ApplyStamp(false, false)); Button(row, "挖空", () => ApplyStamp(true, false));
            Button(details, "替换为本造型…", () => { if (EditorUtility.DisplayDialog("替换造型", "替换当前资产的全部格子？可用撤销恢复。", "替换", "取消")) ApplyStamp(false, true); });
            details.Add(Section("工具"));
            Button(details, "把资产居中到原点", CenterAsset);
            Button(details, "复制当前资产", SaveAs);
            details.Add(Hint("中键旋转 · Shift+中键平移 · 滚轮缩放\n左键笔刷 · R 旋转体块 · X 放置/删除 · F 聚焦"));
        }
        void BuildMapInspector()
        {
            details.Add(Heading(map ? map.name : "地图布局"));
            if (!map) { details.Add(Hint("新建地图，或拖入一份 SpaceMapDefinition。")); return; }
            if (map.enforceUniformGrid)
                details.Add(new HelpBox("统一 1 米体素地图：对象缩放固定为 1。请从左侧选择所需尺寸的资产，位置和旋转仍可自由编辑。", HelpBoxMessageType.Info));
            var connectivity = VoxelConnectivityDiagnostics.ForMap(map);
            if (connectivity.disconnectedInstanceCount > 0)
                details.Add(new HelpBox($"地图中有 {connectivity.disconnectedAssetCount} 份不连通资产，涉及 {connectivity.disconnectedInstanceCount} 个实例。首次致命命中后，这些实例可能分离为合计 {connectivity.possibleRigidBodyCount} 个刚体。建议按连通组件重新编辑对应资产。", HelpBoxMessageType.Warning));
            Button(details, "重新检查连通性", BuildInspector);
            Vector(details, "地图尺寸", map.size, v => ChangeMap("修改地图边界", () => map.size = Vector3.Max(Vector3.one, v), false));
            Vector(details, "玩家出生点", map.playerSpawn, v => ChangeMap("修改玩家出生点", () => map.playerSpawn = v, false));
            Vector(details, "玩家朝向", map.playerEulerAngles, v => ChangeMap("修改玩家朝向", () => map.playerEulerAngles = v, false));
            Float(details, "摆放平面 Y", planeHeight, -50000, 50000, v => { planeHeight = v; gridDirty = true; Repaint(); });
            Float(details, "移动吸附/米", snap, .25f, 100, v => snap = v);
            details.Add(Hint("建议在 Y<0 的下半区制作矿带和基地，上半区留给飞行。可用坐标字段自由调整高度。"));
            details.Add(Section("场景对象 · " + map.instances.Count));
            var names = new List<string> { "未选择" }; for (int i = 0; i < map.instances.Count; i++) names.Add((i + 1) + " · " + map.instances[i].label);
            var select = new PopupField<string>(names, Mathf.Clamp(selectedInstance + 1, 0, names.Count - 1));
            select.RegisterValueChangedCallback(e => { selectedInstance = names.IndexOf(e.newValue) - 1; BuildInspector(); Repaint(); }); details.Add(select);
            if (selectedInstance >= 0 && selectedInstance < map.instances.Count)
            {
                var item = map.instances[selectedInstance];
                StringField(details, "名称", item.label, v => ChangeMap("重命名地图对象", () => item.label = v, false));
                var field = new ObjectField("资产") { objectType = typeof(VoxelAssetDefinition), value = item.asset, allowSceneObjects = false };
                field.RegisterValueChangedCallback(e =>
                {
                    var replacement = e.newValue as VoxelAssetDefinition;
                    if (!replacement || !CanPlaceAsset(replacement)) { field.SetValueWithoutNotify(item.asset); return; }
                    ChangeMap("替换地图对象", () => { item.asset = replacement; if (map.enforceUniformGrid) item.scale = 1; });
                    BuildInspector();
                }); details.Add(field);
                if (item.asset)
                {
                    var size = item.asset.LocalBounds.size;
                    details.Add(Hint($"资产尺寸：{size.x:0.##} × {size.y:0.##} × {size.z:0.##} 米"));
                }
                Vector(details, "位置", item.position, v => MoveInstance(item, v, item.eulerAngles, item.scale));
                Vector(details, "旋转", item.eulerAngles, v => MoveInstance(item, item.position, v, item.scale));
                if (map.enforceUniformGrid)
                {
                    var scale = new FloatField("统一缩放") { value = 1 }; scale.SetEnabled(false); details.Add(scale);
                }
                else Float(details, "统一缩放", item.scale, .25f, 100, v => MoveInstance(item, item.position, item.eulerAngles, v));
                Vector(details, "漂移/米秒", item.driftVelocity, v => ChangeMap("修改漂移", () => item.driftVelocity = v, false));
                Vector(details, "旋转/度秒", item.angularVelocity, v => ChangeMap("修改漂浮旋转", () => item.angularVelocity = v, false));
                var row = Row(details); Button(row, "复制", DuplicateInstance); Button(row, "删除", DeleteInstance);
            }
            details.Add(Section("下半区批量散布"));
            Integer(details, "数量", scatterCount, v => scatterCount = Mathf.Clamp(v, 1, 200));
            Integer(details, "种子", scatterSeed, v => scatterSeed = v);
            if (map.enforceUniformGrid)
                details.Add(Hint("按选中资产的实际尺寸散布，缩放固定为 1。不同大小请分别选择已有尺寸版本后散布。"));
            else
            {
                Float(details, "最小缩放", scatterScaleMin, .25f, 100, v => scatterScaleMin = v);
                Float(details, "最大缩放", scatterScaleMax, .25f, 100, v => scatterScaleMax = v);
            }
            Button(details, "散布左侧选中的资产", Scatter);
            details.Add(Section("敌方出生点（供后续波次使用）"));
            for (int i = 0; i < map.enemySpawns.Count; i++)
            {
                int index = i; Vector(details, "出生点 " + (i + 1), map.enemySpawns[i], v => ChangeMap("修改敌方出生点", () => map.enemySpawns[index] = v, false));
            }
            var points = Row(details); Button(points, "添加", () => { ChangeMap("添加敌方出生点", () => map.enemySpawns.Add(pivot), false); BuildInspector(); });
            Button(points, "删除最后一个", () => { if (map.enemySpawns.Count > 0) { ChangeMap("删除敌方出生点", () => map.enemySpawns.RemoveAt(map.enemySpawns.Count - 1), false); BuildInspector(); } });
            details.Add(Hint("选择模式：左键选取并拖动 · R 旋转 90°\nCtrl+D 复制 · Delete 删除 · F 聚焦\n破坏预览不影响原始资产和已保存地图。"));
        }

        void ChangeAsset(string label, Action action, bool rebuild = true)
        {
            if (!asset) return; Undo.RecordObject(asset, label); action();
            if (IsUniformVoxelAsset(asset)) asset.cellSize = 1;
            asset.RebuildCellChunks(); asset.NotifyChanged(); EditorUtility.SetDirty(asset);
            if (!editedDefinitions.Contains(asset)) editedDefinitions.Add(asset);
            if (rebuild) rebuildRequested = true; MarkUnsaved(); Repaint();
        }
        void ChangeMap(string label, Action action, bool rebuild = true)
        {
            if (!map) return; Undo.RecordObject(map, label); action(); EditorUtility.SetDirty(map);
            if (!editedDefinitions.Contains(map)) editedDefinitions.Add(map);
            if (rebuild) rebuildRequested = true; MarkUnsaved(); Repaint();
        }
        void MarkUnsaved()
        {
            editedDefinitions.RemoveAll(value => !value || !EditorUtility.IsDirty(value));
            hasUnsavedChanges = editedDefinitions.Count > 0 || (asset && EditorUtility.IsDirty(asset)) || (map && EditorUtility.IsDirty(map));
            saveChangesMessage = "保存地图工作室中的资产和地图修改？";
        }
        public override void SaveChanges() { SaveCurrent(); base.SaveChanges(); }
        void SaveCurrent()
        {
            if (asset) AssetDatabase.SaveAssetIfDirty(asset); if (map) AssetDatabase.SaveAssetIfDirty(map);
            foreach (var value in editedDefinitions) if (value) AssetDatabase.SaveAssetIfDirty(value);
            editedDefinitions.Clear();
            hasUnsavedChanges = false; SetStatus("已保存资产和地图。GUID 引用保持不变。");
        }
        void NewDefinition()
        {
            SpaceMapStudioContent.EnsureFolders();
            string path = EditorUtility.SaveFilePanelInProject(mapMode ? "新建地图" : "新建体块资产", mapMode ? "SpaceMap" : "VoxelAsset", "asset", "保存开发数据", ContentRoot + (mapMode ? "/Layouts" : "/Assets"));
            if (string.IsNullOrEmpty(path)) return;
            if (mapMode) { var value = CreateInstance<SpaceMapDefinition>(); value.enforceUniformGrid = true; AssetDatabase.CreateAsset(value, path); OpenMap(value); }
            else { var value = CreateInstance<VoxelAssetDefinition>(); value.cellSize = 1; value.hullModules = SpaceMapStudioContent.HullDatabase; value.surfaces = SpaceMapStudioContent.DefaultSurfaces(); AssetDatabase.CreateAsset(value, path); OpenAsset(value); }
            SaveCurrent();
        }
        void SaveAs()
        {
            UnityEngine.Object source = mapMode ? (UnityEngine.Object)map : asset; if (!source) return;
            string path = EditorUtility.SaveFilePanelInProject("另存为", source.name + "_Copy", "asset", "创建独立副本", Path.GetDirectoryName(AssetDatabase.GetAssetPath(source)));
            if (string.IsNullOrEmpty(path) || path == AssetDatabase.GetAssetPath(source)) return;
            var copy = Instantiate(source); copy.name = Path.GetFileNameWithoutExtension(path); AssetDatabase.CreateAsset(copy, path);
            if (copy is SpaceMapDefinition level) OpenMap(level); else OpenAsset((VoxelAssetDefinition)copy); SaveCurrent();
        }
        static bool IsUniformVoxelAsset(VoxelAssetDefinition value) => value &&
            AssetDatabase.GetAssetPath(value).Replace('\\', '/').IndexOf("/UniformVoxels/", StringComparison.OrdinalIgnoreCase) >= 0;
        int EditableCellLimit => IsUniformVoxelAsset(asset) ? MaximumUniformEditableCells : VoxelAssetDefinition.MaximumCells;
        bool CanPlaceAsset(VoxelAssetDefinition value)
        {
            if (map && map.enforceUniformGrid && value && value.cellSize != 1f)
            { SetStatus("此地图只接受 1 米格资产。请选择对应实际尺寸的统一体素资产。"); return false; }
            return true;
        }
        void SetStatus(string text) { if (status != null) status.text = text; }
        void Safe(Action action) { try { action(); } catch (Exception ex) { SetStatus(ex.Message); Debug.LogException(ex); } }
        static Button Button(VisualElement parent, string text, Action action, bool active = false)
        {
            var button = new Button(() => action()) { text = text }; if (active) button.AddToClassList("active"); parent.Add(button); return button;
        }
        static VisualElement Row(VisualElement parent) { var row = new VisualElement(); row.AddToClassList("row"); parent.Add(row); return row; }
        static Label Heading(string text) { var value = new Label(text); value.AddToClassList("heading"); return value; }
        static Label Section(string text) { var value = new Label(text); value.AddToClassList("section"); return value; }
        static Label Hint(string text) { var value = new Label(text); value.AddToClassList("hint"); return value; }
        static void StringField(VisualElement parent, string name, string value, Action<string> apply)
        { var field = new TextField(name) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => apply(e.newValue)); parent.Add(field); }
        static void Float(VisualElement parent, string name, float value, float min, float max, Action<float> apply)
        { var field = new FloatField(name) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => { float v = float.IsNaN(e.newValue) || float.IsInfinity(e.newValue) ? value : Mathf.Clamp(e.newValue, min, max); field.SetValueWithoutNotify(v); apply(v); }); parent.Add(field); }
        static void Integer(VisualElement parent, string name, int value, Action<int> apply)
        { var field = new IntegerField(name) { value = value, isDelayed = true }; field.RegisterValueChangedCallback(e => apply(e.newValue)); parent.Add(field); }
        static void Vector(VisualElement parent, string name, Vector3 value, Action<Vector3> apply)
        { var field = new Vector3Field(name) { value = value }; field.AddToClassList("studio-vector"); field.RegisterValueChangedCallback(e => { var v = e.newValue; if (!float.IsNaN(v.sqrMagnitude) && !float.IsInfinity(v.sqrMagnitude)) apply(v); }); parent.Add(field); }
        static void VectorInt(VisualElement parent, string name, Vector3Int value, Action<Vector3Int> apply)
        { var field = new Vector3IntField(name) { value = value }; field.AddToClassList("studio-vector"); field.RegisterValueChangedCallback(e => apply(e.newValue)); parent.Add(field); }
        static Vector3Int ClampSize(Vector3Int value, int max) => new Vector3Int(Mathf.Clamp(value.x, 1, max), Mathf.Clamp(value.y, 1, max), Mathf.Clamp(value.z, 1, max));
    }
}
