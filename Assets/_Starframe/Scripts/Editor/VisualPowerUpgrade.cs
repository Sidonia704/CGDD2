using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Starframe.Construction;
using Starframe.Flight;
using Starframe.Combat;
namespace Starframe.EditorTools
{
    public static class VisualPowerUpgrade
    {
        const string Root="Assets/_Starframe/",Sources=Root+"assetsForCodex/",Models=Root+"Art/Models/Construction/",Icons=Root+"Art/UI/Construction/Icons/";
        [MenuItem("Starframe/Apply plastic and power asset upgrade")]
        public static void Apply()
        {
            Folder(Models);Folder(Icons);Folder(Root+"Art/Models/Modules/");Folder(Root+"Art/Materials/Ships/");
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var plastic=Material(Root+"Art/Materials/Ships/AssemblyPlastic.mat",new Color(.82f,.86f,.86f),.43f);
            var coreMaterial=Material(Root+"Art/Materials/Ships/AssemblyCoreGlow.mat",Color.white,.3f);
            coreMaterial.EnableKeyword("_EMISSION");coreMaterial.SetColor("_EmissionColor",new Color(.35f,.8f,1)*3.5f);EditorUtility.SetDirty(coreMaterial);
            var sourceNames=new[]{"block1","block2","thruster1","thruster2","block3","block4","core","lazer","psu"};
            var iconNames=new[]{"","","thruster1","thruster2","","","","lazer1","psu1"};
            var logicalNames=new[]{"Cube","Wedge","Thruster1","Thruster2","Slab","Pyramid","Core","Laser","Power"};
            for(int i=0;i<logicalNames.Length;i++){
                var shape=(BlockShape)i;string logical=logicalNames[i];var definition=db.Get(shape);
                if(!definition){definition=ScriptableObject.CreateInstance<ModuleDefinition>();definition.shape=shape;AssetDatabase.CreateAsset(definition,Root+"Settings/Modules/"+logical+".asset");db.modules.Add(definition);}
                string modelPath=Root+"Art/Models/Modules/"+logical+".fbx";CopyAsset(Sources+sourceNames[i]+".fbx",modelPath);
                var importer=(ModelImporter)AssetImporter.GetAtPath(modelPath);importer.isReadable=true;importer.importNormals=ModelImporterNormals.Import;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;importer.SaveAndReimport();
                var source=AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);var rotation=shape==BlockShape.Wedge?Quaternion.Euler(0,180,0):BlockModules.IsThruster(shape)?Quaternion.Euler(0,0,-90):shape==BlockShape.Laser?Quaternion.Euler(0,90,0):Quaternion.identity;
                var baked=StoreMesh(Bake(source,rotation,shape),Models+logical+"Mesh.asset");definition.mesh=baked;
                definition.outline=StoreMesh(BlockGeometry.CreateOutline(shape,.012f),Models+logical+"PreviewEdges.asset");
                definition.drawOutline=false;definition.material=shape==BlockShape.Core?coreMaterial:plastic;
                if(shape==BlockShape.Laser){definition.occupiedCells=new[]{Vector3Int.zero,new Vector3Int(0,0,1),new Vector3Int(0,0,2)};var vertices=baked.vertices;var tip=vertices.Where(v=>v.z>baked.bounds.max.z-.025f).ToArray();var muzzle=tip.Aggregate(Vector3.zero,(s,v)=>s+v)/Mathf.Max(1,tip.Length);muzzle.z=baked.bounds.max.z+.025f;definition.muzzleLocalPosition=muzzle;definition.muzzleClearanceDiameter=.25f;definition.weapon.sustainedPowerWatts=50;definition.weapon.visualLength=1.8f;definition.weapon.tailLength=4.5f;EditorUtility.SetDirty(definition.weapon);}
                if(shape==BlockShape.Power){definition.displayName="电源";definition.category=ModuleCategory.Power;definition.massKg=600;definition.maxHp=100;definition.recoverySeconds=4;definition.occupiedCells=new[]{Vector3Int.zero};definition.storageJoules=500;definition.generationWatts=60;}
                if(!string.IsNullOrEmpty(iconNames[i])){string name=shape==BlockShape.Laser?"LaserCannon":logical;definition.icon=ImportIcon(Sources+iconNames[i]+".png",Icons+name+".png");definition.selectedIcon=ImportIcon(Sources+iconNames[i]+"s.png",Icons+name+"Selected.png");}
                EditorUtility.SetDirty(definition);
            }
            EditorUtility.SetDirty(db);BlockModules.Configure(db);
            var voxel=Shader.Find("Starframe/Combat/VoxelGlow");if(!voxel)throw new InvalidOperationException("Voxel glow shader did not compile.");
            var tuning=AssetDatabase.LoadAssetAtPath<FlightTuning>(Root+"Settings/Flight/FlightTuning.asset");
            tuning.maxSpeed=250;tuning.maxLinearAcceleration=100;tuning.angularAccelerationGain=10;tuning.maxAngularAcceleration=360;tuning.angularRateGain=4;tuning.maxAngularRate=150;tuning.rollRateGain=5;tuning.maxRollRate=180;tuning.angularAuthorityReferenceAcceleration=10;tuning.angularAuthorityExponent=1.1f;tuning.boostPowerWatts=100;tuning.boostMultiplier=1.5f;tuning.exhaustShader=voxel;tuning.exhaustLength=2.4f;tuning.exhaustMaxLength=6;tuning.exhaustIntensity=3;EditorUtility.SetDirty(tuning);
            var battle=AssetDatabase.LoadAssetAtPath<BattleTuning>(Root+"Settings/Flight/BattleTuning.asset");battle.unlimitedRangePower=false;battle.laserShader=voxel;battle.destructionShader=voxel;EditorUtility.SetDirty(battle);
            foreach(var guid in AssetDatabase.FindAssets("t:VolumeProfile",new[]{Root})){var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath(guid));if(profile.TryGet<Bloom>(out var bloom)){bloom.active=true;bloom.intensity.Override(.7f);bloom.threshold.Override(1.05f);bloom.scatter.Override(.55f);EditorUtility.SetDirty(profile);}}
            foreach(var guid in AssetDatabase.FindAssets("t:Material",new[]{Root+"Art/Materials"})){var material=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));if(material.shader&&material.shader.name=="Starframe/SpaceSky"){material.SetFloat("_Exposure",1);material.SetFloat("_NebulaStrength",1.2f);material.SetFloat("_StarStrength",3.2f);EditorUtility.SetDirty(material);}}
            Folder(Root+"Data/RangeTargets/");var demo=FlightRangeBootstrap.DefaultShip();File.WriteAllText(Root+"Data/RangeTargets/PoweredRangeDemo.json",JsonUtility.ToJson(demo,true));AssetDatabase.ImportAsset(Root+"Data/RangeTargets/PoweredRangeDemo.json");
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        [MenuItem("Starframe/Refresh supplied models only")]
        public static void RefreshModels()
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var sourceNames=new[]{"block1","block2","thruster1","thruster2","block3","block4","core","lazer","psu"};
            var logicalNames=new[]{"Cube","Wedge","Thruster1","Thruster2","Slab","Pyramid","Core","Laser","Power"};
            for(int i=0;i<logicalNames.Length;i++){
                var shape=(BlockShape)i;var definition=database.Get(shape);if(!definition)continue;
                string path=Root+"Art/Models/Modules/"+logicalNames[i]+".fbx";CopyAsset(Sources+sourceNames[i]+".fbx",path);
                var importer=(ModelImporter)AssetImporter.GetAtPath(path);importer.isReadable=true;importer.importNormals=ModelImporterNormals.Import;importer.materialImportMode=ModelImporterMaterialImportMode.None;importer.importAnimation=false;importer.SaveAndReimport();
                var rotation=shape==BlockShape.Wedge?Quaternion.Euler(0,180,0):BlockModules.IsThruster(shape)?Quaternion.Euler(0,0,-90):shape==BlockShape.Laser?Quaternion.Euler(0,90,0):Quaternion.identity;
                var mesh=StoreMesh(Bake(AssetDatabase.LoadAssetAtPath<GameObject>(path),rotation,shape),Models+logicalNames[i]+"Mesh.asset");definition.mesh=mesh;
                if(shape==BlockShape.Laser){var tip=mesh.vertices.Where(v=>v.z>mesh.bounds.max.z-.025f).ToArray();var muzzle=tip.Aggregate(Vector3.zero,(s,v)=>s+v)/Mathf.Max(1,tip.Length);muzzle.z=mesh.bounds.max.z+.025f;definition.muzzleLocalPosition=muzzle;}
                EditorUtility.SetDirty(definition);
            }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Starframe/Apply reference lighting and materials")]
        public static void ApplyReferenceLook()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop play mode before saving the reference look.");
            var original=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if(original.isDirty)throw new InvalidOperationException("Save the current scene before changing scene lighting.");
            string originalPath=original.path;
            ApplyModulePalette();
            var reflection=PlasticReflection();
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            if(pipeline){pipeline.supportsHDR=true;pipeline.shadowDistance=160;pipeline.shadowCascadeCount=2;pipeline.mainLightShadowmapResolution=2048;EditorUtility.SetDirty(pipeline);}
            foreach(var guid in AssetDatabase.FindAssets("t:VolumeProfile",new[]{Root})){
                var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if(!profile.TryGet<Tonemapping>(out var tone))tone=profile.Add<Tonemapping>();tone.mode.Override(TonemappingMode.ACES);
                if(profile.TryGet<Bloom>(out var bloom)){bloom.intensity.Override(.55f);bloom.threshold.Override(1.1f);}
                EditorUtility.SetDirty(profile);
            }
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            if(renderer){
                var ao=renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
                if(!ao){ao=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();ao.name="Module contact shading";AssetDatabase.AddObjectToAsset(ao,renderer);renderer.rendererFeatures.Add(ao);}
                var settings=new SerializedObject(ao);var root=settings.FindProperty("m_Settings");
                root.FindPropertyRelative("Intensity").floatValue=1.75f;root.FindPropertyRelative("Radius").floatValue=.32f;root.FindPropertyRelative("DirectLightingStrength").floatValue=.5f;root.FindPropertyRelative("Falloff").floatValue=120;
                settings.ApplyModifiedPropertiesWithoutUndo();ao.SetActive(true);ao.Create();EditorUtility.SetDirty(ao);
                var serializedRenderer=new SerializedObject(renderer);var map=serializedRenderer.FindProperty("m_RendererFeatureMap");map.arraySize=renderer.rendererFeatures.Count;
                for(int i=0;i<renderer.rendererFeatures.Count;i++){AssetDatabase.TryGetGUIDAndLocalFileIdentifier(renderer.rendererFeatures[i],out string _,out long id);map.GetArrayElementAtIndex(i).longValue=id;}
                serializedRenderer.ApplyModifiedPropertiesWithoutUndo();renderer.SetDirty();EditorUtility.SetDirty(renderer);
            }
            foreach(string path in new[]{Root+"Scenes/Construction/ShipEditor.unity",Root+"Scenes/Testing/FlightRange.unity"}){
                var scene=EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
                RenderSettings.ambientMode=AmbientMode.Trilight;RenderSettings.ambientSkyColor=new Color(.16f,.21f,.29f);RenderSettings.ambientEquatorColor=new Color(.08f,.11f,.16f);RenderSettings.ambientGroundColor=new Color(.04f,.06f,.10f);RenderSettings.ambientIntensity=1;
                RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=reflection;RenderSettings.reflectionIntensity=.8f;
                foreach(var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)){
                    bool fill=light.name.Contains("Fill");light.color=fill?new Color(.58f,.73f,1):new Color(1,.91f,.8f);light.intensity=fill?.28f:2.1f;
                    light.transform.rotation=fill?Quaternion.Euler(28,30,0):Quaternion.Euler(42,-135,0);light.shadows=fill?LightShadows.None:LightShadows.Soft;light.shadowStrength=.85f;light.shadowBias=.025f;light.shadowNormalBias=.15f;
                }
                if(path.Contains("FlightRange")){RenderSettings.skybox=AssetDatabase.LoadAssetAtPath<Material>(Root+"Art/Materials/Environment/SpaceNebula.mat");RenderSettings.ambientSkyColor=new Color(.24f,.29f,.36f);RenderSettings.ambientEquatorColor=new Color(.18f,.22f,.28f);RenderSettings.ambientGroundColor=new Color(.12f,.15f,.20f);}
                foreach(var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))if(camera.name=="Main Camera"){camera.clearFlags=CameraClearFlags.Skybox;camera.allowHDR=true;var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;data.renderShadows=true;}
                EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            }
            if(!string.IsNullOrEmpty(originalPath))EditorSceneManager.OpenScene(originalPath,OpenSceneMode.Single);
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Starframe/Apply subtle ivory module palette")]
        public static void ApplyModulePalette()
        {
            var database=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            var plastic=Plastic("AssemblyPlastic",Color.white*.92f,.60f);
            var cool=Plastic("AssemblyHullCool",Color.white*.90f,.59f);
            var sage=Plastic("AssemblyHullSage",Color.white*.91f,.58f);
            var warm=Plastic("AssemblyHullWarm",Color.white*.895f,.60f);
            var graphite=Plastic("AssemblyGraphite",Color.white*.865f,.58f);
            var engine=Plastic("AssemblyEngine",Color.white*.88f,.61f);
            var power=Plastic("AssemblyPower",Color.white*.89f,.57f);
            foreach(var module in database.modules){if(module.shape==BlockShape.Core)continue;module.material=module.shape==BlockShape.Laser?graphite:BlockModules.IsThruster(module.shape)?engine:module.shape==BlockShape.Power?power:module.shape==BlockShape.Wedge?cool:module.shape==BlockShape.Slab?sage:module.shape==BlockShape.Pyramid?warm:plastic;EditorUtility.SetDirty(module);}
            AssetDatabase.SaveAssets();
        }
        static Material Plastic(string name,Color colour,float smoothness)
        {
            colour.a=1;
            var material=Material(Root+"Art/Materials/Ships/"+name+".mat",colour,smoothness);
            material.shader=Shader.Find("Universal Render Pipeline/Complex Lit");material.SetFloat("_ClearCoatMask",.28f);material.SetFloat("_ClearCoatSmoothness",.72f);material.SetFloat("_SpecularHighlights",1);material.SetFloat("_EnvironmentReflections",1);material.EnableKeyword("_CLEARCOAT");EditorUtility.SetDirty(material);return material;
        }
        static Cubemap PlasticReflection()
        {
            const string path=Root+"Art/Materials/Ships/PlasticSoftboxReflection.asset";const int size=64;
            var cube=AssetDatabase.LoadAssetAtPath<Cubemap>(path);if(!cube){cube=new Cubemap(size,TextureFormat.RGBAHalf,true){name="Plastic softbox reflection"};AssetDatabase.CreateAsset(cube,path);}
            var key=new Vector3(-.4f,.8f,-.5f).normalized;var fill=new Vector3(.5f,.45f,.7f).normalized;
            for(int face=0;face<6;face++){
                var pixels=new Color[size*size];
                for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                    float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;Vector3 d;
                    switch((CubemapFace)face){case CubemapFace.PositiveX:d=new Vector3(1,-v,-u);break;case CubemapFace.NegativeX:d=new Vector3(-1,-v,u);break;case CubemapFace.PositiveY:d=new Vector3(u,1,v);break;case CubemapFace.NegativeY:d=new Vector3(u,-1,-v);break;case CubemapFace.PositiveZ:d=new Vector3(u,-v,1);break;default:d=new Vector3(-u,-v,-1);break;}
                    d.Normalize();float a=Mathf.Pow(Mathf.Max(0,Vector3.Dot(d,key)),28),b=Mathf.Pow(Mathf.Max(0,Vector3.Dot(d,fill)),40);
                    pixels[y*size+x]=new Color(.075f,.095f,.13f)+new Color(3.2f,2.8f,2.3f)*a+new Color(1.1f,1.5f,2.1f)*b;
                }cube.SetPixels(pixels,(CubemapFace)face);
            }cube.Apply(true,false);EditorUtility.SetDirty(cube);return cube;
        }
        static void Folder(string path){Directory.CreateDirectory(path);AssetDatabase.Refresh();}
        static void CopyAsset(string source,string destination){File.Copy(source,destination,true);AssetDatabase.ImportAsset(destination,ImportAssetOptions.ForceUpdate);}
        static Sprite ImportIcon(string source,string destination){CopyAsset(source,destination);var importer=(TextureImporter)AssetImporter.GetAtPath(destination);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.filterMode=FilterMode.Bilinear;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(destination);}
        static Material Material(string path,Color colour,float smoothness){var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}material.shader=Shader.Find("Universal Render Pipeline/Lit");material.SetColor("_BaseColor",colour);material.SetFloat("_Metallic",0);material.SetFloat("_Smoothness",smoothness);material.SetFloat("_Surface",0);material.SetFloat("_Cull",2);EditorUtility.SetDirty(material);return material;}
        static Mesh StoreMesh(Mesh mesh,string path){var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){EditorUtility.CopySerialized(mesh,existing);UnityEngine.Object.DestroyImmediate(mesh);EditorUtility.SetDirty(existing);return existing;}AssetDatabase.CreateAsset(mesh,path);return mesh;}
        static Mesh Bake(GameObject model,Quaternion rotation,BlockShape shape)
        {
            var combines=model.GetComponentsInChildren<MeshFilter>(true).Select(f=>new CombineInstance{mesh=f.sharedMesh,transform=Matrix4x4.Rotate(rotation)*model.transform.worldToLocalMatrix*f.transform.localToWorldMatrix}).ToArray();
            var mesh=new Mesh{name=shape+"BevelledVisual"};mesh.CombineMeshes(combines,true,true);mesh.RecalculateBounds();var source=mesh.bounds;
            var size=shape==BlockShape.Core?Vector3.one*.84f:shape==BlockShape.Thruster2?new Vector3(.98f,1.98f,.98f):shape==BlockShape.Slab?new Vector3(.98f,.49f,.98f):shape==BlockShape.Laser?Vector3.Min(source.size*.5f,new Vector3(.98f,.98f,2.98f)):Vector3.one*.98f;
            var centre=shape==BlockShape.Thruster2?Vector3.up*.5f:shape==BlockShape.Slab?Vector3.down*.245f:shape==BlockShape.Laser?new Vector3(0,-.5f+size.y*.5f,1):Vector3.zero;
            var scale=new Vector3(size.x/source.size.x,size.y/source.size.y,size.z/source.size.z);var vertices=mesh.vertices;var normals=mesh.normals;
            for(int i=0;i<vertices.Length;i++){vertices[i]=Vector3.Scale(vertices[i]-source.center,scale)+centre;if(i<normals.Length)normals[i]=new Vector3(normals[i].x/scale.x,normals[i].y/scale.y,normals[i].z/scale.z).normalized;}
            mesh.vertices=vertices;if(normals.Length==vertices.Length)mesh.normals=normals;else mesh.RecalculateNormals();mesh.RecalculateBounds();mesh.RecalculateTangents();return mesh;
        }
    }
}
