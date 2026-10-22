# 战斗与平衡入口

- `Settings/Modules/ModuleDatabase.asset` 登记全部部件定义。
- `Settings/Modules/Laser.asset` 配置激光炮模型、图标、三格占用、质量、血量及恢复时间。
- `Settings/Modules/LaserRapidCannon.asset` 配置射速、伤害、弹速、圆柱直径、穿深、第二层倍率、射程及体素弹道。`sustainedPowerWatts` 为持续开火每秒耗电，默认 50，与射速独立。
- `Settings/Modules/Power.asset` 配置电源：600 kg、500 容量、每秒恢复 60。多个现存电源相加；损毁和重建会重新计算。旧蓝图需要安装电源才能开火或加速。
- `Settings/Flight/BattleTuning.asset` 配置脱战等待、恢复频率、bot 运动/射击与双方重生时间。靶场已启用有限电量。
- 蓝图载入弹窗选中蓝图后可“设为靶机”，用 − / + 调整数量；不同蓝图可同时启用，配置保存在本机。默认 `test × 1`，进入靶场读取最新保存文件，缺失时使用 `Data/RangeTargets/test.json` 快照。删除蓝图同时取消其靶机配置。
- `Settings/Flight/FlightTuning.asset` 调整飞控、镜头和推进器体素尾焰。最高速度默认 250；Shift 耗电 100/s，加速度和角机动能力乘 1.5。转向根据总推力/质量计算，用 `angularAuthorityExponent` 调节响应差异，各上限为渐近软上限。原有部件推力沿用模块定义中的当前值。

玩家左键持续开火，按实际炮口方向发射；本船遮挡时停火，无自伤。多格组件只有一份 ID 和血量。伤害统计只计实际扣血。穿深从第一次实体接触起沿弹道累计世界距离，第一格 40、第二格 20；同一组件同一发只扣一次。

所有模块的碰撞、占用及受击判定均使用完整的标准单元格：核心一格、推进器一/两格、激光三格；缩小、倒角和喷口仅改变视觉。推进器另保留喷口净空。装配时双向检查新武器和已有武器的开火路径；净空直径 `muzzleClearanceDiameter`（当前 0.25）独立于弹丸命中直径 `projectileDiameter`。旧蓝图若新增的第三格与其他组件冲突，需要调整后使用。

微调瞄准：`FlightTuning.aimResponseExponent` 控制中心转向曲线，`aimInputSmoothing` 控制输入平滑秒数，`mouseDegreesPerPixel` 控制鼠标灵敏度。鼠标偏移持续保留，转向采用稳定指数响应并受飞船实际角加速度限制；镜头使用 `cameraRotationSharpness`，输入光标使用 `reticleSharpness/reticleMaxSpeed`。船头十字直接表示物理发射朝向，避免额外的准星显示延迟。

F 锁定前方可见敌机，再按 F 解除；锁定不改变飞控或自动瞄准。预瞄点以敌方核心为目标，使用多帧物理位置估计核心速度再解恒速截击方程。相同武器定义共用一个预瞄点，选朝向最接近船头且靠近组中心的真实可用炮口作为代表；其他炮仍从各自位置平行发射。不同武器定义按各自弹速和射程计算，并用颜色/弹速标签区分。无解、超射程、目标死亡时隐藏；敌机重生后需要重新锁定。`BattleTuning.lockConeDegrees/lockRange` 控制选择范围。

预瞄图标表示从代表炮口出发的截击方向，与船头十字准星共用 `AimProjection` 和物理姿态，避免第三人称镜头视差和插值时差。锁定框标记核心位置；对齐十字准星与预瞄图标开火。`BattleTuning.leadVelocitySampleWindow` 默认 0.16 秒，`leadVelocityResponse` 默认 0.04 秒；增加这些值可进一步平滑，减小可更快跟随机动。核心采样包含旋转造成的位置运动，切换目标、目标死亡或重生后丢弃旧样本。

普通掉血不减重、不降低功能。结构连通与碰撞统一使用完整占用格的面邻接；倒角、斜面和半高外形不改变连接，边与角接触不算连接。方块仅在自身血量归零时被摧毁；断连片区、核心毁坏后的所有存活部件均转为独立漂浮残骸，每片一个无重力刚体，保留完整格碰撞及剩余血量，可继续被打碎和再次分裂。全部存活部件完成转移并更新功能后才触发死亡事件及重生计时；重生另建完整船体，避开旧残骸，旧残骸不随之删除或回血。残骸不推进、不射击、不供电，不按时间、数量或越界清理，退出场景时统一卸载。`BattleTuning.debrisDriftSpeed/debrisSpinDegreesPerSecond` 调整运动。

残骸分离时暂时避让原船，避免包围核心的外壳被卡住；包围盒离开原船后恢复双方碰撞。对其他飞船的碰撞和所有弹道受击判定始终保留。

整船 1 秒不受有效船体伤害后，现存部件按最大血量/恢复时间回血；缺失部件仍从与核心相连的原船逐步重建，须有现存面支撑，读满自己的恢复时间才满血出现。新恢复部件下一次评估才提供支撑。再次受伤重置所有缺失读条，核心死亡停止恢复。

模块定义使用 `generationWatts/storageJoules` 管理电源，保留 `thrusterPowerWatts/shieldPowerWatts` 扩展通道。加速与武器共享电力并统一按比例分配；不足时减弱加速、降低射速，单发伤害保持。基础飞行与刹车不耗电。容量减少截断现有电量；部件重建增加容量不免费充电。

外观入口：`Art/Materials/Ships/AssemblyPlastic.mat` 及同目录各模块材质使用 0.865～0.92 的浅色灰阶，保留塑料清漆高光；`AssemblyCoreGlow.mat` 调整核心发光，PC_Renderer 的接触阴影强化拼缝。`BattleTuning.particleScale` 统一控制激光、喷流、受击与击毁特效大小，当前为 1.5，仅改变视觉。激光体素大小/长度在武器定义，流动尾焰的 `exhaustFlowSpeed`、长度、宽度和亮度在 FlightTuning；普通方块碎片与核心爆炸在 `CombatVfxPool.cs`。配装与靶场弹窗共用 `GlassUiPalette.cs`。新模型已导入 `Art/Models/Modules` 并保留 FBX 硬边法线，图标在 `Art/UI/Construction/Icons`；船体图标与 X 核心图标保持原版。以后替换源模型可执行菜单 `Starframe/Refresh supplied models only`，保留平衡参数。

新增部件：给 `BlockShape` 追加稳定编号，创建 `ModuleDefinition` 并登记数据库，设置类别、局部占格和资源；新增武器关联独立 `WeaponDefinition`。炮口局部 +Z、安装底面局部 -Y。图标和模型按数据库读取，避免在 UI 添加对应数组槽位。

性能标记：`Starframe.Connectivity/Performance/Recovery/Projectiles`。邻接图初始化缓存，结构变化才做 BFS；恢复按 10 Hz 评估；弹丸与视觉复用对象池，物理查询复用缓冲区。测试包含 1000 部件的保存载入、断连和恢复；这不等于大规模舰队的 FPS 验收。
