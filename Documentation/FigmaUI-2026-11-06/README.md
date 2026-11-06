# Frame 23 UI implementation — 2026-11-06

Implemented from the user's full-resolution `C:/Users/34808/Desktop/Frame 23.png` and the cached Figma design contexts in `reference/`. These are real Unity views with live game data and rendered ship/module previews.

## Delivered

- Home, 4 × 3 blueprint library and target picker, training setup, deployment, assembly panels, independent 3D module catalogue, integrated weapon-group editing, settings and combat HUD.
- Sulphur Point regular/bold and Dolce Vita fonts; Microsoft YaHei dynamic fallback. Chinese glyph scale is **0.7**, following the final request to reduce Chinese by about 30%. English/numerals retain their sizing.
- Blue background, opaque dark panels, lime indicators and orange primary actions. Settings provide background swatches and RGB adjustment, applied across game backgrounds. Menu/editor grading preserves the reference blue.
- Real part count, mass, health, cost, mobility and rated firepower. Mobility combines mean directional acceleration and mean angular speed; firepower uses rated weapon damage/cadence including existing modifiers and reload/charge. These are display metrics, not changes to combat strength.
- Training attack and movement are independently selectable both before entry and in the live options panel. Weapon-group editing supports six groups and left/right mouse bindings. Existing stored legacy groups are preserved.
- Hell is appended to the difficulty enum with a 0.5 budget multiplier; existing enum values are stable. The reviewed first mission has a 30,000 standard / 15,000 Hell budget.
- HUD has individual weapon readiness, Gatling heat, missile reload, target cost, revised lock frames, transparent ship monitors and radar. Runtime geometry checks are cached by structural/tuning revision rather than repeated during every graphical refresh.
- Core uses a green emissive cube and armour uses neutral grey presentation.

## Verification

40 focused EditMode cases passed: settings 8, deployment 25, weapon groups 7. JSON reports are retained alongside this file.

Native live checks verified six-group limit and mouse binding changes, independent catalogue state, all four training toggle combinations, the Hell budget and Chinese font scale. Actual mounted missiles entered reload; four Gatlings reached overheat and recovered at zero heat. Multiple English/Chinese menu, assembly and combat screenshots were inspected. The final six-group label font formula was checked live and then saved to source; final compilation succeeded.

The review used an isolated ship workspace in `IsolatedProfile/`. The real current ship remains unchanged, SHA256 `35E972847D7AE8E25F7BBF661CCCD92406AA78EC48384D777FEEA3CCEBDEDC35`. Training mode was restored to FlyOnly before stopping Play. Final state and trace export are recorded in `final-state.json`.

The retained console includes unsupported CLI-command probes and Unity AI service messages; it is not represented as an empty console. No standalone build/performance benchmark or non-16:9 layout verification was performed. Ships and statistics intentionally use actual game data rather than the reference's repeated placeholder ship. Pixel-identical rendering is not claimed.

## Screenshot review

Captures are under `Assets/tmp/FigmaUI/`, including earlier iterations. Final examples:

- `Final-Home-English.png`, `Final-Home-Chinese.png`
- `Final-Assembly-Chinese.png`
- `Blueprints-Chinese-v2.png`, `TargetPicker-Chinese-v2.png`
- `RangeSetup-Chinese-v2.png`, `Deployment-Chinese-v2.png`
- `Settings-Chinese-v2.png`, `Catalogue-English-v3.png`, `Groups-English-v2.png`
- `Final-Combat-Mixed.png`, `Final-Combat-Heat.png`, `Final-LiveRangeOptions.png`

`tools/FigmaUIAssets.cs`, `tools/FigmaUIReview.cs` and `tools/FigmaUIFinish.cs` are explicit editor review helpers outside Assets; they do not run automatically in the game.
