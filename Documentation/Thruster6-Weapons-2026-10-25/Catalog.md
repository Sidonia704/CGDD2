# Starframe component catalog — 2026-10-25 follow-up

Generated from `Settings/Modules/ModuleDatabase.asset` after installing Thruster6 and applying the follow-up weapon tuning. Cells are integer occupancy in local X×Y×Z; thrust is per nozzle. `—` means the property does not apply.

|Component|Category|Cells|Mass kg|HP|Recovery s|Thrust N/nozzle|Damage × rate|Speed m/s|Tracer|Visual length/tail/width m|Power W|
|---|---|---|---:|---:|---:|---:|---|---:|---|---:|---:|
|Cube|Hull|1×1×1|300|60|6|—|—|—|—|—|0|
|Wedge|Hull|1×1×1|150|30|6|—|—|—|—|—|0|
|Thruster1|Propulsion|1×1×1|600|100|8|320000|—|—|—|—|0|
|Thruster2|Propulsion|1×2×1|1400|200|8|800000|—|—|—|—|0|
|Slab|Hull|1×1×1|150|30|6|—|—|—|—|—|0|
|Pyramid|Hull|1×1×1|100|20|6|—|—|—|—|—|0|
|Core|Core|1×1×1|2000|50|8|—|—|—|—|—|0|
|Laser|Weapon|1×1×3|600|120|8|—|25 × 10|1400|voxel|18 / 10 / 0.75|50|
|Power|Power|1×1×1|600|100|6|—|—|—|—|—|60|
|Shield|Shield|1×1×1|600|100|12|—|—|—|—|—|100|
|Block5|Hull|1×1×1|230|46|6|—|—|—|—|—|0|
|Block6|Hull|1×1×1|180|36|6|—|—|—|—|—|0|
|Block7|Hull|1×1×2|300|60|6|—|—|—|—|—|0|
|Block8|Hull|2×2×2|2400|480|10|—|—|—|—|—|0|
|Thruster3|Propulsion|1×2×2|2500|360|10|1480000|—|—|—|—|0|
|Thruster4|Propulsion|1×2×4|4800|650|12|2880000|—|—|—|—|0|
|Thruster5|Propulsion|2×3×2|7600|950|14|4400000|—|—|—|—|0|
|Power2|Power|2×2×2|4800|650|12|—|—|—|—|—|560|
|Shield2|Shield|2×2×2|4200|600|16|—|—|—|—|—|520|
|Armor1|Shield|1×1×1|420|180|7|—|—|—|—|—|0|
|Armor2|Shield|1×1×2|800|340|9|—|—|—|—|—|0|
|Armor3|Shield|1×1×3|1180|480|11|—|—|—|—|—|0|
|Armor4|Shield|2×1×3|2280|900|14|—|—|—|—|—|0|
|Gun1|Weapon|1×1×3|650|110|8|—|15 × 16|1800|voxel|4 / 100 / 2.2|20|
|Gun2|Weapon|2×1×4|1550|240|10|—|20 × 20|2000|voxel|5 / 100 / 2.4|35|
|Gun3|Weapon|2×2×4|2900|420|13|—|28 × 24|2200|voxel|6 / 100 / 2.6|60|
|Laser2|Weapon|2×1×4|1700|230|10|—|40 × 10|1500|voxel|20 / 12 / 0.85|100|
|Laser3|Weapon|2×2×4|3200|400|13|—|65 × 10|1600|voxel|24 / 15 / 1.05|180|
|Railgun1|Weapon|1×1×4|1000|140|10|—|340 × 3|10000|continuous|70 / 200 / 8|65|
|Railgun2|Weapon|2×1×5|2100|280|12|—|650 × 2.5|12000|continuous|100 / 300 / 9|120|
|Railgun3|Weapon|2×2×6|3900|470|16|—|1150 × 2|14000|continuous|140 / 420 / 10|200|
|Missile1|Weapon|1×1×3|850|120|9|—|400 × 0.25|240|voxel|1.5 / 7 / 0.32|90|
|Missile2|Weapon|2×1×3|1600|220|11|—|700 × 0.182|300|voxel|2.5 / 10 / 0.42|150|
|Missile3|Weapon|1×1×5|1800|240|13|—|1100 × 0.143|360|voxel|3.5 / 13 / 0.52|230|
|Sword1|Weapon|1×1×3|700|130|8|—|45 × 12|1400|voxel|40 / 0 / 0.45|125|
|Sword2|Weapon|2×1×4|1550|260|11|—|80 × 12|1400|voxel|40 / 0 / 0.7|220|
|Thruster6|Propulsion|1×1×1|400|80|8|45000|—|—|—|—|0|

## Thruster6 / 五向微调推进器

- Shape id 36, single body cell, category Propulsion, 400 kg, 80 HP, 8 s recovery.
- 45 kN per nozzle (Thruster1 remains 320 kN), five nozzles: +Y, +X, −X, +Z, −Z. Only local −Y mounts to the hull.
- Each nozzle keeps one adjacent cell for runtime exhaust obstruction and nozzle-mask evaluation. Placement no longer hard-reserves that cell: the green preview is half a cell (0.5 m), a flush block is allowed, and the mounting cell below is still not reserved.
- The rendered plume is one cell (1 m) long and 0.3 m wide. Runtime nozzle blocking still checks the complete adjacent cell, so a flush block closes only that nozzle; the other four keep firing.
- Nozzle mouths (local space, inset 12 mm so the measured mouth stays inside the frame):
  - (0.00, 1.00, 0.00) → (0.00, 0.47, 0.00)
  - (1.00, 0.00, 0.00) → (0.46, 0.15, 0.00)
  - (-1.00, 0.00, 0.00) → (-0.46, 0.15, 0.00)
  - (0.00, 0.00, 1.00) → (0.00, 0.15, 0.46)
  - (0.00, 0.00, -1.00) → (0.00, 0.15, -0.46)
- The five exhausts are independent: blocking one removes only its axis; the other four still fire and the shared manoeuvre rating is counted once.

## Follow-up weapon tuning

- Rapid lasers: Laser2/Laser3 10 shots/s (from 12); base Laser stays 10/s.
- Kinetic guns: Gun1/2/3 16/20/24 shots/s at 1800/2000/2200 m/s, with compact tracer heads (4/5/6 m long, 2.2/2.4/2.6 wide) and long thin tails.
- Railguns: 3/2.5/2 shots/s (first press fires immediately, then cools down), 10/12/14 km/s, continuous gold beam with a white core, 200/300/420 m tail, 0.09 s linger, visual width 8/9/10 and minimum 4.5/5/5.5 screen pixels. Physical diameter, damage and penetration are unchanged.
- Gun and railgun impact flashes are enlarged on both sides of the damage volume; both now have a white core flash plus more/longer particles.

The original ten module definitions, supplied FBX/PNG sources and user blueprints/maps were not rewritten. Thruster6 source FBX/PNG hashes are recorded in `Before/SourceAssetHashes.sha256`.
