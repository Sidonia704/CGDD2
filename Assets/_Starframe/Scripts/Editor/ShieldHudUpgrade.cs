using System.IO;
using Starframe.Construction;
using Starframe.Flight;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Starframe.EditorTools
{
    public static class ShieldHudUpgrade
    {
        const string Root="Assets/_Starframe/";
        [MenuItem("Starframe/Apply shield and HUD assets")]
        public static void Apply()
        {
            var db=AssetDatabase.LoadAssetAtPath<ModuleDatabase>(Root+"Settings/Modules/ModuleDatabase.asset");
            SetGray("AssemblyPlastic",.93f);SetGray("AssemblyHullCool",.84f);SetGray("AssemblyHullSage",.89f);
            SetGray("AssemblyHullWarm",.865f);SetGray("AssemblyEngine",.81f);SetGray("AssemblyGraphite",.78f);
            var core=AssetDatabase.LoadAssetAtPath<Material>(Root+"Art/Materials/Ships/AssemblyCoreGlow.mat");
            core.SetColor("_BaseColor",Color.white);core.SetColor("_EmissionColor",Color.white*3.2f);core.EnableKeyword("_EMISSION");EditorUtility.SetDirty(core);
            var power=EnergyMaterial("AssemblyPower",new Color(.16f,.3f,.52f),new Color(.45f,1.3f,2.4f),.18f);
            var shieldMaterial=EnergyMaterial("AssemblyShield",new Color(.02f,.08f,.28f),new Color(.025f,.42f,2.1f),.6f);
            var surface=AssetDatabase.LoadAssetAtPath<Material>(Root+"Art/Materials/Ships/EnergyShieldSurface.mat");
            if(!surface){surface=new Material(Shader.Find("Starframe/Ships/EnergyShield"));AssetDatabase.CreateAsset(surface,Root+"Art/Materials/Ships/EnergyShieldSurface.mat");}
            surface.shader=Shader.Find("Starframe/Ships/EnergyShield");surface.SetColor("_Color",new Color(.2f,.5f,.85f,.055f));surface.SetFloat("_SurfaceOpacity",.0015f);surface.SetFloat("_HitOpacity",.12f);EditorUtility.SetDirty(surface);
            var shield=AssetDatabase.LoadAssetAtPath<ModuleDefinition>(Root+"Settings/Modules/Shield.asset");
            if(!shield){shield=ScriptableObject.CreateInstance<ModuleDefinition>();AssetDatabase.CreateAsset(shield,Root+"Settings/Modules/Shield.asset");}
            shield.shape=BlockShape.Shield;shield.displayName="护盾发生器";shield.category=ModuleCategory.Shield;
            shield.massKg=600;shield.maxHp=100;shield.recoverySeconds=3;shield.occupiedCells=new[]{Vector3Int.zero};
            shield.shieldCapacity=300;shield.shieldRadius=8;shield.shieldRecoveryDelay=2;shield.shieldRecoveryPerSecond=50;shield.shieldPowerWatts=100;
            shield.mesh=db.Get(BlockShape.Cube).mesh;shield.drawOutline=false;shield.material=shieldMaterial;shield.shieldSurfaceMaterial=surface;
            shield.icon=Icon("shield.png","Shield.png");shield.selectedIcon=Icon("shields.png","ShieldSelected.png");
            if(!db.modules.Contains(shield))db.modules.Add(shield);EditorUtility.SetDirty(shield);EditorUtility.SetDirty(db);
            db.Get(BlockShape.Power).material=power;EditorUtility.SetDirty(db.Get(BlockShape.Power));
            foreach(var guid in AssetDatabase.FindAssets("t:VolumeProfile",new[]{Root}))
            {
                var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetDatabase.GUIDToAssetPath(guid));
                if(!profile.TryGet<Bloom>(out var bloom))bloom=profile.Add<Bloom>();
                bloom.active=true;bloom.intensity.Override(.65f);bloom.threshold.Override(1.1f);bloom.scatter.Override(.55f);
                EditorUtility.SetDirty(bloom);EditorUtility.SetDirty(profile);
            }
            AssetDatabase.SaveAssets();AssetDatabase.Refresh();
        }
        static void SetGray(string name,float gray)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(Root+"Art/Materials/Ships/"+name+".mat");
            if(!material)return;material.SetColor("_BaseColor",new Color(gray,gray,gray,1));EditorUtility.SetDirty(material);
        }
        static Material EnergyMaterial(string name,Color body,Color emission,float flow)
        {
            string path=Root+"Art/Materials/Ships/"+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find("Starframe/Ships/FlowingEnergy"));AssetDatabase.CreateAsset(material,path);}
            material.shader=Shader.Find("Starframe/Ships/FlowingEnergy");material.SetColor("_BaseColor",body);material.SetColor("_EmissionColor",emission);
            material.SetFloat("_FlowSpeed",.8f);material.SetFloat("_FlowStrength",flow);material.SetFloat("_Smoothness",.65f);EditorUtility.SetDirty(material);return material;
        }
        static Sprite Icon(string source,string destination)
        {
            string path=Root+"Art/UI/Construction/Icons/"+destination;
            File.Copy(Root+"assetsForCodex/"+source,path,true);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceUpdate);
            var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;
            importer.alphaIsTransparency=true;importer.mipmapEnabled=false;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
