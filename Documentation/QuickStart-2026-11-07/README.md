# Beginner quick-start handouts — 2026-11-07

Two separate one-sided A4 landscape handouts: English and Simplified Chinese. Print at actual size or fit to A4 landscape. The three columns follow the first session: build/save, practise/fight, then read the HUD and recover from common firing problems.

## Deliverables

- `output/pdf/Starframe_Quick_Start_EN.pdf` and `Starframe_Quick_Start_ZH.pdf` are the printable originals.
- Identical PDFs and 150 dpi PNG previews are mirrored under `site/guides/` and `docs/guides/`. Existing PDF URLs stay unchanged.
- This folder contains rendered PNGs, extracted text, `content.json`, `layout.json`, and `validation.json` for review.
- Run `tools/QuickStart/make_guides.py` with Python + ReportLab to rebuild the PDFs. The stable entry point delegates to `make_guides_v2.py`, which owns the current content and layout.

## Content checked against the game

Read-only source checks used `GameInput`, `WeaponGroupInputRouter`, `WeaponGroups`, `ShipEditorController`, `WorkbenchOrbit`, `ArcadeMissionDirector`, and combat/player logic. This is documentation only; gameplay code is unchanged by this handout task.

- Flight: WASD, Space/Ctrl, Q/E, persistent mouse steering, Shift boost, mouse group fire, F lock, Tab flares, number keys toggle groups, V cadence.
- Editor: left click place, X delete mode, Space catalogue, R rotate, Alt copy, M core mirror, P weapon groups, orbit/pan/zoom/frame.
- New/default group assignments are stated as defaults; saved blueprints may use different fire bindings.
- Mission goal is six waves plus remaining enemies, ending with the t8 flagship and escorts. Losing the player's core ends the sortie.
- Training target selection and ATTACK/MOVE toggles, weapon readiness, heat, shield/energy, conventional missile reacquisition and VLS area acquisition are explained without a balance-stat dump.

## Validation

Both PDFs were rendered with Poppler at 150 dpi and visually inspected after increasing body type to 9.6 pt and control descriptions to 9.2 pt. One page each, A4 landscape, no clipped text, missing Chinese glyphs or footer overlap. Chinese SimHei and the supplied Sulphur Point English fonts are embedded. Page count, page dimensions, key text, font embedding and byte-identical site/docs copies were checked with pypdf. No Unity session or gameplay test was run for the manual.

An independent GPT-6 Astra review accepted the final English and Chinese renders and control/content accuracy after clarifying post-overheat cooling and beam-versus-projectile aiming.

The handout records current defaults. Rebuild it when controls, mission flow or important readiness rules change.
