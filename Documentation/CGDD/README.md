# CGDD website setup / 2026-10-22

CGDD is a course-evidence toolkit, not a game backend or a WebGL build system. `Assets/CGDD/Editor/CgddBootstrap.cs` installs a static-site template. `TraceDocsExporter.cs` reads `@role`, `@calls` and `@tune` comments and packages the relevant source code and documents for that website.

## Completed

- Added reviewed documentation headers to 86 existing Starframe runtime files. `annotation-audit.json` records hashes of their executable bodies. No body or tuning initializer was changed.
- Exported 86 distinct script files and 52 tunables using the running Unity Editor. Separate partial-class files now retain separate export keys.
- Corrected extraction of grouped numeric declarations, leading-decimal literals, bool literals and implicit zero defaults. C# defaults remain distinct from serialized asset overrides.
- Added `ExportForAutomation()` so the same export can run without a modal dialog or closing the live Editor. Batch export also avoids the dialog.
- Added a site-only identity override, subsystem diagram choices and a script picker. Both languages now show current exported documents instead of a stale template translation. Source code is never translated.
- Added a documented challenge baseline. The practice range respawns destroyed ships and has no terminal mission victory. Mission reporting interfaces are not presented as completed wave-based levels.
- Prepared `site/` as its own Git repository with an initial automation-authored snapshot and a workflow for later GitHub Pages deployment. No remote, upload or online publication was performed.

## Daily workflow

After future gameplay changes, follow the root `AGENTS.md`: keep headers accurate, record only genuine game changes in the append-only DEVLOG, and export in Unity. Ask about the player's verb and win/loss only if challenge feel changes. Add a Stage Reflection only at the student's request.

Then inspect the website and commit inside `site/`. Connect a real GitHub remote only when one exists. The standalone site's README includes exact commands. The root-level Pages workflow still supports a future full-project repository, but the active standalone-site workflow is `site/.github/workflows/pages.yml`.

`tools/prepare-cgdd-trace.cjs` is the one-time reviewed annotation setup, not a substitute for maintaining headers after gameplay edits. `tools/check-cgdd-site.cjs` checks current source/export synchronization. Its optional `--verify-setup-baseline` flag also checks that the initial 2026-10-22 setup did not change executable bodies or existing DEVLOG history. Do not use that flag to reject later authorized game changes.

## Still intentional placeholders

The DEVLOG contains its original two examples, not reconstructed personal development history. The recording slot remains empty until `site/media/play.mp4` is supplied. There is no online URL until a remote is connected and the Pages workflow succeeds.
