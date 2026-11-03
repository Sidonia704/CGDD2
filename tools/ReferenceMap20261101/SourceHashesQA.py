"""Verify the five original reference model files against the audit manifest."""

import hashlib
import json
from pathlib import Path

root = Path("Documentation/ReferenceMap-2026-11-01")
manifest = json.loads(Path("Documentation/MapAudit-2026-11-01/content-manifest.json").read_text(encoding="utf-8"))
names = {"astro.fbx", "cggd.blend", "debri.fbx", "mainstru.fbx", "scatter.fbx"}
entries = [x for x in manifest if x["path"].startswith("Assets/_Starframe/assetsForCodex/scenemodel/") and Path(x["path"]).name in names]
records = []
for entry in entries:
    digest = hashlib.sha256()
    with Path(entry["path"]).open("rb") as source:
        while block := source.read(4 * 1024 * 1024):
            digest.update(block)
    actual = digest.hexdigest().upper()
    records.append({"path": entry["path"], "expectedSha256": entry["sha256"], "actualSha256": actual,
                    "unchanged": actual == entry["sha256"]})
report = {"sourceFileCount": len(records), "allOriginalsUnchanged": len(records) == 5 and all(x["unchanged"] for x in records), "files": records}
(root / "source-qa-original-hashes.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(report["allOriginalsUnchanged"], [Path(x["path"]).name for x in records])
