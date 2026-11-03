# 战舰框架编辑器入口

Unity 菜单 `Starframe/战舰框架` 提供：

- **打开组件 / 打开飞控**：选中原生数据库或飞控资产，在 Inspector 调整。
- **导入参考船（保留同名存档）**：读取本目录 `Blueprints/Battleship-*.json`，另存到玩家蓝图库；同名蓝图保留，当前工作区不切换、不重写。
- **恢复本轮调整前参数**：按 hull / flight / weapon / power / shield / mission 读取本轮 `before.json` 的 `assets[].fields[{field,type,value}]`；仅恢复明确列出的数值、布尔及激活枚举字段，可 Undo。美术、网格、引用、占格、数组和玩家蓝图均不进入恢复范围。

分组沿用 `tools/Battleship20261102/Snapshot.cs` 的分类：装甲和核心属于 hull，推进器模块属于 flight，武器本体与武器定义属于 weapon。恢复会预先验证快照完整性、资产 GUID、字段类型及标量值，失败时不开始写入。

本轮原生辅助入口：`BattleshipSnapshot.CaptureBefore()` / `ExportAfter()` / `Restore(category)`。它们位于 tools 目录，供 native helper 加载；旧 Balance 轮次的 helper 和菜单保持独立。`BattleshipLocalization.Apply()` 仅 upsert 本轮13个 CSV key 的中英原生表条目，不重建字体。
