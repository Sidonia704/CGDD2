using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Tables;
using UnityEngine.Rendering;
using Starframe.Construction;
using Starframe.Flight;
using L10n = Starframe.Presentation.L10n;

namespace Starframe.EditorTools
{
    /// <summary>Creates native assets for the one-cell flare countermeasure module.</summary>
    public static class FlareAssetInstall
    {
        const string Root = "Assets/_Starframe/";
        const string SourceRoot = Root + "assetsForCodex/";
        const string SourceModel = SourceRoot + "flare.fbx";
        const string ModelPath = Root + "Art/Models/Modules/Expansion/FlareLauncher.fbx";
        const string MeshPath = Root + "Art/Models/Construction/Expansion/FlareLauncherMesh.asset";
        const string OutlinePath = Root + "Art/Models/Construction/Expansion/FlareLauncherOutline.asset";
        const string DefinitionPath = Root + "Settings/Modules/FlareLauncher.asset";
        const string FlarePath = Root + "Settings/Modules/FlareLauncherCountermeasure.asset";
        const string IconRoot = Root + "Art/UI/Construction/Icons/Expansion/";
        const string MaterialsRoot = Root + "Art/Materials/Ships/";
        const float TargetPlanarWidth = .9f;
        const float MaximumThinHeightRatio = .35f;
        const float BoundsTolerance = .002f;

        public static void Install()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before installing the flare module.");

            var database = AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root + "Settings/Modules/ModuleDatabase.asset");
            if (!database) throw new InvalidOperationException("ModuleDatabase is missing.");
            var plastic = AssetDatabase.LoadAssetAtPath<Material>(MaterialsRoot + "AssemblyPlastic.mat");
            if (!plastic) throw new FileNotFoundException("AssemblyPlastic material is missing.", MaterialsRoot + "AssemblyPlastic.mat");
            if (!File.Exists(SourceModel)) throw new FileNotFoundException("Supplied flare model is missing.", SourceModel);
            if (!File.Exists(SourceRoot + "flare.png")) throw new FileNotFoundException("Normal flare icon is missing.", SourceRoot + "flare.png");
            if (!File.Exists(SourceRoot + "flares.png")) throw new FileNotFoundException("Selected flare icon is missing.", SourceRoot + "flares.png");

            Directory.CreateDirectory(Path.GetDirectoryName(ModelPath));
            Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
            Directory.CreateDirectory(IconRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(DefinitionPath));
            Directory.CreateDirectory(Root + "Settings/Modules/");

            // Import a project copy. Never modify the user-provided source FBX or its importer.
            File.Copy(SourceModel, ModelPath, true);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var sourceImporter = AssetImporter.GetAtPath(SourceModel) as ModelImporter;
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Copied flare FBX did not import as a model.");
            if (sourceImporter)
            {
                importer.globalScale = sourceImporter.globalScale;
                importer.useFileScale = sourceImporter.useFileScale;
                importer.bakeAxisConversion = sourceImporter.bakeAxisConversion;
            }
            importer.isReadable = true;
            importer.importNormals = ModelImporterNormals.Import;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = false;
            importer.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (!model) throw new InvalidOperationException("Copied flare FBX could not be loaded.");
            var worldBounds = MeasureMeshFilterWorldBounds(model);
            var baked = BakeThinPlate(model, worldBounds);
            var bakedBounds = baked.bounds;
            if (Mathf.Abs(bakedBounds.min.y + .5f) > BoundsTolerance)
                throw new InvalidOperationException("Baked flare mesh bottom must be local Y=-0.5; got " + bakedBounds.min.y.ToString("F5"));
            if (Mathf.Abs(Mathf.Max(bakedBounds.size.x, bakedBounds.size.z) - TargetPlanarWidth) > BoundsTolerance)
                throw new InvalidOperationException("Baked flare mesh maximum planar width must be approximately 0.9 m; got " + bakedBounds.size.ToString("F4"));
            ValidateSingleCellThinPlate(bakedBounds);

            var flare = AssetDatabase.LoadAssetAtPath<FlareDefinition>(FlarePath);
            if (!flare)
            {
                flare = ScriptableObject.CreateInstance<FlareDefinition>();
                flare.name = "FlareLauncherCountermeasure";
                AssetDatabase.CreateAsset(flare, FlarePath);
            }
            // Keep gameplay tuning authoritative in the dedicated native definition.
            flare.burstCount = 12;
            flare.cooldownSeconds = 10;
            EditorUtility.SetDirty(flare);

            var definition = database.Get(BlockShape.FlareLauncher);
            if (!definition) definition = AssetDatabase.LoadAssetAtPath<ModuleDefinition>(DefinitionPath);
            if (!definition)
            {
                definition = ScriptableObject.CreateInstance<ModuleDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            if (!database.modules.Contains(definition)) database.modules.Add(definition); // Append; preserve the existing module entries and order.
            definition.name = "FlareLauncher";
            definition.shape = BlockShape.FlareLauncher;
            definition.displayName = "Flare Launcher";
            definition.category = ModuleCategory.Weapon;
            definition.cost = 0;
            definition.massKg = 150;
            definition.maxHp = 60;
            definition.recoverySeconds = 3;
            definition.thrustNewtons = 0;
            definition.occupiedCells = new[] { Vector3Int.zero };
            definition.localExhaustDirections = Array.Empty<Vector3>();
            definition.localNozzlePositions = Array.Empty<Vector3>();
            definition.weapon = null;
            definition.countermeasure = flare;
            definition.mesh = StoreMesh(baked, MeshPath);
            definition.outline = StoreMesh(BlockGeometry.CreateOutline(BlockShape.FlareLauncher, .012f), OutlinePath);
            // Match the existing hard-bevel plastic modules; the full-cell mesh is only a placement preview.
            definition.drawOutline = false;
            definition.material = plastic;
            definition.icon = ImportIcon("flare.png", "FlareLauncher.png");
            definition.selectedIcon = ImportIcon("flares.png", "FlareLauncherSelected.png");
            definition.generationWatts = 0;
            definition.storageJoules = 0;
            definition.thrusterPowerWatts = 0;
            definition.shieldPowerWatts = 0;
            definition.shieldCapacity = 0;
            definition.shieldSurfaceMaterial = null;
            definition.shieldSurfaceMesh = null;

            EnsureStringEntry("module.FlareLauncher", "干扰弹发射器", "Flare Launcher");
            EnsureStringEntry("module.short.FlareLauncher", "干扰弹", "Flares");
            EnsureStringEntry("settings.action.Countermeasure", "释放干扰弹", "Deploy countermeasures");
            EnsureStringEntry("module.stat.flareSalvo", "齐射 {0} 枚 · 冷却 {1:0.#} 秒", "Salvo {0} · cooldown {1:0.#} s");
            EnsureStringEntry("module.stat.flareDecoyChance", "导弹诱偏概率 {0:0}%", "Missile decoy chance {0:0}%");
            EnsureStringEntry("module.stat.flareClearance", "发射器 +Y 至舰体边缘需留空格", "Keep the +Y cell column clear to the hull edge");
            EnsureStringEntry("app.helpFlightBody",
                "{0}/{1} 前进/后退  ·  {2}/{3} 左右平移  ·  {4}/{5} 上升/下降\n{6}/{7} 左右滚转  ·  {8} 加速  ·  {9} 锁定/解除目标  ·  {10} 释放干扰弹",
                "{0}/{1} Forward/back  ·  {2}/{3} Strafe left/right  ·  {4}/{5} Up/down\n{6}/{7} Roll left/right  ·  {8} Boost  ·  {9} Lock/unlock target  ·  {10} Deploy countermeasures");
            EnsureStringEntry("hud.flaresMissing", "{0}·干扰弹 — 未装配", "{0}·FLARES — NOT FITTED");
            EnsureStringEntry("hud.flaresStatus", "{0}·干扰弹 {1}/{2} 可用·{3:0.0}秒", "{0}·FLARES {1}/{2} READY·{3:0.0}s");
            EnsureStringEntry("hud.missileIncoming", "导弹来袭·{0:0}米", "MISSILE INCOMING·{0:0}m");
            EnsureStringEntry("hud.enemyLocked", "被 {0} 个敌方目标锁定", "TARGETED BY {0} HOSTILE(S)");
            EnsureStringEntry("hud.lock", "F · 瞄准红色标记锁定", "F · Aim at a red marker to lock");
            EnsureStringEntry("hud.radarInitial", "扫描中…", "Scanning...");
            EnsureStringEntry("hud.radarContacts", "{2:0} 米 · {0} 个接触目标 · {1}", "{2:0} m · {0} contacts · {1}");
            EnsureStringEntry("hud.combatHelp", "左键开火 · F 锁定/解除 · R 释放干扰弹 · 滚轮镜头 · Esc 退出 · F1 性能参数", "LMB Fire · F Lock/unlock · R Deploy flares · Wheel Camera · Esc Exit · F1 Diagnostics");
            EnsureStringEntry("hud.allHelp", "W/S 前后 · A/D 左右 · Space/Ctrl 上下 · Q/E 滚转 · Shift 加速 · 鼠标 偏移转向 · 左键 开火 · F 锁定/解除 · R 释放干扰弹 · 滚轮 镜头 · Esc 退出", "W/S Forward/back · A/D Strafe · Space/Ctrl Up/down · Q/E Roll · Shift Boost · Mouse Steer · LMB Fire · F Lock/unlock · R Deploy flares · Wheel Camera · Esc Exit");

            EditorUtility.SetDirty(definition);
            EditorUtility.SetDirty(database);
            BlockModules.Configure(database);
            BlockModules.InvalidateSurfaces();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            VerifyAssets(database, definition, flare);
            Debug.Log("Flare launcher installed. ModuleDatabase now contains " + database.modules.Count + " modules; authored bounds " + bakedBounds.size.ToString("F4") + ".");
        }

        static Bounds MeasureMeshFilterWorldBounds(GameObject model)
        {
            bool found = false;
            var result = new Bounds();
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (!renderer) continue;
                // Renderer.bounds is Unity's native MeshFilter/renderer world-space bound.
                if (!found) { result = renderer.bounds; found = true; }
                else result.Encapsulate(renderer.bounds);
            }
            if (!found || result.size.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException("Flare FBX has no measurable MeshFilter world bounds.");
            Debug.Log("Flare source MeshFilter world bounds: center=" + result.center.ToString("F5") + ", size=" + result.size.ToString("F5"));
            return result;
        }

        static Mesh BakeThinPlate(GameObject source, Bounds measuredWorldBounds)
        {
            var combines = new List<CombineInstance>();
            foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!filter.sharedMesh) continue;
                var renderer = filter.GetComponent<Renderer>();
                if (!renderer) continue;
                for (int submesh = 0; submesh < filter.sharedMesh.subMeshCount; submesh++)
                    combines.Add(new CombineInstance
                    {
                        mesh = filter.sharedMesh,
                        subMeshIndex = submesh,
                        // Keep the imported FBX root rotation and scale; they are part of its authored axis and unit conversion.
                        transform = filter.transform.localToWorldMatrix
                    });
            }
            if (combines.Count == 0) throw new InvalidOperationException("Flare FBX contains no mesh submeshes.");
            var mesh = new Mesh { name = "FlareLauncherThinPlate", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combines.ToArray(), true, true);
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            if (bounds.size.x < .0001f || bounds.size.y < .0001f || bounds.size.z < .0001f)
                throw new InvalidOperationException("Measured flare model has a degenerate dimension: " + bounds.size.ToString("F5"));

            // Renderer bounds and merged vertices are now both in imported world axes and units.
            if ((measuredWorldBounds.size - bounds.size).magnitude > Mathf.Max(.01f, bounds.size.magnitude * .05f))
                throw new InvalidOperationException("Flare MeshFilter world bounds disagree with the merged mesh: renderer=" + measuredWorldBounds.size.ToString("F5") + ", merged=" + bounds.size.ToString("F5"));

            float scale = TargetPlanarWidth / Mathf.Max(bounds.size.x, bounds.size.z);
            var vertices = mesh.vertices;
            float targetCenterY = -.5f + bounds.extents.y * scale;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3((vertices[i].x - bounds.center.x) * scale,
                    (vertices[i].y - bounds.center.y) * scale + targetCenterY,
                    (vertices[i].z - bounds.center.z) * scale);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            if (mesh.uv.Length == mesh.vertexCount) mesh.RecalculateTangents();
            return mesh;
        }

        static void ValidateSingleCellThinPlate(Bounds bounds)
        {
            const float halfCell = .5f + BoundsTolerance;
            if (bounds.min.x < -halfCell || bounds.min.y < -halfCell || bounds.min.z < -halfCell ||
                bounds.max.x > halfCell || bounds.max.y > halfCell || bounds.max.z > halfCell)
                throw new InvalidOperationException("Baked flare visual must remain inside its one-cell hull bounds; got " + bounds.ToString());
            float planarWidth = Mathf.Max(bounds.size.x, bounds.size.z);
            if (bounds.size.y >= planarWidth * MaximumThinHeightRatio)
                throw new InvalidOperationException("Baked flare visual is not a thin plate in local Y; size=" + bounds.size.ToString("F5"));
        }

        static Mesh StoreMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
                return existing;
            }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Sprite ImportIcon(string fileName, string outputName)
        {
            string destination = IconRoot + outputName;
            File.Copy(SourceRoot + fileName, destination, true);
            AssetDatabase.ImportAsset(destination, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(destination) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Could not configure flare icon importer: " + destination);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.spritePixelsPerUnit = 100;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destination);
            if (!sprite) throw new InvalidOperationException("Configured flare icon did not import as a Sprite: " + destination);
            return sprite;
        }

        static void EnsureStringEntry(string key, string chinese, string english)
        {
            var configuredLocales = LocalizationEditorSettings.GetLocales();
            var locales = new[]
            {
                configuredLocales.FirstOrDefault(locale => locale && locale.Identifier.Code == "zh-Hans"),
                configuredLocales.FirstOrDefault(locale => locale && locale.Identifier.Code == "en")
            };
            if (locales.Any(locale => !locale))
                throw new InvalidOperationException("Chinese and English locales must be installed before the flare StringTable entries can be authored.");
            var collection = LocalizationEditorSettings.GetStringTableCollection(L10n.StringTable);
            if (!collection) throw new InvalidOperationException("GameUI StringTable collection is missing; run localization setup first.");
            var values = new[] { chinese, english };
            for (int i = 0; i < locales.Length; i++)
            {
                var table = collection.GetTable(locales[i].Identifier) as StringTable;
                if (!table) throw new InvalidOperationException("GameUI StringTable is missing locale " + locales[i].Identifier.Code);
                var entry = table.GetEntry(key) ?? table.AddEntry(key, values[i]);
                entry.Value = values[i];
                entry.IsSmart = false;
                EditorUtility.SetDirty(table);
            }
            EditorUtility.SetDirty(collection);
            EditorUtility.SetDirty(collection.SharedData);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(FlareAssetInstall), collection);
        }

        public static string Verify(ModuleDatabase database = null)
        {
            database = database ? database : AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root + "Settings/Modules/ModuleDatabase.asset");
            if (!database) throw new InvalidOperationException("ModuleDatabase is missing.");
            var definition = database.Get(BlockShape.FlareLauncher);
            if (!definition) throw new InvalidOperationException("FlareLauncher is not indexed by ModuleDatabase.");
            if (!definition.countermeasure) throw new InvalidOperationException("FlareLauncher has no FlareDefinition.");
            if (definition.weapon) throw new InvalidOperationException("FlareLauncher must not be configured as a direct projectile weapon.");
            if (definition.occupiedCells == null || definition.occupiedCells.Length != 1 || definition.occupiedCells[0] != Vector3Int.zero)
                throw new InvalidOperationException("FlareLauncher must occupy exactly its origin cell.");
            if (!definition.mesh || !definition.icon || !definition.selectedIcon || !definition.material)
                throw new InvalidOperationException("FlareLauncher is missing a mesh, material, or icon reference.");
            var bounds = definition.mesh.bounds;
            if (Mathf.Abs(bounds.min.y + .5f) > BoundsTolerance ||
                Mathf.Abs(Mathf.Max(bounds.size.x, bounds.size.z) - TargetPlanarWidth) > BoundsTolerance)
                throw new InvalidOperationException("FlareLauncher mesh bounds failed verification: " + bounds.ToString());
            ValidateSingleCellThinPlate(bounds);
            return "PASS: FlareLauncher references, one-cell occupancy, local bottom, and planar bounds verified.";
        }

        static void VerifyAssets(ModuleDatabase database, ModuleDefinition definition, FlareDefinition flare)
        {
            if (database.modules.Count(d => d && d.shape == BlockShape.FlareLauncher) != 1)
                throw new InvalidOperationException("ModuleDatabase must contain exactly one FlareLauncher without changing its existing entries.");
            if (!AssetDatabase.Contains(definition) || !AssetDatabase.Contains(flare))
                throw new InvalidOperationException("Flare assets were not persisted as native Unity assets.");
            Debug.Log(Verify(database));
        }
    }
}
