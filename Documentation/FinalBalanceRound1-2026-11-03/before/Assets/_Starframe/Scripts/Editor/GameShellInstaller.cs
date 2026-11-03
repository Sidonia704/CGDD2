using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.App;
using Starframe.Construction;
using Starframe.Flight;

namespace Starframe.EditorTools
{
    public static class GameShellInstaller
    {
        public const string HangarScene="Assets/_Starframe/Scenes/Frontend/Hangar.unity";
        const string SettingsRoot="Assets/_Starframe/Settings/Game";
        [MenuItem("Starframe/Game/Install game shell")]
        static void InstallFromMenu()=>Debug.Log(Install());
        public static string Install()
        {
            Directory.CreateDirectory(SettingsRoot);Directory.CreateDirectory("Assets/_Starframe/Scenes/Frontend");Directory.CreateDirectory("Assets/_Starframe/Art/UI/Operations");
            AssetDatabase.Refresh();
            var catalog=AssetDatabase.LoadAssetAtPath<MissionCatalog>(SettingsRoot+"/Missions.asset");
            if(!catalog){catalog=ScriptableObject.CreateInstance<MissionCatalog>();AssetDatabase.CreateAsset(catalog,SettingsRoot+"/Missions.asset");}
            foreach(var spec in new[]{new[]{"ice","6000"},new[]{"frontier","10000"}}){
                string id=spec[0],path=SettingsRoot+"/"+id+".asset";var mission=AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
                if(!mission){mission=ScriptableObject.CreateInstance<MissionDefinition>();mission.id=id;mission.titleKey="mission."+id+".title";mission.descriptionKey="mission."+id+".description";mission.regionKey="mission."+id+".region";mission.baseBudget=float.Parse(spec[1],System.Globalization.CultureInfo.InvariantCulture);mission.useProvisionalPricing=true;mission.unpricedPartCost=10;AssetDatabase.CreateAsset(mission,path);}
                if(!catalog.missions.Contains(mission))catalog.missions.Add(mission);
                var imageSource="Documentation/UIRefresh-2026-10-18/Previews/"+(id=="ice"?"Flight":"Assembly")+".png";
                var imagePath="Assets/_Starframe/Art/UI/Operations/"+id+".png";
                if(!File.Exists(imagePath)&&File.Exists(imageSource)){File.Copy(imageSource,imagePath);AssetDatabase.ImportAsset(imagePath);}
                if(!mission.thumbnail)mission.thumbnail=AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath);
                EditorUtility.SetDirty(mission);
            }
            EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssets();
            if(!File.Exists(HangarScene))CreateHangar(catalog);
            var scenes=new[]{HangarScene,"Assets/_Starframe/Scenes/Construction/ShipEditor.unity","Assets/_Starframe/Scenes/Testing/FlightRange.unity"};
            EditorBuildSettings.scenes=scenes.Select(p=>new EditorBuildSettingsScene(p,true)).Concat(EditorBuildSettings.scenes.Where(s=>!scenes.Contains(s.path))).ToArray();
            FrameworkBuildChecks.IncludeRuntimeShaders();
            return "Hangar entry, 2 mission definitions, editor and range registered.";
        }
        static void CreateHangar(MissionCatalog catalog)
        {
            var previous=EditorSceneManager.GetSceneManagerSetup();
            try{
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Environment/SpaceNebula.mat");
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.35f,.42f,.54f);RenderSettings.ambientEquatorColor=new Color(.20f,.24f,.32f);RenderSettings.ambientGroundColor=new Color(.08f,.11f,.18f);RenderSettings.reflectionIntensity=.85f;
                var reflection=AssetDatabase.LoadAssetAtPath<Cubemap>("Assets/_Starframe/Art/Materials/Ships/PlasticSoftboxReflection.asset");
                if(reflection){RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=reflection;}
                var cameraObject=new GameObject("Hangar Camera",typeof(Camera),typeof(AudioListener));cameraObject.tag="MainCamera";var camera=cameraObject.GetComponent<Camera>();camera.orthographic=true;camera.orthographicSize=9;camera.nearClipPlane=.1f;camera.farClipPlane=1000;camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;
                cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=true;
                Light("Warm key",new Vector3(38,-28,0),new Color(1,.92f,.83f),1.35f);
                Light("Cool fill",new Vector3(20,145,0),new Color(.59f,.74f,1),.4f);
                var volume=new GameObject("Hangar Post Processing").AddComponent<Volume>();volume.isGlobal=true;volume.priority=1;volume.sharedProfile=AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_Starframe/Settings/Rendering/ConstructionPostFX.asset");
                var shell=new GameObject("Game Application").AddComponent<GameAppBootstrap>();shell.previewCamera=camera;shell.catalog=catalog;
                shell.assemblyPrefab=AssetDatabase.LoadAssetAtPath<ShipAssembly>("Assets/_Starframe/Prefabs/Ships/FlightShipAssembly.prefab");shell.tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>("Assets/_Starframe/Settings/Flight/FlightTuning.asset");
                shell.rangeBattleTuning=AssetDatabase.LoadAssetAtPath<Starframe.Combat.BattleTuning>("Assets/_Starframe/Settings/Flight/BattleTuning.asset");
                EditorSceneManager.SaveScene(scene,HangarScene);
            }finally{EditorSceneManager.RestoreSceneManagerSetup(previous);}
        }
        static void Light(string name,Vector3 angles,Color color,float intensity)
        {
            var light=new GameObject(name).AddComponent<UnityEngine.Light>();light.type=LightType.Directional;light.color=color;light.intensity=intensity;light.transform.eulerAngles=angles;light.shadows=LightShadows.Soft;
        }
    }
}
