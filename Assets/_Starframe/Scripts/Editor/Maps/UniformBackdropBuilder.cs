using System;
using System.Collections.Generic;
using System.IO;
using Starframe.Flight;
using Starframe.Maps;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Starframe.EditorTools
{
    /// <summary>Native editable one-unit rocks plus separate single-material instancing meshes.</summary>
    public static class UniformBackdropBuilder
    {
        public const string Root = "Assets/_Starframe/Maps/UniformVoxels/Backdrop";
        public const int VariantCount = 16;
        static int next, completed, errors;
        static bool running;
        static string latestError;
        static readonly List<string> builtPaths = new List<string>();

        public static float Diameter(int index)
        {
            CheckIndex(index);
            // Reduce the original rounded size by 30%; native cells and instance scale stay one unit.
            float originalDiameter = Mathf.Round(Mathf.Exp(Mathf.Lerp(Mathf.Log(24f), Mathf.Log(310f), (index / 2) / 7f)));
            return Mathf.Round(originalDiameter * .7f);
        }

        static string Name(int index) => "Backdrop_" + (index / 2).ToString("00") + "_D" +
            Diameter(index).ToString("000") + "_" + (index % 2 == 0 ? "A" : "B");
        public static string AssetPath(int index) => Root + "/Assets/" + Name(index) + ".asset";
        public static string MeshPath(int index) => Root + "/Meshes/" + Name(index) + ".asset";
        public static string RenderPath(int index) => Root + "/Render/" + Name(index) + ".asset";
        public static string MidPath(int index) => Root + "/LodMid/" + Name(index) + ".asset";
        public static string FarPath(int index) => Root + "/LodFar/" + Name(index) + ".asset";

        public static string BuildDisplayLods()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before preparing display LODs.");
            var report=new List<string>();
            for(int index=0;index<VariantCount;index++)BuildDisplayLods(index,report);
            Directory.CreateDirectory("Documentation/UniformVoxels");
            File.WriteAllLines("Documentation/UniformVoxels/BackdropDisplayLods.txt",report);
            return string.Join("\n",report);
        }

        static void BuildDisplayLods(int index,List<string> report)
        {
            Folder(Root+"/LodMid");Folder(Root+"/LodFar");
            var source=AssetDatabase.LoadAssetAtPath<Mesh>(RenderPath(index));
            if(!source)throw new InvalidOperationException("Missing exact native render mesh: "+index);
            for(int level=0;level<2;level++)
            {
                float error=level==0?4f:8f;
                var mesh=NativeRockLodBuilder.Build(source,error,out var stats);
                try
                {
                    StoreOptimizedRenderMesh(mesh,level==0?MidPath(index):FarPath(index),Name(index)+(level==0?" native mid display":" native far display"));
                    report?.Add(Name(index)+" errorLimit="+error+" actualError="+stats.maximumActualDisplacement+" triangles="+stats.inputTriangles+" -> "+stats.outputTriangles);
                }
                finally{UnityEngine.Object.DestroyImmediate(mesh);}
            }
        }

        public static object Status() => new { running, completed, total = VariantCount, next, errors, latestError,
            built = builtPaths.ToArray() };

        public static object Begin()
        {
            if (running || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop active backdrop work and Play mode before building.");
            next = completed = errors = 0;
            latestError = null;
            builtPaths.Clear();
            running = true;
            EditorApplication.update += Tick;
            return Status();
        }

        static void Tick()
        {
            if (!running || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (next == VariantCount)
            {
                running = false;
                EditorApplication.update -= Tick;
                return;
            }
            int index = next++;
            try { builtPaths.Add(BuildOne(index)); completed++; }
            catch (Exception error) { errors++; latestError = error.ToString(); Debug.LogException(error); }
        }

        public static string BuildOne(int index)
        {
            CheckIndex(index);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Backdrop generation requires Edit mode.");
            foreach (string folder in new[] { "/Assets", "/Meshes", "/Render" }) Folder(Root + folder);
            float diameter = Diameter(index);
            var ratio = index % 2 == 0 ? new Vector3(1f, .8f, .9f) : new Vector3(.8f, 1.1f, 1f);
            var bounds = new Bounds(Vector3.zero, ratio * diameter);
            var cells = PatchworkVoxelRocks.Build(bounds, 1f, 2000000, Name(index), out _);
            string path = AssetPath(index);
            var asset = AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(path);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<VoxelAssetDefinition>();
                AssetDatabase.CreateAsset(asset, path);
            }
            asset.name = Name(index);
            asset.category = "Uniform backdrop rocks";
            asset.cellSize = 1f;
            asset.cells = cells;
            asset.hullModules = SceneVoxelModules.GetOrCreate();
            asset.surfaces = new[] { AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Maps/Materials/Rock.mat") };
            asset.surfaceColors = new List<Color> { Color.white };
            asset.baseColor = Color.white;
            asset.initialLodMesh = null;
            asset.initialLodSignature = 0;
            asset.authoredPhysics = null; // Decorative assets never receive a physics body.
            asset.RebuildCellChunks();
            asset.NotifyChanged();
            EditorUtility.SetDirty(asset);
            var native = VoxelAssemblyBaker.Bake(asset, MeshPath(index));
            StoreRenderMesh(native, RenderPath(index), Name(index) + " worldunit");
            BuildDisplayLods(index,null);
            AssetDatabase.SaveAssetIfDirty(asset);
            return path;
        }

        static void StoreRenderMesh(Mesh native, string path, string meshName)
        {
            var optimized=DiagonalVoxelFaceMerger.Merge(native);
            try{StoreOptimizedRenderMesh(optimized,path,meshName);}
            finally{if(optimized!=native)UnityEngine.Object.DestroyImmediate(optimized);}
        }

        public static string OptimizeRenderMeshes()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before optimizing backdrop meshes.");
            var report=new List<string>();
            for(int index=0;index<VariantCount;index++)
            {
                var asset=AssetDatabase.LoadAssetAtPath<VoxelAssetDefinition>(AssetPath(index));
                if(!asset||!asset.HasValidInitialLod)throw new InvalidOperationException("Missing native source cache: "+index);
                var before=AssetDatabase.LoadAssetAtPath<Mesh>(RenderPath(index));
                long oldTriangles=VoxelAssemblyBaker.TriangleCount(before);
                StoreRenderMesh(asset.initialLodMesh,RenderPath(index),Name(index)+" worldunit");
                BuildDisplayLods(index,null);
                report.Add(Name(index)+": "+oldTriangles+" -> "+VoxelAssemblyBaker.TriangleCount(before));
            }
            Directory.CreateDirectory("Documentation/UniformVoxels");
            File.WriteAllLines("Documentation/UniformVoxels/BackdropVisibilityOptimization.txt",report);
            return string.Join("\n",report);
        }

        static void StoreOptimizedRenderMesh(Mesh native, string path, string meshName)
        {
            // Copy only: retain the original native material-slot cache for the voxel editor.
            var triangles = new List<int>();
            for (int slot = 0; slot < native.subMeshCount; slot++)
            {
                if (native.GetTopology(slot) != MeshTopology.Triangles)
                    throw new InvalidOperationException("Backdrop cache must contain triangles only.");
                triangles.AddRange(native.GetTriangles(slot));
            }
            var render = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!render)
            {
                render = new Mesh();
                AssetDatabase.CreateAsset(render, path);
            }
            render.Clear();
            render.indexFormat = native.indexFormat;
            render.vertices = native.vertices;
            render.normals = native.normals;
            render.uv = native.uv;
            render.tangents = native.tangents;
            var colors=new Color32[native.vertexCount];for(int i=0;i<colors.Length;i++)colors[i]=new Color32(255,255,255,255);render.colors32=colors;
            render.subMeshCount = 1;
            render.SetTriangles(triangles, 0, false);
            render.bounds = native.bounds;
            render.name = meshName;
            render.UploadMeshData(false);
            EditorUtility.SetDirty(render);
            AssetDatabase.SaveAssetIfDirty(render);
        }

        /// <summary>Save only the requested scene. Refuse to include any unsaved changes in that scene.</summary>
        public static int InstallToScene(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || running)
                throw new InvalidOperationException("Stop active backdrop work and Play mode before scene installation.");
            if (!scenePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath))
                throw new ArgumentException("Use an existing Assets scene path.", nameof(scenePath));
            var meshes = new Mesh[VariantCount];
            var mid=new Mesh[VariantCount];var far=new Mesh[VariantCount];
            var diameters = new float[VariantCount];
            for (int i = 0; i < VariantCount; i++)
            {
                meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(RenderPath(i));
                if (!meshes[i] || meshes[i].subMeshCount != 1 || meshes[i].vertexCount == 0)
                    throw new InvalidOperationException("Build every render variant first: " + RenderPath(i));
                diameters[i] = Diameter(i);
                mid[i]=AssetDatabase.LoadAssetAtPath<Mesh>(MidPath(i));far[i]=AssetDatabase.LoadAssetAtPath<Mesh>(FarPath(i));
                if(!mid[i]||!far[i])throw new InvalidOperationException("Build native display LODs first: "+i);
            }
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (!opened && scene.isDirty)
                throw new InvalidOperationException("The target scene has unsaved changes: " + scenePath);
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                var backdrops = new List<RangeAsteroidBackdrop>();
                foreach (var root in scene.GetRootGameObjects())
                    backdrops.AddRange(root.GetComponentsInChildren<RangeAsteroidBackdrop>(true));
                if (backdrops.Count == 0)
                    throw new InvalidOperationException("No existing RangeAsteroidBackdrop in " + scenePath);
                foreach (var backdrop in backdrops)
                {
                    backdrop.ConfigureUniformRocks(meshes, diameters);
                    backdrop.ConfigureUniformLods(mid,far);
                    var settings=new SerializedObject(backdrop);
                    bool mission=scenePath.EndsWith("/Gameplay/SaturnRing.unity",StringComparison.OrdinalIgnoreCase);
                    settings.FindProperty("maximumVisibleTriangles").intValue=mission?12000000:4000000;
                    settings.FindProperty("densityMultiplier").intValue=1;
                    if(mission){settings.FindProperty("farFadeStart").floatValue=28000;settings.FindProperty("farFadeEnd").floatValue=35000;}
                    settings.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(backdrop);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Could not save " + scenePath);
                return backdrops.Count;
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static void CheckIndex(int index)
        {
            if (index < 0 || index >= VariantCount) throw new ArgumentOutOfRangeException(nameof(index));
        }

        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            Folder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
