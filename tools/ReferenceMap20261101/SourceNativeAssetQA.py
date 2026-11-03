"""Read-only static correspondence audit of catalog, Unity assets, and layout."""

import json
import re
from pathlib import Path


ROOT = Path("Documentation/ReferenceMap-2026-11-01")
native = Path("Assets/_Starframe/Maps/ReferenceMiningStation")
catalog = json.loads(Path("Temp/ReferenceMap20261101/geometry-full-scene-final.json").read_text(encoding="utf-8"))
assets = {x["assetKey"]: x for x in catalog["assets"]}
asset_paths = {p.stem: p for p in (native / "Assets").rglob("*.asset")}
mesh_paths = {p.stem: p for p in (native / "Meshes").rglob("*.asset")}
layout = native / "Layouts/ReferenceMiningStation3500.asset"
layout_text = layout.read_text(encoding="utf-8")
layout_guids = re.findall(r"^\s+asset: \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}", layout_text, re.MULTILINE)
guid_to_key = {}
asset_names_mismatch = []
cell_sizes_mismatch = []
for key, path in asset_paths.items():
    meta = path.with_suffix(path.suffix + ".meta").read_text(encoding="utf-8")
    guid = re.search(r"^guid: ([0-9a-f]{32})$", meta, re.MULTILINE)
    if guid is None:
        raise ValueError(f"Missing Unity GUID: {path}")
    if guid.group(1) in guid_to_key:
        raise ValueError(f"Duplicate Unity GUID: {path}")
    guid_to_key[guid.group(1)] = key
    with path.open(encoding="utf-8") as stream:
        head = "".join(next(stream) for _ in range(25))
    actual_name = re.search(r"^  m_Name: (.+)$", head, re.MULTILINE)
    actual_size = re.search(r"^  cellSize: ([0-9.]+)$", head, re.MULTILINE)
    if actual_name is None or actual_name.group(1) != key:
        asset_names_mismatch.append(key)
    if actual_size is None or key not in assets or float(actual_size.group(1)) != float(assets[key]["cellSize"]):
        cell_sizes_mismatch.append(key)
missing_guid_refs = sorted(set(layout_guids) - set(guid_to_key))
placed_keys = {guid_to_key[g] for g in layout_guids if g in guid_to_key}
catalog_placed_keys = {x["assetKey"] for x in catalog["instances"]}
report = {
    "catalogUniqueAssets": len(assets),
    "nativeDefinitionAssets": len(asset_paths),
    "nativePreviewMeshes": len(mesh_paths),
    "catalogInstances": len(catalog["instances"]),
    "layoutAssetReferences": len(layout_guids),
    "placedUniqueAssets": len(placed_keys),
    "unplacedPrototypeAssets": sorted(set(assets) - placed_keys),
    "catalogAssetsMissingNative": sorted(set(assets) - set(asset_paths)),
    "nativeAssetsMissingCatalog": sorted(set(asset_paths) - set(assets)),
    "catalogMeshesMissingNative": sorted(set(assets) - set(mesh_paths)),
    "nativeMeshesMissingCatalog": sorted(set(mesh_paths) - set(assets)),
    "layoutRefsMissingNativeGuid": missing_guid_refs,
    "catalogPlacedKeysMissingLayout": sorted(catalog_placed_keys - placed_keys),
    "layoutPlacedKeysMissingCatalog": sorted(placed_keys - catalog_placed_keys),
    "assetNamesMismatch": asset_names_mismatch,
    "assetCellSizesMismatch": cell_sizes_mismatch,
}
report["allChecksPassed"] = (report["catalogUniqueAssets"] == report["nativeDefinitionAssets"] == report["nativePreviewMeshes"] == 2345
                             and report["catalogInstances"] == report["layoutAssetReferences"] == 2854
                             and len(report["unplacedPrototypeAssets"]) == 7
                             and all(not value for key, value in report.items() if isinstance(value, list) and key != "unplacedPrototypeAssets"))
(ROOT / "source-qa-native-assets.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print({k: v for k, v in report.items() if not isinstance(v, list)})
print("unplaced prototypes", report["unplacedPrototypeAssets"])
