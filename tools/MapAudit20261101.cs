using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Starframe.Maps;
using Starframe.Flight;

// Read-only inventory. Does not instantiate maps, save assets or change the active scene.
public static class MapAudit20261101
{
    public static object Diagnostics()
    {
        var assembly = typeof(Starframe.EditorTools.SpaceMapStudioWindow).Assembly;
        var type = assembly.GetType("Starframe.EditorTools.VoxelConnectivityDiagnostics");
        var map = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>("Assets/_Starframe/Maps/Layouts/MiningStationAsteroidField.asset");
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var summary = type.GetMethod("ForMap", flags).Invoke(null, new object[] { map });
        var legacy = assembly.GetType("Starframe.EditorTools.VisualPowerUpgrade");
        object[] apply = { null }, refresh = { null };
        bool applyReady = (bool)legacy.GetMethod("PreflightApply", flags).Invoke(null, apply);
        bool refreshReady = (bool)legacy.GetMethod("PreflightModelRefresh", flags).Invoke(null, refresh);
        return new { disconnectedAssets = summary.GetType().GetField("disconnectedAssetCount").GetValue(summary), affectedInstances = summary.GetType().GetField("disconnectedInstanceCount").GetValue(summary), initialIslandBodies = summary.GetType().GetField("possibleRigidBodyCount").GetValue(summary),
            legacyApplyReady = applyReady, legacyRefreshReady = refreshReady, applyMissing = apply[0], refreshMissing = refresh[0], note = "Only inspected cached diagnostics and preflights; did not execute legacy upgrades or mutate assets." };
    }
    static string[] Paths(string filter, string root) => AssetDatabase.FindAssets(filter, new[] { root }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x).ToArray();
    static float[] Vec(Vector3 value) => new[] { value.x, value.y, value.z };
    static object BoundsData(Bounds bounds) => new { center = Vec(bounds.center), size = Vec(bounds.size) };
    static Bounds Transform(Bounds bounds, Matrix4x4 matrix)
    {
        var result = new Bounds(matrix.MultiplyPoint3x4(bounds.min), Vector3.zero);
        for (int i = 0; i < 8; i++) result.Encapsulate(matrix.MultiplyPoint3x4(bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
        return result;
    }
    public static object Run()
    {
        var definitions = new Dictionary<VoxelAssetDefinition, string>();
        var colliderCounts = new Dictionary<VoxelAssetDefinition, int>();
        var assets = new List<object>();
        foreach (var path in Paths("t:VoxelAssetDefinition", "Assets/_Starframe/Maps"))
        {
            var asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path); definitions[asset] = path;
            bool valid = asset.Validate(out var error);
            var states = asset.cells.ToDictionary(c => c.position, c => new VoxelCellState { position = c.position, shape = c.shape, modelOffset = c.modelOffset, quarterTurns = c.quarterTurns, surface = c.surface });
            int islands = VoxelGeometry.Islands(states).Count;
            int colliderCount = VoxelGeometry.CollisionBoxes(states, asset.cellSize).Count; colliderCounts[asset] = colliderCount;
            assets.Add(new { path, valid, error, records = asset.cells.Count, asset.cellSize, bounds = BoundsData(asset.LocalBounds), asset.blockHealth, asset.damageResetSeconds, asset.massPerCell,
                faceConnectedIslands = islands, estimatedMergedBoxColliders = colliderCount,
                shapes = asset.cells.GroupBy(c => c.shape).Select(g => new { shape = g.Key.ToString(), count = g.Count() }).ToArray(),
                invalidSurfaces = asset.cells.Count(c => c.surface < 0 || c.surface >= asset.SurfaceCount),
                hullDatabase = AssetDatabase.GetAssetPath(asset.hullModules) });
        }
        var layouts = new List<object>();
        foreach (var path in Paths("t:SpaceMapDefinition", "Assets/_Starframe/Maps"))
        {
            var map = AssetDatabase.LoadAssetAtPath<SpaceMapDefinition>(path); bool valid = map.Validate(out var error);
            var bounds = new Bounds(); bool first = true;
            foreach (var item in map.instances.Where(i => i != null && i.asset)) { var next = Transform(item.asset.LocalBounds, item.Matrix); if (first) { bounds = next; first = false; } else bounds.Encapsulate(next); }
            layouts.Add(new { path, valid, error, size = Vec(map.size), spawn = Vec(map.playerSpawn), instances = map.instances.Count, records = map.instances.Where(i => i?.asset).Sum(i => (long)i.asset.cells.Count), estimatedMergedBoxColliders = map.instances.Where(i => i?.asset).Sum(i => colliderCounts[i.asset]), bounds = BoundsData(bounds),
                movingAtStart = map.instances.Count(i => i != null && (i.driftVelocity != Vector3.zero || i.angularVelocity != Vector3.zero)),
                parts = map.instances.Where(i => i?.asset).GroupBy(i => i.asset).Select(g => new { path = AssetDatabase.GetAssetPath(g.Key), instances = g.Count(), records = g.Key.cells.Count }).ToArray() });
        }
        var db = AssetDatabase.LoadAssetAtPath<ModuleDatabase>("Assets/_Starframe/Settings/Modules/ModuleDatabase.asset");
        var modules = db.modules.Select(m => new { path = AssetDatabase.GetAssetPath(m), shape = m ? m.shape.ToString() : "Missing", mesh = m && m.mesh, material = m && m.material, icon = m && m.icon, selectedIcon = m && m.selectedIcon }).ToArray();
        var references = new List<object>();
        foreach (var path in Paths("t:Model", "Assets/_Starframe/assetsForCodex/scenemodel").Where(x => x.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); Bounds local = new Bounds(), world = new Bounds(); bool first = true;
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh).ToArray();
            foreach (var filter in filters)
            {
                var localMatrix = prefab.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                var worldMatrix = filter.transform.localToWorldMatrix;
                if (filter.sharedMesh.isReadable)
                {
                    foreach (var vertex in filter.sharedMesh.vertices)
                    {
                        var l = localMatrix.MultiplyPoint3x4(vertex); var w = worldMatrix.MultiplyPoint3x4(vertex);
                        if (first) { local = new Bounds(l, Vector3.zero); world = new Bounds(w, Vector3.zero); first = false; }
                        else { local.Encapsulate(l); world.Encapsulate(w); }
                    }
                }
                else
                {
                    var l = Transform(filter.sharedMesh.bounds, localMatrix); var w = Transform(filter.sharedMesh.bounds, worldMatrix);
                    if (first) { local = l; world = w; first = false; } else { local.Encapsulate(l); world.Encapsulate(w); }
                }
            }
            references.Add(new { path, meshes = filters.Length, exactVertices = filters.All(f => f.sharedMesh.isReadable), rootPosition = Vec(prefab.transform.position), rootRotationEuler = Vec(prefab.transform.eulerAngles), rootScale = Vec(prefab.transform.localScale), rootLocalBounds = BoundsData(local), unityWorldBounds = BoundsData(world), importerScale = ((ModelImporter)AssetImporter.GetAtPath(path)).globalScale });
        }
        var guidOwners = new Dictionary<string, List<string>>();
        foreach (var file in Directory.EnumerateFiles("Assets", "*", SearchOption.AllDirectories).Where(p => new[] { ".asset", ".mat", ".unity", ".prefab" }.Contains(Path.GetExtension(p).ToLowerInvariant())))
        {
            if (new FileInfo(file).Length > 12000000) continue;
            string content = File.ReadAllText(file); if (!content.StartsWith("%YAML")) continue;
            foreach (Match match in Regex.Matches(content, @"guid: ([0-9a-f]{32})"))
            {
                string guid = match.Groups[1].Value; if (guid.StartsWith("0000000000000000")) continue;
                if (!guidOwners.TryGetValue(guid, out var owners)) guidOwners[guid] = owners = new List<string>();
                string owner = file.Replace('\\', '/'); if (!owners.Contains(owner)) owners.Add(owner);
            }
        }
        var missing = guidOwners.Select(pair => new { guid = pair.Key, resolved = AssetDatabase.GUIDToAssetPath(pair.Key), owners = pair.Value.ToArray() }).Where(item => string.IsNullOrEmpty(item.resolved) || item.resolved.StartsWith("Assets/") && !File.Exists(item.resolved) && !Directory.Exists(item.resolved)).ToArray();
        var activeRoots = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToList();
        var pipeline = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        if (pipeline) activeRoots.Add(AssetDatabase.GetAssetPath(pipeline));
        var activeDependencies = new HashSet<string>(AssetDatabase.GetDependencies(activeRoots.ToArray(), true));
        return new { project = Application.dataPath, unity = Application.unityVersion, playing = EditorApplication.isPlaying, scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path, assets, layouts, modules, references, missingReferences = missing,
            activeTemplateReferenceOwners = missing.SelectMany(item => item.owners).Where(activeDependencies.Contains).Distinct().ToArray(),
            note = "YAML reference scan excludes files over 12 MB; builtin GUIDs ignored. Model bounds use readable vertices (or conservative transformed AABB if exactVertices=false), imported prefab root-local vs Unity world, root scale and rotation applied once. No map runtime was constructed." };
    }
}
