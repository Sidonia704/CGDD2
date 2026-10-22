using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Localization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.TextCore.LowLevel;

namespace Starframe.Presentation.Editor
{
    /// <summary>Idempotent native asset setup. Does not open or rewrite scenes.</summary>
    public static class LocalizationInstaller
    {
        const string Root = "Assets/_Starframe/Localization";
        const string Fonts = Root + "/Fonts";
        const string Csv = Root + "/GameUI.csv";

        [MenuItem("Starframe/Localization/Install Chinese and English")]
        public static void Setup()
        {
            if (TMP_Settings.instance == null)
                throw new InvalidOperationException("Import TMP Essential Resources before installing localization.");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(Fonts);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
            if (!settings)
            {
                string guid = AssetDatabase.FindAssets("t:LocalizationSettings", new[] { "Assets" }).FirstOrDefault();
                if (guid != null) settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (!settings)
                {
                    settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                    AssetDatabase.CreateAsset(settings, Root + "/LocalizationSettings.asset");
                }
                LocalizationEditorSettings.ActiveLocalizationSettings = settings;
            }
            var en = EnsureLocale("en", "English");
            var zh = EnsureLocale("zh-Hans", "简体中文");
            LocalizationSettings.ProjectLocale = en;
            // A system selector recognizes Chinese variants; English is the deterministic fallback.
            var selectors=settings.GetStartupLocaleSelectors();
            if(!selectors.Any(selector=>selector is SystemLocaleSelector)) selectors.Add(new SystemLocaleSelector());
            if(!selectors.OfType<SpecificLocaleSelector>().Any(selector=>selector.LocaleId.Code=="en"))
                selectors.Add(new SpecificLocaleSelector { LocaleId = new LocaleIdentifier("en") });
            settings.GetStringDatabase().UseFallback=true;
            var locales = new List<Locale> { en, zh };
            var strings = LocalizationEditorSettings.GetStringTableCollection(L10n.StringTable) ??
                LocalizationEditorSettings.CreateStringTableCollection(L10n.StringTable, Root, locales);
            foreach (var locale in locales)
                if (!strings.GetTable(locale.Identifier)) strings.AddNewTable(locale.Identifier);
            var records = ReadCsv(File.ReadAllText(Csv, Encoding.UTF8));
            if (records.Count < 2) throw new InvalidDataException("The localization source contains no entries.");
            var header = records[0];
            foreach (var locale in locales)
            {
                int column = header.IndexOf(locale.Identifier.Code);
                if (column < 0) throw new InvalidDataException("Missing CSV locale column: " + locale.Identifier.Code);
                var table = (StringTable)strings.GetTable(locale.Identifier);
                foreach (var row in records.Skip(1))
                {
                    if (row.Count <= column || string.IsNullOrWhiteSpace(row[0])) continue;
                    string value = row[column].Replace("\\n", "\n");
                    if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Empty translation: " + row[0] + " / " + locale.Identifier.Code);
                    var entry = table.GetEntry(row[0]) ?? table.AddEntry(row[0], value);
                    entry.Value = value;
                    entry.IsSmart = false; // Positional .NET formats, including rich text and telemetry.
                }
                EditorUtility.SetDirty(table);
            }
            strings.SetPreloadTableFlag(true);
            EditorUtility.SetDirty(strings); EditorUtility.SetDirty(strings.SharedData);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(LocalizationInstaller), strings);

            string chinesePath = Fonts + "/MicrosoftYaHei.ttc";
            if (!File.Exists(chinesePath))
            {
                string systemFont = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msyh.ttc");
                if (!File.Exists(systemFont)) throw new FileNotFoundException("Microsoft YaHei is required for the Chinese locale.", systemFont);
                File.Copy(systemFont, chinesePath);
                AssetDatabase.ImportAsset(chinesePath, ImportAssetOptions.ForceSynchronousImport);
            }
            var latinFont = CreateFont("Assets/TextMesh Pro/Fonts/LiberationSans.ttf", Fonts + "/UI Latin.asset");
            var chineseFont = CreateFont(chinesePath, Fonts + "/UI Chinese.asset");
            Prewarm(latinFont,records,header.IndexOf("en"));
            Prewarm(chineseFont,records,header.IndexOf("zh-Hans"));
            var assets = LocalizationEditorSettings.GetAssetTableCollection(L10n.AssetTable) ??
                LocalizationEditorSettings.CreateAssetTableCollection(L10n.AssetTable, Root, locales);
            foreach (var locale in locales)
            {
                if (!assets.GetTable(locale.Identifier)) assets.AddNewTable(locale.Identifier);
                var table = (AssetTable)assets.GetTable(locale.Identifier);
                // Public editor API stores the GUID and marks the font Addressable in a locale group.
                assets.AddAssetToTable(table, L10n.FontKey, locale.Identifier.Code == "en" ? latinFont : chineseFont);
                EditorUtility.SetDirty(table);
            }
            assets.SetPreloadTableFlag(true);
            EditorUtility.SetDirty(assets); EditorUtility.SetDirty(assets.SharedData); EditorUtility.SetDirty(settings);
            LocalizationEditorSettings.EditorEvents.RaiseCollectionModified(typeof(LocalizationInstaller), assets);
            AssetDatabase.SaveAssets();
            Debug.Log(Validate());
        }

        static Locale EnsureLocale(string code, string name)
        {
            var locale = LocalizationEditorSettings.GetLocales().FirstOrDefault(value => value.Identifier.Code == code);
            if (locale) return locale;
            locale = Locale.CreateLocale(code); locale.LocaleName = name;
            AssetDatabase.CreateAsset(locale, Root + "/" + code + ".asset");
            LocalizationEditorSettings.AddLocale(locale);
            return locale;
        }

        static TMP_FontAsset CreateFont(string sourcePath, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing) return existing;
            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (!source) throw new FileNotFoundException("Could not load the locale font: " + sourcePath);
            var font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = Path.GetFileNameWithoutExtension(assetPath);
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.isMultiAtlasTexturesEnabled = true;
            font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            AssetDatabase.CreateAsset(font, assetPath);
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
                if (atlas) EditorUtility.SetDirty(atlas);
            }
            if (font.material && !AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
            font.material.mainTexture = font.atlasTexture;
            EditorUtility.SetDirty(font.material); EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            if (!AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Material>().Any())
                throw new InvalidDataException("The font material was not saved: " + assetPath);
            return font;
        }

        static void Prewarm(TMP_FontAsset font,List<List<string>> records,int column)
        {
            // Include authored UI before building; arbitrary blueprint names can still grow the dynamic atlas.
            string text=new string(records.Skip(1).Where(row=>row.Count>column).SelectMany(row=>row[column]).Where(c=>!char.IsControl(c)).Concat("0123456789.,:;/+-−×%()[]<>□ ·").Distinct().ToArray());
            if(!font.TryAddCharacters(text,out string missing)&&!string.IsNullOrEmpty(missing))
                Debug.LogWarning(font.name+" has unsupported authored characters: "+missing);
            foreach(var atlas in font.atlasTextures)
            {
                if(!atlas)continue;
                if(!AssetDatabase.Contains(atlas))AssetDatabase.AddObjectToAsset(atlas,font);
                EditorUtility.SetDirty(atlas);
            }
            if(font.material){font.material.mainTexture=font.atlasTexture;EditorUtility.SetDirty(font.material);}
            EditorUtility.SetDirty(font);
        }

        public static string Validate()
        {
            var collection = LocalizationEditorSettings.GetStringTableCollection(L10n.StringTable);
            if (!collection || collection.SharedData.Entries.Count == 0) return "INCONCLUSIVE: no GameUI entries.";
            var gaps = new List<string>(); int count = 0;
            foreach (var key in collection.SharedData.Entries)
                foreach (var table in collection.StringTables)
                {
                    count++;
                    if (string.IsNullOrWhiteSpace(table.GetEntry(key.Id)?.Value)) gaps.Add(table.LocaleIdentifier.Code + "/" + key.Key);
                }
            return gaps.Count == 0 ? "COMPLETE: " + count + " entries checked, no gaps." : "GAPS: " + string.Join(", ", gaps);
        }

        public static void BuildAddressables() { AddressableAssetSettings.BuildPlayerContent(); }

        // Handles quoted commas, quotes and line breaks, including UTF-8 translator exports.
        static List<List<string>> ReadCsv(string csv)
        {
            var rows = new List<List<string>>(); var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
            for (int i = 0; i < csv.Length; i++)
            {
                char c = csv[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted) { row.Add(field.ToString()); field.Clear(); }
                else if ((c == '\n' || c == '\r') && !quoted)
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(field.ToString()); field.Clear(); if (row.Any(value => value.Length > 0)) rows.Add(row); row = new List<string>();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
            if (rows.Count > 0 && rows[0].Count > 0) rows[0][0] = rows[0][0].TrimStart('\uFEFF');
            return rows;
        }
    }
}
