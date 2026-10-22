// @role Resolve UI language, localized strings and language-owned fonts.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using UnityEngine.Localization.Settings;

namespace Starframe.Presentation
{
    /// <summary>The package SelectedLocale is the single source of language state.</summary>
    public static class L10n
    {
        public const string StringTable = "GameUI";
        public const string AssetTable = "GameAssets";
        public const string FontKey = "UI.Font";
        public static event Action Changed;

        sealed class FontLease
        {
            public LocalizedTmpFont reference;public TMP_FontAsset font;
            public void Loaded(TMP_FontAsset value)=>font=value;
        }
        static readonly Dictionary<string,FontLease> fonts=new Dictionary<string,FontLease>();
        static L10n() { LocalizationSettings.SelectedLocaleChanged += LocaleChanged;Application.quitting+=ReleaseFonts; }
        static void LocaleChanged(Locale locale) { Changed?.Invoke(); }

        public static bool IsEnglish => LocalizationSettings.SelectedLocale &&
            LocalizationSettings.SelectedLocale.Identifier.Code.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        public static TMP_FontAsset CurrentFont => FontForLocale(LocalizationSettings.SelectedLocale);
        public static TMP_FontAsset Font => CurrentFont;
        static FontLease Lease(Locale locale)
        {
            if(!locale)return null;string code=locale.Identifier.Code;
            if(fonts.TryGetValue(code,out var existing))return existing;
            var value=new FontLease{reference=new LocalizedTmpFont{TableReference=AssetTable,TableEntryReference=FontKey,LocaleOverride=locale}};
            fonts.Add(code,value);value.reference.AssetChanged+=value.Loaded;return value;
        }
        public static TMP_FontAsset FontForLocale(Locale locale)
        {
            var lease=Lease(locale?locale:LocalizationSettings.SelectedLocale);if(lease==null)return null;
            return lease.font?lease.font:lease.reference.LoadAsset();
        }
        static void ReleaseFonts()
        {
            foreach(var value in fonts.Values)value.reference.AssetChanged-=value.Loaded;fonts.Clear();
        }

        public static IEnumerator EnsureReady()
        {
            yield return LocalizationSettings.InitializationOperation;
            yield return LocalizationSettings.StringDatabase.GetTableAsync(StringTable);
            yield return LocalizationSettings.AssetDatabase.GetTableAsync(AssetTable);
            // Keep the two asset-table fonts alive while screens and locales change. Per-label loads
            // could release the previous font during a locale-change callback in a bundled Player.
            foreach(string code in new[]{"en","zh-Hans"})
            {
                var lease=Lease(LocalizationSettings.AvailableLocales.GetLocale(code));
                if(lease!=null)yield return lease.reference.CurrentLoadingOperationHandle;
            }
        }

        public static void SelectLocale(string code)
        {
            var locale = LocalizationSettings.AvailableLocales.GetLocale(code);
            if (locale) LocalizationSettings.SelectedLocale = locale;
        }

        public static string T(string key, params object[] args) =>
            LocalizationSettings.StringDatabase.GetLocalizedString(StringTable, key, args);

        public static object Value(string key) => new LocalizedValue(key);
        sealed class LocalizedValue : IFormattable
        {
            readonly string key;
            public LocalizedValue(string value) { key=value; }
            public override string ToString() => T(key);
            public string ToString(string format, IFormatProvider provider) => T(key);
        }

        public static bool IsKey(string value) => !string.IsNullOrEmpty(value) &&
            (value.StartsWith("ui.", StringComparison.Ordinal) || value.StartsWith("module.", StringComparison.Ordinal) ||
             value.StartsWith("category.", StringComparison.Ordinal) || value.StartsWith("hud.", StringComparison.Ordinal) ||
             value.StartsWith("editor.", StringComparison.Ordinal) || value.StartsWith("error.", StringComparison.Ordinal) ||
             value.StartsWith("menu.", StringComparison.Ordinal) || value.StartsWith("settings.", StringComparison.Ordinal) ||
             value.StartsWith("level.", StringComparison.Ordinal) || value.StartsWith("app.", StringComparison.Ordinal) || value.StartsWith("deploy.", StringComparison.Ordinal) || value.StartsWith("mission.", StringComparison.Ordinal));

        public static void Bind(TMP_Text text, string key, params object[] args)
        {
            if (!text) return;
            ApplyFont(text);
            var binding = text.GetComponent<LocalizedUiText>();
            if (!binding) binding = text.gameObject.AddComponent<LocalizedUiText>();
            binding.Configure(text, key, args);
        }

        public static void ApplyFont(TMP_Text text)
        {
            if (!text) return;
            var binding = text.GetComponent<LocalizedUiFont>();
            if (!binding) binding = text.gameObject.AddComponent<LocalizedUiFont>();
            binding.ApplyForContent();
        }

        public static void ApplyUserContentFont(TMP_Text text)
        {
            if(!text)return;
            var binding=text.GetComponent<LocalizedUiFont>();
            if(!binding)binding=text.gameObject.AddComponent<LocalizedUiFont>();
            binding.EnableUserContent();
        }

        public static void Unbind(TMP_Text text)
        {
            if (!text) return;
            var binding = text.GetComponent<LocalizedUiText>();
            if (binding) binding.Clear();
        }

        // Domain validation messages originate below the presentation layer. Keep their
        // public exception text intact, and translate them only where shown to players.
        public static string LegacyKey(string message)
        {
            switch (message)
            {
                case "必须对齐标准格子。": case "Part must fit one standard grid cell.": return "error.grid";
                case "每艘船只能有一个核心。": case "每个船体只能放置一个核心。": return "error.oneCore";
                case "占用格或推进器净空已被占用。": case "实体或推进器喷口净空被占用。": return "error.clearance";
                case "必须与现有船体通过面连接。": case "蓝图必须通过面连接组成一个整体。": return "error.connected";
                case "实体模型发生重叠。": case "Two identical parts share the same pose.": return "error.overlap";
                case "武器炮口前方被现有部件遮挡。": return "error.muzzleBlocked";
                case "该部件会遮挡已有武器的开火路径。": return "error.blocksWeapon";
                case "已有同名蓝图。": return "error.duplicateBlueprint";
                case "Blueprint was not found.": return "error.fileMissing";
                case "Blueprint exceeds the 16 MiB file size limit.": return "error.fileTooLarge";
                case "Invalid blueprint header or version.": case "Part IDs must be unique and nonempty.":
                case "Unsupported shape.": case "Invalid face rotation.": case "Part pose is not finite.":
                case "Part quaternion must be normalized.": return "error.invalidBlueprint";
                default: return null;
            }
        }

        public static string ErrorKey(Exception error)
        {
            string key = LegacyKey(error.Message);
            if (key != null) return key;
            if (error is FileNotFoundException) return "error.fileMissing";
            if (error is UnauthorizedAccessException) return "error.access";
            if (error is InvalidDataException) return "error.invalidBlueprint";
            if (error is IOException) return "error.storage";
            return "error.operation";
        }
        public static string Error(Exception error) => T(ErrorKey(error));
    }
}
