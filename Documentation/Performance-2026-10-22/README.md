# 飞行战斗性能优化（2026-10-22）

本轮针对大型飞船在飞行、开火时的开销；没有调整装配放置规则、蓝图、武器数值、AI 难度或残骸存活规则。

## 实现

- `ShipInstanceRenderer`：64 部件起，为重复的 URP 不透明船体网格按网格/材质/子网格分组，通过 GPU 实例批次绘制。每批最多 256 个实例。原来的每块碰撞体和状态对象保留；损坏/断连的部件下一帧从实例中排除，重生或修复后重新出现。自定义能量材质及动态特效维持独立逻辑。
- `VoxelGlow.shader`：激光和推进器的颜色、亮度、流动相位等支持 GPU 实例属性，让现有共享网格真正参与自动实例化。外观参数和流动算法不变。Resources/Combat 下的材质保留独立构建需要的着色器变体。
- `ShipCollisionSweep`：用完整船体碰撞范围加移动距离执行范围查询并复用结果缓冲，排除自身碰撞体；附近有外部物体时仍执行原来的完整刚体扫掠。密集场景按需扩容缓冲。范围涵盖整个蓝图，部件恢复不需要重算范围。
- `ProjectileWorld`：每个物理步只采样一次各个护盾的坐标与半径；同一步内仍逐发检查实时护盾值、组件状态，保留护盾耗尽后的穿透伤害。
- `ShipStatusGraphic`：自身状态仅在血量或结构发生变化时重建；超过 256 部件的敌方示意图以 15 Hz 更新，其余保持 30 Hz。自身/敌方损伤提示、核心位置和敌方翻滚仍保留。

Prefab 本身用于保存对象配置，不会消除逐部件渲染提交。项目已经有飞船装配 Prefab，因此本轮采用共享网格实例化，避免批量转换资产及重复网格内存。

## 验证与测量

使用真实“战舰”蓝图（742 部件）和现有 5 架靶机（各 73 部件），总共 1107 部件，持续飞行及开火。对比通过临时脚本在同一 Editor 会话重新加载相同场景，并切换运行时优化开关；不保存玩家蓝图或设置。最终数值见 `Combat-Comparison.json`。Editor 采样不能等同于独立发行版帧率。

最终场内平均约 792 发弹丸，双方各生成 2783 发，总存活部件均为 1107。开启优化后：

| 指标 | 关闭优化 | 开启优化 |
| --- | ---: | ---: |
| 主线程平均耗时 | 8.90 ms | 5.85 ms |
| 帧间隔 P95 | 14.51 ms | 8.60 ms |
| 弹道计算平均耗时 | 2.78 ms | 0.53 ms |
| 平均绘制批次 | 2783 | 325 |

CPU/GPU：i7-13700K / RTX 5080，Game View 1280×720，每档预热 3 秒、采样 8 秒。主线程约下降 34%，较慢帧 P95 约下降 41%。该对比两档均保留 HUD 的按需刷新，未把它的收益计入上述差值。

- `EditModeResults.json`：编辑器回归结果。
- 当前完整编辑器回归 221 项通过；新增内容覆盖碰撞范围、密集场景、护盾移动采样与同一步多发耗尽护盾。
- `PlayModeResults.json`：渲染优化后完整运行测试结果。
- `FinalRenderingPlayMode.json`：追加移动碰撞筛查后的大型飞船显示、击毁、残骸与重生复核。
- `FinalFlightPlayMode.json`、`FinalCombatPlayMode.json`：飞控稳定性、开火/死亡/残骸/重生流程复核。
- `BuildResult.json`：Windows 开发构建成功，0 错误、486 条构建警告；验证包位于 `Temp/PerformancePlayer/`，未覆盖原有 Builds 输出。
- `PlayerQA/Report.json`：独立构建实际运行的 53 项检查通过，游戏错误计数为 0。原有依赖包构建警告未在本轮处理。
- `Before/`：修改前源码和材质备份，供按功能回退。

## 分别停用 / 回退

1. **船体实例化**：运行时关闭船根对象上的 `ShipInstanceRenderer` 组件，会恢复原有 MeshRenderer。永久停用可将其 `MinimumPartCount` 设为 `int.MaxValue`，不影响蓝图数据。
2. **移动碰撞筛查**：关闭 `ShipFlightController.UseCollisionBroadPhase`，恢复每次移动都执行完整扫掠的路径。
3. **特效实例化**：还原 `Before/Shaders/VoxelGlow.shader`，不必回退武器或推进器数值。
4. **HUD 缓存**：还原 `Before/Flight/ShipStatusGraphic.cs`，运行时的 HealthRevision 保留也不会改变战斗逻辑。
5. **护盾坐标采样**：将 `ProjectileWorld.UseShieldGeometryCache` 设为 false 即可比较原来的逐弹坐标查询路径。

不要将整个 Before 目录直接复制进 Assets；只还原相应原文件。材质备份只用于还原“GPU Instancing”选项，本轮未修改这些材质的颜色、高光或纹理。
