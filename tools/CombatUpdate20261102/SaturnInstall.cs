using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Starframe.EditorTools
{
    /// <summary>Installs a dedicated Saturn sky material into the two requested range scenes.</summary>
    public static class SaturnEnvironmentInstall
    {
        const string Root = "Assets/_Starframe/";
        const string SaturnSource = "tools/CombatUpdate20261102/TexturesSource/Saturn8k.png";
        const string StarSource = Root + "assetsForCodex/HDR_white_local_star_no_nebulae.hdr";
        const string SpaceTextureFolder = Root + "Art/Textures/Space/";
        const string SaturnAssetPath = SpaceTextureFolder + "Saturn8k.png";
        const string StarAssetPath = SpaceTextureFolder + "RangeStarfield.hdr";
        const string ShaderPath = Root + "Art/Shaders/Environment/SaturnRingSky.shader";
        const string MaterialFolder = Root + "Art/Materials/Environment/";
        const string MaterialPath = MaterialFolder + "SaturnRangeSky.mat";
        static readonly string[] ScenePaths =
        {
            Root + "Scenes/Testing/FlightRange.unity",
            Root + "Maps/ReferenceMiningStation/Scenes/ReferenceMiningStation3500.unity"
        };

        [MenuItem("Starframe/Environment/Install Saturn range sky")]
        public static void Install()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before installing the Saturn range sky.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string saturnSource = Path.Combine(projectRoot, SaturnSource);
            string starSource = Path.Combine(projectRoot, StarSource);
            if (!File.Exists(saturnSource)) throw new FileNotFoundException("Licensed Saturn texture source is missing.", saturnSource);
            if (!File.Exists(starSource)) throw new FileNotFoundException("Supplied range starfield HDRI is missing.", starSource);
            foreach (var scenePath in ScenePaths)
            {
                string sceneFile = Path.Combine(projectRoot, scenePath);
                if (!File.Exists(sceneFile)) throw new FileNotFoundException("Requested range scene is missing.", sceneFile);
            }

            Directory.CreateDirectory(SpaceTextureFolder);
            Directory.CreateDirectory(MaterialFolder);
            CopyOnlyIfMissing(saturnSource, SaturnAssetPath, projectRoot);
            CopyOnlyIfMissing(starSource, StarAssetPath, projectRoot);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ConfigureImporter(SaturnAssetPath, 8192, true, true);
            ConfigureImporter(StarAssetPath, 4096, false, true);

            var saturn = AssetDatabase.LoadAssetAtPath<Texture2D>(SaturnAssetPath);
            var stars = AssetDatabase.LoadAssetAtPath<Texture2D>(StarAssetPath);
            if (!saturn || saturn.width != 8192 || saturn.height != 4096)
                throw new InvalidDataException("Saturn8k.png did not import at its verified 8192 x 4096 resolution.");
            if (!stars) throw new InvalidOperationException("Copied starfield HDRI did not import as a Texture2D.");

            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Saturn sky shader is missing or unsupported: " + ShaderPath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
            {
                material = new Material(shader) { name = "SaturnRangeSky" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            else material.shader = shader;
            material.SetTexture("_StarTex", stars);
            material.SetTexture("_SaturnTex", saturn);
            material.SetFloat("_StarExposure", .45f);
            material.SetVector("_PlanetDirection", new Vector4(-1f, -.05f, .2f, 0f));
            material.SetVector("_PlanetPoleDirection", new Vector4(.18f, .97f, .24f, 0f));
            material.SetFloat("_PlanetRadius", 60f);
            material.SetFloat("_PlanetWorldRadius", 20000000f);
            material.SetVector("_PlanetCenterWS", new Vector4(-20300000f, -400000f, 800000f, 0f));
            material.SetFloat("_PlanetRotation", 0f);
            material.SetFloat("_PlanetBrightness", .38f);
            material.SetColor("_PlanetTint", new Color(.84f, .87f, .9f, 1f));
            material.SetVector("_PlanetLightDirection", new Vector4(.75f, .35f, -.3f, 0f));
            material.SetFloat("_AtmosphereStrength", .09f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            foreach (var scenePath in ScenePaths) InstallInScene(scenePath, material);
            AssetDatabase.SaveAssets();
        }

        static void CopyOnlyIfMissing(string source, string assetPath, string projectRoot)
        {
            string destination = Path.Combine(projectRoot, assetPath);
            if (File.Exists(destination)) return;
            FileUtil.CopyFileOrDirectory(source, assetPath);
        }

        static void ConfigureImporter(string assetPath, int maximumSize, bool sRgb, bool highQuality)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (!importer) throw new InvalidOperationException("Texture importer not found: " + assetPath);
            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = sRgb;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = highQuality ? 4 : 1;
            importer.maxTextureSize = maximumSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }

        static void InstallInScene(string scenePath, Material material)
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = SceneManager.GetSceneByPath(scenePath);
                if (!scene.IsValid() || !scene.isLoaded)
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new InvalidOperationException("Could not open scene for skybox assignment: " + scenePath);
                if (!SceneManager.SetActiveScene(scene))
                    throw new InvalidOperationException("Could not activate scene for skybox assignment: " + scenePath);
                RenderSettings.skybox = material;
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new IOException("Could not save the updated range scene: " + scenePath);
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
            }
        }
    }
}
