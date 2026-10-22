# STATUS

- Game: Starframe / 星构. Build a modular spacecraft, test it, diagnose weaknesses and revise the blueprint.
- Stage: construction and combat-practice loop implemented; deployment validation and result-reporting interfaces exist. Mission wave progression and automatic mission victory/defeat are not implemented in the runtime scripts reviewed on 2026-10-22.
- Player verbs: assemble, rotate, remove, save, pilot, aim, fire, test and revise.
- Practice outcome: a destroyed core kills the current ship instance, then the range respawns it. The range is an open-ended drill, not a mission with a terminal victory state.
- Mission outcomes: a future level script must report Victory / Defeat / Aborted through MissionResults. The existing smoke run exercises that API and is not proof of a finished mission.
- Evidence baseline: script annotations and C# field defaults were documented without changing gameplay behavior or tuning values. No historical Interaction or Stage Reflection was invented.
- Flowchart and tunables table are written to `docs/` and `site/data/` by `CGDD/Export Trace Docs`.
- `DEVLOG.md` logs only the game itself. Site language, diagram styling, and click-to-read-script do not go into an Interaction or Stage Reflection.
- The student site defaults to English. The nav can switch to Chinese. Chrome strings live in `site/lang.js`. Exported docs are English; Chinese is a display translation.
- Website Git repository: `site/` (branch `main`), connected to `https://github.com/Sidonia704/CGDD2.git`. `site/README.md` explains export, preview, commit and GitHub Pages publication.
- GitHub Pages is enabled through the website repository's deployment workflow: `https://sidonia704.github.io/CGDD2/`.
