# 本轮暂停交接 — 2026-10-24

## 最新状态（2026-10-24 续接后）

用户已要求继续，但随后明确限定：**只做地图编辑，停止其余部分测试，保留已经获得的诊断。** 后续不要再启动 AI、飞控或全游戏回归，除非用户重新明确授权。

- 地图共用模型/材质、四种基础体块、独立基础色/颜色槽和接缝修复均保留；地图工作室已重新打开到 Asteroid 资产，未启动新的地图或战斗试玩。
- 停止了续接时的 AI 单项测试；之前的失败记录保留。测试代码保留发射次数、射击采样、遮挡数、伤害、距离、速度和阶段的诊断输出，原有判定与 AI/飞控参数没有改变。
- 新外观真实地图 Play/Stop 和性能检查仍未额外执行，按用户要求不继续扩展测试。已有 EditMode 250/250、实际四形状按钮/保存/改色与接缝图像验证结果仍有效。
- 以下内容为上一轮交接历史；其中关于继续排查 AI 的建议已被本段最新用户范围限制取代。

用户要求提前结束本轮，保留当前进度供下一轮继续。没有启动后续测试或实际地图 Play 检查；最后的单项测试已完成，没有测试留在运行。

## 当前需求与已完成内容

1. 地图与飞船共用模型、塑料材质；允许单独更改地图基础色。
2. 地图编辑支持正方体、斜面体、半高块、四棱锥，保存形状与旋转。
3. 后续反馈：缝隙内渲染像法线反转。已定位并修正误剔面：源模型视觉边长约 0.98 格，真实缝隙存在，不能用完整 1 格碰撞占用推断视觉完全遮挡。原模型三角形和法线方向经检查均正常。现在按实际视觉覆盖条件保留封闭面。

以上代码已实现并编译。现有 5 份源资产已通过 Unity API 绑定 `Assets/_Starframe/Settings/Modules/ModuleDatabase.asset`；体块坐标、旧颜色槽编号和地图布局保留，旧配置字节备份位于本目录 `Before/`。没有修改船体模型、原材质或 AI/飞控参数。

## 主要改动

- `Assets/_Starframe/Scripts/Runtime/Construction/Maps/VoxelHullAppearance.cs`：复用四种原模型和原材质；颜色乘色副本不改原材质；保留原始法线/UV/切线；严格的视觉剔面条件。
- `VoxelAssetDefinition.cs`：`hullModules`、`baseColor`、`surfaceColors`；`VoxelCell.shape`、`quarterTurns`；默认旧数据保持 Cube/0 旋转。
- `VoxelStructureRuntime.cs` / `VoxelTemplateCache.cs` / `VoxelGeometry.cs`：材质槽/模型合并、旋转、碎块数据保留。碰撞/伤害/连通仍是完整 1 格，整资产及断裂岛各一个刚体，不创建逐块 GameObject。
- `Assets/_Starframe/Scripts/Editor/Maps/`：四种笔刷按钮、R 旋转、基础色与颜色槽、实际塑料材质预览；新建资产默认绑定船体库。
- `Assets/_Starframe/Tests/Construction/VoxelHullAppearanceTests.cs`：11 项形状/法线/材质隔离/旋转/断裂/接缝回归。

入口：`Starframe → 开发工具 → 体块地图工作室`。右侧“笔刷”选形状；“船体外观 → 基础色”改整份资产颜色；白色保留原船体材质。颜色槽控制局部配色。测试时暂时关闭了地图工作室窗口，可以从菜单重新打开。

## 验证结果与未完成事项

- 最终 **EditMode 250/250 通过**，含新接缝测试。结果：`EditModeTests.json`。
- 实际 UI 选取四种体块、旋转、放置、保存载入、改基础色通过，且船体材质文件逐字节保持不变：`EditorSmoke.json`。
- 实际预览：`FourShapes.png`、`FourShapesTinted.png`、`SeamsFixed.png`、`MapSharedHull.png`。`SeamsFixed.png` 已目视确认缝隙不再露出误删面的空腔。
- 完整 PlayMode 为 **28/29**。失败项：`RangePracticeModeTests.CombatPilotLeadsAndDamagesAMovingPlayerUsingActualProjectiles`，其提前量与移动断言通过，8 秒内对玩家造成实际伤害的断言失败。`PlayModeTests.json`。
- 关闭地图预览后单独复跑此项仍失败：`IsolatedAiTest.json`。**原因未确定，不能声称是已证实的历史问题，也不能声称全部 PlayMode 通过。没有调整或放宽测试、AI 参数和游戏参数。** 下一轮若继续验证，应先判断是否与本轮有关，避免无授权扩展为 AI 平衡任务。
- 尚未对新外观下的真实 21 实例地图执行额外 Play/Stop 或性能检查；上一轮旧外观的这类测试通过，不能当成本轮新外观证据。真实有缝模型为避免漏面会保留更多几何，仍共享整资产网格与复合碰撞。

## 记录与恢复注意

- 已追加 `DEVLOG.md` Interaction 003，并更新 `STATUS.md` 和使用说明；已在 Unity 运行 `TraceDocsExporter.ExportForAutomation()`，同步 `docs/` 与 `site/data/`。没有 Stage Reflection、Git 提交或发布。
- 当前常用 CLI：`C:/Users/34808/AppData/Local/Unity/bin/unity.exe`，`command ... --caller plugin --skill unity-cli`。端口会在 7800/7801 切换，按当前工程自动定位即可；PowerShell 执行目前需要工具的 `require_escalated`。
- 批量资产绑定的 eval 曾因工具 5 秒主线程限制报超时，但所有写入及备份已完成并核实；不要重复迁移/覆盖备份。后续较长检查使用 `run_script --timeout_ms 75000`。
- `Temp/HullStudioSmoke.cs` 为临时 UI 验证脚本，已完成并清理自己的 `HullStudioQA_*` 临时资源；没有后台回调继续工作。不要把验证脚本当成游戏入口。
- 继续时先读取本交接和项目 AGENTS/STATUS；尊重用户暂停，等待下一轮明确继续。
