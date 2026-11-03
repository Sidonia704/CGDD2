# 地图破坏关闭与交接更新 — 2026-11-03

用户已取消第二轮校准。当前交接入口为 Documentation/PROJECT_CONTEXT.md，其次STATUS.md顶部；旧报告仅供按需查证。

- SpaceMapRuntime.allowDestruction默认false。游戏加载设置每个VoxelStructureRuntime.damageEnabled=false；ApplyDamage在写入HP或拓扑前返回0，PrewarmDamageVisuals跳过。
- 地图仍有原有质量、无重力漂浮、碰撞、代理／精细碰撞预算。船体部件和舰船残骸仍可破坏。
- 不可破坏地图拦截首表面弹丸；光束／导弹保留命中特效，三类均保留弱白地图命中反馈，不记录虚假伤害。爆炸不扫描不可破坏地图的格子。
- 开发工具单资产破坏预览保留；旧地图破坏测试明确开启allowDestruction以验证保留的后端。不要因此把生产地图重新打开。
- 本轮只编译和4个针对性EditMode用例（4/4），覆盖默认免伤、无稀疏记录／重建、保留刚体碰撞漂移，以及弹丸／光束／导弹的阻挡与白色反馈；没有跑全套／Play或做平衡校准。
- 可选冷场景预览核对超过30秒工具等待，首场景反序列化约39秒；操作随后完成finally关闭预览，Editor恢复ready/stopped；核对仅剩Unity SceneView自带CustomLightsScene，任务创建的预览均已清理。不是游戏编译错误，不据此宣称完成运行态或性能测试。
- 旧交接及改动前源码在before/。没有编辑地图资产／场景YAML、用户蓝图或工作船。
