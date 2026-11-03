"""Preserve only the frozen reference-map interchange outside Unity's Temp directory."""

import argparse
import hashlib
import json
import pathlib
import zipfile


PROJECT = pathlib.Path(__file__).resolve().parents[2]
DOCS = pathlib.Path("Documentation/ReferenceMap-2026-11-01")
ARCHIVE = PROJECT / DOCS / "interchange-final.zip"
MANIFEST = PROJECT / DOCS / "interchange-final-manifest.json"
CATALOG = pathlib.Path("Temp/ReferenceMap20261101/geometry-full-scene-final.json")
AUDIT = pathlib.Path("Temp/ReferenceMap20261101/geometry-full-scene-audit.json")
LINKS = pathlib.Path("Temp/ReferenceMap20261101/scene-assembly-links.json")
LINK_AUDIT = pathlib.Path("Temp/ReferenceMap20261101/scene-assembly-links-audit.json")
EXTRAS = [
    pathlib.Path("Temp/ReferenceMap20261101/card-rock-clusters.json"),
    pathlib.Path("Documentation/ReferenceMap-2026-11-01/source-qa-original-hashes.json"),
]
CATALOG_SHA = "f5434e45ad1a4afe940771a79081b25f5e54547b9c00f284364c8c7a936d3862"
LINKS_SHA = "e642eeb2133169e076492e9873f704d51da4ff3c8501078688617abf8bcf9461"


def sha256(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def frozen_entries():
    if sha256(PROJECT / CATALOG) != CATALOG_SHA or sha256(PROJECT / LINKS) != LINKS_SHA:
        raise RuntimeError("Frozen catalog or link sidecar SHA-256 changed")
    catalog = json.loads((PROJECT / CATALOG).read_text(encoding="utf-8"))
    audit = json.loads((PROJECT / AUDIT).read_text(encoding="utf-8"))
    links = json.loads((PROJECT / LINKS).read_text(encoding="utf-8"))
    link_audit = json.loads((PROJECT / LINK_AUDIT).read_text(encoding="utf-8"))
    if (len(catalog["assets"]), len(catalog["instances"])) != (2345, 2854):
        raise RuntimeError("Frozen catalog counts changed")
    if not audit["allChecksPassed"] or audit["assetCount"] != 2345 or audit["uniqueCells"] != 2559830:
        raise RuntimeError("Frozen scene audit did not pass")
    if not link_audit["allChecksPassed"] or link_audit["pairCount"] != 125 or len(links["links"]) != 125:
        raise RuntimeError("Frozen link audit did not pass")
    if links["catalogSha256"] != CATALOG_SHA or link_audit["sidecarSha256"] != LINKS_SHA:
        raise RuntimeError("Assembly links do not match frozen catalog")

    rows_by_path = {}
    cell_count = 0
    for asset in catalog["assets"]:
        relative = pathlib.PurePosixPath(asset["cellRowsPath"])
        if (not relative.parts or relative.parts[:2] != ("Temp", "ReferenceMap20261101")
                or ".." in relative.parts or relative.suffix != ".json"):
            raise RuntimeError("Unexpected cell row path: " + str(relative))
        row_path = pathlib.Path(relative.as_posix())
        if row_path in rows_by_path:
            raise RuntimeError("Multiple final assets reference one row file: " + str(row_path))
        payload = json.loads((PROJECT / row_path).read_text(encoding="utf-8"))
        rows = payload["cells"]
        if payload["assetKey"] != asset["assetKey"] or len(rows) != asset["cells"]:
            raise RuntimeError("Row identity or cell count mismatch: " + str(row_path))
        geometry_hash = hashlib.sha256(json.dumps(rows, separators=(",", ":")).encode("utf-8")).hexdigest()
        if geometry_hash != asset["geometryHashSha256"]:
            raise RuntimeError("Frozen catalog geometry SHA-256 mismatch: " + str(row_path))
        rows_by_path[row_path] = geometry_hash
        cell_count += len(rows)
    if cell_count != 2559830:
        raise RuntimeError("Frozen unique cell total changed")

    ordered = [CATALOG, AUDIT, LINKS, LINK_AUDIT, *EXTRAS, *sorted(rows_by_path)]
    if len(ordered) != len(set(ordered)) or len(rows_by_path) != 2345:
        raise RuntimeError("Archive entry list contains duplicate or missing rows")
    return ordered, rows_by_path, cell_count


def create():
    entries, row_hashes, cell_count = frozen_entries()
    records = []
    # Exclusive creation leaves any earlier archive untouched.
    with zipfile.ZipFile(ARCHIVE, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=3,
                         allowZip64=True) as output:
        for relative in entries:
            path = PROJECT / relative
            record = {"path": relative.as_posix(), "bytes": path.stat().st_size, "sha256": sha256(path)}
            if relative in row_hashes:
                record["geometryHashSha256"] = row_hashes[relative]
            output.write(path, arcname=relative.as_posix())
            records.append(record)
    manifest = {
        "archive": (DOCS / ARCHIVE.name).as_posix(),
        "archiveSha256": sha256(ARCHIVE),
        "archiveBytes": ARCHIVE.stat().st_size,
        "entryCount": len(records),
        "uncompressedBytes": sum(item["bytes"] for item in records),
        "rowFileCount": len(row_hashes),
        "uniqueCells": cell_count,
        "catalogSha256": CATALOG_SHA,
        "sidecarSha256": LINKS_SHA,
        "entries": records,
    }
    MANIFEST.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    verify()


def verify():
    manifest = json.loads(MANIFEST.read_text(encoding="utf-8"))
    if sha256(ARCHIVE) != manifest["archiveSha256"] or ARCHIVE.stat().st_size != manifest["archiveBytes"]:
        raise RuntimeError("Archive SHA-256 or size mismatch")
    with zipfile.ZipFile(ARCHIVE, "r") as source:
        names = source.namelist()
        if names != [item["path"] for item in manifest["entries"]] or len(names) != manifest["entryCount"]:
            raise RuntimeError("Archive entry names or order mismatch")
        for record in manifest["entries"]:
            digest = hashlib.sha256()
            size = 0
            with source.open(record["path"]) as entry:
                for block in iter(lambda: entry.read(1024 * 1024), b""):
                    digest.update(block)
                    size += len(block)
            if size != record["bytes"] or digest.hexdigest() != record["sha256"]:
                raise RuntimeError("Archived entry SHA-256 mismatch: " + record["path"])
    print(json.dumps({key: manifest[key] for key in ("archive", "archiveSha256", "archiveBytes",
                      "entryCount", "uncompressedBytes", "rowFileCount", "uniqueCells")}, indent=2))


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--verify", action="store_true", help="check the existing archive against its saved manifest")
    args = parser.parse_args()
    verify() if args.verify else create()
