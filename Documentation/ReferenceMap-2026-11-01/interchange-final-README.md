# Frozen reference-map interchange

`interchange-final.zip` preserves the exact derived voxel data used for the 3,500 m reference map. Its ZIP entries use project-relative paths. It contains the frozen catalog, both final audits, the 125-link assembly sidecar, all 2,345 cell-row JSON files referenced by the catalog, card-rock cluster metadata, and original-source hash metadata. It contains no Unity assets, source meshes, or abandoned geometry batches.

Verify the archive against `interchange-final-manifest.json` from the project root:

```powershell
python tools/ReferenceMap20261101/ArchiveFinalInterchange.py --verify
```

If Unity clears `Temp`, extract the ZIP **from the project root** to restore the same `Temp/ReferenceMap20261101/...` paths expected by the native importer:

```powershell
Expand-Archive -LiteralPath Documentation/ReferenceMap-2026-11-01/interchange-final.zip -DestinationPath . -Force
```

The manifest records the ZIP SHA-256, each entry's SHA-256 and byte count, and the catalog geometry SHA-256 for each row file. Creation validated every row's asset key, cell count, and geometry hash against the frozen catalog before archiving. The archive was then reopened and every entry hash was checked.
