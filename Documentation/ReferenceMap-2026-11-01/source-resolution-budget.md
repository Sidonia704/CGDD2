# Conditional 131,072-cell resolution budget

This is a source-derived sizing proposal, pending the student's choice between a strict 1 m physical grid and fixed coarser grids for large bodies. The 3500 m map extent is independent of cell size. For a consistently wound, closed shell of volume `V`, any 1-cell-per-occupied-cube solid representation needs at least `V / c³` cells at cell edge `c`. The table also shows `V/c³ + A/c²` (`A` = triangle surface area) as a rough boundary-cost planning proxy, **not an upper bound**. Actual voxelization and connectivity remain mandatory.

| Source body | `V` m³ | `A` m² | Mathematical minimum `c` for 131,072 cells | Candidate fixed `c` | Planning proxy at candidate `c` |
| --- | ---: | ---: | ---: | ---: | ---: |
| Left rounded station mass, shell 1 | 10,390,266 | 271,781 | 4.296 m | 5 m | ~94,000 cells |
| Right rounded mass, shell 128 | 3,430,141 | 128,476 | 2.969 m | 4 m | ~61,600 cells |
| Long forward block, shell 131 | 682,990 | 52,860 | 1.734 m | 2 m | ~98,600 cells |
| Five parallel tanks, shells 570–574 | 115,695 each | 16,309 each | 0.959 m | 2 m each | ~18,500 each |
| Largest placed field rock | 134,795 | varies | 1.009 m | 2 m | ~17,000 plus surface cost |
| Hex deck sample | — | — | measured | 1 m | largest connected asset 92,762 cells |
| Bridge truss sample | — | — | measured | 1 m | 38,140 cells |

The left mass alone cannot fit a strict 1 m solid grid within 131,072 cells, or even the proposed 1,048,576-cell opt-in cap. A 4 m grid also has a volume-only lower bound of 162,348 cells, so 5 m is the first integer-metre candidate with room for boundary cells. The right mass at 3 m has a volume-only lower bound of 127,042 cells and almost no boundary margin; 4 m is the practical first candidate. The five tanks at 1 m have a 115,695-cell volume floor and roughly 16,309 m² of boundary, so 1 m may exceed the cap and should be measured before acceptance.

Under a mixed fixed-grid choice, each **physical asset** keeps one immutable cell size. Slim trusses, gaps, plates and other fine parts stay at 1 m; large rounded masses use their stated fixed coarser grid. Where a 1 m structure attaches to a coarse mass and they must behave as one rigid body, the runtime needs explicit multi-resolution same-body support or a documented structural connection. Merely storing them as touching separate rigid bodies would change initial fracture behaviour. A strict 1 m choice instead requires a same-body segmented data format and rendering/physics budget for more than 10 million cells in the left mass alone.

The 1,279 closed placed field rocks together enclose about 6.86 million m³. Even if every field rock fits its own asset cap, a strict 1 m scene needs at least 6.86 million occupied rock cells before the 3,065 open-card substitutes, station, and debris. Use `source-volume-budget.json` for per-shell volume and surface area.
