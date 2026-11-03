"""Write a visually reviewed, exhaustive station shell-family index."""

import json
from pathlib import Path


groups = [
    ("left_angled_truss", [[0, 0]], "truss", "high"),
    ("left_rounded_mass", [[1, 1]], "rounded mass", "high"),
    ("left_upper_equipment", [[2, 18]], "upper platform/equipment", "high"),
    ("left_frame_and_tanks", [[19, 24]], "upright frame, two tanks, pipe", "high"),
    ("left_box_grid", [[25, 118]], "rack of small box supports", "high"),
    ("left_spherical_tanks", [[119, 126]], "three spheres and short fittings", "high"),
    ("central_bridge_truss", [[127, 127]], "truss", "high"),
    ("right_rounded_mass", [[128, 128]], "rounded mass", "high"),
    ("right_mass_fittings", [[129, 133]], "spheres, forward block and booms", "high"),
    ("right_truss_extension", [[134, 134]], "truss", "high"),
    ("right_pipe_fixtures", [[135, 199]], "pipes/tanks with remote fittings", "medium"),
    ("left_radiator_bank", [[200, 213], [243, 270]], "radiator lattice bank", "medium"),
    ("right_radiator_bank", [[214, 242], [271, 284]], "radiator lattice bank", "medium"),
    ("central_cantilever_beams", [[285, 339]], "long beams plus fittings", "medium"),
    ("outer_equipment_craft_a", [[340, 397]], "outer craft and sparse fittings", "low"),
    ("outer_equipment_craft_b", [[398, 449]], "beam and outer craft", "low"),
    ("crossing_booms_and_craft", [[450, 496]], "crossing booms, deck fittings, craft", "low"),
    ("open_hex_deck", [[497, 569], [593, 600]], "hex tiles with gaps", "high"),
    ("five_parallel_tanks", [[570, 574]], "five long parallel cylinders", "high"),
    ("hex_deck_understructure", [[575, 592]], "underside machinery and long support", "high"),
    ("tiny_right_fitting", [[601, 601]], "isolated small fitting", "high"),
]
records = []
all_ids = []
for name, ranges, role, confidence in groups:
    ids = [i for first, last in ranges for i in range(first, last + 1)]
    all_ids.extend(ids)
    records.append({"name": name, "sourceObject": "Cylinder.018", "sourceShellIds": ids,
                    "role": role, "visualConfidence": confidence,
                    "physicalRule": "A visual family only. Voxelize exact source geometry, union only real contact, and split every six-disconnected result into an independent asset. Do not add bridges."})
assert sorted(all_ids) == list(range(602)), "Every station shell must appear once"
output = Path("Documentation/ReferenceMap-2026-11-01/source-station-semantic-groups.json")
output.write_text(json.dumps({"sourceObject": "Cylinder.018", "shellCount": 602,
                              "groups": records}, indent=2), encoding="utf-8")
print("WROTE", output, "groups", len(records), "covered shells", len(all_ids))
