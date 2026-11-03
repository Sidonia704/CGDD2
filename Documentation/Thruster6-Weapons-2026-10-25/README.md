# Thruster6 + 武器手感收尾 · 2026-10-25

接手 Codex 未完成的最后一个任务。本目录是完成后的权威记录；历史报告只作参考。

## 交付结果

- 新组件 **Thruster6 / 五向微调推进器**（shape 36）已进入 `ModuleDatabase`，并在装配面板 Propulsion 分区显示为 `Drive VI`。
- 单格本体；五个喷口各 45 kN；只在本体底面（局部 −Y）连接。**尾焰可视长度为一格（1 m）**；放置时喷口采用**半格判定**：绿色预览为 0.5 m，相邻格不再硬性禁止，方块可以贴近放置。运行时的喷口遮挡仍按完整相邻格检查——放下方块会关闭该方向喷口，其余四向照常。
- 激光速射炮 2/3 从 12 发/秒降到 **10 发/秒**；基础 Laser 保持 10 发/秒。
- 机枪 1/2/3 射速 **16/20/24 发/秒**，弹速 **1800/2000/2200 m/s**（原 12/15/18、1250/1350/1450）。
- 电磁炮 1/2/3 弹速 **10/12/14 km/s**，改为连续金色光束 + 白色核心，拖尾总长 **270/400/560 m**（visualLength 70/100/140 + tail 200/300/420），命中后残留 **0.09 s**；视觉宽度 8/9/10，最小屏幕像素 4.5/5/5.5。真实弹径、伤害和穿透未改。
- 电磁炮的可见光束从枪口出发、朝弹丸前方延伸，尾迹不会伸到枪口后方；高弹速下形成一条快速扫过的直线脉冲，而不是小点拖尾。

## 关键数值

|组件|射速 (发/秒)|弹速 (m/s)|视觉|
|---|---:|---:|---|
|Laser2|10|1500|20 / 12 / 0.85|
|Laser3|10|1600|24 / 15 / 1.05|
|Gun1|16|1800|4 / 100 / 2.2|
|Gun2|20|2000|5 / 100 / 2.4|
|Gun3|24|2200|6 / 100 / 2.6|
|Railgun1|3|10000|continuous 70 / 200 / 8|
|Railgun2|2.5|12000|continuous 100 / 300 / 9|
|Railgun3|2|14000|continuous 140 / 420 / 10|

Thruster6：400 kg、80 HP、8 s 恢复、45 kN/喷口。喷口局部方向为 +Y、+X、−X、+Z、−Z；实测喷口位置（向内收 12 mm，渲染时再向外偏 15 mm）：

- +Y `(-0.0006, 0.4680, 0.0033)`
- +X `(0.4570, 0.1488, 0.0047)`
- −X `(-0.4570, 0.1488, 0.0039)`
- +Z `(-0.0006, 0.1488, 0.4575)`
- −Z `(-0.0006, 0.1509, -0.4575)`

五个喷口独立判定：只堵住一个时，该方向失去推力，其余四个照常工作；共享转向辅助只按一个喷口额定值计入，避免五向喷口变成五个推进器叠加。`ThrusterExhaust` 对该模块的尾焰长度限制为 `1 m`（一格），宽度仍限制在 `0.3 m`；`BlockModules.PlacementCells` 不再把喷口格算作放置占用，绿色预览按 `PlacementClearance=.5` 缩成半格。运行时遮挡判定仍使用完整相邻格。

## 证据

- [ComponentPanel.png](ComponentPanel.png)：真实 ShipEditor Play Mode 面板截图；Propulsion 计数 06，`Drive VI` 槽位可见。
- [CombatVisuals.png](CombatVisuals.png)：真实渲染池的武器静态预览；第三行是 Railgun3 的连续金色脉冲（白核 + 长尾）。
- [CatalogVisuals.png](CatalogVisuals.png)：37 个组件总览（36 为 Thruster6）。
- [Thruster6-SixFaces.png](Thruster6-SixFaces.png)：五个喷口面 + 底面，标注喷嘴坐标；与 `SourceFaces.png` 对照。
- [Catalog.md](Catalog.md)：从数据库导出的完整组件表和本次数值。
- [Thruster6-Module.json](Thruster6-Module.json)：安装后的序列化配置。
- [EditMode-Full.xml](EditMode-Full.xml)：EditMode **308/308 通过**（含电源顶面、宽武器双底、电磁炮首发和多炮口可视体测试）。
- [EditMode-Mounting.xml](EditMode-Mounting.xml)：安装/电源/宽武器专项全量回归 **305/305 通过**。
- [RailgunFire.xml](RailgunFire.xml)：三档生产电磁炮首发/连射专项 **3/3 通过**。
- [WeaponVisuals.xml](WeaponVisuals.xml)：Gun1/2/3 多炮口生成点与可视体专项 **3/3 通过**。
- [PlayMode-AssemblyPreview.xml](PlayMode-AssemblyPreview.xml)：面板 PlayMode **1/1 通过**（包含 `Thruster6Slot` 存在断言）。
- [PlayMode-AssemblyPreview-Retry.xml](PlayMode-AssemblyPreview-Retry.xml)：面板 PlayMode 复跑 **1/1 通过**，用于确认批处理退出崩溃可复现。
- [PlayMode-AssemblyPreview-HalfCell.xml](PlayMode-AssemblyPreview-HalfCell.xml)：面板 PlayMode **1/1 通过**，额外断言 Thruster6 的绿色净空预览缩为 **0.5 格**。
- [PlayMode-RangeStationary.xml](PlayMode-RangeStationary.xml)：靶场固定射击 PlayMode **1/1 通过**。
- [Before/SourceAssetHashes.sha256](Before/SourceAssetHashes.sha256)：`assetsForCodex` 中的 Thruster6 源 FBX/PNG 哈希；源文件未被改写。

## 验证说明

- EditMode 全量回归 308/308，0 失败。
- 面板 PlayMode 用例三次都通过了断言（包括半格预览），但批处理进程在写出结果后退出时都以 `0xC0000005` 结束；三份 XML 都明确记录 1/1 通过。单跑靶场 PlayMode 用例可正常退出，未出现测试失败。该退出码发生在结果写出之后，不是断言失败；以后跑 PlayMode 批处理时请以 XML 结果为准。
- 历史 AI 移动目标伤害测试未重跑、未调整 AI 或飞控；按用户此前要求保留原状。
- 未重跑或修改正式关卡地图、波次、自动胜负。

## 2026-10-25 后续修复：电源顶面 / 宽武器安装 / 电磁炮触发

- **大电源顶部**：`Power` 和 `Power2` 现在按完整常规方块判定，六个方向都有完整接触面；装饰网格的凹陷/中空不再让顶面无法承载方块。实现位置在 `BlockModules.Patches` 的 `FullFace` 分支。
- **宽 2 格武器/装甲**：所有武器与装甲的底部按宽度列检查支撑；宽 2 格需要左右两列都有接触面。装甲只检查其真实脚面所在的一排（`MountingCell`），避免把前部斜空面当成支撑。1 格宽武器/装甲保持单点支撑；推进器、电源、护盾等其他功能性方块不受这条额外限制。实现：`BlockConnectivity.HasCompleteMount`、`ShipAssembly.CanPlace`。
- **电磁炮触发**：三档生产电磁炮的 `firstShotImmediate` 已启用；按下后的第一发立即发射，随后严格按各自射速冷却。宽 2 格电磁炮现在可正确落在两个支撑上；枪口净空/遮挡规则仍然生效。实现：`WeaponDefinition.firstShotImmediate`、`WeaponActivationState`、`ComponentLibraryInstaller.ApplyRailPulseVisual`。
- 电磁炮专项测试覆盖 `Railgun1/2/3` 的“首发立即 + 射速冷却 + 弹丸生成”，不改变物理弹径、伤害或穿透。

## 2026-10-25 连射观感收尾

- **电磁炮连射**：Railgun1/2/3 射速改为 **3 / 2.5 / 2 发/秒**（冷却 0.33/0.4/0.5 s），首发仍立即发射；伤害、穿透和弹速不变。
- **机枪弹丸缩小**：Gun1/2/3 可视头长 12/15/18 → **4/5/6 m**，可视宽度 5 → **2.2/2.4/2.6**，最小屏幕像素 2.8 → **2.2**；拖尾仍保留 100 m，但更细。
- **命中特效放大**：机枪和电磁炮的命中闪光、白核、碎片尺寸/数量/持续时间都放大约 2–3 倍；机枪也新增白色核心闪光。
- **连射显示排查**：新增 `WeaponVisualTests`，用真实 Gun1/2/3 资产、真实 `LaserVisualPool`、带世界偏移的船体验证 2/4/6 个炮口的生成点、左右两侧、可视体数量和拖尾头部位置；同时让 `OwnPathBlocked` 忽略相邻武器，避免密集多炮互相误判为炮口遮挡。

## 重建入口

- `Starframe/Components/Install five-way Thruster6`
- `Starframe/Components/Apply follow-up weapon tuning`
- `Starframe/Components/Render Thruster6 six faces`
- `Starframe/Components/Render component contact sheet`

`ComponentLibraryInstaller.Install()` 仍可重建 26 个扩展组件；`ApplyFollowUpWeaponTuning()` 只改射速/弹速/电磁炮视觉，不动枪口、材质和物理弹径。原十种组件配置、用户蓝图和地图未被覆盖。
