# Fullscreen UI polish

2026-11-06. Targeted corrections to existing uGUI screens; no architecture replacement.

- Ordinary buttons mostly 20–24 reference pixels; primary actions 32, 320×82; smaller test-flight controls, training switches, titles and assembly headings. Existing Chinese fallback scale 0.7 retained.
- Buttons and card footers vertically centered. Pagination has sufficient width. Long card names fit within 15–20 reference pixels. Settings note no longer truncates.
- Slider track 4 pixels and handle 12×22, with a fixed-height handle parent: Unity drives handle anchors, so changing size alone did not survive refresh. Full control hit areas remain.
- Group labels always GROUP 1, GROUP 2, etc. New/unconfigured blueprints start with two mouse groups. Existing groups, assignments and underlying saved names are preserved; deleting an optional group does not cause it to reappear.
- Countermeasure default Tab. Preferences schema 2 migrates schema-1 R bindings to Tab once; other bindings survive. Explicit later remaps are supported.

Validation: 8 weapon-group and 9 settings EditMode checks pass, including new initialization/deletion and preference migration cases. Final compilation passes. Maximized native Game view 2560×1440 was advancing during review; screenshots exported at 1920×1080. Two visual iterations corrected the slider-anchor issue and remaining text clipping. Final settings, library and target-picker text audits reported no truncation. Astra inspected the final screenshots and approved the visual result.

Screenshots: `Assets/tmp/UI-Polish/` — Home.png, Range.png, Range-Chinese.png, Assembly-Chinese.png, Groups.png, Settings-Final.png, Blueprints-Final.png, Deployment-Final.png, TargetPicker-Final.png. Earlier captures retain first-pass findings.

Review used `IsolatedProfile/`; original working ship SHA256 before/after: `0FB91A8B78A3CA6F868B89968E360D23CD613F4E9967553FDB46AB86E773F714`. Preferences were not saved by the review. Play stopped; original Game-view maximization/background state restored. Trace documentation exported. No standalone build, exhaustive aspect-ratio, or broad combat test claim.
