using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.TextCore.LowLevel;
using Starframe.Presentation;
using Starframe.Construction;
using Starframe.Flight;

public static class FigmaUIAssets
{
    const string Root="Assets/_Starframe/Resources/UI/Fonts";
    static void Folder(string path){if(AssetDatabase.IsValidFolder(path))return;Folder(Path.GetDirectoryName(path).Replace('\\','/'));AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\','/'),Path.GetFileName(path));}
    static void Backup(string path){if(string.IsNullOrEmpty(path)||!File.Exists(path))return;string to="Documentation/FigmaUI-2026-11-06/before/"+path;if(File.Exists(to))return;Directory.CreateDirectory(Path.GetDirectoryName(to));File.Copy(path,to);}
    static TMP_FontAsset Font(string path,string source,int size,int padding,bool dynamic)
    {
        var existing=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);if(existing)return existing;
        var sourceFont=AssetDatabase.LoadAssetAtPath<UnityEngine.Font>(source);if(!sourceFont)throw new Exception("Font missing: "+source);
        var font=TMP_FontAsset.CreateFontAsset(sourceFont,size,padding,GlyphRenderMode.SDF16,1024,1024,AtlasPopulationMode.Dynamic,true);
        font.name=Path.GetFileNameWithoutExtension(path);font.atlasTextures[0].name=font.name+" Atlas";
        if(!dynamic){string characters=new string(Enumerable.Range(32,95).Select(i=>(char)i).ToArray())+"°²×·–—…−→←↑↓✓";font.TryAddCharacters(characters,out var missing);font.atlasPopulationMode=AtlasPopulationMode.Static;}
        AssetDatabase.CreateAsset(font,path);
        AssetDatabase.AddObjectToAsset(font.material,font);
        foreach(var atlas in font.atlasTextures)if(atlas&&!AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);
        var serialized=new SerializedObject(font);var clear=serialized.FindProperty("m_ClearDynamicDataOnBuild");if(clear!=null){clear.boolValue=true;serialized.ApplyModifiedPropertiesWithoutUndo();}
        EditorUtility.SetDirty(font);return font;
    }
    public static string Run()
    {
        Folder(Root);
        var chinese=Font(Root+"/ChineseFallback.asset","Assets/_Starframe/Localization/Fonts/MicrosoftYaHei.ttc",48,5,true);
        var chineseFace=chinese.faceInfo;chineseFace.scale=.7f;chinese.faceInfo=chineseFace;EditorUtility.SetDirty(chinese);
        var latin=Font(Root+"/SulphurPoint-Regular.asset","Assets/_Starframe/assetsForCodex/fonts/Sulphur_Point/SulphurPoint-Regular.ttf",90,9,false);
        var bold=Font(Root+"/SulphurPoint-Bold.asset","Assets/_Starframe/assetsForCodex/fonts/Sulphur_Point/SulphurPoint-Bold.ttf",90,9,false);
        var title=Font(Root+"/DolceVita.asset","Assets/_Starframe/assetsForCodex/fonts/Dolce Vita.ttf",90,9,false);
        var symbol=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Starframe/Localization/Fonts/UI Latin.asset");
        foreach(var font in new[]{latin,bold,title}){font.fallbackFontAssetTable=new List<TMP_FontAsset>{chinese};if(symbol&&symbol!=font)font.fallbackFontAssetTable.Add(symbol);EditorUtility.SetDirty(font);}
        var theme=AssetDatabase.LoadAssetAtPath<UiTheme>("Assets/_Starframe/Resources/UI/WorkbenchTheme.asset");
        Backup(AssetDatabase.GetAssetPath(theme));
        theme.refinedGlass=true;theme.frostedFinish=false;theme.glassTint=theme.dialogTint=theme.buttonTint=new Color32(27,31,45,255);
        theme.text=Color.white;theme.muted=new Color(.78f,.80f,.84f,1);theme.accent=new Color32(185,255,3,255);
        theme.combatInstrument=theme.accent;theme.amber=new Color32(255,191,0,255);theme.danger=new Color32(255,86,86,255);
        theme.grainStrength=0;theme.sheenStrength=0;theme.blurRadius=0;theme.refractionPixels=0;EditorUtility.SetDirty(theme);
        Folder("Assets/_Starframe/Art/FigmaUI");
        string cubePath="Assets/_Starframe/Art/FigmaUI/CoreCube.asset";
        var cube=AssetDatabase.LoadAssetAtPath<Mesh>(cubePath);if(!cube){cube=BlockGeometry.CreateSolid(BlockShape.Core);cube.name="Core cube";AssetDatabase.CreateAsset(cube,cubePath);}
        var core=AssetDatabase.LoadAssetAtPath<ModuleDefinition>("Assets/_Starframe/Settings/Modules/Core.asset");
        Backup(AssetDatabase.GetAssetPath(core));
        var coreMaterial=Material("CoreGreen",new Color(.14f,.56f,.025f,1),new Color(.12f,2.4f,.04f,1));
        core.mesh=cube;core.material=coreMaterial;core.drawOutline=false;
        var smallPower=AssetDatabase.LoadAssetAtPath<ModuleDefinition>("Assets/_Starframe/Settings/Modules/Power.asset");
        core.generationWatts=smallPower.generationWatts*.5f;core.storageJoules=smallPower.storageJoules*.5f;EditorUtility.SetDirty(core);
        var armorMaterial=Material("ArmorGrey",new Color(.47f,.49f,.52f,1),Color.black);
        foreach(string id in new[]{"Armor1","Armor2","Armor3","Armor4"}){var definition=AssetDatabase.LoadAssetAtPath<ModuleDefinition>("Assets/_Starframe/Settings/Modules/"+id+".asset");if(!definition)continue;Backup(AssetDatabase.GetAssetPath(definition));definition.material=armorMaterial;EditorUtility.SetDirty(definition);}
        AssetDatabase.SaveAssets();UiTheme.Reload();
        return "Configured static Sulphur Point/Dolce Vita + dynamic Chinese fallback; applied Figma palette; green cube core and grey armor.";
    }
    public static string BindScenes()
    {
        if(EditorApplication.isPlaying)throw new Exception("Stop play before binding authored scenes.");
        string original=UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
        foreach(string path in new[]{"Assets/_Starframe/Scenes/Frontend/Hangar.unity","Assets/_Starframe/Scenes/Construction/ShipEditor.unity"})
        {
            Backup(path);var scene=UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            if(path.Contains("Hangar")){
                var app=UnityEngine.Object.FindFirstObjectByType<Starframe.App.GameAppBootstrap>();var grid=app.GetComponent<Starframe.App.FigmaHangarGrid>();if(!grid)grid=app.gameObject.AddComponent<Starframe.App.FigmaHangarGrid>();
                var so=new SerializedObject(grid);so.FindProperty("gridMaterial").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Material>("Assets/_Starframe/Art/Materials/Construction/ConstructionGrid.mat");so.ApplyModifiedPropertiesWithoutUndo();
            }else{
                var ui=UnityEngine.Object.FindFirstObjectByType<ShipEditorUI>();var so=new SerializedObject(ui);so.FindProperty("assemblyMissions").objectReferenceValue=AssetDatabase.LoadAssetAtPath<Starframe.App.MissionCatalog>("Assets/_Starframe/Settings/Game/Missions.asset");so.ApplyModifiedPropertiesWithoutUndo();
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        }
        if(!string.IsNullOrEmpty(original))UnityEditor.SceneManagement.EditorSceneManager.OpenScene(original);
        return "Bound native hangar grid and editor mission catalog; restored original scene.";
    }
    static Material Material(string name,Color color,Color emission)
    {
        string path="Assets/_Starframe/Art/FigmaUI/"+name+".mat";var material=AssetDatabase.LoadAssetAtPath<Material>(path);
        if(!material){material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};AssetDatabase.CreateAsset(material,path);}
        material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.4f);material.SetFloat("_Metallic",0);material.enableInstancing=true;
        if(emission.maxColorComponent>0){material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;material.EnableKeyword("_EMISSION");material.SetColor("_EmissionColor",emission);}else{material.DisableKeyword("_EMISSION");material.SetColor("_EmissionColor",Color.black);}
        EditorUtility.SetDirty(material);return material;
    }
}
