# Thruster6 asset configuration

Single-cell five-way maneuvering thruster. The supplied source model has an authored nonuniform root scale and root rotation; those transforms are preserved before one final uniform scale. The mounting plate was identified on the source +X face, then rotated -90 degrees about Z to become local -Y.

- Mass: 400 kg; durability: 80 HP; recovery: 8 s.
- Nominal force: 45,000 N per nozzle (Thruster1 is unchanged at 320,000 N).
- Exhaust directions: +Y, +X, -X, +Z, -Z. Only -Y mounts to the ship.
- Each exhaust reserves one adjacent cell; no extra clearance is reserved below the mounting plate.
- Source files and their importer settings are untouched. Output FBX, mesh and icons are separate assets.

- (0, 1, 0): nozzle (-0.00061, 0.46800, 0.00326)
- (1, 0, 0): nozzle (0.45701, 0.14877, 0.00468)
- (-1, 0, 0): nozzle (-0.45701, 0.14877, 0.00387)
- (0, 0, 1): nozzle (-0.00061, 0.14877, 0.45751)
- (0, 0, -1): nozzle (-0.00061, 0.15085, -0.45751)
