using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using TMPro;
using UnityEngine.TextCore.LowLevel;

public static class BakeChineseUI
{
    public static object Run()
    {
        if(Application.isPlaying)throw new Exception("Bake outside Play mode.");
        const string root="Assets/_Starframe/Resources/UI/Fonts/";
        var chars=new HashSet<char>();
        foreach(var path in Directory.GetFiles("Assets/_Starframe/Scripts","*.cs",SearchOption.AllDirectories).Concat(Directory.GetFiles("Assets/_Starframe/Localization","*.asset",SearchOption.TopDirectoryOnly)))
        {
            string text=File.ReadAllText(path);
            text=Regex.Replace(text,@"\\u([0-9a-fA-F]{4})",m=>((char)Convert.ToInt32(m.Groups[1].Value,16)).ToString());
            foreach(char c in text)if((c>=0x3000&&c<=0x9fff)||(c>=0xff00&&c<=0xffef))chars.Add(c);
        }
        var font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(root+"ChineseUI.asset");
        if(!font)
        {
            var source=AssetDatabase.LoadAssetAtPath<Font>("Assets/_Starframe/Localization/Fonts/MicrosoftYaHei.ttc");
            font=TMP_FontAsset.CreateFontAsset(source,48,5,GlyphRenderMode.SDF16,2048,2048,AtlasPopulationMode.Dynamic,true);
            font.name="ChineseUI";AssetDatabase.CreateAsset(font,root+"ChineseUI.asset");
            AssetDatabase.AddObjectToAsset(font.material,font);
        }
        font.atlasPopulationMode=AtlasPopulationMode.Dynamic;
        font.TryAddCharacters(new string(chars.OrderBy(c=>c).ToArray()),out string missing);
        foreach(var atlas in font.atlasTextures)if(atlas&&!AssetDatabase.Contains(atlas)){atlas.name="ChineseUI Atlas";AssetDatabase.AddObjectToAsset(atlas,font);}
        font.atlasPopulationMode=AtlasPopulationMode.Static;
        var face=font.faceInfo;face.scale=.7f;font.faceInfo=face;
        var fallback=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(root+"ChineseFallback.asset");
        font.fallbackFontAssetTable=new List<TMP_FontAsset>{fallback};
        foreach(string name in new[]{"SulphurPoint-Regular","SulphurPoint-Bold","DolceVita"})
        {
            var latin=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(root+name+".asset");
            var list=latin.fallbackFontAssetTable.Where(f=>f&&f!=font&&f!=fallback).ToList();list.Insert(0,font);latin.fallbackFontAssetTable=list;EditorUtility.SetDirty(latin);AssetDatabase.SaveAssetIfDirty(latin);
        }
        EditorUtility.SetDirty(font);AssetDatabase.SaveAssetIfDirty(font);
        return new{requested=chars.Count,baked=font.characterTable.Count,atlases=font.atlasTextures.Length,missing,scale=font.faceInfo.scale};
    }
}
