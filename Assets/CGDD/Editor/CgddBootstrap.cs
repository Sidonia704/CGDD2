// Menu: CGDD/Bootstrap Student Site, CGDD/Export Trace Docs, CGDD/Open Student Site
// Advanced: CGDD/Advanced/Force Overwrite Templates, CGDD/Advanced/Uninstall Course Toolkit
// Teacher: CGDD/Teacher/Export Toolkit Package (see CgddPackageExporter)
// Copies AGENTS / DEVLOG / STATUS / docs / site / .gitignore / Pages workflow from Assets/CGDD/Templates/ to the project root.
// Default does not overwrite existing files. Force Overwrite Templates overwrites.
// After copy (and on Export), ApplySiteIdentity replaces "Your Game Title" placeholders with Application.productName.
// Site ships with a local mermaid.min.js so diagrams work offline / on file://.

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class CgddBootstrap
{
    const string TemplateRoot = "Assets/CGDD/Templates";

    [MenuItem("CGDD/Bootstrap Student Site", false, 10)]
    public static void Bootstrap()
    {
        Run(overwrite: false);
    }

    [MenuItem("CGDD/Advanced/Force Overwrite Templates", false, 100)]
    public static void BootstrapForce()
    {
        if (!EditorUtility.DisplayDialog(
                "Force overwrite?",
                "This will overwrite existing AGENTS.md, DEVLOG.md, STATUS.md, docs/challenge-map.md, the entire site/, .gitignore, and .github/workflows/pages.yml at the project root with the templates.",
                "Overwrite",
                "Cancel"))
            return;
        Run(overwrite: true);
    }

    [MenuItem("CGDD/Open Student Site", false, 30)]
    public static void OpenStudentSite()
    {
        string index = SiteIndexPath();
        if (!File.Exists(index))
        {
            EditorUtility.DisplayDialog(
                "Site missing",
                "Could not find site/index.html at the project root.\nRun CGDD → Bootstrap Student Site first.",
                "OK");
            return;
        }

        Application.OpenURL(ToFileUrl(index));
    }

    [MenuItem("CGDD/Advanced/Uninstall Course Toolkit", false, 120)]
    public static void UninstallCourseToolkit()
    {
        if (!EditorUtility.DisplayDialog(
                "Uninstall CGDD toolkit?",
                "Step 1/2 — Remove Assets/CGDD (menus + templates).\n\n"
                + "This does not delete your game scripts or scenes.\n"
                + "Project-root course files (AGENTS.md, DEVLOG.md, STATUS.md, docs/, site/, Pages workflow) are asked about next.",
                "Remove Assets/CGDD",
                "Cancel"))
            return;

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        bool removedToolkit = false;
        if (AssetDatabase.IsValidFolder("Assets/CGDD"))
        {
            removedToolkit = AssetDatabase.DeleteAsset("Assets/CGDD");
            if (!removedToolkit)
                EditorUtility.DisplayDialog("Uninstall", "Could not delete Assets/CGDD. Close any locked files and try again.", "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("Uninstall", "Assets/CGDD was already missing.", "OK");
        }

        AssetDatabase.Refresh();

        bool alsoRoot = EditorUtility.DisplayDialog(
            "Also remove course files at project root?",
            "Step 2/2 — Optional.\n\n"
            + "Delete these if they exist:\n"
            + "• AGENTS.md, DEVLOG.md, STATUS.md\n"
            + "• docs/\n"
            + "• site/\n"
            + "• .github/workflows/pages.yml\n\n"
            + "Does NOT delete .gitignore (may be yours).\n"
            + "If you already wrote a real DEVLOG, copy it out before Yes.",
            "Yes, delete root course files",
            "No, keep root files");

        int deleted = 0;
        if (alsoRoot)
        {
            deleted += DeletePath(Path.Combine(projectRoot, "AGENTS.md"));
            deleted += DeletePath(Path.Combine(projectRoot, "DEVLOG.md"));
            deleted += DeletePath(Path.Combine(projectRoot, "STATUS.md"));
            deleted += DeletePath(Path.Combine(projectRoot, "docs"));
            deleted += DeletePath(Path.Combine(projectRoot, "site"));
            deleted += DeletePath(Path.Combine(projectRoot, ".github", "workflows", "pages.yml"));
            // Remove empty .github/workflows and .github if empty
            TryDeleteEmptyDir(Path.Combine(projectRoot, ".github", "workflows"));
            TryDeleteEmptyDir(Path.Combine(projectRoot, ".github"));
        }

        AssetDatabase.Refresh();
        string msg = removedToolkit ? "Removed Assets/CGDD. " : "Assets/CGDD not removed (missing or locked). ";
        msg += alsoRoot ? $"Deleted {deleted} root path(s)." : "Left project-root course files in place.";
        Debug.Log("CGDD/Advanced/Uninstall Course Toolkit: " + msg);
        EditorUtility.DisplayDialog("Uninstall complete", msg + "\n\nYour gameplay Assets (outside CGDD) were not touched.", "OK");
    }

    static int DeletePath(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return 1;
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                return 1;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("CGDD uninstall could not delete " + path + ": " + ex.Message);
        }
        return 0;
    }

    static void TryDeleteEmptyDir(string path)
    {
        try
        {
            if (Directory.Exists(path) && Directory.GetFileSystemEntries(path).Length == 0)
                Directory.Delete(path);
        }
        catch
        {
            // ignore
        }
    }


    public static void OfferOpenSite(string title, string body)
    {
        if (EditorUtility.DisplayDialog(title, body + "\n\nOpen the student site now?", "Open site", "Not now"))
            OpenStudentSite();
    }

    /// <summary>
    /// Replace template placeholders in site/index.html and site/lang.js with the Unity product name.
    /// Safe to call after Bootstrap or Export; does not wipe custom titles that are already set.
    /// </summary>
    public static void ApplySiteIdentity()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string product = Application.productName;
        if (string.IsNullOrWhiteSpace(product) || product == "ProductName")
            product = new DirectoryInfo(projectRoot).Name;

        string display = HumanizeName(product);
        string ledeEn = GuessLedeEn(projectRoot, display);
        string ledeZh = GuessLedeZh(projectRoot, display);
        // Optional site-only identity. Do not change the Unity product name just to name a website.
        string identityPath = Path.Combine(projectRoot, "site", "identity.json");
        if (File.Exists(identityPath))
        {
            var identity = JsonUtility.FromJson<SiteIdentity>(File.ReadAllText(identityPath));
            if (identity != null)
            {
                if (!string.IsNullOrWhiteSpace(identity.displayName)) display = identity.displayName;
                if (!string.IsNullOrWhiteSpace(identity.summaryEn)) ledeEn = identity.summaryEn;
                if (!string.IsNullOrWhiteSpace(identity.summaryZh)) ledeZh = identity.summaryZh;
            }
        }
        string company = Application.companyName;
        if (string.IsNullOrWhiteSpace(company) || company == "DefaultCompany")
            company = null;

        string kicker = company != null
            ? $"CGDD · Unity 6 · {display} · {company}"
            : $"CGDD · Unity 6 · {display}";

        string indexPath = Path.Combine(projectRoot, "site", "index.html");
        if (File.Exists(indexPath))
            WriteIndexIdentity(indexPath, display, kicker, ledeEn);

        string langPath = Path.Combine(projectRoot, "site", "lang.js");
        if (File.Exists(langPath))
            WriteLangIdentity(langPath, display, kicker, ledeEn, ledeZh);

        string posterPath = Path.Combine(projectRoot, "site", "media", "poster.svg");
        if (File.Exists(posterPath))
            WritePosterIdentity(posterPath, display);
    }

    [System.Serializable]
    sealed class SiteIdentity
    {
        public string displayName;
        public string summaryEn;
        public string summaryZh;
    }

    static void Run(bool overwrite)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string templateAbs = Path.Combine(Application.dataPath, "CGDD", "Templates");
        if (!Directory.Exists(templateAbs))
        {
            EditorUtility.DisplayDialog("Templates missing", "Could not find Assets/CGDD/Templates/. Import the CGDD Student Toolkit first.", "OK");
            return;
        }

        int copied = 0;
        int skipped = 0;

        copied += CopyFile(Path.Combine(templateAbs, "AGENTS.md"), Path.Combine(projectRoot, "AGENTS.md"), overwrite, ref skipped);
        copied += CopyFile(Path.Combine(templateAbs, "DEVLOG.md"), Path.Combine(projectRoot, "DEVLOG.md"), overwrite, ref skipped);
        copied += CopyFile(Path.Combine(templateAbs, "STATUS.md"), Path.Combine(projectRoot, "STATUS.md"), overwrite, ref skipped);

        string docsSrc = Path.Combine(templateAbs, "docs");
        string docsDst = Path.Combine(projectRoot, "docs");
        Directory.CreateDirectory(docsDst);
        if (Directory.Exists(docsSrc))
        {
            foreach (string file in Directory.GetFiles(docsSrc, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(docsSrc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dest = Path.Combine(docsDst, rel);
                copied += CopyFile(file, dest, overwrite, ref skipped);
            }
        }

        string siteSrc = Path.Combine(templateAbs, "site");
        string siteDst = Path.Combine(projectRoot, "site");
        if (Directory.Exists(siteSrc))
        {
            foreach (string file in Directory.GetFiles(siteSrc, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta")) continue;
                string rel = file.Substring(siteSrc.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string dest = Path.Combine(siteDst, rel);
                copied += CopyFile(file, dest, overwrite, ref skipped);
            }
        }

        // Git helpers (stored without a leading dot under Templates so Unity can import them)
        copied += CopyFile(
            Path.Combine(templateAbs, "dot-gitignore"),
            Path.Combine(projectRoot, ".gitignore"),
            overwrite,
            ref skipped);
        copied += CopyFile(
            Path.Combine(templateAbs, "github-workflow-pages.yml"),
            Path.Combine(projectRoot, ".github", "workflows", "pages.yml"),
            overwrite,
            ref skipped);

        ApplySiteIdentity();
        AssetDatabase.Refresh();
        string msg = overwrite
            ? $"Wrote {copied} file(s) (force overwrite). Skipped {skipped}."
            : $"Wrote {copied} new file(s). Skipped {skipped} existing.";
        msg += "\nSite title set from Unity product name.";
        msg += "\nGit: .gitignore and .github/workflows/pages.yml are at the project root when missing (Bootstrap does not overwrite existing ones).";
        msg += "\nIf you are new to GitHub: push this project, then Settings → Pages → Source: GitHub Actions.";
        msg += "\n\nNext: annotate gameplay scripts with @role / @calls / @tune, then CGDD → Export Trace Docs.";
        Debug.Log("CGDD/Bootstrap Student Site: " + msg.Replace("\n", " "));
        OfferOpenSite("Bootstrap complete", msg);
    }

    static void WritePosterIdentity(string path, string display)
    {
        string svg = File.ReadAllText(path);
        string esc = EscapeHtml(display);
        string next = Regex.Replace(
            svg,
            @"(<text\b[^>]*\bid\s*=\s*[""']poster-title[""'][^>]*>)([\s\S]*?)(</text>)",
            m => m.Groups[1].Value + esc + m.Groups[3].Value,
            RegexOptions.IgnoreCase);
        if (next == svg)
        {
            // Template placeholder or leftover Hold-the-Line stamp from older toolkit copies.
            next = svg.Replace("YOUR GAME TITLE", esc);
            if (next.IndexOf("HOLD THE LINE", System.StringComparison.Ordinal) >= 0)
                next = next.Replace("HOLD THE LINE", esc);
        }
        if (next != svg)
            File.WriteAllText(path, next, new UTF8Encoding(false));
    }

    static void WriteIndexIdentity(string path, string display, string kicker, string ledeEn)
    {
        string html = File.ReadAllText(path);
        string h1 = SplitTitleHtml(display);
        // Always replace known placeholders; also replace if still the template title.
        html = Regex.Replace(html, @"<title>[^<]*</title>", $"<title>{EscapeHtml(display)} · CGDD</title>");
        html = Regex.Replace(
            html,
            @"<p class=""kicker""[^>]*>[\s\S]*?</p>",
            $"<p class=\"kicker\" data-i18n=\"kicker\">{EscapeHtml(kicker)}</p>",
            RegexOptions.IgnoreCase);
        html = Regex.Replace(
            html,
            @"<h1>[\s\S]*?</h1>",
            $"<h1>{h1}</h1>",
            RegexOptions.IgnoreCase);
        html = Regex.Replace(
            html,
            @"<p class=""lede""[^>]*>[\s\S]*?</p>",
            $"<p class=\"lede\" data-i18n=\"lede\">{EscapeHtml(ledeEn)}</p>",
            RegexOptions.IgnoreCase);
        File.WriteAllText(path, html, new UTF8Encoding(false));
    }

    static void WriteLangIdentity(string path, string display, string kicker, string ledeEn, string ledeZh)
    {
        string js = File.ReadAllText(path);
        js = ReplaceJsStringProp(js, "kicker", kicker);
        js = ReplaceJsStringProp(js, "lede", ledeEn, onlyLang: "en");
        js = ReplaceJsStringProp(js, "lede", ledeZh, onlyLang: "zh");
        js = ReplaceJsStringProp(js, "title", display + " · CGDD");
        // flow-no-role keys are in the template; leave them.
        File.WriteAllText(path, js, new UTF8Encoding(false));
    }

    // Replace every `key: "..."` (and template placeholders) for a UI string prop.
    // If onlyLang is set, only replace inside that lang block (zh / en).
    static string ReplaceJsStringProp(string js, string key, string value, string onlyLang = null)
    {
        string esc = EscapeJs(value);
        if (onlyLang == null)
        {
            return Regex.Replace(
                js,
                $@"({key}:\s*)""(?:\\.|[^""\\])*""",
                m => m.Groups[1].Value + "\"" + esc + "\"");
        }

        // Narrow to ui.zh or ui.en block roughly
        var blockRx = new Regex(
            onlyLang == "zh"
                ? @"(zh:\s*\{)([\s\S]*?)(\n\s*\},?\s*\n\s*en:)"
                : @"(en:\s*\{)([\s\S]*?)(\n\s*\}\s*\n\s*\})");
        return blockRx.Replace(js, m =>
        {
            string body = Regex.Replace(
                m.Groups[2].Value,
                $@"({key}:\s*)""(?:\\.|[^""\\])*""",
                mm => mm.Groups[1].Value + "\"" + esc + "\"");
            return m.Groups[1].Value + body + m.Groups[3].Value;
        });
    }

    static string HumanizeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Game";
        // MingSailing -> Ming Sailing; keep existing spaces
        if (name.Contains(" ")) return name.Trim();
        string spaced = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");
        spaced = Regex.Replace(spaced, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
        return spaced.Replace('_', ' ').Trim();
    }

    static string SplitTitleHtml(string display)
    {
        string[] parts = display.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
            return EscapeHtml(parts[0]) + "<br>" + EscapeHtml(string.Join(" ", parts, 1, parts.Length - 1));
        return EscapeHtml(display);
    }

    static string GuessLedeEn(string projectRoot, string display)
    {
        string from = ReadFirstUsefulLine(Path.Combine(projectRoot, "README.md"))
            ?? ReadFirstUsefulLine(Path.Combine(projectRoot, "STATUS.md"));
        if (!string.IsNullOrEmpty(from)) return from;
        return $"{display}: play in Unity, tune from script headers, and keep the student site in sync with Export Trace Docs.";
    }

    static string GuessLedeZh(string projectRoot, string display)
    {
        string from = ReadFirstUsefulLine(Path.Combine(projectRoot, "README.md"))
            ?? ReadFirstUsefulLine(Path.Combine(projectRoot, "STATUS.md"));
        if (!string.IsNullOrEmpty(from)) return from;
        return $"{display}：在 Unity 里玩，用脚本头调参，Export Trace Docs 同步学生站。";
    }

    static string ReadFirstUsefulLine(string path)
    {
        if (!File.Exists(path)) return null;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length < 12) continue;
            if (line.StartsWith("#")) line = line.TrimStart('#').Trim();
            if (line.Length < 12) continue;
            if (line.StartsWith("Only ") || line.StartsWith("EXAMPLE")) continue;
            if (IsStatusTemplateNoise(line)) continue;
            // Drop a leading markdown bullet if we still took a STATUS/README list item.
            if (line.StartsWith("- ")) line = line.Substring(2).Trim();
            if (line.Length < 12) continue;
            if (IsStatusTemplateNoise(line)) continue;
            if (line.Length > 180) line = line.Substring(0, 177) + "…";
            return line;
        }
        return null;
    }

    static bool IsStatusTemplateNoise(string line)
    {
        if (string.IsNullOrEmpty(line)) return true;
        string t = line.TrimStart('-', ' ', '	');
        if (t.StartsWith("Stage:", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (line.IndexOf("toolkit is installed", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (t.StartsWith("Flowchart", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("DEVLOG", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("The student site", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("Ask your Agent", System.StringComparison.OrdinalIgnoreCase)) return true;
        if (line.IndexOf("Site language, diagram styling", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (line.IndexOf("Chrome strings live in", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    static string EscapeHtml(string s)
    {
        if (s == null) return "";
        return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }

    static string EscapeJs(string s)
    {
        if (s == null) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    }

    static string SiteIndexPath()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "site", "index.html");
    }

    static string ToFileUrl(string absolutePath)
    {
        string full = Path.GetFullPath(absolutePath);
        return new System.Uri(full).AbsoluteUri;
    }

    static int CopyFile(string src, string dst, bool overwrite, ref int skipped)
    {
        if (!File.Exists(src)) return 0;
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        if (File.Exists(dst) && !overwrite)
        {
            skipped++;
            return 0;
        }

        File.Copy(src, dst, overwrite: true);
        return 1;
    }
}
