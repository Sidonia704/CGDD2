# generation prune and component verification

> **历史记录（14 件旧组件）**：下列运行时格数和耗时来自第三版场景重组之前，不能作为当前 20 件组件的验证结果。当前核查见 [MapAudit-2026-11-01](<Documentation/MapAudit-2026-11-01/README.md>)；本文件保留原始数据供追溯。

absent: Assets/_Starframe/Maps/MiningStationRound2/Generated_v01
absent: Assets/_Starframe/Maps/MiningStationRound2/Generated_v02
absent: Assets/_Starframe/Maps/MiningStationRound2/Generated_v03

## runtime load check (14 components)
| component | cells | build ms | alive | colliders |
|---|---:|---:|---:|---:|
| Debris_Chunk | 706 | 75 | 706 | 257 |
| Debris_Shard | 136 | 7 | 136 | 64 |
| Rock_Large | 10910 | 1521 | 10910 | 2150 |
| Rock_Medium | 3535 | 786 | 3535 | 626 |
| Rock_Small | 623 | 72 | 623 | 112 |
| Station_CapsuleTank | 5056 | 605 | 5056 | 28 |
| Station_CylinderTank | 6240 | 790 | 6240 | 33 |
| Station_DomeLarge | 20220 | 3359 | 20220 | 3411 |
| Station_DomeSmall | 8914 | 1210 | 8914 | 1351 |
| Station_HexDeck | 6216 | 1173 | 6216 | 146 |
| Station_MachineryCage | 864 | 152 | 864 | 82 |
| Station_SlabDeck | 8585 | 1407 | 8585 | 241 |
| Station_TrussGirder | 648 | 101 | 648 | 304 |
| Station_TrussRiser | 648 | 71 | 648 | 264 |

all components loaded with their full cell set.
