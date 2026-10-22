# Starframe / CGDD student site

This directory is a standalone, buildless website and an independent local Git repository. The Unity project itself is not a Git repository. No remote or online deployment is configured yet.

## What this website shows

1. **Framework**: generated script relationships, individual source files, C# field defaults and the challenge baseline.
2. **Process**: the current append-only `DEVLOG.md`. EXAMPLE sections remain format samples. Earlier development history has not been invented.
3. **Playable**: a recording placeholder. The game runs in Unity, not in this webpage. Add a recording at `media/play.mp4` when available.

The default diagram shows the core loop. Select a subsystem or all scripts to explore the export. The script selector opens files outside the visible diagram too. EN/中文 switches interface language without replacing current exported documents with an old sample translation. Source code keeps its original language.

## Export from Unity

1. Keep `@role`, `@calls` and `@tune` at the top of runtime scripts accurate.
2. Run **CGDD → Export Trace Docs** in the open Unity project.
3. Refresh the website. The exporter updates `data/bundle.js` and the matching flowchart, tunables, challenge and DEVLOG files.

The parent `docs/script-flow.mmd` and `docs/tunables.md` are generated too. Do not hand-edit them. Edit script headers instead. Keep the parent `DEVLOG.md` append-only. Site-only changes do not belong in Interaction or Stage Reflection sections.

`identity.json` controls the website name and English/Chinese description without changing the Unity product name. Tunables show C# field defaults. ScriptableObject assets and Inspector overrides can differ.

## Preview

- Double-click `index.html`, or use **CGDD → Open Student Site**. Mermaid and exported data are local and do not need a CDN. External fonts have system fallbacks.
- To play a recording or preview over HTTP, serve this folder. For example, from the Unity project root: `python -m http.server 8765 --directory site`, then open `http://localhost:8765/`.

## Save a local Git update

After exporting in Unity, run PowerShell inside `site/`:

```powershell
git status
git add .
git commit -m "Update Starframe course evidence"
```

Set your own repository identity before making personal commits if Git asks:

```powershell
git config user.name "YOUR NAME"
git config user.email "YOUR EMAIL"
```

The initial generated-site snapshot uses the automation identity `CGDD Toolkit <cgdd-toolkit@localhost>`, not an invented student identity.

## Connect GitHub later

Create an empty GitHub repository. Inside `site/`, replace the example URL with your actual repository:

```powershell
git remote add origin https://github.com/YOUR_ACCOUNT/YOUR_REPOSITORY.git
git push -u origin main
```

Choose **Settings → Pages → Source: GitHub Actions** in that repository. `.github/workflows/pages.yml` publishes this website on pushes to `main`. It stages only website assets, not the Git database, identity config or these instructions. GitHub shows the published URL after a successful run. For subsequent updates: export in Unity, commit here, then `git push`.

Do not initialize a repository in the parent solely to publish this website. If the whole Unity project is versioned later, manage this nested repository deliberately, for example as a submodule.
