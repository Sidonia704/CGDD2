using System;
using UnityEditor;
using UnityEngine;

namespace Starframe.EditorTools
{
    /// <summary>Updates the shared range sky material with the distant, large-scale planet.</summary>
    public static class RangeBackdropPlanetInstall
    {
        public const string MaterialPath = "Assets/_Starframe/Art/Materials/Environment/SaturnRangeSky.mat";
        public const string ShaderPath = "Assets/_Starframe/Art/Shaders/Environment/SaturnRingSky.shader";
        public const float WorldRadiusMetres = 20000000f;
        public static readonly Vector4 CenterWorldMetres = new Vector4(-20300000f, -400000f, 800000f, 0f);

        [MenuItem("Starframe/Environment/Set immense range planet")]
        public static void Install() { Apply(); }

        public static string Apply()
        {
            if (EditorApplication.isPlaying)
                throw new InvalidOperationException("Stop Play Mode before changing the range sky material.");

            AssetDatabase.ImportAsset(ShaderPath, ImportAssetOptions.ForceSynchronousImport);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (!material)
                throw new InvalidOperationException("Shared range sky material is missing: " + MaterialPath);
            if (!material.shader || material.shader.name != "Starframe/Environment/Saturn Ring Sky")
                throw new InvalidOperationException("Shared range sky material does not use SaturnRingSky.shader.");
            if (!material.HasProperty("_PlanetWorldRadius") || !material.HasProperty("_PlanetCenterWS"))
                throw new InvalidOperationException("Updated world-space planet properties are unavailable.");

            material.SetFloat("_PlanetWorldRadius", WorldRadiusMetres);
            material.SetVector("_PlanetCenterWS", CenterWorldMetres);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return MaterialPath;
        }
    }
}
