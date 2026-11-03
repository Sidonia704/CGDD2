"""Audit all-eight shape usage and multi-cell offset balance in sample voxels."""

import json
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np


root = Path("Temp/ReferenceMap20261101")
output = {}
shape_ids = {0: "Cube", 1: "Wedge", 4: "Slab", 5: "Pyramid",
             10: "Block5", 11: "Block6", 12: "Block7", 13: "Block8"}
for label, directory in (("hex_deck", root / "GeometryHexDeck"),
                         ("truss", root / "GeometryTruss")):
    manifest = json.loads((directory / "geometry-manifest.json").read_text(encoding="utf-8"))
    counts = Counter()
    offsets = defaultdict(Counter)
    turns = defaultdict(set)
    for asset in manifest["assets"]:
        data = np.load(directory / asset["npz"])
        shape = data["shape"]
        for shape_id, name in shape_ids.items():
            selected = shape == shape_id
            counts[name] += int(selected.sum())
            for offset, turns_xyz in zip(data["modelOffset"][selected],
                                         data["quarterTurns"][selected]):
                offsets[name][tuple(int(x) for x in offset)] += 1
                turns[name].add(tuple(int(x) for x in turns_xyz))
    output[label] = {
        "shapeCellCounts": dict(counts),
        "shapeOffsetCounts": {name: {str(k): v for k, v in counter.items()}
                              for name, counter in offsets.items()},
        "distinctQuarterTurns": {name: len(values) for name, values in turns.items()},
        "block7CellCountDivisibleBy2": counts["Block7"] % 2 == 0,
        "block8CellCountDivisibleBy8": counts["Block8"] % 8 == 0,
    }
    print(label, "cells", sum(counts.values()), "shapes", dict(counts))
    print("  Block7 offsets", offsets["Block7"], "Block8 offsets", offsets["Block8"])
output_path = Path("Documentation/ReferenceMap-2026-11-01/source-qa-template-usage.json")
output_path.write_text(json.dumps(output, indent=2), encoding="utf-8")
print("WROTE", output_path)
